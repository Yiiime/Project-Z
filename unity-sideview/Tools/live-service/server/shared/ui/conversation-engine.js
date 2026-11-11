import {SessionContinuity,validateAgendaUpdates,validateTopicProgress} from './session-continuity.js';
import {LiveEngine} from './live-engine.js';
import {AUDIENCE_NAMES} from '../audience-roster.js';
import {AudienceDirector} from './audience-director.js';
import {TALENT_MOODS,TALENT_GESTURES} from './talent-config.js';
const id=()=>crypto.randomUUID();
// The model owns conversational decisions. This runtime executes reviewed speech and tool decisions.
export class ConversationEngine extends LiveEngine {
  constructor(){
    super();this.client=null;this.enabled=false;this.epoch=0;this.inbox=[];this.facts=[];this.memory='';this.nextWake=0;this.pending=null;
    this.session=new SessionContinuity();this.preparing=null;this.prepared=null;this.preparationVersion=0;this.speechPlan=null;this.playbackLifecycle=0;this.speechAudio=null;this.failureStreak=0;this.topicEnabled=false;this.topicWork=null;this.topicProgress=null;this.topicVersion=0;this.handoffEnabled=false;this.audiencePreview=null;this.inboxVersion=0;
    this._state.play={active:null,queue:[],tarot:null,drawing:null,history:[]};
    this._state.agent={enabled:false,source:'local',capabilities:[]};
    this.talent=null;this._state.media=null;
    this.recentExpressions=[]; this.speechLinks=new Map();
    this.addEventListener('speak',({detail})=>{
      if(detail.resume)return;
      this._cancelTopic('new_speech');
      this.audience?.dialogue.invalidate(this._state.call?.active||this.audience?.config.phases?'host':null);
      if(detail.source!=='deepseek')return;
      if(this.handoffEnabled)this.audience.setHostPreview({speechId:detail.id,text:detail.text});
      // These are dispatched speech cues, not proof that a model was rendered.
      this.recentExpressions.push({mood:detail.mood,gesture:detail.gesture,transition:detail.transition,speech:detail.text.slice(0,80)});
      this.recentExpressions=this.recentExpressions.slice(-6);
    });
    this._state.stage='free';this._state.stageLabel='和 Z 聊聊天';
    this.audience=new AudienceDirector({now:()=>this._clock, phase:()=>this._state.media?.kind==='singing'&&this._state.media.phase==='playing'?'performing':this._state.call?.active?'call':this._clock<15000?'opening':'chat',request:(p,o)=>this.client.generate(p.kind==='audience'?{...p,context:{...p.context,experience:'autonomous'}}:p,o),
      canPublish:()=>this.enabled&&this._started&&!this._destroyed&&!this._state.paused,
      canCommit:()=>!!this.audience.config.phases||!this.pending,
      canPreview:()=>this.handoffEnabled&&!this.pending&&!this.audiencePreview&&!this._unhandledInbox().some(m=>m.mine)&&!this._mediaPlaying()&&(!this._active||this.speechPlan?.result.action==='speak'&&this.speechPlan.result.continuation!=='await_event'),
      onSelected:preview=>this._selectAudiencePreview(preview),
      onSelectionEnd:entry=>this._endAudiencePreview(entry),
      canAdvance:()=>!this.inbox.some(m=>m.mine),publish:item=>{
        const m=this._message(item.text,{id:item.messageId,name:AUDIENCE_NAMES[item.npc_id],type:'chat',source:'agent-audience'});
        if(this.audience.config.phases&&!this._state.call?.active&&(item.intent!=='advance'||this._clock<(this.nextAudienceTurn||0)||this._hasPendingPlayerChat()||this._mediaPlaying())){
          this._public('npc.message',m.text,{id:m.id,speakerRole:'audience',speakerId:item.npc_id});this._emitState();
        }else{if(this.audience.config.phases)this.nextAudienceTurn=this._clock+20000;this._receive(m,false,item.npc_id);}}});
  }
  start(){if(this._started||this._destroyed)return;this._started=true;this._lastTick=performance.now();this._timer=setInterval(()=>this._tick(),160);
    this._public('session.opened','Z 的直播已开始，现在是自由对话，没有预设节目。');this._emitState();}
  configureAgent(client,config={}){this.client=client;this.singingEnabled=config.singingBackground===true;clearInterval(this.singingPoll);if(this.singingEnabled){void this._pollSinging();this.singingPoll=setInterval(()=>void this._pollSinging(),5000);}this.enabled=!!config.enabled;this.topicEnabled=config.topic?.enabled===true;this.handoffEnabled=config.policy?.audience?.handoff===true;this.audience.setConfig(config.policy?.audience||{});this.audience.dialogue.configure(config.review);this.audience.dialogue.setConcurrent(!!this.audience.config.phases);
    Object.assign(this._state.agent,{enabled:this.enabled,errorCode:null,source:this.enabled?'ready':'local',capabilities:config.capabilities||[]});
    this._state.textMode=this.enabled?'provider':'local';this._state.textStatus=this.enabled?'ready':'local';this.nextWake=this._clock;this._emitState();}
  configureSpeech(director){this.speechAudio=director;director.addEventListener('preparation',({detail})=>this.audience.dialogue.log({stage:'host.audio.prepare',...detail}));}
  _warmCandidate(requestId,result){if(['speak','tool'].includes(result?.action)&&typeof result.speech==='string'&&result.speech.length<=360&&TALENT_MOODS.includes(result.mood))return this.speechAudio?.warmSpeech(requestId,result.speech,result.mood);return null;}
  _unhandledInbox(){const handled=new Set(this._active&&this.speechPlan?.speechId===this._active.id?this.speechPlan.result.handledIds:[]);return this.inbox.filter(m=>!handled.has(m.id)).map(m=>({...m}));}
  _selectAudiencePreview(preview){
    this._cancelPreparation('audience_selected');
    this.audiencePreview={...preview};this.nextWake=this._clock;this._maybePrepare();
  }
  _endAudiencePreview({id,reason}){
    if(this.audiencePreview?.id!==id||reason==='published')return;
    this.audiencePreview=null;this._cancelPreparation('audience_'+reason);this.nextWake=this._clock;
  }
  _topicCanMove(){return this.topicProgress?.anchorSpeechId===String(this.speechPlan?.speechId)&&['wrapping','complete'].includes(this.topicProgress.status);}
  _wakeDelay(result){if(result===this.speechPlan?.result&&this.topicProgress?.anchorSpeechId===String(this.speechPlan.speechId)&&this.topicProgress.status==='waiting')return result.wakeAfterMs;return result.action!=='wait'&&(result.continuation==='continue'||result===this.speechPlan?.result&&result.action==='speak'&&result.continuation!=='await_event'&&this._topicCanMove())?Math.min(350,result.wakeAfterMs):result.wakeAfterMs;}
  _cancelTopic(reason){this.topicVersion++;this.topicWork?.controller.abort();this.topicWork=null;this.topicProgress=null;}
  _maybeAssessTopic(){
    const speech=this._active,plan=this.speechPlan;
    if(!this.topicEnabled||!this.enabled||!this._started||this._destroyed||this._state.paused||this._mediaPlaying()||!speech||!plan||plan.speechId!==speech.id||plan.result.action!=='speak'||plan.result.continuation==='await_event'||this.topicWork||plan.topicAttemptedVersion===this.topicVersion)return;
    plan.topicAttemptedVersion=this.topicVersion;
    const controller=new AbortController(),version=this.topicVersion,requestId=id(),handled=new Set(plan.result.handledIds);
    const context={publicEvents:this.facts.slice(-12),sessionEvents:this.session.evidence(),inbox:this.inbox.filter(m=>!handled.has(m.id)).map(m=>({...m})),inProgress:{speechId:String(speech.id),text:speech.text}};
    const work={controller,version};this.topicWork=work;
    this.audience.dialogue.log({stage:'topic.started',requestId,anchorSpeechId:speech.id});
    void this.client.generate({requestId,kind:'topic',eventId:requestId,message:'判断当前这段交流在这句说完后是否自然收束，并提出可选的后续方向。',context},{signal:controller.signal}).then(result=>{
      if(controller.signal.aborted||version!==this.topicVersion||this._destroyed||this._state.paused||this.speechPlan!==plan)return;
      const {status,reason,nextDirection,evidenceIds,anchorSpeechId}=result;
      const judgment=validateTopicProgress({status,reason,nextDirection,evidenceIds,anchorSpeechId},[...context.publicEvents,...context.sessionEvents]);
      if(anchorSpeechId!==String(speech.id))return;
      this.topicProgress=judgment;
      this.audience.dialogue.log({stage:'topic.ready',requestId,...judgment});
      // Planning signals are private. They never complete tasks or publish speech.
      if(judgment.status==='waiting'&&plan.completedAt!==undefined&&!this._active&&!this.pending)this.nextWake=Math.max(this.nextWake,plan.completedAt+plan.result.wakeAfterMs);
      if(!this.audiencePreview&&this._topicCanMove()&&!this.inbox.some(m=>m.mine&&!handled.has(m.id))){
        this._cancelPreparation('topic_transition_ready');
        if(!this._active&&!this.pending)this.nextWake=Math.min(this.nextWake,this._clock+350);
        this._maybePrepare();
      }
    }).catch(()=>{if(!controller.signal.aborted)this.audience.dialogue.log({stage:'topic.unavailable',requestId});})
      .finally(()=>{if(this.topicWork===work)this.topicWork=null;if(!this._destroyed)this._emitState();});
  }
  configureTalent(talent){
    this.talent=talent;
    talent.addEventListener('state',({detail})=>{
      const old=this._state.media;this._state.media=detail;
      if(detail&&old?.phase!==detail.phase){
        if(['completed','failed','interrupted'].includes(detail.phase)){this.epoch++;this.pending?.abort();this.pending=null;}
        this._public('talent.'+detail.phase,JSON.stringify(detail));this.nextWake=this._clock;
      }
      this._emitState();
    });
    talent.addEventListener('cue',({detail})=>this._public('talent.segment.started',JSON.stringify({taskId:detail.taskId,text:detail.text})));
  }
  async _singingApi(path,body){
    const response=await this.client.fetch('/api/singing/'+path,{method:body?'POST':'GET',headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(10000)});
    const data=await response.json();if(!response.ok)throw Error(data.error||'singing_unavailable');return data;
  }
  async _pollSinging(){if(!this.singingEnabled||this._destroyed||this.singingPolling)return;this.singingPolling=true;try{
    const state=await this._singingApi('state'),key=JSON.stringify(state.tasks.map(t=>[t.id,t.status,t.updatedAt]));
    if(key!==this.singingSnapshot){this.singingSnapshot=key;this._cancelPreparation('singing_state_changed');this.nextWake=this._clock;}
  }catch{}finally{this.singingPolling=false;}}
  async _executeSinging(tool){
    if(!this.singingEnabled||this.singingExecuting)return false;this.singingExecuting=true;
    const a=tool.args||{},lifecycle=this.playbackLifecycle,sessionId=this.session.snapshot().sessionId;
    try{
      if(tool.name==='prepare_song'||tool.name==='clarify_song'){
        const m=[...this.inbox,...this._state.messages,...this._sessionPlayers()].find(m=>m.id===a.messageId&&m.mine);if(!m)throw Error('request_source_missing');
        const task=await this._singingApi(tool.name==='prepare_song'?'tasks':`tasks/${a.taskId}/clarify`,{text:m.text,sourceId:m.id,sessionId});
        if(!this._destroyed)this._toolEvent(JSON.stringify({tool:tool.name,status:'persisted',taskId:task.id,state:task.status,request:task.originalText}));
      }else if(tool.name==='cancel_song'){
        if(![...this.inbox,...this._state.messages,...this._sessionPlayers()].some(m=>m.id===a.messageId&&m.mine))throw Error('request_source_missing');
        const task=await this._singingApi(`tasks/${a.taskId}/cancel`,{});this._toolEvent(JSON.stringify({tool:tool.name,status:task.status,taskId:task.id}));
      }else{
        if(this._state.play.active||this._state.media||this._state.paused)throw Error('audio_busy');
        const playback=await this._singingApi(`tasks/${a.taskId}/play`,{sessionId,operationId:id()});
        if(this._destroyed||lifecycle!==this.playbackLifecycle||this._state.paused||this._state.media||this._state.play.active){return false;}
        const catalog=this.talent.catalog;catalog.songs=catalog.songs.filter(s=>s.id!==playback.song.id).concat(playback.song);
        if(!this.talent.start('singing',{songId:playback.song.id})){throw Error('playback_not_started');}
        this.talent.current.songRequestId=playback.taskId;
      }
      this.nextWake=this._clock;return true;
    }catch(e){if(!this._destroyed)this._toolEvent(JSON.stringify({tool:tool.name,status:'failed',reason:/^[a-z_]+$/.test(e.message)?e.message:'singing_unavailable'}));return false;}finally{this.singingExecuting=false;}
  }
  _mediaPlaying(){return ['preparing','ready','starting','playing','paused'].includes(this._state.media?.phase);}
  stopTalent(){this.playbackLifecycle++;return this.talent?.cancel('viewer_stop')||false;}
  _public(type,text,metadata={}){
    if(this.handoffEnabled&&!['speech.progress','speech.completed','npc.message','player.message'].includes(type)){
      this.audience.clearPreparation('public_context_changed');this.audiencePreview=null;
    }
    const e={id:id(),type,text:String(text).slice(0,360),...metadata};
    this.audience.record(e,{replace:Boolean(metadata.speechId)});
    const published=this.audience.getPublicEvents().find(x=>x.id===e.id);if(!published)return;
    const index=this.facts.findIndex(x=>x.id===e.id);if(index>=0)this.facts[index]=published;else this.facts.push(published);
    this.facts=this.facts.slice(-12);this.session.record(published);
    if(!['speech.progress','speech.completed'].includes(type)&&!(type==='npc.message'&&this.audience.config.phases)){
      const transitionWasPreparing=Boolean(this.topicWork||this.topicProgress);
      this._cancelTopic('public_context_changed');
      const matchingPublication=type==='npc.message'&&this.audiencePreview?.status==='published'&&this.audiencePreview.messageId===e.id;
      if(!matchingPublication&&(type!=='npc.message'||transitionWasPreparing||this.handoffEnabled))this._cancelPreparation('public_context_changed');
    }
  }
  recordSpeechProgress(speechId,text){
    const speech=this._active;
    if(!speech||speech.id!==speechId||!text||!speech.text.startsWith(text))return;
    const eventId=`speech-${speechId}`,previous=this.facts.find(e=>e.id===eventId);
    if(previous?.text===text)return;
    this._public('speech.progress',text,{id:eventId,speakerRole:'host',speakerId:'Z',speechId:String(speechId),status:'speaking',replyTo:this.speechLinks.get(speechId)||[]});
  }
  exportDialogueTrace(){return {...this.audience.dialogue.exportTrace(),session:this.session.snapshot(),scheduler:{preparing:!!this.preparing,prepared:!!this.prepared,nextWake:this.nextWake,clock:this._clock,topicAssessing:!!this.topicWork,topicProgress:this.topicProgress,audiencePool:this.audience.pool.length,audienceGenerating:this.audience.requests.size}};}
  _receive(m,mine,speakerId){
    this.inboxVersion++;
    if(!mine&&this.audiencePreview?.status==='pending'&&this.audiencePreview.messageId!==m.id){this.audience.clearPreparation('other_message_published');this.audiencePreview=null;}
    if(mine&&(this.handoffEnabled||this.audience.config.phases)){this.audience.clearPreparation('player_arrived');this.audiencePreview=null;}
    if(this.audiencePreview?.messageId===m.id&&this.audiencePreview.text===m.text&&this.audiencePreview.npcId===speakerId)this.audiencePreview.status='published';
    this.inbox.push({id:m.id,name:m.name,text:m.text,mine});if(this.inbox.length>16){const old=this.inbox.shift();this._setReceipt(old.id,'seen');}
    this._public(mine?'player.message':'npc.message',m.text,{id:m.id,speakerRole:mine?'player':'audience',speakerId:speakerId||(mine?'player':m.name)});
    this.audience.dialogue.setWaiting(this.inbox);
    if(mine){this.failureStreak=0;this.speechAudio?.dropWarmSpeech();this.epoch++;this.pending?.abort();this.pending=null;this.nextWake=this._clock;this._setReceipt(m.id,'pending');}
    this.nextWake=this._clock;if(this.handoffEnabled)this._maybePrepare();this._emitState();}
  sendMessage(value){const text=String(value||'').trim();if(!text||text.length>120||this._destroyed||this._state.paused)return false;
    const m=this._message(text,{name:'小岛来客',mine:true,type:'chat'});this._receive(m,true);
    if(!this.enabled){this._setReceipt(m.id,'seen');this._message('主 Agent 尚未连接，请在连接状态中重新检查。');}return true;}
  _hasPendingPlayerChat(){return this.inbox?.some(m=>m.mine)||false;}
  toggleFollow(){if(this._destroyed)return this._state.followed;this._state.followed=!this._state.followed;this._persist();this._public('viewer.follow',this._state.followed?'有观众关注了 Z。':'观众取消了关注。');this._emitState();return this._state.followed;}
  _selectPlayerReply(){return false;}
  _seedChat(){}
  advance(){return false;}
  triggerStory(){return false;} openLottery(){return false;}
  _tick(){if(this._destroyed||this._state.paused)return;const now=performance.now();this._clock+=Math.max(0,Math.min(500,now-this._lastTick));this._lastTick=now;
    const due=this._jobs.filter(j=>j.at<=this._clock);this._jobs=this._jobs.filter(j=>j.at>this._clock);for(const j of due)j.callback();
    if(this._active&&(this._speechDriven?this._active.complete:this._active.until<=this._clock))this._finishSpeech();
    this._pumpSpeech();
    if(this._canPresentMedia?.()!==false&&this._state.media?.phase==='ready'&&!this._active&&!this._queue.length&&!this.pending)void this.talent.play();
    this.audience.tick();this._maybeAssessTopic();this._maybePrepare();this._think();}
  _pumpSpeech(){if(!this._mediaPlaying())super._pumpSpeech();}
  _cancelPreparation(reason){
    if(this.prepared)this.speechAudio?.dropWarmSpeech(this.prepared.requestId);
    this.preparationVersion++;this.preparing?.controller.abort();this.preparing=null;this.prepared=null;
    this.audience?.dialogue.log({stage:'host.prepare.invalidated',reason});
  }
  _hostContext(inbox,turn={mode:'live'}){
    return {...this._toolContext(),experience:'autonomous',publicEvents:this.facts.slice(-12),host:{inbox,memory:this.memory,recentExpressions:this.recentExpressions.map(e=>({...e})),session:this.session.snapshot(),turn,...(turn.mode==='prepare'&&this.audiencePreview?.status==='pending'?{pendingAudience:(({id,messageId,npcId,text,status})=>({id,messageId,npcId,text,status}))(this.audiencePreview)}:{}),...(this.topicProgress?{topicProgress:this.topicProgress}:{})}};
  }
  _maybePrepare(){
    const speech=this._active,plan=this.speechPlan,preview=this.audiencePreview?.status==='pending'?this.audiencePreview:null;
    if(this.failureStreak>=3||!this.enabled||!this._started||this.pending||this.preparing||this.prepared||this._state.paused||this._destroyed||this._mediaPlaying()||!speech&&!preview)return;
    if(speech&&(!plan||plan.speechId!==speech.id))return;
    const owner=preview||plan;if(owner.attemptedVersion===this.preparationVersion)return;
    const inbox=this._unhandledInbox(),playerWaiting=inbox.some(m=>m.mine);
    if(!preview&&!playerWaiting&&(plan.epoch!==this.epoch||plan.result.action!=='speak'||!['continue','leave_space'].includes(plan.result.continuation)))return;
    owner.attemptedVersion=this.preparationVersion;
    const controller=new AbortController(),version=this.preparationVersion,requestId=id();
    const context=this._hostContext(inbox,{mode:'prepare',...(speech?{inProgress:{speechId:String(speech.id),text:speech.text}}:{})});
    const work={controller,version};this.preparing=work;
    this.audience.dialogue.log({stage:'host.prepare.started',requestId,anchorSpeechId:speech?.id||null,priority:playerWaiting?'player':preview?'selected_audience':'continuation'});
    void this.client.generate({requestId,kind:'host',eventId:requestId,message:'提前准备下一步；未发布的选定弹幕只供预备，不代表已经说过。',context},{signal:controller.signal}).then(result=>{
      if(controller.signal.aborted||version!==this.preparationVersion||this._destroyed||this._state.paused)return;
      this.prepared={result,requestId,context,at:this._clock,anchorSpeechId:speech?.id||null,audienceDependency:preview?{id:preview.id,messageId:preview.messageId,npcId:preview.npcId,text:preview.text}:null};
      this._warmCandidate(requestId,result);
      this.audience.dialogue.log({stage:'host.prepare.ready',requestId});
    }).catch(()=>{if(!controller.signal.aborted)this.audience.dialogue.log({stage:'host.prepare.unavailable',requestId});})
      .finally(()=>{if(this.preparing===work)this.preparing=null;});
  }
  async _think(){if(this.failureStreak>=3||this.singingExecuting||!this.enabled||!this._started||this.pending||this._active||this._queue.length||this._state.paused||this._destroyed||this._clock<this.nextWake)return;
    if(this._mediaPlaying()&&!this.inbox.some(m=>m.mine))return;
    if(this.audiencePreview?.status==='pending'||this.preparing)return;
    let prepared=this.prepared;this.prepared=null;
    if(prepared&&(this._clock-prepared.at>30000||prepared.anchorSpeechId!==null&&!this.session.events.some(e=>e.id===`speech-${prepared.anchorSpeechId}`&&e.type==='speech.completed'))){this.speechAudio?.dropWarmSpeech(prepared.requestId);prepared=null;}
    if(prepared?.audienceDependency){const d=prepared.audienceDependency;
      if(!this.session.events.some(e=>e.id===d.messageId&&e.type==='npc.message'&&e.speakerId===d.npcId&&e.text===d.text)){this.speechAudio?.dropWarmSpeech(prepared.requestId);prepared=null;}
    }
    const controller=new AbortController(),epoch=this.epoch;this.pending=controller;
    const inbox=this.inbox.map(m=>({...m})),requestId=prepared?.requestId||id(),context=prepared?.context||this._hostContext(inbox),generationContext={publicEvents:context.publicEvents,inbox:context.host.inbox,turn:context.host.turn};
    const sentenceReply=this._beginSentenceReply?.({controller,epoch,inbox,requestId,context,generationContext});
    try{
      this._waitingReceipts('pending',inbox);this._state.agent.source='pending';this._state.agent.errorCode=null;this._emitState();
      let result=prepared?.result||await this.client.generate({requestId,kind:'host',eventId:requestId,message:'请决定直播下一步。',context},{signal:controller.signal,...(sentenceReply?{onSentence:sentenceReply.onSentence}:{})});
      if(epoch!==this.epoch||this._destroyed||this._state.paused)return;
      this._validateCallReply?.(result,context);
      const allowed=new Set(inbox.map(m=>m.id));
      if(!['speak','wait','tool'].includes(result.action)||!Array.isArray(result.handledIds)||result.handledIds.some(x=>!allowed.has(x))||!Number.isInteger(result.wakeAfterMs)||result.wakeAfterMs<2000||result.wakeAfterMs>120000)throw Error('invalid_host_decision');
      if(result.continuation!==undefined&&!['continue','leave_space','await_event'].includes(result.continuation))throw Error('invalid_continuation');
      const agendaUpdates=validateAgendaUpdates(result.agendaUpdates,{items:context.host.session.unfinishedItems,evidence:context.host.session.evidence,allowCurrentSpeech:result.action!=='wait'});
      const gesture=result.gesture??'none',transition=result.transition??'smooth';
      if(!TALENT_MOODS.includes(result.mood)||!TALENT_GESTURES.includes(gesture)||!['smooth','sudden'].includes(transition)||result.action==='wait'&&gesture!=='none')throw Error('invalid_host_expression');
      if(result.sessionDecision?.intent==='stop'&&this.commitSessionDecision?.(result,context))return;
      if(this._mediaPlaying()&&result.action!=='wait'){
        if(result.action!=='tool'||result.tool?.name!=='finish_activity'||result.tool.args?.taskId!==this._state.media.id||result.tool.args?.cancel!==true)throw Error('audio_busy');
        // Stop playback only after this utterance passes review.

      }
      if(sentenceReply?.received()){await sentenceReply.finish(result,agendaUpdates);}
      else if(result.action==='speak'||result.action==='tool'){
        if(!result.speech)throw Error('missing_speech');
        let preparedAudioKey=requestId,repaired=false,ticket,reviewFailure=null;
        const candidate={id:requestId,role:'host',speakerId:'Z',text:result.speech,intent:result.action,replyTo:result.handledIds,evidenceIds:[],...(this._state.call?.active?{segment:{index:0,scope:'reply'}}:{}),...(result.action==='tool'?{plannedTool:{name:result.tool.name,targetId:result.tool.args?.taskId||result.tool.args?.songId||result.tool.args?.messageId||''}}:{})};
        if(this._state.call?.active&&this._approveCallCandidate){
          const approved=await this._approveCallCandidate({candidate,result,context,signal:controller.signal,valid:()=>epoch===this.epoch&&!controller.signal.aborted&&!this._destroyed&&!this._state.paused&&this._state.call?.active&&this._state.call.input==='idle'});
          ticket=approved.ticket;preparedAudioKey=approved.candidate.id;repaired=approved.repaired;result={...result,speech:approved.candidate.text};
        }else{this._warmCandidate(requestId,result);ticket=await this.audience.dialogue.inspect(candidate,{signal:controller.signal,generationContext,sessionEvents:this.session.evidence(),onFailure:reason=>{reviewFailure=reason;}});}
        if(epoch!==this.epoch||controller.signal.aborted||this._destroyed||this._state.paused)return;
        if(!ticket&&reviewFailure==='stale'){this.nextWake=this._clock+350;return;}
        if(!ticket)throw Object.assign(Error('reply_not_approved'),{code:'agent_reply_not_approved'});
        if(this._active||this._queue.length){this.nextWake=this._clock+2000;return;}
        const committed=this.audience.dialogue.commit(ticket,()=>{
          if(result.sessionDecision&&this.commitSessionDecision?.(result,context))return;
          if(this._mediaPlaying()){this._executeTool(result.tool);result.action='speak';}
          const handled=new Set(result.handledIds);for(const mid of handled)this._setReceipt(mid,'queued');
          // _say starts immediately here; no asynchronous gap after the revision check.
          const speechId=this._speechId+1,playbackLifecycle=this.playbackLifecycle;this.speechLinks.set(speechId,[...handled]);this._onHostSpeechCommitted?.(speechId,result);
          if(this.audiencePreview?.status==='published')this.audiencePreview=null;
          this._say(result.speech,result.mood,gesture,{source:'deepseek',transition,preparedAudioKey,onComplete:()=>{
            for(const mid of handled)this._setReceipt(mid,'replied');this.inbox=this.inbox.filter(m=>!handled.has(m.id));
            this.audience.dialogue.setWaiting(this.inbox);
            const completed=this.session.events.find(e=>e.id===`speech-${speechId}`&&e.type==='speech.completed');
            if(completed&&!repaired){this.session.apply(agendaUpdates,context.host.session,completed,!handled.size);this.memory=String(result.memory||'').slice(0,800);}if(completed)this._onHostReplyCompleted?.(result,context);
            if(this.speechPlan?.speechId===speechId)this.speechPlan.completedAt=this._clock;
            this.nextWake=this._clock+(epoch===this.epoch&&this.speechPlan?.inboxVersion===this.inboxVersion?this._wakeDelay(result):0);
            if(result.action==='tool'){
              const committedPlayback=['play_prepared_song','start_singing','replay_talent'].includes(result.tool?.name);
              // Once its reviewed introduction completes, ordinary chat waits for the song.
              // Pause, stop, replay and destruction still revoke this committed action.
              const valid=committedPlayback?playbackLifecycle===this.playbackLifecycle&&!this._state.paused&&!this._destroyed:epoch===this.epoch;
              if(valid)this._executeTool(result.tool);else this._toolEvent('工具提议已取消，直播状态或请求已改变。');
            }
            this._emitState();}});
          this.speechPlan={speechId,epoch,result,attempted:false,inboxVersion:this.inboxVersion};
        });
        if(!committed){this.nextWake=this._clock+2000;return;}
      }else{if(this.audiencePreview?.status==='published')this.audiencePreview=null;for(const mid of result.handledIds)this._setReceipt(mid,'seen');this.inbox=this.inbox.filter(m=>!result.handledIds.includes(m.id));this.audience.dialogue.setWaiting(this.inbox);this.session.apply(agendaUpdates,context.host.session);this.memory=String(result.memory||'').slice(0,800);}
      this.nextWake=this._clock+this._wakeDelay(result);
      this.failureStreak=0;this._state.agent.errorCode=null;this._state.agent.validationReason=null;this._state.agent.source='deepseek';this._state.textStatus='ready';
    }catch(error){sentenceReply?.abort();if(error.code==='agent_reply_stale'){if(epoch===this.epoch&&!controller.signal.aborted){this.nextWake=this._clock+350;if(this._state.call?.active)this.callNeedsTurn=true;}return;}if(epoch===this.epoch&&!this._destroyed&&!controller.signal.aborted){if(error.code==='agent_reply_repair_failed')this.callReviewBlocked=true;this.failureStreak++;const retryMs=this.callReviewBlocked?0:error.code==='agent_insufficient_balance'?60000:this.failureStreak<=3?1000*2**(this.failureStreak-1):15000;this.audience.dialogue.log({stage:'host.request.failed',code:error.code||'host_decision_unavailable',retryMs});this._state.agent.errorCode=error.code||'host_decision_unavailable';this._state.agent.validationReason=error.validationReason||null;this._state.agent.source='unavailable';this._state.textStatus='fallback';this._waitingReceipts('failed',inbox);this.nextWake=this._clock+retryMs;}}
    finally{sentenceReply?.dispose();this.speechAudio?.dropWarmSpeech(requestId);if(this.pending===controller)this.pending=null;this._emitState();}
  }
  _waitingReceipts(status,messages=this.inbox){
    const waiting=messages.filter(m=>m.mine&&this.inbox.some(x=>x.id===m.id));
    for(const m of waiting)this._setReceipt(m.id,status);
    if(waiting.length){this._state.replyTo=waiting.at(-1).id;this._state.replyStatus=status;}
  }
  retryReply(){
    if(this._destroyed||this._state.paused||this.pending||this._active||this._queue.length)return false;
    this.failureStreak=0;this.nextWake=this._clock;this._state.agent.errorCode=null;this._state.agent.validationReason=null;
    this._waitingReceipts('pending');if(this._state.call?.active)this.callNeedsTurn=true;this._emitState();return true;
  }
  _sessionPlayers(){return this.session.evidence().filter(e=>e.type==='player.message'&&e.speakerRole==='player').map(e=>({id:e.id,name:'小岛来客',text:e.text,mine:true}));}
  _toolContext(){
    const c={phase:'chat',activeSkill:null};
    c.mediaRuntime={audioEnabled:!!this.talent?.speech.enabled&&this.talent.speech.volume>0,ttsAvailable:!!this.talent?.speech.available};
    if(this._state.media){const m=this.talent.snapshot();return {...c,activeSkill:m.kind,phase:m.phase,taskId:m.id,media:m};}
    return c;
  }
  _toolEvent(text){this._public('tool.result',text);this.nextWake=this._clock;}
  _executeTool(tool){
    if(['prepare_song','clarify_song','cancel_song','play_prepared_song'].includes(tool?.name)){void this._executeSinging(tool);return true;}
    const a=tool?.args||{};let ok=false;
    if(tool?.name==='start_singing'&&!this._state.media){
      ok=this.talent?.start('singing',a)||false;
    }else if(tool?.name==='replay_talent'&&this._state.media&&a.taskId===this._state.media.id)ok=this.talent.replay();
    else if(tool?.name==='finish_activity'&&this._state.media&&a.taskId===this._state.media.id)ok=this.talent.finish(a.cancel);
    this._toolEvent(JSON.stringify({tool:tool?.name,status:ok?'accepted':'unavailable_or_stale',taskId:this._state.media?.id||null}));return ok;
  }
  _finishSpeech(){const speech=this._active;super._finishSpeech();if(speech){this._public('speech.completed',speech.text,{id:`speech-${speech.id}`,speakerRole:'host',speakerId:'Z',speechId:String(speech.id),status:'completed',replyTo:this.speechLinks.get(speech.id)||[]});this.speechLinks.delete(speech.id);}}
  setPaused(value){if(value){this.playbackLifecycle++;if(this.handoffEnabled){this.audience.clearPreparation('paused');this.audiencePreview=null;}this._cancelTopic('paused');this.speechAudio?.dropWarmSpeech();this._cancelPreparation('paused');this.epoch++;this.pending?.abort();this.pending=null;this.audience.dialogue.invalidate();}super.setPaused(value);if(value)this.talent?.pause();else {this.talent?.resume();if(this.handoffEnabled&&this._active?.source==='deepseek')this.audience.setHostPreview({speechId:this._active.id,text:this._active.text});}}
  replay(){this.playbackLifecycle++;this.audiencePreview=null;this.inboxVersion=0;this._cancelTopic('replay');this.speechAudio?.dropWarmSpeech();this._cancelPreparation('replay');this.session.reset();this.speechPlan=null;this.failureStreak=0;this.epoch++;this.pending?.abort();this.pending=null;this.client?.cancel();this.audience.invalidate();this.inbox=[];this.facts=[];this.memory='';this.nextWake=0;
    this.recentExpressions=[];this.speechLinks.clear();
    this.talent?.reset();this.facts=[];this.session.reset();const agent=this._state.agent;super.replay();this._state.media=null;this._state.play={active:null,queue:[],tarot:null,drawing:null,history:[]};this._state.stage='free';this._state.stageLabel='和 Z 聊聊天';this._state.agent=agent;this.audience.setConfig(this.audience.config);this._emitState();return true;}
  destroy(){this.playbackLifecycle++;clearInterval(this.singingPoll);this.audiencePreview=null;this._cancelTopic('destroyed');this.speechAudio?.dropWarmSpeech();this._cancelPreparation('destroyed');this.epoch++;this.pending?.abort();this.client?.cancel();this.audience.invalidate();this.talent?.destroy();super.destroy();}
}
