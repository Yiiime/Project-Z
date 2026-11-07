import { AppError } from './provider.mjs';
import { LABELS, strength, memoryState, memoryView, accessible } from './natural-memory.mjs';
import { PERSONA_REVISION, upgradeSnapshot } from './roster-migration.mjs';

// Inspection deliberately avoids engine.response/select: reading must not advance
// response revisions, strengthen recall, retry extraction, or call the model.
export function inspectMemories(store, book, { worldId = '', actorId = 'c01' } = {}) {
  const actor = book.actors.find(a => a.id === actorId);
  if (!actor) throw new AppError('请选择岛民名单中的角色', 400);
  const experiments = store.experiments().filter(e => e.worldId && e.id === 'world_' + e.worldId)
    .sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)));
  const worlds = experiments.map(e => ({ worldId: e.worldId, day: e.day, period: e.period, hour: e.hour,
    loadedAt: e.createdAt, epoch: e.epoch, seq: e.seq }));
  const exp = worldId ? experiments.find(e => e.worldId === worldId) : experiments[0];
  if (worldId && !exp) throw new AppError('这份游戏进度不存在，请重新选择', 404);
  const result = { ok: true, worlds, world: exp ? worlds.find(w => w.worldId === exp.worldId) : null,
    actorId, actors: book.actors.map(a => ({ id: a.id, name: a.name, role: a.role, count: 0 })), memories: [], pending: 0 };
  if (!exp) return result;
  const belongs = r => r.epoch === exp.epoch && (r.createdHour ?? r.hour) <= exp.hour;
  let source={version:1,personaRevision:exp.personaRevision||0,worldId:exp.worldId,hour:exp.hour,seq:exp.seq,
    memories:store.records(exp.id,'memory').filter(belongs),messages:store.records(exp.id,'message').filter(belongs),
    events:store.records(exp.id,'event').filter(belongs),pending:store.records(exp.id,'trace').filter(t=>belongs(t)&&t.status==='partial'),reflected:exp.reflected||{}};
  if(source.personaRevision<PERSONA_REVISION){source=upgradeSnapshot(source,book);result.world.migrationPending=true;}
  const all = source.memories.map(m=>({...m,epoch:exp.epoch}));
  const own = all.filter(m => m.actorId === actorId);
  const ownById = new Map(own.map(m => [m.id, m]));
  const messages = new Map(source.messages.filter(m => m.participants?.includes(actorId)).map(m => [m.id, m]));
  const personName = id => id === 'player' ? '玩家' : id === 'system' ? '系统' : book.actors.find(a => a.id === id)?.name || '系统';
  result.actors.forEach(a => { a.count = all.filter(m => m.actorId === a.id && !m.supersededBy).length; });
  result.pending = source.pending.filter(t => t.status === 'partial' &&
    t.observers?.includes(actorId) && !t.extractionActors?.includes(actorId)).length;
  result.memories = own.slice().reverse().sort((a, b) => b.createdHour - a.createdHour).map(m => {
    const view = memoryView(m, exp), state = memoryState(m, exp, all);
    const dependency = id => {
      const source = ownById.get(id);
      return source ? { id, gist: source.gist, kind: source.kind, counterexample: (m.counterexamples || []).includes(id) } : null;
    };
    return { id: m.id, actorId, kind: m.kind, kindLabel: LABELS[m.kind] || m.kind,
      content: m.content, gist: m.gist, subject: m.subject || '', origin: m.origin || '', sourceLabel: m.sourceLabel || '',
      createdHour: m.createdHour, originalHour: m.originalHour ?? null, ...strength(m, exp.hour), state,
      certainty: m.kind === 'reflection' ? '主观判断，可能有误' : m.kind === 'hearsay' ? '他人陈述，未必属实' :
        m.kind === 'commitment' ? '约定不等于已经完成' : m.origin === 'authored_backstory' ? '人设明确的过去经历，日期未记载' : '游戏确认的亲历或通知',
      recallView: accessible(state) ? { content: view.content, detailLevel: view.detailLevel, reason: view.reason } :
        { content: '当前不用于回应。', detailLevel: 'excluded', reason: state.reason },
      recallCount: m.recallCount || 0, detailRecallCount: m.detailRecallCount || 0,
      commitmentStatus: m.commitmentStatus || '', supersededBy: ownById.has(m.supersededBy) ? m.supersededBy : null,
      evidence: (m.evidence || []).flatMap(e => {
        const message = messages.get(e.messageId);
        return message ? [{ messageId: message.id, quote: e.quote, content: message.content,
          speaker: personName(message.speakerId), hour: message.hour }] : [];
      }), dependencies: (m.dependencies || []).map(dependency).filter(Boolean) };
  });
  return result;
}
