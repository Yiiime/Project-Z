import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import net from 'node:net';
import {fileURLToPath} from 'node:url';
import {spawn, spawnSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {hostEnvironment} from './config.mjs';

const host = path.dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(fs.readFileSync(path.join(host, 'manifest.json')));
const runtime = path.resolve(process.env.Z_PORTABLE_DATA_DIR || path.join(os.homedir(), 'Library/Application Support/Project Z Portable'));
const lock = path.join(runtime, 'launcher.lock');
const children = new Set();
let ownsLock = false, stopping = false;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function cleanup() {
  if (stopping) return;
  stopping = true;
  for (const child of children) if (child.exitCode === null) child.kill('SIGTERM');
  for (let i = 0; i < 30 && [...children].some(c => c.exitCode === null && c.signalCode === null); i++) await delay(100);
  for (const child of children) if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
  if (ownsLock) fs.rmSync(lock, {recursive:true, force:true});
}
for (const signal of ['SIGTERM', 'SIGINT', 'SIGHUP']) process.once(signal, async () => { await cleanup(); process.exit(0); });
function acquireLock() {
  fs.mkdirSync(runtime, {recursive:true, mode:0o700});
  try { fs.mkdirSync(lock, {mode:0o700}); }
  catch (error) {
    if (error.code !== 'EEXIST') throw error;
    let owner;
    try { owner = JSON.parse(fs.readFileSync(path.join(lock, 'owner.json'))); } catch {}
    if (!Number.isInteger(owner?.pid) || owner.pid <= 1) throw Error('already_running');
    try { process.kill(owner.pid, 0); throw Error('already_running'); }
    catch (e) { if (e.code !== 'ESRCH') throw e; }
    fs.rmSync(lock, {recursive:true});
    fs.mkdirSync(lock, {mode:0o700});
  }
  ownsLock = true;
  fs.writeFileSync(path.join(lock, 'owner.json'), JSON.stringify({pid:process.pid}), {mode:0o600});
}
async function freePort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return port;
}
function service(name, tools, session, env) {
  const cwd = path.join(tools, name === 'memory' ? 'MemoryLab' : 'live-service');
  const entry = path.join(cwd, name === 'memory' ? 'server.mjs' : 'server/index.mjs');
  const fd = fs.openSync(path.join(session, name + '.log'), 'a', 0o600);
  const child = spawn(process.execPath, [path.join(host, 'service-runner.mjs'), entry], {cwd, env, stdio:['ignore', fd, fd, 'ipc']});
  fs.closeSync(fd);
  children.add(child);
  child.on('error', () => { child.failed = true; });
  return child;
}
async function health(url, headers = {}) {
  try { const r = await fetch(url, {headers, signal:AbortSignal.timeout(1500)}); return r.ok ? await r.json() : null; } catch { return null; }
}
async function main() {
  acquireLock();
  const session = path.join(runtime, 'runs', randomUUID());
  fs.mkdirSync(session, {recursive:true, mode:0o700});
  const release = path.join(runtime, 'releases', manifest.packageId);
  const tools = path.join(release, 'Tools');
  // Services create scratch files relative to their own source. Give them a private
  // writable copy, leaving the signed app untouched (also works after relocation).
  if (!fs.existsSync(path.join(release, 'ready'))) {
    fs.rmSync(release, {recursive:true, force:true});
    fs.mkdirSync(release, {recursive:true, mode:0o700});
    fs.cpSync(path.join(host, 'Tools'), tools, {recursive:true});
    fs.writeFileSync(path.join(release, 'ready'), '1');
  }
  const seedSession = path.join(host, 'Config/netease-session.json');
  fs.mkdirSync(path.join(runtime, 'config'), {recursive:true, mode:0o700});
  for (const name of ['model.json', 'tts.json']) {
    const seed = path.join(host, 'Config', name), target = path.join(runtime, 'config', name);
    if (fs.existsSync(seed) && !fs.existsSync(target)) { fs.copyFileSync(seed, target); fs.chmodSync(target, 0o600); }
  }
  const sessionTarget = path.join(runtime, 'netease/session.json');
  if (fs.existsSync(seedSession) && !fs.existsSync(sessionTarget)) {
    fs.mkdirSync(path.dirname(sessionTarget), {recursive:true, mode:0o700});
    fs.copyFileSync(seedSession, sessionTarget); fs.chmodSync(sessionTarget, 0o600);
  }
  const config = JSON.parse(fs.readFileSync(path.join(host, 'Config/providers.json')));
  const memoryPort = await freePort();
  let livePort; do { livePort = await freePort(); } while (livePort === memoryPort);
  const env = hostEnvironment(config, {host, tools, runtime, session, memoryPort, livePort});
  const memoryUrl = `http://127.0.0.1:${memoryPort}`, liveUrl = `http://127.0.0.1:${livePort}`;
  const services = [service('memory', tools, session, env), service('live', tools, session, env)];
  let ready = false, memory, live;
  for (let i = 0; i < 120; i++) {
    if (stopping) return;
    if (services.some(c => c.failed || c.exitCode !== null || c.signalCode !== null)) throw Error('service_failed');
    let connection;
    try { connection = JSON.parse(fs.readFileSync(env.Z_LIVE_CONNECTION_FILE)); } catch {}
    [memory, live] = await Promise.all([health(memoryUrl + '/api/health'), connection?.baseUrl === liveUrl ? health(liveUrl + '/health', {Authorization:'Bearer ' + connection.token}) : null]);
    if (memory?.configured && live?.ok) { ready = true; break; }
    await delay(250);
  }
  if (!ready) throw Error('service_not_ready');
  fs.writeFileSync(path.join(runtime, 'latest-run.json'), JSON.stringify({session, memoryUrl, liveUrl, pid:process.pid, services:services.map(c => c.pid), sourceRevision:manifest.sourceRevision}), {mode:0o600});
  console.log('角色与直播服务已就绪。');
  if (process.argv.includes('--check')) {
    console.log(JSON.stringify({ready:true, teamLoginRequired:false, sourceRevision:manifest.sourceRevision}));
    return;
  }
  if (process.argv.includes('--serve')) {
    await new Promise(resolve => { for (const child of services) child.once('exit', resolve); });
    if (!stopping) throw Error('service_stopped');
    return;
  }
  const executable = path.resolve(host, '../../MacOS', manifest.playerExecutable);
  const game = spawn(executable, ['-memoryService', memoryUrl, '-screen-fullscreen', '0', '-screen-width', '1440', '-screen-height', '900', '-logFile', path.join(session, 'player.log'), ...process.argv.slice(2)], {env, stdio:'ignore'});
  children.add(game);
  const exited = new Promise((resolve, reject) => { game.once('exit', code => code === 0 ? resolve() : reject(Error('player_failed'))); game.once('error', () => reject(Error('player_failed'))); });
  for (const child of services) child.once('exit', () => { if (!stopping && game.exitCode === null) game.kill('SIGTERM'); });
  await exited;
}
try { await main(); }
catch (error) {
  // Never print provider responses, keys or raw exception stacks to recipients.
  const message = error.message === 'already_running' ? '游戏已经在运行，请先退出已有窗口。' : '启动未完成，请查看运行目录中的日志：' + runtime;
  console.error(message);
  if (!process.argv.some(v => v === '--check' || v === '--serve')) spawnSync('/usr/bin/osascript', ['-e', 'on run argv\n display alert "Project Z" message (item 1 of argv)\nend run', message], {stdio:'ignore'});
  process.exitCode = 1;
} finally { await cleanup(); }
