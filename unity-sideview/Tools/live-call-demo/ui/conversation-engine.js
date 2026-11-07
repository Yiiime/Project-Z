import {SessionContinuity,validateAgendaUpdates,validateTopicProgress} from './session-continuity.js';
import {DrawingDirector} from './drawing-director.js';
import {PlayEngine} from './play-engine.js';
import {AudienceDirector} from './audience-director.js';
import {TALENT_MOODS,TALENT_GESTURES,liveToolEnabled,liveSkillEnabled} from './talent-config.js';
const id=()=>crypto.randomUUID();
// The model owns conversational decisions. This runtime only executes speech/wait.
export class ConversationEngine extends PlayEngine {
  constructor(){
    super({storage:null});this.client=null;this.enabled=false;this.epoch=0;this.inbox=[];this.facts=[];this.memory='';this.nextWake=0;this.pending=null;
    this.session=new SessionContinuity();this.preparing=null;this.prepared=null;this.preparationVersion=0;this.speechPlan=null;this.speechAudio=null;this.failureStreak=0;this.topicEnabled=false;this.topicWork=null;this.topicProgress=null;this.topicVersion=0;this.handoffEnabled=false;this.audiencePreview=null;this.inboxVersion=0;
    this._state.play={active:null,queue:[],tarot:null,drawing:null,history:[]};
    this._state.agent={enabled:false,source:'local',capabilities:[]};
    this.talent=null;this._state.media=null;
    this.drawings=new DrawingDirector({generate:(p,o)=>this.client.generate(p,o),now:()=>this._clock,changed:d=>{this._state.play.drawing=d;this._emitState();},event:(type,text)=>{this.epoch++;this.pending?.abort();this.pending=null;this._public(type,text);this.nextWake=this._clock;}});
    this.recentExpressions=[]; this.speechLinks=new Map();
    this.addEventListener('speak',({detail})=>{
      if(detail.resume)return;
      this._cancelTopic('new_speech');
      this.audience?.dialogue.invalidate(this._state.call?.active?'host':null);
      if(detail.source!=='deepseek')return;
      if(this.handoffEnabled)this.audience.setHostPreview({speechId:detail.id,text:detail.text});
      // These are dispatched speech cues, not proof that a model was rendered.
      this.recentExpressions.push({mood:detail.mood,gesture:detail.gesture,transition:detail.transition,speech:detail.text.slice(0,80)});
      this.recentExpressions=this.recentExpressions.slice(-6);
    });
    this._state.stage='free';this._state.stageLabel='和 Z 聊聊天';
    this.audience=new AudienceDirector({now:()=>this._clock,request:(p,o)=>this.client.generate(p.kind==='audience'?{...p,context:{...p.context,experience:'autonomous'}}:p,o),
      canPublish:()=>this.enabled&&this._started&&!this._destroyed&&!this._state.paused,
      canCommit:()=>!this.pending,
      canPreview:()=>this.handoffEnabled&&!this.pending&&!this.audiencePreview&&!this._unhandledInbox().some(m=>m.mine)&&!this._mediaPlaying()&&(!this._active||this.speechPlan?.result.action==='speak'&&this.speechPlan.result.continuation!=='await_event'),
      onSelected:preview=>this._selectAudiencePreview(preview),
      onSelectionEnd:entry=>this._endAudiencePreview(entry),
      canAdvance:()=>!this.inbox.some(m=>m.mine),publish:item=>{
        const m=this._message(item.text,{id:item.messageId,name:({wind:'晚风来信',moon:'月亮邮差',salt:'海盐汽水'})[item.npc_id],type:'chat',source:'agent-audience'});
        this._receive(m,false,item.npc_id);}});
  }
  start(){if(this._started||this._destroyed)return;this._started=true;this._lastTick=performance.now();this._timer=setInterval(()=>this._tick(),160);
    this._public('session.opened','Z 的直播已开始，现在是自由对话，没有预设节目。');this._emitState();}
  configureAgent(client,config={}){this.client=client;this.singingEnabled=config.singingBackground===true;clearInterval(this.singingPoll);if(this.singingEnabled){void this._pollSinging();this.singingPoll=setInterval(()=>void this._pollSinging(),5000);}this.enabled=!!config.enabled;this.topicEnabled=config.topic?.enabled===true;this.handoffEnabled=config.policy?.audience?.handoff===true;this.audience.setConfig(config.policy?.audience||{});this.audience.dialogue.configure(config.review);
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
      if(detail&&old?.phase!==detail.phase)void this._singingReceipt(detail);
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
    const j=this.talent?.current;if(j?.persistentPlayback&&['playing','paused'].includes(j.phase))void this._singingReceipt(this.talent.snapshot());
  }catch{}finally{this.singingPolling=false;}}
  _singingReceipt(detail){
    const persistent=this.talent?.current?.persistentPlayback;if(!persistent||!['playing','paused','completed','failed','interrupted'].includes(detail.phase))return;
    const payload={eventId:id(),phase:detail.phase,positionMs:detail.positionMs};
    this.singingReceipts=(this.singingReceipts||Promise.resolve()).catch(()=>{}).then(async()=>{
      // Retry the same receipt ID: completion must not be lost or counted twice.
      for(let attempt=0;attempt<2;attempt++){try{await this._singingApi('playbacks/'+persistent.playbackId,payload);return;}catch(e){if(attempt===1){this.audience.dialogue.log({stage:'singing.receipt.failed',phase:detail.phase});return;}}}
    });return this.singingReceipts;
  }
  async _executeSinging(tool){
    if(!this.singingEnabled)return false;
    const a=tool.args||{},epoch=this.epoch,sessionId=this.session.snapshot().sessionId;
    try{
      if(tool.name==='prepare_song'||tool.name==='clarify_song'){
        const m=[...this.inbox,...this._state.messages,...this._sessionPlayers()].find(m=>m.id===a.messageId&&m.mine);if(!m)throw Error('request_source_missing');
        const task=await this._singingApi(tool.name==='prepare_song'?'tasks':`tasks/${a.taskId}/clarify`,{text:m.text,sourceId:m.id,sessionId});
        if(!this._destroyed)this._toolEvent(JSON.stringify({tool:tool.name,status:'persisted',taskId:task.id,state:task.status,request:task.originalText}));
      }else{
        if(this._state.play.active||this._state.media||this._state.paused)throw Error('audio_busy');
        const playback=await this._singingApi(`tasks/${a.taskId}/play`,{sessionId,operationId:id()});
        if(this._destroyed||epoch!==this.epoch||this._state.paused||this._state.media||this._state.play.active){await this._singingApi('playbacks/'+playback.playbackId,{eventId:id(),phase:'interrupted',positionMs:0});return false;}
        const catalog=this.talent.catalog;catalog.songs=catalog.songs.filter(s=>s.id!==playback.song.id).concat(playback.song);
        if(!this.talent.start('singing',{songId:playback.song.id})){await this._singingApi('playbacks/'+playback.playbackId,{eventId:id(),phase:'failed',positionMs:0});throw Error('playback_not_started');}
        this.talent.current.persistentPlayback=playback;
      }
      this.nextWake=this._clock;return true;
    }catch(e){if(!this._destroyed)this._toolEvent(JSON.stringify({tool:tool.name,status:'failed',reason:/^[a-z_]+$/.test(e.message)?e.message:'singing_unavailable'}));return false;}
  }
  _mediaPlaying(){return ['starting','playing','paused'].includes(this._state.media?.phase);}
  stopTalent(){return this.talent?.cancel('viewer_stop')||false;}
  _public(type,text,metadata={}){
    if(this.handoffEnabled&&!['speech.progress','speech.completed','npc.message','player.message'].includes(type)){
      this.audience.clearPreparation('public_context_changed');this.audiencePreview=null;
    }
    const e={id:id(),type,text:String(text).slice(0,360),...metadata};
    this.audience.record(e,{replace:Boolean(metadata.speechId)});
    const published=this.audience.getPublicEvents().find(x=>x.id===e.id);if(!published)return;
    const index=this.facts.findIndex(x=>x.id===e.id);if(index>=0)this.facts[index]=published;else this.facts.push(published);
    this.facts=this.facts.slice(-12);this.session.record(published);
    if(!['speech.progress','speech.completed'].includes(type)){
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
    if(mine&&this.handoffEnabled){this.audience.clearPreparation('player_arrived');this.audiencePreview=null;}
    if(this.audiencePreview?.messageId===m.id&&this.audiencePreview.text===m.text&&this.audiencePreview.npcId===speakerId)this.audiencePreview.status='published';
    this.inbox.push({id:m.id,name:m.name,text:m.text,mine});if(this.inbox.length>16){const old=this.inbox.shift();this._setReceipt(old.id,'seen');}
    this._public(mine?'player.message':'npc.message',m.text,{id:m.id,speakerRole:mine?'player':'audience',speakerId:speakerId||(mine?'player':m.name)});
    this.audience.dialogue.setWaiting(this.inbox);
    if(mine){this.speechAudio?.dropWarmSpeech();this.epoch++;this.pending?.abort();this.pending=null;this.nextWake=this._clock;this._setReceipt(m.id,'pending');}
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
    if(!this._active&&!this._queue.length&&!this.pending)this.drawings.advance();this.audience.tick();this._maybeAssessTopic();this._maybePrepare();this._think();}
  _pumpSpeech(){if(!this._mediaPlaying())super._pumpSpeech();}
  _cancelPreparation(reason){
    if(this.prepared)this.speechAudio?.dropWarmSpeech(this.prepared.requestId);
    this.preparationVersion++;this.preparing?.controller.abort();this.preparing=null;this.prepared=null;
    this.audience?.dialogue.log({stage:'host.prepare.invalidated',reason});
  }
  _hostContext(inbox,turn={mode:'live'}){
    return {...this._toolContext(),experience:'autonomous',publicEvents:this.facts.slice(-12),host:{inbox,memory:this.memory,round:this._state.play.tarot?.round||0,recentExpressions:this.recentExpressions.map(e=>({...e})),session:this.session.snapshot(),turn,...(turn.mode==='prepare'&&this.audiencePreview?.status==='pending'?{pendingAudience:(({id,messageId,npcId,text,status})=>({id,messageId,npcId,text,status}))(this.audiencePreview)}:{}),...(this.topicProgress?{topicProgress:this.topicProgress}:{})}};
  }
  _maybePrepare(){
    const speech=this._active,plan=this.speechPlan,preview=this.audiencePreview?.status==='pending'?this.audiencePreview:null;
    if(!this.enabled||!this._started||this.pending||this.preparing||this.prepared||this._state.paused||this._destroyed||this._mediaPlaying()||!speech&&!preview)return;
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
  async _think(){if(!this.enabled||!this._started||this.pending||this._active||this._queue.length||this._state.paused||this._destroyed||this._clock<this.nextWake)return;
    if(this._mediaPlaying()&&!this.inbox.some(m=>m.mine))return;
    if(this.audiencePreview?.status==='pending'||this.preparing)return;
    let prepared=this.prepared;this.prepared=null;
    if(prepared&&(this._clock-prepared.at>30000||prepared.anchorSpeechId!==null&&!this.session.events.some(e=>e.id===`speech-${prepared.anchorSpeechId}`&&e.type==='speech.completed'))){this.speechAudio?.dropWarmSpeech(prepared.requestId);prepared=null;}
    if(prepared?.audienceDependency){const d=prepared.audienceDependency;
      if(!this.session.events.some(e=>e.id===d.messageId&&e.type==='npc.message'&&e.speakerId===d.npcId&&e.text===d.text)){this.speechAudio?.dropWarmSpeech(prepared.requestId);prepared=null;}
    }
    const controller=new AbortController(),epoch=this.epoch;this.pending=controller;
    const inbox=this.inbox.map(m=>({...m})),requestId=prepared?.requestId||id(),context=prepared?.context||this._hostContext(inbox),generationContext={publicEvents:context.publicEvents,inbox:context.host.inbox,turn:context.host.turn};
    const sentenceReply=!prepared?this._beginSentenceReply?.({controller,epoch,inbox,requestId,context,generationContext}):null;
    try{
      const result=prepared?.result||await this.client.generate({requestId,kind:'host',eventId:requestId,message:'请决定直播下一步。',context},{signal:controller.signal,...(sentenceReply?{onSentence:sentenceReply.onSentence}:{})});
      if(epoch!==this.epoch||this._destroyed||this._state.paused)return;
      this._validateCallReply?.(result,context);
      const allowed=new Set(inbox.map(m=>m.id));
      if(!['speak','wait','tool'].includes(result.action)||!Array.isArray(result.handledIds)||result.handledIds.some(x=>!allowed.has(x))||!Number.isInteger(result.wakeAfterMs)||result.wakeAfterMs<2000||result.wakeAfterMs>120000)throw Error('invalid_host_decision');
      if(result.continuation!==undefined&&!['continue','leave_space','await_event'].includes(result.continuation))throw Error('invalid_continuation');
      const agendaUpdates=validateAgendaUpdates(result.agendaUpdates,{items:context.host.session.unfinishedItems,evidence:context.host.session.evidence,allowCurrentSpeech:result.action!=='wait'});
      const gesture=result.gesture??'none',transition=result.transition??'smooth';
      if(!TALENT_MOODS.includes(result.mood)||!TALENT_GESTURES.includes(gesture)||!['smooth','sudden'].includes(transition)||result.action==='wait'&&gesture!=='none')throw Error('invalid_host_expression');
      if(sentenceReply?.received()){
        await sentenceReply.finish(result,agendaUpdates);
        if(epoch!==this.epoch||controller.signal.aborted)return;
        this.nextWake=this._clock+this._wakeDelay(result);this.failureStreak=0;this._state.agent.errorCode=null;this._state.agent.source='deepseek';this._state.textStatus='ready';return;
      }
      if(this._mediaPlaying()&&result.action!=='wait'){
        if(result.action!=='tool'||result.tool?.name!=='finish_activity'||result.tool.args?.taskId!==this._state.media.id||result.tool.args?.cancel!==true)throw Error('audio_busy');
        // Stop playback only after this utterance passes review.

      }
      if(result.action==='speak'||result.action==='tool'){
        if(!result.speech)throw Error('missing_speech');
        this._warmCandidate(requestId,result);
        const candidate={id:requestId,role:'host',speakerId:'Z',text:result.speech,intent:result.action,replyTo:result.handledIds,evidenceIds:[]};
        const ticket=await this.audience.dialogue.inspect(candidate,{signal:controller.signal,generationContext,sessionEvents:this.session.evidence(),compact:!!context.call?.active});
        if(epoch!==this.epoch||controller.signal.aborted||this._destroyed||this._state.paused)return;
        if(!ticket||this._active||this._queue.length){this.nextWake=this._clock+2000;return;}
        const committed=this.audience.dialogue.commit(ticket,()=>{
          if(this._mediaPlaying()){this._executeTool(result.tool);result.action='speak';}
          const handled=new Set(result.handledIds);for(const mid of handled)this._setReceipt(mid,'queued');
          // _say starts immediately here; no asynchronous gap after the revision check.
          const speechId=this._speechId+1;this.speechLinks.set(speechId,[...handled]);
          if(this.audiencePreview?.status==='published')this.audiencePreview=null;
          this._onHostSpeechCommitted?.(speechId,result,context);
          this._say(result.speech,result.mood,gesture,{source:'deepseek',transition,preparedAudioKey:requestId,onComplete:()=>{
            for(const mid of handled)this._setReceipt(mid,'replied');this.inbox=this.inbox.filter(m=>!handled.has(m.id));
            this.audience.dialogue.setWaiting(this.inbox);
            const completed=this.session.events.find(e=>e.id===`speech-${speechId}`&&e.type==='speech.completed');
            if(completed){this.session.apply(agendaUpdates,context.host.session,completed,!handled.size);this.memory=String(result.memory||'').slice(0,800);}
            if(this.speechPlan?.speechId===speechId)this.speechPlan.completedAt=this._clock;
            this.nextWake=this._clock+(epoch===this.epoch&&this.speechPlan?.inboxVersion===this.inboxVersion?this._wakeDelay(result):0);
            if(result.action==='tool'){if(epoch===this.epoch)this._executeTool(result.tool);else this._toolEvent('工具提议已取消，期间收到新观众消息。');}
            this._emitState();}});
          this.speechPlan={speechId,epoch,result,attempted:false,inboxVersion:this.inboxVersion};
        });
        if(!committed){this.nextWake=this._clock+2000;return;}
      }else{if(this.audiencePreview?.status==='published')this.audiencePreview=null;for(const mid of result.handledIds)this._setReceipt(mid,'seen');this.inbox=this.inbox.filter(m=>!result.handledIds.includes(m.id));this.audience.dialogue.setWaiting(this.inbox);this.session.apply(agendaUpdates,context.host.session);this.memory=String(result.memory||'').slice(0,800);}
      this.nextWake=this._clock+this._wakeDelay(result);
      this.failureStreak=0;this._state.agent.errorCode=null;this._state.agent.source='deepseek';this._state.textStatus='ready';
    }catch(error){sentenceReply?.abort();if(epoch===this.epoch&&!this._destroyed&&!controller.signal.aborted){this.failureStreak++;const retryMs=error.code==='agent_insufficient_balance'?60000:this.failureStreak<=3?1000*2**(this.failureStreak-1):15000;this.audience.dialogue.log({stage:'host.request.failed',code:error.code||'host_decision_unavailable',retryMs});this._state.agent.errorCode=error.code||'agent_response_invalid';this._state.agent.source=['agent_connection_failed','agent_provider_unavailable','agent_not_configured','agent_insufficient_balance','agent_unavailable'].includes(error.code)?'unavailable':'retrying';this._state.textStatus='fallback';this.nextWake=this._clock+retryMs;}}
    finally{sentenceReply?.dispose();this.speechAudio?.dropWarmSpeech(requestId);if(this.pending===controller)this.pending=null;this._emitState();}
  }
  _sessionPlayers(){return this.session.evidence().filter(e=>e.type==='player.message'&&e.speakerRole==='player').map(e=>({id:e.id,name:'小岛来客',text:e.text,mine:true}));}
  _toolContext(){
    const p=this._state.play,t=p[p.active];const c={phase:'chat',activeSkill:p.active==='tarot'?'entertainment-tarot':p.active==='drawing'?'simple-drawing':null};
    c.mediaRuntime={audioEnabled:!!this.talent?.speech.enabled&&this.talent.speech.volume>0,ttsAvailable:!!this.talent?.speech.available};
    if(this._state.media){const m=this.talent.snapshot();return {...c,activeSkill:m.kind,phase:m.phase,taskId:m.id,media:m};}
    const d=p.drawing;if(d?.mode==='generated_vector')c.drawing={mode:d.mode,artworkId:d.id,revision:d.revision,artifactId:d.artifactId,title:d.title,phase:d.phase,stage:d.stage,stageCount:d.stageCount,visible:d.open,displayedRevision:d.displayedRevision,elements:(d.scene?.elements||[]).map(({id,label,stage,fill,stroke})=>({id,label,stage,fill,stroke,shown:d.displayed===`${d.revision}:${d.stage}`&&stage<=d.stage&&d.open}))};
    if(!t)return c;c.taskId=t.id;c.revision=t.revision||0;
    if(p.active==='tarot'){c.phase=t.phase;c.origin=t.origin;c.question=t.question;
      if(t.phase==='awaiting_selection'&&t.origin==='host')c.candidates=t.candidates.map(({id,label})=>({id,label}));
      if(['interpreting','completed'].includes(t.phase))c.cards=t.cards.map(({name,title,meaning,advice,position})=>({name,title,meaning,advice,position}));
    }else if(t.mode==='generated_vector'){c.phase=t.phase;}else{c.phase=t.phase;const shown=t.displayed?.split(':').map(Number);if(shown?.[0]===t.revision)c.drawing={title:shown[1]>=1?t.title:'轮廓',subject:t.subject,stage:shown[1],...(shown[1]>=1?{accessory:t.accessory}:{})};}
    return c;
  }
  _toolEvent(text){this._public('tool.result',text);this.nextWake=this._clock;}
  _executeTool(tool){
    if(!liveToolEnabled(tool?.name)){this._toolEvent(JSON.stringify({tool:tool?.name,status:'capability_disabled'}));return false;}
    if(['prepare_song','clarify_song','play_prepared_song'].includes(tool?.name)){void this._executeSinging(tool);return true;}
    const a=tool?.args||{},p=this._state.play,t=p[p.active];let ok=false;
    if(['start_performance','start_singing'].includes(tool?.name)&&!p.active&&!this._state.media){
      ok=this.talent?.start(tool.name==='start_performance'?'voice-performance':'singing',a,{inbox:[...this.inbox,...this._state.messages,...this._sessionPlayers()]})||false;
    }else if(tool?.name==='replay_talent'&&this._state.media&&a.taskId===this._state.media.id){const persistent=this.talent.current?.persistentPlayback;if(persistent){this.talent.finish();void this._executeSinging({name:'play_prepared_song',args:{taskId:persistent.taskId}});ok=true;}else ok=this.talent.replay();}
    else if(tool?.name==='finish_activity'&&this._state.media&&a.taskId===this._state.media.id)ok=this.talent.finish(a.cancel);
    else if(tool?.name==='start_tarot'&&!p.active&&!this._state.media&&['host','viewer'].includes(a.origin)&&['single','three'].includes(a.spread)&&typeof a.question==='string'&&a.question.trim()){
      if(a.origin==='host'||this.inbox.some(m=>m.id===a.requesterId&&m.mine)||this._state.messages.some(m=>m.id===a.requesterId&&m.mine)||this._sessionPlayers().some(m=>m.id===a.requesterId))ok=this._request({kind:'tarot',origin:a.origin,question:a.question.slice(0,120),spread:a.spread});
    }else if(tool?.name==='start_drawing'&&!p.active)ok=this.requestDrawing(a);
    else if(tool?.name==='choose_tarot'&&t?.id===a.taskId&&p.active==='tarot')ok=this._chooseTarot(t.id,a.round,a.candidateId,'host');
    else if(tool?.name==='finish_activity'&&t?.id===a.taskId)ok=this.finishActivity(!!a.cancel);
    else if(tool?.name==='set_panel'&&p[a.kind]?.id===a.taskId)ok=this.setPanel(a.kind,a.open);
    else if(tool?.name==='edit_drawing'&&p.drawing?.id===a.taskId)ok=this.editDrawing(a);
    this._toolEvent(JSON.stringify({tool:tool?.name,status:ok?'accepted':'unavailable_or_stale',taskId:this._state.media?.id||this._state.play[this._state.play.active]?.id||null}));return ok;
  }
  requestDrawing(args={}){
    if(!liveSkillEnabled('simple-drawing'))return false;
    const p=this._state.play;if(!this.enabled||!this.client||p.active||this._state.media||this._state.paused||this._destroyed)return false;
    const requester=args.requesterId?[...this.inbox,...this._state.messages,...this._sessionPlayers()].find(m=>m.id===args.requesterId&&m.mine):null;
    if(args.requesterId&&!requester)return false;
    const previous=p.drawing;
    p.active='drawing';
    const ok=this.drawings.start({request:args.request,owner:requester?.name||'Z',requesterId:requester?.id||null});
    if(!ok){p.active=null;return false;}
    if(previous)p.drawingHistory=[...(p.drawingHistory||[]),previous].slice(-5);
    this._state.stageLabel='绘画 · Z 的小画室';this._emitState();return true;
  }
  editDrawing(args){
    if(!liveSkillEnabled('simple-drawing'))return false;
    const p=this._state.play;if(this._state.media||p.active&&p.active!=='drawing'||this._state.paused||this._destroyed||!this.enabled)return false;
    const old=p.active;p.active='drawing';const ok=this.drawings.edit(args||{});if(!ok)p.active=old;
    if(ok){this.epoch++;this.pending?.abort();this.pending=null;this.nextWake=this._clock;this._emitState();}return ok;
  }
  setPanel(kind,open){if(kind==='drawing'&&this._state.media)return false;return super.setPanel(kind,open);}
  // Existing modules execute geometry and deck transitions, never scripted hosting.
  _line(kind,taskId,line,done,revision){if(this._valid(kind,taskId,revision))done?.();}
  _autoClose(){}
  _drainPlayQueue(){}
  _request(request){if(!liveSkillEnabled({tarot:'entertainment-tarot',drawing:'simple-drawing'}[request.kind]))return false;if(this._state.play.active||this._state.media||request.kind==='lottery')return false;return super._request(request);}
  candidatesDisplayed(taskId,round){const t=this._state.play.tarot;
    if(!this._valid('tarot',taskId)||t.round!==round||t.phase!=='candidates_ready'||!t.open||t.paused||this._state.paused)return false;
    t.phase='awaiting_selection';this._toolEvent('牌背已展示，等待'+(t.origin==='host'?'Z':'提问观众')+'选择。');this._emitState();return true;}
  _readTarot(){this._state.play.tarot.phase='completed';this._emitState();}
  tarotDisplayed(taskId){const ok=super.tarotDisplayed(taskId);if(ok)this._toolEvent('牌面已实际展示：'+this._state.play.tarot.cards.map(c=>c.title+'正位').join('、'));return ok;}
  drawingDisplayed(taskId,revision,stage){if(this._state.play.drawing?.mode==='generated_vector')return this.drawings.displayed(taskId,revision,stage);const ok=super.drawingDisplayed(taskId,revision,stage);if(ok)this._toolEvent('画板已实际展示：'+(stage===0?'轮廓':this._state.play.drawing.title)+'，阶段'+stage);return ok;}
  finishActivity(cancel=false){const p=this._state.play,d=p.drawing;if(p.active==='drawing'&&d?.mode==='generated_vector'){if(this._state.paused||!cancel&&!['completed','failed'].includes(d.phase))return false;if(cancel)this.drawings.cancel();d.open=false;p.active=null;this._setStage('free');this._toolEvent(cancel?'当前绘画已取消。':d.phase==='failed'?'本次绘画未完成，已结束任务并回到聊天。':'草图展示已结束，可以继续聊天或随后修改这幅画。');this._emitState();return true;}const ok=super.finishActivity(cancel);if(ok)this._toolEvent(cancel?'当前才艺已取消。':'当前才艺已结束。');return ok;}
  _finishSpeech(){const speech=this._active;super._finishSpeech();if(speech){this._public('speech.completed',speech.text,{id:`speech-${speech.id}`,speakerRole:'host',speakerId:'Z',speechId:String(speech.id),status:'completed',replyTo:this.speechLinks.get(speech.id)||[]});this.speechLinks.delete(speech.id);}}
  setPaused(value){this.drawings.setPaused(value);if(value){if(this.handoffEnabled){this.audience.clearPreparation('paused');this.audiencePreview=null;}this._cancelTopic('paused');this.speechAudio?.dropWarmSpeech();this._cancelPreparation('paused');this.epoch++;this.pending?.abort();this.pending=null;this.audience.dialogue.invalidate();}super.setPaused(value);if(value)this.talent?.pause();else {this.talent?.resume();if(this.handoffEnabled&&this._active?.source==='deepseek')this.audience.setHostPreview({speechId:this._active.id,text:this._active.text});}}
  replay(){this.audiencePreview=null;this.inboxVersion=0;this._cancelTopic('replay');this.speechAudio?.dropWarmSpeech();this._cancelPreparation('replay');this.session.reset();this.speechPlan=null;this.failureStreak=0;this.epoch++;this.pending?.abort();this.pending=null;this.client?.cancel();this.audience.invalidate();this.inbox=[];this.facts=[];this.memory='';this.nextWake=0;
    this.recentExpressions=[];this.speechLinks.clear();
    this.drawings.reset();this.talent?.reset();this.facts=[];this.session.reset();const agent=this._state.agent;super.replay();this._state.media=null;this._state.play={active:null,queue:[],tarot:null,drawing:null,history:[]};this._state.stage='free';this._state.stageLabel='和 Z 聊聊天';this._state.agent=agent;this.audience.setConfig(this.audience.config);this._emitState();return true;}
  destroy(){clearInterval(this.singingPoll);this.audiencePreview=null;this._cancelTopic('destroyed');this.speechAudio?.dropWarmSpeech();this._cancelPreparation('destroyed');this.epoch++;this.pending?.abort();this.client?.cancel();this.audience.invalidate();this.drawings.reset();this.talent?.destroy();super.destroy();}
}
