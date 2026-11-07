/** Worker/build/preview integration only. Fake credentials, fake HTTP, no paid calls. */
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { Readable, Writable } from 'node:stream';
import { Script } from 'node:vm';
import { randomUUID, webcrypto } from 'node:crypto';

const origin = 'https://voice-integration.example.test';
const markers = { DEEPSEEK_API_KEY: 'fixture-chat-secret-must-stay-server-side', VOLC_TTS_API_KEY: 'fixture-voice-secret-must-stay-server-side' };
let checks = 0;
async function check(name, run) { await run(); checks++; console.log('PASS ' + name); }
function post(endpoint, data) {
  return new Request(origin + endpoint, { method: 'POST', headers: { Origin: origin, 'Content-Type': 'application/json', 'oai-authenticated-user-id': 'integration-fixture' }, body: JSON.stringify(data) });
}

const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'z-voice-integration-'));
const oldFetch = globalThis.fetch;
const oldCrypto = globalThis.crypto;
try {
  if (!globalThis.crypto) Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
  let upstreamCalls = 0;
  globalThis.fetch = async (url, init) => {
    upstreamCalls++;
    assert.equal(url, 'https://api.deepseek.com/chat/completions', 'integration test must never reach a voice or external service');
    assert.equal(init.headers.Authorization, 'Bearer ' + markers.DEEPSEEK_API_KEY);
    return Response.json({ choices: [{ finish_reason: 'stop', message: { content: JSON.stringify({ text: '来啦，晚上好！', mood: 'happy' }) } }] });
  };
  await check('actual browser and Worker bundle include SpeechDirector and both adapters without embedding environment values', async () => {
    await fs.writeFile(path.join(dir, 'package.json'), '{"type":"module"}');
    for (const folder of ['scripts', 'worker', 'ui/assets']) await fs.mkdir(path.join(dir, folder), { recursive: true });
    await fs.cp(new URL('../ui/audio/songs/',import.meta.url),path.join(dir,'ui/audio/songs'),{recursive:true});
    await fs.cp(new URL('../server/singing/',import.meta.url),path.join(dir,'server/singing'),{recursive:true});
    await fs.mkdir(path.join(dir,'docs/reviews'),{recursive:true});
    await fs.copyFile(new URL('../docs/reviews/wanan-vivi2.json',import.meta.url),path.join(dir,'docs/reviews/wanan-vivi2.json'));
    await fs.copyFile(new URL('../ui/call-contract.js',import.meta.url),path.join(dir,'ui/call-contract.js'));
    await fs.copyFile(new URL('../ui/drawing-scene.js',import.meta.url),path.join(dir,'ui/drawing-scene.js'));
    await fs.copyFile(new URL('../ui/drawing-director.js',import.meta.url),path.join(dir,'ui/drawing-director.js'));
    for (const file of ['build-site.mjs', 'html-build.mjs', 'z-skill.mjs','live2d-assets.mjs','song-assets.mjs','agent-bundle.mjs']) await fs.copyFile(new URL('../scripts/' + file, import.meta.url), path.join(dir, 'scripts', file));
    for (const file of ['index.js', 'demo-visitor.js', 'singing-core.js', 'song-parser.js', 'singing-cloud.js', 'deepseek-chat.js', 'performance-director.js', 'agent-director.js', 'drawing-planner.js', 'volc-tts.js','public-assets.js']) await fs.copyFile(new URL('../worker/' + file, import.meta.url), path.join(dir, 'worker', file));
    for (const file of ['game-return.js', 'call-voice-policy.js', 'call-noise-monitor.js','doubao-recognition.js','voiceprint-recognition.js', 'index.html', 'live2d-preview.html', 'performance-intent.js', 'live2d-actions.js', 'app.js', 'call-contract.js', 'call-engine.js', 'call-ui.js', 'call.css', 'styles.css', 'cards.css', 'manga-motion.css', 'live-engine.js', 'play-engine.js', 'play-ui.js', 'agent-client.js', 'agent-engine.js', 'session-continuity.js', 'conversation-engine.js', 'audience-director.js', 'dialogue-review.js', 'text-director.js', 'speech-director.js', 'talent-director.js', 'talent-config.js', 'chat-client.js', 'card-art.js', 'manga-face.js','live2d-config.js','live2d-stage.js','live2d.css'])
      await fs.copyFile(new URL('../ui/' + file, import.meta.url), path.join(dir, 'ui', file));
    for (const file of await fs.readdir(new URL('../ui/assets/', import.meta.url))) {
      if (file.endsWith('.svg') || ['manga-live-night.png','live2d-empty-room.png', 'asset-notes.json'].includes(file)) await fs.copyFile(new URL('../ui/assets/' + file, import.meta.url), path.join(dir, 'ui/assets', file));
    }
    for (const file of ['SKILL.md', 'references/z-profile.md', 'references/z-voice.md', 'references/world.md', 'references/plot.md']) {
      const target = path.join(dir, 'skills/moon-z-life', file); await fs.mkdir(path.dirname(target), { recursive: true });
      await fs.copyFile(new URL('../skills/moon-z-life/' + file, import.meta.url), target);
    }
    await fs.cp(new URL('../main-agent/', import.meta.url), path.join(dir, 'main-agent'), {recursive:true});
    for (const name of ['audience-reaction','singing','voice-performance','simple-drawing','entertainment-tarot','call-companion'])
      await fs.cp(new URL('../skills/' + name + '/', import.meta.url), path.join(dir,'skills',name), {recursive:true});
    // This file belongs to the temporary fixture. No project credential file is read.
    await fs.writeFile(path.join(dir, '.env.local'), Object.entries(markers).map(([key, value]) => key + '=' + value).join('\n'));
    execFileSync(process.execPath, [path.join(dir, 'scripts/build-site.mjs')], { env: markers, stdio: 'pipe' });
    const built = await fs.readFile(path.join(dir, 'dist/server/index.js'), 'utf8');
    const browser = await fs.readFile(path.join(dir, 'dist/.openai/browser-check.js'), 'utf8');
    new Script(browser, { filename: 'generated-browser.js' });
    assert(browser.includes('const SpeechModule=')); assert(browser.includes('class SpeechDirector'));
    assert(built.includes('const handleChat=')); assert(built.includes('const handleTts='));
    assert(built.includes('openspeech.bytedance.com')); assert(!browser.includes('openspeech.bytedance.com'));
    assert(!browser.includes('【角色素材 1】'));
    for (const value of Object.values(markers)) { assert(!built.includes(value)); assert(!browser.includes(value)); }
    assert(!built.includes('api.dev.runwayml.com')); assert(!built.includes('RUNWAYML_API_SECRET'));
  });
  const worker = (await import(pathToFileURL(path.join(dir, 'dist/server/index.js')).href)).default;
  await check('performance preview is bundled, standalone and does not require provider access', async () => {
    const response=await worker.fetch(new Request(origin+'/performance'),{});assert.equal(response.status,200);
    const html=await response.text();assert(html.includes('Z 的表现预览'));assert(html.includes('class Live2DStage'));
    assert(!html.includes("import {Live2DStage}"));assert(!html.includes('/api/tts'));assert(!html.includes('/api/chat'));
    new Script('(async()=>{'+html.match(/<script type="module">([\s\S]*?)<\/script>/)[1]+'})()');
    assert.equal(await (await worker.fetch(new Request(origin+'/performance',{method:'HEAD'}),{})).text(),'');
  });
  await check('missing either key independently disables that adapter while the page and other configuration remain usable', async () => {
    for (const env of [{}, { DEEPSEEK_API_KEY: markers.DEEPSEEK_API_KEY }, { VOLC_TTS_API_KEY: markers.VOLC_TTS_API_KEY }, markers]) {
      const health = await (await worker.fetch(new Request(origin + '/api/health'), env)).json();
      assert.equal(health.status, 'ok'); assert.equal(health.chatConfigured, Boolean(env.DEEPSEEK_API_KEY)); assert.equal(health.voiceConfigured, Boolean(env.VOLC_TTS_API_KEY));
      const chat = await (await worker.fetch(new Request(origin + '/api/chat/config'), env)).json();
      const voice = await (await worker.fetch(new Request(origin + '/api/tts/config'), env)).json();
      assert.equal(chat.enabled, Boolean(env.DEEPSEEK_API_KEY)); assert.equal(voice.enabled, Boolean(env.VOLC_TTS_API_KEY));
      assert.equal(voice.voice, 'Vivi 2.0');
      const page = await worker.fetch(new Request(origin), env); assert.equal(page.status, 200);
      if (env === markers) { const html = await page.text(); for (const value of Object.values(markers)) assert(!html.includes(value)); }
      else await page.body.cancel();
      if (!env.DEEPSEEK_API_KEY) assert.equal((await worker.fetch(post('/api/chat', { requestId: randomUUID(), message: '晚上好！' }), env)).status, 503);
      if (!env.VOLC_TTS_API_KEY) assert.equal((await worker.fetch(post('/api/tts', { requestId: randomUUID(), text: '晚上好！', mood: 'happy' }), env)).status, 503);
    }
    assert.equal(upstreamCalls, 0, 'config/page and absent-key failures must be free of synthesis/generation');
    const chatOnly = await worker.fetch(post('/api/chat', { requestId: randomUUID(), message: '晚上好！' }), { DEEPSEEK_API_KEY: markers.DEEPSEEK_API_KEY });
    assert.equal(chatOnly.status, 200); assert.equal((await chatOnly.json()).text, '来啦，晚上好！'); assert.equal(upstreamCalls, 1);
    for (const endpoint of ['/api/tts/status?ticket=old', '/api/tts/audio?ticket=old']) assert.equal((await worker.fetch(new Request(origin + endpoint), markers)).status, 404);
    assert.equal(upstreamCalls, 1);
  });
} finally {
  globalThis.fetch = oldFetch;
  if (!oldCrypto) delete globalThis.crypto;
  await fs.rm(dir, { recursive: true, force: true });
}

const preview = await fs.readFile(new URL('../scripts/preview-server.mjs', import.meta.url), 'utf8');
const envStart = preview.indexOf('function parseEnvKey('), serverStart = preview.indexOf('\nconst server =', envStart), listenStart = preview.indexOf('\nserver.listen(', serverStart);
assert(envStart > 0 && serverStart > envStart && listenStart > serverStart);
const createEnvironment = new Function('fs', 'process', 'configRoot', 'path', preview.slice(envStart, preview.indexOf('\nconst singing =', envStart)) + '\nreturn environment;');
const loadEnvironment = (filesystem, process, configRoot) => createEnvironment(filesystem, process, configRoot, path);
function previewHandler(worker, environment) {
  let callback;
  const http = { createServer(fn) { callback = fn; return { on() { return this; } }; } };
  new Function('http', 'worker', 'environment', 'port', 'Readable', 'singingHttp', 'singing', 'handleAgent', 'root', 'voiceprintHttp', preview.slice(serverStart, preview.indexOf('\nconst asrBridge=',serverStart)))(http, worker, environment, 43917, Readable, async()=>null, {close:async()=>{}}, ()=>{throw Error('unexpected agent route');}, '/fixture', async()=>null);
  return callback;
}
class FixtureResponse extends Writable {
  constructor() { super(); this.chunks = []; }
  _write(chunk, _encoding, done) { this.chunks.push(Buffer.from(chunk)); done(); }
  writeHead(status, headers = {}) { this.statusCode = status; this.headers = headers; this.headersSent = true; }
  text() { return Buffer.concat(this.chunks).toString('utf8'); }
}
function incoming(body = '{"text":"晚上好"}') {
  const stream = Readable.from([Buffer.from(body)]);
  stream.method = 'POST'; stream.url = '/api/tts';
  stream.headers = { host:'127.0.0.1:43917', 'content-type': 'application/json', origin: 'http://127.0.0.1:43917', 'oai-authenticated-user-id': 'caller-cannot-spoof-local-identity' };
  return stream;
}
const nextTurn = () => new Promise(resolve => setImmediate(resolve));
await check('preview request applies fresh supported environment and local identity without aborting a normally completed response', async () => {
  const files = { '/fixture/.env.local': 'DEEPSEEK_API_KEY=fixture-chat-one\nVOLC_TTS_API_KEY=fixture-voice-one' };
  const reads = [], received = [];
  const fsMock = { async readFile(filename) { reads.push(filename); if (!(filename in files)) throw Error('missing fixture'); return files[filename]; } };
  const environment = loadEnvironment(fsMock, { env: {} }, '/fixture/');
  const handler = previewHandler({ async fetch(request, env) {
    assert.equal(request.headers.get('oai-authenticated-user-id'), 'local-preview-owner');
    assert.equal(request.headers.get('Origin'), 'http://127.0.0.1:43917');
    assert.equal(await request.text(), '{"text":"晚上好"}'); received.push({ env, signal: request.signal });
    return Response.json({ ok: true });
  } }, environment);
  for (let i = 0; i < 2; i++) {
    if (i) files['/fixture/.env.local'] = 'DEEPSEEK_API_KEY=fixture-chat-two\nVOLC_TTS_API_KEY=fixture-voice-two';
    const output = new FixtureResponse(); const finished = new Promise(resolve => output.once('finish', resolve));
    await handler(incoming(), output); await finished; await nextTurn();
    assert.equal(output.statusCode, 200); assert.deepEqual(JSON.parse(output.text()), { ok: true });
  }
  assert.deepEqual(received.map(item => item.env), [{ DEEPSEEK_API_KEY: 'fixture-chat-one', VOLC_TTS_API_KEY: 'fixture-voice-one' }, { DEEPSEEK_API_KEY: 'fixture-chat-two', VOLC_TTS_API_KEY: 'fixture-voice-two' }]);
  assert(received.every(item => !item.signal.aborted));
  assert(reads.every(filename => ['/fixture/DeepSeek配置.env', '/fixture/火山语音配置.env', '/fixture/.env.local'].includes(filename)));
});
await check('preview disconnect aborts the Worker request and cancels a pending response stream', async () => {
  let signal, canceled = false;
  const handler = previewHandler({ async fetch(request) {
    signal = request.signal;
    return new Response(new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode('{"type":"start"}\n')); }, cancel() { canceled = true; } }), { headers: { 'Content-Type': 'application/x-ndjson' } });
  } }, async () => ({}));
  const output = new FixtureResponse(); await handler(incoming(), output); await nextTurn();
  assert.equal(signal.aborted, false); assert(output.text().includes('"start"'));
  output.destroy(); await nextTurn(); await nextTurn();
  assert.equal(signal.aborted, true); assert.equal(canceled, true);
});
await check('preview rejects oversized incoming bodies before environment access or adapter dispatch', async () => {
  let calls = 0;
  const handler = previewHandler({ async fetch() { calls++; throw Error('must not dispatch'); } }, async () => { calls++; throw Error('must not load configuration'); });
  const output = new FixtureResponse(); const finished = new Promise(resolve => output.once('finish', resolve));
  await handler(incoming('x'.repeat(16385)), output); await finished;
  assert.equal(output.statusCode, 413); assert.equal(calls, 0);
});
console.log(`\n${checks} voice integration scenarios passed (temporary build, in-memory HTTP, no paid calls).`);
