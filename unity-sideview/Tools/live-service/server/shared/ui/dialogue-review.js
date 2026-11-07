// Public provenance and an independent semantic gate. No content heuristics here.
const clone = value => JSON.parse(JSON.stringify(value));
const newId = () => globalThis.crypto.randomUUID();
export class DialogueReview {
  constructor({request, now = () => Date.now(), version = null, timeoutMs = 4500} = {}) {
    this.request=request; this.now=now; this.version=version; this.timeoutMs=timeoutMs;
    this.revision=0; this.session=0; this.events=[]; this.waiting=[]; this.traces=[]; this.controllers=new Set();
    this.tickets=new WeakMap();
  }
  configure({version,timeoutMs}={}) {
    if(typeof version==='string')this.version=version;
    if(Number.isFinite(timeoutMs)&&timeoutMs>0)this.timeoutMs=timeoutMs;
    this.invalidate();
  }
  invalidate() {this.revision++;for(const c of this.controllers)c.abort();this.controllers.clear();}
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
  async inspect(candidate,{signal,generationContext=null,sessionEvents=[],onFailure=()=>{}}={}) {
    const snapshot=this.snapshot(),session=this.session,controller=new AbortController(),abort=()=>controller.abort();
    const payload={requestId:newId(),kind:'review',eventId:candidate.id,message:'独立审阅待发布的直播发言',
      context:{...snapshot,...(candidate.role==='host'&&sessionEvents.length?{sessionEvents:clone(sessionEvents)}:{}),candidate:clone(candidate)}};
    this.log({stage:'review.request',requestId:payload.requestId,promptVersion:this.version,candidate,generationContext,snapshot});
    if(signal?.aborted)return null;
    signal?.addEventListener('abort',abort,{once:true});this.controllers.add(controller);
    let timer,aborted;
    const cancelled=new Promise((_,reject)=>{aborted=()=>reject(new Error('review_cancelled'));controller.signal.addEventListener('abort',aborted,{once:true});});
    timer=setTimeout(abort,this.timeoutMs);
    try {
      const reply=await Promise.race([Promise.resolve().then(()=>this.request(payload,{signal:controller.signal})),cancelled]);
      const valid=!controller.signal.aborted&&reply?.decision==='allow'&&reply.candidateId===candidate.id&&reply.revision===snapshot.revision
        &&typeof reply.promptVersion==='string'&&(!this.version||reply.promptVersion===this.version)&&this.revision===snapshot.revision;
      if(session===this.session)this.log({stage:'review.result',requestId:payload.requestId,candidateId:candidate.id,reply,accepted:valid,currentRevision:this.revision});
      if(!valid){onFailure(this.revision!==snapshot.revision?'stale':reply?.decision==='drop'?'rejected':'unavailable');return null;}
      const ticket={};this.tickets.set(ticket,{revision:snapshot.revision,candidate:clone(candidate),promptVersion:reply.promptVersion});
      return ticket;
    }catch {onFailure(signal?.aborted?'stale':'unavailable');if(session===this.session)this.log({stage:'review.unavailable',requestId:payload.requestId,candidateId:candidate.id});return null;}
    finally{clearTimeout(timer);controller.signal.removeEventListener('abort',aborted);signal?.removeEventListener('abort',abort);this.controllers.delete(controller);}
  }
  commit(ticket, publish) {
    const approved=ticket&&this.tickets.get(ticket);if(ticket)this.tickets.delete(ticket);
    if(!approved||approved.revision!==this.revision){this.log({stage:'publish.discard',reason:'stale_or_missing_review'});return false;}
    // Caller invokes this synchronously at the real display/queue boundary.
    this.revision++; // Reserve the turn before a callback can publish another candidate.
    publish(clone(approved.candidate));
    this.log({stage:'publish.commit',candidateId:approved.candidate.id,candidate:approved.candidate,revision:approved.revision,promptVersion:approved.promptVersion});
    return true;
  }
}
