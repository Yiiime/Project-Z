import {ConversationEngine} from './conversation-engine.js';
import {CALL_STORY} from './call-contract.js';

// One host, one speech owner. Call mode only changes scheduling and context.
export class CallConversationEngine extends ConversationEngine {
  constructor(){super();const mayPublish=this.audience.canPublish,mayAdvance=this.audience.canAdvance;this.callAudienceBudget=0;this.callContinuationBudget=0;this.audience.canPublish=()=>mayPublish()&&(!this._state.call?.active||this.callAudienceBudget>0&&this._state.call.input==='idle');this.audience.canAdvance=()=>mayAdvance()&&(!this._state.call?.active||this._state.call.audienceUntil>this._clock);this.callVersion=0;this.callNeedsTurn=false;this.callReviewRetries=0;this.callAudience=[];this._state.call=this._emptyCall();}
  _emptyCall(){return {active:false,id:null,input:'idle',joinedAt:null,story:null,audienceUntil:0,turns:0};}
  joinCall(){
    if(this._destroyed||this._state.paused||!this.enabled||this._state.call.active)return false;
    this.interruptCall('join');this._cancelCallActivity();
    this.savedCallPolicy={handoff:this.handoffEnabled,topic:this.topicEnabled,audience:{...this.audience.config}};this.audience.setConfig({...this.audience.config,handoff:false,intervalMs:6000,publishGapMs:5000,maxConcurrent:1});this.callAudienceBudget=2;this.handoffEnabled=false;this.topicEnabled=false;
    this.inbox=this.inbox.filter(m=>m.mine);this.audience.dialogue.setWaiting(this.inbox);
    this.callAudience=[];this._state.call={...this._emptyCall(),active:true,id:crypto.randomUUID(),joinedAt:Date.now()};
    this._public('call.joined','一位来客进入了连麦席，当前尚未提供语音；先打招呼，陪伴聊天为主。');
    this._wakeCall();this._emitState();return true;
  }
  _wakeCall(){this.callNeedsTurn=true;this.callVersion++;this.nextWake=this._clock;}
  interruptCall(reason='guest_interrupt'){
    const speech=this._active;
    this.epoch++;this.pending?.abort();this.pending=null;this._cancelPreparation(reason);this._cancelTopic(reason);
    this.audience.clearPreparation(reason);this.audiencePreview=null;this.audience.dialogue.invalidate();
    this.speechAudio?.cancel();this._active=null;this._queue=[];this._continuations=[];this.speechPlan=null;
    if(speech){this.speechLinks.delete(speech.id);this._public('call.interrupted','主播发言被打断；只有此前 speech.progress 的部分已播出。');}
    Object.assign(this._state,{currentSpeech:'',currentSpeechId:null,gesture:'none'});
    if(this._state.call?.active&&this._state.media){this.talent?.cancel('call_interrupt');this.talent?.finish(true);}
    this.callNeedsTurn=false;this.callContinuationBudget=0;this.callVersion++;this._emitState();return true;
  }
  _cancelCallActivity(){if(this._state.media){this.talent?.cancel('call_boundary');this.talent?.finish(true);}if(this._state.play.active)this.finishActivity(true);}
  endCall(){
    if(!this._state.call.active)return false;
    this.interruptCall('hangup');this._cancelCallActivity();this._state.call=this._emptyCall();this.callAudience=[];
    if(this.savedCallPolicy?.audience)this.audience.setConfig(this.savedCallPolicy.audience);this.handoffEnabled=this.savedCallPolicy?.handoff??false;this.topicEnabled=this.savedCallPolicy?.topic??false;
    // Never answer an abandoned guest turn after hangup.
    for(const m of this.inbox)this._setReceipt(m.id,'seen');this.inbox=[];this.audience.dialogue.setWaiting([]);
    this._public('call.ended','来客已经离开连麦席，连麦结束。自然回到直播间。');this.nextWake=this._clock;this._emitState();return true;
  }
  setCallInput(input){
    if(!this._state.call.active||!['idle','listening','reviewing'].includes(input))return false;
    if(input!=='idle')this.interruptCall('guest_input');
    this._state.call.input=input;this._emitState();return true;
  }
  sendMessage(value){
    if(!this._state.call.active)return super.sendMessage(value);
    const text=String(value||'').trim();if(!text||text.length>360||this._destroyed||this._state.paused)return false;
    this.interruptCall('guest_message');this._state.call.input='idle';this._state.call.turns++;this.callReviewRetries=0;this.callAudienceBudget=2;this.callContinuationBudget=1;
    const m=this._message(text,{name:'连麦来客',mine:true,type:'chat'});this._receive(m,true);this._wakeCall();return true;
  }
  _receive(m,mine,speakerId){
    if(this._state.call?.active&&!mine){
      this.callAudienceBudget=Math.max(0,this.callAudienceBudget-1);this.callAudience.push({...m,speakerId});this.callAudience=this.callAudience.slice(-6);
      this._public('npc.message',m.text,{id:m.id,speakerRole:'audience',speakerId:speakerId||m.name});
      // Visible reactions never seize the guest's turn. Only an explicit window can admit one.
      if(this._state.call.audienceUntil>this._clock&&!this.inbox.some(x=>x.mine)&&!this._active&&!this.pending){
        this.inbox.push({id:m.id,name:m.name,text:m.text,mine:false});this._state.call.audienceUntil=0;this.audience.dialogue.setWaiting(this.inbox);this._wakeCall();
      }
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
  _hostContext(inbox,turn){const context=super._hostContext(inbox,turn),c=this._state.call;
    if(c.active)context.call={active:true,id:c.id,input:c.input,turns:c.turns,audienceOpen:c.audienceUntil>this._clock,story:c.story?{id:c.story.id,step:c.story.step,status:c.story.status,choices:[...c.story.choices]}:null};return context;
  }
  _canPresentMedia(){return !this._state.call?.active||this._state.call.input==='idle';}
  _pumpSpeech(){if(this._canPresentMedia())super._pumpSpeech();}
  _maybePrepare(){if(!this._state.call?.active)super._maybePrepare();}
  _maybeAssessTopic(){if(!this._state.call?.active)super._maybeAssessTopic();}
  async _think(){
    if(!this._state.call?.active)return super._think();
    if(!this._canPresentMedia()||!this.callNeedsTurn||!this.enabled||!this._started||this.pending||this._active||this._queue.length||this._state.paused||this._destroyed||this._clock<this.nextWake)return;
    const version=this.callVersion,speechId=this._speechId;this.callNeedsTurn=false;const thinking=super._think();this._emitState();await thinking;
    if(this._state.call.active&&version===this.callVersion){if(this.failureStreak)this.callNeedsTurn=true;else if(this._speechId===speechId&&this.inbox.some(m=>m.mine)&&!this._state.media&&this.callReviewRetries++<2)this.callNeedsTurn=true;}
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
