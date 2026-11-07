import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn,spawnSync} from 'node:child_process';
import {setTimeout} from 'node:timers/promises';
const service=fileURLToPath(new URL('../',import.meta.url)),project=path.resolve(service,'../..'),app=path.join(project,'Builds/ZLiveQA/ZLiveQA.app');
if(!fs.existsSync(app))throw Error('请先用 Unity -executeMethod ZLive.EditorTools.ZLiveVerification.BuildWithInputChecks 生成 Builds/ZLiveQA/ZLiveQA.app，详见原生连麦说明。');
let local={};try{local=JSON.parse(fs.readFileSync(path.join(service,'.runtime/native-call-config.json'),'utf8'));}catch(e){if(e.code!=='ENOENT')throw e;}
const port=Number(process.env.Z_UNITY_PORT||local.port||4184);if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Invalid local port');
const env={...process.env,Z_UNITY_PORT:String(port),Z_MEMORY_SAVE_ROOT:path.join(project,'Builds/NativeCall/saves'),
 ...(local.configDirectory?{Z_LIVE_CONFIG_DIR:process.env.Z_LIVE_CONFIG_DIR||local.configDirectory}:{}),...(local.voiceConfigDirectory?{Z_LIVE_VOICE_CONFIG_DIR:process.env.Z_LIVE_VOICE_CONFIG_DIR||local.voiceConfigDirectory}:{})};
const logs=path.join(project,'Builds/NativeCall');fs.mkdirSync(logs,{recursive:true});fs.mkdirSync(env.Z_MEMORY_SAVE_ROOT,{recursive:true});
const connection=()=>({baseUrl:'http://127.0.0.1:'+port,token:fs.readFileSync(path.join(service,'.runtime/local-token'),'utf8').trim()});
const healthy=async()=>{try{const c=connection();return (await fetch(c.baseUrl+'/health',{headers:{Authorization:'Bearer '+c.token},signal:AbortSignal.timeout(700)})).ok;}catch{return false;}};
if(!await healthy()){const log=fs.openSync(path.join(logs,'service.log'),'a',0o600);const child=spawn(process.execPath,['server/index.mjs'],{cwd:service,env,detached:true,stdio:['ignore',log,log]});child.unref();fs.closeSync(log);}
let ready=false;for(let i=0;i<30;i++){if(await healthy()){ready=true;break;}await setTimeout(300);}if(!ready)throw Error('直播服务未启动，请查看 Builds/NativeCall/service.log；同一任务工作树只能运行一个服务实例。');
fs.writeFileSync(path.join(app,'Contents/Resources/zlive-connection.json'),JSON.stringify(connection()),{mode:0o600});
if(spawnSync('/usr/bin/codesign',['--force','--deep','--sign','-',app],{stdio:'ignore'}).status!==0)throw Error('本地试玩包签名失败');
if(process.env.Z_LIVE_PREVIEW_CHECK_ONLY==='1'){console.log('原生连麦试玩服务已就绪：'+connection().baseUrl);process.exit(0);}
const executable=spawnSync('/usr/libexec/PlistBuddy',['-c','Print :CFBundleExecutable',path.join(app,'Contents/Info.plist')],{encoding:'utf8'}).stdout.trim();
console.log('按 Tab 进入 Z 的直播，点麦克风上麦，再点下麦。声音录入在设置中。关闭试玩后，服务仍在后台运行。');
const player=spawn(path.join(app,'Contents/MacOS',executable),['-memoryDisabled','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',path.join(logs,'player.log')],{env,stdio:'inherit'});
player.on('exit',code=>{process.exitCode=code||0;});
