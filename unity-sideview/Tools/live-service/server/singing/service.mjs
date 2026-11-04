import fs from 'node:fs/promises';
import path from 'node:path';
import {DatabaseSync} from 'node:sqlite';
import {randomUUID} from 'node:crypto';
import {Z_VOICE,sha256,parseSongRequest,generateSinging,checkWav} from './provider.mjs';
const uid=()=>randomUUID(),now=()=>new Date().toISOString();
const cleanError=e=>/^[a-z_]{2,80}$/.test(e?.message||'')?e.message:'tool_unavailable';
const terminal=['completed','failed','cancelled','rejected'];
const norm=s=>String(s||'').normalize('NFKC').replace(/[\s《》「」“”]/g,'').toLowerCase();
export const reasons={song_parser_not_configured:'点歌解析服务未配置',song_parser_balance:'点歌解析服务额度不足',song_parser_unavailable:'点歌解析服务暂时不可用',tool_unavailable:'工具暂时不可用',singing_not_configured:'Vivi 唱歌服务未配置',singing_no_complete_audio:'没有收到完整歌声音频',generator_failed:'歌声生成调用失败',generation_outcome_unknown:'生成结果未知，需核对原尝试后再决定是否重试',process_restarted_unknown:'服务重启，原生成结果未知，不自动重复付费生成',audio_range_exceeded:'生成长度超出展示范围，需要调整方案，未截短',voice_configuration_mismatch:'生成使用的声音配置不匹配',review_rejected:'试听未通过',request_cancelled:'请求已取消',needs_material:'缺少可确定的歌曲或片段资料',range_needs_clarification:'请求超出首版单段范围，需确认替代片段',playback_lost:'播放页面断开或未收到有效回执',audio_decode_failed:'音频无法解析',audio_silent:'音频为空或静音',audio_truncated:'音频不完整'};

export class SingingService {
  static async open({root,directory=path.join(root,'data/singing'),environment,parse=parseSongRequest,generate=generateSinging,autoStart=true}={}) {
    await fs.mkdir(directory,{recursive:true,mode:0o700});
    // One service owns the local DB and provider queue. A second preview must
    // not recover or duplicate another process's paid generation.
    const lock=path.join(directory,'service.lock');
    try{const old=JSON.parse(await fs.readFile(lock,'utf8'));try{process.kill(old.pid,0);throw Error('singing_service_already_running');}catch(e){if(e.code!=='ESRCH')throw e;}await fs.unlink(lock);}catch(e){if(e.code!=='ENOENT')throw e;}
    const handle=await fs.open(lock,'wx',0o600);await handle.writeFile(JSON.stringify({pid:process.pid}));await handle.close();
    const self=new SingingService();Object.assign(self,{root,directory,lock,environment,parse,generate,controller:null,busy:false,closed:false});
    try{
      self.db=new DatabaseSync(path.join(directory,'singing.sqlite'));
      self.db.exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS state (id INTEGER PRIMARY KEY CHECK(id=1), value TEXT NOT NULL);');
      if(!self.db.prepare('SELECT id FROM state WHERE id=1').get())self.db.prepare('INSERT INTO state VALUES(1,?)').run(JSON.stringify({version:1,voice:Z_VOICE,tasks:[],songs:[],assets:[],events:[],playbacks:[]}));
      await self.seed();
      self.change(s=>{for(const t of s.tasks){if(['parsing','generating','checking'].includes(t.status))self.record(s,t,'failed','process_restarted_unknown');}
        for(const p of s.playbacks)if(['reserved','playing','paused'].includes(p.status)){p.status='interrupted';const t=s.tasks.find(t=>t.id===p.taskId);if(t&&t.status==='playing'){t.status='ready';self.record(s,t,'ready','playback_lost');}}});
      if(autoStart)self.timer=setInterval(()=>void self.tick(),1000);
      return self;
    }catch(e){self.db?.close();await fs.unlink(lock).catch(()=>{});throw e;}
  }
  read(){return JSON.parse(this.db.prepare('SELECT value FROM state WHERE id=1').get().value);}
  change(fn){this.db.exec('BEGIN IMMEDIATE');try{const s=this.read(),result=fn(s);this.db.prepare('UPDATE state SET value=? WHERE id=1').run(JSON.stringify(s));this.db.exec('COMMIT');return result;}catch(e){this.db.exec('ROLLBACK');throw e;}}
  record(s,t,status,code='',detail={}){t.status=status;t.updatedAt=now();t.reason=code;t.note=reasons[code]||code;s.events.push({id:uid(),taskId:t.id,revision:t.revision,at:t.updatedAt,status,code,...detail});}
  async seed(){
    if(this.read().assets.some(a=>a.id==='wanan-yanrenzhong-vivi2-chorus'))return;
    const review=JSON.parse(await fs.readFile(path.join(this.root,'seed-review.json'),'utf8'));
    const bytes=await fs.readFile(path.join(this.root,'server/assets/songs/wanan-yanrenzhong-vivi2-chorus.wav'));
    if(sha256(bytes)!==review.sha256||!review.approved)throw Error('seed_review_mismatch');
    await fs.mkdir(path.join(this.directory,'assets'),{recursive:true,mode:0o700});
    await fs.writeFile(path.join(this.directory,'assets',review.sha256+'.wav'),bytes,{mode:0o600});
    const seedVoice={identity:review.voiceIdentity,revision:review.voiceVersion,speaker:'zh_female_vv_uranus_bigtts',speakingModel:'seed-tts-2.0',singingModel:'1.2.6.1',mode:'single_voice_a_cappella'};
    const plan={intent:'request',title:'晚安',artist:'颜人中',version:'',question:'',segment:{kind:'chorus',ordinal:1,description:'第一段完整副歌',lyricStart:''}};
    this.change(s=>{s.songs.push({id:'wanan-yanrenzhong',title:plan.title,artist:plan.artist,version:'颜人中歌曲版本',coverage:['第一段完整副歌'],source:'existing_user_review',independentScoreAvailable:false});s.assets.push({id:'wanan-yanrenzhong-vivi2-chorus',plan,voice:seedVoice,sha256:review.sha256,durationMs:Math.round(review.durationSeconds*1000),approved:true,createdAt:now(),review:{source:'seed-review.json',scope:review.scope,approvedAt:review.reviewedOn},checks:{technical:'pass',humanOverall:'pass',independentAutomaticVoice:'unverified'}});});
  }
  findAsset(s,plan){return s.assets.find(a=>a.approved&&a.voice.identity===s.voice.identity&&a.voice.revision===s.voice.revision&&a.voice.speaker===s.voice.speaker&&a.voice.singingModel===s.voice.singingModel&&a.voice.mode===s.voice.mode&&norm(a.plan.title)===norm(plan.title)&&(!plan.artist||norm(a.plan.artist)===norm(plan.artist))&&(!plan.version||norm(a.plan.version)===norm(plan.version))&&a.plan.segment.kind===plan.segment.kind&&a.plan.segment.ordinal===plan.segment.ordinal&&norm(a.plan.segment.lyricStart)===norm(plan.segment.lyricStart)&&a.plan.segment.startMs===plan.segment.startMs&&a.plan.segment.endMs===plan.segment.endMs&&(!['other','lyric_start'].includes(plan.segment.kind)||norm(a.plan.segment.description)===norm(plan.segment.description)));}
  submit({text,sourceId,sessionId,owner='local-preview-owner'}){
    if(typeof text!=='string'||!text.trim()||text.length>600||typeof sourceId!=='string'||!sourceId||sourceId.length>100||typeof sessionId!=='string'||!sessionId||sessionId.length>100)throw Error('invalid_request');
    return this.change(s=>{const old=s.tasks.find(t=>t.owner===owner&&t.sourceId===sourceId&&t.originSessionId===sessionId);if(old){if(old.originalText!==text)throw Error('request_conflict');return old;}
      if(s.tasks.length>=500||s.tasks.filter(t=>t.owner===owner&&!terminal.includes(t.status)&&t.status!=='ready').length>=3)throw Error('task_limit');
      const t={id:uid(),owner,sourceId,originSessionId:sessionId,originalText:text,requestText:text,revision:0,attempt:0,createdAt:now(),updatedAt:now(),voice:structuredClone(s.voice),status:'queued',reason:'',note:'',assetId:null,plan:null};s.tasks.push(t);this.record(s,t,'queued');return t;});
  }
  owned(s,id,owner){const t=s.tasks.find(t=>t.id===id&&t.owner===owner);if(!t)throw Error('task_not_found');return t;}
  context(owner){const s=this.read();return {enabled:true,voice:s.voice,tasks:s.tasks.filter(t=>t.owner===owner).slice(-24).map(t=>({id:t.id,sourceId:t.sourceId,originSessionId:t.originSessionId,requesterKind:['provider-verification','management'].includes(t.originSessionId)?'operator':'player',request:t.originalText,plan:t.plan,status:t.status,reason:t.note,createdAt:t.createdAt,updatedAt:t.updatedAt,assetId:t.assetId,question:t.question||'',attempt:t.attempt})),knowledge:s.songs.slice(-30),limits:{singleSegmentSeconds:90,backgroundConcurrency:1,review:'human_required',providerLookup:'internal_not_exposed',automaticCoverageExpansion:false}};}
  overview(owner){const s=this.read();return {...this.context(owner),assets:s.assets.map(({id,plan,approved,durationMs,sha256,checks,review,voice,createdAt})=>({id,plan,approved,durationMs,sha256,checks,review,voice,createdAt})),events:s.events.filter(e=>s.tasks.some(t=>t.id===e.taskId&&t.owner===owner)).slice(-200)};}
  async tick(){
    if(this.busy||this.closed)return;this.busy=true;
    try{
      const selected=this.change(s=>{const t=s.tasks.find(t=>t.status==='queued');if(!t)return null;this.record(s,t,'parsing');return structuredClone(t);});
      if(!selected)return;const id=selected.id,revision=selected.revision;this.activeId=id;this.controller=new AbortController();
      const update=fn=>this.change(s=>{const t=s.tasks.find(t=>t.id===id);if(!t||t.revision!==revision||t.status==='cancelled'||this.closed)throw Error('generation_cancelled');return fn(s,t);});
      try{
        const env=await this.environment();
        const plan=await this.parse(selected.requestText,{secret:env.DEEPSEEK_API_KEY,signal:AbortSignal.any([this.controller.signal,AbortSignal.timeout(20000)])});
        const next=update((s,t)=>{t.plan=plan;if(plan.intent!=='request'){t.question=plan.question;this.record(s,t,plan.intent==='clarify'?'needs_input':'rejected',plan.intent==='clarify'?'needs_material':'not_a_song_request');return 'stop';}
          const matches=s.songs.filter(x=>norm(x.title)===norm(plan.title));if(!plan.artist&&new Set(matches.map(x=>x.artist)).size>1){t.question='你想听哪个歌手或版本？';this.record(s,t,'needs_input','needs_material');return 'stop';}
          const asset=this.findAsset(s,plan);if(asset){t.assetId=asset.id;this.record(s,t,'ready','',{cacheHit:true,assetId:asset.id});return 'stop';}
          if(plan.segment.kind==='full'||plan.segment.kind==='other'){t.question='首版支持明确的第一句、主歌、副歌、桥段或不超过90秒的范围；请指定片段。';this.record(s,t,'needs_input','range_needs_clarification');return 'stop';}
          t.attempt++;t.attemptId=uid();this.record(s,t,'generating','',{lookup:'provider_internal_not_exposed',attempt:t.attempt,attemptId:t.attemptId});return structuredClone(t);});
        if(next==='stop')return;
        const generated=await this.generate(plan,{root:this.root,directory:path.join(this.directory,'attempts',next.attemptId),secret:env.VOLC_TTS_API_KEY,signal:this.controller.signal});
        update((s,t)=>this.record(s,t,'checking'));
        if(generated.report.voice!==Z_VOICE.speaker||generated.report.model!==Z_VOICE.singingModel||!generated.report.enableMusic||!generated.report.generationDone)throw Error('voice_configuration_mismatch');
        const checks=checkWav(generated.bytes);const assetId=uid();await fs.writeFile(path.join(this.directory,'assets',checks.sha256+'.wav'),generated.bytes,{mode:0o600});
        update((s,t)=>{s.assets.push({id:assetId,plan,voice:structuredClone(t.voice),sha256:checks.sha256,durationMs:checks.durationMs,checks,approved:false,createdAt:now(),attemptId:t.attemptId,provider:generated.report});t.assetId=assetId;this.record(s,t,'awaiting_review','',{assetId,checks,lookup:'provider_internal_not_exposed'});});
      }catch(e){if(!this.closed)this.change(s=>{const t=s.tasks.find(t=>t.id===id);if(t&&t.revision===revision&&t.status!=='cancelled')this.record(s,t,'failed',cleanError(e));});}
    }finally{this.busy=false;this.activeId=null;this.controller=null;}
  }
  review(id,owner,{approved,checks,note}){
    if(typeof approved!=='boolean'||typeof note!=='string'||!note.trim()||note.length>600||approved&&(!checks||!['song','segment','voice','aCappella','ending'].every(k=>checks[k]===true)))throw Error('review_incomplete');
    return this.change(s=>{const t=this.owned(s,id,owner);if(t.status!=='awaiting_review')throw Error('stale_review');const a=s.assets.find(a=>a.id===t.assetId);if(!a||a.voice.revision!==s.voice.revision||a.voice.speaker!==s.voice.speaker||a.voice.singingModel!==s.voice.singingModel||a.voice.mode!==s.voice.mode)throw Error('voice_configuration_mismatch');
      a.approved=approved;a.review={approvedAt:now(),reviewer:owner,note,checks,source:'human_listening'};
      if(approved){let song=s.songs.find(x=>norm(x.title)===norm(a.plan.title)&&norm(x.artist)===norm(a.plan.artist)&&norm(x.version)===norm(a.plan.version));if(!song){song={id:uid(),title:a.plan.title,artist:a.plan.artist,version:a.plan.version,source:'provider_generation_and_human_review',coverage:[],independentScoreAvailable:false};s.songs.push(song);}if(!song.coverage.includes(a.plan.segment.description))song.coverage.push(a.plan.segment.description);}
      this.record(s,t,approved?'ready':'failed',approved?'':'review_rejected',{note,checks});return t;});
  }
  retry(id,owner,{acknowledgeUnknown=false}={}){return this.change(s=>{const t=this.owned(s,id,owner);if(t.status!=='failed'||t.attempt>=3)throw Error('retry_not_allowed');if(/unknown/.test(t.reason)&&!acknowledgeUnknown)throw Error('confirm_unknown_attempt');t.revision++;t.assetId=null;t.voice=structuredClone(s.voice);this.record(s,t,'queued','',{explicitRetry:true});return t;});}
  clarify(id,owner,text){if(typeof text!=='string'||!text.trim()||text.length>600)throw Error('invalid_request');return this.change(s=>{const t=this.owned(s,id,owner);if(t.status!=='needs_input')throw Error('clarification_not_allowed');t.requestText=text;t.revision++;t.question='';this.record(s,t,'queued','',{clarifiedRequest:text});return t;});}
  cancel(id,owner){const t=this.change(s=>{const t=this.owned(s,id,owner);if(t.status==='playing')throw Error('stop_player_first');if(terminal.includes(t.status))return t;t.revision++;this.record(s,t,'cancelled','request_cancelled');return t;});if(this.activeId===id)this.controller?.abort();return t;}
  reserve(id,owner,{sessionId,operationId}){
    if(typeof sessionId!=='string'||!sessionId||sessionId.length>100||typeof operationId!=='string'||!operationId||operationId.length>100)throw Error('invalid_playback');
    return this.change(s=>{const t=this.owned(s,id,owner);let p=s.playbacks.find(p=>p.owner===owner&&p.operationId===operationId);if(p){if(p.taskId!==id||p.sessionId!==sessionId||!['reserved','playing','paused'].includes(p.status))throw Error('stale_playback');return this.playResult(s,t,p);}
      if(!['ready','completed'].includes(t.status)||s.playbacks.some(p=>p.owner===owner&&['reserved','playing','paused'].includes(p.status)))throw Error('playback_not_ready');
      const a=s.assets.find(a=>a.id===t.assetId);if(!a?.approved||a.voice.revision!==s.voice.revision||a.voice.speaker!==s.voice.speaker||a.voice.singingModel!==s.voice.singingModel||a.voice.mode!==s.voice.mode)throw Error('voice_configuration_mismatch');
      p={id:uid(),owner,taskId:id,assetId:a.id,revision:t.revision,sessionId,operationId,status:'reserved',createdAt:now(),updatedAt:now(),positionMs:0,receiptIds:[]};s.playbacks.push(p);this.record(s,t,'playing','',{playbackId:p.id,presentationSessionId:sessionId});return this.playResult(s,t,p);});
  }
  playResult(s,t,p){const a=s.assets.find(a=>a.id===p.assetId);return {playbackId:p.id,taskId:t.id,sessionId:p.sessionId,song:{id:a.id,title:a.plan.title,version:a.plan.version||a.plan.artist,section:a.plan.segment.description,src:`/api/singing/assets/${a.id}`,sha256:a.sha256,approved:true,aCappella:true,reviewRef:'persistent-human-review',voiceIdentity:a.voice.identity,voiceVersion:a.voice.revision}};}
  receipt(id,owner,{eventId,phase,positionMs=0}){
    if(typeof eventId!=='string'||eventId.length>100||!eventId||!['playing','paused','completed','failed','interrupted'].includes(phase)||!Number.isFinite(positionMs)||positionMs<0)throw Error('invalid_receipt');
    return this.change(s=>{const p=s.playbacks.find(p=>p.id===id&&p.owner===owner);if(!p)throw Error('playback_not_found');if(p.receiptIds.includes(eventId))return p;if(['completed','failed','interrupted'].includes(p.status))throw Error('stale_receipt');
      const t=this.owned(s,p.taskId,owner),a=s.assets.find(a=>a.id===p.assetId);if(t.revision!==p.revision||t.status!=='playing')throw Error('stale_receipt');if(phase==='completed'&&(p.status!=='playing'||positionMs<a.durationMs-500))throw Error('invalid_completion');
      p.receiptIds.push(eventId);p.status=phase;p.positionMs=Math.min(positionMs,a.durationMs);p.updatedAt=now();
      if(phase==='completed')this.record(s,t,'completed','',{playbackId:id,positionMs:p.positionMs,presentationSessionId:p.sessionId});
      if(['failed','interrupted'].includes(phase))this.record(s,t,'ready',phase==='failed'?'playback_failed':'playback_interrupted',{playbackId:id,positionMs:p.positionMs});return p;});
  }
  expirePlaybacks(){this.change(s=>{for(const p of s.playbacks){if(['reserved','playing','paused'].includes(p.status)&&Date.now()-Date.parse(p.updatedAt)>120000){p.status='interrupted';const t=s.tasks.find(t=>t.id===p.taskId);if(t?.status==='playing')this.record(s,t,'ready','playback_lost',{playbackId:p.id,positionMs:p.positionMs});}}});}
  async audio(id,{allowUnreviewed=false}={}){const a=this.read().assets.find(a=>a.id===id);if(!a||!a.approved&&!allowUnreviewed)throw Error('asset_unavailable');const bytes=await fs.readFile(path.join(this.directory,'assets',a.sha256+'.wav'));if(sha256(bytes)!==a.sha256)throw Error('asset_integrity_failed');return bytes;}
  async close(){this.closed=true;clearInterval(this.timer);this.controller?.abort();while(this.busy)await new Promise(r=>setTimeout(r,20));this.db.close();await fs.unlink(this.lock).catch(()=>{});}
}
