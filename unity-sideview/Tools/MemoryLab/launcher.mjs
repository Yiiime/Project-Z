import { spawn } from 'node:child_process';
import { mkdirSync,openSync,closeSync } from 'node:fs';
import { dirname,join } from 'node:path';
import { fileURLToPath } from 'node:url';
const root=dirname(fileURLToPath(import.meta.url)),port=Number(process.env.Z_MEMORY_PORT||4330),url=`http://127.0.0.1:${port}`;
const healthy=async()=>{try{const r=await fetch(url+'/api/health',{signal:AbortSignal.timeout(700)});const j=await r.json();return j.app==='project-z-seven-memory';}catch{return false;}};
if(!await healthy()) {
  mkdirSync(join(root,'.data'),{recursive:true,mode:0o700});const log=openSync(join(root,'.data/service.log'),'a',0o600);
  const child=spawn(process.execPath,[join(root,'server.mjs')],{cwd:root,detached:true,stdio:['ignore',log,log],env:process.env});child.unref();closeSync(log);
  let ready=false;for(let i=0;i<30;i++){if(await healthy()){ready=true;break;}await new Promise(r=>setTimeout(r,200));}
  if(!ready){console.error('记忆服务未启动，请检查端口或本机Node版本。');process.exit(1);}
}
console.log(`模型配置：${url}/settings`);
if(process.platform==='darwin')spawn('open',[url+'/settings'],{stdio:'ignore'}).unref();
