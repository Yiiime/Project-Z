import assert from 'node:assert/strict';
import { readdir, readFile, access } from 'node:fs/promises';
import { resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
const assets = [];
async function walk(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    if (entry.name.startsWith('.') || entry.name.endsWith('.meta')) continue;
    const path = resolve(dir, entry.name); assets.push(path);
    if (entry.isDirectory()) await walk(path);
  }
}
await walk(resolve(root, 'Assets'));
const guids = new Set();
for (const path of assets) {
  const meta = await readFile(path + '.meta', 'utf8');
  const guid = meta.match(/^guid: ([a-f0-9]{32})$/m)?.[1];
  assert.ok(guid && !guids.has(guid), `Missing or duplicate GUID: ${relative(root, path)}`);
  guids.add(guid);
  if (/\.(json|asmdef)$/.test(path)) JSON.parse(await readFile(path, 'utf8'));
}
const scene = await readFile(resolve(root, 'Assets/ProjectZ/Scenes/Boot.unity'), 'utf8');
for (const match of scene.matchAll(/guid: ([a-f0-9]{32})/g)) assert.ok(guids.has(match[1]), `Broken scene reference: ${match[1]}`);
const manifest = JSON.parse(await readFile(resolve(root, 'Packages/manifest.json')));
assert.equal(manifest.dependencies['com.unity.render-pipelines.universal'], '17.3.0');
const version = await readFile(resolve(root, 'ProjectSettings/ProjectVersion.txt'), 'utf8');
assert.match(version, /6000\.3\.27f1/);
await access(resolve(root, '打开项目.command'));
console.log(`Source check: ${assets.length} assets/folders have unique metadata; scene references and pinned dependencies are valid.`);
