import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {dirname,join} from 'node:path';
import {mkdirSync,openSync} from 'node:fs';
const root=join(dirname(fileURLToPath(import.meta.url)),'MemoryLab'),port=4341,url=`http://127.0.0.1:${port}`;
let ready=false;
try{const health=await (await fetch(url+'/api/health',{signal:AbortSignal.timeout(1500)})).json();if(health.app!=='moonlife-runtime'||health.runtimeRevision!=='protocol-recovery-v2')throw Error('该端口运行着其他版本，请关闭后重试。');ready=true;}catch(e){if(e.message.includes('其他版本'))throw e;}
if(!ready){mkdirSync(join(root,'.data'),{recursive:true});const log=openSync(join(root,'.data/character-lab.log'),'a',0o600);const p=spawn(process.execPath,[join(root,'server.mjs'),'--port',String(port),'--db',join(root,'.data/character-lab.sqlite')],{cwd:root,detached:true,stdio:['ignore',log,log]});p.unref();for(let n=0;n<25;n++){await new Promise(r=>setTimeout(r,200));try{ready=(await (await fetch(url+'/api/health')).json()).runtimeRevision==='protocol-recovery-v2';if(ready)break;}catch{}}}
if(!ready)throw Error('角色测试服务未启动，请查看MemoryLab/.data/character-lab.log');
console.log('角色交互实验室：'+url+'/lab');
if(process.argv.includes('--open'))spawn('open',[url+'/lab'],{stdio:'ignore'}).unref();
