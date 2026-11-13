import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createEndingServer, ROOT } from '../server.mjs';
import { validateScenario, normalizePlayerName, renderPlayerName, revisionOf, knownMemories, decisionMessages, validateDecision, validateRecollections,
  previewDecision, resolveOutcome, sourceBlocks, annotatedScenario, dailyPageMessages, validateDailyPages,
  previewDailyPages, approvedDailyPagesFor, scenarioMemoryFingerprint, scenarioMemoryFingerprints, recapRanges, validateRecapPages,
  approvedRecapPagesFor, previewRecapPages, recapPagesFor } from '../ending.mjs';
const sample = JSON.parse(await readFile(join(ROOT, 'tests/scenario.fixture.json'), 'utf8'));
const copy = () => structuredClone(sample);
test('manual scenario round-trip retains memory, plot and knowledge flags', () => {
  const s = copy(); s.memories.push({ id: 'private', day: 7, kind: 'plot', title: '幕后', text: 'PRIVATE_SECRET', zKnows: false });
  const validated = validateScenario(s);
  assert.deepEqual(validated, { ...s, playerName: '字小节' }); assert.equal(knownMemories(validated).length, 8);
  const prompt = JSON.stringify(decisionMessages(validated, 'persona'));
  assert.ok(!prompt.includes('PRIVATE_SECRET')); assert.ok(!prompt.includes('leaveOutcome')); assert.ok(prompt.includes('m08'));
});
test('scenario rejects duplicate IDs, unknown versions, too many memories and invalid day', () => {
  const dup = copy(); dup.memories[1].id = 'm01'; assert.throws(() => validateScenario(dup), /重复/);
  const version = copy(); version.version = 2; assert.throws(() => validateScenario(version));
  const tooMany = copy(); tooMany.memories = Array.from({ length: 41 }, () => sample.memories[0]); assert.throws(() => validateScenario(tooMany));
  const day = copy(); day.memories[0].day = 8; assert.throws(() => validateScenario(day));
});
test('plain-text upload retains all source paragraphs and never lets annotation rewrite them', () => {
  const original = '第一天，Z带玩家认路。\n\n第五天，Z亲眼核实证据。\n\n幕后：Z没有收到公司的私信。';
  const blocks = sourceBlocks(original); assert.equal(blocks.length, 3); assert.equal(blocks.map(b => b.text).join(''), original);
  const annotation = { ...copy(), blocks: blocks.map((b, i) => ({ id: b.id, title: `段落${i}`, day: i === 0 ? 1 : 5, kind: 'plot', zKnows: i < 2, text: '编造的替换' })) };
  const scenario = annotatedScenario(annotation, blocks, 'story.md', original);
  assert.equal(scenario.sourceText, original); assert.equal(scenario.sourceName, 'story.md');
  assert.deepEqual(scenario.memories.map(m => m.text), blocks.map(b => b.text.trim()));
  assert.equal(knownMemories(scenario).length, 2);
  assert.ok(!JSON.stringify(decisionMessages(scenario, 'persona')).includes('公司的私信'));
  const missing = structuredClone(annotation); missing.blocks.pop(); assert.throws(() => annotatedScenario(missing, blocks, 'story.md', original));
});
test('recollection publication requires exact full coverage and chronological order', () => {
  const s = validateScenario(sample), r = s.memories.map(m => ({ memoryId: m.id, text: `remember ${m.title}` }));
  assert.equal(validateRecollections({ recollections: r.toReversed() }, s)[0].memoryId, 'm01');
  assert.throws(() => validateRecollections({ recollections: r.slice(1) }, s), /覆盖/);
  const duplicates = structuredClone(r); duplicates[1].memoryId = 'm01'; assert.throws(() => validateRecollections({ recollections: duplicates }, s), /重复/);
  const fabricated = structuredClone(r); fabricated[0].memoryId = 'unknown'; assert.throws(() => validateRecollections({ recollections: fabricated }, s));
});
test('daily pages keep dates, facts and feelings separate without fixed labels', () => {
  const s = validateScenario(sample), pages = previewDailyPages(s);
  assert.equal(pages.length, new Set(knownMemories(s).map(m => m.day)).size);
  assert.deepEqual(validateDailyPages({ dailyPages: pages }, s), pages);
  assert.ok(pages.every(page => page.text.includes('\n\n')));
  const prompt = JSON.stringify(dailyPageMessages({ ...s, memories: s.memories.filter(m => m.day === 1) }, previewDecision(s, 'leave'), 1, 'persona'));
  assert.match(prompt, /按材料判断/); assert.match(prompt, /不重复/); assert.match(prompt, /材料少时只写1行/); assert.match(prompt, /不超过450字/);
  assert.equal(typeof scenarioMemoryFingerprint(s), 'string');
  const legacyName = validateScenario({ ...s, playerName: '旅人', memories: s.memories.map(memory => ({ ...memory, text: memory.text.replaceAll('玩家', '旅人') })) });
  const modernName = validateScenario({ ...s, playerName: '字小节', memories: legacyName.memories.map(memory => ({ ...memory, text: memory.text.replaceAll('旅人', '字小节') })) });
  assert.ok(scenarioMemoryFingerprints(modernName).includes(scenarioMemoryFingerprint(legacyName)));
  const changed = validateScenario({ ...modernName, memories: modernName.memories.map((memory, i) => i === 0 ? { ...memory, text: `${memory.text}正文变化` } : memory) });
  assert.ok(!scenarioMemoryFingerprints(changed).includes(scenarioMemoryFingerprint(legacyName)));
  assert.equal(approvedDailyPagesFor(s), null, 'approved pages must not apply to another story');
  const labeled = structuredClone(pages); labeled[0].text = '今天的事：\n发生了一件事。\n\n我有点难过。';
  assert.throws(() => validateDailyPages({ dailyPages: labeled }, s), /栏目标签/);
  const spacedLabel = structuredClone(pages); spacedLabel[0].text = '重要事实 发生了一件事。\n我把细节留在这里。\n\n我有点难过。';
  assert.throws(() => validateDailyPages({ dailyPages: spacedLabel }, s), /栏目标签/);
  const splitLabel = structuredClone(pages); splitLabel[0].text = 'Z 的感悟\n发生了一件事。\n\n我有点难过。';
  assert.throws(() => validateDailyPages({ dailyPages: splitLabel }, s), /栏目标签/);
  const spacedLabelParts = structuredClone(pages); spacedLabelParts[0].text = 'Z 的 感悟\n发生了一件事。\n\n我有点难过。';
  assert.throws(() => validateDailyPages({ dailyPages: spacedLabelParts }, s), /栏目标签/);
  const crlf = structuredClone(pages); crlf[0].text = '发生了一件事。\r\n我把细节留在这里。\r\n\r\n我有点难过。';
  assert.equal(validateDailyPages({ dailyPages: crlf }, s)[0].text, '发生了一件事。\n我把细节留在这里。\n\n我有点难过。');
  const threeParagraphs = structuredClone(pages); threeParagraphs[0].text = '事实。\n\n感受。\n\n多余。';
  assert.throws(() => validateDailyPages({ dailyPages: threeParagraphs }, s), /严格分出/);
  const twoSentences = structuredClone(pages); twoSentences[0].text = '事实一。事实二。\n\n感受。';
  assert.throws(() => validateDailyPages({ dailyPages: twoSentences }, s), /每句/);
  const tooLong = structuredClone(pages); tooLong[0].text = `${'事实'.repeat(226)}\n\n感受。`;
  assert.throws(() => validateDailyPages({ dailyPages: tooLong }, s), /不超过450/);
  assert.equal(normalizePlayerName('旅人'), '字小节'); assert.equal(normalizePlayerName('玩家'), '字小节');
  assert.equal(normalizePlayerName('听雨'), '听雨');
  assert.equal(renderPlayerName('旅人和玩家', '$&旅人'), '$&旅人和$&旅人');
  assert.equal(renderPlayerName('字小节和玩家', '听雨'), '听雨和听雨');
  assert.equal(renderPlayerName('小旅人到了', '小旅人'), '小旅人到了');
  const playerPrompt = JSON.stringify(dailyPageMessages({ ...s, playerName: '旅人' }, previewDecision(s, 'leave'), 1, 'persona'));
  assert.match(playerPrompt, /Z和字小节/); assert.doesNotMatch(playerPrompt, /Z和旅人/);
  const decisionPrompt = JSON.stringify(decisionMessages({ ...s, playerName: '旅人', context: '旅人在码头等着。' }, 'persona'));
  assert.match(decisionPrompt, /字小节/); assert.doesNotMatch(decisionPrompt, /旅人/);
});
test('recap pages cover only populated periods and keep facts in Z-visible memories', async () => {
  const s = copy(); s.memories.push({ id: 'hidden_recap', day: 2, kind: 'memory', title: '幕后结局', text: 'PRIVATE_RECAP_SECRET。', zKnows: false });
  const scenario = validateScenario(s), daily = previewDailyPages(scenario), recaps = previewRecapPages(scenario, daily);
  assert.deepEqual(recapRanges(scenario), [{ startDay: 1, endDay: 1 }, { startDay: 2, endDay: 3 }, { startDay: 4, endDay: 7 }]);
  assert.deepEqual(recaps.map(page => [page.startDay, page.endDay]), [[1, 1], [2, 3], [4, 7]]);
  assert.deepEqual(validateRecapPages({ recapPages: recaps }, scenario), recaps);
  assert.ok(recaps.every(page => page.text.includes('\n\n') && page.text.length <= 450));
  assert.ok(!JSON.stringify(recaps).includes('PRIVATE_RECAP_SECRET'));
  assert.equal(approvedRecapPagesFor(scenario), null, 'approved recap must not apply to another story');
  assert.deepEqual(recapPagesFor(scenario, daily), recaps);

  const early = validateScenario({ ...scenario, memories: scenario.memories.filter(memory => memory.zKnows && memory.day <= 3) });
  assert.deepEqual(previewRecapPages(early, previewDailyPages(early)).map(page => [page.startDay, page.endDay]), [[1, 1], [2, 3]]);
  const late = validateScenario({ ...scenario, memories: scenario.memories.filter(memory => memory.zKnows && memory.day >= 4) });
  assert.deepEqual(previewRecapPages(late, previewDailyPages(late)).map(page => [page.startDay, page.endDay]), [[4, 7]]);

  const narrated = daily.map(page => ({ ...page }));
  narrated.find(page => page.day === 1).text = '字小节在码头接到我，我只听到了这部分。\n\n我还在辨认这句话。';
  narrated.find(page => page.day === 4).text = '直播在我追问时突然断了。\n\n我没法把没听完的部分补齐。';
  narrated.find(page => page.day === 7).text = '今晚Z自己决定短暂开播。\n\n听见她自己作决定，我还是高兴。';
  const narratedRecaps = previewRecapPages(scenario, narrated);
  assert.match(narratedRecaps[0].text, /字小节在码头接到我/);
  assert.doesNotMatch(narratedRecaps[0].text, /我看到/);
  assert.match(narratedRecaps[2].text, /直播在我追问时突然断了/);
  assert.match(narratedRecaps[2].text, /今晚Z自己决定短暂开播/);
});
test('approved recap variants match both built-in presets while custom sources stay unapproved', async () => {
  const normal = validateScenario(JSON.parse(await readFile(join(ROOT, 'scenario.example.json'), 'utf8')), { maxMemories: 200 });
  const rainy = validateScenario(JSON.parse(await readFile(join(ROOT, '七日随机记忆-雨停以后-20261113.json'), 'utf8')), { maxMemories: 200 });
  for (const scenario of [normal, rainy]) {
    const pages = approvedRecapPagesFor(scenario);
    assert.deepEqual(pages?.map(page => [page.startDay, page.endDay]), [[1, 1], [2, 3], [4, 7]]);
    assert.ok(pages.every(page => page.text.includes('\n\n')));
    const changed = validateScenario({ ...scenario, sourceName: '自定义故事.md', memories: scenario.memories.map((memory, i) => i === 0 ? { ...memory, text: memory.text + '正文变化' } : memory) }, { maxMemories: 200 });
    assert.equal(approvedRecapPagesFor(changed), null);
  }
});
test('recap selects several same-day facts and never promotes unseen memories', () => {
  const base = copy();
  const scenario = validateScenario({ ...base, memories: [
    ...base.memories,
    { id: 'hidden_npc', day: 1, kind: 'memory', title: '幕后岛民', text: 'PRIVATE_UNSEEN_NPC。', zKnows: false },
    { id: 'same_day_npc', day: 1, kind: 'memory', title: '晴野的纸袋', text: '晴野当面教玩家把纸袋压平。', zKnows: true },
    { id: 'same_day_photo', day: 1, kind: 'memory', title: '周伯伯的相机', text: '周伯伯当面给玩家和Z拍了一张照片。', zKnows: true },
  ] });
  const daily = previewDailyPages(scenario);
  daily.find(page => page.day === 1).text = 'Z在码头接到玩家。\n晴野教玩家把纸袋压平。\n周伯伯给两人拍了照片。\n\n我还在认这些人的名字。';
  const pages = previewRecapPages(scenario, daily);
  assert.deepEqual(pages.map(page => [page.startDay, page.endDay]), [[1, 1], [2, 3], [4, 7]]);
  const firstFacts = pages[0].text.split(/\n\n/)[0].split('\n').filter(Boolean);
  assert.equal(firstFacts.length, 3);
  assert.match(pages[0].text, /晴野/);
  assert.match(pages[0].text, /周伯伯/);
  assert.doesNotMatch(JSON.stringify(pages), /PRIVATE_UNSEEN_NPC/);
});
test('preview fallback rewrites source narration into Z first person', () => {
  const source = copy().memories[4];
  const scenario = validateScenario({ ...copy(), memories: [source] });
  const pages = previewRecapPages(scenario);
  assert.deepEqual(pages.map(page => [page.startDay, page.endDay]), [[4, 7]]);
  assert.match(pages[0].text, /我起初不相信/);
  assert.doesNotMatch(pages[0].text, /Z起初不相信/);
});
test('Z choice cannot cite hidden or fabricated reasons', () => {
  const s = copy(), decision = previewDecision(s, 'leave'); decision.reasonMemoryIds = ['unknown'];
  assert.throws(() => validateDecision(decision, s), /不知道/);
});
test('wanting to leave never supplies missing readiness or execution', () => {
  const s = copy(); assert.equal(resolveOutcome(s, 'leave').status, 'pending');
  s.world.leaveOutcome = 'succeeded'; s.world.departureReady = false;
  assert.equal(resolveOutcome(s, 'leave').status, 'blocked');
  s.world.departureReady = true; assert.equal(resolveOutcome(s, 'leave').status, 'succeeded');
});
test('choosing to stay does not turn uncertainty or shutdown into safety', () => {
  const s = copy(); assert.equal(resolveOutcome(s, 'stay').status, 'uncertain');
  s.world.stayOutcome = 'shutdown'; assert.equal(resolveOutcome(s, 'stay').status, 'shutdown');
  s.world.stayOutcome = 'safe'; assert.equal(resolveOutcome(s, 'stay').status, 'safe');
});
async function fixture(t, provider) {
  const dataDir = await mkdtemp(join(tmpdir(), 'z-ending-test-'));
  await import('node:fs/promises').then(fs => fs.writeFile(join(dataDir, 'scenario.json'), JSON.stringify(sample)));
  const app = await createEndingServer({ dataDir, provider, selectionPath: join(dataDir, 'source-selection.json') });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${app.server.address().port}`, status = await fetch(base + '/api/status').then(r => r.json());
  t.after(async () => { app.server.closeAllConnections(); await new Promise(resolve => app.server.close(resolve)); await rm(dataDir, { recursive: true, force: true }); });
  const call = async (path, method = 'GET', data) => {
    const r = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json', 'X-Ending-Token': status.token }, ...(data ? { body: JSON.stringify(data) } : {}) });
    return { status: r.status, data: await r.json() };
  };
  return { app, call, base, status };
}
test('editor save persists, rejects stale revisions, and exports no credentials', async t => {
  const { call } = await fixture(t);
  const old = (await call('/api/scenario')).data;
  const next = copy(); next.memories[0].text = '手动输入了一段新记忆';
  const imported = await call('/api/validate', 'POST', { scenario: next }); assert.equal(imported.status, 200);
  assert.equal((await call('/api/scenario')).data.revision, old.revision);
  assert.equal((await call('/api/validate', 'POST', { scenario: { version: 2 } })).status, 400);
  const saved = await call('/api/scenario', 'PUT', { revision: old.revision, scenario: next }); assert.equal(saved.status, 200);
  assert.notEqual(saved.data.revision, old.revision); assert.equal(saved.data.scenario.memories[0].text, next.memories[0].text);
  assert.equal((await call('/api/scenario', 'PUT', { revision: old.revision, scenario: sample })).status, 409);
  assert.ok(!JSON.stringify(saved.data).includes('apiKey'));
});
test('both offline previews have all known memories and reloadable standalone runs', async t => {
  const { call } = await fixture(t), revision = revisionOf(validateScenario(sample));
  for (const [mode, choice] of [['preview-leave', 'leave'], ['preview-stay', 'stay']]) {
    const r = await call('/api/generate', 'POST', { mode, revision }); assert.equal(r.status, 200); assert.equal(r.data.decision.choice, choice);
    assert.equal(r.data.memoryCount, 8); assert.equal(r.data.mode, mode);
    assert.deepEqual((await call(`/api/runs/${r.data.id}`)).data, r.data);
  }
});
test('JSON file upload previews without overwriting active run until confirmed', async t => {
  const { call, app } = await fixture(t), next = copy(); next.title = '上传的故事'; next.playerName = '听雨';
  const draft = await call('/api/import', 'POST', { name: 'story.json', text: JSON.stringify(next) });
  assert.equal(draft.status, 200); assert.equal(draft.data.scenario.title, '上传的故事');
  assert.equal(draft.data.scenario.playerName, '听雨');
  assert.equal((await call('/api/scenario')).data.scenario.title, sample.title);
  const saved = await call('/api/scenario', 'PUT', { scenario: draft.data.scenario, revision: draft.data.revision, useFile: true });
  assert.equal(saved.status, 200); assert.equal(saved.data.scenario.title, '上传的故事');
  const selected = JSON.parse(await readFile(join(app.dataDir, 'source-selection.json'), 'utf8'));
  assert.equal(selected.scenarioPath, join(app.dataDir, 'scenario.json'));
});
test('unconfigured model never silently substitutes a preview', async t => {
  const provider = { publicConfig: () => ({ configured: false }) };
  const { call } = await fixture(t, provider);
  const r = await call('/api/generate', 'POST', { mode: 'model', revision: revisionOf(validateScenario(sample)) }); assert.equal(r.status, 422);
});
test('model decision uses manual content, filters private facts, and locks choice before narration', async t => {
  const requests = [], provider = {
    publicConfig: () => ({ configured: true }), request: messages => messages,
    call: async messages => { requests.push(messages); const input = JSON.parse(messages[1].content);
      if (requests.length === 1) return { text: JSON.stringify(previewDecision(sample, 'stay')) };
      if (input.dailyPage) return { text: JSON.stringify({ dailyPages: [{ day: input.day, title: `第${input.day}天`, text: '一件具体的事发生了。\n我把细节留在这里。\n\n我还在慢慢想。' }] }) };
      assert.equal(input.choice, 'stay');
      return { text: JSON.stringify({ recollections: input.memories.map(m => ({ memoryId: m.id, text: `我记得：${m.text}` })) }) };
    },
  };
  const { call } = await fixture(t, provider), s = copy(); s.memories[0].text = 'MANUAL_FACT';
  s.memories.push({ id: 'private', day: 7, kind: 'plot', title: '幕后', text: 'PRIVATE_SECRET', zKnows: false });
  const saved = await call('/api/scenario', 'PUT', { revision: revisionOf(validateScenario(sample)), scenario: s });
  const r = await call('/api/generate', 'POST', { mode: 'model', revision: saved.data.revision });
  assert.equal(r.status, 200); assert.equal(r.data.mode, 'model'); assert.equal(r.data.decision.choice, 'stay');
  assert.ok(JSON.stringify(requests).includes('MANUAL_FACT')); assert.ok(!JSON.stringify(requests).includes('PRIVATE_SECRET'));
  assert.equal(r.data.recollections[0].original, 'MANUAL_FACT'); assert.equal(r.data.memoryCount, 8);
  assert.deepEqual(r.data.dailyPages.map(page => page.day), [1, 2, 3, 4, 5, 6, 7]);
  assert.ok(r.data.dailyPages.every(page => page.text.includes('一件具体的事')), '普通模型分支应使用模型返回的每日稿');
  assert.deepEqual(r.data.recapPages.map(page => [page.startDay, page.endDay]), [[1, 1], [2, 3], [4, 7]]);
});
test('partial model recollections fail without publishing fabricated success', async t => {
  let calls = 0; const provider = { publicConfig: () => ({ configured: true }), request: m => m,
    call: async () => ({ text: JSON.stringify(++calls === 1 ? previewDecision(sample, 'leave') : { recollections: [] }) }) };
  const { call, app } = await fixture(t, provider);
  const r = await call('/api/generate', 'POST', { mode: 'model', revision: revisionOf(validateScenario(sample)) });
  assert.equal(r.status, 502); assert.match(r.data.error, /覆盖/);
  assert.equal((await call('/api/status')).data.busy, false);
});
test('malformed daily page retries once then fails instead of using a fabricated preview', async t => {
  let dailyCalls = 0;
  const provider = { publicConfig: () => ({ configured: true }), request: messages => messages,
    call: async messages => {
      const input = JSON.parse(messages[1].content);
      if (input.dailyPage) { dailyCalls += 1; return { text: JSON.stringify({ noDailyPages: true }) }; }
      if (input.choice) return { text: JSON.stringify({ recollections: input.memories.map(m => ({ memoryId: m.id, text: `我看到${m.title}。` })) }) };
      return { text: JSON.stringify({ ...previewDecision(sample, 'leave'), reasonMemoryIds: input.memories.slice(-1).map(m => m.id) }) };
    } };
  const { call } = await fixture(t, provider), s = copy();
  s.memories = s.memories.filter(m => m.zKnows && m.day === 1).slice(0, 1);
  const saved = await call('/api/scenario', 'PUT', { revision: revisionOf(validateScenario(sample)), scenario: s });
  const r = await call('/api/generate', 'POST', { mode: 'model', revision: saved.data.revision });
  assert.equal(r.status, 502); assert.match(r.data.error, /每日回忆格式不完整/); assert.equal(dailyCalls, 2);
});
test('local API refuses foreign origins and unauthenticated writes', async t => {
  const { base } = await fixture(t);
  assert.equal((await fetch(base + '/api/scenario', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: '{}' })).status, 403);
  assert.equal((await fetch(base + '/api/status', { headers: { Origin: 'https://example.com' } })).status, 403);
});
