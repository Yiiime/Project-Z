import fs from 'node:fs';
import path from 'node:path';
import {randomUUID,createHash} from 'node:crypto';
import {handleAgent} from './shared/worker/agent-director.js';
import {handleTts} from './shared/worker/volc-tts.js';
import {singingRequest} from './singing/index.mjs';
import {PcmSpeech} from './pcm-speech.mjs';
import {SpeechDirector} from './shared/ui/speech-director.js';
export const uid=()=>randomUUID();
export const hash=b=>createHash('sha256').update(b).digest('hex');
export function loadEnvironment(source){
 const env={};for(const [name,file] of [['DEEPSEEK_API_KEY','DeepSeek配置.env'],['VOLC_TTS_API_KEY','火山语音配置.env'],['VOLC_ASR_API_KEY','火山识别配置.env'],['NETEASE_SESSION_FILE','.env.local'],['FFMPEG_PATH','.env.local'],['Z_NETEASE_AUTO_RENEW','.env.local'],['Z_SONG_CATALOG','.env.local']]){
  env[name]=process.env[name]?.trim()||'';if(env[name])continue;
  for(const f of ['.env.local',file,'.env']){try{const s=fs.readFileSync(path.join(source,f),'utf8');const m=s.match(new RegExp('^[ \\t]*(?:export[ \\t]+)?'+name+'[ \\t]*=[ \\t]*([^\\r\\n]*)','m'));if(!m)continue;const v=m[1].trim();const q=v.match(/^(['"])(.*?)\1\s*(?:#.*)?$/);env[name]=q?q[2]:/^["']/.test(v)?'':v.replace(/(?:^|\s+)#.*$/,'').trim();if(env[name])break;}catch{}}
 }return env;
}
function request(route,identity,payload,signal){return new Request('https://z-internal.local'+route,{method:payload?'POST':'GET',headers:{Origin:'https://z-internal.local','Content-Type':'application/json','oai-authenticated-user-id':identity},...(payload?{body:JSON.stringify(payload)}:{}),signal});}
export class Providers {
 constructor(env,options={}){this.env=env;this.options=options;this.singing=options.singing;}
 forOwner(owner){return new Providers(this.env,{...this.options,owner,singingContext:()=>this.singing?.context(owner,{publicRoom:true})});}
 async singingFetch(route,options={}){if(!this.singing)return Response.json({error:"singing_unavailable"},{status:503});return singingRequest(this.singing,this.options.owner||"local-owner",route,options.body?JSON.parse(options.body):null);}
 async config(identity){return (await handleAgent(request('/api/agent/config',identity),this.env,this.options)).json();}
 async generate(identity,payload,{signal,onSentence,compactReview,reviewExpanded,broadcastContext,daytimeTasks,songSession,character}={}){const r=await handleAgent(request('/api/agent',identity,payload,signal),this.env,{...this.options,onSentence,compactReview,reviewExpanded,broadcastContext,daytimeTasks,songSession,character}),body=await r.json();if(!r.ok)throw Object.assign(Error(body.error),{code:body.error,validationReason:body.validationReason});return this.options.modelSource&&body.source==='deepseek'?{...body,source:this.options.modelSource,model:this.options.modelName}:body;}
 async synthesizeStream(identity,text,mood,signal,delivery){return handleTts(request('/api/tts',identity,{requestId:uid(),text,...(delivery?{delivery}:{}),mood:({neutral:'warm',shy:'warm',sad:'warm',surprised:'happy'})[mood]||mood},signal),this.env,{...this.options,audioFormat:'pcm'});}
 async synthesize(identity,text,mood,signal,delivery){
  let failure=null;const options={...this.options,diagnostics:detail=>{this.options.diagnostics?.(detail);if(detail.classification==='provider_error_code'&&detail.status===45000010)failure='tts_provider_configuration_error';if(detail.classification==='provider_error_code'&&detail.status===45000030)failure='tts_provider_resource_not_granted';}};
  const requestId=uid(),controller=new AbortController();const abort=()=>controller.abort();signal?.addEventListener('abort',abort,{once:true});const timer=setTimeout(abort,30000);
  try{if(signal?.aborted)throw Error('cancelled');
   const r=await handleTts(request('/api/tts',identity,{requestId,text,...(delivery?{delivery}:{}),mood:({neutral:'warm',shy:'warm',sad:'warm',surprised:'happy'})[mood]||mood},controller.signal),this.env,options);
   if(!r.ok){const error=await r.json();throw Error(error.error||'tts_unavailable');}
   const audio=await SpeechDirector.prototype.readAudio.call({},r.body,null,requestId,controller.signal);
   const clean=v=>v.normalize('NFKC').replace(/[^\p{L}\p{N}]/gu,'');
   if(clean(audio.words.map(w=>w.word||'').join(''))!==clean(text))throw Error('transcript_unverified');
   this.voiceError=null;return {bytes:Buffer.from(audio.bytes),words:audio.words,contentType:'audio/mpeg'};
  }catch(error){this.voiceError=failure||(/^tts_[a-z_]+$/.test(error.message)?error.message:'tts_unavailable');throw Error(this.voiceError);}finally{clearTimeout(timer);signal?.removeEventListener('abort',abort);controller.abort();}
 }
}
export class ArtifactStore {
 constructor(root){this.root=root;fs.mkdirSync(root,{recursive:true,mode:0o700});}
 put(bytes,contentType,extra={}){bytes=Buffer.isBuffer(bytes)?bytes:Buffer.from(JSON.stringify(bytes));const artifactId=hash(bytes),meta={...extra,artifactId,contentType,byteLength:bytes.length,sha256:artifactId};if(!fs.existsSync(path.join(this.root,artifactId))){fs.writeFileSync(path.join(this.root,artifactId),bytes,{mode:0o600});fs.writeFileSync(path.join(this.root,artifactId+'.json'),JSON.stringify(meta),{mode:0o600});}return meta;}
 get(id){if(!/^[a-f0-9]{64}$/.test(id))throw Error('artifact_unavailable');return {meta:JSON.parse(fs.readFileSync(path.join(this.root,id+'.json'),'utf8')),bytes:fs.readFileSync(path.join(this.root,id))};}
}
export class RemoteSpeech extends EventTarget {
 constructor(session,providers){super();this.session=session;this.providers=providers;this.enabled=session.capabilities.audioEnabled;this.volume=this.enabled?1:0;this.available=!!providers.env.VOLC_TTS_API_KEY;this.warm=new Map();this.streams=new Map();}
 cancel(){for(const stream of this.streams.values())stream.close();this.streams.clear();this.dropWarmSpeech();this.session.cancelHost("guest_interrupt");}
 fadeOut(ms){return this.session.fadeHost(ms);}
 warmSpeech(key,text,mood,{streaming=false,delivery}={}){
  if(!this.enabled||!this.available)return null;
  const signature=JSON.stringify([text,mood,delivery||'']);const old=this.warm.get(key);
  if(old?.signature===signature&&!!old.stream===streaming)return old.promise;
  this.dropWarmSpeech();
  if(streaming){const stream=new PcmSpeech(text,signal=>this.providers.synthesizeStream(this.session.id,text,mood,signal,delivery));this.warm.set(key,{signature,stream,controller:stream.controller,promise:stream.ready});return stream.ready;}
  const controller=new AbortController();const promise=this.prepareAudio(text,mood,{signal:controller.signal,delivery});promise.catch(()=>{});this.warm.set(key,{signature,controller,promise});this.dispatchEvent(new CustomEvent('preparation',{detail:{requestId:key,status:'started'}}));return promise;
 }
 dropWarmSpeech(key){for(const [k,v] of this.warm)if(!key||key===k){v.controller.abort();v.stream?.close();this.warm.delete(k);}}
 matchingWarm(key,text,mood,delivery){const warm=this.warm.get(key);if(warm&&warm.signature!==JSON.stringify([text,mood,delivery||''])){this.dropWarmSpeech(key);return null;}return warm;}
 claimStream(key,text,mood,signal,delivery){const warm=this.matchingWarm(key,text,mood,delivery);let stream;if(warm?.stream){this.warm.delete(key);stream=warm.stream;}else{if(warm)this.dropWarmSpeech(key);stream=new PcmSpeech(text,s=>this.providers.synthesizeStream(this.session.id,text,mood,s,delivery));}this.streams.set(stream.id,stream);signal?.addEventListener('abort',()=>{stream.close();this.streams.delete(stream.id);},{once:true});return stream;}
 claim(key,text,mood,signal,delivery){const warm=this.matchingWarm(key,text,mood,delivery);if(warm&&!warm.stream){this.warm.delete(key);signal?.addEventListener('abort',()=>warm.controller.abort(),{once:true});return warm.promise;}if(warm)this.dropWarmSpeech(key);return this.prepareAudio(text,mood,{signal,delivery});}
 async prepareAudio(text,mood,{signal,delivery}={}){const a=await this.providers.synthesize(this.session.id,text,mood,signal,delivery);if(signal?.aborted)throw Error('cancelled');const meta=this.session.artifacts.put(a.bytes,a.contentType);this.session.artifactIds.add(meta.artifactId);return {...meta,words:a.words};}
}
