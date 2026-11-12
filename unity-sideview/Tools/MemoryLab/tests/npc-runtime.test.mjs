import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { Store } from '../lib/store.mjs';
import { Catalog } from '../lib/catalog.mjs';
import { Engine } from '../lib/engine.mjs';
import { createServer } from '../server.mjs';

const snapshot = (locationId = 'dock', extra = {}) => ({
  sceneId: 'Street', locationId, x: 6, y: 0, movementState: 'idle',
  actionId: null, targetLocationId: null, conversationId: null, reason: 'spawned', ...extra,
});
function make(t) {
  const store = new Store();
  const engine = new Engine(store, new Catalog(), { publicConfig: () => ({ configured: false }) });
  engine.create('w'); t.after(() => store.close()); return engine;
}
function reply(c, type = 'wait', target) {
  const action = c.available_actions.find(a => a.type === type && (!target || a.target === target));
  assert(action);
  return { dialogue: c.interaction.mode === 'player' ? '我知道了。' : '',
    action: { action_id: action.id, type: action.type, target: action.target, parameters: action.parameters, description: '按本人实际状态决定' },
    info: { ...structuredClone(c.character_state.info), changes: {}, memory_updates: [], knowledge_updates: [] } };
}
const entries = (e, actor) => e.inspectRuntime('w', actor, { limit: 200 }).entries;

test('all eight NPCs have independent ordered histories and private events go only to recipients', t => {
  const e = make(t);
  for (const actor of e.catalog.actors.keys()) {
    assert.equal(entries(e, actor).length, 1); assert.equal(entries(e, actor)[0].runtime_seq, 1);
  }
  const untouched = entries(e, 'z');
  const secret = e.observe('w', '只有李庭知道的私密安排', ['liting'], { wake: false });
  assert(entries(e, 'liting').some(r => r.source_id === secret.id));
  assert.deepEqual(entries(e, 'z'), untouched);
  assert(!JSON.stringify(e.inspectRuntime('w', 'z')).includes('只有李庭知道'));
  assert.throws(() => e.inspectRuntime('w', 'player'));
});

test('environment inputs use per-NPC sequences, deduplicate retries and do not become memories', t => {
  const e = make(t), w = e.world('w');
  const before = e.records(w, 'event').length;
  const meta = { id: 'host_a', hostSeq: 1, day: 1, period: 0 };
  e.updateEnvironment('w', 'linxu', snapshot(), meta);
  e.updateEnvironment('w', 'z', snapshot('z_home'), { ...meta, id: 'host_b' });
  assert.equal(e.actor('w', 'linxu').runtime_input_seq, 1);
  assert.equal(e.actor('w', 'z').runtime_input_seq, 1);
  assert.equal(e.world('w').seq, 0); assert.equal(e.records(e.world('w'), 'event').length, before);
  const journal = entries(e, 'linxu');
  e.updateEnvironment('w', 'linxu', snapshot(), meta);
  assert.deepEqual(entries(e, 'linxu'), journal);
  assert.throws(() => e.updateEnvironment('w', 'linxu', snapshot('visitor_center'), meta), /冲突/);
  assert.throws(() => e.updateEnvironment('w', 'linxu', snapshot(), { ...meta, id: 'gap', hostSeq: 3 }), /序号/);
  assert.throws(() => e.updateEnvironment('w', 'linxu', snapshot('unknown')), /环境/);
});

test('a NPC decision retains its input, proposal, actual commit and job outcome in its own history', async t => {
  const e = make(t), other = entries(e, 'axing');
  e.decide = reply;
  const j = e.player('w', 'z', '你现在在哪里？');
  const [r] = await e.run('w', 1, j.id);
  assert.equal(r.status, 'proposed');
  assert(entries(e, 'z').some(r => r.type === 'decision.requested' && r.source_id === j.id));
  assert(entries(e, 'z').some(r => r.type === 'proposal.pending'));
  e.commit('w', r.proposalId);
  assert(entries(e, 'z').some(r => r.type === 'proposal.committed'));
  assert(entries(e, 'z').some(r => r.type === 'job.succeeded'));
  assert.deepEqual(entries(e, 'axing'), other);
});

test('authored Harbor and unbound forest scenes report actual whereabouts without pretending to be the main street', t => {
  const e = make(t);
  e.updateEnvironment('w', 'linxu', snapshot('dock', { sceneId: 'Harbor' }));
  assert.equal(e.actor('w', 'linxu').environment.sceneId, 'Harbor');
  e.updateEnvironment('w', 'linxu', snapshot('scene:Forest', { sceneId: 'Forest', locationName: '林间小路' }));
  const context = e.context(e.player('w', 'linxu', '你在哪？'));
  assert.equal(context.environment.map_context.current_place.name, '林间小路');
  assert.equal(context.environment.scene, 'scene:Forest');
  assert(!context.available_actions.some(a => a.type === 'move'));
});

test('environment changes fence this NPC late reply while other NPC changes and small coordinate updates do not', async t => {
  const e = make(t);
  e.updateEnvironment('w', 'z', snapshot('z_home'));
  let resume;
  e.decide = c => new Promise(resolve => { resume = () => resolve(reply(c)); });
  let j = e.player('w', 'z', '你好'), task = e.run('w', 1, j.id);
  e.updateEnvironment('w', 'linxu', snapshot('visitor_center'));
  e.updateEnvironment('w', 'z', snapshot('z_home', { x: 3.45 }));
  resume(); let [r] = await task; assert.equal(r.status, 'proposed'); e.commit('w', r.proposalId);
  j = e.player('w', 'z', '再问一次'); task = e.run('w', 1, j.id);
  e.updateEnvironment('w', 'z', snapshot('visitor_center')); resume();
  [r] = await task; assert.equal(r.status, 'cancelled');
  assert(entries(e, 'z').some(r => r.type === 'job.cancelled'));
});

test('physical reports cannot complete a committed move or replace its verified route receipt', async t => {
  const e = make(t); e.decide = c => reply(c, 'move', 'visitor_center');
  const j = e.player('w', 'linxu', '去游客中心');
  const [r] = await e.run('w', 1, j.id); e.commit('w', r.proposalId);
  const id = e.actor('w', 'linxu').pending_action;
  e.updateEnvironment('w', 'linxu', snapshot('visitor_center', { movementState: 'arrived', actionId: id }));
  assert.equal(e.actor('w', 'linxu').scene, 'dock');
  assert.equal(e.actor('w', 'linxu').pending_action, id);
  const c = e.context(e.player('w', 'linxu', '到了吗？'));
  assert.equal(c.environment.scene, 'visitor_center');
  assert.equal(c.environment.map_context.current_place.name, '游客中心');
  assert.throws(() => e.actionReceipt('w', id, true, { scene: 'cafe' }));
  e.actionReceipt('w', id, true, { scene: 'visitor_center' });
  assert.equal(e.actor('w', 'linxu').pending_action, null);
  assert(entries(e, 'linxu').some(r => r.type === 'action.succeeded'));
});

test('NPC runtime history, independent cursors and environments roll back with the checkpoint', t => {
  const e = make(t), meta = { id: 'original', hostSeq: 1, day: 1, period: 0 };
  e.updateEnvironment('w', 'linxu', snapshot(), meta);
  const cp = e.checkpoint('w'), history = entries(e, 'linxu');
  e.updateEnvironment('w', 'linxu', snapshot('visitor_center'), { ...meta, id: 'future', hostSeq: 2 });
  e.open('w', 'restored', cp);
  assert.equal(e.actor('w', 'linxu').runtime_input_seq, 1);
  assert.equal(e.actor('w', 'linxu').environment.locationId, 'dock');
  const restored = entries(e, 'linxu');
  assert.deepEqual(restored.slice(0, history.length).map(r => r.type), history.map(r => r.type));
  assert(!restored.some(r => r.source_id === 'future'));
  assert.equal(restored.at(-1).runtime_seq, history.at(-1).runtime_seq + 1);
  const count = restored.length; e.updateEnvironment('w', 'linxu', snapshot(), meta);
  assert.equal(entries(e, 'linxu').length, count);
  const first = e.inspectRuntime('w', 'linxu', { limit: 2 });
  const next = e.inspectRuntime('w', 'linxu', { after: first.next_cursor, limit: 2 });
  assert(first.has_more); assert(next.entries.every(r => r.runtime_seq > first.next_cursor));
  assert.throws(() => e.inspectRuntime('w', 'linxu', { limit: 500 }));
});

test('legacy checkpoints open with a new operational history without inventing old decisions', t => {
  const e = make(t), cp = e.checkpoint('w');
  delete cp.records.npc_run; delete cp.records.npc_environment;
  delete cp.npcRuntimeArchive;
  e.open('w', 'legacy', cp);
  assert.equal(entries(e, 'z').length, 1);
  assert.equal(entries(e, 'z')[0].type, 'runtime.opened');
});

test('runtime archives are compact and damaged archives cannot mutate the active world', t => {
  const e = make(t);
  for(let seq=1;seq<=30;seq++)e.updateEnvironment('w','linxu',snapshot(),{id:'repeat_'+seq,hostSeq:seq,day:1,period:0});
  const cp=e.checkpoint('w');
  assert(cp.npcRuntimeArchive.data.length<JSON.stringify(entries(e,'linxu')).length/2);
  const run=e.world('w').runId;
  cp.npcRuntimeArchive.data='invalid';
  assert.throws(()=>e.open('w','corrupt',cp),/存档/);
  assert.equal(e.world('w').runId,run);
});

test('native runtime sync and observation API isolate actors, enforce run identity and preserve retry semantics', async t => {
  const app = createServer({ dbPath: ':memory:', backgroundEnabled: false, decide: reply,
    providerFactory: () => ({ publicConfig: () => ({ configured: true }), config: { topK: 6 } }) });
  app.server.listen(0, '127.0.0.1'); await once(app.server, 'listening');
  t.after(async () => { await new Promise(resolve => app.server.close(resolve)); app.store.close(); });
  const base = { worldId: 'native', runId: 'r1', day: 1, period: 0 };
  const origin = 'http://127.0.0.1:' + app.server.address().port;
  const post = async (path, extra) => {
    const r = await fetch(origin + '/api/game/' + path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Z-Memory': '1' }, body: JSON.stringify({ ...base, ...extra }) });
    return { code: r.status, body: await r.json() };
  };
  assert.equal((await post('open')).code, 200);
  const input = actorId => ({ actorId, runtimeEvent: { actorId, id: 'host_' + actorId, seq: 1, day: 1, period: 0, snapshot: snapshot(actorId === 'z' ? 'z_home' : 'dock') } });
  const a = await post('runtime/sync', input('linxu')), b = await post('runtime/sync', input('z'));
  assert.equal(a.code, 200, a.body.error); assert.equal(b.code, 200, b.body.error);
  assert.equal(a.body.runtimeSeq, 1); assert.equal(b.body.runtimeSeq, 1);
  assert.equal(a.body.seq, 0);
  const inspected = await post('runtime', { actorId: 'linxu' });
  assert.equal(inspected.code, 200); assert(inspected.body.entries.every(r => r.actor === 'linxu'));
  const count = inspected.body.entries.length;
  assert.equal((await post('runtime/sync', input('linxu'))).code, 200);
  assert.equal((await post('runtime', { actorId: 'linxu' })).body.entries.length, count);
  assert.equal((await post('runtime/sync', { ...input('linxu'), actorId: 'z' })).code, 400);
  assert.equal((await post('runtime/sync', { ...input('linxu'), runId: 'old' })).code, 400);
  assert.equal((await post('intent', { actorId: 'linxu', candidates: ['z'], runtimeSeq: 0, runtimeSnapshot: snapshot() })).code, 400);
  const url = origin + '/api/debug/runtimes?worldId=native&actorId=linxu';
  assert.equal((await fetch(url)).status, 400);
  assert.equal((await fetch(url, { headers: { 'X-Test-Token': app.token } })).status, 200);
});
