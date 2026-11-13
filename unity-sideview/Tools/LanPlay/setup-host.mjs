import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {randomBytes} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

const directory = path.dirname(fileURLToPath(import.meta.url));
const local = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(directory, '.local'));
const addresses = Object.entries(os.networkInterfaces()).flatMap(([name, rows]) => rows
  .filter(row => row.family === 'IPv4' && !row.internal && /^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(row.address))
  .map(row => ({name, address: row.address})));
const requested = process.argv[2] || addresses.find(a => a.name === 'en0')?.address || addresses[0]?.address;
if (!addresses.some(a => a.address === requested)) throw Error('没有找到局域网地址。请连接 Wi-Fi，或传入本机私有 IPv4 地址。');
fs.mkdirSync(local, {recursive: true, mode: 0o700});
const configFile = path.join(local, 'host.json');
let config;
if (fs.existsSync(configFile)) {
  config = JSON.parse(fs.readFileSync(configFile));
  if (config.address !== requested) throw Error('主机地址已变化。请保留 .local 中的玩家数据，停止主机后重新生成证书与客户端地址。');
} else {
  const sslConfig = path.join(local, 'certificate.cnf');
  fs.writeFileSync(sslConfig, `[req]\ndistinguished_name=dn\nx509_extensions=extensions\nprompt=no\n[dn]\nCN=Project Z LAN\n[extensions]\nsubjectAltName=IP:${requested}\nbasicConstraints=critical,CA:TRUE\nkeyUsage=critical,digitalSignature,keyEncipherment,keyCertSign\nextendedKeyUsage=serverAuth\n`);
  execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '365', '-config', sslConfig,
    '-keyout', path.join(local, 'host.key'), '-out', path.join(local, 'host.crt')], {stdio: 'ignore'});
  fs.chmodSync(path.join(local, 'host.key'), 0o600);
  config = {address: requested, port: 49460, maxPlayers: 6, invitation: randomBytes(32).toString('hex')};
  fs.writeFileSync(configFile, JSON.stringify(config, null, 2), {mode: 0o600});
}
fs.writeFileSync(path.join(local, 'client-config.json'), JSON.stringify({version: 1,
  serverUrl: `https://${config.address}:${config.port}`, invitation: config.invitation,
  certificate: fs.readFileSync(path.join(local, 'host.crt'), 'utf8')}, null, 2), {mode: 0o600});
console.log(`局域网主机配置就绪：https://${config.address}:${config.port}；最多 ${config.maxPlayers} 台试玩电脑。`);
console.log('公开证书和游戏邀请随包分发；模型密钥、语音密钥和证书私钥仅保留在主机。');
