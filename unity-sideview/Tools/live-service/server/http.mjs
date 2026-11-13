import http from 'node:http';
import https from 'node:https';
import fs from 'node:fs';
import path from 'node:path';
import {timingSafeEqual} from 'node:crypto';
import {LiveSession} from './session.mjs';
import {hash} from './providers.mjs';
import {transcribePcm} from './voice/index.mjs';
import {singingRequest} from './singing/index.mjs';
const json=(res,status,body)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(body));};
export async function createLiveServer({root,providers,artifacts,songRoot,token,tokens,tls,library,transcribe=transcribePcm,voice=null,characterServiceUrl=null}){
 if(characterServiceUrl){const target=new URL(characterServiceUrl);if(target.protocol!=='http:'||target.hostname!=='127.0.0.1'||target.username||target.password||target.pathname!=='/'||target.search||target.hash)throw Error('invalid_character_service');characterServiceUrl=target.origin;}
 const identities=tokens||{'local-owner':token};
 if(Object.values(identities).some(value=>typeof value!=='string'||!value.length))throw Error('invalid_access_tokens');
 const pendingCreations=new Map();
 const scoped=new Map();const providerFor=owner=>{if(!scoped.has(owner))scoped.set(owner,providers.forOwner?providers.forOwner(owner):providers);return scoped.get(owner);};
 const sessions=new Map(),creations=new Map(),songOperations=new Map(),voiceRequests=new Map();fs.mkdirSync(root,{recursive:true,mode:0o700});
 for(const file of fs.readdirSync(root).filter(n=>/^[a-f0-9-]+\.json$/.test(n))){const saved=JSON.parse(fs.readFileSync(path.join(root,file),'utf8'));if(saved.phase==='ended'||!identities[saved.owner])continue;if(characterServiceUrl&&saved.characterBinding)saved.characterBinding={...saved.characterBinding,memoryUrl:characterServiceUrl};const s=new LiveSession({root,providers:providerFor(saved.owner),artifacts,songRoot,saved,id:saved.id,owner:saved.owner,capabilities:saved.capabilities});await s.initialize();sessions.set(s.id,s);}
 const handler=async(req,res)=>{try{
  const auth=Buffer.from(req.headers.authorization||'');const owner=Object.keys(identities).find(id=>{const expected=Buffer.from('Bearer '+identities[id]);return auth.length===expected.length&&timingSafeEqual(auth,expected);});if(!owner)return json(res,401,{error:'unauthorized'});
  const currentProviders=providerFor(owner);
  const url=new URL(req.url,'http://localhost');if(req.method==='GET'&&url.pathname==='/health')return json(res,200,{ok:true,protocolVersion:1,agentConfigured:!!providers.env.DEEPSEEK_API_KEY,ttsConfigured:!!providers.env.VOLC_TTS_API_KEY,...(providers.options?.modelSource?{modelSource:providers.options.modelSource}:{})});
  const pcmRoute=url.pathname.match(/^\/v1\/live\/sessions\/([a-f0-9-]+)\/audio-stream\/([a-f0-9-]+)$/);
  if(pcmRoute&&req.method==='GET'){const s=sessions.get(pcmRoute[1]);if(!s||s.owner!==owner)return json(res,404,{error:'not_found'});const stream=s.speech.streams.get(pcmRoute[2]);if(!stream||s.phase!=='live')return json(res,404,{error:'stale_audio_stream'});stream.response(res);return;}
  const rtcRoute=url.pathname.match(/^\/v1\/live\/sessions\/([a-f0-9-]+)\/voice\/(config|start|joined|stop|enroll)$/);
  if(rtcRoute){
   const s=sessions.get(rtcRoute[1]);if(!s||s.owner!==owner)return json(res,404,{error:'not_found'});if(!voice)return json(res,503,{error:'voiceprint_not_configured'});
   const action=rtcRoute[2];
   if(action==='config'&&req.method==='GET'){s.voiceConfig=await voice.publicConfig(owner);return json(res,200,s.voiceConfig);}
   if(req.method!=='POST')return json(res,405,{error:'method_not_allowed'});
   if(action==='enroll'){
    if(s.engine._state.call.active)return json(res,409,{error:'voiceprint_leave_call_first'});
    if(req.headers['content-type']!=='audio/wav')return json(res,415,{error:'wav_required'});
    s.check({protocolVersion:1,sessionId:s.id,epoch:Number(req.headers['x-live-epoch']),executorLeaseId:req.headers['x-live-executor-lease']},true);
    const chunks=[];let size=0;for await(const chunk of req){size+=chunk.length;if(size>1000000)return json(res,413,{error:'body_too_large'});chunks.push(chunk);}
    const c=new AbortController(),abort=()=>c.abort();res.once('close',abort);
    try{return json(res,200,await voice.enroll(owner,Buffer.concat(chunks),c.signal));}finally{res.off('close',abort);}
   }
   if(!req.headers['content-type']?.startsWith('application/json'))return json(res,415,{error:'json_required'});
   const chunks=[];let size=0;for await(const chunk of req){size+=chunk.length;if(size>2048)return json(res,413,{error:'body_too_large'});chunks.push(chunk);}
   let b;try{b=JSON.parse(Buffer.concat(chunks));}catch{return json(res,400,{error:'invalid_json'});}s.check(b,true);
   if(action==='start')return json(res,200,await voice.start(s));
   if(action==='joined')return json(res,200,voice.joined(s,b.callId));
   if(action==='stop'){if(b.callId!==s.engine._state.call.id)return json(res,409,{error:'stale_voice_turn'});return json(res,200,voice.stop(s));}
  }
  const voiceRoute=url.pathname.match(/^\/v1\/live\/sessions\/([a-f0-9-]+)\/(asr|dictation)$/);
  if(voiceRoute&&req.method==='POST'){
   const session=sessions.get(voiceRoute[1]);if(!session||session.owner!==owner)return json(res,404,{error:'not_found'});const version=Number(req.headers['x-live-call-version']);
   const dictation=voiceRoute[2]==='dictation',phase=session?.phase;
   const current=()=>session&&!session.closed&&session.sessionControl.normal&&session.phase===phase&&phase!=='ended'&&version===session.engine.callVersion&&(dictation?!session.engine._state.call.active:phase==='live'&&session.engine._state.call.active&&session.engine._state.call.input==='listening');
   if(!current())return json(res,409,{error:'stale_voice_turn'});
   if(voiceRequests.has(session.id))return json(res,409,{error:'asr_busy'});
   if(req.headers['content-type']!=='audio/pcm')return json(res,415,{error:'pcm_required'});
   const cancellation=new AbortController();voiceRequests.set(session.id,cancellation);const cancel=()=>cancellation.abort();req.once('aborted',cancel);res.once('close',cancel);
   const expiry=setTimeout(cancel,30000);const monitor=setInterval(()=>{if(!current())cancellation.abort();},100);
   try{const chunks=[];let size=0;for await(const chunk of req){size+=chunk.length;if(size>640000)return json(res,413,{error:'body_too_large'});chunks.push(chunk);}const result=await transcribe(Buffer.concat(chunks),{apiKey:providers.env.VOLC_ASR_API_KEY,uid:session.id,signal:cancellation.signal});
    if(cancellation.signal.aborted||!current())return json(res,409,{error:'stale_voice_turn'});return json(res,200,{...result,callVersion:version});
   }finally{clearTimeout(expiry);clearInterval(monitor);req.off('aborted',cancel);res.off('close',cancel);voiceRequests.delete(session.id);}
  }
  let body;const write=req.method==='POST';if(write){if(!req.headers['content-type']?.startsWith('application/json'))return json(res,415,{error:'json_required'});const chunks=[];let size=0;for await(const b of req){size+=b.length;if(size>32768)return json(res,413,{error:'body_too_large'});chunks.push(b);}try{body=JSON.parse(Buffer.concat(chunks).toString());}catch{return json(res,400,{error:'invalid_json'});}}
  const key=req.headers['idempotency-key'];if(write&&(typeof key!=='string'||key.length<8||key.length>128))return json(res,400,{error:'idempotency_key_required'});
  const fingerprint=write?hash(Buffer.from(JSON.stringify(body))):null;
  if(url.pathname==='/api/song-library/lookup'&&write){if(!library)return json(res,503,{error:'library_unavailable'});return json(res,200,await library.lookup(body));}
  if(url.pathname.startsWith('/api/singing/')){
   if(!providers.singing)return json(res,503,{error:'singing_unavailable'});
   const op=owner+':'+url.pathname+':'+key;let result;const old=write?songOperations.get(op):null;
   if(old){if(old.fingerprint!==fingerprint)return json(res,409,{error:'idempotency_conflict'});result=await old.promise;}
   else{const promise=(async()=>{const response=await singingRequest(providers.singing,owner,url.pathname+url.search,body);return {status:response.status,headers:Object.fromEntries(response.headers),bytes:Buffer.from(await response.arrayBuffer())};})();if(write){songOperations.set(op,{fingerprint,promise});if(songOperations.size>1000)songOperations.delete(songOperations.keys().next().value);}result=await promise;}
   res.writeHead(result.status,result.headers);return res.end(result.bytes);
  }
  if(url.pathname==='/v1/live/sessions'&&write){if(body.protocolVersion!==1||body.mode!=='private')return json(res,400,{error:'unsupported_protocol_or_mode'});
   const creationKey=owner+':'+key;if(creations.has(creationKey)){const c=creations.get(creationKey);if(c.fingerprint!==fingerprint)return json(res,409,{error:'idempotency_conflict'});return json(res,201,(await c.promise).snapshot());}
   if([...sessions.values()].filter(s=>s.owner===owner&&s.phase!=='ended').length+(pendingCreations.get(owner)||0)>=(tokens?2:20))return json(res,429,{error:'session_limit'});
   pendingCreations.set(owner,(pendingCreations.get(owner)||0)+1);
   const promise=(async()=>{try{const s=new LiveSession({root,providers:currentProviders,owner,artifacts,songRoot,memory:body.memory,characterBinding:body.characterBinding,story:body.story,capabilities:body.capabilities});await s.initialize();sessions.set(s.id,s);return s;}finally{pendingCreations.set(owner,pendingCreations.get(owner)-1);}})();creations.set(creationKey,{fingerprint,promise});return json(res,201,(await promise).snapshot());
  }
  const match=url.pathname.match(/^\/v1\/live\/sessions\/([a-f0-9-]+)(?:\/(events|inputs|receipts|control|heartbeat|artifacts)(?:\/([a-f0-9]{64}))?)?$/);if(!match)return json(res,404,{error:'not_found'});
  const s=sessions.get(match[1]);if(!s||s.owner!==owner)return json(res,404,{error:'not_found'});const route=match[2];
  if(req.method==='GET'&&!route)return json(res,200,s.snapshot());
  if(req.method==='GET'&&route==='artifacts'){if(!s.artifactIds.has(match[3]))return json(res,404,{error:'artifact_unavailable'});const a=artifacts.get(match[3]);res.writeHead(200,{'Content-Type':a.meta.contentType,'Content-Length':a.bytes.length,'Cache-Control':'private, max-age=31536000, immutable','X-Content-SHA256':a.meta.sha256});return res.end(a.bytes);}
  if(req.method==='GET'&&route==='events'){
   const after=Number(url.searchParams.get('afterSeq')||0);if(!Number.isSafeInteger(after)||after<0||after>s.seq||after<(s.events[0]?.serverSeq||1)-1)return json(res,409,{error:'resync_required'});
   res.writeHead(200,{'Content-Type':'text/event-stream','Cache-Control':'no-store',Connection:'keep-alive','X-Accel-Buffering':'no'});res.write(': connected\n\n');const send=e=>{if(!res.destroyed&&!res.write(`id: ${e.serverSeq}\nevent: live\ndata: ${JSON.stringify(e)}\n\n`)&&res.writableLength>4*1024*1024)res.destroy();};for(const e of s.events)if(e.serverSeq>after)send(e);s.on('event',send);const timer=setInterval(()=>res.write(': heartbeat\n\n'),10000);res.once('close',()=>{clearInterval(timer);s.off('event',send);});return;
  }
  if(!write)return json(res,405,{error:'method_not_allowed'});
  const dedupe=route+':'+key,old=s.idempotency.get(dedupe);if(old){if(old.fingerprint!==fingerprint)return json(res,409,{error:'idempotency_conflict'});if(route==="control"&&body.action==="end")await s.characters?.flush();return json(res,old.status,old.result);}
  const methods={inputs:'input',receipts:'receipt',control:'control',heartbeat:'heartbeat'};if(!methods[route])return json(res,404,{error:'not_found'});
  const result=s[methods[route]](body),status=['inputs','control'].includes(route)?202:200;s.idempotency.set(dedupe,{fingerprint,status,result});if(s.idempotency.size>2000)s.idempotency.delete(s.idempotency.keys().next().value);s.flush();if(route==="control"&&body.action==="end")await s.characters?.flush();return json(res,status,result);
 }catch(e){const code=/^[a-z_]{1,80}$/.test(e.message)?e.message:'server_error';if(!res.headersSent)json(res,code==='server_error'?500:409,{error:code});else res.destroy();}};
 const server=tls?https.createServer(tls,handler):http.createServer(handler);
 const maintenance=setInterval(()=>{for(const s of sessions.values())s.maintenance();},1000);maintenance.unref();
 server.on('close',()=>{voice?.close();for(const c of voiceRequests.values())c.abort();clearInterval(maintenance);for(const s of sessions.values())s.close();});return {server,sessions};
}
