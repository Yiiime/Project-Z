/** Offline regression for native Worker fetch receivers and secret-safe diagnostics. */
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { webcrypto, randomUUID } from 'node:crypto';
const source = fs.readFileSync(new URL('../worker/deepseek-chat.js', import.meta.url), 'utf8')
  .replace("from './z-persona.js'", `from ${JSON.stringify(new URL('../worker/z-persona.js', import.meta.url).href)}`);
let moduleId = 0;
const load = async () => (await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}#${++moduleId}`)).handleChat;
const SECRET = 'fixture-only-secret-not-for-logs';
const TEXT = '这句测试消息不可以出现在诊断日志中。';
const env = { DEEPSEEK_API_KEY: SECRET };
const req = () => new Request('https://z.example.test/api/chat', { method: 'POST', headers: {
  'oai-authenticated-user-id': 'fixture-user', Origin: 'https://z.example.test', 'Content-Type': 'application/json', 'X-User-Secret': 'never-forward',
}, body: JSON.stringify({ requestId: randomUUID(), kind: 'chat', message: TEXT }) });
const success = () => Response.json({ choices: [{ finish_reason: 'stop', message: { content: JSON.stringify({ text: '收到你的心事，我们一件一件慢慢说。', mood: 'warm' }) } }] });
const originalFetch = globalThis.fetch;
const records = [];
const opts = { crypto: webcrypto, diagnostics: record => records.push(record) };
try {
  const handle = await load(); let calls = 0;
  globalThis.fetch = function (url, init) {
    assert.equal(this, globalThis, 'Worker-native fetch must keep its global owner');
    if (init.redirect === 'error') throw new TypeError('Invalid redirect value. Must be one of follow or manual.');
    assert.equal(init.redirect, 'manual'); assert.equal(url, 'https://api.deepseek.com/chat/completions');
    assert.equal(init.headers.Authorization, `Bearer ${SECRET}`); assert.equal(init.headers['X-User-Secret'], undefined); assert.equal(init.headers['oai-authenticated-user-id'], undefined);
    assert.deepEqual(JSON.parse(init.body).thinking, { type: 'disabled' }); calls++; return Promise.resolve(success());
  };
  assert.equal((await handle(req(), env, opts)).status, 200); assert.equal(calls, 1);
  console.log('PASS native global fetch receiver, Worker-compatible manual redirect and fixed text-only provider request');

  const injected = await load(); let injectedCalls = 0;
  globalThis.fetch = () => { throw new Error('Default fetch must not be used with injection'); };
  const mock = function () { assert.notEqual(this, globalThis, 'Injected fetch is not rebound'); injectedCalls++; return Promise.resolve(success()); };
  assert.equal((await injected(req(), env, { ...opts, fetch: mock })).status, 200); assert.equal(injectedCalls, 1);
  console.log('PASS injected fetch remains independent of native receiver binding');

  for (const [upstream, code, classification] of [
    [401, 'provider_configuration_error', 'configuration_error'], [403, 'provider_configuration_error', 'configuration_error'],
    [400, 'provider_request_rejected', 'request_rejected'], [422, 'provider_request_rejected', 'request_rejected'],
    [402, 'provider_insufficient_balance', 'insufficient_balance'],
  ]) {
    const failure = await load(); records.length = 0;
    const result = await failure(req(), env, { ...opts, fetch: async () => Response.json({ error: `${SECRET} ${TEXT} https://private.example` }, { status: upstream }) });
    assert.equal((await result.json()).error, code); assert.deepEqual(records, [{ classification, status: upstream, operation: 'chat' }]);
  }
  const broken = await load(); records.length = 0;
  await broken(req(), env, { ...opts, fetch: () => { throw new TypeError(`Illegal invocation ${SECRET} ${TEXT} https://private.example`); } });
  assert.deepEqual(records, [{ classification: 'fetch_receiver_error', status: 0, operation: 'chat' }]);
  assert(!JSON.stringify(records).includes(SECRET)); assert(!JSON.stringify(records).includes(TEXT)); assert(!JSON.stringify(records).includes('https://'));
  console.log('PASS diagnostics use only enumerated classification, numeric status and operation; no provider exceptions or bodies');

  const redirects = await load(); records.length = 0; const urls = [];
  const result = await redirects(req(), env, { ...opts, fetch: async (url, init) => {
    urls.push(url); assert.equal(init.redirect, 'manual'); assert(!url.includes('evil.example'));
    return new Response(null, { status: 307, headers: { Location: 'https://evil.example/steal' } });
  } });
  assert.equal(result.status, 502); assert.equal(urls.length, 1); assert.deepEqual(records, [{ classification: 'redirect_error', status: 307, operation: 'chat' }]);
  console.log('PASS redirects never forward credentials or follow a caller/provider-selected host');
} finally { globalThis.fetch = originalFetch; }
console.log('\n4 Worker fetch regression scenarios passed (offline DeepSeek mocks, no paid calls).');
