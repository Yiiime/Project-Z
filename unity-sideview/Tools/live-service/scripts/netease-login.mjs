import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {providerCall} from '../server/song-library/netease.mjs';
const action=process.argv[2]||'status';
if(action==='wait'){
 let last;
 for(let i=0;i<90;i++){
  const state=await providerCall('auth_poll',{});
  if(state.status!==last){console.log(JSON.stringify(state));last=state.status;}
  if(state.status==='authenticated')process.exit(0);
  if(['expired','login_failed','no_pending_login','superseded'].includes(state.status))process.exit(2);
  await new Promise(r=>setTimeout(r,2000));
 }
 console.log(JSON.stringify({status:'login_wait_timeout'}));process.exit(2);
}
if(!['begin','poll','status','refresh','maintain'].includes(action))throw Error('usage_begin_poll_status_wait_refresh_maintain');
const result=await providerCall('auth_'+action,{});
if(result.qrImage){
 const target=fileURLToPath(new URL('../.runtime/netease-auth/login-qr.png',import.meta.url));
 await fs.mkdir(path.dirname(target),{recursive:true,mode:0o700});
 await fs.writeFile(target,Buffer.from(result.qrImage.split(',')[1],'base64'),{mode:0o600});
 delete result.qrImage;result.qrImageFile=target;
}
console.log(JSON.stringify(result));
