import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync, statSync} from 'node:fs';
import {assertConfig} from './collab-core.mjs';

const gitOutput = {encoding: 'utf8', maxBuffer: 32 * 1024 * 1024};
const records = execFileSync('git', [
  'ls-files', '-z', '--format=%(stage) %(objectsize) %(objectname) %(path)',
], gitOutput).split('\0').filter(Boolean);
const localOnly = /(^|\/)(Library|Temp|Logs|UserSettings|TestResults|Builds|\.tooling|node_modules)\//i;
const secrets = /(^|\/)\.env($|\.)|\.(pem|key|p12|mobileprovision)$/i;
const tooLarge = [];
const largeFileLimit = 50 * 1024 * 1024;
const pointerFormat = /^version https:\/\/git-lfs\.github\.com\/spec\/v1\n(?:ext-\d+-[\w.-]+ sha256:[0-9a-f]{64}\n)*oid sha256:[0-9a-f]{64}\nsize (0|[1-9]\d*)\n$/;

function matchesLfsPointer(path, objectId, indexSize, workingSize) {
  // A Git LFS pointer is a small text blob in the index. The worktree may
  // contain either that pointer (CI) or the downloaded asset (Unity).
  if (indexSize > 1024) return false;
  const attribute = execFileSync('git', ['check-attr', '--cached', '-z', 'filter', '--', path], gitOutput);
  if (attribute.split('\0')[2] !== 'lfs') return false;
  const pointer = execFileSync('git', ['cat-file', 'blob', objectId], gitOutput);
  const match = pointer.match(pointerFormat);
  return match !== null && match[0] === pointer && Number(match[1]) === workingSize;
}

for (const record of records) {
  const entry = /^0 (\d+) ([0-9a-f]+) (.+)$/s.exec(record);
  assert.ok(entry, `Unmerged or unsupported index entry: ${record}`);
  const [, bytes, objectId, path] = entry;
  const indexSize = Number(bytes);
  if (!path.endsWith('.env.example')) {
    assert.ok(!localOnly.test(path), `Local-only file tracked: ${path}`);
    assert.ok(!secrets.test(path), `Secret-like file tracked: ${path}`);
  }
  const workingSize = statSync(path).size;
  if (indexSize > largeFileLimit ||
      (workingSize > largeFileLimit && !matchesLfsPointer(path, objectId, indexSize, workingSize))) {
    tooLarge.push(`${path} (${Math.round(Math.max(indexSize, workingSize) / 1024 / 1024)} MB)`);
  }
}
assert.deepEqual(tooLarge, [], `Files above 50 MB need Git LFS or a release asset:\n${tooLarge.join('\n')}`);
assert.match(readFileSync('unity-sideview/ProjectSettings/ProjectVersion.txt', 'utf8'), /^m_EditorVersion: 6000\.3\.27f1$/m);
assert.match(readFileSync('AGENTS.md', 'utf8'), /unity-sideview\//);
const areas = assertConfig(JSON.parse(readFileSync('tools/team/areas.json', 'utf8')));
console.log(`Team project checks passed for ${records.length} tracked files and ${areas.areas.length} collaboration areas.`);
