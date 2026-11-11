import {DialogueReview} from './dialogue-review.js';
/** Continuous audience cadence. Public facts and one explicitly marked committed host preview stay separate. */
import {AUDIENCE_NAMES} from '../audience-roster.js';
const NPC_IDS = new Set(Object.keys(AUDIENCE_NAMES));
const INTENTS = new Set(['reaction', 'atmosphere', 'advance']);
const DEFAULTS = Object.freeze({intervalMs: 6000, generationDeadlineMs: 1500, firstPublishDelayMs: 0,
  candidateTtlMs: 6000, npcPublishCooldownMs: 15000, maxChars: 36, maxConcurrent: 1, maxPoolSize: 1, publishGapMs: 0, handoff: false});
const uuid = () => globalThis.crypto?.randomUUID?.() || 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
  const n = Math.floor(Math.random() * 16); return (c === 'x' ? n : (n & 3) | 8).toString(16);
});
const copyEvent = event => JSON.parse(JSON.stringify(event));

export class AudienceDirector {
  constructor({request, publish, now = () => Date.now(), phase = () => 'chat', random = Math.random, config = {}, canPublish = () => true, canAdvance = () => true, canCommit = () => true, canPreview = () => true, onSelected = () => {}, onSelectionEnd = () => {}} = {}) {
    if (typeof request !== 'function' || typeof publish !== 'function') throw new TypeError('AudienceDirector requires request and publish callbacks');
    this.phase=phase;this.random=random;this.attempted=new Map();this.currentPhase=null;
    this.request = request; this.publish = publish; this.now = now;
    this.canPublish = canPublish; this.canAdvance = canAdvance; this.canCommit=canCommit;this.canPreview=canPreview;this.onSelected=onSelected;this.onSelectionEnd=onSelectionEnd;this.selected=null;this.hostPreview=null;
    this.dialogue=new DialogueReview({request,now}); this.reviewPending=null;
    this.events = []; this.requests = new Map(); this.pool = []; this.candidates = []; this.generation = 0; this.nextPublishAt = 0;
    this.cooldowns = new Map(); this.recentTexts = []; this.recentMessages = []; this.seenEvents = [];
    this.lastRequestAt = -Infinity; this.nextRequestAt = this.now();
    this.setConfig(config);
  }
  setConfig(policyAudience = {}) {
    const config = {...DEFAULTS,handoff:policyAudience?.handoff===true};
    if (policyAudience && typeof policyAudience === 'object') {
      for (const key of ['intervalMs', 'generationDeadlineMs', 'firstPublishDelayMs', 'candidateTtlMs', 'npcPublishCooldownMs', 'publishGapMs']) {
        const value = policyAudience[key];
        if (Number.isFinite(value) && value >= 0) config[key] = value;
      }
      config.intervalMs = Math.max(1, config.intervalMs);
      for(const key of ['maxConcurrent','maxPoolSize'])if(Number.isInteger(policyAudience[key]))config[key]=Math.max(1,Math.min(key==='maxConcurrent'?2:3,policyAudience[key]));
      if (Number.isFinite(policyAudience.maxChars)) config.maxChars = Math.max(1, Math.min(36, Math.floor(policyAudience.maxChars)));
    }
    if(policyAudience?.phases?.chat?.publishGapMs>0)config.phases=policyAudience.phases;
    this.config = config;
    this._cancel();
    this.nextRequestAt = Math.max(this.now(), this.lastRequestAt + config.intervalMs);
  }
  pacing() {return this.config.phases?.[this.currentPhase]||this.config;}
  displayed(item,now) {
    const pace=this.pacing();
    this.cooldowns.set(item.npc_id,now);
    this.nextPublishAt=now+pace.publishGapMs+Math.floor(this.random()*(pace.jitterMs||0));
    this.recentMessages.push({npcId:item.npc_id,text:item.text});this.recentMessages=this.recentMessages.slice(-32);
  }
  get inflight() {return this.requests.values().next().value || null;}
  _allowed() {try {return this.canPublish() === true;} catch {return false;}}
  _advanceAllowed() {try {return this.canAdvance() === true;} catch {return false;}}
  setHostPreview({speechId,text}) {
    if(!this.config.handoff||typeof text!=='string'||!text||text.length>360)return;
    this._cancel('host_preview_replaced');
    this.hostPreview={id:uuid(),speechId:String(speechId),text,publicEventId:`speech-${speechId}`,status:'committed',expiresAt:this.now()+120000};
    this.nextRequestAt=this.now();
    this.dialogue.log({stage:'handoff.host.committed',previewId:this.hostPreview.id,speechId:String(speechId)});
  }
  clearPreparation(reason='cancelled'){this._cancel(reason);this.nextRequestAt=this.now();}
  _endSelection(work,reason) {
    if(this.selected!==work)return;
    this.selected=null;
    if(work.previewSent)this.onSelectionEnd({id:work.payload.requestId,messageId:work.messageId,reason});
    this.dialogue.log({stage:'handoff.audience.ended',candidateId:work.payload.requestId,reason});
  }
  _dependencyReady(work){return !work.sourcePreview||this.hostPreview?.id===work.sourcePreview.id&&this.hostPreview.status==='completed';}
  _cancel(reason='cancelled') {
    this.generation++;this.hostPreview=null;
    if(this.selected)this._endSelection(this.selected,reason);
    for(const work of this.requests.values())work.controller.abort();this.requests.clear();
    this.reviewPending?.controller.abort(); this.reviewPending=null;
    this.pool = []; this.candidates = [];
  }
  invalidate() {
    this._cancel(); this.dialogue.reset(); this.events = []; this.seenEvents = [];
    this.currentPhase=null;this.attempted.clear();this.cooldowns.clear(); this.recentTexts = []; this.recentMessages = [];
    this.lastRequestAt = -Infinity; this.nextRequestAt = this.now(); this.nextPublishAt=0;
  }
  getPublicEvents() {return this.events.map(copyEvent);}
  record(event, {replace=false}={}) {
    if (!event || typeof event.id !== 'string' || !event.id.trim() || event.id.length > 160
      || typeof event.type !== 'string' || !event.type.trim() || event.type.length > 64
      || typeof event.text !== 'string' || !event.text.trim() || event.text.length > 1600
      || !replace&&this.seenEvents.includes(event.id)) return false;
    // Fresh facts update the next request; they do not starve an in-flight response.
    const entry=this.dialogue.record({id:event.id,type:event.type,text:event.text,speakerRole:event.speakerRole,speakerId:event.speakerId,status:event.status,speechId:event.speechId,replyTo:event.replyTo});
    const existing=this.events.findIndex(e=>e.id===entry.id);
    if(existing>=0)this.events[existing]=entry;else this.events.push(entry);
    this.events = this.events.slice(-12);
    if(this.hostPreview&&entry.type==='speech.completed'&&entry.speechId===this.hostPreview.speechId&&entry.text===this.hostPreview.text){
      this.hostPreview.status='completed';
      for(const work of [...this.requests.values(),...this.pool,...(this.selected?[this.selected]:[])])if(work.sourcePreview?.id===this.hostPreview.id)work.expiresAt=this.now()+this.config.candidateTtlMs;
      this.dialogue.log({stage:'handoff.host.released',previewId:this.hostPreview.id,speechId:entry.speechId});
    }
    this.seenEvents.push(event.id); this.seenEvents = this.seenEvents.slice(-100);
    return true;
  }
  tick() {
    const now = this.now();
    if (!this._allowed()) {this._cancel(); return;}
    const phase=this.config.phases?this.phase():'chat';
    if(phase!==this.currentPhase){if(this.currentPhase!==null)this.clearPreparation('phase_changed');this.currentPhase=phase;}
    if(this.hostPreview&&now>=this.hostPreview.expiresAt)this._cancel('host_preview_expired');
    if(this.selected&&now>=this.selected.expiresAt){this.selected.controller.abort();this._endSelection(this.selected,'expired');this.reviewPending=null;this.candidates=[];}
    for(const [key,work] of this.requests)if(now>=work.deadline){work.controller.abort();this.requests.delete(key);}
    if(this.reviewPending && now>=this.reviewPending.expiresAt){this.reviewPending.controller.abort();this.reviewPending=null;}
    this.pool=this.pool.filter(next=>now<next.expiresAt);
    this._publishOne(now);this._reviewNext();
    if (!this.events.length || now < this.nextRequestAt) return;
    const occupied=this.requests.size+this.pool.length+(this.selected?1:0);
    const slots=Math.min(this.config.maxConcurrent-this.requests.size,(this.pacing().maxPoolSize||this.config.maxPoolSize)-occupied);
    if(slots<=0)return;
    const reserved=new Set([...this.requests.values(),...this.pool,...(this.selected?[this.selected]:[])].flatMap(w=>w.eligibleNpcIds||[w.item.npc_id]));
    const eligibleNpcIds = [...NPC_IDS].filter(npc => !reserved.has(npc)&&now - (this.cooldowns.get(npc) ?? -Infinity) >= this.config.npcPublishCooldownMs)
      .sort((a,b)=>(this.attempted.get(a)??-Infinity)-(this.attempted.get(b)??-Infinity));
    this.lastRequestAt=now;this.nextRequestAt=now+this.config.intervalMs;
    if(!eligibleNpcIds.length)return;
    // Parallel requests use distinct viewers; drafts never see each other's text.
    const groups=this.config.maxConcurrent===1?[eligibleNpcIds]:eligibleNpcIds.slice(0,slots).map(npc=>[npc]);
    for(const group of groups){if(group.length===1)this.attempted.set(group[0],now);this._generate(group,now);}
  }
  _generate(eligibleNpcIds,now) {
    const controller = new AbortController(), token = this.generation;
    const publicEvents = this.getPublicEvents().filter(e=>!e.type.startsWith('talent.')||['talent.playing','talent.completed','talent.segment.started'].includes(e.type));
    if(!publicEvents.length)return;
    const allowAdvance = this._advanceAllowed()&&this.pacing().maxAdvances!==0;
    const sourcePreview=this.hostPreview?.status==='committed'?{...this.hostPreview}:null;
    const hostPreview=sourcePreview?(({expiresAt,...view})=>view)(sourcePreview):null;
    const payload = {requestId: uuid(), kind: 'audience', message: '结合公开现场与自己的观看兴趣，选择一条值得发的弹幕，无需逐句回应', eventId: publicEvents.at(-1).id,
      context: {phase: this.currentPhase||'chat', publicEvents, audience: {eligibleNpcIds,...(hostPreview?{hostPreview}:{}),
        ownMessages: eligibleNpcIds.flatMap(npc=>this.recentMessages.filter(m=>m.npcId===npc).slice(-2)),
        waitingMessages:this.dialogue.snapshot().waiting_messages, recentMessages: this.recentMessages.slice(-8).map(item => ({...item})), allowAdvance}}};
    const request = {token, controller, deadline: now + this.config.generationDeadlineMs,
      expiresAt: sourcePreview?sourcePreview.expiresAt:now + this.config.candidateTtlMs, sourcePreview, publicEvents, eligibleNpcIds, allowAdvance, payload};
    this.requests.set(payload.requestId,request);
    Promise.resolve().then(() => {
      if (this.requests.get(request.payload.requestId) !== request || controller.signal.aborted) return null;
      return this.request(payload, {signal: controller.signal});
    }).then(result => this._receive(request, result), error => {if(!request.controller.signal.aborted&&error?.code==='agent_insufficient_balance')this.nextRequestAt=Math.max(this.nextRequestAt,this.now()+60000);this._receive(request, null);});
  }
  async _receive(request, result) {
    if (this.requests.get(request.payload.requestId) !== request || request.token !== this.generation || request.controller.signal.aborted) return;
    this.requests.delete(request.payload.requestId);
    const now = this.now();
    if (!this._allowed() || now >= request.deadline || now >= request.expiresAt || result?.status !== 'ready' || !Array.isArray(result.items)) return;
    const known = new Set([...request.publicEvents.map(event => event.id),...(request.sourcePreview?[request.sourcePreview.id]:[])]);
    for (const item of result.items.slice(0, 20)) {
      if (!item || !NPC_IDS.has(item.npc_id) || !request.eligibleNpcIds.includes(item.npc_id) || !INTENTS.has(item.intent)
        || typeof item.text !== 'string' || !item.text.trim()
        || Array.from(item.text.trim()).length > this.config.maxChars
        || /[\u0000-\u001f\u007f]|<\/?[a-z][^>]*>|```/i.test(item.text)
        || !Array.isArray(item.evidence_refs) || !item.evidence_refs.length || item.evidence_refs.length > 12
        || item.evidence_refs.some(ref => typeof ref !== 'string' || !known.has(ref))) continue;
      if (now - (this.cooldowns.get(item.npc_id) ?? -Infinity) < this.config.npcPublishCooldownMs
        || item.intent === 'advance' && (!request.allowAdvance || !this._advanceAllowed())) continue;
      this.pool.push({...request,item:{npc_id:item.npc_id,text:item.text.trim(),intent:item.intent,evidence_refs:[...item.evidence_refs]}});
      this.dialogue.log({stage:'audience.pool.ready',requestId:request.payload.requestId,size:this.pool.length});
      break; // A lane supplies one candidate; no rewrite loop after rejection.
    }
    this._reviewNext();
  }
  async _reviewNext() {
    if(this.publishing||this.reviewPending||this.candidates.length||!this.canCommit()||!this._allowed())return;
    if(!this.config.phases&&!this.config.handoff&&this.now()<this.nextPublishAt)return;
    let work=this.selected;
    while(!work&&this.pool.length){
      const next=this.pool.shift();
      if(next.token!==this.generation||this.now()>=next.expiresAt||this.now()-(this.cooldowns.get(next.item.npc_id)??-Infinity)<this.config.npcPublishCooldownMs||next.item.intent==='advance'&&!this._advanceAllowed())continue;
      work=next;work.messageId=uuid();this.selected=work;
      if(this.config.handoff&&this.canPreview()){
        work.previewSent=true;
        this.onSelected({id:work.payload.requestId,messageId:work.messageId,npcId:work.item.npc_id,text:work.item.text,status:'pending'});
        this.dialogue.log({stage:'handoff.audience.selected',candidateId:work.payload.requestId,messageId:work.messageId});
      }
    }
    if(!work||!this._dependencyReady(work)||!this.config.phases&&this.now()<this.nextPublishAt)return;
    if(this.recentMessages.some(m=>m.text===work.item.text)){this._endSelection(work,'repeated_text');return;}
    this.reviewPending=work;
    const refs=work.item.evidence_refs.map(ref=>ref===work.sourcePreview?.id?work.sourcePreview.publicEventId:ref);
    const candidate={id:work.payload.requestId,role:'audience',speakerId:work.item.npc_id,text:work.item.text,intent:work.item.intent,replyTo:[],evidenceIds:refs};
    // The reviewer sees actual public text only, never the private preview.
    const ticket=await this.dialogue.inspect(candidate,{signal:work.controller.signal,generationContext:work.payload.context});
    if(this.reviewPending===work)this.reviewPending=null;
    if(!ticket||work.token!==this.generation||work.controller.signal.aborted||this.now()>=work.expiresAt){this._endSelection(work,'review_rejected_or_stale');return;}
    this.candidates.push({token:work.token,notBefore:this.now()+this.config.firstPublishDelayMs,expiresAt:work.expiresAt,ticket,work,item:{...work.item,evidence_refs:refs}});
  }
  _publishOne(now) {
    while (this.candidates.length) {
      const next = this.candidates[0];
      if (next.token !== this.generation || now >= next.expiresAt
        || now - (this.cooldowns.get(next.item.npc_id) ?? -Infinity) < this.config.npcPublishCooldownMs
        || next.item.intent === 'advance' && !this._advanceAllowed()) {
        this.candidates.shift();this._endSelection(next.work,'expired_or_ineligible');continue;
      }
      if (now < Math.max(next.notBefore,this.nextPublishAt)) return;
      if (!this._allowed()) {this._cancel(); return;}
      this.candidates.shift();
      if(!this.canCommit()){this._endSelection(next.work,'host_priority');return;}
      const beforePublish=this.dialogue.events.at(-1)?.sequence;
      try {if(!this.dialogue.commit(next.ticket,()=>this.publish({...next.item,...(this.config.handoff?{messageId:next.work.messageId}:{}), evidence_refs: [...next.item.evidence_refs]}))){this._endSelection(next.work,'stale_review');return;}} catch {this._endSelection(next.work,'publication_failed');return;}
      this._endSelection(next.work,'published');
      this.displayed(next.item,now);
      // Main host records publication synchronously; legacy callbacks may not.
      if(this.dialogue.events.at(-1)?.sequence===beforePublish)this.dialogue.record({id:`npc-${next.item.npc_id}-${uuid()}`,type:'npc.message',text:next.item.text,speakerRole:'audience',speakerId:next.item.npc_id});
      // History helps avoid repetitions, but publication neither adds facts nor starts a request.
      return;
    }
  }
}
