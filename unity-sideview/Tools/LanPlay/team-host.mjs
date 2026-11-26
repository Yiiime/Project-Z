// Run the trusted game gateway from any checkout; keys and identities live outside it.
import path from 'node:path';
import os from 'node:os';
import {loadProfile} from '../DevServices/profile.mjs';

const [action, address] = process.argv.slice(2);
if (!['setup', 'start'].includes(action) || (action === 'start' && address))
  throw Error('用法: node Tools/LanPlay/team-host.mjs setup [本机私网 IP | 127.0.0.1] 或 start');
if (Number(process.versions.node.split('.')[0]) < 24) throw Error('游戏后台需要 Node.js 24 或以上版本。');
const root = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(os.homedir(), '.local/share/project-z/host'));
const {env} = loadProfile({stateDir:root});
Object.assign(process.env, env, {Z_LAN_HOST_ROOT:root});
if (action === 'setup') {
  // Bind to loopback by default for SSH forwarding. A private LAN address is opt-in.
  process.argv.splice(2, process.argv.length, address || '127.0.0.1');
  await import('./setup-host.mjs');
  console.log('客户端邀请文件:', path.join(root, 'client-config.json'));
} else {
  await import('./host.mjs');
}
