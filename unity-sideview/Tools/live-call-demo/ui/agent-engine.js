import {LiveEngine} from './live-engine.js';
import {PlayEngine} from './play-engine.js';
import {AudienceDirector} from './audience-director.js';

const SKILLS = {'entertainment-tarot':'tarot','simple-drawing':'drawing'};
const canonical = kind => ({tarot:'entertainment-tarot',drawing:'simple-drawing'})[kind] || null;
const uuid = () => globalThis.crypto?.randomUUID?.() || 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g,c=>{const n=Math.random()*16|0;return (c==='x'?n:n&3|8).toString(16);});
const clip = value => String(value || '').slice(0,360);

export class AgentEngine extends PlayEngine {
  constructor(options = {}) {
    super(options);
    this.agentEpoch = 0; this.agentEnabled = false; this.agentClient = null; this.publicSerial = 0;
    this._npcBurst=0; this._nextIdleAt=12000; this._idlePending=false;
    this.policy = null; this.publicFacts = []; this.agentControllers = new Set();
    this._state.agent = {enabled:false,profile:'demo-five-skills-v1',source:'local',capabilities:[],lastDecision:null};
    this.audience = new AudienceDirector({
      request:(payload,options) => this.agentClient.generate(payload,options),
      now:() => this._clock,
      canPublish:() => this.agentEnabled && !this._destroyed && !this._state.paused && !['ended','stopped'].includes(this._state.stage),
      canAdvance:() => !this._idlePending && !this._hasPendingPlayerChat() && this._active?.scope !== 'reply',
      publish:item => this._publishNpc(item),
    });
    // The same scheduler runs with an explicit local fallback when offline.
    super.setTextGenerator(payload => this._decide(payload));
  }
  start() {
    const wasStarted=this._started;
    const result=super.start();
    if(!wasStarted&&this._started&&!this._destroyed)
      this._public('session.opened','Z 的占卜小屋正在直播，观众可以在月光弹幕里聊天。');
    return result;
  }
  configureAgent(client, config = {}) {
    this.agentClient = client; this.agentEnabled = Boolean(config.enabled); this.policy = config.policy || null;
    if (config.policy?.audience) this.audience.setConfig(config.policy.audience);
    Object.assign(this._state.agent,{enabled:this.agentEnabled,profile:config.profile||'demo-five-skills-v1',capabilities:config.capabilities||[],source:this.agentEnabled?'ready':'local'});
    super.setTextGenerator(payload => this._decide(payload));
    this._state.textMode = this.agentEnabled ? 'provider' : 'local';
    this._state.textStatus = this.agentEnabled ? 'ready' : 'local';
    this._emitState();
  }
  _public(type, value) {
    if (!value || this._destroyed) return;
    const event = {id:`public-${++this.publicSerial}`,type,text:clip(value)};
    this.publicFacts.push(event); this.publicFacts = this.publicFacts.slice(-12);
    this.audience?.record(event);
  }
  _finishSpeech() {
    const speech = this._active;
    // Release the completed reply's floor before scheduling public reactions.
    super._finishSpeech();
    // This is the director's actual completion boundary, not _say's queued draft.
    if (speech) {
      this._public('speech.completed',speech.text);
      this._nextIdleAt=this._clock+(this.policy?.host?.idleMs||12000);
      if(speech.replyTo){const original=this._state.messages.find(m=>m.id===speech.replyTo);this._npcBurst=original?.mine?0:this._npcBurst+1;}
    }
  }
  _seedChat() {
    // The continuous audience director owns the live chat cadence.
  }
  _tick() {super._tick(); if (!this._state.paused) {this.audience?.tick();this._maybeIdle();}}
  _publishNpc(item) {
    const message=this._message(item.text,{name:({wind:'晚风来信',moon:'月亮邮差',salt:'海盐汽水'})[item.npc_id],type:'chat',source:'agent-audience'});
    if(item.intent==='advance' && this._npcBurst<(this.policy?.host?.maxNpcBurst||2) && !this._state.play.active && !this._state.lottery?.open
      && !this._idlePending && !this._hasPendingPlayerChat() && this._active?.scope!=='reply'){
      this._chatCandidates.push({message:{id:message.id,name:message.name,text:message.text},history:[],score:0.1,order:this._order++});
      this._schedule(700,()=>{this._selectPlayerReply();this._pumpSpeech();},'reply-select');
    }
  }
  _maybeIdle() {
    if(!this.agentEnabled||this._idlePending||this._destroyed||this._state.play.active||this._state.lottery?.open||this._active||this._queue.length||this._hasPendingPlayerChat()||this._clock<this._nextIdleAt)return;
    this._nextIdleAt=this._clock+(this.policy?.host?.dailyCooldownMs||30000);
    if(!this.publicFacts.length)return;
    this._idlePending=true;const epoch=this.agentEpoch,phase=this._phase;
    const controller=new AbortController();this._idleController=controller;this.agentControllers.add(controller);
    this.agentClient.generate({requestId:uuid(),kind:'decision',eventId:uuid(),message:'这是宿主批准的闲聊话口。只基于已公开的内容自然接一句，最多问一个问题，不编造日常经历，不发起新才艺。',context:{...this._safeContext(),phase:'idle'}},{signal:controller.signal}).then(result=>{
      if(epoch!==this.agentEpoch||phase!==this._phase||this._destroyed||this._state.paused||this._state.play.active||this._hasPendingPlayerChat()||result.action!=='respond')return;
      this._npcBurst=0;this._say(clip(result.speech),result.mood||'warm','explain',{scope:'reaction',source:'deepseek',seed:true});
    }).catch(()=>{}).finally(()=>{this.agentControllers.delete(controller);if(epoch===this.agentEpoch){this._idlePending=false;if(this._idleController===controller)this._idleController=null;}});
  }
  _safeContext() {
    const p = this._state.play, kind = p.active, t = p[kind];
    const context = {phase:this._state.stage === 'intro'?'opening':'free',activeSkill:canonical(kind),publicEvents:this.publicFacts.slice(-8)};
    if (!t) return context;
    context.taskId = t.id; context.revision = t.revision || 0;
    if (kind === 'tarot') {
      context.phase = ['interpreting','completed'].includes(t.phase) ? t.phase : t.phase === 'awaiting_selection' ? t.phase : 'preparing';
      context.question=t.question; context.origin=t.origin;
      if (t.phase === 'awaiting_selection') context.candidates = t.candidates.map(c=>({id:c.id,label:c.label}));
      if (['interpreting','completed'].includes(t.phase)) context.cards=t.cards.map(({name,title,meaning,advice,position})=>({name,title,meaning,advice,position}));
    } else {
      context.phase = t.phase === 'completed'?'completed':'drawing';
      // Only details actually acknowledged by the display can enter model context.
      const shown = t.displayed?.split(':').map(Number);
      if (shown?.[0] === t.revision) context.drawing={title:shown[1]>=1?t.title:(t.subject==='cat'?'小猫轮廓':'小兔轮廓'),stage:shown[1],subject:t.subject,...(shown[1]>=1?{accessory:t.accessory}:{})};
    }
    return context;
  }
  _track(source, decision) {
    if (!this._state.agent) return;
    this._state.agent.source=source;
    this._state.agent.lastDecision={action:decision.action,skillId:decision.skillId||null,reasonCode:decision.reasonCode||null};
    this._emitState();
  }
  async _decide(payload) {
    const eventId=payload.replyTo||this._selectedPlayer?.id||payload.requestId, epoch=this.agentEpoch;
    const body={requestId:payload.requestId||uuid(),kind:'decision',message:payload.message,eventId,
      context:{...this._safeContext(),history:payload.history||[]}};
    let decision;
    try {
      if (!this.agentEnabled) throw new Error('offline');
      decision=await this.agentClient.generate(body);
      if (!decision || decision.selectedEventId !== eventId || !['respond','clarify','request_skill','unavailable'].includes(decision.action) || !decision.speech) throw new Error('invalid_decision');
      decision.source='deepseek';
    } catch {
      decision=this._localDecision(payload.message,eventId); decision.source='local-fallback';
    }
    if (epoch!==this.agentEpoch || this._destroyed) throw new Error('cancelled');
    if(decision.action==='request_skill' && !this._state.messages.some(m=>m.id===eventId&&m.mine)){
      decision={...decision,action:'respond',skillId:null,args:{},speech:'这个玩法建议先记下，等大家想玩的时候再来。'};
    }
    this._track(decision.source,decision);
    return {text:clip(decision.speech),mood:decision.mood||'warm',source:decision.source,decision};
  }
  _localDecision(message,eventId) {
    const base={action:'respond',selectedEventId:eventId,skillId:null,args:{},mood:'warm',reasonCode:'local_fallback',speech:''};
    const explicit = !/怎么|如何|为什么|是什么|会不会|能不能.*(?:吗|么)$/.test(message);
    if (/唱(?:一首|首|歌|一下|段)|来.*歌|想听.*(?:歌|唱)|点歌/.test(message))
      return {...base,action:'unavailable',skillId:'singing',speech:'点歌我收到了，不过现在还没有接好清唱能力，暂时唱不了。我们可以先聊聊这首歌。'};
    if (/配音|念一下|读一下|朗读|声线.*演/.test(message))
      return {...base,action:'unavailable',skillId:'voice-performance',speech:'配音的完整音频制作和检查还没有接好，暂时不能开始表演。你可以先把想演的台词告诉我。'};
    if (/(?:塔罗|占卜|抽牌|翻牌)/.test(message) && /帮我|给我|想.*(?:抽|占卜|塔罗)|^(?:塔罗|占卜)[：:]/.test(message)) {
      const question=message.replace(/^(?:塔罗|占卜)[：:]\s*/,'');
      if (/^(?:帮我|给我|我想)(?:来|做|抽|玩)?(?:个|一次|一张)?(?:塔罗|占卜|抽牌)[吧呀。！!？?]*$/.test(message))
        return {...base,action:'clarify',skillId:'entertainment-tarot',speech:'想围绕哪件事看一看？把你的问题告诉我吧。'};
      return {...base,action:'request_skill',skillId:'entertainment-tarot',args:{question,spread:/三张/.test(message)?'three':'single'},speech:'好，围绕你刚才的问题来一轮。牌背摆好后，由你自己选。'};
    }
    if (/画(?:一|个|只|幅|张)|画画|帮我画|^画/.test(message) && explicit) {
      // Only the exact supported preset is accepted locally; never silently drop constraints.
      const normalized=message.replace(/^(?:请|帮我|给我)/,'').replace(/[。！!吧呀]/g,'');
      const match=normalized.match(/^画(?:一只|个|只)?(?:戴(围巾|蝴蝶结)的)?小?(猫|兔)(?:子)?$/);
      if(match)return {...base,action:'request_skill',skillId:'simple-drawing',args:{subject:match[2]==='猫'?'cat':'rabbit',accessory:match[1]==='蝴蝶结'?'bow':'scarf'},speech:'可以，先用准备好的简笔画笔画演示给你看。'};
      return {...base,action:'unavailable',skillId:'simple-drawing',speech:'现在先能演示小猫、小兔和围巾、蝴蝶结，其他主题或细节还画不了。要不要从「点一幅画」里选一个？'};
    }
    if(this._state.play.active==='tarot' && /不像|不准|不贴/.test(message)) return {...base,speech:'不贴合就不用硬套。我们保留原牌，回到你实际遇到的事来聊。'};
    return {...base,speech:this._answer(message).text};
  }
  _applyGeneratedText(request) {
    if(!this._textRequestCurrent(request)||!request.ready)return;
    const decision=!request.ready.failed?request.ready.result?.decision:null, epoch=this.agentEpoch;
    super._applyGeneratedText(request);
    if(decision?.action!=='request_skill'||!this._state.messages.some(m=>m.id===request.replyTo&&m.mine))return;
    const utterance=this._active?.replyTo===request.replyTo?this._active:this._queue.find(item=>item.replyTo===request.replyTo);
    if(!utterance)return;
    const previous=utterance.onComplete;
    utterance.onComplete=()=>{previous?.();if(epoch===this.agentEpoch&&!this._destroyed)this._executeIntent(decision);};
  }
  _executeIntent(decision) {
    const args=decision.args||{};
    if(decision.skillId==='entertainment-tarot' && typeof args.question==='string' && args.question.trim())
      return this.requestTarot({origin:'viewer',question:args.question,spread:['three','three_reflection'].includes(args.spread)?'three':'single'});
    if(decision.skillId==='simple-drawing' && ['cat','rabbit'].includes(args.subject) && ['scarf','bow'].includes(args.accessory))
      return this.requestDrawing({subject:args.subject,accessory:args.accessory});
    this._message('这项能力还没有可执行的适配器，本次没有启动表演。'); return false;
  }
  sendMessage(value) {
    const message=String(value||'').trim();
    if(message)this._idleController?.abort();
    const direct=/^我选\s*[ABCabc左右中]$/.test(message) || /^(?:把)?(?:围巾|蝴蝶结|装饰)?(?:换|改)成(围巾|蝴蝶结)[。！!]?$/ .test(message);
    const accepted=direct?super.sendMessage(message):LiveEngine.prototype.sendMessage.call(this,message);
    if(accepted) {this._public('player.message',message);this.audience?.tick();}
    return accepted;
  }
  _candidateScore(value) {return super._candidateScore(value) + (/塔罗|占卜|画|唱|配音/.test(value)?2:0);}
  async _talent(message,kind,taskId,revision) {
    if (!this.agentEnabled) throw new Error('offline');
    const controller=new AbortController();this.agentControllers.add(controller);
    const epoch=this.agentEpoch;
    try {
      const result=await this.agentClient.generate({requestId:uuid(),kind:'talent',message:clip(message),eventId:taskId,context:this._safeContext()},{signal:controller.signal});
      if(epoch!==this.agentEpoch||!this._valid(kind,taskId,revision))throw new Error('stale_task');
      this._track('deepseek',result);return result;
    } finally {this.agentControllers.delete(controller);}
  }
  _line(kind,taskId,line,done,revision) {
    const task=this._state.play[kind];
    const eligible=this.agentEnabled && (kind==='tarot'&&task?.phase==='interpreting' || kind==='drawing'&&task?.phase==='completed');
    if(!eligible)return super._line(kind,taskId,line,done,revision);
    const token=`${taskId}:${revision||0}:${task.readCount??task.stage}:${line}`;
    if(this._talentLine===token)return;
    this._talentLine=token;
    this._talent(`本次需要表达的内容：${line}`,kind,taskId,revision).then(result=>{
      if(!['interpret','follow_up'].includes(result.action)||!result.speech)throw new Error('invalid_talent');
      return {speech:result.speech,source:'deepseek'};
    }).catch(()=>({speech:line,source:'local-fallback'})).then(result=>{
      if(this._talentLine!==token||!this._valid(kind,taskId,revision))return;
      this._talentLine=null;
      if(result.source==='local-fallback')this._track(result.source,{action:'follow_up'});
      this._say(clip(result.speech),'warm','explain',{source:result.source,onComplete:()=>{if(this._valid(kind,taskId,revision))done?.();}});
    });
  }
  candidatesDisplayed(taskId,round) {
    const t=this._state.play.tarot;
    if(!this._valid('tarot',taskId)||t.round!==round||t.phase!=='candidates_ready'||!t.open||t.paused||this._state.paused)return false;
    this._public('tarot.candidates.displayed',`${t.origin==='host'?'Z':'观众'}的第 ${round} 轮牌背已显示：${t.candidates.map(c=>c.label).join('、')}`);
    if(t.origin==='viewer') {
      // Apply the package's configured reminder and pause semantics.
      t.phase='awaiting_selection';this._emitState();
      this._line('tarot',taskId,'轮到你啦，从这三个牌背里点一张。');
      const config=this.policy?.skills?.['entertainment-tarot'];
      this._later('tarot',taskId,config?.selectionReminderAfterMs||15000,()=>{
        if(t.round===round&&t.phase==='awaiting_selection'&&!t.paused){t.reminded=true;this._line('tarot',taskId,'还没选好也没关系，等你自己决定。');this._emitState();}
      });
      this._later('tarot',taskId,config?.selectionWaitMs||45000,()=>{
        if(t.round===round&&t.phase==='awaiting_selection'&&!t.paused){t.paused=true;this._message('本轮已暂停，牌背保持不变。点「继续选牌」后再选。');this._emitState();}
      });
      return true;
    }
    if(!this.agentEnabled)return super.candidatesDisplayed(taskId,round);
    t.phase='awaiting_selection';this._emitState();
    const epoch=this.agentEpoch;
    this._talent('这是 Z 自己的问题，请从已经显示的牌背中选择一个候选位置。','tarot',taskId).then(result=>{
      if(result.action!=='choose_host'||!t.candidates.some(c=>c.id===result.candidateId))throw new Error('invalid_choice');
      return result.candidateId;
    }).catch(()=>{if(epoch===this.agentEpoch)this._track('local-fallback',{action:'choose_host'});return t.candidates[Math.floor(Math.random()*3)]?.id;}).then(candidateId=>{
      if(epoch!==this.agentEpoch||!this._valid('tarot',taskId)||t.round!==round||t.phase!=='awaiting_selection')return;
      const candidate=t.candidates.find(c=>c.id===candidateId);if(!candidate)return;
      super._line('tarot',taskId,`我选${candidate.label==='中'?'中间':candidate.label+'边'}这张。`,()=>this._chooseTarot(taskId,round,candidateId,'host'));
    });
    return true;
  }
  tarotDisplayed(taskId) {
    const t=this._state.play.tarot;
    if(!this._valid('tarot',taskId)||t.phase!=='revealing'||!t.open||this._state.paused||t.paused)return false;
    this._public('tarot.displayed',t.cards.map(c=>`${c.position}：${c.title}正位`).join('；'));
    return super.tarotDisplayed(taskId);
  }
  drawingDisplayed(taskId,revision,stage) {
    const d=this._state.play.drawing;
    if(!this._valid('drawing',taskId,revision)||this._state.paused||!d.open||d.stage!==stage||stage<0||d.displayed===`${revision}:${stage}`)return false;
    this._public('drawing.displayed',stage===0?`${d.subject==='cat'?'小猫':'小兔'}的轮廓已显示`:`${d.title}，${stage===2?'完成图':'五官和装饰'}已显示`);
    return super.drawingDisplayed(taskId,revision,stage);
  }
  finishActivity(cancel=false) {
    const result=super.finishActivity(cancel);
    if(result&&!this._state.play.active){this._talentLine=null;for(const controller of this.agentControllers)controller.abort();}
    return result;
  }
  _cancelAgentWork() {this.agentEpoch++;this._idlePending=false;this._talentLine=null;this.agentClient?.cancel();for(const c of this.agentControllers)c.abort();this.agentControllers.clear();this.audience?.invalidate();}
  replay() {
    this._cancelAgentWork();this.publicFacts=[];this.publicSerial=0;
    this._npcBurst=0;this._nextIdleAt=this.policy?.host?.idleMs||12000;
    const agent=this._state.agent;const result=super.replay();
    // The base engine resets its clock to zero; rebase the continuous cadence.
    this.audience?.setConfig(this.policy?.audience||{});
    this._state.agent={...agent,lastDecision:null};this._emitState();return result;
  }
  destroy(){this._cancelAgentWork();super.destroy();}
}
