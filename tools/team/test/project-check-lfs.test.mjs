import test, {after} from 'node:test';
import assert from 'node:assert/strict';
import {copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, truncateSync, writeFileSync} from 'node:fs';
import {execFileSync, spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {join} from 'node:path';
import {tmpdir} from 'node:os';

const repoRoot = fileURLToPath(new URL('../../../', import.meta.url));
const checkScript = fileURLToPath(new URL('../project-check.mjs', import.meta.url));
const largeFileLimit = 50 * 1024 * 1024;
const gitEnv = {
  ...process.env,
  GIT_CONFIG_NOSYSTEM: '1',
  GIT_CONFIG_GLOBAL: '/dev/null',
  GIT_AUTHOR_NAME: 'project-check-lfs-test',
  GIT_AUTHOR_EMAIL: 'project-check-lfs-test@example.invalid',
  GIT_COMMITTER_NAME: 'project-check-lfs-test',
  GIT_COMMITTER_EMAIL: 'project-check-lfs-test@example.invalid',
};
const roots = [];

after(() => {
  for (const root of roots) rmSync(root, {recursive: true, force: true});
});

function git(root, args) {
  return execFileSync('git', args, {cwd: root, env: gitEnv, encoding: 'utf8'});
}

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'project-check-lfs-'));
  roots.push(root);
  mkdirSync(join(root, 'tools/team'), {recursive: true});
  mkdirSync(join(root, 'unity-sideview/ProjectSettings'), {recursive: true});
  copyFileSync(checkScript, join(root, 'tools/team/project-check.mjs'));
  copyFileSync(join(repoRoot, 'tools/team/collab-core.mjs'), join(root, 'tools/team/collab-core.mjs'));
  copyFileSync(join(repoRoot, 'tools/team/areas.json'), join(root, 'tools/team/areas.json'));
  writeFileSync(join(root, 'AGENTS.md'), 'unity-sideview/\n');
  writeFileSync(join(root, 'unity-sideview/ProjectSettings/ProjectVersion.txt'), 'm_EditorVersion: 6000.3.27f1\n');
  git(root, ['init', '-q']);
  return root;
}

function commitFixture(root) {
  git(root, ['add', '.']);
  git(root, ['commit', '-qm', 'project-check-lfs fixture']);
}

function lfsPointer(size) {
  return [
    'version https://git-lfs.github.com/spec/v1',
    'oid sha256:' + 'a'.repeat(64),
    'size ' + size,
    '',
  ].join('\n');
}

function runCheck(root) {
  const result = spawnSync(process.execPath, [join(root, 'tools/team/project-check.mjs')], {
    cwd: root,
    env: gitEnv,
    encoding: 'utf8',
  });
  return result;
}

function assertCheckPasses(root) {
  const result = runCheck(root);
  assert.equal(result.status, 0, result.stderr || result.stdout);
}

function assertCheckFails(root, text) {
  const result = runCheck(root);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, new RegExp(text));
}

test('project check accepts a Git LFS pointer checkout', () => {
  const root = fixture();
  const path = join(root, 'large.bin');
  writeFileSync(join(root, '.gitattributes'), 'large.bin filter=lfs diff=lfs merge=lfs -text\n');
  writeFileSync(path, lfsPointer(largeFileLimit + 1));
  commitFixture(root);
  assertCheckPasses(root);
});

test('project check accepts a downloaded Git LFS asset with the declared size', () => {
  const root = fixture();
  const path = join(root, 'large.bin');
  writeFileSync(join(root, '.gitattributes'), 'large.bin filter=lfs diff=lfs merge=lfs -text\n');
  writeFileSync(path, lfsPointer(largeFileLimit + 1));
  commitFixture(root);
  truncateSync(path, largeFileLimit + 1);
  assertCheckPasses(root);
});

test('project check rejects a staged large blob even when the worktree is small', () => {
  const root = fixture();
  const path = join(root, 'large.bin');
  writeFileSync(path, '');
  truncateSync(path, largeFileLimit + 1);
  commitFixture(root);
  writeFileSync(path, 'small worktree replacement\n');
  assertCheckFails(root, 'large\.bin');
});

test('project check rejects a downloaded asset whose size disagrees with its pointer', () => {
  const root = fixture();
  const path = join(root, 'large.bin');
  writeFileSync(join(root, '.gitattributes'), 'large.bin filter=lfs diff=lfs merge=lfs -text\n');
  writeFileSync(path, lfsPointer(largeFileLimit + 1));
  commitFixture(root);
  truncateSync(path, largeFileLimit + 2);
  assertCheckFails(root, 'large\.bin');
});
