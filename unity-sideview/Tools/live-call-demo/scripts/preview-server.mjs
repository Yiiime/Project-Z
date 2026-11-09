import http from 'node:http';
import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import { Readable } from 'node:stream';
import { webcrypto } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import {SingingService} from '../server/singing/service.mjs';
import {singingHttp} from '../server/singing/http.mjs';
import {loadAsrConfig,publicAsrConfig} from '../server/asr/config.mjs';
import {attachAsrBridge} from '../server/asr/bridge.mjs';
import {loadVoiceprintConfig} from '../server/voiceprint/config.mjs';
import {createVoiceprintHttp} from '../server/voiceprint/http.mjs';
import {attachVoiceprintBridge} from '../server/voiceprint/bridge.mjs';
import {handleAgent} from '../worker/agent-director.js';

// Node 18 provides fetch/Request/Response, but may not expose Web Crypto globally.
if (!globalThis.crypto) Object.defineProperty(globalThis, 'crypto', { value: webcrypto, configurable: true });
const root = fileURLToPath(new URL('../', import.meta.url));
// Local preview can reuse the user's configured demo without copying secret files.
const configRoot = process.env.Z_DEMO_CONFIG_DIR ? path.resolve(process.env.Z_DEMO_CONFIG_DIR) : root;
const worker = (await import(new URL('../dist/server/index.js', import.meta.url))).default;
const port = Number(process.env.Z_PREVIEW_PORT || 4174);
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Z_PREVIEW_PORT must be a valid local port');
function parseEnvKey(source, name) {
  if (!['DEEPSEEK_API_KEY', 'VOLC_TTS_API_KEY'].includes(name)) return '';
  const match = source.match(new RegExp('^[ \\t]*(?:export[ \\t]+)?' + name + '[ \\t]*=[ \\t]*([^\\r\\n]*)', 'm'));
  if (!match) return '';
  const value = match[1].trim();
  if (/^(['"])/.test(value)) {
    const quoted = value.match(/^(['"])(.*?)\1\s*(?:#.*)?$/);
    return quoted ? quoted[2].trim() : '';
  }
  return value.replace(/(?:^|[ \t]+)#.*$/, '').trim();
}
function parseDeepSeekKey(source) { return parseEnvKey(source, 'DEEPSEEK_API_KEY'); }
async function environment() {
  const env = {};
  for (const [name, visible] of [['DEEPSEEK_API_KEY','DeepSeek配置.env'], ['VOLC_TTS_API_KEY','火山语音配置.env']]) {
    env[name] = process.env[name]?.trim() || '';
    if (env[name]) continue;
    // Only explicitly supported variables are read. Values never reach HTML/logs.
    for (const filename of [visible, '.env.local']) {
      try { const key = parseEnvKey(await fs.readFile(path.join(configRoot, filename), 'utf8'), name); if (key) { env[name] = key; break; } } catch {}
    }
  }
  return env;
}
const singing = await SingingService.open({root,environment});
const voiceprintHttp = createVoiceprintHttp(configRoot);
const server = http.createServer(async (incoming, outgoing) => {
  const controller = new AbortController();
  let responseStream;
  outgoing.once('close', () => {
    if (!outgoing.writableEnded) { controller.abort(); responseStream?.destroy(); }
  });
  try {
    if(![`127.0.0.1:${port}`,`localhost:${port}`].includes(incoming.headers.host)){outgoing.writeHead(403);outgoing.end('Invalid local host');return;}
    if(incoming.url==='/api/local-status'&&incoming.method==='GET'){outgoing.writeHead(200,{'Content-Type':'application/json','Cache-Control':'no-store'});outgoing.end(JSON.stringify({app:'z-call-local',root,port}));return;}
    if(incoming.method==='GET'&&incoming.url==='/api/voiceprint/rtc.js'){outgoing.writeHead(200,{'Content-Type':'text/javascript','Cache-Control':'public, max-age=3600'});outgoing.end(await fs.readFile(path.join(root,'node_modules/@volcengine/rtc/index.esm.min.js')));return;}
    if(incoming.method==='GET'&&incoming.url==='/api/asr/config'){outgoing.writeHead(200,{'Content-Type':'application/json','Cache-Control':'no-store'});outgoing.end(JSON.stringify(publicAsrConfig(await loadAsrConfig(configRoot))));return;}
    const audioModule={'/api/asr/worklet.js':'asr-audio-worklet.js','/api/asr/pcm.js':'pcm-resampler.js'}[incoming.url];
    if(incoming.method==='GET'&&audioModule){outgoing.writeHead(200,{'Content-Type':'text/javascript','Cache-Control':'no-store'});outgoing.end(await fs.readFile(path.join(root,'ui',audioModule)));return;}
    const chunks = []; let bytes = 0;
    const bodyLimit=incoming.url==='/api/voiceprint/enroll'?1000000:incoming.url?.split('?')[0]==='/api/agent'?131072:16384;
    for await (const chunk of incoming) {
      bytes += chunk.length;
      if (bytes > bodyLimit) { outgoing.writeHead(413); outgoing.end('Request too large'); return; }
      chunks.push(chunk);
    }
    const headers = new Headers();
    for (const [name, value] of Object.entries(incoming.headers)) if (value !== undefined) headers.set(name, Array.isArray(value) ? value.join(',') : value);
    // This simulated identity exists only on a server bound to loopback.
    headers.set('oai-authenticated-user-id', 'local-preview-owner');
    const method = incoming.method || 'GET';
    const request = new Request(`http://${incoming.headers.host}${incoming.url || '/'}`, { method, headers, signal: controller.signal,
      ...(!['GET', 'HEAD'].includes(method) ? { body: Buffer.concat(chunks), duplex: 'half' } : {}) });
    const env=await environment(),pathname=new URL(request.url).pathname;
    const response = await voiceprintHttp(request) || await singingHttp(request,singing) || (pathname==='/api/agent'||pathname==='/api/agent/config' ? await handleAgent(request,env,{singingContext:()=>singing.context('local-preview-owner')}) : await worker.fetch(request,env,{}));
    outgoing.writeHead(response.status, Object.fromEntries(response.headers));
    if (response.body) {
      responseStream = Readable.fromWeb(response.body);
      responseStream.on('error', () => outgoing.destroy());
      responseStream.pipe(outgoing);
    } else outgoing.end();
  } catch {
    if (!outgoing.headersSent && !outgoing.destroyed) { outgoing.writeHead(500, { 'Content-Type': 'text/plain' }); outgoing.end('Preview request failed'); }
    else outgoing.destroy();
  }
});
server.on('error', error => {
  console.error(error.code === 'EADDRINUSE'
    ? `端口 ${port} 已被占用。若 Demo 已经运行，请直接打开 http://127.0.0.1:${port}/；否则设置 Z_PREVIEW_PORT 使用其他端口。`
    : `本地服务启动失败：${error.code || 'unknown_error'}`);
  void singing.close().finally(()=>{process.exitCode = 1;});
});
const asrBridge=attachAsrBridge(server,{port,loadConfig:()=>loadAsrConfig(configRoot)});
const voiceprintBridge=attachVoiceprintBridge(server,{port,loadConfig:()=>loadVoiceprintConfig(configRoot)});
server.on('upgrade',(req,socket)=>{if(!['/api/asr/stream','/api/voiceprint/stream'].includes(req.url))socket.destroy();});
server.listen(port, '127.0.0.1', () => {
  const url = `http://127.0.0.1:${port}/`;
  console.log(`Z preview: ${url} (local identity simulation; secret values are never logged)`);
  if (process.env.Z_DEMO_OPEN_BROWSER === '1' && process.platform === 'darwin') {
    const browser = spawn('/usr/bin/open', [url], { stdio: 'ignore' });
    browser.on('error', () => console.error(`无法自动打开浏览器，请手动访问 ${url}`));
    browser.on('exit', code => { if (code) console.error(`请手动访问 ${url}`); });
  }
});
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => (asrBridge.close(),voiceprintBridge.close(),server.close(),void singing.close().finally(()=>process.exit(0))));
