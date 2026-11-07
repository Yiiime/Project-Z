// Public provenance and an independent semantic gate. No content heuristics here.
const clone = value => JSON.parse(JSON.stringify(value));
const newId = () => globalThis.crypto.randomUUID();
export class DialogueReview {
  constructor({request, now = () => Date.now(), version = null, timeoutMs = 4500} = {}) {
    this.request=request; this.now=now; this.version=version; this.timeoutMs=timeoutMs;
    this.revision=0; this.session=0; this.events=[]; this.waiting=[]; this.traces=[]; this.controllers=new Set();
    this.tickets=new WeakMap();this.concurrent=false;this.roleEpoch={host:0,audience:0};this.reservations={host:0,audience:0};
  }
  configure({version,timeoutMs}={}) {
    if(typeof version==='string')this.version=version;
    if(Number.isFinite(timeoutMs)&&timeoutMs>0)this.timeoutMs=timeoutMs;
    this.invalidate();
  }
  invalidate(role=null) {this.revision++;for(const key of ['host','audience'])if(!role||role===key)this.roleEpoch[key]++;for(const c of this.controllers)if(!role||c.reviewRole===role){c.abort();this.controllers.delete(c);}}
  setConcurrent(value) {this.concurrent=!!value;this.invalidate();}
  _current(approved) {
    if(approved.session!==this.session||approved.epoch!==this.roleEpoch[approved.candidate.role])return false;
    if(!this.concurrent)return approved.revision===this.revision;
    const role=approved.candidate.role;
    if(approved.reservation!==this.reservations[role])return false;
    // Call reactions are reviewed against an immutable public cut. New player/system facts
    // invalidate it; a growing played prefix or another spectator does not erase its evidence.
    const before=approved.snapshot,byId=new Map(this.events.map(e=>[e.id,e]));
    const same=(a,b)=>a.type===b.type&&a.text===b.text&&a.speakerRole===b.speakerRole&&a.speakerId===b.speakerId&&a.status===b.status&&JSON.stringify(a.replyTo)===JSON.stringify(b.replyTo);
    for(const old of before.publicEvents){
      const next=byId.get(old.id);if(!next)return false;
      if(same(old,next))continue;
      if(role==='audience'&&old.type==='speech.progress'&&['speech.progress','speech.completed'].includes(next.type)&&next.speechId===old.speechId&&next.speakerRole==='host'&&next.text.startsWith(old.text))continue;
      return false;
    }
    const oldIds=new Set(before.publicEvents.map(e=>e.id));
    if(this.events.some(e=>!oldIds.has(e.id)&&e.type!=='npc.message'&&!(role==='audience'&&['speech.progress','speech.completed'].includes(e.type))))return false;
    if(role==='host')return JSON.stringify(this.waiting)===JSON.stringify(before.waiting_messages);
    return this.waiting.every(e=>before.waiting_messages.some(old=>JSON.stringify(old)===JSON.stringify(e)));
  }
  reset() {this.session++;this.invalidate();this.events=[];this.waiting=[];this.traces=[];}
  record(event) {
    const role=event.speakerRole||(event.type.startsWith('speech.')?'host':event.type==='player.message'?'player':event.type==='npc.message'?'audience':'system');
    const entry={id:event.id,type:event.type,text:event.text,speakerRole:role,speakerId:event.speakerId||(role==='host'?'Z':role),
      sequence:++this.revision,occurredAt:this.now(),status:event.status||(event.type==='speech.completed'?'completed':'published'),
      speechId:event.speechId||null,replyTo:[...(event.replyTo||[])]};
    const index=this.events.findIndex(e=>e.id===entry.id);
    if(index>=0)this.events[index]=entry;else this.events.push(entry);
    this.events=this.events.slice(-40);
    // Coalesce subtitle progress in the trace; review snapshots retain exact prefixes.
    const previous=this.traces.find(t=>t.stage==='public.event'&&t.event.id===entry.id);
    if(previous){previous.event=clone(entry);previous.lastAt=this.now();}
    else this.log({stage:'public.event',event:entry});
    return clone(entry);
  }
  setWaiting(messages) {
    const next=messages.map(m=>({id:m.id,speakerRole:m.mine?'player':'audience',speakerId:m.speakerId||this.events.find(e=>e.id===m.id)?.speakerId||m.name,text:m.text}));
    if(JSON.stringify(next)!==JSON.stringify(this.waiting)){this.waiting=clone(next);this.revision++;}
  }
  snapshot() {return {revision:this.revision,publicEvents:clone(this.events),waiting_messages:clone(this.waiting)};}
  log(entry) {this.traces.push({at:this.now(),wallAt:Date.now(),...clone(entry)});this.traces=this.traces.slice(-120);}
  exportTrace() {return {version:this.version,revision:this.revision,records:clone(this.traces)};}
  async inspect(candidate,{signal,generationContext=null,sessionEvents=[],compact=false,onOutcome,onFailure=()=>{}}={}) {
    const outcome=(value,detail)=>{if(value!=='allowed')onFailure(['stale','cancelled'].includes(value)?'stale':value==='rejected'?'rejected':'unavailable');try{onOutcome?.(value,detail);}catch{}};
    const snapshot=this.snapshot(),session=this.session,controller=new AbortController(),abort=()=>controller.abort();
    controller.reviewRole=candidate.role;
    const approved={snapshot,session,revision:snapshot.revision,candidate:clone(candidate),epoch:this.roleEpoch[candidate.role],reservation:this.reservations[candidate.role]};
    const payload={requestId:newId(),kind:'review',eventId:candidate.id,message:'独立审阅待发布的直播发言',
      context:{...snapshot,...(candidate.role==='host'&&sessionEvents.length?{sessionEvents:clone(sessionEvents)}:{}),candidate:clone(candidate)}};
    this.log({stage:'review.request',requestId:payload.requestId,promptVersion:this.version,candidate,generationContext,snapshot});
    if(signal?.aborted)return null;
    signal?.addEventListener('abort',abort,{once:true});this.controllers.add(controller);
    let timer,aborted,timedOut=false;
    const cancelled=new Promise((_,reject)=>{aborted=()=>reject(new Error('review_cancelled'));controller.signal.addEventListener('abort',aborted,{once:true});});
    timer=setTimeout(()=>{timedOut=true;abort();},this.timeoutMs);
    try {
      let reply=await Promise.race([Promise.resolve().then(()=>this.request(payload,{signal:controller.signal,compactReview:compact})),cancelled]);
      if(compact&&reply?.decision==='need_context'&&!controller.signal.aborted&&this._current(approved)){
        this.log({stage:'review.expand',candidateId:candidate.id});
        reply=await Promise.race([Promise.resolve().then(()=>this.request({...payload,requestId:newId()},{signal:controller.signal,compactReview:true,reviewExpanded:true})),cancelled]);
      }
      const valid=!controller.signal.aborted&&reply?.decision==='allow'&&reply.candidateId===candidate.id&&reply.revision===snapshot.revision
        &&typeof reply.promptVersion==='string'&&(!this.version||reply.promptVersion===this.version)&&this._current(approved);
      if(session===this.session)this.log({stage:'review.result',requestId:payload.requestId,candidateId:candidate.id,reply,accepted:valid,currentRevision:this.revision});
      if(!valid){outcome(timedOut?'unavailable':controller.signal.aborted?'cancelled':reply?.candidateId!==candidate.id||reply?.revision!==snapshot.revision?'invalid_reply':this.version&&reply?.promptVersion!==this.version?'version_mismatch':!this._current(approved)?'stale':reply?.decision==='drop'?'rejected':'invalid_reply',{reason:reply?.reason,issue:reply?.issue,span:reply?.span,evidenceIds:reply?.evidenceIds});return null;}
      outcome('allowed');
      const ticket={};this.tickets.set(ticket,{...approved,promptVersion:reply.promptVersion});
      return ticket;
    }catch(error) {outcome(timedOut?'unavailable':controller.signal.aborted?'cancelled':'unavailable');if(session===this.session)this.log({stage:'review.unavailable',requestId:payload.requestId,candidateId:candidate.id,code:error?.code||'review_unavailable'});return null;}
    finally{clearTimeout(timer);controller.signal.removeEventListener('abort',aborted);signal?.removeEventListener('abort',abort);this.controllers.delete(controller);}
  }
  commit(ticket, publish) {
    const approved=ticket&&this.tickets.get(ticket);if(ticket)this.tickets.delete(ticket);
    if(!approved||!this._current(approved)){this.log({stage:'publish.discard',reason:'stale_or_missing_review'});return false;}
    // Caller invokes this synchronously at the real display/queue boundary.
    this.revision++; // Reserve the turn before a callback can publish another candidate.
    this.reservations[approved.candidate.role]++;
    publish(clone(approved.candidate));
    this.log({stage:'publish.commit',candidateId:approved.candidate.id,candidate:approved.candidate,revision:approved.revision,promptVersion:approved.promptVersion});
    return true;
  }
}
