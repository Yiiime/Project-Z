import {randomUUID} from 'node:crypto';

const FALLBACK='那今天就先聊到这里。谢谢大家陪我，大家晚安。';
const terminal=new Set(['completed','displayed','ready','failed','skipped','interrupted','cancelled']);
export const SESSION_LIMITS={playerClosingMs:30000,hostClosingMs:45000,extensionMs:120000,invitationMs:6000};

// Transport live/paused is separate from the conversation's five phases.
export class SessionControl {
  constructor(session,saved=null,{now=Date.now,limits={}}={}) {
    this.s=session;this.now=now;this.limits={...SESSION_LIMITS,...limits};
    this.data=saved?structuredClone(saved):{phase:'opening',version:0,startedAt:now(),closeReason:null,extensionUsed:0,extensionActive:false,extensionScope:'',deadlineAt:null,extensionDeadline:null,readyAt:null};
    this.inputQueue=[];this.requests=new Set();this.disposed=false;
  }
  get normal(){return ['opening','live'].includes(this.data.phase);}
  snapshot(){const d=this.data;return {phase:d.phase,version:d.version,closeReason:d.closeReason,extensionUsed:d.extensionUsed,extensionActive:d.extensionActive,extensionScope:d.extensionScope,remainingMs:d.deadlineAt||d.extensionActive?Math.max(0,(d.deadlineAt||d.extensionDeadline)-this.now()):null,elapsedMs:Math.max(0,(d.endedAt||this.now())-d.startedAt)};}
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
    if(!this.normal)return true;
    if(context.host.sessionControl?.version!==this.data.version)return true;
    const player=context.host.inbox.find(m=>m.mine&&m.id===d.sourceMessageId);
    if(d.intent==='stop'){if(player)this.stop();return true;}
    if(d.intent==='close'){
      const e=this.s.engine;
      if(this.data.phase!=='live'||e._state.media||e.inbox.some(m=>m.mine&&!result.handledIds.includes(m.id)))return true;
      // New inputs invalidate the engine epoch before this commit hook.
      this.begin('host',result.speech);this.pendingHandled=[...result.handledIds];return true;
    }
    if(d.intent==='finish_extension'&&this.data.extensionActive){this.begin('host',result.speech);return true;}
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
  begin(reason,announcement=''){
    const d=this.data,oldDeadline=d.deadlineAt;
    d.phase='closing';d.closeReason=reason;d.version++;d.extensionActive=false;
    d.deadlineAt=Math.min(oldDeadline||Infinity,this.now()+(reason==='player'?this.limits.playerClosingMs:this.limits.hostClosingMs));
    this.pendingHandled=[];d.readyAt=null;this.announcement=reason==='host'?announcement:'';this.announcementId=null;this.farewellId=null;
    this.generating=false;for(const c of this.requests)c.abort();this.inputQueue=[];
    this.freeze();this.changed();
  }
  extend(sourceId,scope){
    const d=this.data;
    if(d.phase!=='closing'||d.closeReason!=='host'||d.extensionUsed!==0||!scope?.trim()||scope.length>160||this.now()>=d.deadlineAt||!this.barrierReady)return false;
    // Only a real player input already accepted into this session can extend it.
    if(!this.s.engine.session.evidence().some(e=>e.id===sourceId&&e.type==='player.message'))return false;
    d.extensionUsed=1;d.extensionActive=true;d.extensionScope=scope;d.extensionDeadline=this.now()+this.limits.extensionMs;
    d.phase='live';d.closeReason=null;d.deadlineAt=null;d.readyAt=null;d.version++;
    this.s.cancelHost('extension_accepted');this.s.engine._continuations=[];
    this.s.engine._public('session.extended','已接受本场唯一一次挽留，追加范围：'+scope);
    this.s.engine.nextWake=this.s.engine._clock;this.changed();return true;
  }
  say(text,kind){
    const e=this.s.engine,id=String(e._speechId+1);
    if(kind==='farewell')this.farewellId=id;else this.announcementId=id;
    e._say(text,'warm','none',{source:'session-control'});this.changed();
  }
  async farewell(){
    if(this.generating||this.disposed||this.data.phase!=='closing'||!this.barrierReady)return;
    this.generating=true;const version=this.data.version;let speech=FALLBACK;
    try{
      const reply=await this.request('session',this.context('farewell'));
      const evidence=this.s.engine.session.evidence();
      const review=await this.request('review',{revision:this.s.revision,publicEvents:this.s.engine.facts.slice(-12),sessionEvents:evidence,waiting_messages:[],candidate:{id:randomUUID(),role:'host',speakerId:'Z',text:reply.speech,intent:'speak',replyTo:[],evidenceIds:[]}},4000);
      if(review.decision==='allow')speech=reply.speech;
    }catch{/* A fixed short goodbye is valid even when the provider is unavailable. */}
    if(this.disposed||version!==this.data.version||this.data.phase!=='closing')return;
    this.generating=false;this.data.phase='farewell';this.data.version++;
    this.say(speech,'farewell');
  }
  audioDisabled(){
    if(this.data.phase==='farewell'&&!this.textFallback){this.textFallback=true;this.say(FALLBACK,'farewell');}
    else if(this.data.phase==='closing'&&this.announcementId){this.announcementId=null;this.data.readyAt=this.now()+this.limits.invitationMs;}
  }
  receipt(c,p){
    if(c.commandId===this.barrier&&p.status==='completed'){
      this.barrierReady=true;
      if(this.announcement){const text=this.announcement;this.announcement='';this.say(text,'announcement');}
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
          this.textFallback=true;this.say(FALLBACK,'farewell');
        }
      }
      if(this.normal&&p.status==='completed')this.opened();
    }
    this.changed();
  }
  tick(){
    const d=this.data;if(this.disposed||d.phase==='ended')return;
    if(d.deadlineAt&&this.now()>=d.deadlineAt){this.end('closing_timeout');return;}
    if(d.extensionActive&&this.now()>=d.extensionDeadline){this.begin('host');return;}
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
