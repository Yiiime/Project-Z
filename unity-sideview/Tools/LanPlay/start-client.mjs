import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn, spawnSync} from 'node:child_process';
import {digest} from './protocol.mjs';
import {teamForInvitation} from './team-access.mjs';

const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(here, '../..');
const args = process.argv.slice(2);
function option(name) {
  const i = args.indexOf(name);
  if (i < 0) return null;
  if (!args[i + 1] || args[i + 1].startsWith('--')) throw Error(name + ' 缺少参数。');
  return args[i + 1];
}
const editor = args.includes('--editor'), appArg = option('--app'), app = appArg && path.resolve(appArg);
const sshAlias = option('--ssh');
if (editor && app) throw Error('请在 --editor 和 --app 中选择一个。');
if (app && (!app.endsWith('.app') || !fs.statSync(app, {throwIfNoEntry: false})?.isDirectory()))
  throw Error('--app 需要现有的 macOS 游戏应用目录。');
const configFile = path.resolve(option('--config') || path.join(root, 'connection-config.json'));
let config;
try { config = JSON.parse(fs.readFileSync(configFile, 'utf8')); }
catch { throw Error('无法读取主机连接文件，请确认 --config 指向主机导出的 client-config.json。'); }

if (sshAlias) {
  const {ensureSshTunnel} = await import('./ssh-tunnel.mjs');
  await ensureSshTunnel(sshAlias, config);
}
const team = teamForInvitation(config);
if (team) {
  let connected = false;
  try { if (team.session()) { await team.verify(); connected = true; } } catch {}
  if (!connected) {
    if (args.includes('--check')) {
      if (!editor && !app) spawn('/usr/bin/open', [path.join(root,'连接团队.command')], {stdio:'ignore'}).unref();
      console.error('请先运行 Tools/team-model/login.mjs 完成团队登录。');
      process.exit(78);
    }
    const login = spawnSync(process.execPath,[path.join(here,'../team-model/login.mjs')],{stdio:'inherit'});
    if (login.status !== 0) throw Error('请先用有项目权限的 GitHub 账号登录。');
  }
}
// Separate trusted hosts and their player registrations in a development checkout.
// The original packaged client keeps its existing registration and save directory.
const runtime = editor || app
  ? path.join(root, 'UserSettings/ZLive/hosts', digest(config.serverUrl + config.certificate))
  : path.join(root, '.local/lan-client');
fs.mkdirSync(runtime, {recursive: true, mode: 0o700});
const stateFile = path.join(runtime, 'relay.json');
const health = async (url, token) => {try {const res = await fetch(url, {headers: token ? {Authorization: 'Bearer ' + token} : {}, signal: AbortSignal.timeout(3000)}); return res.ok ? await res.json() : null;} catch {return null;}};
let state;
try {state = JSON.parse(fs.readFileSync(stateFile));} catch {}
if (state?.fingerprint !== digest(JSON.stringify(config)) || (state?.teamFingerprint??null)!==(team?digest(team.origin+team.certificate):null) || !(await health(state?.connection?.baseUrl + '/relay-health', state?.connection?.token))?.ok) {
  const log = fs.openSync(path.join(runtime, 'connection.log'), 'a', 0o600);
  const child = spawn(process.execPath, [path.join(here, 'relay.mjs'), configFile, runtime], {detached: true, stdio: ['ignore', log, log]});
  child.unref(); fs.closeSync(log); state = null;
  for (let i = 0; i < 80; i++) {
    if (child.exitCode !== null) break;
    try {const next = JSON.parse(fs.readFileSync(stateFile)); if (next.pid === child.pid) {state = next; break;}} catch {}
    await new Promise(resolve => setTimeout(resolve, 250));
  }
}
if (!state) throw Error('无法建立到主机的连接。详情见 ' + path.join(runtime, 'connection.log'));
let ready = false, speechConfigured = false;
for (let i = 0; i < 12; i++) {
  const [memory, live] = await Promise.all([health(state.memoryUrl + '/api/health'), health(state.connection.baseUrl + '/health', state.connection.token)]);
  speechConfigured = Boolean(memory?.speech?.configured && live?.ttsConfigured);
  if (memory?.configured && live?.agentConfigured && (team || speechConfigured)) {ready = true; break;}
  await new Promise(resolve => setTimeout(resolve, 500));
}
if (!ready) throw Error('主机的模型或语音暂未就绪，请让主机管理员检查服务；玩家无需填写 API。');
fs.writeFileSync(path.join(runtime, 'game-connection.json'), JSON.stringify({memoryUrl: state.memoryUrl}), {mode: 0o600});
if (editor) {
  const {writeEditorConnection} = await import('../editor-connection.mjs');
  writeEditorConnection(root, {memoryUrl:state.memoryUrl, ...state.connection});
}
console.log('已连接主机。角色模型与直播模型已配置，语音配置' + (speechConfigured ? '已就绪。' : '尚未就绪。'));
if (editor) { console.log('在 macOS Unity 编辑器中先选择「Seabreeze → 服务连接 → 本地开发」，然后进入 Play。'); process.exit(0); }
if (process.argv.includes('--check')) {console.log('局域网启动自检通过。'); process.exit(0);}
const gameApp = app || path.join(root, 'ProjectZ-Day01.app');
const result = spawnSync('/usr/bin/open', ['-n', gameApp, '--env', 'Z_MEMORY_SAVE_ROOT=' + path.join(runtime, 'saves'),
  '--env', 'Z_LIVE_CONNECTION_FILE=' + path.join(runtime, 'connection.json'), '--args', '-memoryService', state.memoryUrl,
  '-screen-fullscreen', '0', '-screen-width', '1440', '-screen-height', '900', '-logFile', path.join(runtime, 'player.log')], {stdio: 'inherit'});
if (result.status !== 0) throw Error('游戏启动失败，请确认应用完整解压到可写文件夹。');
