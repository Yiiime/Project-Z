import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Store } from '../lib/store.mjs';
import { BOOK, GameEngine } from '../lib/game-engine.mjs';
import { inspectMemories } from '../lib/memory-inspector.mjs';
import { createMemoryServer } from '../server.mjs';
import { FixtureProvider } from './fixtures.mjs';

function setup(t) {
  const dir = mkdtempSync(join(tmpdir(), 'z-memory-inspector-')), store = new Store(join(dir, 'test.sqlite'));
  const provider = new FixtureProvider(), engine = new GameEngine(store, provider);
  t.after(() => { store.close(); rmSync(dir, { recursive: true, force: true }); });
  return { store, provider, engine };
}
const start = { worldId: 'inspector_test', runId: 'run_initial', day: 1, period: 0 };
const inspect = (store, actorId = 'c01') => inspectMemories(store, BOOK, { worldId: start.worldId, actorId });
function database(store) {
  return JSON.stringify({ experiments: store.experiments(), records: store.db.prepare('SELECT * FROM records ORDER BY id').all(),
    changes: store.db.prepare('SELECT total_changes() AS n').get().n });
}
test('empty inspector lists ten exact identities, validates selection and reveals no configuration', t => {
  const { store, engine } = setup(t);
  const empty = inspectMemories(store, BOOK);
  assert.equal(empty.world, null); assert.deepEqual(empty.memories, []);
  assert.deepEqual(empty.actors.map(a => a.name), ['留洋', '晴子', '梨园', '伊停', '周结', '正义', '星亮', 'Z', '阿遥', '陈伯']);
  assert.throws(() => inspect(store, 'player'), /角色/);
  assert.throws(() => inspect(store), /不存在/);
  engine.open(start); store.saveSettings({ apiKey: 'NEVER_RETURN_PRIVATE_KEY' });
  const value = inspect(store); assert.ok(value.memories.length);
  assert.ok(value.memories.every(m => m.actorId === 'c01'));
  assert.ok(!JSON.stringify(value).includes('NEVER_RETURN_PRIVATE_KEY'));
  assert.ok(!('privateFacts' in value.actors[0]));
});
test('Day4 and separate dialogue memories are visible with witnesses and read-only strength', async t => {
  const { store, provider, engine } = setup(t); engine.open(start);
  const input = { ...start, day: 4 };
  await engine.sync({ ...input, eventData: { id: start.worldId + '_day4', seq: 1, kind: 'day4', day: 4, period: 0 } });
  const proposal = await engine.utterance({ ...input, actorId: 'c01', recipientId: 'c02', requestId: 'line_request', conversationId: 'convo', scene: 'Street' });
  await engine.sync({ ...input, eventData: { id: 'spoken_line', seq: 2, kind: 'utterance', day: 4, period: 0, speakerId: 'c01', recipientId: 'c02',
    conversationId: 'convo', location: 'Street', observers: ['c01', 'c02'], proposalId: proposal.proposalId, text: proposal.text } });
  const before = database(store), calls = provider.calls.length;
  for (const a of BOOK.actors) {
    const view = inspect(store, a.id);
    assert.equal(view.memories.filter(m => m.origin === 'public_day4').length, 1);
    assert.equal(view.memories.filter(m => m.origin === 'staff_day4').length, 0);
    assert.ok(view.memories.every(m => m.actorId === a.id));
  }
  const heard = inspect(store, 'c02').memories.find(m => m.origin === 'dialogue');
  assert.equal(heard.certainty, '他人陈述，未必属实');
  assert.equal(heard.evidence[0].speaker, '留洋'); assert.ok(heard.evidence[0].content.includes(heard.evidence[0].quote));
  assert.ok(!inspect(store, 'c07').memories.some(m => m.origin === 'dialogue' || m.origin === 'conversation_presence'));
  for (let i = 0; i < 4; i++) inspect(store);
  assert.equal(database(store), before); assert.equal(provider.calls.length, calls);
});
test('loading an earlier snapshot removes future records and inspector keeps epochs separate', async t => {
  const { store, engine } = setup(t); const earlier = engine.open(start).checkpoint;
  await engine.sync({ ...start, day: 4, eventData: { id: start.worldId + '_day4', seq: 1, kind: 'day4', day: 4, period: 0 } });
  assert.ok(inspect(store).memories.some(m => m.origin === 'public_day4'));
  engine.open({ ...start, runId: 'run_restored', checkpoint: earlier });
  let value = inspect(store); assert.equal(value.world.day, 1); assert.equal(value.world.epoch, 2);
  assert.ok(!value.memories.some(m => m.origin === 'public_day4'));
  const exp = engine.experiment(start.worldId), source = store.records(exp.id, 'memory')[0];
  store.put('memory', { ...source, id: 'stale', epoch: 1, content: 'STALE_PRIVATE' });
  store.put('memory', { ...source, id: 'future', createdHour: exp.hour + 40, content: 'FUTURE_PRIVATE' });
  store.put('trace', { id: 'hidden_trace', experimentId: exp.id, epoch: exp.epoch, hour: exp.hour, requests: { secret: 'PROMPT_INTERNAL' }, responses: { secret: 'RAW_RESPONSE' } });
  value = JSON.stringify(inspect(store));
  for (const text of ['STALE_PRIVATE', 'FUTURE_PRIVATE', 'PROMPT_INTERNAL', 'RAW_RESPONSE']) assert.ok(!value.includes(text));
});
test('evidence and reflection dependencies never cross the selected actor; superseded memories are excluded from recall', t => {
  const { store, engine } = setup(t); engine.open(start);
  const exp = engine.experiment(start.worldId), sources = store.records(exp.id, 'memory'), source = sources.find(m => m.actorId === 'c01'), other = sources.find(m => m.actorId === 'c02');
  store.put('message', { id: 'private_message', experimentId: exp.id, epoch: exp.epoch, hour: exp.hour, participants: ['c02'], content: 'UNHEARD_PRIVATE' });
  store.put('memory', { ...source, id: 'thought', kind: 'reflection', content: '我的看法（可能有误）：<script>this is text</script>',
    dependencies: [source.id, other.id], evidence: [{ messageId: 'private_message', quote: 'UNHEARD_PRIVATE' }] });
  let thought = inspect(store).memories.find(m => m.id === 'thought');
  assert.equal(thought.certainty, '主观判断，可能有误'); assert.equal(thought.evidence.length, 0);
  assert.deepEqual(thought.dependencies.map(d => d.id), [source.id]);
  store.put('memory', { ...source, supersededBy: 'thought' });
  const replaced = inspect(store).memories.find(m => m.id === source.id);
  assert.equal(replaced.state.code, 'superseded'); assert.equal(replaced.recallView.detailLevel, 'excluded');
});
test('HTTP inspector serves its page but requires local same-origin custom-header reads', async t => {
  const dir = mkdtempSync(join(tmpdir(), 'z-memory-inspector-http-'));
  const provider = new FixtureProvider(), app = createMemoryServer({ dbPath: join(dir, 'test.sqlite'), configPath: join(dir, 'absent.json'), providerFactory: () => provider });
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  t.after(async () => { app.server.closeAllConnections?.(); await new Promise(resolve => app.server.close(resolve)); app.store.close(); rmSync(dir, { recursive: true, force: true }); });
  const base = 'http://127.0.0.1:' + app.server.address().port;
  app.engine.open(start); const before = database(app.store);
  const options = { headers: { 'X-Z-Memory': '1' } };
  for (const path of ['/memories', '/memories.js', '/memories.css']) assert.equal((await fetch(base + path)).status, 200);
  assert.equal((await fetch(base + '/api/debug/memories')).status, 403);
  assert.equal((await fetch(base + '/api/debug/memories', { headers: { ...options.headers, Origin: 'https://elsewhere.example' } })).status, 403);
  const response = await fetch(base + '/api/debug/memories', options), result = await response.json();
  assert.equal(response.status, 200); assert.match(response.headers.get('cache-control'), /no-store/);
  assert.equal(result.actorId, 'c01'); assert.ok(result.memories.length);
  assert.equal((await fetch(base + '/api/debug/memories?actorId=unknown', options)).status, 400);
  assert.equal(database(app.store), before); assert.equal(provider.calls.length, 0);
});
