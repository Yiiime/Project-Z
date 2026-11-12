import {mkdirSync,existsSync,openSync,closeSync} from 'node:fs';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn,execFileSync} from 'node:child_process';
import {createServer} from './server.mjs';

// One package owns its service and saves. Closing the game also closes that service.
const root=dirname(dirname(fileURLToPath(import.meta.url)));
const data=join(root,'PlayData');
mkdirSync(join(data,'saves'),{recursive:true,mode:0o700});
mkdirSync(join(data,'.local'),{recursive:true,mode:0o700});
const app=join(root,'ProjectZSevenMemory.app');
const name=execFileSync('/usr/libexec/PlistBuddy',['-c','Print :CFBundleExecutable',join(app,'Contents/Info.plist')],{encoding:'utf8'}).trim();
const config=process.env.Z_MEMORY_CONFIG||join(data,'.local/model.json');
const service=createServer({dbPath:join(data,'runtime.sqlite'),configPath:config});
await new Promise((yes,no)=>{service.server.once('error',no);service.server.listen(0,'127.0.0.1',yes);});
const url='http://127.0.0.1:'+service.server.address().port;
console.log('街角长椅任务试玩已启动。与咖啡店的李庭交谈，问问有什么可以帮忙的。');
console.log('模型设置：'+url+'/settings');
if(!service.status().configured)spawn('/usr/bin/open',[url+'/settings'],{stdio:'ignore'}).unref();
const log=openSync(join(data,'launcher.log'),'a',0o600);
let game;
try {
  game=spawn(join(app,'Contents/MacOS',name),['-memoryService',url,'-logFile',join(data,'player.log')],
    {cwd:root,env:{...process.env,Z_MEMORY_SAVE_ROOT:join(data,'saves')},stdio:['ignore',log,log]});
  closeSync(log);
  process.exitCode=await new Promise((yes,no)=>{game.once('exit',code=>yes(code??1));game.once('error',no);});
} finally {
  await new Promise(yes=>service.server.close(yes));service.store.close();
}
