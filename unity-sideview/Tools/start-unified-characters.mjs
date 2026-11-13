import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn,spawnSync} from 'node:child_process';
import {loadTeamClient} from './team-model/client.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const memory=path.join(root,'Tools/MemoryLab'),live=path.join(root,'Tools/live-service');
const team=loadTeamClient();
const runtime=path.join(memory,'.local/unified-runtime');fs.mkdirSync(runtime,{recursive:true,mode:0o700});
const memoryPort=Number(process.env.Z_MEMORY_PORT||8942),livePort=Number(process.env.Z_UNITY_PORT||8943);
const liveRuntime=path.join(live,'.runtime-unified');
if(Number(process.versions.node.split('.')[0])<24)throw Error('需要 Node 24 或更新版本。');
const stateFile=path.join(runtime,'processes.json');
let previous={};try{previous=JSON.parse(fs.readFileSync(stateFile));}catch{}
if(process.argv.includes('--restart'))for(const pid of Object.values(previous))try{process.kill(pid,'SIGTERM');}catch{}
if(process.argv.includes('--restart'))await new Promise(r=>setTimeout(r,1000));
const alive=async(url,headers={})=>{try{const r=await fetch(url,{headers,signal:AbortSignal.timeout(1000)});return r.ok;}catch{return false;}};
function start(name,cwd,args,env){const file=fs.openSync(path.join(runtime,name+'.log'),'a',0o600);const child=spawn(process.execPath,args,{cwd,env:{...process.env,...env},stdio:['ignore',file,file],detached:true});fs.closeSync(file);child.unref();previous[name]=child.pid;fs.writeFileSync(stateFile,JSON.stringify(previous),{mode:0o600});}
const config=process.env.Z_MEMORY_CONFIG||path.join(memory,'.local/model.json');
const memoryReady=async()=>{try{const r=await fetch(`http://127.0.0.1:${memoryPort}/api/health`,{signal:AbortSignal.timeout(1000)});const data=await r.json();return r.ok&&data.runtimeRevision==='integrated-npc-runtime-v1'&&(!team||data.team?.enabled);}catch{return false;}};
if(await alive(`http://127.0.0.1:${memoryPort}/api/health`)&&!await memoryReady())throw Error('当前端口是旧版角色服务。请先关闭旧服务，或设置 Z_MEMORY_PORT 使用空闲端口后重新启动。');
if(!await alive(`http://127.0.0.1:${memoryPort}/api/health`))start('memory',memory,['server.mjs','--port',String(memoryPort),'--db',path.join(memory,'.data/unified-local.sqlite')],{Z_MEMORY_CONFIG:config});
const tokenFile=path.join(liveRuntime,'local-token');let token='';try{token=fs.readFileSync(tokenFile,'utf8').trim();}catch{}
if(!await alive(`http://127.0.0.1:${livePort}/health`,{Authorization:'Bearer '+token}))start('live',live,['server/index.mjs'],{Z_UNITY_PORT:String(livePort),Z_LIVE_RUNTIME_DIR:liveRuntime,Z_MEMORY_CONFIG:config,Z_LIVE_CHARACTER_SERVICE_URL:`http://127.0.0.1:${memoryPort}`});
let ready=false;
for(let n=0;n<40;n++){
 try{token=fs.readFileSync(tokenFile,'utf8').trim();}catch{}
 if(await memoryReady()&&await alive(`http://127.0.0.1:${livePort}/health`,{Authorization:'Bearer '+token})){ready=true;break;}
 await new Promise(r=>setTimeout(r,500));
}
if(!ready)throw Error('服务尚未就绪，请查看 Tools/MemoryLab/.local/unified-runtime 下的日志。');
const connection=JSON.stringify({baseUrl:`http://127.0.0.1:${livePort}`,token});
const editorConnection=path.join(root,'UserSettings/ZLive/local-connection.json');fs.mkdirSync(path.dirname(editorConnection),{recursive:true});fs.writeFileSync(editorConnection,connection,{mode:0o600});
console.log(`统一角色服务已就绪：岛上 ${memoryPort}，直播 ${livePort}。全员直播，个人记忆跨频道延续。`);
if(process.argv.includes('--services-only'))process.exit(0);
if(team){const state=await fetch(`http://127.0.0.1:${memoryPort}/api/health`).then(r=>r.json());if(!state.configured){const child=spawn('/usr/bin/open',[`http://127.0.0.1:${memoryPort}/settings`],{stdio:'ignore'});child.on('error',()=>{});child.unref();}}
const app=path.join(root,'Builds/UnifiedCharacters/ProjectZ.app');
const saveRoot=process.env.Z_MEMORY_SAVE_ROOT||path.join(runtime,'saves');
const report=path.join(path.dirname(app),'build-report.txt');
const newerSource=(folder,time)=>fs.readdirSync(folder,{withFileTypes:true}).some(entry=>{const file=path.join(folder,entry.name);return entry.isDirectory()?newerSource(file,time):fs.statSync(file).mtimeMs>time;});
const buildCurrent=fs.existsSync(report)&&!newerSource(path.join(root,'Assets'),fs.statSync(report).mtimeMs);
if(fs.existsSync(app)&&buildCurrent&&fs.readFileSync(report,'utf8').startsWith('Result: Succeeded')&&!process.argv.includes('--editor')){
 fs.writeFileSync(path.join(app,'Contents/Resources/zlive-connection.json'),connection,{mode:0o600});
 const signed=spawnSync('/usr/bin/codesign',['--force','--deep','--sign','-',app],{stdio:'ignore'});if(signed.status!==0)throw Error('本机应用签名失败。');
 spawn('/usr/bin/open',[app,'--env','Z_MEMORY_SAVE_ROOT='+saveRoot,'--env','Z_LIVE_CONNECTION_FILE='+editorConnection,'--args','-memoryService',`http://127.0.0.1:${memoryPort}`],{stdio:'ignore'}).unref();
}else{
 const candidates=[process.env.Z_UNITY_EDITOR,path.join(process.env.HOME,'Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity'),'/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity'].filter(Boolean);
 const editor=candidates.find(fs.existsSync);if(!editor)throw Error('需要 Unity 6000.3.27f1。服务已启动，可手动打开本工程。');
 spawn(editor,['-projectPath',root,'-memoryService',`http://127.0.0.1:${memoryPort}`],{stdio:'ignore',detached:true,env:{...process.env,Z_MEMORY_SAVE_ROOT:saveRoot,Z_MEMORY_PORT:String(memoryPort),Z_UNITY_PORT:String(livePort),Z_LIVE_RUNTIME_DIR:liveRuntime,Z_LIVE_CONNECTION_FILE:editorConnection}}).unref();
}
