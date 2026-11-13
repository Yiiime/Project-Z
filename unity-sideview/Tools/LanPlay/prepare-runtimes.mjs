import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(here, '.local')), 'runtimes');
fs.mkdirSync(root, {recursive: true});
// Official nodejs.org v24.13.0 SHASUMS256.txt; no third-party runtime mirror.
const hashes = {arm64: 'd595961e563fcae057d4a0fb992f175a54d97fcc4a14dc2d474d92ddeea3b9f8',
  x64: '6f03c1b48ddbe1b129a6f8038be08e0899f05f17185b4d3e4350180ab669a7f3'};
for (const [arch, checksum] of Object.entries(hashes)) {
  const name = 'node-v24.13.0-darwin-' + arch;
  const response = await fetch('https://nodejs.org/dist/v24.13.0/' + name + '.tar.gz', {signal: AbortSignal.timeout(180000)});
  if (!response.ok) throw Error('Official Node download failed: ' + response.status);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (createHash('sha256').update(bytes).digest('hex') !== checksum) throw Error('Node checksum mismatch');
  fs.writeFileSync(path.join(root, name + '.tar.gz'), bytes);
  execFileSync('tar', ['-xzf', path.join(root, name + '.tar.gz'), '-C', root]);
  fs.copyFileSync(path.join(root, name, 'bin/node'), path.join(root, 'node-' + arch)); fs.chmodSync(path.join(root, 'node-' + arch), 0o755);
  fs.copyFileSync(path.join(root, name, 'LICENSE'), path.join(root, 'NODE-' + arch.toUpperCase() + '-LICENSE.txt'));
  console.log('Verified official Node 24.13.0 for ' + arch);
}
