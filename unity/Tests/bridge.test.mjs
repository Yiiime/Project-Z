import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import http from 'node:http';
import { configuration, createBridge, modelResponder, validateRequest, validateResponse } from '../Tools/character-bridge.mjs';

const request = () => ({ requestId: 'a'.repeat(32), slotId: 'b'.repeat(32), revision: 1, actorId: 'z', phase: 'choice',
  playerText: '', authoredText: '一起看看潮水。', allowedActions: ['look', 'nod'], observations: [
    { id: 'c'.repeat(32), actorId: 'z', type: 'choice', fact: '玩家和 Z 一起看了潮水。', day: 1 } ] });
const output = () => ({ text: '那片叶子还在这里。', emotion: 'warm', action: 'nod', sourceEventIds: ['c'.repeat(32)] });
async function usingBridge(options, run) {
  const server = createBridge(options);
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const address = `http://127.0.0.1:${server.address().port}`;
  try { await run(address, server); }
  finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
}
const post = (url, body = request(), headers = {}) => fetch(url + '/character', { method: 'POST',
  headers: { 'Content-Type': 'application/json', ...headers }, body: JSON.stringify(body) });

test('valid response carries engine identity and is marked live', () => {
  const response = validateResponse(request(), output());
  assert.equal(response.mode, 'live'); assert.equal(response.slotId, request().slotId);
  assert.equal(response.revision, 1);
});
test('request rejects malformed IDs, actors and observation sources', () => {
  for (const change of [r => r.slotId = 'other', r => r.actorId = 'npc', r => r.observations[0].type = 'rumor',
    r => r.allowedActions = ['teleport'], r => r.playerText = 'x'.repeat(501)]) {
    const r = request(); change(r); assert.throws(() => validateRequest(r));
  }
});
test('invented sources and unsupported actions are rejected', () => {
  const invalid = output(); invalid.sourceEventIds = ['d'.repeat(32)]; assert.throws(() => validateResponse(request(), invalid));
  invalid.sourceEventIds = []; invalid.action = 'delete'; assert.throws(() => validateResponse(request(), invalid));
});
test('followup requires its specific source', () => {
  const r = request(); r.phase = 'followup'; const o = output(); o.sourceEventIds = [];
  assert.throws(() => validateResponse(r, o)); r.observations = []; assert.throws(() => validateRequest(r));
});
test('official endpoint configuration rejects credential-bearing or arbitrary destinations', () => {
  for (const url of ['https://example.com', 'https://api.deepseek.com.evil.test', 'http://api.deepseek.com',
    'https://user@api.deepseek.com', 'https://api.deepseek.com?key=test'])
    assert.throws(() => configuration({ Z_MODEL_BASE_URL: url }));
  assert.equal(configuration({ Z_MODEL_BASE_URL: 'https://ark.cn-beijing.volces.com/api/v3' }).endpoint,
    'https://ark.cn-beijing.volces.com/api/v3/chat/completions');
});
test('health reports unconfigured mode without exposing secrets', async () => {
  await usingBridge({}, async url => {
    const health = await (await fetch(url + '/health')).json(); assert.equal(health.configured, false);
    assert.deepEqual(Object.keys(health).sort(), ['configured', 'ok', 'protocol']);
    assert.equal((await post(url)).status, 503);
  });
});
test('browser origins and unexpected host headers are refused', async () => {
  await usingBridge({ respond: async () => output() }, async url => {
    assert.equal((await post(url, request(), { Origin: 'https://other.test' })).status, 403);
    const status = await new Promise((resolve, reject) => {
      const req = http.request(url + '/character', { method: 'POST', headers: { Host: 'other.test', 'Content-Type': 'application/json' } }, res => {
        res.resume(); res.on('end', () => resolve(res.statusCode));
      });
      req.on('error', reject); req.end(JSON.stringify(request()));
    });
    assert.equal(status, 403);
  });
});
test('duplicate requests are coalesced and conflicting reuse is refused', async () => {
  let calls = 0;
  await usingBridge({ respond: async () => { calls++; await new Promise(r => setTimeout(r, 15)); return output(); } }, async url => {
    const responses = await Promise.all([post(url), post(url)]);
    assert.equal(responses[0].status, 200); assert.equal(responses[1].status, 200); assert.equal(calls, 1);
    const changed = request(); changed.playerText = 'different'; assert.equal((await post(url, changed)).status, 409);
  });
});
test('failed calls can be retried without silent authored fallback', async () => {
  let calls = 0;
  await usingBridge({ respond: async () => { if (++calls === 1) throw new Error('private data'); return output(); } }, async url => {
    const failed = await post(url); assert.equal(failed.status, 500); assert.doesNotMatch(await failed.text(), /private data|authored/);
    assert.equal((await post(url)).status, 200); assert.equal(calls, 2);
  });
});
test('oversized bodies are rejected', async () => {
  await usingBridge({}, async url => {
    assert.equal((await post(url, { padding: 'x'.repeat(70000) })).status, 413);
  });
});
test('UTF-8 split between network chunks retains Chinese text', async () => {
  let received;
  await usingBridge({ respond: async r => { received = r.authoredText; return output(); } }, async url => {
    const bytes = Buffer.from(JSON.stringify(request())); const offset = bytes.indexOf(Buffer.from('一')) + 1;
    await new Promise((resolve, reject) => {
      const req = http.request(url + '/character', { method: 'POST', headers: { 'Content-Type': 'application/json' } }, res => {
        assert.equal(res.statusCode, 200); res.resume(); res.on('end', resolve);
      });
      req.on('error', reject); req.write(bytes.subarray(0, offset)); setTimeout(() => req.end(bytes.subarray(offset)), 5);
    });
    assert.equal(received, request().authoredText);
  });
});
test('provider adapter uses JSON contract and never reflects provider errors or credentials', async () => {
  let sent;
  const respond = modelResponder({ endpoint: 'https://api.deepseek.com/chat/completions', key: 'test-key', model: 'test-model' },
    async (url, init) => { sent = JSON.parse(init.body); return { ok: true, json: async () => ({ choices: [{ finish_reason: 'stop',
      message: { content: JSON.stringify(output()) } }] }) }; });
  assert.deepEqual(await respond(request()), output()); assert.equal(sent.response_format.type, 'json_object');
  const fail = modelResponder({ endpoint: '', key: 'test-key', model: 'test' }, async () => ({ ok: false, status: 401 }));
  await assert.rejects(fail(request()), error => /401/.test(error.message) && !error.message.includes('test-key'));
});
test('provider truncation and missing configuration fail visibly', async () => {
  await assert.rejects(modelResponder({ model: '', key: '' })(request()));
  const truncated = modelResponder({ endpoint: '', key: 'x', model: 'x' }, async () => ({ ok: true,
    json: async () => ({ choices: [{ finish_reason: 'length', message: { content: '{}' } }] }) }));
  await assert.rejects(truncated(request()));
});
