import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';import {spawnSync,execFileSync} from 'node:child_process';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),repo=path.dirname(root);
const destination=path.resolve(process.argv[2]||path.join(root,'Builds/Day01-Playtest'));
const app=path.join(destination,'ProjectZ-Day01.app');if(!fs.existsSync(app))throw Error('Build the macOS app at '+app+' first.');
const excluded=p=>p.split(path.sep).some(x=>['node_modules','tests','test-results','__pycache__','.local','.data'].includes(x)||x.startsWith('.runtime')||x.startsWith('.env'))||/配置\.env$|\.log$|\.sqlite(?:-wal|-shm)?$/.test(p);
for(const service of ['MemoryLab','live-service']){
 const from=path.join(root,'Tools',service),to=path.join(destination,'Tools',service);
 fs.cpSync(from,to,{recursive:true,filter:p=>!excluded(path.relative(from,p))});
 const install=spawnSync(process.execPath,[execFileSync('which',['npm'],{encoding:'utf8'}).trim(),'ci','--omit=dev','--ignore-scripts'],{cwd:to,stdio:'inherit'});if(install.status)throw Error('Dependency install failed: '+service);
}
fs.copyFileSync(path.join(root,'Tools/start-day01-playtest.mjs'),path.join(destination,'Tools/start-day01-playtest.mjs'));
fs.mkdirSync(path.join(destination,'Runtime'),{recursive:true});fs.copyFileSync(process.execPath,path.join(destination,'Runtime/node'));fs.chmodSync(path.join(destination,'Runtime/node'),0o755);
const license=path.join(root,'Tools/MemoryLab/licenses/NODE-LICENSE.txt');if(fs.existsSync(license))fs.copyFileSync(license,path.join(destination,'Runtime/NODE-LICENSE.txt'));
const launcher=path.join(destination,'开始试玩.command');fs.writeFileSync(launcher,'#!/bin/bash\nset -euo pipefail\ncd "$(dirname "$0")"\n./Runtime/node Tools/start-day01-playtest.mjs "$@"\n');fs.chmodSync(launcher,0o755);
// Local-only path reference; no provider secrets are copied into the app or archive.
fs.writeFileSync(path.join(destination,'local-source.json'),JSON.stringify({configDirectory:path.join(root,'Tools/live-service'),modelFile:path.join(root,'Tools/MemoryLab/.local/model.json')},null,2));
fs.writeFileSync(path.join(destination,'试玩说明.txt'),'双击「开始试玩.command」。新游戏从姓名开始，继续游戏恢复本包独立存档。\nA/D 或方向键移动；走近按 E 对话；P 拍照；Tab 手机；Esc 返回。任务提示会显示下一步与还未认识的人。\n第二场直播结束进入第2天；之后休息/喝咖啡4次再直播，直播结束切次日。\n需要联网；复用本机已有 DeepSeek/语音配置。包内没有供应商密钥。可以直接双击文件夹内的 app，应用会自动启动连接检查；不要单独移走 app。\n这是开发草稿，不含世界观视频1；烟花保留占位。\n');
fs.copyFileSync(path.join(root,'Docs/Day01-验收记录-20261113.md'),path.join(destination,'验收记录.md'));
fs.cpSync(path.join(root,'Docs/Verification/Day01-20261113'),path.join(destination,'Verification/Day01-20261113'),{recursive:true});
fs.appendFileSync(path.join(destination,'试玩说明.txt'),'本包保持开发草稿：真实首日流程、晚间直播、32.284秒《晚安》完整副歌及进入第2天已补验，包含最新32张傍晚地图，详见验收记录.md。\n');
fs.writeFileSync(path.join(destination,'source-revision.txt'),execFileSync('git',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}));
const signed=spawnSync('/usr/bin/codesign',['--force','--deep','--sign','-',app],{stdio:'inherit'});if(signed.status)throw Error('Signing failed');
console.log('Local playtest ready: '+destination);
