import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createServer } from '../server.mjs';
import { authoredFile, GameEndingCoordinator } from '../lib/ending.mjs';
import { readFile, writeFile, mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

async function fixture(t, hook) {
  const requests = [];
  const provider = { publicConfig: () => ({ configured: true }), config: { topK: 6 },
    request: messages => messages,
    call: async messages => {
      requests.push(messages); if (hook) await hook(messages);
      const input = JSON.parse(messages[1].content);
      if (input.dailyPage) return { text: JSON.stringify({ dailyPages: [{ day: input.day, title: `第${input.day}天`, text: '一天里发生了一件具体的事。\n我把当时看到的细节记在这里。\n\n我还在慢慢想。' }] }) };
      if (input.choice) return { text: JSON.stringify({ recollections: input.memories.map(m => ({ memoryId: m.id, text: `我记得${m.title}。${m.text}` })) }) };
      return { text: JSON.stringify({ choice: 'leave', title: '带着这段日子', opening: '我记得我们说过的话。', reason: '我想自己去看看。', reasonMemoryIds: [], decision: '我选择离开，执行结果还要确认。', lastLine: '这一步，让我自己来。' }) };
    } };
  const app = createServer({ dbPath: ':memory:', providerFactory: () => provider, backgroundEnabled: false, endingScenarioPath: authoredFile });
  app.server.listen(0, '127.0.0.1'); await once(app.server, 'listening');
  const address = `http://127.0.0.1:${app.server.address().port}`;
  t.after(async () => { app.server.closeAllConnections(); await new Promise(r => app.server.close(r)); app.store.close(); });
  const post = async (path, body) => {
    const response = await fetch(address + '/api/game/' + path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Z-Memory': '1' }, body: JSON.stringify(body) });
    return { status: response.status, data: await response.json() };
  };
  const base = { worldId: 'ending_game', runId: 'play1', day: 1, period: 0 };
  await post('open', base);
  return { app, requests, post, base };
}
test('game-native ending uses current Z-visible memories and same provider, without exposing private NPC talk', async t => {
  const { app, requests, post, base } = await fixture(t);
  await post('sync', { ...base, eventData: { id: 'ours', seq: 1, day: 1, period: 0, kind: 'utterance', text: 'LIVE_PLAYER_MEMORY', speakerId: 'player', recipientId: 'z', conversationId: 'ours', observers: ['player', 'z'], discloseTo: ['player', 'z'], locationId: 'dock' } });
  await post('sync', { ...base, eventData: { id: 'private', seq: 2, day: 1, period: 0, kind: 'utterance', text: 'PRIVATE_NPC_SECRET', speakerId: 'liting', recipientId: 'z', conversationId: 'secret', observers: ['liting', 'z'], discloseTo: ['liting', 'z'], locationId: 'cafe' } });
  const before = structuredClone(app.engine.world(base.worldId));
  const result = await post('ending', { ...base, requestId: 'ending1', details: { mode: 'model' } });
  assert.equal(result.status, 200, result.data.error);
  assert.equal(result.data.ending.liveMemoryCount, 1);
  assert.equal(result.data.ending.worldId, base.worldId);
  assert.ok(JSON.stringify(requests).includes('LIVE_PLAYER_MEMORY'));
  assert.ok(!JSON.stringify(requests).includes('PRIVATE_NPC_SECRET'));
  assert.equal(result.data.ending.memoryCount, result.data.ending.recollections.length);
  assert.equal(result.data.ending.outcome.status, 'pending');
  const after = app.engine.world(base.worldId);
  assert.equal(after.day, before.day); assert.equal(after.seq, before.seq);
  assert.deepEqual(after.actors, before.actors);
  const checkpoint = JSON.parse(result.data.checkpoint);
  assert.equal(checkpoint.world.seventhNightEndings.ending1.result.id, result.data.ending.id);
});
test('repeated native ending request returns same result and checkpoints survive save reload', async t => {
  const { requests, post, base } = await fixture(t);
  const request = { ...base, requestId: 'same', details: { mode: 'model' } };
  const first = await post('ending', request), count = requests.length;
  const second = await post('ending', request);
  assert.equal(second.data.ending.id, first.data.ending.id); assert.equal(requests.length, count);
  const loaded = await post('open', { ...base, runId: 'play2', checkpoint: first.data.checkpoint });
  assert.equal(loaded.status, 200, loaded.data.error);
  assert.equal(JSON.parse(loaded.data.checkpoint).world.seventhNightEndings.same.result.id, first.data.ending.id);
  const wrong = await post('ending', { ...base, runId: 'play2', requestId: 'same', details: { mode: 'preview-stay' } });
  assert.equal(wrong.status, 400);
});
test('late ending after a timeline reload cannot write into the new game', async t => {
  let release, entered;
  const ready = new Promise(r => entered = r), wait = new Promise(r => release = r);
  let first = true;
  const { app, post, base } = await fixture(t, async () => { if (first) { first = false; entered(); await wait; } });
  const pending = post('ending', { ...base, requestId: 'late', details: { mode: 'model' } });
  await ready;
  await post('open', { ...base, runId: 'new-play' }); release();
  const result = await pending; assert.equal(result.status, 400);
  assert.ok(!app.engine.world(base.worldId).seventhNightEndings?.late);
});
test('main game reads the same selected file as the authoring upload', async t => {
  const dir=await mkdtemp(join(tmpdir(),'z-ending-selection-'));t.after(()=>rm(dir,{recursive:true,force:true}));
  const scenario=JSON.parse(await readFile(authoredFile,'utf8'));scenario.title='上传后被主游戏选中的故事';
  const selected=join(dir,'scenario.json'),selection=join(dir,'source-selection.json');
  await writeFile(selected,JSON.stringify(scenario));await writeFile(selection,JSON.stringify({scenarioPath:selected}));
  const coordinator=new GameEndingCoordinator(null,null,null,{scenarioPath:null,selectionPath:selection});
  assert.equal(coordinator.authored().title,scenario.title);
  const explicit=new GameEndingCoordinator(null,null,null,{scenarioPath:authoredFile,selectionPath:selection});
  assert.notEqual(explicit.authored().title,scenario.title);
});
