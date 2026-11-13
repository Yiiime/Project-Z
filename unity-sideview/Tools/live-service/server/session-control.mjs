import {utteranceMetadata} from './shared/utterance.js';
import {randomUUID} from 'node:crypto';

const FALLBACK='那今天就先聊到这里。谢谢大家陪我，大家晚安。';
const terminal=new Set(['completed','displayed','ready','failed','skipped','interrupted','cancelled']);
export const SESSION_LIMITS={playerClosingMs:30000,hostClosingMs:45000,extensionMs:120000,invitationMs:6000,targetMs:480000,hardMs:600000};
const BEATS=[
  {id:"connect",at:0,label:"确定话题",directive:"接住玩家当前表达，选择一个有公开依据的本场话题，不重复完整开场。"},
  {id:"develop",at:60000,label:"展开交流",directive:"沿当前话题展开具体细节或人物动机，回应玩家，不不断另起话题。"},
  {id:"advance",at:180000,label:"推进故事",directive:"基于已知故事或本场真实交流推进一个小变化，或给玩家一个可自由回应的选择；不虚构世界事实，不代选、不因时间到宣称任务完成。无剧情素材时深化当前话题。"},
  {id:"resolve",at:300000,label:"回应与回收",directive:"接住玩家实际回应，说明已有依据的结果，回收本场线索；未回答的选择不代答，不开启新长活动。"},
  {id:"wrap",at:420000,label:"准备收尾",directive:"处理一个关键未尽事项，给已聊内容一个落点，表达准备收播；不再展开新故事或才艺。"},
  {id:"close",at:480000,label:"收尾时间",directive:"停止扩展内容，简短交代未完事项并自然道别；仅在宿主允许时接受一次有限挽留。"}
];

// Transport live/paused is separate from the conversation's five phases.
export class SessionControl {
  constructor(session,saved=null,{now=Date.now,limits={}}={}) {
    this.s=session;this.now=now;this.limits={...SESSION_LIMITS,...limits};
    this.data=saved?structuredClone(saved):{phase:'opening',version:0,startedAt:now(),closeReason:null,extensionUsed:0,extensionActive:false,extensionScope:'',deadlineAt:null,extensionDeadline:null,readyAt:null};
    // Persist absolute deadlines: pause, reconnect and restart never buy more time.
    this.data.targetAt??=this.data.startedAt+this.limits.targetMs;
    this.data.hardAt??=this.data.startedAt+this.limits.hardMs;
    this.data.paceIndex??=-1;
    this.inputQueue=[];this.requests=new Set();this.disposed=false;
  }
  get onboarding(){return ['arrival','first_evening'].includes(this.s.scene?.context?.mode);}
  eveningElapsed(){return Math.max(0,(this.s.engine._clock||0)-(this.data.eveningStartClock??this.s.engine._clock??0));}
  get normal(){return ['opening','live'].includes(this.data.phase);}
  timing(){
    const d=this.data,at=d.endedAt??this.now(),elapsed=Math.max(0,at-d.startedAt);
    const index=BEATS.findLastIndex(b=>elapsed>=b.at),beat=BEATS[Math.max(0,index)];
    const next=BEATS[index+1];
    return {targetMs:d.targetAt-d.startedAt,hardMs:d.hardAt-d.startedAt,targetRemainingMs:Math.max(0,d.targetAt-at),hardRemainingMs:Math.max(0,d.hardAt-at),beat:beat.id,beatLabel:beat.label,directive:d.extensionActive?'只完成已经约定的追加范围，不重开故事；范围完成就提出 finish_extension。':beat.directive,nextBeatInMs:next?Math.max(0,d.startedAt+next.at-at):null};
  }
  snapshot(){const d=this.data;return {phase:d.phase,version:d.version,closeReason:d.closeReason,extensionUsed:d.extensionUsed,extensionActive:d.extensionActive,extensionScope:d.extensionScope,remainingMs:d.deadlineAt||d.extensionActive?Math.max(0,(d.deadlineAt||d.extensionDeadline)-this.now()):null,elapsedMs:Math.max(0,(d.endedAt??this.now())-d.startedAt),...(!this.onboarding?{timing:this.timing()}:{})};}
  advancePace(){
    const index=BEATS.findLastIndex(b=>this.now()-this.data.startedAt>=b.at);
    if(index<=this.data.paceIndex)return;
    this.data.paceIndex=index;this.data.version++;
    // Replan the next turn with fresh timing; never interrupt the current utterance here.
    const e=this.s.engine;e.epoch++;e.pending?.abort();e.pending=null;
    e._cancelPreparation('session_time_checkpoint');e._cancelTopic('session_time_checkpoint');
    this.s.speech.dropWarmSpeech();e.speechPlan=null;e._continuations=[];e.nextWake=e._clock;
    this.changed();
  }
  changed(){this.s.changed();}
  opened(){if(this.data.phase==='opening'){this.data.phase='live';this.data.version++;this.changed();}}
  context(mode,message=null){return {mode,sessionControl:this.snapshot(),publicEvents:this.s.engine.session.evidence().slice(-32),message};}
  async request(kind,context,timeoutMs=6000){
    const controller=new AbortController();this.requests.add(controller);let timer;
    try{return await Promise.race([this.s.providers.generate(this.s.id,{kind,requestId:randomUUID(),eventId:randomUUID(),context},{signal:controller.signal}),new Promise((_,reject)=>{timer=setTimeout(()=>{controller.abort();reject(Error('session_timeout'));},timeoutMs);})]);}
    finally{clearTimeout(timer);this.requests.delete(controller);}
  }
  enqueue(message){
    if(this.disposed||['farewell','ended'].includes(this.data.phase)||this.data.closeReason==='player')return;
    // One bounded semantic lane; no keyword matching, and no need to wait for singing.
    if(this.inputQueue.length>=16)this.inputQueue.shift();
    this.inputQueue.push(message);void this.inspectInputs();
  }
  async inspectInputs(){
    if(this.inspecting)return;this.inspecting=true;
    try{while(this.inputQueue.length&&!this.disposed){
      const message=this.inputQueue.shift(),version=this.data.version;
      let reply;try{reply=await this.request('session',this.context('inspect',message));}catch{continue;}
      if(this.disposed||this.data.phase==='ended')return;
      if(reply.intent==='stop'){this.stop();return;}
      if(version===this.data.version&&reply.intent==='extend')this.extend(message.id,reply.scope);
    }}finally{this.inspecting=false;}
  }
  decision(result,context){
    const d=result.sessionDecision;if(!d)return false;
    if(this.s.scene?.context?.mode==='arrival' && d.intent!=='stop')return false;
    if(!this.normal)return true;
    if(context.host.sessionControl?.version!==this.data.version)return true;
    const player=context.host.inbox.find(m=>m.mine&&m.id===d.sourceMessageId);
    if(d.intent==='stop'){if(player)this.stop();return true;}
    if(d.intent==='close'){
      if(this.s.scene?.context?.mode==='first_evening' && (this.eveningElapsed()<135000 || !this.data.eveningSongComplete))return true;
      const e=this.s.engine;
      if(this.data.phase!=='live'||(this.s.scene?.context?.mode==='first_evening'?e._mediaPlaying():e._state.media)||e.inbox.some(m=>m.mine&&!result.handledIds.includes(m.id)))return true;
      // New inputs invalidate the engine epoch before this commit hook.
      this.begin('host',result.speech,result);this.pendingHandled=[...result.handledIds];return true;
    }
    if(d.intent==='finish_extension'&&this.data.extensionActive){this.begin('host',result.speech,result);return true;}
    return true;
  }
  freeze(){
    const s=this.s,e=s.engine;
    e.playbackLifecycle++;e.epoch++;e.pending?.abort();e.pending=null;e._cancelPreparation('session_closing');e._cancelTopic('session_closing');
    e.audience.clearPreparation('session_closing');e.audience.invalidate();e.audiencePreview=null;
    s.speech.dropWarmSpeech();s.cancelHost('session_closing');
    if(e._state.call.active)e.endCall();
    s.talent.cancel('session_closing');s.talent.finish(true);
    e._queue=[];e._continuations=[];e._jobs=[];e.speechPlan=null;e.callNeedsTurn=false;
    for(const m of e.inbox)if(m.mine)e._setReceipt(m.id,'cancelled');
    e.inbox=[];e._state.replyStatus='cancelled';e.audience.dialogue.setWaiting([]);
    for(const c of [...s.commands.values()])if(!terminal.has(c.status)){
      c.status='cancelled';s.waiters.get(c.commandId)?.reject(Error('session_closing'));s.waiters.delete(c.commandId);
    }
    // This barrier must be acknowledged by the executor before a new voice starts.
    this.barrier=s.command('execution.cancel',{all:true,reason:'session_closing'}).commandId;
    this.barrierReady=false;
    s.phase='live';e.setPaused(false);s.command('media.resume',{});
  }
  stop(){
    const d=this.data;if(this.disposed||d.phase==='ended'||d.closeReason==='player')return;
    const deadline=this.now()+this.limits.playerClosingMs;
    if(d.phase==='farewell'){d.closeReason='player';d.deadlineAt=Math.min(d.deadlineAt,deadline);this.changed();return;}
    this.begin('player');
  }
  begin(reason,announcement='',metadata={}){
    const d=this.data,oldDeadline=d.deadlineAt;
    d.phase='closing';d.closeReason=reason;d.version++;d.extensionActive=false;
    d.deadlineAt=Math.min(this.onboarding?Infinity:d.hardAt,oldDeadline||Infinity,this.now()+(reason==='player'?this.limits.playerClosingMs:this.limits.hostClosingMs));
    this.pendingHandled=[];d.readyAt=null;this.announcement=reason==='host'?announcement:'';this.announcementId=null;this.farewellId=null;this.announcementMetadata=utteranceMetadata('z',metadata,{allowLegacy:true});
    this.generating=false;for(const c of this.requests)c.abort();this.inputQueue=[];
    this.freeze();this.changed();
  }
  extend(sourceId,scope){
    const d=this.data;
    if(d.phase!=='closing'||d.closeReason!=='host'||d.extensionUsed!==0||!scope?.trim()||scope.length>160||this.now()>=d.deadlineAt||!this.barrierReady||this.now()>=d.hardAt-this.limits.hostClosingMs)return false;
    // Only a real player input already accepted into this session can extend it.
    if(!this.s.engine.session.evidence().some(e=>e.id===sourceId&&e.type==='player.message'))return false;
    d.extensionUsed=1;d.extensionActive=true;d.extensionScope=scope;d.extensionDeadline=Math.min(this.now()+this.limits.extensionMs,d.hardAt-this.limits.hostClosingMs);
    d.phase='live';d.closeReason=null;d.deadlineAt=null;d.readyAt=null;d.version++;
    this.s.cancelHost('extension_accepted');this.s.engine._continuations=[];
    this.s.engine._public('session.extended','已接受本场唯一一次挽留，追加范围：'+scope);
    this.s.engine.nextWake=this.s.engine._clock;this.changed();return true;
  }
  say(text,kind,metadata={}){
    const e=this.s.engine,id=String(e._speechId+1);
    if(kind==='farewell')this.farewellId=id;else this.announcementId=id;
    e._say(text,'warm','none',{...utteranceMetadata('z',metadata,{allowLegacy:true}),source:'session-control'});this.changed();
  }
  async farewell(){
    if(this.generating||this.disposed||this.data.phase!=='closing'||!this.barrierReady)return;
    this.generating=true;const version=this.data.version;let speech=this.s.scene?.context?null:FALLBACK,metadata={};
    try{
      const reply=await this.request('session',this.context('farewell'));
      const evidence=this.s.engine.session.evidence();
      const review=await this.request('review',{revision:this.s.revision,publicEvents:this.s.engine.facts.slice(-12),sessionEvents:evidence,waiting_messages:[],candidate:{id:randomUUID(),role:'host',speakerId:'Z',...utteranceMetadata('z',reply,{allowLegacy:true}),text:reply.speech,intent:'speak',replyTo:[],evidenceIds:[]}},4000);
      if(review.decision==='allow'){speech=reply.speech;metadata=utteranceMetadata('z',reply,{allowLegacy:true});}
    }catch{/* A fixed short goodbye is valid even when the provider is unavailable. */}
    if(this.disposed||version!==this.data.version||this.data.phase!=='closing')return;
    this.generating=false;if(!speech){this.end('farewell_unavailable');return;}this.data.farewellText=speech;this.data.phase='farewell';this.data.version++;
    this.say(speech,'farewell',metadata);
  }
  audioDisabled(){
    if(this.data.phase==='farewell'&&!this.textFallback){this.textFallback=true;this.say(this.data.farewellText||FALLBACK,'farewell');}
    else if(this.data.phase==='closing'&&this.announcementId){this.announcementId=null;this.data.readyAt=this.now()+this.limits.invitationMs;}
  }
  receipt(c,p){
    if(this.s.scene?.context?.mode==='first_evening'){
      if(c.type==='host.present' && p.status==='started')this.data.eveningStartClock??=this.s.engine._clock||0;
      if(c.type==='media.play' && p.status==='completed' && this.s.talent.current?.title?.includes('晚安'))this.data.eveningSongComplete=true;
    }
    if(c.commandId===this.barrier&&p.status==='completed'){
      this.barrierReady=true;
      if(this.announcement){const text=this.announcement;this.announcement='';this.say(text,'announcement',this.announcementMetadata);}
      else void this.farewell();
    }
    if(c.type==='host.present'){
      if(c.payload.speechId===this.announcementId&&terminal.has(p.status)){
        if(p.status==='completed'){const ids=new Set(this.pendingHandled);for(const id of ids)this.s.engine._setReceipt(id,'replied');this.s.engine.inbox=this.s.engine.inbox.filter(m=>!ids.has(m.id));}
        this.pendingHandled=[];this.data.readyAt=this.now()+this.limits.invitationMs;this.announcementId=null;
      }
      if(c.payload.speechId===this.farewellId){
        if(p.status==='completed')this.end('farewell_completed');
        else if(['failed','interrupted','skipped'].includes(p.status)&&!this.textFallback){
          this.textFallback=true;this.say(this.data.farewellText||FALLBACK,'farewell');
        }
      }
      if(this.normal&&p.status==='completed')this.opened();
    }
    this.changed();
  }
  tick(){
    const d=this.data;if(this.disposed||d.phase==='ended')return;
    if(!this.onboarding&&this.now()>=d.hardAt){this.end('session_hard_limit');return;}
    if(this.s.scene?.context?.mode==='first_evening' && this.normal && this.eveningElapsed()>=140000 && d.eveningSongComplete && !this.s.engine._active && !this.s.engine._mediaPlaying()){this.begin('host');return;}
    if(this.s.scene?.context?.mode==='first_evening' && this.normal && this.eveningElapsed()>=180000 && !['starting','playing'].includes(this.s.talent.current?.phase)){this.begin('host');return;}
    if(d.deadlineAt&&this.now()>=d.deadlineAt){this.end('closing_timeout');return;}
    if(d.extensionActive&&this.now()>=d.extensionDeadline){this.begin('host');return;}
    if(!this.onboarding&&this.normal&&!d.extensionActive&&this.now()>=d.targetAt){this.begin('host',this.s.scene?.context?'':'今晚的时间差不多了，我们把话题轻轻收住，准备和大家道个晚安。');return;}
    if(!this.onboarding&&this.normal&&this.now()>=d.hardAt-this.limits.hostClosingMs){this.begin('host');return;}
    if(this.normal&&!this.onboarding)this.advancePace();
    if(d.phase==='closing'&&this.barrierReady&&!this.announcementId&&d.readyAt&&this.now()>=d.readyAt&&!this.inspecting)void this.farewell();
  }
  end(reason){
    if(this.data.phase==='ended')return;
    this.data.phase='ended';this.data.version++;this.data.endReason=reason;this.data.endedAt=this.now();this.data.extensionActive=false;
    this.data.deadlineAt=null;this.dispose();
    this.s.control({protocolVersion:1,sessionId:this.s.id,epoch:this.s.epoch,action:'end'});
  }
  dispose(){this.disposed=true;for(const c of this.requests)c.abort();this.requests.clear();this.inputQueue=[];}
}
