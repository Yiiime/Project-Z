import test from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {mkdtempSync, readFileSync, rmSync, mkdirSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

const kit = new URL('../member-kit/', import.meta.url).pathname;

test('installer scripts are valid bash and pin verified downloads', () => {
  for (const file of ['install.sh', '安装.command', 'build.sh']) execFileSync('bash', ['-n', join(kit, file)]);
  const script = readFileSync(join(kit, 'install.sh'), 'utf8');
  for (const name of ['z_node_sha_arm64', 'z_node_sha_x64', 'z_gh_sha_arm64', 'z_gh_sha_x64', 'z_dotnet_sha_arm64', 'z_dotnet_sha_x64']) {
    assert.match(script, new RegExp(`${name}="[0-9a-f]{64,128}"`));
  }
  assert.doesNotMatch(script, /\bsudo\b/);
  assert.doesNotMatch(script, /auth token/);
});

test('check mode reports missing tools without writing a project', {skip: process.platform !== 'darwin' && 'installer runs only on macOS'}, () => {
  const home = mkdtempSync(join(tmpdir(), 'z-kit-home-'));
  try {
    mkdirSync(join(home, 'Desktop'));
    const output = execFileSync('bash', [join(kit, 'install.sh'), '--check'], {
      encoding: 'utf8', env: {HOME: home, PATH: '/usr/bin:/bin:/usr/sbin:/sbin', LANG: 'en_US.UTF-8'},
    });
    assert.match(output, /Node\.js 22 还没有安装/);
    assert.match(output, /检查完成/);
    assert.throws(() => readFileSync(join(home, 'Project-Z', '.git', 'HEAD')));
  } finally {
    rmSync(home, {recursive: true, force: true});
  }
});

test('member README covers both human and agent installation', () => {
  const readme = readFileSync(join(kit, 'README.md'), 'utf8');
  const agent = readFileSync(join(kit, 'AGENT.md'), 'utf8');
  assert.match(readme, /方法 A：让 Codex 帮你装/);
  assert.match(readme, /方法 B：自己双击安装/);
  assert.match(readme, /仍要打开/);
  assert.match(agent, /install\.sh" --check/);
  assert.match(agent, /不要使用 `sudo`/);
});
