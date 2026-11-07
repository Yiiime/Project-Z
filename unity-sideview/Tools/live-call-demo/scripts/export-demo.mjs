// Portable private integration handoff. Never copy runtime state or credentials.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {TALENT_CATALOG} from '../ui/talent-config.js';
const root=fileURLToPath(new URL('../',import.meta.url));
const stamp=new Date().toISOString().replace(/[-:]/g,'').replace(/\.\d+Z$/,'Z');
const folder=path.resolve(root,'../output',`Z-live-singing-integration-${stamp}`);
await fs.mkdir(folder,{recursive:true});
const files=[];
async function copy(relative){
 const source=path.join(root,relative),stat=await fs.lstat(source);
 if(stat.isSymbolicLink()||!stat.isFile())throw Error('Non-regular export file: '+relative);
 const dest=path.join(folder,relative);await fs.mkdir(path.dirname(dest),{recursive:true});await fs.copyFile(source,dest);files.push(relative);
}
async function tree(relative,extensions){
 for(const entry of (await fs.readdir(path.join(root,relative),{withFileTypes:true})).sort((a,b)=>a.name.localeCompare(b.name))){
  if(entry.name.startsWith('.')||['node_modules','data','outputs','_backup','__pycache__'].includes(entry.name))continue;
  const rel=path.posix.join(relative,entry.name);
  if(entry.isSymbolicLink())throw Error('Symlink excluded: '+rel);
  if(entry.isDirectory())await tree(rel,extensions);
  else if(extensions.has(path.extname(entry.name)))await copy(rel);
 }
}
for(const relative of ['package.json','package-lock.json','.env.example','.gitignore','LICENSE','ASSETS.md','启动Demo.command'])await copy(relative);
for(const directory of ['worker','server','scripts','tests','main-agent','skills'])
 await tree(directory,new Set(['.js','.mjs','.json','.md','.txt','.yaml','.html','.css']));
for(const name of await fs.readdir(path.join(root,'ui')))if(/\.(?:js|css|html)$/.test(name))await copy('ui/'+name);
for(const name of ['manga-live-night.png','live2d-empty-room.png','avatar-z.svg','avatar-moon.svg','avatar-wind.svg','mic.svg','asset-notes.json'])await copy('ui/assets/'+name);
for(const song of TALENT_CATALOG.songs){
 if(!/^\/audio\/songs\/[\w-]+\.(wav|mp3)$/.test(song.src)||!/^docs\/reviews\/[\w-]+\.json$/.test(song.reviewRef))throw Error('Unexpected song asset path');
 await copy('ui'+song.src);await copy(song.reviewRef);
}
// Preserve the user's existing private demo assets and their original notices.
// Cubism Core remains a remote runtime URL, as in the source demo.
await tree('ui/live2d',new Set(['.js','.json','.moc3','.png','.md','.txt','.html']));
await tree('docs/licenses',new Set(['.md','.txt']));
for(const name of ['INTEGRATION-HANDOFF.md','VERIFICATION.md','verification-results.json','call-integration.md','LIVE2D.md','VOICE.md','TALENT-AUDIO.md'])await copy('docs/'+name);
await fs.copyFile(path.join(root,'docs/INTEGRATION-HANDOFF.md'),path.join(folder,'README.md'));files.push('README.md');
await fs.copyFile(path.join(root,'docs/VERIFICATION.md'),path.join(folder,'VERIFICATION.md'));files.push('VERIFICATION.md');
await fs.chmod(path.join(folder,'启动Demo.command'),0o755);
const notes=JSON.parse(await fs.readFile(path.join(folder,'ui/assets/asset-notes.json'),'utf8'));
delete notes.fileKey;delete notes.frame;delete notes.illustration?.originalFile;notes.name='Z 直播 · 聊天与唱歌';
await fs.writeFile(path.join(folder,'ui/assets/asset-notes.json'),JSON.stringify(notes,null,2)+'\n');
// Defense in depth: compare actual local secret values in memory, never log them.
const privateValues=[];
for(const filename of await fs.readdir(root)){
 if(!/\.env(?:\.local)?$/.test(filename))continue;
 const source=await fs.readFile(path.join(root,filename),'utf8');
 for(const line of source.split(/\r?\n/)){
  const m=line.match(/^\s*(?:export\s+)?([A-Z_]*(?:KEY|TOKEN|SECRET|APP_ID|VOICEPRINT_ID))\s*=\s*(.*?)\s*$/);if(!m)continue;
  const v=m[2].replace(/\s+#.*$/,'').replace(/^(['"])(.*)\1$/,'$2');if(v.length>=8)privateValues.push(v);
 }
}
const inventory=[];
for(const file of files.sort()){
 const bytes=await fs.readFile(path.join(folder,file));
 if(privateValues.some(secret=>bytes.includes(Buffer.from(secret))))throw Error('Export contains local private configuration in '+file);
 inventory.push({path:file,bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')});
}
await fs.writeFile(path.join(folder,'MANIFEST.json'),JSON.stringify({schemaVersion:1,profile:'singing-only',createdAt:new Date().toISOString(),source:'z-live-local Web demo',excluded:['credentials','voiceprint profile','user memory','runtime databases','generated candidates','test recordings','logs','node_modules','.git','Sites account metadata'],files:inventory},null,2)+'\n');
const archive=folder+'.zip';
execFileSync('python3',['-c',`from pathlib import Path
import sys, zipfile
root=Path(sys.argv[1])
with zipfile.ZipFile(sys.argv[2], 'w', zipfile.ZIP_DEFLATED) as z:
 for f in sorted(root.rglob('*')):
  if f.is_file(): z.write(f,root.name+'/'+str(f.relative_to(root)))
`,folder,archive],{stdio:['ignore','pipe','pipe']});
console.log(JSON.stringify({folder,archive,files:inventory.length,bytes:(await fs.stat(archive)).size,privateValueScan:'passed'},null,2));
