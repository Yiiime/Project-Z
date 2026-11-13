import { spawn,spawnSync } from 'node:child_process';
import { mkdirSync,openSync,closeSync,existsSync } from 'node:fs';
import { dirname,join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadTeamClient } from '../team-model/client.mjs';
const root=dirname(fileURLToPath(import.meta.url)),port=Number(process.env.Z_MEMORY_PORT||4330),url=`http://127.0.0.1:${port}`;
const healthy=async()=>{try{const r=await fetch(url+'/api/health',{signal:AbortSignal.timeout(700)});const j=await r.json();return j.app==='moonlife-runtime';}catch{return false;}};
if(!await healthy()) {
  if(!existsSync(join(root,'node_modules/ajv'))){const setup=spawnSync(process.platform==='win32'?'npm.cmd':'npm',['ci','--ignore-scripts','--no-audit','--no-fund'],{cwd:root,stdio:'inherit',shell:process.platform==='win32'});if(setup.status!==0){console.error('对话服务依赖安装失败，请检查 Node.js 与网络。');process.exit(1);}}
  mkdirSync(join(root,'.data'),{recursive:true,mode:0o700});const log=openSync(join(root,'.data/service.log'),'a',0o600);
  const child=spawn(process.execPath,[join(root,'server.mjs')],{cwd:root,detached:true,stdio:['ignore',log,log],env:process.env});child.unref();closeSync(log);
  let ready=false;for(let i=0;i<30;i++){if(await healthy()){ready=true;break;}await new Promise(r=>setTimeout(r,200));}
  if(!ready){console.error('记忆服务未启动，请检查端口或本机Node版本。');process.exit(1);}
}
const status=await fetch(url+'/api/health').then(r=>r.json());
if(loadTeamClient()&&!status.team?.enabled){console.error('此端口仍在运行旧版对话服务，请关闭旧服务后重新启动本版本。');process.exit(1);}
console.log(status.configured?'对话服务已连接。':`对话连接：${url}/settings`);
if(!status.configured){const command=process.platform==='darwin'?'open':process.platform==='win32'?'explorer.exe':'xdg-open';const child=spawn(command,[url+'/settings'],{stdio:'ignore'});child.on('error',()=>{});child.unref();}
