import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createServer } from '../server.mjs';

test('game open accepts a large saved ending checkpoint while ordinary writes retain the smaller limit', async t => {
  const provider = { publicConfig: () => ({ configured: true }), config: {}, request: value => value,
    call: async () => { throw Error('Model call is not expected'); } };
  const app = createServer({ dbPath: ':memory:', providerFactory: () => provider, backgroundEnabled: false });
  app.server.listen(0, '127.0.0.1'); await once(app.server, 'listening');
  t.after(async () => { app.server.closeAllConnections(); await new Promise(resolve => app.server.close(resolve)); app.store.close(); });
  const address = `http://127.0.0.1:${app.server.address().port}`;
  const post = async (path, body) => {
    const response = await fetch(address + path, { method: 'POST', headers: {
      'Content-Type': 'application/json', 'X-Z-Memory': '1' }, body: JSON.stringify(body) });
    return { status: response.status, data: await response.json() };
  };
  const base = { worldId: 'large_checkpoint', runId: 'first', day: 1, period: 0 };
  const opened = await post('/api/game/open', base); assert.equal(opened.status, 200, opened.data.error);
  const checkpoint = JSON.parse(opened.data.checkpoint);
  checkpoint.world.seventhNightEndings = { saved: { result: { sourceText: 'x'.repeat(8_100_000) } } };
  const request = { ...base, runId: 'restored', checkpoint: JSON.stringify(checkpoint) };
  assert.ok(Buffer.byteLength(JSON.stringify(request)) > 8_000_000);
  assert.ok(Buffer.byteLength(JSON.stringify(request)) < 12_000_000);
  const restored = await post('/api/game/open', request);
  assert.equal(restored.status, 200, restored.data.error);
  const ordinary = await post('/api/game/focus', { ...base, runId: 'restored', padding: 'x'.repeat(8_100_000) });
  assert.equal(ordinary.status, 400); assert.equal(ordinary.data.error, '请求过大');
});
