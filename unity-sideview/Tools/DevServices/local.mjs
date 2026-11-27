// Run the two game backends from a checkout. Child IPC also closes on an unclean launcher exit.
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import net from 'node:net';
import { spawn, spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { DatabaseSync } from 'node:sqlite';
import { loadProfile } from './profile.mjs';
import { loadVoiceprintConfig } from '../live-service/server/voiceprint/config.mjs';

const tools = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const project = path.dirname(tools);
const state = path.resolve(process.env.Z_DEV_STATE_DIR || path.join(project, 'UserSettings/DevServices'));
const editorFile = path.resolve(process.env.Z_DEV_EDITOR_FILE || path.join(project, 'UserSettings/ZLive/local-connection.json'));
const controlFile = path.join(state, 'control.json');
const recordFile = path.join(state, 'editor-restore.json');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

function read(file) {
  try { return fs.readFileSync(file, 'utf8'); }
  catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}
function write(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
  const temporary = file + '.' + randomUUID();
  fs.writeFileSync(temporary, data, { flag: 'wx', mode: 0o600 });
  try { fs.renameSync(temporary, file); } finally { fs.rmSync(temporary, { force: true }); }
}
function port(value) {
  const p = Number(value);
  if (!Number.isInteger(p) || p < 1024 || p > 65535) throw Error('端口必须为 1024 到 65535 的整数。');
  return p;
}
function assertFree(port) {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', () => reject(Error('127.0.0.1:' + port + ' 无法监听。请选择其他端口或关闭所属应用。')));
    server.listen(port, '127.0.0.1', () => server.close(resolve));
  });
}
function restoreEditor() {
  const saved = read(recordFile);
  if (!saved) return;
  const { file, previous, published } = JSON.parse(saved);
  if (read(file) === published) {
    if (previous === null) fs.rmSync(file, { force: true });
    else write(file, previous);
  }
  fs.rmSync(recordFile);
}
function configuration() {
  const { env, mode } = loadProfile({ stateDir: state });
  const memoryPort = port(env.Z_MEMORY_PORT || 8942), livePort = port(env.Z_UNITY_PORT || 8943);
  if (memoryPort === livePort) throw Error('角色和直播端口须不同。');
  const memoryUrl = 'http://127.0.0.1:' + memoryPort, liveUrl = 'http://127.0.0.1:' + livePort;
  Object.assign(env, {
    Z_MEMORY_PORT: String(memoryPort), Z_UNITY_PORT: String(livePort), Z_LIVE_OFFICE: '0',
    Z_MEMORY_DB: path.join(state, 'memory.sqlite'), Z_LIVE_RUNTIME_DIR: path.join(state, 'live'),
    Z_LIVE_CONNECTION_FILE: path.join(state, 'connection.json'), Z_LIVE_CHARACTER_SERVICE_URL: memoryUrl
  });
  return { env, mode, memoryPort, livePort, memoryUrl, liveUrl };
}
function holdLock() {
  fs.mkdirSync(state, { recursive: true, mode: 0o700 });
  const lock = new DatabaseSync(path.join(state, 'launcher-lock.sqlite'));
  // The OS releases this transaction if the parent dies. Never signal a PID read from disk.
  try { lock.exec('BEGIN IMMEDIATE'); } catch { lock.close(); throw Error('此运行目录已有本地服务，请使用 status 或 stop。'); }
  return lock;
}
async function health(url, token) {
  try {
    const r = await fetch(url, { headers: token ? { Authorization: 'Bearer ' + token } : {}, signal: AbortSignal.timeout(1200) });
    return r.ok ? await r.json() : null;
  } catch { return null; }
}
function service(name, entry, env) {
  const fd = fs.openSync(path.join(state, name + '.log'), 'a', 0o600);
  try {
    const child = spawn(process.execPath, [path.join(tools, 'PortableHost/service-runner.mjs'), path.join(tools, entry)], {
      cwd: path.join(tools, entry.split('/')[0]), env, stdio: ['ignore', fd, fd, 'ipc']
    });
    child.once('error', () => { child.launchFailed = true; });
    return child;
  } finally { fs.closeSync(fd); }
}
const ended = c => c.launchFailed || c.exitCode !== null || c.signalCode !== null;

async function run() {
  if (Number(process.versions.node.split('.')[0]) < 24) throw Error('游戏后台需要 Node.js 24 或更新版本。');
  const lock = holdLock(), children = [];
  let server, finished = false, childFailed = false;
  let signalEnd;
  const done = new Promise(resolve => { signalEnd = resolve; });
  function stop() { finished = true; signalEnd(); }
  if (process.connected) process.once('disconnect', stop);
  for (const signal of ['SIGINT','SIGTERM','SIGHUP']) process.once(signal, stop);
  try {
    restoreEditor(); // recover a previous crashed instance before publishing a new connection
    const previous = read(editorFile);
    if (previous !== null && !process.argv.includes('--activate')) throw Error('已有 Editor 连接。需要临时切换到本机时，请添加 --activate；正常停止后会恢复。');
    const { env, mode, memoryPort, livePort, memoryUrl, liveUrl } = configuration();
    await Promise.all([assertFree(memoryPort), assertFree(livePort)]);
    if (finished) return;
    fs.rmSync(env.Z_LIVE_CONNECTION_FILE, { force: true });
    children.push(service('memory', 'MemoryLab/server.mjs', env));
    children.push(service('live', 'live-service/server/index.mjs', env));
    for (const child of children) {
      const childEnded = () => { if (!finished) childFailed = true; stop(); };
      child.once('error', childEnded); child.once('exit', childEnded);
    }
    let token;
    const probe = async () => {
      const descriptor = JSON.parse(read(env.Z_LIVE_CONNECTION_FILE) || '{}');
      token = descriptor.baseUrl === liveUrl ? descriptor.token : null;
      const [memory, live] = await Promise.all([health(memoryUrl + '/api/health'), token && health(liveUrl + '/health', token)]);
      return { memory, live, ready: Boolean(memory?.runtimeRevision === 'integrated-npc-runtime-v1' && live?.ok && live.protocolVersion === 1) };
    };
    let result;
    const until = Date.now() + 30000;
    while (!finished && Date.now() < until) {
      result = await probe(); if (result.ready) break;
      await wait(180);
    }
    if (childFailed || (!finished && !result?.ready)) throw Error('后台未启动完成。请检查 ' + state + '/memory.log 和 live.log。');
    if (finished) return;

    const published = JSON.stringify({ memoryUrl, baseUrl: liveUrl, token });
    if (read(editorFile) !== previous) throw Error('Editor 连接已被其他程序更新，请重试。');
    write(recordFile, JSON.stringify({ file: editorFile, previous, published }));
    write(editorFile, published);
    write(path.join(state, 'game-connection.json'), JSON.stringify({ memoryUrl }));
    const controlToken = randomUUID();
    const { NativeVoiceService } = await import('../live-service/server/voiceprint/native-service.mjs');
    const voiceProfiles = new NativeVoiceService({
      configRoot: env.Z_LIVE_VOICE_CONFIG_DIR, profileRoot: path.join(env.Z_LIVE_RUNTIME_DIR, 'voiceprint'),
      isolatedPlayer: env.Z_LIVE_PLAYER_ISOLATED === '1', load: root => loadVoiceprintConfig(root, env)
    });
    async function diagnostics() {
      const { memory, live, ready } = await probe();
      const team = mode === 'team';
      const voice = await voiceProfiles.config('local-owner');
      const decoder = spawnSync(env.FFMPEG_PATH || 'ffmpeg', ['-version'], { timeout: 2000, stdio: 'ignore', env }).status === 0;
      return { running: true, ready, provider: mode, memoryUrl, liveUrl, configDirectory: env.Z_LIVE_CONFIG_DIR,
        settingsUrl: memoryUrl + '/settings', upstreamRequestsTested: false,
        capabilities: {
          characterModelConfigured: Boolean(memory?.configured), liveModelConfigured: Boolean(live?.agentConfigured && (!team || memory?.team?.connected)),
          teamLoggedIn: team ? Boolean(memory?.team?.connected) : null, characterTtsConfigured: Boolean(memory?.speech?.configured),
          liveTtsConfigured: Boolean(live?.ttsConfigured), audioTranscriptionKeyPresent: Boolean(env.VOLC_ASR_API_KEY),
          voiceprintMode: voice.mode, voiceprintConfigComplete: voice.validMode && !voice.missing.length, voiceprintMissing: voice.missing,
          musicSessionFilePresent: fs.existsSync(env.NETEASE_SESSION_FILE), ffmpegAvailable: decoder
        } };
    }
    server = http.createServer(async (req, res) => {
      const send = (status, body) => { res.writeHead(status, { 'Content-Type':'application/json', 'Cache-Control':'no-store' }); res.end(JSON.stringify(body)); };
      if (req.headers.origin || req.headers.authorization !== 'Bearer ' + controlToken) return send(403, {});
      if (req.url === '/stop' && req.method === 'POST') { send(200, { stopping:true }); stop(); return; }
      if (req.url === '/status' && req.method === 'GET') {
        try { send(200, await diagnostics()); } catch { send(503, { error:'backend_check_failed' }); }
        return;
      }
      send(404, {});
    });
    await new Promise((resolve,reject) => { server.once('error',reject); server.listen(0,'127.0.0.1',resolve); });
    write(controlFile, JSON.stringify({ port:server.address().port, token:controlToken }));
    console.log(JSON.stringify(await diagnostics(), null, 2));
    await done;
    if (childFailed) throw Error('后台进程意外退出，请检查运行目录中的日志。');
  } finally {
    finished = true;
    // The handles belong to this invocation even when startup failed part-way through.
    for (const child of children) if (!ended(child)) child.kill('SIGTERM');
    for (let i = 0; i < 35 && children.some(c => !ended(c)); i++) await wait(100);
    for (const child of children) if (!ended(child)) child.kill('SIGKILL');
    if (server) { server.closeAllConnections(); server.close(); }
    restoreEditor();
    fs.rmSync(controlFile, { force: true });
    fs.rmSync(path.join(state, 'game-connection.json'), { force: true });
    fs.rmSync(path.join(state, 'connection.json'), { force: true });
    lock.close();
  }
}
async function requestLauncher(command) {
  const data = JSON.parse(read(controlFile) || 'null');
  if (data) {
    let response;
    try {
      response = await fetch('http://127.0.0.1:' + port(data.port) + '/' + command, {
        method: command === 'stop' ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + data.token },
        signal: AbortSignal.timeout(7000)
      });
    } catch {}
    if (response?.ok) return response.json();
    if (response) throw Error('启动器响应状态 ' + response.status + '，请检查本地服务运行目录。');
  }
  throw Error('未连接到此运行目录的本地后台。请先在另一个终端运行 local.mjs start。');
}
async function control(command) {
  console.log(JSON.stringify(await requestLauncher(command), null, 2));
  if (command === 'stop') {
    const until = Date.now() + 8000;
    while (read(controlFile) && Date.now() < until) await wait(100);
    if (read(controlFile)) throw Error('服务仍在停止，尚未完成进程回收。');
  }
}
async function openApp(file) {
  if (process.platform !== 'darwin') throw Error('此命令用于 macOS 游戏 .app。');
  const app = path.resolve(file);
  if (!app.endsWith('.app') || !fs.statSync(app, { throwIfNoEntry: false })?.isDirectory())
    throw Error('请提供现有的游戏 .app 路径。');
  const status = await requestLauncher('status');
  if (!status.ready) throw Error('本地角色或直播服务尚未就绪，请查看 local.mjs status。');
  const liveFile = path.join(state, 'connection.json');
  const live = JSON.parse(read(liveFile) || '{}');
  if (live.baseUrl !== status.liveUrl || !live.token) throw Error('本地直播连接发生变化，请重新检查后台。');
  const saves = path.join(state, 'app-saves');
  fs.mkdirSync(saves, { recursive: true, mode: 0o700 });
  const result = spawnSync('/usr/bin/open', ['-n', app,
    '--env', 'Z_LIVE_CONNECTION_FILE=' + liveFile, '--env', 'Z_MEMORY_SAVE_ROOT=' + saves,
    '--args', '-memoryService', status.memoryUrl, '-logFile', path.join(state, 'player.log')], { stdio: 'inherit' });
  if (result.status !== 0) throw Error('macOS 未能打开游戏应用。');
  console.log('已请求打开游戏。本地存档：' + saves);
}
try {
  const [command, ...args] = process.argv.slice(2);
  if (command === 'start' && args.every(arg => arg === '--activate')) await run();
  else if ((command === 'status' || command === 'stop') && !args.length) await control(command);
  else if (command === 'app' && args.length === 1) await openApp(args[0]);
  else throw Error('用法：node unity-sideview/Tools/DevServices/local.mjs start [--activate] | status | stop | app 游戏.app');
} catch (error) {
  console.error(error instanceof SyntaxError ? '私有配置 JSON 格式有误。' : error.message);
  process.exitCode = 1;
}
