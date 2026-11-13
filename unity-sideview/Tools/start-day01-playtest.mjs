import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn,spawnSync} from 'node:child_process';
import {loadEnvironment} from './live-service/server/providers.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const live=path.join(root,'Tools/live-service'),memory=path.join(root,'Tools/MemoryLab'),runtime=path.join(root,'.local/day01');
fs.mkdirSync(runtime,{recursive:true,mode:0o700});
let local={};try{local=JSON.parse(fs.readFileSync(path.join(root,'local-source.json'),'utf8'));}catch{}
const source=process.env.Z_LIVE_CONFIG_DIR||local.configDirectory||live;
const existing=loadEnvironment(source),modelFile=process.env.Z_MEMORY_CONFIG||local.modelFile||path.join(memory,'.local/model.json');
if(!existing.DEEPSEEK_API_KEY){try{const c=JSON.parse(fs.readFileSync(modelFile,'utf8'));if(c.provider==='deepseek'||c.baseUrl?.includes('api.deepseek.com'))existing.DEEPSEEK_API_KEY=c.apiKey||'';}catch{}}
if(!existing.DEEPSEEK_API_KEY)throw Error('尚未找到本机模型配置。请先在原工程配置 DeepSeek，再启动试玩；密钥无需放进游戏包。');
const memoryPort=Number(process.env.Z_MEMORY_PORT||49452),livePort=Number(process.env.Z_UNITY_PORT||49453);
const liveRuntime=path.join(runtime,'live'),connection=path.join(runtime,'connection.json');
const env={...process.env,...Object.fromEntries(Object.entries(existing).filter(([,v])=>v)),Z_MEMORY_PORT:String(memoryPort),Z_UNITY_PORT:String(livePort),Z_LIVE_RUNTIME_DIR:liveRuntime,Z_MEMORY_DB:path.join(runtime,'memory.sqlite'),Z_MEMORY_CONFIG:modelFile,Z_LIVE_CONFIG_DIR:source,Z_LIVE_CONNECTION_FILE:connection};
const processFile=path.join(runtime,'processes.json');let processes={};try{processes=JSON.parse(fs.readFileSync(processFile,'utf8'));}catch{}
const health=async(url,headers={})=>{try{const r=await fetch(url,{headers,signal:AbortSignal.timeout(1200)});return r.ok?await r.json():null;}catch{return null;}};
const token=()=>{try{return fs.readFileSync(path.join(liveRuntime,'local-token'),'utf8').trim();}catch{return '';}};
function start(name,cwd,entry){const fd=fs.openSync(path.join(runtime,name+'.log'),'a',0o600);const child=spawn(process.execPath,[entry],{cwd,env,stdio:['ignore',fd,fd],detached:true});child.unref();fs.closeSync(fd);processes[name]=child.pid;fs.writeFileSync(processFile,JSON.stringify(processes),{mode:0o600});}
const memoryUrl=`http://127.0.0.1:${memoryPort}`,liveUrl=`http://127.0.0.1:${livePort}`;
const m=await health(memoryUrl+'/api/health');if(m&&m.runtimeRevision!=='integrated-npc-runtime-v1')throw Error('角色服务端口被旧版本占用，请更换 Z_MEMORY_PORT。');
if(!m)start('memory',memory,'server.mjs');
if(!await health(liveUrl+'/health',{Authorization:'Bearer '+token()}))start('live',live,'server/index.mjs');
let ready=false;for(let i=0;i<50;i++){const a=await health(memoryUrl+'/api/health'),b=await health(liveUrl+'/health',{Authorization:'Bearer '+token()});if(a?.configured&&b?.ok){ready=true;break;}await new Promise(r=>setTimeout(r,300));}
if(!ready)throw Error('连接尚未就绪，请查看 .local/day01 中的日志后重试。');
fs.writeFileSync(connection,JSON.stringify({baseUrl:liveUrl,token:token()}),{mode:0o600});
fs.writeFileSync(path.join(runtime, 'game-connection.json'), JSON.stringify({memoryUrl: memoryUrl}), {mode: 0o600});
console.log('小岛已连接。新游戏输入名字；A/D 移动，E 交谈，P 拍照，Tab 手机，Esc 返回。');
if(process.argv.includes('--check')){console.log('启动自检通过：角色与直播服务就绪，使用独立试玩存档。');process.exit(0);}
const app=path.join(root,'ProjectZ-Day01.app');if(!fs.existsSync(app))throw Error('缺少 ProjectZ-Day01.app，请先生成试玩包。');
const result=spawnSync('/usr/bin/open',['-n',app,'--env','Z_MEMORY_SAVE_ROOT='+path.join(runtime,'saves'),'--env','Z_LIVE_CONNECTION_FILE='+connection,'--args','-memoryService',memoryUrl,'-screen-fullscreen','0','-screen-width','1440','-screen-height','900','-logFile',path.join(runtime,'player.log')],{stdio:'inherit'});
if(result.status!==0)throw Error('游戏启动失败，请查看系统提示。');
