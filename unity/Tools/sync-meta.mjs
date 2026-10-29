import { readdir, readFile, writeFile, access } from 'node:fs/promises';
import { resolve, relative, extname } from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
export const guidFor = path => createHash('sha256').update(`project-z/v1/${path}`).digest('hex').slice(0, 32);
async function walk(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name.endsWith('.meta') || entry.name.startsWith('.')) continue;
    const path = resolve(directory, entry.name), key = relative(root, path).split('\\').join('/');
    try { await access(path + '.meta'); }
    catch {
      const kind = entry.isDirectory() ? 'DefaultImporter' : extname(path) === '.cs' ? 'MonoImporter' :
        extname(path) === '.asmdef' ? 'AssemblyDefinitionImporter' : extname(path) === '.json' ? 'TextScriptImporter' : 'DefaultImporter';
      const extras = kind === 'MonoImporter' ? '  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n' : '';
      await writeFile(path + '.meta', `fileFormatVersion: 2\nguid: ${guidFor(key)}\n${entry.isDirectory() ? 'folderAsset: yes\n' : ''}${kind}:\n  externalObjects: {}\n${extras}  userData:\n  assetBundleName:\n  assetBundleVariant:\n`, { flag: 'wx' });
    }
    if (entry.isDirectory()) await walk(path);
  }
}
await walk(resolve(root, 'Assets'));
const content = JSON.parse(await readFile(resolve(root, 'Assets/ProjectZ/Resources/Content/episodes.json')));
console.log(`Source metadata ready; ${content.episodes.length} episodes.`);
