const uid=()=>crypto.randomUUID(),now=()=>new Date().toISOString();
const cleanError=e=>/^[a-z_]{2,80}$/.test(e?.message||'')?e.message:'tool_unavailable';
const terminal=['completed','failed','cancelled','rejected'];
const norm=s=>String(s||'').normalize('NFKC').replace(/[\s《》「」“”]/g,'').toLowerCase();
export const reasons={song_parser_not_configured:'点歌解析服务未配置',song_parser_balance:'点歌解析服务额度不足',song_parser_unavailable:'点歌解析服务暂时不可用',tool_unavailable:'工具暂时不可用',singing_not_configured:'Vivi 唱歌服务未配置',singing_no_complete_audio:'没有收到完整歌声音频',generator_failed:'歌声生成调用失败',generation_outcome_unknown:'生成结果未知，需核对原尝试后再决定是否重试',process_restarted_unknown:'服务重启，原生成结果未知，不自动重复付费生成',audio_range_exceeded:'生成长度超出展示范围，需要调整方案，未截短',voice_configuration_mismatch:'生成使用的声音配置不匹配',review_rejected:'试听未通过',request_cancelled:'请求已取消',needs_material:'缺少可确定的歌曲或片段资料',range_needs_clarification:'请求超出首版单段范围，需确认替代片段',playback_lost:'播放页面断开或未收到有效回执',audio_decode_failed:'音频无法解析',audio_silent:'音频为空或静音',audio_truncated:'音频不完整'};


export class SingingCore {
  constructor(state){this.state=state;}
  read(){return this.state;}
  change(fn){return fn(this.state);}
  record(s,t,status,code='',detail={}){t.status=status;t.updatedAt=now();t.reason=code;t.note=reasons[code]||code;s.events.push({id:uid(),taskId:t.id,revision:t.revision,at:t.updatedAt,status,code,...detail});}
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
}
