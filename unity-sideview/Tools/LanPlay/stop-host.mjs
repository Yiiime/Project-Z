import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
const here = path.dirname(fileURLToPath(import.meta.url)), root = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(here, '.local'));
const file = path.join(root, 'host-process.json');
if (!fs.existsSync(file)) {console.log('局域网主机没有启动记录。'); process.exit(0);}
const {pid} = JSON.parse(fs.readFileSync(file));
if (!Number.isInteger(pid) || pid < 2) throw Error('无效主机进程记录。');
let command = '';
try {command = execFileSync('/bin/ps', ['-p', String(pid), '-o', 'command='], {encoding: 'utf8'});} catch {}
if (!command.includes(path.join(here, 'host.mjs'))) {console.log('该局域网主机已停止；没有操作其他进程。'); process.exit(0);}
process.kill(pid, 'SIGTERM');
console.log('已请求停止局域网主机。玩家数据保留，当前本机单人试玩服务不受影响。');
