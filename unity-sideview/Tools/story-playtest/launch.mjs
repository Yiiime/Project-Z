import {spawn,execFileSync} from 'node:child_process';
import {mkdirSync,openSync,closeSync,existsSync,readdirSync,readFileSync,writeFileSync} from 'node:fs';
import {dirname,resolve,join} from 'node:path';
import {fileURLToPath} from 'node:url';
const project=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const output=join(project,'Builds/StoryPlaytest'),app=join(output,'ProjectZStory.app');
const data=join(output,'Data'),port=4347,url='http://127.0.0.1:'+port;
if(!existsSync(app)){console.error('尚未构建剧情试玩应用。');process.exit(1);}
mkdirSync(data,{recursive:true});
let healthy=false;try{healthy=(await (await fetch(url+'/api/health',{signal:AbortSignal.timeout(1200)})).json()).app==='moonlife-runtime';}catch{}
if(!healthy){
 const log=openSync(join(data,'service.log'),'a',0o600);
 const service=spawn(process.execPath,[join(project,'Tools/MemoryLab/server.mjs'),'--port',String(port),'--db',join(data,'service/unified.sqlite')],{cwd:join(project,'Tools/MemoryLab'),detached:true,stdio:['ignore',log,log],env:{...process.env,Z_STORY_ASSET_MODE:'vector'}});service.unref();closeSync(log);
 for(let n=0;n<30;n++){await new Promise(r=>setTimeout(r,250));try{healthy=(await (await fetch(url+'/api/health',{signal:AbortSignal.timeout(500)})).json()).app==='moonlife-runtime';if(healthy)break;}catch{}}
}
if(!healthy){console.error('本机模型服务未能启动，请查看 '+join(data,'service.log'));process.exit(1);}
const lock=join(data,'player.pid');
try{const pid=Number(readFileSync(lock,'utf8'));if(pid>1){process.kill(pid,0);console.log('剧情试玩已经在运行，请切回游戏窗口。');process.exit(0);}}catch{}
const binary=execFileSync('/usr/bin/plutil',['-extract','CFBundleExecutable','raw','-o','-',join(app,'Contents/Info.plist')],{encoding:'utf8'}).trim();
const executable=join(app,'Contents/MacOS',binary);
const log=openSync(join(data,'player.log'),'a');
const player=spawn(executable,['-storyPlaytest','-memoryService',url,'-screen-fullscreen','0','-screen-width','1440','-screen-height','900','-logFile',join(data,'unity-player.log')],{cwd:project,detached:true,stdio:['ignore',log,log],env:{...process.env,Z_MEMORY_SAVE_ROOT:join(data,'saves')}});
writeFileSync(lock,String(player.pid));player.unref();closeSync(log);
console.log('剧情试玩已启动。存档独立保存于 '+join(data,'saves'));
