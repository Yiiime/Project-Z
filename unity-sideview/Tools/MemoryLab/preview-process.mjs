import {spawn} from 'node:child_process';
import {once} from 'node:events';
import fs from 'node:fs';
import path from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {fileURLToPath} from 'node:url';

export async function startMemoryPreview({data,apiKey,port}){
  const cwd=path.dirname(fileURLToPath(import.meta.url));
  const output=fs.openSync(path.join(data,'日志','记忆服务.log'),'a',0o600);
  const url='http://127.0.0.1:'+port;
  let child;
  try {
    child=spawn(process.execPath,['server.mjs','--db',path.join(data,'memory.sqlite'),'--port',String(port)],{
      cwd,stdio:['ignore','pipe',output],env:{...process.env,GAME_MODEL_API_KEY:apiKey,GAME_MODEL_NAME:'deepseek-flash'}
    });
    // Wait for our own server's listen callback before accepting health from this port.
    const listening=once(child.stdout,'data');
    await Promise.race([listening,once(child,'exit').then(()=>{throw Error('记忆服务未能启动。');}),
      delay(10000,null,{ref:false}).then(()=>{throw Error('等待记忆服务超时。');})]);
    child.stdout.resume();
    const response=await fetch(url+'/api/health',{signal:AbortSignal.timeout(2500)});
    const health=await response.json();
    if(!response.ok||health.app!=='moonlife-runtime'||!health.configured)throw Error('记忆服务未就绪。');
    return {child,url};
  } catch(e) {child?.kill();throw e;}
  finally {fs.closeSync(output);}
}

