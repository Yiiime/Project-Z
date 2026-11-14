import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createServer } from '../server.mjs';
import { authoredFile, reviewedPresentationId } from '../lib/ending.mjs';
import { knownMemories, validateScenario } from '../../../Assets/Seabreeze/Dialogue/EndingDemo/ending.mjs';

const script = readFileSync(new URL('./fixtures/looking-back-presentation.md', import.meta.url), 'utf8');
const scenario = validateScenario(JSON.parse(readFileSync(authoredFile, 'utf8')));
function fixture(t, choice = 'leave') {
  let calls = 0;
  const provider = { publicConfig: () => ({ configured: true }), config: { topK: 6 },
    request: messages => messages, call: async () => { calls++; throw Error('Unexpected model generation'); } };
  const app = createServer({ dbPath: ':memory:', providerFactory: () => provider, backgroundEnabled: false, endingScenarioPath: authoredFile });
  t.after(() => { app.server.emit('close'); app.store.close(); });
  const base = { worldId: 'presentation_world', runId: 'first_run' };
  app.engine.open(base.worldId, base.runId);
  const ending = { id: 'reviewed_original', mode: 'model', worldId: base.worldId, runId: base.runId,
    createdAt: '2026-11-14T03:00:00Z', sourceName: '七日完整模拟-对话与行动.md', playerName: scenario.playerName,
    sourceRevision: 'original_source:file', contextRevision: 'original_context',
    decision: { choice }, outcome: { status: choice === 'leave' ? 'pending' : 'uncertain' },
    recollections: knownMemories(scenario).map(memory => ({ memoryId: memory.id, day: memory.day, original: memory.text })) };
  ending.memoryCount = ending.recollections.length;
  const world = app.engine.world(base.worldId);
  world.seventhNightEndings = { original: { fingerprint: 'fixture', result: ending } };
  world.endingMemorySource = { scenario, originalText: script, fileName: '回过头的人-三幕黑屏.md', version: 'first_upload' };
  app.engine.save(world);
  const checkRun = input => {
    const current = app.engine.world(input.worldId);
    assert.equal(current.runId, input.runId);
    return current;
  };
  return { app, base, ending, checkRun, calls: () => calls };
}

test('presentation recognition uses complete content and tolerates file line endings', () => {
  assert.equal(reviewedPresentationId(script), 'looking-back-three-acts');
  assert.equal(reviewedPresentationId('\uFEFF' + script.replace(/\n/g, '\r\n') + '\r\n'), 'looking-back-three-acts');
  assert.equal(reviewedPresentationId(script + '\n新增的真实经历。'), null);
  assert.equal(reviewedPresentationId('回过头的人-三幕黑屏.md'), null);
});

for (const choice of ['leave', 'stay']) test(`mistaken script upload replays the saved ${choice} result without generating or changing it`, async t => {
  const f = fixture(t, choice);
  const before = structuredClone(f.app.engine.world(f.base.worldId));
  const state = f.app.endings.sourceState(before);
  assert.equal(state.presentationId, 'looking-back-three-acts');
  assert.equal(state.phase, 'complete');
  assert.deepEqual(state.ending, f.ending);
  const replay = await f.app.endings.generate({ ...f.base, requestId: 'retry', details: { mode: 'model' } }, f.checkRun);
  assert.deepEqual(replay, f.ending);
  assert.equal(f.calls(), 0);
  assert.deepEqual(f.app.engine.world(f.base.worldId), before);
  const checkpoint = f.app.engine.checkpoint(f.base.worldId);
  f.app.engine.open(f.base.worldId, 'restored_run', checkpoint);
  assert.deepEqual(f.app.endings.sourceState(f.app.engine.world(f.base.worldId)).ending, f.ending);
});

test('reupload and parser changes still recover exact original memories with live events', t => {
  const f = fixture(t);
  const world = f.app.engine.world(f.base.worldId);
  world.endingMemorySource.version = 'different_upload';
  world.endingMemorySource.fileName = 'renamed.md';
  world.endingMemorySource.scenario = { ...scenario, title: 'A different parser title' };
  world.seventhNightEndings.original.result.recollections.push({ memoryId: 'game_001', day: 1, original: '真实的游戏中交流' });
  // A newer result with the same file name but changed story must not win.
  const unrelated = structuredClone(f.ending);
  unrelated.id = 'unrelated'; unrelated.createdAt = '2026-11-14T04:00:00Z';
  unrelated.recollections[0].original += ' 不同的事件。';
  world.seventhNightEndings.newer = { result: unrelated };
  f.app.engine.save(world);
  const state = f.app.endings.sourceState(f.app.engine.world(f.base.worldId));
  assert.equal(state.ending.id, f.ending.id);
  assert.equal(state.ending.recollections.at(-1).memoryId, 'game_001');
});

test('a reviewed historical revision survives Unity replacement characters only with every original identity intact', t => {
  const f = fixture(t);
  const world = f.app.engine.world(f.base.worldId);
  const result = world.seventhNightEndings.original.result;
  result.sourceRevision = '04a9cba4752834f0:machine-specific-upload-id';
  result.recollections[0].original = result.recollections[0].original.slice(0, -1) + '\uFFFD';
  f.app.engine.save(world);
  assert.equal(f.app.endings.sourceState(f.app.engine.world(f.base.worldId)).ending.id, result.id);
  result.recollections[0].memoryId = 'same-title-but-different-story';
  f.app.engine.save(world);
  assert.equal(f.app.endings.sourceState(f.app.engine.world(f.base.worldId)).phase, 'failed');
});

test('a presentation without a matching saved ending reports what is missing', t => {
  const f = fixture(t);
  const world = f.app.engine.world(f.base.worldId);
  world.seventhNightEndings.original.result.recollections[0].original += ' 正文已变更。';
  f.app.engine.save(world);
  const state = f.app.endings.sourceState(f.app.engine.world(f.base.worldId));
  assert.equal(state.phase, 'failed'); assert.equal(state.ending, undefined);
  assert.match(state.error, /七日经历文件/);
  assert.throws(() => f.app.endings.generate({ ...f.base, requestId: 'retry' }, f.checkRun), /七日经历文件/);
  assert.equal(f.calls(), 0);
});

test('uploading the built-in presentation is stopped before model parsing or source replacement', async t => {
  const f = fixture(t);
  const url = f.app.endingUploads.open(f.base, 'http://127.0.0.1:1');
  const token = new URL(url).hash.slice(1);
  const before = structuredClone(f.app.engine.world(f.base.worldId));
  await assert.rejects(f.app.endingUploads.import(token, { name: '任意名字.md', text: script }), /直接返回游戏查看/);
  assert.equal(f.calls(), 0);
  assert.deepEqual(f.app.engine.world(f.base.worldId), before);
});
