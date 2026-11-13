import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { homedir } from 'node:os';
import { validateScenario, knownMemories, decisionMessages, validateDecision, recollectionMessages,
  validateRecollections, validateDailyPages, parseModel, previewDecision, previewRecollections,
  previewDailyPages, approvedDailyPagesFor, requestDailyPage, recapPagesFor, resolveOutcome, revisionOf } from '../../../Assets/Seabreeze/Dialogue/EndingDemo/ending.mjs';
import { TurnError } from './policy.mjs';

export const authoredFile = resolve(dirname(fileURLToPath(import.meta.url)), '../../../Assets/Seabreeze/Dialogue/EndingDemo/scenario.example.json');
// Exact authored revisions on either side of the built-in player rename.
// Custom uploads, including stories with the same title or player name, have different revisions.
const renamedPresets = [
  { old: 'cc8fbffac2020e4e', current: 'a526b25f52fc4f44', name: '旅人', file: authoredFile, originals: '2fe591641e7ff153625e1493468e0c87a8f74a858957ab1bccda3e49b38346b8' },
  { old: '83bdcc7cd93b3fab', current: '12f1f93212e78610', name: '听雨', file: resolve(dirname(authoredFile), '七日随机记忆-雨停以后-20261113.json'), originals: '02ec00798cd87aad71992eedd790079ec070d42cb1175f558ab30d7919dfa43d' },
];
function readPreset(raw, maxMemories) {
  const checked = validateScenario(raw, { maxMemories });
  const old = renamedPresets.find(p => p.name === raw.playerName && p.old === revisionOf({ ...checked, playerName: raw.playerName }));
  if (old) {
    const updated = validateScenario(JSON.parse(readFileSync(old.file, 'utf8')), { maxMemories });
    if (revisionOf(updated) === old.current) return updated;
  }
  return checked;
}
function savedRevisionBeforeRename(world, projected, authored, sourceRevision) {
  const [revision, ...version] = sourceRevision.split(':');
  const preset = renamedPresets.find(p => p.current === revision);
  if (!preset) return;
  const oldRevision = [preset.old, ...version].join(':');
  const visible = knownMemories(projected.scenario), authoredIds = new Set(authored.memories.map(m => m.id));
  const normalize = text => String(text ?? '').replaceAll(preset.name, '字小节').replaceAll('玩家', '字小节');
  const previous = Object.values(world.seventhNightEndings || {}).map(entry => entry.result)
    .filter(saved => saved?.worldId === world.worldId && saved.sourceRevision === oldRevision && saved.contextRevision &&
      saved.sourceName === (world.endingMemorySource?.fileName || authored.sourceName))
    .sort((a, b) => (b.createdAt || '').localeCompare(a.createdAt || ''));
  for (const saved of previous) {
    if (JSON.stringify(saved.sourceEventIds) !== JSON.stringify(projected.eventIds) ||
        !saved.zState || ['trust', 'affinity', 'emotion', 'world_suspicion'].some(k => saved.zState[k] !== world.actors.z.info[k])) continue;
    const memories = saved.recollections;
    if (!Array.isArray(memories) || memories.length !== visible.length || !memories.every((m, i) =>
        m.memoryId === visible[i].id && m.day === visible[i].day && normalize(m.title) === normalize(visible[i].title) && typeof m.original === 'string' &&
        (authoredIds.has(m.memoryId) || normalize(m.original) === normalize(visible[i].text)))) continue;
    const digest = createHash('sha256').update(memories.filter(m => authoredIds.has(m.memoryId))
      .map(m => m.day + '\n' + m.original).join('\n\u001e\n')).digest('hex');
    if (digest === preset.originals) return saved.contextRevision;
  }
}
export function gameEndingScenario(engine, catalog, world, authored) {
  const scenario = structuredClone(authored);
  // The shortcut is an authored seventh-night slice, enriched by this save's real Z-visible events.
  // It does not promote imagined task receipts into the running world.
  scenario.memories = knownMemories(scenario);
  const events = engine.records(world, 'event').filter(e => e.visible?.includes('z') &&
    (e.visible.includes('player') || e.disclose_to?.includes('player')) && typeof e.text === 'string' && e.text.trim());
  const groups = new Map();
  for (const event of events) {
    const day = Math.min(7, Math.max(1, 1 + Math.floor((event.hour || 0) / 24)));
    const key = `${day}:${event.conversationId || event.location || 'island'}`;
    if (!groups.has(key)) groups.set(key, { day, location: event.location, lines: [], sources: [] });
    const group = groups.get(key);
    const name = id => id === 'player' ? scenario.playerName : catalog.actors.has(id) ? catalog.actor(id).name : '游戏记录';
    group.lines.push(`${name(event.speaker)}${event.recipient ? '对' + name(event.recipient) : ''}：${event.text}${event.kind === 'fact' ? '（已确认发生的事）' : '（当时听到或看到的内容，未必已证实）'}`);
    group.sources.push(event.id);
  }
  let index = 0;
  for (const group of groups.values()) {
    const text = group.lines.join('\n');
    for (let offset = 0; offset < text.length; offset += 1700) {
      scenario.memories.push({ id: `game_${String(++index).padStart(3, '0')}`, day: group.day, kind: 'memory', zKnows: true,
        title: `本局第${group.day}天 · ${catalog.map.places[group.location]?.name || '与岛民的相遇'}${offset ? '（续）' : ''}`,
        text: text.slice(offset, offset + 1700) });
    }
  }
  scenario.context += `\n补充的回忆是你在岛上与${scenario.playerName}共同听到、见到，或已获得向${scenario.playerName}分享许可的内容；未核实的说法仍然只是当时听到的话。你不知道未听见的私聊。`;
  const z = world.actors.z.info;
  scenario.context += `\n你对${scenario.playerName}和眼前处境的初步看法：${JSON.stringify({affinity:z.affinity,trust:z.trust,emotion:z.emotion,world_suspicion:z.world_suspicion})}。这些仅供理解心情，不是去留门槛；台词不讲分数、字段名、记录ID或后台。`;
  const parsed = validateScenario(scenario, { maxMemories: 400 });
  return { scenario: parsed, liveMemoryCount: index, eventIds: events.map(e => e.id) };
}

export class GameEndingCoordinator {
  constructor(engine, catalog, provider, { scenarioPath = process.env.Z_ENDING_SCENARIO,
    selectionPath = resolve(homedir(), '.project-z/ending-demo/source-selection.json') } = {}) {
    this.engine = engine; this.catalog = catalog; this.provider = provider; this.scenarioPath = scenarioPath;
    this.selectionPath = selectionPath; this.inflight = new Map(); this.progress = new Map();
  }
  authored(world) {
    if (world?.endingMemorySource?.scenario) return readPreset(world.endingMemorySource.scenario, 200);
    let path = this.scenarioPath;
    if (!path && existsSync(this.selectionPath)) path = JSON.parse(readFileSync(this.selectionPath, 'utf8')).scenarioPath;
    path ||= authoredFile;
    if (!existsSync(path)) throw new TurnError('选中的第七晚剧情文件未找到，请重新上传使用');
    return readPreset(JSON.parse(readFileSync(path, 'utf8')), 40);
  }
  context(world) {
    const authored = this.authored(world);
    const projected = gameEndingScenario(this.engine, this.catalog, world, authored);
    const sourceRevision = revisionOf(authored) + ':' + (world.endingMemorySource?.version || 'file');
    const contextRevision = savedRevisionBeforeRename(world, projected, authored, sourceRevision) ??
      createHash('sha256').update(revisionOf(projected.scenario) + sourceRevision).digest('hex').slice(0, 16);
    return { ...projected, contextRevision, sourceRevision,
      sourceName: world.endingMemorySource?.fileName || authored.sourceName || '七日剧情' };
  }
  sourceState(world) {
    const context = this.context(world);
    const pending = this.progress.get(world.worldId);
    const current = pending?.runId === world.runId && pending.sourceRevision === context.sourceRevision ? pending : null;
    const outdatedProgress = pending?.runId === world.runId && pending.sourceRevision !== context.sourceRevision;
    const cached = Object.values(world.seventhNightEndings || {}).map(e => e.result)
      // Keep exact pre-rename built-in checkpoints readable while allowing a
      // completed snapshot to remain visible after new live events change only
      // its contextRevision.
      .filter(e => e.sourceRevision === context.sourceRevision || e.contextRevision === context.contextRevision)
      .sort((a,b) => a.createdAt.localeCompare(b.createdAt)).at(-1);
    const ending = current?.result || (!current && cached);
    const outdated = outdatedProgress && !ending;
    const previous = Object.values(world.seventhNightEndings || {}).map(e => e.result)
      .filter(e => e.id !== ending?.id).sort((a,b) => a.createdAt.localeCompare(b.createdAt)).at(-1);
    return { sourceName: context.sourceName, sourceRevision: context.sourceRevision, uploaded: !!world.endingMemorySource,
      contextRevision: context.contextRevision, memoryCount: knownMemories(context.scenario).length,
      newExperiencesAvailable: !!ending && ending.contextRevision !== context.contextRevision,
      liveMemoryCount: context.liveMemoryCount, phase: current?.phase || (outdated ? 'outdated' : 'idle'),
      completed: current?.completed || 0, error: current?.error || (outdated ? '生成期间本局经历已变化，请按最新记忆重新生成。已上传文件仍然保留。' : ''),
      ...(ending ? { ending } : {}), ...(previous ? { previousEnding: previous } : {}) };
  }
  generate(input, assertRun) {
    const world = assertRun(input);
    if (!/^[a-zA-Z0-9_-]{1,100}$/.test(input.requestId || '')) throw new TurnError('结局请求缺少稳定标识');
    const mode = input.details?.mode || 'model';
    if (!['model', 'preview-leave', 'preview-stay'].includes(mode)) throw new TurnError('结局模式无效');
    const fingerprint = createHash('sha256').update(JSON.stringify({ requestId: input.requestId, mode })).digest('hex');
    const cached = world.seventhNightEndings?.[input.requestId];
    if (cached) { if (cached.fingerprint !== fingerprint) throw new TurnError('结局请求标识已用于另一种选择'); return Promise.resolve(cached.result); }
    const key = `${world.worldId}:${world.epoch}:${input.requestId}`;
    if (this.inflight.has(key)) {
      const pending = this.inflight.get(key);
      if (pending.fingerprint !== fingerprint) throw new TurnError('结局请求标识已用于另一种选择');
      return pending.promise;
    }
    const promise = this.run(input, mode, fingerprint, assertRun).finally(() => this.inflight.delete(key));
    this.inflight.set(key, { fingerprint, promise }); return promise;
  }
  async run(input, mode, fingerprint, assertRun) {
    const original = assertRun(input), epoch = original.epoch;
    const sourceDay = original.day, sourceSeq = original.seq, zState = structuredClone(original.actors.z.info);
    const { scenario, liveMemoryCount, eventIds, contextRevision, sourceRevision, sourceName } = this.context(original);
    const progress = { runId: original.runId, contextRevision, sourceRevision, phase: 'deciding', completed: 0 };
    this.progress.set(original.worldId, progress);
    const checkCurrent = () => {
      const current = assertRun(input);
      // Autonomous NPC events continue while an upload is being generated.
      // Finish this immutable snapshot; only replacing its authored source or
      // leaving its timeline can invalidate the request.
      if (current.epoch !== epoch || this.context(current).sourceRevision !== sourceRevision)
        throw new TurnError('经历已更新，旧结局不会写回，请使用最新记忆重新生成');
      return current;
    };
    const persona = this.catalog.profile('z');
    let decision, recollections = [], dailyPages = [], recapPages = [];
    try {
    if (mode === 'model') {
      const response = await this.provider.call(this.provider.request(decisionMessages(scenario, persona), { json: true, maxTokens: 2800 }));
      checkCurrent();
      decision = validateDecision(parseModel(response.text), scenario);
      const memories = knownMemories(scenario);
      progress.phase = 'recalling';
      for (let start = 0; start < memories.length; start += 12) {
        checkCurrent();
        const batch = { ...scenario, memories: memories.slice(start, start + 12) };
        const response = await this.provider.call(this.provider.request(recollectionMessages(batch, decision, persona), { json: true, maxTokens: 6500 }));
        recollections.push(...validateRecollections(parseModel(response.text), batch));
        checkCurrent(); progress.completed = recollections.length;
      }
      const days = [...new Set(memories.map(m => m.day))].sort((a, b) => a - b);
      const approved = approvedDailyPagesFor(scenario);
      if (approved) {
        dailyPages = validateDailyPages({ dailyPages: approved }, scenario);
      } else {
        const recollectionById = new Map(recollections.map(item => [item.memoryId, item.text]));
        for (const day of days) {
          const dayScenario = { ...scenario, memories: memories.filter(m => m.day === day).map(memory => ({
            ...memory, text: recollectionById.get(memory.id) || memory.text,
          })) };
          dailyPages.push(await requestDailyPage(this.provider, dayScenario, decision, day, persona));
          checkCurrent();
        }
      }
    } else {
      decision = previewDecision(scenario, mode === 'preview-leave' ? 'leave' : 'stay');
      recollections = previewRecollections(scenario); dailyPages = previewDailyPages(scenario);
    }
    recapPages = recapPagesFor(scenario, dailyPages);
    const current = checkCurrent();
    if (current.epoch !== epoch) throw new TurnError('结局属于已取消的时间线');
    const result = { id: randomUUID(), mode, revision: revisionOf(scenario), createdAt: new Date().toISOString(),
      scenarioTitle: scenario.title, playerName: scenario.playerName, lastMessage: scenario.lastMessage, decision, recollections,
      dailyPages, recapPages, memoryCount: recollections.length,
      outcome: resolveOutcome(scenario, decision.choice), worldId: current.worldId, runId: current.runId,
      sourceDay, sourceSeq, liveMemoryCount, sourceEventIds: eventIds,
      zState, sourceName, contextRevision, sourceRevision };
    current.seventhNightEndings ||= {};
    current.seventhNightEndings[input.requestId] = { fingerprint, result };
    const keys = Object.keys(current.seventhNightEndings);
    if (keys.length > 5) delete current.seventhNightEndings[keys[0]];
    this.engine.save(current);
    progress.phase = 'complete'; progress.completed = recollections.length; progress.result = result;
    return result;
    } catch (error) {
      progress.phase = 'failed'; progress.error = error.message || '结局生成未完成，请重试'; throw error;
    }
  }
}
