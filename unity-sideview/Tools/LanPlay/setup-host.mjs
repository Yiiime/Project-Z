import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {randomBytes} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {loadTeamClient} from './team-access.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const local = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(directory, '.local'));
const addresses = Object.entries(os.networkInterfaces()).flatMap(([name, rows]) => rows
  .filter(row => row.family === 'IPv4' && !row.internal && /^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(row.address))
  .map(row => ({name, address: row.address})));
const requested = process.argv[2] || addresses.find(a => a.name === 'en0')?.address || addresses[0]?.address;
if (requested !== '127.0.0.1' && !addresses.some(a => a.address === requested)) throw Error('没有找到局域网地址。请连接 Wi-Fi，或传入本机私有 IPv4 地址。');
fs.mkdirSync(local, {recursive: true, mode: 0o700});
const configFile = path.join(local, 'host.json');
const teamModel = Boolean(loadTeamClient());
const port = Number(process.env.Z_LAN_PORT || 49460);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw Error('Z_LAN_PORT 应为 1024 到 65535 之间的整数。');
const capacity = Number(process.env.Z_LAN_MAX_PLAYERS || 6);
if (!Number.isInteger(capacity) || capacity < 1 || capacity > 64) throw Error('Z_LAN_MAX_PLAYERS 应为 1 到 64 的整数。');
let config;
if (fs.existsSync(configFile)) {
  config = JSON.parse(fs.readFileSync(configFile));
  if (typeof config.teamModel === 'boolean' && config.teamModel !== teamModel) throw Error('已有邀请使用不同认证方式。切换时请使用独立的 Z_LAN_HOST_ROOT 并签发新邀请。');
  if (process.env.Z_LAN_PORT && config.port !== port) throw Error('已有邀请使用不同端口。');
  if (config.address !== requested) throw Error('主机地址已变化。请保留 .local 中的玩家数据，停止主机后重新生成证书与客户端地址。');
} else {
  const sslConfig = path.join(local, 'certificate.cnf');
  fs.writeFileSync(sslConfig, `[req]\ndistinguished_name=dn\nx509_extensions=extensions\nprompt=no\n[dn]\nCN=Project Z LAN\n[extensions]\nsubjectAltName=IP:${requested}\nbasicConstraints=critical,CA:TRUE\nkeyUsage=critical,digitalSignature,keyEncipherment,keyCertSign\nextendedKeyUsage=serverAuth\n`);
  execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '365', '-config', sslConfig,
    '-keyout', path.join(local, 'host.key'), '-out', path.join(local, 'host.crt')], {stdio: 'ignore'});
  fs.chmodSync(path.join(local, 'host.key'), 0o600);
  config = {address: requested, port, maxPlayers: capacity, invitation: randomBytes(32).toString('hex')};
}
if (process.env.Z_LAN_MAX_PLAYERS) config.maxPlayers = capacity;
config.teamModel = teamModel;
fs.writeFileSync(configFile, JSON.stringify(config, null, 2), {mode: 0o600});
fs.writeFileSync(path.join(local, 'client-config.json'), JSON.stringify({version: 1, teamModel,
  serverUrl: `https://${config.address}:${config.port}`, invitation: config.invitation,
  certificate: fs.readFileSync(path.join(local, 'host.crt'), 'utf8')}, null, 2), {mode: 0o600});
console.log(`局域网主机配置就绪：https://${config.address}:${config.port}；最多 ${config.maxPlayers} 台试玩电脑。`);
console.log('将证书和邀请私下交给受邀成员；模型密钥、语音密钥和证书私钥仅保留在主机。');
