import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
let lock;try{lock=JSON.parse(await fs.readFile(path.join(root,'data/singing/service.lock'),'utf8'));}catch{console.log('本地 Demo 没有运行。');process.exit(0);}
let found=false;
const first=Number(process.env.Z_PREVIEW_PORT||4174);
for(let port=first;port<first+10;port++){
 try{const r=await fetch(`http://127.0.0.1:${port}/api/local-status`,{signal:AbortSignal.timeout(500)}),j=await r.json();if(j.app==='z-call-local'&&path.resolve(j.root)===path.resolve(root)){
  const current=JSON.parse(await fs.readFile(path.join(root,'data/singing/service.lock'),'utf8'));
  if(!Number.isInteger(current.pid)||current.pid!==lock.pid)throw Error('服务已经变化，请重试');
  process.kill(current.pid,'SIGTERM');console.log('本地 Demo 已停止，已保存的点歌记录保留。');found=true;break;
 }}catch(e){if(e.code==='ESRCH')break;}
}
if(!found)console.log('未找到该本地 Demo 的运行服务，无需停止。');
