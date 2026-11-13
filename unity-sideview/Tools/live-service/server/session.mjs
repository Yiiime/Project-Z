import {LiveScene,isSceneTool} from './live-scene.mjs';
import {CharacterBridge} from './character-bridge.mjs';
import {utteranceMetadata} from './shared/utterance.js';
import {BroadcastMemory} from './broadcast-memory.mjs';
import fs from 'node:fs';
import path from 'node:path';
import {EventEmitter} from 'node:events';
import {ConversationEngine} from './shared/ui/conversation-engine.js';
import {CallConversationEngine} from './shared/ui/call-engine.js';
import {RemoteSpeech,uid} from './providers.mjs';
import {RemoteTalent} from './remote-talent.mjs';
import {SessionControl} from './session-control.mjs';
const terminal=new Set(['completed','displayed','ready','failed','skipped','interrupted','cancelled']);
const copy=v=>structuredClone(v);
class NativeConversationEngine extends CallConversationEngine {
 _onHostSpeechCommitted(id,result){
   super._onHostSpeechCommitted(id,result);
   if(['arrival','first_evening'].includes(this.liveScene?.context?.mode)&&result.virtualAudience?.length){
     this.virtualReplies??=new Map();this.virtualReplies.set(String(id),result.virtualAudience);
   }
 }
 _public(type,text,metadata={}){
   this.broadcast?.record(type,text,metadata,this._state.media);
   const result=super._public(type,text,metadata);
   if(type==='speech.completed'||type==='speech.interrupted'){
     const key=String(metadata.speechId),lines=this.virtualReplies?.get(key)||[];
     this.virtualReplies?.delete(key);
     if(type==='speech.completed'){this.liveScene?.hostCompleted(key);for(const [index,line] of lines.entries())this.liveScene.session.command('virtual_audience.present',{name:line.name,text:line.text,messageId:uid(),delayMs:index*600});}
   }
   return result;
 }
 _hostContext(inbox,...args){
   if(this.liveScene?.context?.mode==='first_evening')inbox=[...inbox.filter(m=>m.mine),...inbox.filter(m=>!m.mine).slice(-3)];
   const context=super._hostContext(inbox,...args);if(this.sessionControl)context.host.sessionControl=this.sessionControl.snapshot();return context;
 }
 _wakeDelay(result){const delay=super._wakeDelay(result);return this.liveScene?.context?.mode==='first_evening'?Math.min(delay,2000):delay;}
 _recapTurn(){
   if(this.liveScene?.context)return false;
   return this.sessionControl?.normal && this.broadcast?.nextItem() && !this._state.call.active &&
     !this._hasPendingPlayerChat() && !this._mediaPlaying() && !this.singingExecuting;
 }
 _think(){
   if(this.liveScene?.pending)return;
   if(!this.sessionControl?.normal)return;
   if(!this._recapTurn())return super._think();
   if(this._state.paused||!this._started||this._active||this.pending||this._queue.length||this._destroyed)return;
   if((this.recapRetryAt||0)>this._clock)return;
   this._cancelPreparation('daytime_recap');
   this.broadcast.recapItem=this.broadcast.nextItem();
   this._say(this.broadcast.opening(this.broadcast.recapItem),'warm','wave',{source:'daytime_recap',priority:12,scope:'story'});
 }
 _maybePrepare(){if(this.liveScene?.pending)return;if(!this._recapTurn()&&(!this.sessionControl||this.sessionControl.normal))return super._maybePrepare();}
 _maybeAssessTopic(){if(!this.sessionControl||this.sessionControl.normal)return super._maybeAssessTopic();}
 _canPresentMedia(){return (!this.sessionControl||this.sessionControl.normal)&&super._canPresentMedia();}
 _pumpSpeech(){if(this.sessionControl&&!this.sessionControl.normal){if(!this._mediaPlaying())return ConversationEngine.prototype._pumpSpeech.call(this);}else return super._pumpSpeech();}
 _executeTool(tool){if(this.sessionControl&&!this.sessionControl.normal)return false;if(isSceneTool(tool?.name))return this.liveScene?.execute(tool)||false;return super._executeTool(tool);}

 _message(text,options={}){return super._message(text,{...options,...(options.mine&&this.liveScene?.context?{name:this.liveScene.context.playerName}:{}),...(options.mine&&this._state.call?.active?{type:'call'}:{})});}
}
export class LiveSession extends EventEmitter {
 constructor({id=uid(),owner='local-owner',root,providers,artifacts,songRoot,capabilities={},memory=null,saved=null,story=null,characterBinding=null}){
  super();Object.assign(this,{id,owner,root,providers,artifacts,songRoot});this.capabilities={audioEnabled:capabilities.audioEnabled===true,characterReady:capabilities.characterReady===true};
  this.epoch=saved?saved.epoch+1:1;this.lease=uid();this.leaseUntil=Date.now()+30000;this.phase='paused';this.seq=saved?.seq||0;this.revision=saved?.revision||0;this.events=saved?.events||[];this.commands=new Map(saved?.commands||[]);this.idempotency=new Map(saved?.idempotency||[]);this.artifactIds=new Set(saved?.artifactIds||[]);this.waiters=new Map();this.sent={};this.initializing=true;
  this.engine=new NativeConversationEngine();this.scene=new LiveScene(this,story,saved?.scene);this.engine.liveScene=this.scene;
  const binding=saved?.broadcast?.binding||memory;
  this.broadcast=binding?new BroadcastMemory(binding,saved?.broadcast):null;
  if(this.broadcast)this.broadcast.songTasks=()=>this.providers.singing?.daytimeTasks?.(this.owner,this.broadcast.binding) || [];
  if(this.broadcast)this.broadcast.clock=()=>this.engine._clock;
  this.engine.broadcast=this.broadcast;this.engine.setSpeechDriven(true);this.speech=new RemoteSpeech(this,providers);this.talent=new RemoteTalent(this,this.speech);this.engine.configureSpeech(this.speech);this.engine.configureTalent(this.talent);
  this.engine.addEventListener('speak',({detail})=>{if(!detail.resume){if(this.broadcast)this.broadcast.cueAtStart=this.broadcast.cueId;
    if(detail.source==="daytime_recap")this.broadcast.recapSpeech=String(detail.id);void this.presentHost(detail);}});
  this.engine.addEventListener('state',()=>this.changed());
  this.sessionControl=new SessionControl(this,saved?.sessionControl);this.engine.sessionControl=this.sessionControl;
  this.engine.commitSessionDecision=(result,context)=>this.sessionControl.decision(result,context);
  const canPublish=this.engine.audience.canPublish;this.engine.audience.canPublish=()=>this.sessionControl.normal&&!this.engine._recapTurn()&&canPublish();
  const characterScope=characterBinding||saved?.characterBinding;
  if(characterScope){
    this.characters=new CharacterBridge(characterScope,{pending:saved?.characterOutbox||[],publications:saved?.characterPublications||[],onChanged:()=>this.flush()});
    const committed=this.engine._onHostSpeechCommitted.bind(this.engine);
    this.engine._onHostSpeechCommitted=(id,result)=>{committed(id,result);this.characters.bindPublication('speech-'+id,result.characterProposalId,'z',result.speech);};
    const publish=this.engine._public.bind(this.engine);
    this.engine._public=(type,text,metadata={})=>{const before=this.broadcast?.last;publish(type,text,metadata);const event=this.engine.facts.find(e=>e.id===metadata.id)||this.engine.facts.at(-1);const receipt=this.broadcast?.last>before?this.broadcast.pending.at(-1):null;this.characters.record(event,receipt);};
  }
  this.installAudience();
  if(this.characters)this.engine.audience.setConfig({...this.engine.audience.config,singleActorRequests:true,generationDeadlineMs:15000,candidateTtlMs:20000});
  if(saved)this.restore(saved);
  this.initializing=false;this.flush();
 }
 async refreshCharacterMemory(){if(!this.characters||!this.broadcast)return;await this.characters.flush();const projection=await this.characters.post('projection');const {ok,...view}=projection;this.broadcast.refresh({...this.broadcast.binding,...view});}
 async initialize(){await this.refreshCharacterMemory();const config=await this.providers.config(this.id);this.engine.configureAgent({sentenceStreaming:true,generate:async(p,o)=>{
    if(p.kind==='host'&&this.sessionControl.eveningSongDue())return {action:'tool',actorId:'z',delivery:'轻声温柔地说，带着笑意。',expression:'neutral',speech:'给大家唱一首《晚安》，祝大家晚安。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:2000,continuation:'await_event',memory:'',agendaUpdates:[],tool:{name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}};
    if(p.kind==='audience'&&this.scene.context?.mode==='arrival')return {requestId:p.requestId,status:'skip',items:[],source:'runtime'};
    if(p.kind==='audience'&&this.scene.context?.mode==='first_evening'&&p.context.audience?.eligibleNpcIds)p.context.audience.eligibleNpcIds=p.context.audience.eligibleNpcIds.filter(id=>id!=='liyuan');
    const hostSide=p.kind==='host'||p.kind==='repair'||(p.kind==='review'&&p.context.candidate?.role==='host');
    if(hostSide)await this.refreshCharacterMemory();
    const cue=p.context?.host?.inbox?.findLast(m=>m.mine)?.text;
    const memory=hostSide?this.broadcast?.recall(cue):null;
    // First-night memories are already public world facts, including before their first retelling on stream.
    if(memory&&this.scene.context?.mode==='first_evening'){
      memory.today=[...this.broadcast.binding.today];
      memory.entries=[...new Map([...memory.entries,...this.broadcast.binding.entries.filter(e=>e.layer==='daily'&&e.speaker==='engine')].map(e=>[e.id,e])).values()];
    }
    const daytimeTasks=memory?.daySongs.length?this.broadcast.songTasks().filter(t=>
      this.broadcast.visibleSongs(cue).some(s=>s.sourceId===t.sourceId)):[];
    const reply=await this.providers.generate(this.id,p,{...o,character:this.characters,daytimeTasks,sceneContext:this.scene.binding(),broadcastContext:memory,songSession:memory?this.engine.session.id:null});
      const proposal=reply.z_state_update?.memories?.[0];
      if(p.kind==='host' && proposal && this.broadcast?.offer?.id===proposal.id &&
         this.broadcast.offer.step===proposal.step && this.broadcast.offer.source===this.broadcast.lastTopic?.id)
        this.broadcast.offer.quote=proposal.quote;
      return reply;
  },fetch:(route,options)=>this.providers.singingFetch(route,options),cancel(){}},config);
  if(this.scene.context?.mode==='first_evening'){this.engine.handoffEnabled=false;this.engine.audience.setConfig({handoff:false});}
  this.engine.setPaused(true);this.engine.start();this.phase='paused';if(this.sessionControl.data.phase==='ended'){this.phase='ended';this.sessionControl.dispose();}else if(!this.sessionControl.normal)this.sessionControl.end('server_restarted_during_close');this.sessionControl.tick();this.flush();return this;}
 restore(saved){const e=this.engine,x=saved.engine;Object.assign(e,{_state:x.state,_clock:x.clock,_speechId:x.speechId,_messageId:x.messageId,inbox:x.inbox,facts:x.facts,memory:x.memory});Object.assign(e.session,x.session);e._state.call=e._emptyCall();e._state.paused=true;e._state.currentSpeech=null;e._state.speaking=false;e._state.media=null;
  e.audience.events=copy(x.facts);for(const fact of x.facts)e.audience.dialogue.record(fact);
  // Retire unfinished activities from older sessions without replaying them.
  const retiredActivity=x.state.play?.active||x.media&&x.media.kind!=='singing';
  e._state.play={active:null,queue:[],tarot:null,drawing:null,history:[]};
  e._state.stage='free';e._state.stageLabel='和 Z 聊聊天';
  if(retiredActivity){e.memory='';e.session.items=[];}
  if(x.media?.kind==='singing'){this.talent.current={...x.media,phase:'interrupted',reason:'server_restarted',controller:new AbortController()};e._state.media=this.talent.snapshot();}
  for(const c of this.commands.values())if(!terminal.has(c.status)){c.status='interrupted';c.reason='server_restarted';}
  if(x.activeSpeech)e._public('speech.interrupted','服务端重启，上一句是否播完未确认。');
 }
 installAudience(){const a=this.engine.audience,original=a.publish;
  a.publish=item=>{if(this.scene.context?.mode==='arrival'||this.scene.context?.mode==='first_evening'&&item.npc_id==='liyuan')return Promise.reject(Error('audience_not_present'));const messageId=item.messageId||uid();if(this.characters)this.characters.bindPublication(messageId,item.characterProposalId,item.npc_id,item.text);const c=this.command('audience.present',{...utteranceMetadata(item.npc_id,item,{allowLegacy:true}),messageId,npcId:item.npc_id,text:item.text,recipientIds:item.recipientIds||[],replyTo:item.reply_to?[item.reply_to]:[],evidenceRefs:item.evidence_refs});return new Promise((resolve,reject)=>this.waiters.set(c.commandId,{resolve:()=>{if(!this.sessionControl.normal){reject(Error('session_closing'));return;}original({...item,messageId:c.payload.messageId});resolve();},reject}));};
  a._publishOne=async now=>{
   if(a.publishing)return;while(a.candidates.length){const n=a.candidates[0];if(n.token!==a.generation||now>=n.expiresAt||now-(a.cooldowns.get(n.item.npc_id)??-Infinity)<a.config.npcPublishCooldownMs||n.item.intent==='advance'&&!a._advanceAllowed()){a.candidates.shift();a._endSelection(n.work,'expired_or_ineligible');continue;}
    if(now<Math.max(n.notBefore,a.nextPublishAt))return;if(!a._allowed()){a._cancel();return;}a.candidates.shift();if(!a.canCommit()){a._endSelection(n.work,'host_priority');return;}
    a.publishing=true;try{let completion;const committed=a.dialogue.commit(n.ticket,()=>{completion=a.publish({...n.item,...(a.config.handoff?{messageId:n.work.messageId}:{}),evidence_refs:[...n.item.evidence_refs]});});if(!committed){a._endSelection(n.work,'stale_review');return;}await completion;a._endSelection(n.work,'published');a.displayed(n.item,a.now());}catch{a._endSelection(n.work,'publication_failed');}finally{a.publishing=false;}return;
   }
  };
 }
 persistData(){const e=this.engine;return {scene:this.scene.snapshot(),characterBinding:this.characters?.binding,characterOutbox:this.characters?.pending,characterPublications:this.characters?[...this.characters.publications]:[],broadcast:this.broadcast?.save(),sessionControl:this.sessionControl.data,id:this.id,owner:this.owner,epoch:this.epoch,seq:this.seq,revision:this.revision,capabilities:this.capabilities,phase:this.phase,commands:[...this.commands],events:this.events,idempotency:[...this.idempotency].slice(-2000),artifactIds:[...this.artifactIds],engine:{state:e._state,clock:e._clock,speechId:e._speechId,messageId:e._messageId,inbox:e.inbox,facts:e.facts,memory:e.memory,session:{id:e.session.id,events:e.session.events,items:e.session.items,recentInitiatives:e.session.recentInitiatives},media:this.talent.snapshot(),activeSpeech:e._active?{id:e._active.id}:null}};}
 flush(){if(this.initializing)return;fs.mkdirSync(this.root,{recursive:true,mode:0o700});const target=path.join(this.root,this.id+'.json');fs.writeFileSync(target+'.tmp',JSON.stringify(this.persistData()),{mode:0o600});fs.renameSync(target+'.tmp',target);}
 event(type,payload,extra={}){const event={protocolVersion:1,eventId:uid(),sessionId:this.id,epoch:this.epoch,serverSeq:++this.seq,type,payload,...extra};this.events.push(event);this.events=this.events.slice(-1000);this.flush();this.emit('event',event);return event;}
 command(type,payload,taskId=null,resourceRevision=null){const command={commandId:uid(),type,taskId,resourceRevision,payload,epoch:this.epoch,status:'sent',issuedAt:new Date().toISOString(),expiresAt:type==='story.present'&&payload.tool==='draw_guest'?null:new Date(Date.now()+120000).toISOString()};this.commands.set(command.commandId,command);if(this.commands.size>1200)for(const [id,c] of this.commands){if(terminal.has(c.status))this.commands.delete(id);if(this.commands.size<=1000)break;}const event=this.event(type,payload,{commandId:command.commandId,taskId,resourceRevision,issuedAt:command.issuedAt,expiresAt:command.expiresAt});Object.assign(command,{event});this.flush();return command;}
 snapshot(){const e=this.engine,p=e._state.play,t=p.tarot,d=p.drawing;return {story:this.scene.binding(),memoryLast:this.broadcast?.last,memoryEvents:this.broadcast?.pending,protocolVersion:1,sessionId:this.id,epoch:this.epoch,executorLeaseId:this.lease,lastServerSeq:this.seq,sessionRevision:this.revision,phase:this.phase,sessionControl:this.sessionControl.snapshot(),capabilities:this.capabilities,call:{...copy(e._state.call),story:null,version:e.callVersion,busy:!!(e._state.currentSpeech||e.pending||e.callNeedsTurn||e._queue.length||e._continuations.length||["preparing","ready","starting","playing","paused"].includes(e._state.media?.phase)),voiceAvailable:!!this.providers.env.VOLC_ASR_API_KEY,thinkingAudio:this.thinkingAudio||null},room:{...(this.characters?{participants:this.characters.binding.actors||["z","liyuan","liting","axing","zhengyi","xiaoxiaoqing","linxu","zhou_bobo"]}:{}),viewers:e._state.viewers,stageLabel:e._state.stageLabel,replyStatus:e._state.replyStatus,messages:copy(e._state.messages.filter(m=>m.type!=='call'&&!(m.mine&&m.name==='连麦来客'))),voiceAvailable:this.speech.available,voiceError:this.providers.voiceError||null},performance:{waiting:e._state.agent.source!=='unavailable'&&['pending','queued'].includes(e._state.replyStatus)},agent:{enabled:e.enabled,status:e._state.agent.source,errorCode:e._state.agent.errorCode},publicEvents:copy(e.facts),activeTask:p.active?{kind:p.active,taskId:p[p.active].id}:this.talent.snapshot(),voiceOwner:e._active?{speechId:String(e._active.id)}:this.talent.snapshot(),panels:{tarot:!!t?.open,drawing:!!d?.open},tarot:t?{id:t.id,revision:0,origin:t.origin,owner:t.owner,spread:t.spread,paused:t.paused,activeIndex:t.activeIndex,phase:t.phase,question:t.question,round:t.round,slots:copy(t.slots),candidates:copy(t.candidates),cards:['revealing','interpreting','completed'].includes(t.phase)?copy(t.cards):[]}:null,recentArtwork:d?{id:d.id,revision:d.revision,title:d.title,phase:d.phase,stage:d.stage,stageCount:d.stageCount,stages:copy(d.stages),owner:d.owner,reason:d.reason,artifactId:this.sent.drawingArtifact||null}:null,pendingCommands:[...this.commands.values()].filter(c=>c.epoch===this.epoch&&!terminal.has(c.status)).map(c=>c.event).filter(Boolean),recentCommandOutcomes:[...this.commands.values()].filter(c=>terminal.has(c.status)).slice(-30).map(c=>({commandId:c.commandId,status:c.status}))};}
 changed(){if(this.initializing||this.closed)return;this.revision++;if(this.phase==='live')this.syncPresentation();this.flush();if(!this.stateTimer)this.stateTimer=setTimeout(()=>{this.stateTimer=null;if(!this.closed)this.event('session.state',this.snapshot());},50);}
 syncPresentation(){}
 async prepareThinking(){if(this.thinkingAudio||this.thinkingPending||!this.speech.enabled||!this.speech.available)return;this.thinkingPending=true;try{this.thinkingAudio=await this.speech.prepareAudio('我想想啊…','thinking',{});if(!this.closed)this.changed();}catch{}finally{this.thinkingPending=false;}}

 async presentHost(detail){const controller=new AbortController();this.hostController?.abort();this.hostController=controller;const epoch=this.epoch;try{
  const replyTo=this.engine.speechLinks.get(detail.id)||[],recipientIds=[...new Set(replyTo.map(id=>this.engine.session.events.find(e=>e.id===id)?.speakerId).filter(Boolean))];
  let audio=null,reason=null;const closing=!this.sessionControl.normal;
  if((this.engine._state.call.active||this.scene.context?.mode==='first_evening')&&this.speech.enabled&&this.speech.available&&!closing){
   const stream=this.speech.claimStream(detail.preparedAudioKey,detail.text,detail.mood,controller.signal,detail.delivery);
   this.command('host.present',{...utteranceMetadata('z',detail,{allowLegacy:true}),replyTo,recipientIds,speechId:String(detail.id),text:detail.text,mood:detail.mood,gesture:detail.gesture,transition:detail.transition,modality:'pcm-stream',audioStreamId:stream.id,words:[]});return;
  }
  if(this.speech.enabled&&this.speech.available&&!this.sessionControl.textFallback){const audioController=new AbortController();const abort=()=>audioController.abort();controller.signal.addEventListener('abort',abort,{once:true});let timer;
   try{audio=await Promise.race([this.speech.claim(detail.preparedAudioKey,detail.text,detail.mood,audioController.signal,detail.delivery),new Promise((_,reject)=>{timer=setTimeout(()=>{audioController.abort();reject(Error('tts_unavailable'));},closing?5000:30000);})]);}
   catch(error){reason=/^tts_[a-z_]+$/.test(error.message)?error.message:'tts_unavailable';}finally{clearTimeout(timer);controller.signal.removeEventListener('abort',abort);}}

  if(controller.signal.aborted||this.closed||this.epoch!==epoch||this.engine._active?.id!==detail.id)return;
  this.command('host.present',{...utteranceMetadata('z',detail,{allowLegacy:true}),replyTo,recipientIds,speechId:String(detail.id),text:detail.text,mood:detail.mood,gesture:detail.gesture,transition:detail.transition,gestureHoldMs:detail.gestureHoldMs||0,carryArms:!!detail.carryArms,modality:audio?'audio':'text',audioArtifactId:audio?.artifactId||null,sha256:audio?.sha256||null,words:audio?.words||[],fallbackReason:reason});
 }catch{this.interruptSpeech('prepare_failed');}}
 interruptSpeech(reason){const e=this.engine;if(!e._active)return;const speech=e._active;if(speech.source==='daytime_recap')e.recapRetryAt=e._clock+4000;e.epoch++;e.pending?.abort();e.pending=null;e._cancelPreparation(reason);e._cancelTopic(reason);e._active=null;e._queue=[];e._continuations=[];e.speechPlan=null;e._state.currentSpeech=null;e._state.speaking=false;this.hostController?.abort();e._public('speech.interrupted',reason,{speechId:String(speech.id)});e.nextWake=e._clock+1000;e._emitState();}
 cancelPresentation(kind,taskId){for(const c of this.commands.values())if(c.type===kind+'.present'&&c.taskId===taskId&&!terminal.has(c.status)){c.status='cancelled';this.command('execution.cancel',{targetCommandId:c.commandId,reason:'panel_hidden'},taskId,c.resourceRevision);}}
 cancelTask(taskId,reason){for(const c of [...this.commands.values()])if(c.taskId===taskId&&!terminal.has(c.status)){c.status='cancelled';this.waiters.get(c.commandId)?.reject(Error(reason));this.waiters.delete(c.commandId);this.command('execution.cancel',{targetCommandId:c.commandId,playbackId:c.payload.playbackId,reason},taskId,c.resourceRevision);}}
 check(body,lease=false){if(body.protocolVersion!==1)throw Error('unsupported_protocol');if(body.sessionId!==this.id||body.epoch!==this.epoch)throw Error('stale_epoch');if(lease&&(body.executorLeaseId!==this.lease||Date.now()>this.leaseUntil))throw Error('executor_lease_expired');}
 input(body){this.check(body);if(body.type==='story.accept_arrival'){if(!this.scene.accept())throw Error('state_conflict');this.changed();return {accepted:true};}if(this.phase==='ended')throw Error('session_paused');const p=body.payload||{};let ok=false;
  if(body.type==='viewer.message'&&!this.sessionControl.normal){
   if(this.sessionControl.data.phase!=='closing'||this.sessionControl.data.closeReason!=='host')throw Error('session_closing');
   if(typeof p.text!=='string'||!p.text.trim()||p.text.length>120)throw Error('invalid_input');
   const m=this.engine._message(p.text.trim(),{mine:true,name:'你'});this.engine._public('player.message',m.text,{id:m.id,speakerRole:'player',speakerId:'player'});this.sessionControl.enqueue({id:m.id,text:m.text});this.changed();return {accepted:true};
  }
  if(!this.sessionControl.normal&&body.type!=='client.capabilities_changed')throw Error('session_closing');
  if(this.phase!=='live')throw Error('session_paused');
  if(body.type==='call.join'){ok=this.engine.joinCall();if(ok)void this.prepareThinking();}
  else if(body.type==='call.end'){ok=this.engine.endCall();}
  else if(body.type==='call.input'){ok=this.engine.setCallInput(p.state);}
  else if(body.type==='call.audience'){ok=this.engine.inviteAudience();}
  else if(body.type==='viewer.message'){if(typeof p.text!=='string'||!p.text.trim()||p.text.length>(this.engine._state.call.active?360:120))throw Error('invalid_input');ok=this.engine.sendMessage(p.text,{recipientIds:p.recipientIds,replyToMessageId:p.replyToMessageId});if(ok){this.sessionControl.opened();const m=this.engine.session.evidence().filter(e=>e.type==='player.message').at(-1);if(m)this.sessionControl.enqueue({id:m.id,text:m.text});}}
  else if(body.type==='client.capabilities_changed'){const wasEnabled=this.capabilities.audioEnabled||this.speech.enabled;this.capabilities.audioEnabled=p.audioEnabled===true;this.speech.enabled=this.capabilities.audioEnabled;this.speech.volume=this.speech.enabled?1:0;if(!this.speech.enabled){if(wasEnabled||!this.sessionControl.normal){this.talent.cancel('audio_disabled');this.cancelHost('audio_disabled');}this.sessionControl.audioDisabled();}ok=true;}
  if(!ok)throw Error('state_conflict');this.flush();return {accepted:true};
 }
 fadeHost(ms){const speechId=String(this.engine._active?.id||'');if(!speechId)return Promise.resolve();const c=this.command('host.fade',{speechId,durationMs:Math.min(200,Math.max(0,ms))});return new Promise(resolve=>{const timer=setTimeout(()=>{this.waiters.delete(c.commandId);resolve();},1200);this.waiters.set(c.commandId,{resolve:()=>{clearTimeout(timer);resolve();},reject:()=>{clearTimeout(timer);resolve();}});});}
 cancelHost(reason){for(const c of [...this.commands.values()])if(c.type==='host.present'&&!terminal.has(c.status)){c.status='interrupted';this.command('execution.cancel',{targetCommandId:c.commandId,reason});}this.interruptSpeech(reason);}
 control(body){this.check(body);const action=body.action;
  if(action==='stop_session'){this.sessionControl.stop();return {accepted:true};}
  if(action!=='end'&&!this.sessionControl.normal){if(action==='resume'||action==='pause')return {accepted:true};throw Error('session_closing');}

  if(action==='resume'){if(this.phase==='ended')throw Error('session_ended');this.leaseUntil=Date.now()+30000;this.phase='live';this.engine.setPaused(false);if(this.engine._recapTurn())this.engine._think();this.command('media.resume',{});this.syncPresentation();}
  else if(action==='retry_reply'){if(!this.engine.retryReply())throw Error('reply_retry_unavailable');}
  else if(action==='pause'){if(this.engine._state.call.active)this.engine.endCall();this.phase='paused';this.engine.setPaused(true);this.command('media.pause',{});}
  else if(action==='stop_activity'){const t=this.engine._state.play[this.engine._state.play.active]||this.talent.current;if(t&&body.taskId!==t.id)throw Error('state_conflict');if(t&&(body.resourceRevision??0)!==(t.revision||0))throw Error('stale_revision');this.cancelHost('viewer_stop');if(t){this.cancelTask(t.id,'viewer_stop');if(this.talent.current)this.talent.finish(true);}}
  else if(action==='end'){if(this.phase==='ended')return {accepted:true};this.sessionControl.data.phase='ended';this.sessionControl.dispose();if(this.engine._state.call.active)this.engine.endCall();this.phase='ended';this.cancelHost('session_ended');if(this.talent.current)this.talent.cancel('session_ended');for(const c of [...this.commands.values()])if(!terminal.has(c.status)&&!c.type.startsWith('execution.')){c.status='cancelled';this.waiters.get(c.commandId)?.reject(Error('ended'));this.waiters.delete(c.commandId);}this.engine.setPaused(true);this.command('execution.cancel',{all:true,reason:'session_ended'});this.command('session.ended',{});}
  else throw Error('invalid_control');this.changed();return {accepted:true};
 }
 receipt(body){this.check(body,true);const c=this.commands.get(body.commandId),p=body.payload||{};if(!c||c.epoch!==this.epoch||body.taskId!==c.taskId||body.resourceRevision!==c.resourceRevision)throw Error('state_conflict');
  if(terminal.has(c.status)){if(c.lastReceipt&&JSON.stringify(c.lastReceipt)===JSON.stringify(p))return {accepted:true,duplicate:true};throw Error('state_conflict');}
  for(const k of ['artifactId','playbackId','speechId','stage','round','view'])if(c.payload[k]!==undefined&&p[k]!==c.payload[k]&& !['received','failed','skipped','interrupted'].includes(p.status))throw Error('invalid_evidence');
  if(p.status==='received'){c.received=true;this.flush();return {accepted:true};}
  if(this.phase!=='live'&&!['interrupted','failed','skipped','completed'].includes(p.status))throw Error('session_paused');
  const allowed={'story.present':['displayed'],'virtual_audience.present':['displayed'],'host.fade':['completed'],'host.present':['started','progress','completed'],'audience.present':['displayed'],'media.prepare':['ready'],'media.play':['started','progress','completed'],'media.pause':['completed'],'media.resume':['completed'],'execution.cancel':['completed'],'activity.release':['completed'],'session.ended':['completed']};
  if(!(allowed[c.type]||[]).includes(p.status)&&!['failed','skipped','interrupted'].includes(p.status))throw Error('invalid_transition');
  if(p.status==='started'&&c.status!=='sent'||p.status==='progress'&&c.status!=='started'||p.status==='completed'&&['host.present','media.play'].includes(c.type)&&c.status!=='started')throw Error('invalid_transition');
  if(['started','progress','completed'].includes(p.status)&&['host.present','media.play'].includes(c.type)){
   if(!Number.isFinite(p.positionMs)||p.positionMs<0||p.positionMs<(c.positionMs||0)||p.positionMs>180000)throw Error('invalid_position');
   if(c.type==='host.present'&&p.modality!==c.payload.modality)throw Error('invalid_modality');
   if(p.status==='started'&&(!Number.isFinite(p.durationMs)||p.durationMs<(c.payload.modality==='pcm-stream'?0:1)||p.durationMs>180000))throw Error('invalid_duration');
   if(p.status==='completed'&&p.positionMs<(c.durationMs||0)-250)throw Error('not_finished');
  }
  if(c.payload.modality==='pcm-stream'&&['progress','completed'].includes(p.status)){const stream=this.speech.streams.get(c.payload.audioStreamId);if(!stream||p.positionMs>stream.bytes/48+5)throw Error('invalid_position');if(p.status==='completed'&&(!stream.done||stream.error||p.positionMs<stream.durationMs-5))throw Error('not_finished');}
  const failed=['failed','skipped','interrupted'].includes(p.status);
  if(c.type==='story.present'){this.scene.receipt(c,p);}
  else if(c.type==='virtual_audience.present'){if(!failed){this.scene.audienceDisplayed();const message=this.engine._message(c.payload.text,{id:c.payload.messageId,name:c.payload.name,type:'chat'});if(this.scene.context?.mode!=='first_evening'){this.engine._cancelTopic('virtual_audience_displayed');this.engine._cancelPreparation('virtual_audience_displayed');}this.engine._receive(message,false,'virtual_'+c.payload.messageId,{recipientIds:['z']});}}
  else if(c.type==='host.present'){
   // Start the prepared animation as the reviewed lottery announcement starts speaking.
   if(p.status==='started' && this.engine.speechPlan?.result?.tool?.name==='draw_guest' && this.scene.allowed().includes('draw_guest'))this.scene.execute(this.engine.speechPlan.result.tool);
   if(failed)this.interruptSpeech(p.reason||p.status);
   else if(p.status==='progress'&&typeof p.text==='string'){if(!c.payload.text.startsWith(p.text)||p.text.length<(c.textLength||0))throw Error('invalid_progress');this.engine.recordSpeechProgress(Number(p.speechId),p.text);c.textLength=p.text.length;}
   else if(p.status==='completed'){if(!this.engine.completeSpeech(Number(p.speechId)))throw Error('state_conflict');}
  }else if(c.type==='host.fade'){const waiter=this.waiters.get(c.commandId);waiter?.resolve();this.waiters.delete(c.commandId);}
  else if(c.type==='audience.present'){
   const waiter=this.waiters.get(c.commandId);if(!waiter)throw Error('state_conflict');if(failed)waiter.reject(Error(p.status));else waiter.resolve();this.waiters.delete(c.commandId);
  }  else if(['media.prepare','media.play'].includes(c.type))this.talent.receipt(c,p);
  else if(c.type==='panel.set'&&!failed&&p.visible!==c.payload.visible)throw Error('invalid_evidence');
  if(c.payload.audioStreamId&&terminal.has(p.status)){this.speech.streams.get(c.payload.audioStreamId)?.close();this.speech.streams.delete(c.payload.audioStreamId);}
  c.status=p.status==='progress'?'started':p.status;c.positionMs=p.positionMs??c.positionMs;c.durationMs=p.durationMs??c.durationMs;c.lastReceipt=copy(p);this.sessionControl.receipt(c,p);this.changed();return {accepted:true};
 }
 heartbeat(body){this.check(body,true);if(this.broadcast&&body.memory){
    this.broadcast.refresh(this.characters?{...body.memory,entries:this.broadcast.binding.entries,stories:this.broadcast.binding.stories}:body.memory);
    this.broadcast.acknowledge(body.memoryAck);
  }this.leaseUntil=Date.now()+30000;return {epoch:this.epoch,serverTime:new Date().toISOString(),leaseUntil:this.leaseUntil};}
 maintenance(){if(this.closed)return;this.sessionControl.tick();if(this.sessionControl.normal&&this.phase==='live'&&Date.now()>this.leaseUntil){this.phase='paused';this.engine.setPaused(true);this.cancelHost('executor_disconnected');this.talent.cancel('executor_disconnected');for(const c of [...this.commands.values()])if(c.type==='audience.present'&&!terminal.has(c.status)){c.status='interrupted';this.waiters.get(c.commandId)?.reject(Error('disconnected'));this.waiters.delete(c.commandId);}this.event('session.state',this.snapshot());}for(const c of [...this.commands.values()])if(c.status==='sent'&&Date.parse(c.expiresAt)<Date.now()){c.status='interrupted';this.waiters.get(c.commandId)?.reject(Error('expired'));this.waiters.delete(c.commandId);if(c.type==='host.present')this.interruptSpeech('execution_timeout');if(c.type.startsWith('media.')&&c.taskId===this.talent.current?.id)this.talent.cancel('execution_timeout');this.flush();}}
 close(){if(this.closed)return;this.flush();this.closed=true;this.sessionControl.dispose();clearTimeout(this.stateTimer);this.hostController?.abort();for(const w of this.waiters.values())w.reject(Error('closed'));this.waiters.clear();this.engine.destroy();}
}
