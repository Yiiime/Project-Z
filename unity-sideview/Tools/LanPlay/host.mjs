import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {createGateway} from './gateway.mjs';
import {loadEnvironment} from '../live-service/server/providers.mjs';
import {loadTeamClient,memberVerifier} from './team-access.mjs';
import {TeamClient} from '../team-model/client.mjs';

const here = path.dirname(fileURLToPath(import.meta.url)), tools = path.dirname(here);
const local = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(here, '.local'));
const config = JSON.parse(fs.readFileSync(path.join(local, 'host.json')));
const configDirectory = process.env.Z_LIVE_CONFIG_DIR || path.join(tools, 'live-service');
const provider = loadEnvironment(configDirectory);
const cloudWorker = process.env.Z_CLOUD_WORKER === '1';
const team=loadTeamClient();
if (cloudWorker && team) throw Error('云端游戏工作进程需要主机的直连供应商配置。');
if (typeof config.teamModel === 'boolean' && config.teamModel !== Boolean(team))
  throw Error('认证模式与签发的客户端邀请不一致，请检查主机服务配置。');
const modelFile = process.env.Z_MEMORY_CONFIG || path.join(tools, 'MemoryLab/.local/model.json');
if (!provider.DEEPSEEK_API_KEY && !team) {try {const shared=JSON.parse(fs.readFileSync(modelFile));if(shared.provider==='deepseek'&&/^https:\/\/api\.deepseek\.com(?:\/v1)?\/?$/.test(shared.baseUrl||''))provider.DEEPSEEK_API_KEY=shared.apiKey;} catch {}}
if (cloudWorker && !provider.DEEPSEEK_API_KEY) throw Error('云端主机需要配置文字模型。');
if (!cloudWorker && !team && (!provider.DEEPSEEK_API_KEY || !provider.VOLC_TTS_API_KEY)) throw Error('主机必须先配置模型和火山 TTS，玩家端无需配置。');
const sessions = new Map(), children = new Set();
const idleTimers = new Map(), draining = new Map();
let shuttingDown = false;

// The gateway calls this after authorization, and again only when the full proxy response closes.
// An open SSE/audio stream holds its worker pair; a closed game starts the idle countdown.
function onCloudActivity(id, activeCount) {
  clearTimeout(idleTimers.get(id));
  idleTimers.delete(id);
  const generation = sessions.get(id);
  if (shuttingDown || activeCount || !generation) return;
  const timer = setTimeout(async () => {
    const services = await generation.catch(() => null);
    if (idleTimers.get(id) !== timer) return;
    idleTimers.delete(id);
    if (sessions.get(id) === generation) services?.retire();
  }, 180_000);
  timer.unref();
  idleTimers.set(id,timer);
}
const freePort = () => new Promise((resolve, reject) => {
  const server = http.createServer(); server.on('error', reject);
  server.listen(0, '127.0.0.1', () => {const port = server.address().port; server.close(() => resolve(port));});
});
async function startPlayer(id, forget) {
  const root = path.join(local, 'players', id); fs.mkdirSync(root, {recursive: true, mode: 0o700});
  const memoryPort = await freePort(), livePort = await freePort(), own = [];
  if (shuttingDown) throw Error('host_stopping');
  const closed = [];
  let retiring = false;
  function retireCloudPair() {
    if (retiring) return;
    retiring = true;
    // Publish the barrier before signalling. Reconnects must not share or reopen the data
    // directory until BOTH old processes have closed, even if the first one crashed.
    const timeout = setTimeout(() => {
      for (const child of own) if (child.exitCode === null && !child.signalCode) child.kill('SIGKILL');
    }, 4000);
    timeout.unref();
    const stopped = Promise.all(closed).then(() => {
      clearTimeout(timeout);
      forget();
      if (draining.get(id) === stopped) draining.delete(id);
    });
    draining.set(id, stopped);
    for (const child of own) if (child.exitCode === null && !child.signalCode) child.kill('SIGTERM');
  }
  const connection = path.join(root, 'live-connection.json');
  const env = {...process.env, ...Object.fromEntries(Object.entries(provider).filter(([, value]) => value)),
    Z_MEMORY_CONFIG: modelFile, Z_LIVE_PLAYER_ISOLATED: '1',
    ...(cloudWorker ? {Z_CLOUD_PLAYER_ID:id, Z_LAN_HOST_ROOT:local} : {}),
    Z_TTS_CONFIG: process.env.Z_TTS_CONFIG || path.join(tools, 'MemoryLab/.local/tts.json'),
    Z_LIVE_OFFICE: '0', Z_MEMORY_PORT: String(memoryPort), Z_UNITY_PORT: String(livePort),
    Z_LIVE_CONFIG_DIR: configDirectory, Z_LIVE_RUNTIME_DIR: path.join(root, 'live'),
    Z_MEMORY_DB: path.join(root, 'memory.sqlite'), Z_LIVE_CONNECTION_FILE: connection,
    ...(team?{Z_TEAM_SESSION_DIR:path.join(root,'team-session')} : {}),
    Z_LIVE_CHARACTER_SERVICE_URL: `http://127.0.0.1:${memoryPort}`};
  try {
    for (const [name, folder, entry] of [['memory', 'MemoryLab', 'server.mjs'], ['live', 'live-service', 'server/index.mjs']]) {
      const fd = fs.openSync(path.join(root, name + '.log'), 'a', 0o600);
      const child = spawn(process.execPath, [path.join(tools, 'PortableHost/service-runner.mjs'), path.join(tools, folder, entry)],
        {cwd: path.join(tools, folder), env, stdio: ['ignore', fd, fd, 'ipc']});
      fs.closeSync(fd); own.push(child); children.add(child);
      if (cloudWorker) closed.push(new Promise(resolve => child.once('close', resolve)));
      child.once('close', () => {
        children.delete(child);
        if (cloudWorker) { retireCloudPair(); return; }
        forget();
        for (const peer of own) if (peer !== child && peer.exitCode === null && !peer.signalCode) peer.kill('SIGTERM');
      });
      child.once('error', () => { child.launchFailed = true; });
    }
    for (let i = 0; i < 100; i++) {
      if (own.some(child => child.launchFailed || child.exitCode !== null || child.signalCode)) throw Error('backend_start_failed');
      try {
        const token = fs.readFileSync(path.join(root, 'live/local-token'), 'utf8').trim();
        const [m, l] = await Promise.all([
          fetch(`http://127.0.0.1:${memoryPort}/api/health`, {signal: AbortSignal.timeout(1000)}),
          fetch(`http://127.0.0.1:${livePort}/health`, {headers: {Authorization: 'Bearer ' + token}, signal: AbortSignal.timeout(1000)})]);
        const memoryStatus=await m.json(),liveStatus=await l.json();
        if (m.ok && l.ok && memoryStatus.configured && (cloudWorker || team || liveStatus.ttsConfigured))
          return {memory: `http://127.0.0.1:${memoryPort}`, live: `http://127.0.0.1:${livePort}`, liveToken: token,
            ...(cloudWorker ? {retire:retireCloudPair} : {})};
      } catch {}
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw Error('backend_start_timeout');
  } catch (error) {
    if (cloudWorker) retireCloudPair();
    else for (const child of own) child.kill('SIGTERM');
    throw error;
  }
}
const {server} = createGateway({key: fs.readFileSync(path.join(local, 'host.key')), cert: fs.readFileSync(path.join(local, 'host.crt')),
  invitation: config.invitation, maxPlayers: config.maxPlayers, registryFile: path.join(local, 'players.json'),
  verifyMember:memberVerifier(team),
  onActivity:cloudWorker ? onCloudActivity : null,
  async backend(id,member) {
    if (draining.has(id)) await draining.get(id);
    if (shuttingDown) throw Error('host_stopping');
    if(team){
      const scoped=new TeamClient(team.config,{certificate:team.certificate,sessionDirectory:path.join(local,'players',id,'team-session')});
      if(scoped.session()?.token!==member.token)scoped.save(member);
    }
    if (!sessions.has(id)) {
      const forget = () => { if (sessions.get(id) === pending) sessions.delete(id); };
      const pending = startPlayer(id, forget).catch(error => { forget(); throw error; });
      sessions.set(id, pending);
    }
    return sessions.get(id);
  }});
server.listen(config.port, config.address, () => {
  fs.writeFileSync(path.join(local, 'host-process.json'), JSON.stringify({pid: process.pid, address: config.address, port: config.port}), {mode: 0o600});
  console.log(`小岛主机已启动：https://${config.address}:${config.port}。游戏后台按玩家分别启动。`);
});
server.on('error', error => {console.error('主机启动失败：' + error.code); process.exitCode = 1; for (const child of children) child.kill('SIGTERM');});
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  shuttingDown = true;
  for (const timer of idleTimers.values()) clearTimeout(timer);
  idleTimers.clear();
  server.closeAllConnections(); server.close(); for (const child of children) child.kill('SIGTERM');
  setTimeout(() => {for (const child of children) child.kill('SIGKILL'); process.exit(0);}, 4000).unref();
});
