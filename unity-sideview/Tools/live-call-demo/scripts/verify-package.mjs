import fs from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
const manifest=JSON.parse(await fs.readFile(path.join(root,'MANIFEST.json'),'utf8'));
if(manifest.profile!=='singing-only'||!Array.isArray(manifest.files))throw Error('Invalid package manifest');
const seen=new Set();
for(const file of manifest.files){
 if(!file.path||path.isAbsolute(file.path)||file.path.split('/').some(p=>!p||p==='..')||seen.has(file.path))throw Error('Invalid file path');
 seen.add(file.path);const target=path.join(root,file.path),stat=await fs.lstat(target);
 if(!stat.isFile()||stat.isSymbolicLink())throw Error('Non-regular package file: '+file.path);
 const bytes=await fs.readFile(target);
 if(bytes.length!==file.bytes||createHash('sha256').update(bytes).digest('hex')!==file.sha256)throw Error('Package file changed: '+file.path);
}
console.log(`Verified ${seen.size} package files (SHA-256).`);
