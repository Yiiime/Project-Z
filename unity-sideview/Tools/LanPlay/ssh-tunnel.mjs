import tls from 'node:tls';
import {X509Certificate} from 'node:crypto';
import {spawn} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';

// The host invitation contains the certificate for its loopback TLS listener.
function probe(port, certificate, fingerprint) {
  return new Promise(resolve => {
    const socket = tls.connect({host: '127.0.0.1', port, ca: certificate, rejectUnauthorized: true});
    const finish = status => { socket.destroy(); resolve(status); };
    socket.setTimeout(1200, () => finish('untrusted'));
    socket.once('secureConnect', () => finish(socket.getPeerCertificate().fingerprint256 === fingerprint ? 'ready' : 'untrusted'));
    socket.once('error', error => finish(error.code === 'ECONNREFUSED' ? 'absent' : 'untrusted'));
    socket.once('close', () => resolve('untrusted'));
  });
}

export async function ensureSshTunnel(alias, config) {
  if (!/^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,252}$/.test(alias))
    throw Error('--ssh 需要已有的 SSH 主机别名。');
  const origin = new URL(config.serverUrl);
  if (origin.hostname !== '127.0.0.1' || origin.protocol !== 'https:' || origin.username || origin.password ||
      origin.pathname !== '/' || origin.search || origin.hash || !origin.port || !config.certificate)
    throw Error('--ssh 需要主机按 127.0.0.1 生成的 HTTPS 回环邀请。');
  const port = Number(origin.port);
  let fingerprint;
  try { fingerprint = new X509Certificate(config.certificate).fingerprint256; }
  catch { throw Error('邀请中的网关证书无效。'); }
  const prior = await probe(port, config.certificate, fingerprint);
  if (prior === 'ready') return;
  if (prior !== 'absent') throw Error('本机邀请端口已被占用，端点未通过邀请证书验证。');

  const child = spawn('ssh', ['-NTax', '-S', 'none', '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes',
    '-o', 'UpdateHostKeys=no', '-o', 'GatewayPorts=no', '-o', 'ExitOnForwardFailure=yes', '-o', 'ConnectTimeout=8',
    '-o', 'ForkAfterAuthentication=no', '-o', 'ServerAliveInterval=30', '-o', 'ServerAliveCountMax=3',
    '-L', '127.0.0.1:' + port + ':127.0.0.1:' + port, '--', alias], {detached: true, stdio: 'ignore'});
  let spawnError;
  child.once('error', error => { spawnError = error; });
  try {
    const deadline = Date.now() + 12000;
    do {
      await delay(180);
      // A concurrent client can win the port bind. Its validated tunnel is reusable too.
      if (await probe(port, config.certificate, fingerprint) === 'ready') { child.unref(); return; }
      if (spawnError?.code === 'ENOENT') throw Error('未找到 OpenSSH 客户端 ssh。');
      if (spawnError || child.exitCode !== null || child.signalCode)
        throw Error('SSH 连接失败。请确认别名、已核对的主机指纹、可非交互使用的 SSH 凭据，以及转发权限。');
    } while (Date.now() < deadline);
    throw Error('SSH 已尝试转发，但远端网关尚未通过 TLS 验证。请检查主机服务、证书和回环端口。');
  } catch (error) {
    if (child.pid && child.exitCode === null && !child.signalCode) child.kill();
    throw error;
  }
}
