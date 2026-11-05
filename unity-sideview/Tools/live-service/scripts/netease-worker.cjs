// Isolated read-only provider process. Only our explicitly authorized local
// session is loaded; no browser cookies, ambient NETEASE_COOKIE or alternate sources.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const {createSessionStore,authAction,audioAccess}=require('./netease-session.cjs');
const allowed = new Set(['cloudsearch','song_detail','song_url','song_url_v1','song_download_url_v1','lyric_new','artist_detail']);
console.log = console.info = console.warn = console.error = () => {};
async function main() {
 let raw='';for await(const part of process.stdin)raw+=part;
 const {action,query={}}=JSON.parse(raw),store=createSessionStore();
 fs.mkdirSync(os.tmpdir(),{recursive:true,mode:0o700});
 const token=path.join(os.tmpdir(),'anonymous_token');if(!fs.existsSync(token))fs.writeFileSync(token,'',{mode:0o600});
 const root=path.dirname(require.resolve('@neteasecloudmusicapienhanced/api/package.json'));
 const request=require(path.join(root,'util/request.js'));
 const call=(name,q,cookie)=>require(path.join(root,'module',name+'.js'))({...q,cookie:{...cookie},unblock:'false',randomCNIP:false,timeout:['login_status','login_refresh'].includes(name)?8000:25000},request);
 if(['auth_begin','auth_status','auth_poll','auth_refresh','auth_maintain'].includes(action)){
  const body=await authAction(action,store,call);process.stdout.write(JSON.stringify({ok:true,body}));return;
 }
 if(!allowed.has(action))throw Error('provider_action_invalid');
 let cookie;try{cookie=store.load();}catch{throw Error('netease_session_invalid');}
 const audioAction=['song_url','song_url_v1','song_download_url_v1'].includes(action);
 let result;try{result=audioAction?await audioAccess(action,query,store,call):await call(action,query,cookie);}catch(e){if([301,401].includes(Number(e?.body?.code||e?.status)))throw Error(cookie.MUSIC_U?'netease_login_expired':'netease_login_required');throw e;}
 const body={...result.body,sessionAccess:cookie.MUSIC_U?'authenticated_session':'anonymous'};
 if([301,401].includes(body.code))throw Error(cookie.MUSIC_U?'netease_login_expired':'netease_login_required');
 delete body.cookie; // Never return credentials to parent, logs or web callers.
 process.stdout.write(JSON.stringify({ok:true,body}));
}
main().catch(e=>{
 const code=Number(e?.body?.code||e?.status)||null;
 const safeErrors=['netease_account_mismatch','netease_login_required','netease_login_expired','netease_session_invalid','netease_login_check_failed','audio_trial_only_authenticated','audio_access_unavailable_authenticated'];
 process.stdout.write(JSON.stringify({ok:false,code,...(safeErrors.includes(e.message)?{error:e.message}:{})}));process.exitCode=1;
});
