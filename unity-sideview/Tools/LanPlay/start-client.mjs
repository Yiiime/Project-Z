import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn, spawnSync} from 'node:child_process';
import {digest} from './protocol.mjs';

const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../..');
const configFile = path.join(root, 'connection-config.json'), config = JSON.parse(fs.readFileSync(configFile));
const runtime = path.join(root, '.local/lan-client'); fs.mkdirSync(runtime, {recursive: true, mode: 0o700});
const stateFile = path.join(runtime, 'relay.json');
const health = async (url, token) => {try {const res = await fetch(url, {headers: token ? {Authorization: 'Bearer ' + token} : {}, signal: AbortSignal.timeout(3000)}); return res.ok ? await res.json() : null;} catch {return null;}};
let state;
try {state = JSON.parse(fs.readFileSync(stateFile));} catch {}
if (state?.fingerprint !== digest(JSON.stringify(config)) || !(await health(state?.connection?.baseUrl + '/relay-health', state?.connection?.token))?.ok) {
  const log = fs.openSync(path.join(runtime, 'connection.log'), 'a', 0o600);
  const child = spawn(process.execPath, [path.join(here, 'relay.mjs'), configFile, runtime], {detached: true, stdio: ['ignore', log, log]});
  child.unref(); fs.closeSync(log); state = null;
  for (let i = 0; i < 80; i++) {
    if (child.exitCode !== null) break;
    try {const next = JSON.parse(fs.readFileSync(stateFile)); if (next.pid === child.pid) {state = next; break;}} catch {}
    await new Promise(resolve => setTimeout(resolve, 250));
  }
}
if (!state) throw Error('连接不到小岛主机。请确认同一 Wi-Fi／局域网、主机已启动，且没有阻止局域网访问。详情见 .local/lan-client/connection.log。');
let ready = false;
for (let i = 0; i < 12; i++) {
  const [memory, live] = await Promise.all([health(state.memoryUrl + '/api/health'), health(state.connection.baseUrl + '/health', state.connection.token)]);
  if (memory?.configured && memory?.speech?.configured && live?.agentConfigured && live?.ttsConfigured) {ready = true; break;}
  await new Promise(resolve => setTimeout(resolve, 500));
}
if (!ready) throw Error('主机的模型或语音暂未就绪，请让主机管理员检查服务；玩家无需填写 API。');
fs.writeFileSync(path.join(runtime, 'game-connection.json'), JSON.stringify({memoryUrl: state.memoryUrl}), {mode: 0o600});
console.log('小岛已连接：模型、角色语音和直播均由主机提供。');
if (process.argv.includes('--check')) {console.log('局域网启动自检通过。'); process.exit(0);}
const app = path.join(root, 'ProjectZ-Day01.app');
const result = spawnSync('/usr/bin/open', ['-n', app, '--env', 'Z_MEMORY_SAVE_ROOT=' + path.join(runtime, 'saves'),
  '--env', 'Z_LIVE_CONNECTION_FILE=' + path.join(runtime, 'connection.json'), '--args', '-memoryService', state.memoryUrl,
  '-screen-fullscreen', '0', '-screen-width', '1440', '-screen-height', '900', '-logFile', path.join(runtime, 'player.log')], {stdio: 'inherit'});
if (result.status !== 0) throw Error('游戏启动失败，请确认应用完整解压到可写文件夹。');
