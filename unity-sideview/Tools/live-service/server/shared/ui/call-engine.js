import {approveCallCandidate} from './call-review.js';
import {ConversationEngine} from './conversation-engine.js';
import {CALL_STORY} from './call-contract.js';

// One host, one speech owner. Call mode only changes scheduling and context.
export class CallConversationEngine extends ConversationEngine {
  constructor(){super();const mayCommit=this.audience.canCommit,mayAdvance=this.audience.canAdvance;this.callContinuationBudget=0;this.audience.canCommit=()=>this._state.call?.active||mayCommit();this.audience.canAdvance=()=>mayAdvance()&&(!this._state.call?.active||this._state.call.input==='idle'&&this._state.call.audienceUntil>this._clock);this.callVersion=0;this.callNeedsTurn=false;this.callReviewRetries=0;this.callAudience=[];this._state.call=this._emptyCall();}
  _emptyCall(){return {active:false,id:null,input:'idle',joinedAt:null,story:null,audienceUntil:0,turns:0};}
  joinCall(){
    if(this._destroyed||this._state.paused||!this.enabled||this._state.call.active)return false;
    this.interruptCall('join');this._cancelCallActivity();
    this.savedCallPolicy={handoff:this.handoffEnabled,topic:this.topicEnabled,audience:{...this.audience.config}};this.audience.setConfig({...this.audience.config,handoff:false,intervalMs:5000,publishGapMs:5000,maxConcurrent:1});this.audience.dialogue.setConcurrent(true);this.handoffEnabled=false;this.topicEnabled=false;
    this.inbox=this.inbox.filter(m=>m.mine);this.audience.dialogue.setWaiting(this.inbox);
    this.callAudience=[];this.callAudienceUsed=new Set();this.callAudienceSpeech=new Map();this.callAudienceNextAt=this._clock+20000;this.callAudienceLastTurn=0;this._state.call={...this._emptyCall(),active:true,id:crypto.randomUUID(),joinedAt:Date.now()};
    this._public('call.joined','一位来客进入了连麦席，当前尚未提供语音；先打招呼，陪伴聊天为主。');
    this._wakeCall();this._emitState();return true;
  }
  _wakeCall(){this.callNeedsTurn=true;this.callVersion++;this.nextWake=this._clock;}
  interruptCall(reason='guest_interrupt'){
    this.callRevisionFeedback=null;this.callReviewBlocked=false;this._cancelCallQuality();
    if(['join','hangup'].includes(reason))this.callQualityFeedback=[];
    this.callYieldVersion=(this.callYieldVersion||0)+1;this.callYield=null;
    const speech=this._active;if(speech)this.callAudienceSpeech?.delete(speech.id);
    this.epoch++;this.pending?.abort();this.pending=null;this._cancelPreparation(reason);this._cancelTopic(reason);
    if(!this._state.call?.active||['hangup','paused'].includes(reason))this.audience.clearPreparation(reason);
    this.audiencePreview=null;this.audience.dialogue.invalidate(this._state.call?.active&&!['hangup','paused'].includes(reason)?'host':null);
    this.speechAudio?.cancel();this._active=null;this._queue=[];this._continuations=[];this.speechPlan=null;
    if(speech){this.speechLinks.delete(speech.id);this._public('call.interrupted','主播发言被打断；只有此前 speech.progress 的部分已播出。');}
    Object.assign(this._state,{currentSpeech:'',currentSpeechId:null,gesture:'none'});
    if(this._state.call?.active&&this._state.media){this.talent?.cancel('call_interrupt');this.talent?.finish(true);}
    this.callNeedsTurn=false;this.callContinuationBudget=0;this.callVersion++;this._emitState();return true;
  }
  async yieldCallFloor(){
    const c=this._state.call;if(!c.active||this._state.paused||this._destroyed)return false;
    if(this.callYield)return true;
    if(!this._active){this.setCallInput('listening');return true;}
    const version=this.callYieldVersion=(this.callYieldVersion||0)+1,callId=c.id;
    this.callYield={phase:'fading'};c.input='listening';
    // Stop the old plan immediately, but allow its last audible prefix to fade.
    this.epoch++;this.pending?.abort();this.pending=null;this.callNeedsTurn=false;
    this._active.onComplete=null;this._queue=[];this._continuations=[];this.speechPlan=null;
    this._cancelPreparation('guest_voice');this._cancelTopic('guest_voice');this.audience.dialogue.invalidate('host');this._emitState();
    try{await this.speechAudio?.fadeOut?.(180);}catch{}
    if(version!==this.callYieldVersion||!this._state.call.active||this._state.call.id!==callId||this._state.paused||this._destroyed)return false;
    const nextInput=this._state.call.input,needsReply=this.callNeedsTurn;
    this.interruptCall('soft_yield');this._state.call.input=nextInput;
    if(needsReply)this._wakeCall();
    // Yield silently: no extra speech or acknowledgement gate before replying.
    this._emitState();this._event('call-listening',{callId:c.id});
    return true;
  }
  _finishSpeech(){const yielded=this._active?.source==='call-yield',selected=this.callAudienceSpeech?.get(this._active?.id);if(selected){for(const id of selected)this.callAudienceUsed.add(id);this.callAudienceSpeech.delete(this._active.id);this.callAudienceLastTurn=this._state.call.turns;this.callAudienceNextAt=this._clock+20000;}super._finishSpeech();if(yielded){this.callYield=null;this._emitState();}}
  _cancelCallActivity(){if(this._state.media){this.talent?.cancel('call_boundary');this.talent?.finish(true);}if(this._state.play.active)this.finishActivity(true);}
  endCall(){
    if(!this._state.call.active)return false;
    this.interruptCall('hangup');this._cancelCallActivity();this._state.call=this._emptyCall();this.callAudience=[];this.audience.dialogue.setConcurrent(false);
    if(this.savedCallPolicy?.audience)this.audience.setConfig(this.savedCallPolicy.audience);this.handoffEnabled=this.savedCallPolicy?.handoff??false;this.topicEnabled=this.savedCallPolicy?.topic??false;
    // Never answer an abandoned guest turn after hangup.
    for(const m of this.inbox)this._setReceipt(m.id,'seen');this.inbox=[];this.audience.dialogue.setWaiting([]);
    this._public('call.ended','来客已经离开连麦席，连麦结束。自然回到直播间。');this.nextWake=this._clock;this._emitState();return true;
  }
  setCallInput(input){
    if(!this._state.call.active||!['idle','listening','reviewing'].includes(input))return false;
    if(this._state.call.input===input)return true;
    if(this.callYield){this._state.call.input=input;this._emitState();return true;}
    // Only confirmed speech / explicit editing takes the floor; merely arming ASR does not call this.
    if(input!=='idle')this.interruptCall('guest_input');
    this._state.call.input=input;this._emitState();return true;
  }
  sendMessage(value){
    if(!this._state.call.active)return super.sendMessage(value);
    const text=String(value||'').trim();if(!text||text.length>360||this._destroyed||this._state.paused)return false;
    if(/(?:弹幕.{0,6}(?:怎么|想法|意见|建议)|(?:看看|问问|读|听听|回应).{0,4}(?:弹幕|大家|观众))/.test(text))this._state.call.audienceUntil=this._clock+15000;
    if(!this.callYield)this.interruptCall('guest_message');this._state.call.input='idle';this._state.call.turns++;this.callReviewRetries=0;this.callContinuationBudget=1;
    const m=this._message(text,{name:'连麦来客',mine:true,type:'chat'});this._receive(m,true);this._wakeCall();return true;
  }
  _receive(m,mine,speakerId){
    if(this._state.call?.active&&!mine){
      this.callAudience.push({...m,speakerId,at:this._clock});this.callAudience=this.callAudience.slice(-12);
      this._public('npc.message',m.text,{id:m.id,speakerRole:'audience',speakerId:speakerId||m.name});
      // Reactions are public evidence, not additional guest requests. The host scheduler chooses a safe gap.
      this._emitState();return;
    }
    super._receive(m,mine,speakerId);if(this._state.call?.active&&mine)this._wakeCall();
  }
  inviteAudience(){
    if(!this._state.call.active||this._state.paused)return false;
    this._state.call.audienceUntil=this._clock+12000;
    return this.sendMessage('我想听听弹幕的想法，挑一条和我们刚才话题有关的回应，再回来继续聊。');
  }
  offerCallStory(){
    const c=this._state.call;if(!c.active||c.story||this._state.play.active||this._state.media)return false;
    c.story={id:CALL_STORY.id,step:0,status:'offered',choices:[],revision:1};
    this._public('call.story.offered','明信片的可选话题卡已展示，等待连麦来客选择，也可跳过。');this._emitState();return true;
  }
  chooseCallStory(choice,revision){
    const c=this._state.call,s=c.story;if(!c.active||this._state.paused||!s||s.status!=='offered'||s.revision!==revision)return false;
    const item=CALL_STORY.steps[s.step]?.choices.find(x=>x.id===choice);if(!item)return false;
    this.interruptCall('story_choice');c.input='idle';s.choices.push(choice);s.revision++;
    if(choice==='skip')s.status='skipped';else if(s.step===CALL_STORY.steps.length-1)s.status='completed';else s.step++;
    this._public('call.story.choice',item.result+' 这是连麦小游戏，不改变主线剧情，也不表示已生成图片。');
    this._wakeCall();this._emitState();return true;
  }
  _callAudienceEligible(){const c=this._state.call;return this.enabled&&!this._destroyed&&!this._state.paused&&c?.active&&c.input==='idle'&&!this.callYield&&!this._mediaPlaying()&&(c.audienceUntil>this._clock||(c.turns-this.callAudienceLastTurn>=2&&this._clock>=this.callAudienceNextAt));}
  _callAudienceCandidates(context){
    if(!this._callAudienceEligible())return [];
    const evidence=[...context.publicEvents,...(context.host.session?.evidence||[])],seen=new Set(),names={wind:'晚风来信',moon:'月亮邮差',salt:'海盐汽水'};
    const topic=context.host.inbox.filter(m=>m.mine).map(m=>m.text).join(' ')+(context.host.session?.evidence||[]).filter(e=>e.type==='player.message').slice(-2).map(e=>e.text).join(' ');
    const pairs=text=>new Set(Array.from({length:Math.max(0,text.length-1)},(_,i)=>text.slice(i,i+2)));const terms=pairs(topic);
    return this.callAudience.filter(m=>{if(this._clock-m.at>60000||this.callAudienceUsed.has(m.id)||!names[m.speakerId]||!evidence.some(e=>e.type==='npc.message'&&e.id===m.id&&e.text===m.text&&e.speakerId===m.speakerId))return false;const text=m.text.replace(/\s|[，。！？]/g,'');if(seen.has(text))return false;seen.add(text);return true;})
      .map(m=>({...m,score:[...pairs(m.text)].filter(t=>terms.has(t)).length})).sort((a,b)=>b.score-a.score||b.at-a.at).slice(0,5).map(m=>({id:m.id,npcId:m.speakerId,name:names[m.speakerId],text:m.text}));
  }
  _hostContext(inbox,turn){const context=super._hostContext(inbox,turn),c=this._state.call;
    if(c.active){const audienceCandidates=this._callAudienceCandidates(context);context.call={active:true,id:c.id,input:c.input,turns:c.turns,audienceOpen:audienceCandidates.length>0,audienceCandidates,audienceReason:audienceCandidates.length?(c.audienceUntil>this._clock?'invited':'cadence'):'none',story:c.story?{id:c.story.id,step:c.story.step,status:c.story.status,choices:[...c.story.choices]}:null};
      if(audienceCandidates.length){this.callAudienceNextAt=this._clock+20000;c.audienceUntil=0;}
    }if(c.active&&this.callRevisionFeedback)context.host.revisionFeedback={...this.callRevisionFeedback};if(c.active&&this.callQualityFeedback?.length)context.host.qualityFeedback=[...this.callQualityFeedback];return context;
  }
  _validateCallReply(result,context){if(!context.call?.active)return;const ids=result.audienceMessageIds||[];
    const latest=context.host.inbox.findLast(m=>m.mine);
    if(!Array.isArray(ids)||ids.length>1||ids.some(id=>!context.call.audienceCandidates.some(m=>m.id===id))||ids.length&&result.action!=='speak'||result.action!=='wait'&&latest&&!result.handledIds.includes(latest.id))throw Error('invalid_call_reply');
  }
  _onHostSpeechCommitted(speechId,result){this.callRevisionFeedback=null;if(this._state.call?.active&&result.audienceMessageIds?.length)this.callAudienceSpeech.set(speechId,[...result.audienceMessageIds]);}
  _warmCandidate(requestId,result){
    if(this._state.call?.active&&['speak','tool'].includes(result?.action)&&result.speech)return this.speechAudio?.warmSpeech(requestId,result.speech,result.mood,{streaming:true});
    return super._warmCandidate(requestId,result);
  }
  _approveCallCandidate(options){return approveCallCandidate(this,options);}
  retryReply(){this.callReviewBlocked=false;return super.retryReply();}
  _cancelCallQuality(){clearTimeout(this.callQualityTimer);this.callQualityTimer=null;this.callQualityWork?.abort();this.callQualityWork=null;}
  _onHostReplyCompleted(result,context){
    if(!this._state.call?.active||!context.call?.active)return;
    this._cancelCallQuality();const callId=this._state.call.id,epoch=this.epoch;
    this.callQualityTimer=setTimeout(async()=>{
      this.callQualityTimer=null;if(this.epoch!==epoch||!this._state.call.active||this._state.paused||this._destroyed||this.pending||this._active||this._state.call.input!=='idle')return;
      const work=this.callQualityWork=new AbortController();
      try{const reply=await this.client.generate({requestId:crypto.randomUUID(),kind:'quality',eventId:crypto.randomUUID(),message:'评估刚刚实际播完的整轮表达',context:{publicEvents:this.facts.slice(-12),playerMessages:context.host.inbox.filter(m=>m.mine).map(m=>m.text),reply:result.speech}},{signal:work.signal});
        if(!work.signal.aborted&&this.epoch===epoch&&this._state.call.id===callId){this.callQualityFeedback=(reply.issues||[]).filter(x=>['repetitive','over_questioning','overlong','missed_request'].includes(x)).slice(0,2);this.audience.dialogue.log({stage:'host.quality',issues:this.callQualityFeedback});}
      }catch{}finally{if(this.callQualityWork===work)this.callQualityWork=null;}
    },500);this.callQualityTimer.unref?.();
  }
  _beginSentenceReply({controller,epoch,inbox,requestId,context,generationContext}){
    if(!this._state.call?.active||context.activeSkill||context.media||!this.client.sentenceStreaming)return null;
    const signal=controller.signal,callId=this._state.call.id,received=[],spoken=[],owned=new Set();let chain=Promise.resolve(),failure=null,stopped=false,warmKey=null,waitCancel=null,resolveGeneration,repairedAny=false;const generationDone=new Promise(r=>resolveGeneration=r),budget={remaining:1};
    const valid=()=>!stopped&&!signal.aborted&&epoch===this.epoch&&!this._destroyed&&!this._state.paused&&this._state.call?.id===callId&&this._state.call.input==='idle';
    const abort=()=>{
      stopped=true;resolveGeneration();waitCancel?.();if(warmKey)this.speechAudio?.dropWarmSpeech(warmKey);
      // A voice interruption owns its 180 ms fade; aborting generation must not cut it short.
      if(this._active&&owned.has(this._active.id)&&this.callYield?.phase!=='fading'){this._active.onComplete=null;this.speechLinks.delete(this._active.id);this._active=null;this.speechPlan=null;this._queue=[];Object.assign(this._state,{currentSpeech:'',currentSpeechId:null,gesture:'none'});this.speechAudio?.cancel();this._emitState();}
    };
    signal.addEventListener('abort',abort,{once:true});
    const play=async sentence=>{
      if(!valid())throw Error('stale_sentence');
      const result={action:'speak',speech:sentence.text,mood:sentence.mood,gesture:sentence.gesture,transition:sentence.transition,handledIds:sentence.handledIds,audienceMessageIds:sentence.audienceMessageIds};
      this._validateCallReply(result,context);
      const initial={id:requestId+'-s'+sentence.index,role:'host',speakerId:'Z',text:sentence.text,intent:'speak',replyTo:sentence.handledIds,evidenceIds:[],segment:{index:sentence.index,scope:'sentence'}};
      warmKey=initial.id;
      const approved=await this._approveCallCandidate({candidate:initial,result,context,signal,valid,budget,beforeRepair:()=>generationDone});
      const {ticket,candidate}=approved;const candidateId=candidate.id;warmKey=candidateId;repairedAny||=approved.repaired;
      const spokenText=candidate.text;
      if(!valid()||this._active||this._queue.length)throw Object.assign(Error('sentence_not_current'),{code:'agent_reply_stale'});
      await new Promise((resolve,reject)=>{
        let timer;const cancel=()=>{waitCancel=null;clearTimeout(timer);signal.removeEventListener('abort',cancel);reject(Error('sentence_cancelled'));};waitCancel=cancel;signal.addEventListener('abort',cancel,{once:true});
        timer=setTimeout(cancel,90000);
        const committed=this.audience.dialogue.commit(ticket,()=>{
          const speechId=this._speechId+1;owned.add(speechId);this.speechLinks.set(speechId,[...sentence.handledIds]);
          this._say(spokenText,sentence.mood,sentence.index===0?sentence.gesture:'none',{source:'deepseek',transition:sentence.transition,preparedAudioKey:candidateId,onComplete:()=>{
            waitCancel=null;clearTimeout(timer);signal.removeEventListener('abort',cancel);spoken.push({speechId,text:spokenText});resolve();
          }});
          warmKey=null;
        });
        if(!committed)cancel();
      });
    };
    return {
      received:()=>received.length>0,
      onSentence:sentence=>{
        if(!valid())return;if(sentence.index!==received.length||received.length>=6)throw Error('invalid_sentence_index');
        received.push(sentence);this.audience.dialogue.log({stage:'host.sentence.received',requestId,index:sentence.index,chars:sentence.text.length});
        chain=chain.then(()=>{if(failure)throw failure;return play(sentence);}).catch(error=>{failure=error;abort();});
      },
      finish:async(result,agendaUpdates)=>{
        resolveGeneration();
        if(result.action!=='speak'||received.map(s=>s.text).join('').replace(/\s/g,'')!==result.speech.replace(/\s/g,''))throw Error('sentence_final_mismatch');
        await chain;if(failure)throw failure;if(!valid()||spoken.length!==received.length)throw Error('sentence_incomplete');
        this.callRevisionFeedback=null;
        const handled=new Set(result.handledIds);for(const mid of handled)this._setReceipt(mid,'replied');this.inbox=this.inbox.filter(m=>!handled.has(m.id));this.audience.dialogue.setWaiting(this.inbox);
        const last=spoken.at(-1),completed=this.session.events.find(e=>e.id===`speech-${last.speechId}`&&e.type==='speech.completed');
        if(!completed)throw Error('sentence_not_played');
        if(!repairedAny){this.session.apply(agendaUpdates,context.host.session,completed,!handled.size);this.memory=String(result.memory||'').slice(0,800);}
        result={...result,speech:spoken.map(s=>s.text).join('')};this._onHostReplyCompleted(result,context);
        for(const mid of result.audienceMessageIds||[])this.callAudienceUsed.add(mid);
        if(result.audienceMessageIds?.length){this.callAudienceLastTurn=this._state.call.turns;this.callAudienceNextAt=this._clock+20000;}
        this.speechPlan={speechId:last.speechId,epoch,result,attempted:false,inboxVersion:this.inboxVersion,completedAt:this._clock};
        this.audience.dialogue.log({stage:'host.sentences.completed',requestId,count:spoken.length});
      },abort,dispose:()=>signal.removeEventListener('abort',abort),
    };
  }
  _tick(...args){super._tick(...args);if(this._callAudienceEligible()&&!this.pending&&!this._active&&!this._queue.length&&!this.callNeedsTurn&&!this.inbox.some(m=>m.mine)&&this.callAudience.some(m=>!this.callAudienceUsed.has(m.id)&&this._clock-m.at<=60000)){if(this._callAudienceCandidates(super._hostContext(this.inbox)).length)this._wakeCall();}}
  _canPresentMedia(){return !this.callYield&&(!this._state.call?.active||this._state.call.input==='idle');}
  _pumpSpeech(){if(this.callYield){if(this.callYield.phase==='acknowledging'&&this._queue[0]?.source==='call-yield')super._pumpSpeech();return;}if(this._canPresentMedia())super._pumpSpeech();}
  _maybePrepare(){if(!this._state.call?.active)super._maybePrepare();}
  _maybeAssessTopic(){if(!this._state.call?.active)super._maybeAssessTopic();}
  async _think(){
    if(!this._state.call?.active)return super._think();
    if(this.callReviewBlocked||this.callYield||!this._canPresentMedia()||!this.callNeedsTurn||!this.enabled||!this._started||this.pending||this._active||this._queue.length||this._state.paused||this._destroyed||this._clock<this.nextWake)return;
    this._cancelCallQuality();const version=this.callVersion,speechId=this._speechId;this.callNeedsTurn=false;const thinking=super._think();this._emitState();await thinking;
    if(this._state.call.active&&version===this.callVersion){if(this.failureStreak)this.callNeedsTurn=this.failureStreak<3;else if(this._speechId===speechId&&this.inbox.some(m=>m.mine)&&!this._state.media&&this.callReviewRetries++<2)this.callNeedsTurn=true;}
  }
  _wakeDelay(result){
    const delay=super._wakeDelay(result);
    if(this._state.call?.active&&result===this.speechPlan?.result&&this.speechPlan.completedAt!==undefined&&result.continuation==='continue'&&this.callContinuationBudget>0){
      this.callContinuationBudget--;this.callNeedsTurn=true;return Math.min(350,delay);
    }
    return delay;
  }
  _toolEvent(text){super._toolEvent(text);if(this._state.call?.active)this._wakeCall();}
  _public(type,text,metadata={}){super._public(type,text,metadata);if(this._state.call?.active&&/^(talent\.(completed|failed|interrupted)|drawing\.)/.test(type))this._wakeCall();}
  async _pollSinging(){const previous=this.singingSnapshot;await super._pollSinging();if(this._state.call?.active&&previous!==this.singingSnapshot)this._wakeCall();}
  _executeTool(tool){
    if(tool?.name==='offer_call_story'){const ok=this.offerCallStory();this._toolEvent(JSON.stringify({tool:tool.name,status:ok?'offered':'unavailable'}));return ok;}
    if(this._state.call?.active&&!this._canPresentMedia())return false;
    return super._executeTool(tool);
  }
  setPaused(value){if(value&&this._state.call?.active){this.interruptCall('paused');this._state.call.input='idle';}super.setPaused(value);}
  replay(){if(this._state.call?.active)this.endCall();const ok=super.replay();this._state.call=this._emptyCall();return ok;}
  destroy(){if(this._state.call?.active)this.endCall();super.destroy();}
  exportDialogueTrace(){return {...super.exportDialogueTrace(),call:{...this._state.call,needsTurn:this.callNeedsTurn}};}
}
