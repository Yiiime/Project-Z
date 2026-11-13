import {spawn,spawnSync} from 'node:child_process';
import {existsSync,readFileSync,mkdirSync,openSync,closeSync} from 'node:fs';
import {dirname,resolve,join,delimiter} from 'node:path';
import {homedir} from 'node:os';
import {fileURLToPath} from 'node:url';

export async function startLocalLive(){
  const root=resolve(dirname(fileURLToPath(import.meta.url)),'../live-service');
  if(!existsSync(join(root,'server/index.mjs')))throw Error('试玩包缺少直播服务，请重新构建完整团队版本。');
  const candidates=[process.env.Z_LIVE_NODE,process.execPath,join(homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node'),'/opt/homebrew/bin/node','/usr/local/bin/node'].filter(Boolean);
  const node=candidates.find(file=>existsSync(file)&&spawnSync(file,['-e','process.exit(Number(process.versions.node.split(".")[0])>=24?0:1)'],{stdio:'ignore'}).status===0);
  if(!node)throw Error('完整团队对话需要 Node.js 24 或更新版本。');
  const port=Number(process.env.Z_UNITY_PORT||4180),url=`http://127.0.0.1:${port}`;
  const connectionFile=process.env.Z_LIVE_CONNECTION_FILE||resolve(root,'../../UserSettings/ZLive/local-connection.json');
  const healthy=async()=>{try{const config=JSON.parse(readFileSync(connectionFile,'utf8'));if(config.baseUrl!==url)return false;const r=await fetch(url+'/health',{headers:{Authorization:'Bearer '+config.token},signal:AbortSignal.timeout(1000)});const data=await r.json();return r.ok&&data.protocolVersion===1&&data.modelSource==='team-ark';}catch{return false;}};
  if(await healthy())return;
  if(!existsSync(join(root,'node_modules/ws'))){
    const result=spawnSync(process.platform==='win32'?'npm.cmd':'npm',['ci','--ignore-scripts','--no-audit','--no-fund'],{cwd:root,env:{...process.env,PATH:dirname(node)+delimiter+(process.env.PATH||'')},stdio:'inherit',shell:process.platform==='win32'});
    if(result.status!==0)throw Error('直播服务依赖安装失败，请检查网络与 npm。');
  }
  const runtime=process.env.Z_LIVE_RUNTIME_DIR||join(root,'.runtime');
  mkdirSync(runtime,{recursive:true,mode:0o700});
  const log=openSync(join(runtime,'team-start.log'),'a',0o600);
  const child=spawn(node,[join(root,'server/index.mjs')],{cwd:root,env:{...process.env,Z_LIVE_CONNECTION_FILE:connectionFile},detached:true,stdio:['ignore',log,log]});
  let failed=false;child.on('error',()=>{failed=true;});child.unref();closeSync(log);
  for(let n=0;n<50&&!failed;n++){if(await healthy())return;await new Promise(resolve=>setTimeout(resolve,200));}
  throw Error('团队直播服务未启动，请检查是否有旧服务占用端口 '+port+'。');
}
