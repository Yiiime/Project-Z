import {spawn,spawnSync} from 'node:child_process';
import net from 'node:net';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
const first=Number(process.env.Z_PREVIEW_PORT||4174);
if(!Number.isInteger(first)||first<1024||first>65525)throw Error('请使用 1024—65525 之间的端口。');
const openBrowser=url=>{if(process.platform==='darwin'&&process.env.Z_DEMO_OPEN_BROWSER!=='0'){const p=spawn('/usr/bin/open',[url],{stdio:'ignore'});p.on('error',()=>console.log('请手动打开：'+url));}};
const available=port=>new Promise(resolve=>{const s=net.createServer();s.once('error',()=>resolve(false));s.listen(port,'127.0.0.1',()=>s.close(()=>resolve(true)));});
let port;
for(let candidate=first;candidate<first+10;candidate++){
 const url=`http://127.0.0.1:${candidate}/`;
 try{const r=await fetch(url+'api/local-status',{signal:AbortSignal.timeout(600)}),j=await r.json();if(j.app==='z-call-local'&&path.resolve(j.root)===path.resolve(root)){console.log('本地 Demo 已在运行：'+url);openBrowser(url);process.exit(0);}}catch{}
 if(await available(candidate)){port=candidate;break;}
}
if(!port)throw Error('本地端口暂不可用，请关闭旧的 Demo 窗口后再试。');
console.log('正在准备 Z 的本地连麦 Demo…');
const build=spawnSync(process.execPath,['scripts/build-site.mjs'],{cwd:root,stdio:'inherit'});
if(build.status!==0)process.exit(build.status||1);
console.log(`打开 http://127.0.0.1:${port}/\n点击麦克风上麦，再点下麦。请保留此窗口，按 Control+C 停止。`);
const child=spawn(process.execPath,['scripts/preview-server.mjs'],{cwd:root,stdio:'inherit',env:{...process.env,Z_PREVIEW_PORT:String(port),Z_DEMO_OPEN_BROWSER:process.env.Z_DEMO_OPEN_BROWSER||'1'}});
for(const signal of ['SIGINT','SIGTERM'])process.once(signal,()=>child.kill(signal));
child.once('error',()=>{console.error('本地服务启动失败。');process.exitCode=1;});
child.once('exit',(code,signal)=>{process.exitCode=code??(signal?0:1);});
