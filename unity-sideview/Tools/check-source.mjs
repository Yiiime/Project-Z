import assert from 'node:assert/strict';
import {readFileSync, readdirSync, existsSync} from 'node:fs';
import {join, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
process.chdir(join(dirname(fileURLToPath(import.meta.url)), '..'));
const walk = dir => readdirSync(dir, {withFileTypes:true}).flatMap(e => e.isDirectory() ? walk(join(dir,e.name)) : [join(dir,e.name)]);
const files = walk('Assets');
const guids = new Map();
for (const f of files) {
  if (!f.endsWith('.meta')) { assert.ok(existsSync(f+'.meta'), `Missing meta: ${f}`); continue; }
  assert.ok(existsSync(f.slice(0,-5)), `Orphan meta: ${f}`);
  const guid = readFileSync(f,'utf8').match(/^guid: ([0-9a-f]{32})$/m)?.[1];
  assert.ok(guid, `Invalid meta GUID: ${f}`);
  assert.ok(!guids.has(guid), `Duplicate GUID: ${f} / ${guids.get(guid)}`); guids.set(guid,f.slice(0,-5));
}
const build = readFileSync('ProjectSettings/EditorBuildSettings.asset','utf8');
let scenes = 0;
for (const m of build.matchAll(/path: (Assets\/[^\n]+)\n\s+guid: ([0-9a-f]{32})/g)) {
  assert.ok(existsSync(m[1]), `Missing build scene: ${m[1]}`);
  assert.equal(guids.get(m[2]),m[1],`Scene GUID mismatch: ${m[1]}`); scenes++;
}
assert.ok(scenes>0, 'No build scenes');
for (const f of ['Packages/manifest.json','Packages/packages-lock.json']) {
  const text = readFileSync(f,'utf8'); JSON.parse(text);
  assert.ok(!/file:|\/Users\/|[A-Z]:\\/.test(text), `Local package dependency: ${f}`);
}
console.log(`Sideview static checks: ${files.length} asset files, ${guids.size} GUIDs, ${scenes} build scenes. Editor compilation and play testing are separate.`);
