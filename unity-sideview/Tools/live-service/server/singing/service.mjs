import fs from 'node:fs/promises';
import path from 'node:path';
import {DatabaseSync} from 'node:sqlite';
import {randomUUID} from 'node:crypto';
import {Z_VOICE,sha256,parseSongRequest,generateSinging,checkWav} from './provider.mjs';
import {SongLibrary} from '../song-library/library.mjs';
import {NeteaseSource} from '../song-library/netease.mjs';
import {OfficialRetrieval} from '../song-library/official-retrieval.mjs';
import {decide} from '../song-library/decision.mjs';
import {startNeteaseSessionMaintenance} from '../song-library/netease-auth.mjs';
const uid=()=>randomUUID(),now=()=>new Date().toISOString();
const cleanError=e=>/^[a-z_]{2,80}$/.test(e?.message||'')?e.message:'tool_unavailable';
const terminal=['scheduled','completed','failed','cancelled','rejected'];
export const reasons={
 singing_provider_resource_not_granted:'唱歌服务尚未开通，请后台操作者检查语音项目权限',singing_provider_configuration_error:'唱歌鉴权失败，请后台操作者检查语音配置',singing_provider_unavailable:'唱歌服务暂时不可用',provider_not_singing:'生成服务返回了说话音频，尚无可用歌声',song_parser_not_configured:'点歌解析服务未配置',song_parser_balance:'点歌解析额度不足',song_parser_unavailable:'点歌解析暂时不可用',song_parser_rate_limited:'点歌解析繁忙',tool_unavailable:'准备唱段暂时失败',singing_not_configured:'唱歌服务未配置',singing_no_complete_audio:'没有收到完整歌声音频',generator_failed:'歌声生成未完成',generation_outcome_unknown:'上次生成结果未知，需要后台核对后才能重试',process_restarted_unknown:'服务重启，原生成结果未知，需要后台核对',audio_range_exceeded:'只支持不超过90秒的明确唱段',voice_configuration_mismatch:'声音配置不匹配',review_rejected:'此前素材已被明确拒绝，不再使用',material_previously_rejected:'此前参考资料已被明确拒绝，不再使用',request_cancelled:'点歌已取消',needs_material:'请补充歌名或片段',range_needs_clarification:'请指定第一句、主歌、副歌、桥段或90秒以内的明确范围',audio_decode_failed:'歌声音频无法解析',audio_silent:'歌声音频为空或静音',audio_truncated:'歌声音频不完整',asset_integrity_failed:'歌声文件校验失败',official_not_found:'没有找到符合条件的指定歌曲版本',official_audio_unavailable:'暂时拿不到指定版本的完整音频或歌词',search_incomplete:'歌曲查询暂未完成',netease_login_required:'主播侧网易云需要后台登录',netease_login_expired:'主播侧网易云登录已过期，需要后台重新登录',netease_session_invalid:'主播侧网易云登录状态异常，需要后台处理',netease_account_mismatch:'后台网易云账号与绑定账号不一致',netease_login_check_failed:'暂时无法确认后台网易云登录状态',audio_trial_only_authenticated:'后台已登录，但这首歌仍只有试听音频',audio_access_unavailable_authenticated:'后台已登录，但暂时无法获取这首歌的完整音频',audio_decoder_not_installed:'后台缺少音频解码器',source_not_configured:'歌曲资料来源尚未配置'
};
const voiceMatches=(a,v)=>['identity','speaker','singingModel','mode'].every(k=>a?.[k]===v[k])&&String(a?.revision)===String(v.revision);
export const playableAsset=a=>a&&a.review?.approved!==false&&(a.approved===true||a.acceptance?.policy==='technical_audio_v1'&&a.acceptance.sha256===a.sha256);
const segment=p=>p.segment.kind==='time_range'?{kind:'time_range',startMs:p.segment.startMs,endMs:p.segment.endMs}:p.segment.kind==='lyric_start'?{kind:'lyric_start',text:p.segment.lyricStart,...(p.segment.ordinal>1?{occurrence:p.segment.ordinal}:{})}:{kind:p.segment.kind,ordinal:p.segment.ordinal};

// One durable queue; the existing live host remains the only conversation/TTS owner.
export class SingingService {
 static async open({root,directory=path.join(root,'.runtime/singing'),libraryDirectory=path.join(directory,'library'),environment=()=>({}),parse=parseSongRequest,generate=generateSinging,autoStart=true,sources=[new NeteaseSource()],autoRenew=autoStart,onSessionStatus=()=>{}}={}) {
  await fs.mkdir(directory,{recursive:true});
  const lock=path.join(directory,'service.lock');
  try{const old=JSON.parse(await fs.readFile(lock,'utf8'));try{process.kill(old.pid,0);throw Error('singing_service_already_running');}catch(e){if(e.code!=='ESRCH')throw e;}await fs.unlink(lock);}catch(e){if(e.code!=='ENOENT')throw e;}
  const handle=await fs.open(lock,'wx',0o600);await handle.writeFile(JSON.stringify({pid:process.pid}));await handle.close();
  const self=new SingingService();Object.assign(self,{root,directory,lock,environment,parse,generate,busy:false,closed:false});
  try{
   self.db=new DatabaseSync(path.join(directory,'singing.sqlite'));
   self.db.exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS state (id INTEGER PRIMARY KEY, value TEXT NOT NULL);');
   if(!self.db.prepare('SELECT id FROM state WHERE id=1').get())self.db.prepare('INSERT INTO state VALUES(1,?)').run(JSON.stringify({version:2,voice:Z_VOICE,tasks:[],songs:[],assets:[],events:[],playbacks:[]}));
   self.library=new SongLibrary({directory:libraryDirectory,sources});
   await self.seed();await self.migrate();self.syncLibrary();
   if(autoStart)self.timer=setInterval(()=>void self.tick(),1000);
   if(autoRenew&&sources.some(s=>s instanceof NeteaseSource))self.maintenance=startNeteaseSessionMaintenance({onStatus:onSessionStatus});
   return self;
  }catch(e){self.library?.close();self.db?.close();await fs.unlink(lock).catch(()=>{});throw e;}
 }
 read(){return JSON.parse(this.db.prepare('SELECT value FROM state WHERE id=1').get().value);}
 change(fn){this.db.exec('BEGIN IMMEDIATE');try{const s=this.read(),r=fn(s);this.db.prepare('UPDATE state SET value=? WHERE id=1').run(JSON.stringify(s));this.db.exec('COMMIT');return r;}catch(e){this.db.exec('ROLLBACK');throw e;}}
 record(s,t,status,code='',detail={}){t.status=status;t.updatedAt=now();t.reason=code;t.note=reasons[code]||code;s.events.push({id:uid(),taskId:t.id,revision:t.revision,at:now(),status,code,...detail});}
 async seed(){
  await fs.mkdir(path.join(this.directory,'assets'),{recursive:true});
  const review=JSON.parse(await fs.readFile(path.join(this.root,'seed-review.json'),'utf8'));
  const extra=JSON.parse(await fs.readFile(path.join(this.root,'server/assets/songs/imported-seeds.json'),'utf8'));
  const seeds=[{id:'wanan-yanrenzhong-vivi2-chorus',file:'wanan-yanrenzhong-vivi2-chorus.wav',title:'晚安',artist:'颜人中',sha256:review.sha256,segment:{kind:'chorus',ordinal:1},review},...extra.map(x=>({...x,id:'qingtian-vivi-chorus'}))];
  for(const item of seeds){
   if(this.read().assets.some(a=>a.id===item.id))continue;
   const bytes=await fs.readFile(path.join(this.root,'server/assets/songs',item.file));if(sha256(bytes)!==item.sha256||!item.review.approved)throw Error('seed_review_mismatch');
   const checks=checkWav(bytes);await fs.writeFile(path.join(this.directory,'assets',checks.sha256+'.wav'),bytes,{mode:0o600});
   this.change(s=>s.assets.push({id:item.id,plan:{intent:'request',title:item.title,artist:item.artist,version:'',question:'',segment:{...item.segment,description:'第一段完整副歌',lyricStart:''}},voice:Z_VOICE,sha256:checks.sha256,durationMs:checks.durationMs,approved:true,review:item.review,checks,createdAt:now()}));
  }
 }
 async migrate(){
  // Retain old evidence and explicit rejections. Pending audio gets technical validation,
  // never a fabricated human approval. Shared tasks consult the same asset result.
  for(const a of this.read().assets){
   if(a.approved||a.acceptance||a.review?.approved===false||a.review&&!a.approved)continue;
   try{const bytes=await fs.readFile(path.join(this.directory,'assets',a.sha256+'.wav'));if(sha256(bytes)!==a.sha256)throw Error('asset_integrity_failed');const checks=checkWav(bytes);
    this.change(s=>{const x=s.assets.find(x=>x.id===a.id);x.checks=checks;x.acceptance={policy:'technical_audio_v1',sha256:a.sha256,listeningVerified:false};});
   }catch(e){this.change(s=>{s.assets.find(x=>x.id===a.id).unavailable=cleanError(e);});}
  }
  this.change(s=>{
   s.version=2;
   for(const t of s.tasks){
    if(['parsing','retrieving','generating','checking'].includes(t.status))this.record(s,t,'failed','process_restarted_unknown');
    else if(t.status==='playing')this.record(s,t,'scheduled','',{legacyPlaybackUnknown:true});
    else if(t.status==='awaiting_review'){const a=s.assets.find(x=>x.id===t.assetId);this.record(s,t,playableAsset(a)&&!a.unavailable?'ready':'failed',a?.unavailable||(!playableAsset(a)?'review_rejected':''));}
   }
  });
 }
 syncLibrary(){
  for(const a of this.read().assets){
   if(a.libraryAssetId||!playableAsset(a)||a.unavailable)continue;
   const p=a.plan;
   const r=this.library.register({title:p.title,artist:p.artist||'歌手待确认',version:p.version||'Z 唱段（原参考录音未绑定）'});
   const prior=this.library.db.prepare('SELECT id FROM assets WHERE provenance=?').get('singing-service:'+a.id);
   const item=prior?this.library.asset(prior.id):this.library.addAsset(r.id,{file:path.join(this.directory,'assets',a.sha256+'.wav'),segment:segment(p),voice:a.voice,provenance:'singing-service:'+a.id});
   if(!item.acceptance&&item.review?.approved!==false)this.library.acceptTechnical(item.id);
   this.change(s=>{const x=s.assets.find(x=>x.id===a.id);x.libraryAssetId=item.id;x.recordingId=item.recording_id;});
  }
 }
 submit({text,sourceId,sessionId,owner='local-preview-owner'}){
  if(typeof text!=='string'||!text.trim()||text.length>600||typeof sourceId!=='string'||!sourceId||sourceId.length>100||typeof sessionId!=='string'||!sessionId||sessionId.length>100)throw Error('invalid_request');
  return this.change(s=>{const old=s.tasks.find(t=>t.owner===owner&&t.sourceId===sourceId&&t.originSessionId===sessionId);if(old){if(old.originalText!==text)throw Error('request_conflict');return old;}
   if(s.tasks.length>=500||s.tasks.filter(t=>t.owner===owner&&!terminal.includes(t.status)&&t.status!=='ready').length>=3)throw Error('task_limit');
   const t={id:uid(),owner,sourceId,originSessionId:sessionId,originalText:text,requestText:text,revision:0,attempt:0,createdAt:now(),voice:Z_VOICE,assetId:null,plan:null};s.tasks.push(t);this.record(s,t,'queued');return t;});
 }
 owned(s,id,owner){const t=s.tasks.find(t=>t.id===id&&t.owner===owner);if(!t)throw Error('task_not_found');return t;}
 context(owner){const s=this.read();return {enabled:true,voice:s.voice,tasks:s.tasks.filter(t=>t.owner===owner).slice(-24).map(t=>({id:t.id,sourceId:t.sourceId,originSessionId:t.originSessionId,requesterKind:['provider-verification','management'].includes(t.originSessionId)?'operator':'player',request:t.originalText,plan:t.plan,status:t.status,reason:t.note,createdAt:t.createdAt,updatedAt:t.updatedAt,assetId:t.assetId,question:t.question||'',attempt:t.attempt})),knowledge:s.assets.filter(a=>playableAsset(a)&&!a.unavailable).map(a=>({title:a.plan.title,artist:a.plan.artist,coverage:[a.plan.segment.description],listeningVerified:false})).slice(-30),limits:{singleSegmentSeconds:90,backgroundConcurrency:1,review:'none',fixedWaiting:false,providerLookup:'netease_source_policy',referenceConditioning:false}};}
 overview(owner){return this.context(owner);}
 async tick(){
  if(this.busy||this.closed)return;this.busy=true;
  try{
   const selected=this.change(s=>{const t=s.tasks.find(t=>t.status==='queued');if(!t)return;this.record(s,t,'parsing');return structuredClone(t);});if(!selected)return;
   const {id,revision}=selected;this.activeId=id;this.controller=new AbortController();
   const update=fn=>this.change(s=>{const t=s.tasks.find(t=>t.id===id);if(this.closed||this.controller.signal.aborted||!t||t.revision!==revision||t.status==='cancelled')throw Error('generation_cancelled');return fn(s,t);});
   try{
    const env=await this.environment();let plan=await this.parse(selected.requestText,{secret:env.DEEPSEEK_API_KEY,signal:AbortSignal.any([this.controller.signal,AbortSignal.timeout(20000)])});
    update((s,t)=>t.plan=plan);this.syncLibrary();
    let decision=decide(this.library,plan,{officialOnly:true});
    if(decision.route==='clarify'){update((s,t)=>{t.question=decision.reason;this.record(s,t,plan.intent==='not_request'?'rejected':'needs_input','needs_material');});return;}
    if(decision.route==='unavailable')throw Error(decision.reason);
    if(decision.route==='missing'){
     update((s,t)=>this.record(s,t,'retrieving'));
     if(!this.library.sources.has('netease'))throw Error('source_not_configured');
     const result=await new OfficialRetrieval(this.library).prepare(plan);
     update((s,t)=>t.retrieval={status:result.status,outcome:result.outcome,recordingId:result.recordingId,error:result.error});
     if(result.status!=='completed')throw Error(result.error&&reasons[result.error]?result.error:result.outcome||'official_audio_unavailable');
     const r=this.library.recording(result.recordingId);plan={...plan,title:r.title,artist:r.artist};
     decision=decide(this.library,plan,{officialOnly:true,recordingId:r.id});
    }
    if(decision.route==='cached'){
     const asset=this.read().assets.find(a=>a.libraryAssetId===decision.assetId&&playableAsset(a)&&!a.unavailable);
     if(!asset)throw Error('asset_integrity_failed');await this.audio(asset.id);
     update((s,t)=>{t.plan=asset.plan;t.assetId=asset.id;this.record(s,t,'ready','',{cacheHit:true});});return;
    }
    if(decision.route!=='generate')throw Error('needs_material');
    const recording=this.library.recording(decision.recording.id);
    if(this.library.material(recording.id).status==='review_rejected')throw Error('material_previously_rejected');
    plan={...plan,artist:recording.artist};
    // Unknown paid attempts are not bypassed by a second task for the same range.
    const prior=this.read().tasks.find(t=>t.id!==id&&/unknown/.test(t.reason||'')&&t.recordingId===recording.id&&JSON.stringify(segment(t.plan))===JSON.stringify(segment(plan)));
    if(prior&&!selected.acknowledgeUnknown)throw Error('generation_outcome_unknown');
    const attemptId=uid();update((s,t)=>{t.plan=plan;t.recordingId=recording.id;t.attempt++;t.attemptId=attemptId;this.record(s,t,'generating');});
    const generated=await this.generate(plan,{root:this.root,directory:path.join(this.directory,'attempts',attemptId),secret:env.VOLC_TTS_API_KEY,signal:this.controller.signal});
    update((s,t)=>this.record(s,t,'checking'));
    if(!voiceMatches(generated.report&&{...Z_VOICE,speaker:generated.report.voice,singingModel:generated.report.model},Z_VOICE)||!generated.report.enableMusic||!generated.report.generationDone)throw Error('voice_configuration_mismatch');
    const checks=checkWav(generated.bytes),assetId=uid(),file=path.join(this.directory,'assets',checks.sha256+'.wav');
    await fs.writeFile(file,generated.bytes,{mode:0o600});
    update(()=>{});const libAsset=this.library.addAsset(recording.id,{file,segment:segment(plan),voice:Z_VOICE,provenance:'singing-service:'+assetId});this.library.acceptTechnical(libAsset.id);
    update((s,t)=>{s.assets.push({id:assetId,libraryAssetId:libAsset.id,recordingId:recording.id,plan,voice:Z_VOICE,sha256:checks.sha256,durationMs:checks.durationMs,checks,approved:false,acceptance:{policy:'technical_audio_v1',sha256:checks.sha256,listeningVerified:false},createdAt:now(),provider:generated.report});t.assetId=assetId;this.record(s,t,'ready');});
   }catch(e){if(!this.closed)this.change(s=>{const t=s.tasks.find(t=>t.id===id);if(t&&t.revision===revision&&t.status!=='cancelled')this.record(s,t,'failed',cleanError(e));});}
  }finally{this.busy=false;this.activeId=null;this.controller=null;}
 }
 retry(id,owner,{acknowledgeUnknown=false}={}){return this.change(s=>{const t=this.owned(s,id,owner);if(t.status!=='failed'||t.attempt>=3)throw Error('retry_not_allowed');if(/unknown/.test(t.reason)&&!acknowledgeUnknown)throw Error('confirm_unknown_attempt');t.revision++;t.assetId=null;t.acknowledgeUnknown=acknowledgeUnknown;this.record(s,t,'queued');return t;});}
 clarify(id,owner,text){if(typeof text!=='string'||!text.trim()||text.length>600)throw Error('invalid_request');return this.change(s=>{const t=this.owned(s,id,owner);if(t.status!=='needs_input')throw Error('clarification_not_allowed');t.requestText=JSON.stringify({originalRequest:t.originalText,previousPlan:t.plan,clarification:text});t.revision++;t.question='';this.record(s,t,'queued');return t;});}
 cancel(id,owner){const t=this.change(s=>{const t=this.owned(s,id,owner);if(terminal.includes(t.status))return t;t.revision++;this.record(s,t,'cancelled','request_cancelled');return t;});if(this.activeId===id)this.controller?.abort();return t;}
 async reserve(id,owner){
  const snapshot=this.read(),task=this.owned(snapshot,id,owner);if(task.status!=='ready')throw Error('playback_not_ready');
  await this.audio(task.assetId);
  return this.change(s=>{const t=this.owned(s,id,owner);if(t.status!=='ready')throw Error('playback_not_ready');const a=s.assets.find(a=>a.id===t.assetId);
   // Dispatch is not a playback receipt. Coalesce requests already waiting for this rendition.
   for(const x of s.tasks)if(x.owner===owner&&x.assetId===a.id&&x.status==='ready')this.record(s,x,'scheduled','',{dispatched:true});
   return {taskId:id,song:{id:a.id,title:a.plan.title,version:a.plan.version||a.plan.artist,section:a.plan.segment.description,src:'/api/singing/assets/'+a.id,sha256:a.sha256,playable:true,aCappella:true,readinessRef:'technical_audio_v1',voiceIdentity:a.voice.identity,voiceVersion:Number(a.voice.revision)}};
  });
 }
 async audio(id){const a=this.read().assets.find(a=>a.id===id);if(!playableAsset(a)||a.unavailable||!voiceMatches(a.voice,this.read().voice))throw Error('asset_unavailable');if(a.recordingId&&this.library.material(a.recordingId).status==='review_rejected')throw Error('material_previously_rejected');if(a.libraryAssetId&&this.library.asset(a.libraryAssetId).review?.approved===false)throw Error('asset_unavailable');const bytes=await fs.readFile(path.join(this.directory,'assets',a.sha256+'.wav'));if(sha256(bytes)!==a.sha256)throw Error('asset_integrity_failed');checkWav(bytes);return bytes;}
 async close(){this.closed=true;clearInterval(this.timer);this.controller?.abort();const maintenance=this.maintenance?.stop();while(this.busy)await new Promise(r=>setTimeout(r,20));await maintenance;this.library.close();this.db.close();await fs.unlink(this.lock).catch(()=>{});}
}
