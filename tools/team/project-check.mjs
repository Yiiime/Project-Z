import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync, statSync} from 'node:fs';
import {assertConfig} from './collab-core.mjs';

const files = execFileSync('git', ['ls-files', '-z'], {encoding: 'utf8'}).split('\0').filter(Boolean);
const localOnly = /(^|\/)(Library|Temp|Logs|UserSettings|TestResults|Builds|\.tooling|node_modules)\//i;
const secrets = /(^|\/)\.env($|\.)|\.(pem|key|p12|mobileprovision)$/i;
const tooLarge = [];
for (const path of files) {
  if (!path.endsWith('.env.example')) {
    assert.ok(!localOnly.test(path), `Local-only file tracked: ${path}`);
    assert.ok(!secrets.test(path), `Secret-like file tracked: ${path}`);
  }
  const size = statSync(path).size;
  if (size > 50 * 1024 * 1024) tooLarge.push(`${path} (${Math.round(size / 1024 / 1024)} MB)`);
}
assert.deepEqual(tooLarge, [], `Files above 50 MB need Git LFS or a release asset:\n${tooLarge.join('\n')}`);
assert.match(readFileSync('unity/ProjectSettings/ProjectVersion.txt', 'utf8'), /^m_EditorVersion: 6000\.3\.27f1$/m);
assert.match(readFileSync('AGENTS.md', 'utf8'), /unity\//);
const areas = assertConfig(JSON.parse(readFileSync('tools/team/areas.json', 'utf8')));
console.log(`Team project checks passed for ${files.length} tracked files and ${areas.areas.length} collaboration areas.`);
