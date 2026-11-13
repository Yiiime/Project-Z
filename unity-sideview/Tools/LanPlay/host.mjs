import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {createGateway} from './gateway.mjs';
import {loadEnvironment} from '../live-service/server/providers.mjs';

const here = path.dirname(fileURLToPath(import.meta.url)), tools = path.dirname(here);
const local = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(here, '.local'));
const config = JSON.parse(fs.readFileSync(path.join(local, 'host.json')));
const configDirectory = process.env.Z_LIVE_CONFIG_DIR || path.join(tools, 'live-service');
const provider = loadEnvironment(configDirectory);
const modelFile = process.env.Z_MEMORY_CONFIG || path.join(tools, 'MemoryLab/.local/model.json');
if (!provider.DEEPSEEK_API_KEY) {try {provider.DEEPSEEK_API_KEY = JSON.parse(fs.readFileSync(modelFile)).apiKey;} catch {}}
if (!provider.DEEPSEEK_API_KEY || !provider.VOLC_TTS_API_KEY) throw Error('主机必须先配置模型和火山 TTS，玩家端无需配置。');
const sessions = new Map(), children = new Set();
const freePort = () => new Promise((resolve, reject) => {
  const server = http.createServer(); server.on('error', reject);
  server.listen(0, '127.0.0.1', () => {const port = server.address().port; server.close(() => resolve(port));});
});
async function startPlayer(id) {
  const root = path.join(local, 'players', id); fs.mkdirSync(root, {recursive: true, mode: 0o700});
  const memoryPort = await freePort(), livePort = await freePort(), own = [];
  const connection = path.join(root, 'live-connection.json');
  const env = {...process.env, ...Object.fromEntries(Object.entries(provider).filter(([, value]) => value)),
    GAME_MODEL_API_KEY: provider.DEEPSEEK_API_KEY, Z_MEMORY_CONFIG: modelFile,
    Z_TTS_CONFIG: process.env.Z_TTS_CONFIG || path.join(tools, 'MemoryLab/.local/tts.json'),
    Z_LIVE_OFFICE: '0', Z_MEMORY_PORT: String(memoryPort), Z_UNITY_PORT: String(livePort),
    Z_LIVE_CONFIG_DIR: configDirectory, Z_LIVE_RUNTIME_DIR: path.join(root, 'live'),
    Z_MEMORY_DB: path.join(root, 'memory.sqlite'), Z_LIVE_CONNECTION_FILE: connection,
    Z_LIVE_CHARACTER_SERVICE_URL: `http://127.0.0.1:${memoryPort}`};
  try {
    for (const [name, folder, entry] of [['memory', 'MemoryLab', 'server.mjs'], ['live', 'live-service', 'server/index.mjs']]) {
      const fd = fs.openSync(path.join(root, name + '.log'), 'a', 0o600);
      const child = spawn(process.execPath, [entry], {cwd: path.join(tools, folder), env, stdio: ['ignore', fd, fd]});
      fs.closeSync(fd); own.push(child); children.add(child);
      child.once('exit', () => {children.delete(child); sessions.delete(id); for (const peer of own) if (peer !== child && peer.exitCode === null) peer.kill('SIGTERM');});
    }
    for (let i = 0; i < 100; i++) {
      if (own.some(child => child.exitCode !== null)) throw Error('backend_start_failed');
      try {
        const token = fs.readFileSync(path.join(root, 'live/local-token'), 'utf8').trim();
        const [m, l] = await Promise.all([
          fetch(`http://127.0.0.1:${memoryPort}/api/health`, {signal: AbortSignal.timeout(1000)}),
          fetch(`http://127.0.0.1:${livePort}/health`, {headers: {Authorization: 'Bearer ' + token}, signal: AbortSignal.timeout(1000)})]);
        if (m.ok && l.ok && (await m.json()).configured && (await l.json()).ttsConfigured)
          return {memory: `http://127.0.0.1:${memoryPort}`, live: `http://127.0.0.1:${livePort}`, liveToken: token};
      } catch {}
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw Error('backend_start_timeout');
  } catch (error) {for (const child of own) child.kill('SIGTERM'); throw error;}
}
const {server} = createGateway({key: fs.readFileSync(path.join(local, 'host.key')), cert: fs.readFileSync(path.join(local, 'host.crt')),
  invitation: config.invitation, maxPlayers: config.maxPlayers, registryFile: path.join(local, 'players.json'),
  backend(id) {if (!sessions.has(id)) sessions.set(id, startPlayer(id).catch(error => {sessions.delete(id); throw error;})); return sessions.get(id);}});
server.listen(config.port, config.address, () => {
  fs.writeFileSync(path.join(local, 'host-process.json'), JSON.stringify({pid: process.pid, address: config.address, port: config.port}), {mode: 0o600});
  console.log(`小岛主机已启动：https://${config.address}:${config.port}。请保持 Mac 开机、联网且不合盖。`);
});
server.on('error', error => {console.error('主机启动失败：' + error.code); process.exitCode = 1; for (const child of children) child.kill('SIGTERM');});
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  server.closeAllConnections(); server.close(); for (const child of children) child.kill('SIGTERM');
  setTimeout(() => {for (const child of children) child.kill('SIGKILL'); process.exit(0);}, 4000).unref();
});
