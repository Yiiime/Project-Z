import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';

const types={'.js':'text/javascript; charset=utf-8','.json':'application/json; charset=utf-8','.moc3':'application/octet-stream','.png':'image/png','.md':'text/plain; charset=utf-8','.txt':'text/plain; charset=utf-8','.html':'text/plain; charset=utf-8'};

// Only this dedicated, public asset directory is ever exposed by the Worker.
export async function buildLive2DAssets(root){
  const directory=path.join(root,'ui/live2d'),assets={};let total=0;
  async function visit(relative=''){
    let entries;
    try{entries=await fs.readdir(path.join(directory,relative),{withFileTypes:true});}
    catch(error){if(error.code==='ENOENT'&&!relative)return;throw error;}
    for(const entry of entries.sort((a,b)=>a.name.localeCompare(b.name))){
      if(!/^[a-zA-Z0-9_.-]+$/.test(entry.name)||entry.name.startsWith('.'))throw new Error('Invalid Live2D asset filename');
      const file=path.posix.join(relative,entry.name);
      if(entry.isSymbolicLink())throw new Error('Live2D asset symlinks are not allowed');
      if(entry.isDirectory()){await visit(file);continue;}
      const type=types[path.extname(file)];
      if(!entry.isFile()||!type)throw new Error('Unexpected Live2D asset type: '+file);
      const bytes=await fs.readFile(path.join(directory,file));total+=bytes.length;
      if(bytes.length>8*1024*1024||total>16*1024*1024)throw new Error('Live2D asset budget exceeded');
      assets['/live2d/'+file]={type,body:bytes.toString('base64'),size:bytes.length,etag:'"'+createHash('sha256').update(bytes).digest('hex')+'"'};
    }
  }
  await visit();return {assets,total};
}
