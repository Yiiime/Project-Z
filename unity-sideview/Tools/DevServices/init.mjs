// Create a private provider configuration outside the checkout. Existing files always win.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const args = process.argv.slice(2);
if (args.length && (args.length !== 1 || args[0] !== '--install')) throw Error('用法: node Tools/DevServices/init.mjs [--install]');
if (Number(process.versions.node.split('.')[0]) < 24) throw Error('游戏后台需要 Node.js 24 或以上版本。');

const tools = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = path.resolve(process.env.Z_DEV_CONFIG_DIR || path.join(os.homedir(), '.config/project-z/services'));
fs.mkdirSync(root, { recursive: true, mode: 0o700 });
const templates = {
  '.env.local': '# team uses the repository GitHub model gateway; direct uses host provider keys.\nZ_DEV_PROVIDER=team\n' +
    fs.readFileSync(path.join(tools, 'live-service/.env.example'), 'utf8'),
  'model.json': fs.readFileSync(path.join(tools, 'MemoryLab/model-config.example.json')),
  'tts.json': fs.readFileSync(path.join(tools, 'MemoryLab/tts-config.example.json'))
};
for (const [name, contents] of Object.entries(templates)) {
  try {
    fs.writeFileSync(path.join(root, name), contents, { flag: 'wx', mode: 0o600 });
    console.log('Created', path.join(root, name));
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    console.log('Using existing', path.join(root, name));
  }
}

if (args.includes('--install')) {
  for (const folder of ['MemoryLab', 'live-service']) {
    const install = spawnSync('npm', ['ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund'], {
      cwd: path.join(tools, folder), stdio: 'inherit'
    });
    if (install.error || install.status !== 0) throw Error(folder + ' 依赖安装失败，请检查 npm 与网络。');
  }
}
