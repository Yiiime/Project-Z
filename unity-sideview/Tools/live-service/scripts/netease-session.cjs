const fs=require('node:fs');
const path=require('node:path');
const {randomUUID,createHash}=require('node:crypto');
const SESSION_FILE=process.env.NETEASE_SESSION_FILE||path.resolve(__dirname,'../.runtime/netease-auth/session.json');
function read(file){try{return JSON.parse(fs.readFileSync(file,'utf8'));}catch(e){if(e.code==='ENOENT')return null;throw Error('session_file_invalid');}}
function write(file,value){fs.mkdirSync(path.dirname(file),{recursive:true,mode:0o700});const tmp=file+'.'+randomUUID()+'.tmp';fs.writeFileSync(tmp,JSON.stringify(value),{mode:0o600});fs.renameSync(tmp,file);fs.chmodSync(file,0o600);}
function parseCookies(headers){const cookie={};for(const row of headers||[]){const pair=row.split(';')[0],i=pair.indexOf('=');if(i>0&&/^[A-Za-z0-9_]+$/.test(pair.slice(0,i)))cookie[pair.slice(0,i)]=pair.slice(i+1);}return cookie;}
function createSessionStore(file=SESSION_FILE,{expectedAccountHash=file===SESSION_FILE?read(path.resolve(__dirname,'../config/netease-account.json'))?.accountFingerprint:null}={}){return {
 file,pendingFile:file+'.pending',
 load(){const s=read(file);if(!s)return {};if(!s.cookie||typeof s.cookie.MUSIC_U!=='string'||!s.cookie.MUSIC_U)throw Error('session_file_invalid');return s.cookie;},
 save(cookie,accountId){if(!cookie.MUSIC_U)throw Error('login_cookie_missing');if(accountId)this.bind(accountId);write(file,{...(read(file)||{}),cookie,savedAt:new Date().toISOString()});},
 bind(accountId){if(expectedAccountHash&&createHash('sha256').update('netease:'+String(accountId)).digest('hex')!==expectedAccountHash)throw Error('netease_account_mismatch');const owner=read(file+'.owner');const id=String(accountId);if(owner?.accountId&&owner.accountId!==id)throw Error('netease_account_mismatch');if(!owner)write(file+'.owner',{accountId:id,boundAt:new Date().toISOString()});},
 metadata(){const r=read(file)||{};return {lastRefreshAt:r.lastRefreshAt||null,lastRefreshAttemptAt:r.lastRefreshAttemptAt||null,lastRefreshError:r.lastRefreshError||null,accountBound:!!read(file+'.owner')};},
 stamp(values){write(file,{...(read(file)||{}),...values});},
 lockRefresh(){fs.mkdirSync(path.dirname(file),{recursive:true,mode:0o700});const lock=file+'.renew.lock';try{if(fs.statSync(lock).mtimeMs<Date.now()-120000)fs.unlinkSync(lock);}catch(e){if(e.code!=='ENOENT')throw e;}try{const fd=fs.openSync(lock,'wx',0o600);fs.closeSync(fd);return ()=>{try{fs.unlinkSync(lock);}catch{}};}catch(e){if(e.code==='EEXIST')return null;throw e;}},
 pending(){return read(file+'.pending');},
 savePending(value){write(file+'.pending',value);},
 clearPending(){try{fs.unlinkSync(file+'.pending');}catch(e){if(e.code!=='ENOENT')throw e;}},
 clear(){try{fs.unlinkSync(file);}catch(e){if(e.code!=='ENOENT')throw e;}}
};}
async function sessionStatus(store,call){
 let cookie;
 try{cookie=store.load();}catch{return {status:'invalid',authenticated:false,error:'netease_session_invalid',requiresLogin:true};}
 if(!cookie.MUSIC_U)return {status:'anonymous',authenticated:false,requiresLogin:true};
 try{
  const r=await call('login_status',{},cookie),data=r.body?.data;
  if(data?.account?.id){store.bind(data.account.id);return {status:'authenticated',authenticated:true,requiresLogin:false};}
  if([301,401].includes(data?.code)||[301,401].includes(r.body?.code)||data&&Object.hasOwn(data,'account')&&data.account===null&&(data.code===undefined||data.code===200))return {status:'expired',authenticated:false,error:'netease_login_expired',requiresLogin:true};
 }catch(e){if(e.message==='netease_account_mismatch')return {status:'account_mismatch',authenticated:false,error:'netease_account_mismatch',requiresLogin:true};if([301,401].includes(Number(e?.body?.code||e?.status)))return {status:'expired',authenticated:false,error:'netease_login_expired',requiresLogin:true};}
 return {status:'check_failed',authenticated:null,error:'netease_login_check_failed',requiresLogin:false};
}
async function audioAccess(action,query,store,call){
 let status=await sessionStatus(store,call);
 if(status.status==='expired'){const meta=store.metadata();if(!meta.lastRefreshAttemptAt||Date.now()-meta.lastRefreshAttemptAt>15*60*1000)status=await maintainSession(store,call,{force:true});}
 if(['expired','invalid','check_failed','account_mismatch'].includes(status.status))throw Error(status.error);
 const result=await call(action,query,store.load());
 const info=result.body?.data?.find(d=>String(d.id)===String(query.id));
 if(info){
  const trial=!!info.freeTrialInfo&&info.freeTrialInfo!=='null';
  const unavailable=!info.url||info.code!==200;
  if(trial||unavailable){
   if(!status.authenticated)throw Error('netease_login_required');
   throw Error(trial?'audio_trial_only_authenticated':'audio_access_unavailable_authenticated');
  }
 }
 return {...result,sessionStatus:status.status};
}
async function authAction(action,store,call){
 if(action==='auth_begin'){
  const r=await call('login_qr_key',{},{}),key=r.body?.data?.unikey;
  if(!key)throw Error('qr_key_unavailable');
  const qr=await call('login_qr_create',{key,platform:'web',qrimg:true},{});
  if(!qr.body?.data?.qrimg?.startsWith('data:image/png;base64,'))throw Error('qr_image_unavailable');
  store.savePending({key,createdAt:Date.now()});
  return {status:'awaiting_scan',qrImage:qr.body.data.qrimg};
 }
 if(action==='auth_status')return sessionStatus(store,call);
 if(action==='auth_maintain')return maintainSession(store,call);
 if(action==='auth_refresh')return maintainSession(store,call,{force:true});
 if(action==='auth_poll'){
  const pending=store.pending();if(!pending)return {status:'no_pending_login'};
  if(Date.now()-pending.createdAt>5*60*1000){store.clearPending();return {status:'expired'};}
  const r=await call('login_qr_check',{key:pending.key},{}),code=r.body?.code;
  if(store.pending()?.key!==pending.key)return {status:'superseded'};
  if(code===800){store.clearPending();return {status:'expired',code};}
  if(code===801||code===802)return {status:code===801?'awaiting_scan':'awaiting_confirmation',code};
  if(code!==803)return {status:'login_failed',code:typeof code==='number'?code:null};
  const cookie=parseCookies(r.cookie);
  if(!cookie.MUSIC_U)throw Error('login_cookie_missing');
  const status=await call('login_status',{},cookie);
  if(!status.body?.data?.account?.id)throw Error('login_status_unverified');
  store.save(cookie,status.body.data.account.id);store.stamp({lastRefreshAt:Date.now(),lastRefreshAttemptAt:Date.now(),lastRefreshError:null});store.clearPending();return {status:'authenticated',authenticated:true};
 }
 throw Error('invalid_auth_action');
}
module.exports={createSessionStore,parseCookies,authAction,sessionStatus,audioAccess,maintainSession};

async function maintainSession(store,call,{force=false,time=Date.now()}={}){
 let cookie,meta;try{cookie=store.load();meta=store.metadata();}catch{return {status:'invalid',authenticated:false,error:'netease_session_invalid',requiresLogin:true};}
 if(!cookie.MUSIC_U)return {status:'anonymous',authenticated:false,requiresLogin:true,renewal:'not_started'};
 const cooldown=meta.lastRefreshError?15*60*1000:24*60*60*1000;
 if(!force&&meta.lastRefreshAttemptAt&&time-meta.lastRefreshAttemptAt<cooldown)return {...await sessionStatus(store,call),renewal:'not_due',...meta};
 const unlock=store.lockRefresh();if(!unlock)return {...await sessionStatus(store,call),renewal:'in_progress'};
 try{
  const before=await sessionStatus(store,call);
  if(['account_mismatch','invalid','check_failed'].includes(before.status))return {...before,renewal:'not_started'};
  store.stamp({lastRefreshAttemptAt:time});
  try{
   const r=await call('login_refresh',{},cookie);if(r.body?.code!==200)throw Error('session_refresh_rejected');
   const merged={...cookie,...parseCookies(r.cookie)};
   const verified=await call('login_status',{},merged),id=verified.body?.data?.account?.id;
   if(!id)throw Error('session_refresh_unverified');
   if(store.load().MUSIC_U!==cookie.MUSIC_U)return {...await sessionStatus(store,call),renewal:'superseded'};
   store.save(merged,id);store.stamp({lastRefreshAt:time,lastRefreshError:null});
   return {status:'authenticated',authenticated:true,requiresLogin:false,renewal:'renewed',...store.metadata()};
  }catch(e){
   store.stamp({lastRefreshError:e.message==='netease_account_mismatch'?'netease_account_mismatch':'session_refresh_failed'});
   if(e.message==='netease_account_mismatch')return {status:'account_mismatch',authenticated:false,error:e.message,requiresLogin:true,renewal:'failed'};
   return {...await sessionStatus(store,call),renewal:'failed',...store.metadata()};
  }
 }finally{unlock();}
}
