/** Offline Vivi SSE adapter tests. No credentials or paid provider calls. */
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { webcrypto, randomUUID } from 'node:crypto';

const source = (await fs.readFile(new URL('../worker/volc-tts.js', import.meta.url), 'utf8')).replace("'../ui/talent-config.js'",JSON.stringify(new URL('../ui/talent-config.js',import.meta.url).href));
let moduleId = 0, checks = 0;
const load = async () => (await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}#${++moduleId}`)).handleTts;
const ORIGIN = 'https://z.example.test';
const SECRET = 'offline-fixture-never-a-real-key';
const env = { VOLC_TTS_API_KEY: SECRET };
const sentence = { text: '你好，Z。', phonemes: ['not returned'], words: [
  { word: '你', startTime: 0.1, endTime: 0.25, confidence: 0.9 },
  { word: '好，', startTime: 0.25, endTime: 0.5, confidence: 0.8 },
  { word: 'Z。', startTime: 0.6, endTime: 1.2 },
] };
const packets = () => [{ code: 0, data: 'SUQzBA==' }, { code: 0, data: 'AQID' }, { code: 0, data: null, sentence }, { code: 20000000, data: null, usage: { text_words: 5 } }];
const input = (patch = {}) => ({ requestId: randomUUID(), text: '你好，Z。', mood: 'happy', ...patch });
function request(value = input(), options = {}) {
  const headers = new Headers({ Origin: ORIGIN, 'Content-Type': 'application/json', 'oai-authenticated-user-id': options.user || 'alice', ...(options.headers || {}) });
  for (const key of options.removeHeaders || []) headers.delete(key);
  const method = options.method || 'POST';
  return new Request(ORIGIN + (options.path || '/api/tts'), { method, headers, signal: options.signal,
    ...(!['GET', 'HEAD'].includes(method) ? { body: options.raw ?? JSON.stringify(value) } : {}) });
}
function sse(values = packets(), options = {}) {
  const text = options.text ?? values.map((value, index) => `event: ${index === values.length - 1 ? 152 : 352}\r\ndata: ${JSON.stringify(value)}\r\n\r\n`).join('');
  const bytes = new TextEncoder().encode(text);
  return new Response(new ReadableStream({
    start(controller) {
      const size = options.chunk || bytes.length;
      for (let offset = 0; offset < bytes.length; offset += size) controller.enqueue(bytes.slice(offset, offset + size));
      if (!options.hold) controller.close();
    }, cancel() { options.onCancel?.(); },
  }), { headers: { 'Content-Type': options.contentType || 'text/event-stream; charset=utf-8' } });
}
const opts = fetch => ({ fetch, crypto: webcrypto });
const frames = async response => (await response.text()).trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
async function check(name, fn) { await fn(); checks++; console.log('PASS ' + name); }

await check('readonly config needs no identity, reports fixed Vivi settings and reads no legacy secret', async () => {
  const handle = await load(); let calls = 0;
  const guarded = new Proxy({}, { get(_, key) { assert.equal(key, 'VOLC_TTS_API_KEY'); return undefined; } });
  const response = await handle(request(null, { path: '/api/tts/config', method: 'GET', removeHeaders: ['oai-authenticated-user-id'] }), guarded, opts(() => { calls++; throw Error(); }));
  assert.equal(response.status, 200); assert.deepEqual(await response.json(), { enabled: false, provider: 'Volcengine', voice: 'Vivi 2.0', speaker: 'zh_female_vv_uranus_bigtts', format: 'mp3', maxTextLength: 500 });
  assert.equal(calls, 0);
  assert.equal((await handle(request(), {}, opts(() => { calls++; }))).status, 503); assert.equal(calls, 0);
  const enabled = await handle(request(null, { path: '/api/tts/config', method: 'GET' }), env); assert.equal((await enabled.json()).enabled, true);
});

await check('fixed official SSE request protects auth, mood instructions and exact TTS2 subtitle parameters', async () => {
  const handle = await load(); let calls = 0; const value = input();
  const response = await handle(request(value, { headers: { 'X-User-Secret': 'not-forwarded' } }), env, opts(async (url, init) => {
    calls++; assert.equal(url, 'https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse'); assert.equal(init.method, 'POST'); assert.equal(init.redirect, 'manual');
    assert.equal(init.headers['X-Api-Key'], SECRET); assert.equal(init.headers['X-Api-Resource-Id'], 'seed-tts-2.0'); assert.equal(init.headers['X-Api-Request-Id'], value.requestId);
    assert.equal(init.headers['oai-authenticated-user-id'], undefined); assert.equal(init.headers['X-User-Secret'], undefined);
    const body = JSON.parse(init.body); assert.match(body.user.uid, /^z_[0-9a-f]{64}$/); assert(!body.user.uid.includes('alice'));
    assert.equal(body.req_params.text, value.text); assert.equal(body.req_params.speaker, 'zh_female_vv_uranus_bigtts');
    assert.deepEqual(body.req_params.audio_params, { format: 'mp3', sample_rate: 24000, enable_subtitle: true, speech_rate: 3 });
    assert.equal(body.req_params.audio_params.enable_timestamp, undefined); assert.equal(typeof body.req_params.additions, 'string');
    assert(JSON.parse(body.req_params.additions).context_texts[0].includes('有活力')); return sse(packets(), { chunk: 7 });
  }));
  assert.equal(response.status, 200); assert.match(response.headers.get('Content-Type'), /application\/x-ndjson/); assert.equal(response.headers.get('Cache-Control'), 'private, no-store');
  const result = await frames(response); assert.deepEqual(result.map(item => item.type), ['start', 'audio', 'audio', 'sentence', 'done']);
  assert(result.every(item => item.requestId === value.requestId)); assert.deepEqual(result.filter(item => item.type === 'audio').map(item => item.index), [0, 1]);
  assert.deepEqual(result[0], { type: 'start', requestId: value.requestId, format: 'mp3', sampleRate: 24000, source: 'volcengine', speaker: 'zh_female_vv_uranus_bigtts' });
  assert.deepEqual(result[3].sentence, { text: sentence.text, words: sentence.words }); assert.deepEqual(result[4].usage, { textWords: 5 });
  assert(!JSON.stringify(result).includes(SECRET)); assert.equal(calls, 1);
});

await check('native fetch is bound to its owner and cannot redirect the speech credential', async () => {
  const handle = await load(), old = globalThis.fetch;
  try {
    globalThis.fetch = function (url, init) { assert.equal(this, globalThis); assert.equal(init.redirect, 'manual'); return Promise.resolve(sse()); };
    assert.equal((await frames(await handle(request(), env, { crypto: webcrypto }))).at(-1).type, 'done');
    let calls = 0; const diagnostics = [];
    const result = await frames(await handle(request(), env, { ...opts(async (_, init) => { calls++; assert.equal(init.redirect, 'manual'); return new Response(null, { status: 307, headers: { Location: 'https://evil.test' } }); }), diagnostics: item => diagnostics.push(item) }));
    assert.equal(result.at(-1).type, 'error'); assert.equal(calls, 1); assert.deepEqual(diagnostics, [{ operation: 'tts', classification: 'redirect_error', status: 307 }]);
  } finally { globalThis.fetch = old; }
});

await check('invalid identity, origin, roles, endpoint overrides and oversized input reject before synthesis', async () => {
  const handle = await load(); let calls = 0; const settings = opts(async () => { calls++; return sse(); });
  for (const [value, options, status] of [
    [input(), { removeHeaders: ['oai-authenticated-user-id'] }, 401], [input(), { headers: { Origin: 'https://evil.test' } }, 403],
    [input(), { headers: { 'Content-Type': 'text/plain' } }, 415], [input(), { raw: '{bad' }, 400],
    [input({ text: '字'.repeat(501) }), {}, 400], [input({ text: '\u0000' }), {}, 400], [input({ text: ' ' }), {}, 400],
    [input({ mood: 'evil' }), {}, 400], [input({ requestId: 'same-id' }), {}, 400], [input({ endpoint: 'https://evil.test' }), {}, 400],
    [input({ speaker: 'other', resource: 'other', instructions: 'other' }), {}, 400], [input({ history: [{ role: 'system', content: 'other' }] }), {}, 400],
    [input(), { raw: 'x'.repeat(8193) }, 413], [input(), { headers: { 'Content-Length': '9000' } }, 413],
    [input(), { method: 'GET' }, 405], [input(), { path: '/api/tts/audio' }, 404],
  ]) assert.equal((await handle(request(value, options), env, settings)).status, status);
  assert.equal(calls, 0);
});

await check('same user UUID is rejected during and after generation, including failure, without paid retry', async () => {
  const handle = await load(); const value = input(); let calls = 0, resolve;
  const held = new Promise(done => { resolve = done; });
  const settings = opts(async () => { calls++; await held; return sse(); });
  const first = await handle(request(value), env, settings);
  assert.equal((await handle(request(value), env, settings)).status, 409);
  assert.equal((await handle(request({ ...value, text: '换一条' }), env, settings)).status, 409);
  resolve(); assert.equal((await frames(first)).at(-1).type, 'done');
  assert.equal((await handle(request(value), env, settings)).status, 409); assert.equal(calls, 1);
  assert.equal((await frames(await handle(request(value, { user: 'bob' }), env, settings))).at(-1).type, 'done'); assert.equal(calls, 2);
  const failed = input(); const badSettings = opts(async () => { calls++; return new Response('private', { status: 503 }); });
  assert.equal((await frames(await handle(request(failed), env, badSettings))).at(-1).type, 'error');
  assert.equal((await handle(request(failed), env, badSettings)).status, 409); assert.equal(calls, 3);
});

await check('provider HTTP/errors are safe NDJSON terminal frames and never leak bodies or exceptions', async () => {
  for (const [status, code] of [[401, 'tts_provider_configuration_error'], [403, 'tts_provider_configuration_error'], [402, 'tts_provider_insufficient_balance'], [429, 'tts_provider_rate_limited'], [400, 'tts_provider_request_rejected'], [503, 'tts_provider_unavailable']]) {
    const handle = await load(); let calls = 0; const records = [];
    const result = await frames(await handle(request(), env, { ...opts(async () => { calls++; return new Response(`${SECRET} caller-private-text`, { status }); }), diagnostics: item => records.push(item) }));
    assert.deepEqual(result.map(item => item.type), ['start', 'error']); assert.equal(result[1].error, code); assert.equal(calls, 1);
    assert(!JSON.stringify({ result, records }).includes(SECRET)); assert(!JSON.stringify({ result, records }).includes('caller-private-text'));
  }
  const handle = await load(), records = [];
  const result = await frames(await handle(request(), env, { ...opts(() => { throw new TypeError(`Illegal invocation ${SECRET}`); }), diagnostics: item => records.push(item) }));
  assert.equal(result.at(-1).error, 'tts_provider_unavailable'); assert.deepEqual(records, [{ operation: 'tts', classification: 'fetch_receiver_error', status: 0 }]);
  const rejected = await frames(await handle(request(), env, opts(async () => sse([{ code: 45000000, message: SECRET }]))));
  assert.equal(rejected.at(-1).error, 'tts_provider_request_rejected'); assert(!JSON.stringify(rejected).includes(SECRET));
});

await check('success requires explicit final code plus audio; malformed/empty/truncated streams fail safely', async () => {
  for (const response of [
    sse([{ code: 0, data: 'AQID' }]), sse([{ code: 20000000 }]), sse([], { text: 'data: not-json\n\n' }),
    sse([{ code: 0, data: 'not-base64!' }]), sse([{ code: 0, data: 'AQID', sentence: { text: '错', words: [{ word: '错', startTime: 2, endTime: 1 }] } }]),
    sse(packets(), { contentType: 'application/json' }),
  ]) {
    const handle = await load(); const result = await frames(await handle(request(), env, opts(async () => response)));
    assert.equal(result.at(-1).type, 'error'); assert.equal(result.some(item => item.type === 'done'), false);
  }
});

await check('event, total wire, decoded audio and subtitle-word budgets are independently bounded', async () => {
  const hugeAudio = Array.from({ length: 4 }, () => ({ code: 0, data: 'A'.repeat(400000) }));
  const hugeWords = [{ code: 0, sentence: { text: '字', words: Array(2049).fill({ word: '字', startTime: 0, endTime: 1 }) } }];
  for (const response of [
    sse([], { text: 'data: ' + 'x'.repeat(524289) + '\n\n' }),
    sse([], { text: (':' + 'x'.repeat(400000) + '\n\n').repeat(11), chunk: 300000 }),
    sse(hugeAudio), sse(hugeWords),
  ]) {
    const handle = await load(); const result = await frames(await handle(request(), env, opts(async () => response)));
    assert.equal(result.at(-1).type, 'error'); assert(!result.some(item => item.type === 'done'));
  }
});

await check('timeout aborts an uncooperative fetch, releases capacity and ignores late provider results', async () => {
  const handle = await load(); let signal, resolve, calls = 0;
  const value = input(); const records = [];
  const settings = { ...opts((_, init) => { calls++; signal = init.signal; return new Promise(done => { resolve = done; }); }), timeoutMs: 5, diagnostics: item => records.push(item) };
  const result = await frames(await handle(request(value), env, settings));
  assert.equal(result.at(-1).error, 'tts_provider_timeout'); assert.equal(signal.aborted, true); assert.equal(calls, 1);
  assert.equal((await handle(request(value), env, settings)).status, 409);
  resolve(sse()); await new Promise(done => setTimeout(done, 2));
  assert.equal(result.length, 2); assert.deepEqual(records, [{ operation: 'tts', classification: 'timeout', status: 0 }]);
  assert.equal((await frames(await handle(request(), env, opts(async () => sse())))).at(-1).type, 'done');
});

await check('request abort and response-reader cancel both stop the provider and release concurrency', async () => {
  for (const mode of ['signal', 'reader']) {
    const handle = await load(); let upstreamSignal, canceled = 0;
    const client = new AbortController();
    const response = await handle(request(input(), { signal: client.signal }), env, opts(async (_, init) => { upstreamSignal = init.signal; return sse([{ code: 0, data: 'AQID' }], { hold: true, onCancel: () => canceled++ }); }));
    const reader = response.body.getReader(); assert.match(new TextDecoder().decode((await reader.read()).value), /"type":"start"/);
    await new Promise(done => setTimeout(done, 2));
    if (mode === 'signal') { client.abort(); while (!(await reader.read()).done) {} }
    else await reader.cancel();
    assert.equal(upstreamSignal.aborted, true); assert.equal(canceled, 1);
    assert.equal((await frames(await handle(request(), env, opts(async () => sse())))).at(-1).type, 'done');
  }
});

function pullDrivenSse() {
  const values = [...Array.from({ length: 3 }, () => ({ code: 0, data: 'A'.repeat(128000) })), { code: 20000000 }];
  const state = { pulls: 0, canceled: 0, signal: null };
  const response = new Response(new ReadableStream({
    pull(controller) {
      state.pulls++;
      const value = values.shift();
      if (!value) { controller.close(); return; }
      controller.enqueue(new TextEncoder().encode('data: ' + JSON.stringify(value) + '\n\n'));
    }, cancel() { state.canceled++; },
  }, { highWaterMark: 0 }), { headers: { 'Content-Type': 'text/event-stream' } });
  return { state, fetch: async (_, init) => { state.signal = init.signal; return response; } };
}
await check('slow downstream reads apply backpressure, resume in order, and retain only bounded frames', async () => {
  const handle = await load(), upstream = pullDrivenSse();
  const response = await handle(request(), env, opts(upstream.fetch));
  await new Promise(done => setTimeout(done, 5));
  assert.equal(upstream.state.pulls, 2, 'Only the queued audio plus one bounded in-flight event is read');
  const result = await frames(response);
  assert.deepEqual(result.map(item => item.type), ['start', 'audio', 'audio', 'audio', 'done']);
  assert.deepEqual(result.filter(item => item.type === 'audio').map(item => item.index), [0, 1, 2]);
  assert.equal(upstream.state.signal.aborted, true); assert.equal(upstream.state.canceled, 1);
});
await check('cancel and timeout unblock a producer waiting on downstream backpressure', async () => {
  for (const mode of ['cancel', 'timeout']) {
    const handle = await load(), upstream = pullDrivenSse();
    const response = await handle(request(), env, { ...opts(upstream.fetch), timeoutMs: mode === 'timeout' ? 15 : 20000 });
    await new Promise(done => setTimeout(done, mode === 'timeout' ? 20 : 5));
    if (mode === 'cancel') await response.body.cancel();
    else {
      const result = await frames(response);
      assert.equal(result.at(-1).error, 'tts_provider_timeout'); assert(!result.some(item => item.type === 'done'));
    }
    assert.equal(upstream.state.signal.aborted, true); assert.equal(upstream.state.canceled, 1);
    assert.equal((await frames(await handle(request(), env, opts(async () => sse())))).at(-1).type, 'done');
  }
});

await check('two active streams per user are bounded and cancel cleans the reservation exactly once', async () => {
  const handle = await load(); let calls = 0;
  const settings = opts(async () => { calls++; return sse([{ code: 0, data: 'AQID' }], { hold: true }); });
  const a = await handle(request(), env, settings), b = await handle(request(), env, settings);
  assert.equal((await handle(request(), env, settings)).status, 429); assert.equal(calls, 2);
  const other = await handle(request(input(), { user: 'bob' }), env, settings); assert.equal(other.status, 200); assert.equal(calls, 3);
  await Promise.all([a.body.cancel(), b.body.cancel(), other.body.cancel()]);
  const next = await handle(request(), env, opts(async () => sse())); assert.equal((await frames(next)).at(-1).type, 'done');
});

await check('isolate-wide active limit bounds distinct slow users and releases every canceled slot', async () => {
  const handle = await load(); let calls = 0;
  const settings = opts(async () => { calls++; return sse([{ code: 0, data: 'AQID' }], { hold: true }); });
  const active = [];
  for (let index = 0; index < 6; index++) active.push(await handle(request(input(), { user: `viewer-${index}` }), env, settings));
  assert.equal((await handle(request(input(), { user: 'viewer-next' }), env, settings)).status, 429); assert.equal(calls, 6);
  await Promise.all(active.map(response => response.body.cancel()));
  assert.equal((await frames(await handle(request(), env, opts(async () => sse())))).at(-1).type, 'done');
});

await check('per-isolate user quota and dedup TTL expire without changing the fixed service', async () => {
  const handle = await load(); let now = 1000, calls = 0; const first = input();
  const settings = { ...opts(async () => { calls++; return sse(); }), now: () => now };
  for (let index = 0; index < 30; index++) assert.equal((await frames(await handle(request(index ? input() : first), env, settings))).at(-1).type, 'done');
  const limited = await handle(request(), env, settings); assert.equal(limited.status, 429); assert.equal((await limited.json()).error, 'tts_rate_limited');
  assert.equal((await handle(request(first), env, settings)).status, 409); assert.equal(calls, 30);
  now += 60001; assert.equal((await frames(await handle(request(), env, settings))).at(-1).type, 'done');
  now += 5 * 60 * 1000; assert.equal((await frames(await handle(request(first), env, settings))).at(-1).type, 'done'); assert.equal(calls, 32);
});

console.log(`\n${checks} Volcengine TTS regression scenarios passed (offline mocks, no paid calls).`);
