import assert from 'node:assert/strict';
import {AudienceDirector} from '../ui/audience-director.js';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {AgentClient} from '../ui/agent-client.js';
import {SessionContinuity} from '../ui/session-continuity.js';
import worker from '../dist/server/index.js';
import {reviewReply} from './review-fixture.mjs';
const flush=async()=>{for(let i=0;i<40;i++)await Promise.resolve();};
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve};};
const answer=(extra={})=>({action:'speak',speech:'先把刚才的想法讲清楚。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:9000,continuation:'leave_space',memory:'',agendaUpdates:[],...extra});
const topic=(p,status='complete')=>({status,reason:'这句说完已回应当前的问题。',nextDirection:'接续本场另一个有依据的角度。',evidenceIds:p.context.publicEvents.slice(0,1).map(e=>e.id),anchorSpeechId:p.context.inProgress.speechId});
const make=generate=>{const e=new ConversationEngine();e.configureAgent({generate,cancel(){}},{enabled:true,topic:{enabled:true}});e.setSpeechDriven(true);e.start();clearInterval(e._timer);return e;};
let count=0;const test=async(name,fn)=>{await fn();count++;console.log('PASS '+name);};
await test('two viewer lanes overlap; the faster lane publishes first and a third replenishes a bounded pool',async()=>{
  let now=0;const calls=[],published=[];const d=new AudienceDirector({now:()=>now,config:{maxConcurrent:2,maxPoolSize:3,intervalMs:1500,generationDeadlineMs:3500,candidateTtlMs:12000,publishGapMs:2500},publish:x=>published.push(x),request:(p,o)=>p.kind==='review'?Promise.resolve(reviewReply(p)):new Promise(resolve=>calls.push({p,o,resolve}))});
  d.record({id:'public',type:'speech.completed',text:'这是本场已公开的内容。'});d.tick();await flush();assert.equal(calls.length,2);assert.deepEqual(calls.map(c=>c.p.context.audience.eligibleNpcIds),[['wind'],['moon']]);
  const reply=(call,text)=>({status:'ready',items:[{npc_id:call.p.context.audience.eligibleNpcIds[0],text,intent:'reaction',evidence_refs:['public']}]});
  calls[1].resolve(reply(calls[1],'第二路先有结果'));await flush();d.tick();assert.equal(published.length,1);assert.equal(published[0].npc_id,'moon');assert.equal(d.requests.size,1);
  calls[0].resolve(reply(calls[0],'第一路稍后准备好'));await flush();assert.equal(d.pool.length,1);now=1500;d.tick();await flush();assert.equal(calls.length,3);assert.deepEqual(calls[2].p.context.audience.eligibleNpcIds,['salt']);
  calls[2].resolve(reply(calls[2],'第三位有自己的兴趣'));await flush();assert(d.pool.length+d.candidates.length+d.requests.size<=3);
  now=2500;d.tick();await flush();d.tick();assert.equal(published.length,2);assert.equal(published[1].npc_id,'wind');d.invalidate();
});
await test('pooled drafts review the latest published peer; semantic duplication is rejected by reviewer',async()=>{
  let now=0;const published=[],reviews=[];const d=new AudienceDirector({now:()=>now,config:{maxConcurrent:2,maxPoolSize:3,publishGapMs:1000},publish:x=>published.push(x),request:async p=>{
    if(p.kind==='review'){reviews.push(p);return reviewReply(p,reviews.length===1?'allow':'drop');}
    return {status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],text:'同一份观看诉求',intent:'reaction',evidence_refs:['public']}]};
  }});
  d.record({id:'public',type:'speech.completed',text:'公开的内容。'});d.tick();await flush();d.tick();assert.equal(published.length,1);now=1000;d.tick();await flush();d.tick();assert.equal(reviews.length,2);assert(reviews[1].context.publicEvents.some(e=>e.type==='npc.message'&&e.text==='同一份观看诉求'));assert.equal(published.length,1);d.invalidate();
});
await test('pause aborts every parallel lane and late results cannot repopulate the pool',async()=>{
  let allowed=true;const calls=[];const d=new AudienceDirector({canPublish:()=>allowed,config:{maxConcurrent:2,maxPoolSize:3},publish:()=>assert.fail('late publish'),request:(p,o)=>new Promise(resolve=>calls.push({p,o,resolve}))});
  d.record({id:'public',type:'speech.completed',text:'公开的内容。'});d.tick();await flush();allowed=false;d.tick();assert(calls.every(c=>c.o.signal.aborted));for(const c of calls)c.resolve({status:'ready',items:[{npc_id:c.p.context.audience.eligibleNpcIds[0],text:'迟到内容',intent:'reaction',evidence_refs:['public']}]});await flush();assert.equal(d.pool.length,0);assert.equal(d.requests.size,0);d.invalidate();
});
await test('topic assessment overlaps host preparation, replans a transition privately, and shortens only the completed-turn gap',async()=>{
  const judgment=deferred(),draft=deferred(),calls=[];let initial=true;
  const e=make((p,o)=>{calls.push({p,o});if(p.kind==='review')return Promise.resolve(reviewReply(p));if(p.kind==='topic')return judgment.promise;if(p.context.host.turn.mode==='prepare'){if(initial){initial=false;return draft.promise;}return Promise.resolve(answer({speech:'自然接到另一个角度。',continuation:'continue'}));}return Promise.resolve(answer());});
  try{await e._think();const active=e._active.id;e._maybeAssessTopic();e._maybePrepare();assert(e.topicWork&&e.preparing);assert.equal(e._wakeDelay(e.speechPlan.result),9000);const check=calls.find(c=>c.p.kind==='topic');judgment.resolve(topic(check.p));await flush();assert(e.prepared);assert(calls.find(c=>c.p.context.host?.turn.mode==='prepare').o.signal.aborted);assert.equal(e._active.id,active);assert(!e.facts.some(f=>f.text==='自然接到另一个角度。'));assert.equal(e.session.items.length,0);assert.equal(e.prepared.context.host.topicProgress.status,'complete');draft.resolve(answer({speech:'过时旧稿'}));await flush();assert.equal(e.prepared.result.speech,'自然接到另一个角度。');e.completeSpeech(active);assert.equal(e.nextWake,e._clock+350);e._clock=e.nextWake;await e._think();assert.equal(e._active.text,'自然接到另一个角度。');}finally{e.destroy();}
});
await test('new player or audience message invalidates transition and latest inbox can return to the old subject',async()=>{
  for(const mine of [true,false]){
    const calls=[];const e=make(async p=>{calls.push(p);if(p.kind==='review')return reviewReply(p);if(p.kind==='topic')return topic(p);return answer({speech:p.context.host.topicProgress?'准备转到新内容':'回应当前内容'});});
    try{await e._think();e._maybeAssessTopic();await flush();assert(e.prepared);const previous=e.prepared.requestId;
      if(mine)e.sendMessage('刚才那件事我还有一点想问');else {const m=e._message('刚才那件事我还有一点想问',{name:'海盐汽水',type:'chat'});e._receive(m,false,'salt');}
      assert.equal(e.topicProgress,null);assert.equal(e.prepared,null);e._maybePrepare();await flush();assert(e.prepared);assert.notEqual(e.prepared.requestId,previous);assert(e.prepared.context.host.inbox.some(m=>m.text.includes('还有一点')));assert(!e.prepared.context.host.topicProgress);
    }finally{e.destroy();}
  }
});
await test('waiting, failed, or stalled assessment never holds normal speech or changes a necessary response window',async()=>{
  for(const mode of ['waiting','failed','stalled']){
    const deferredTopic=deferred();const e=make(async p=>p.kind==='review'?reviewReply(p):p.kind==='topic'?mode==='failed'?Promise.reject(Error('timeout')):mode==='stalled'?deferredTopic.promise:topic(p,'waiting'):answer());
    try{await e._think();e._maybeAssessTopic();e._maybePrepare();await flush();assert(e.prepared);e.completeSpeech(e._active.id);assert.equal(e.nextWake,e._clock+9000);e._clock=e.nextWake;await e._think();assert(e._active);deferredTopic.resolve({});await flush();}finally{e.destroy();}
  }
});
await test('a waiting judgment preserves the response window even when the previous plan was continue',async()=>{
  const e=make(async p=>p.kind==='review'?reviewReply(p):p.kind==='topic'?topic(p,'waiting'):answer({continuation:'continue'}));
  try{await e._think();e._maybeAssessTopic();await flush();e.completeSpeech(e._active.id);assert.equal(e.nextWake,e._clock+9000);}finally{e.destroy();}
});
await test('late topic signals after pause, replay, receipt or destroy remain inert; await-event does not speculate',async()=>{
  for(const change of [e=>e.setPaused(true),e=>e.replay(),e=>e._toolEvent('有新的实际回执'),e=>e.destroy()]){
    let request;const delayed=deferred(),e=make(async p=>p.kind==='review'?reviewReply(p):p.kind==='topic'?(request=p,delayed.promise):answer());
    try{await e._think();e._maybeAssessTopic();change(e);delayed.resolve(topic(request));await flush();assert.equal(e.topicProgress,null);}finally{e.destroy();}
  }
  const e=make(async p=>{assert.notEqual(p.kind,'topic');return p.kind==='review'?reviewReply(p):answer({continuation:'await_event'});});try{await e._think();e._maybeAssessTopic();assert.equal(e.topicWork,null);}finally{e.destroy();}
});
await test('replay retains parallel pool configuration',async()=>{
  const e=make(async p=>p.kind==='review'?reviewReply(p):answer());try{e.audience.setConfig({maxConcurrent:2,maxPoolSize:3});e.replay();assert.equal(e.audience.config.maxConcurrent,2);assert.equal(e.audience.config.maxPoolSize,3);}finally{e.destroy();}
});
await test('recent real discussion survives NPC traffic, but partial speech and cross-session memories do not enter history',async()=>{
  const s=new SessionContinuity();s.record({id:'older',type:'speech.completed',text:'刚才讲过的内容',speakerRole:'host'});for(let i=0;i<16;i++)s.record({id:'n'+i,type:'npc.message',text:'弹幕反馈'});s.record({id:'draft',type:'speech.progress',text:'未说完'});assert(s.evidence().some(e=>e.id==='older'));assert(!s.evidence().some(e=>e.id==='draft'));assert.deepEqual(s.snapshot().crossSessionMemory,{available:false,entries:[]});
});
await test('real client and built Worker permit two audience lanes plus topic and host; third audience lane is bounded',async()=>{
  const original=globalThis.fetch,origin='https://parallel-pipeline.test',calls=[],releases=[];
  globalThis.fetch=async(url,o)=>{const envelope=JSON.parse(o.body),input=JSON.parse(envelope.messages[1].content),hold=deferred();calls.push(input);releases.push(()=>hold.resolve());await hold.promise;
    const result=input.inProgress?{status:'complete',reason:'当前问题已讲足',nextDirection:'接续本场的另一个角度',evidenceIds:['public']}:input.allowed_candidate_npc_ids?{status:'ok',npc_id:input.allowed_candidate_npc_ids[0],intent:'reaction',text:'一个新的观看诉求',evidence_ids:['public']}:answer();return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(result)}}]});};
  const statuses=[];const client=new AgentClient({fetchImpl:async(path,o)=>{const res=await worker.fetch(new Request(origin+path,{...o,headers:{...o.headers,Origin:origin,'oai-authenticated-user-id':'parallel-pipeline'}}),{DEEPSEEK_API_KEY:'fixture'});statuses.push(res.status);return res;}});
  const event={id:'public',type:'speech.completed',text:'公开的内容。'},aud=npc=>({kind:'audience',eventId:'public',context:{publicEvents:[event],audience:{eligibleNpcIds:[npc]}}});
  const running=[];
  try{
    running.push(client.generate(aud('wind')),client.generate(aud('moon')),client.generate({kind:'topic',eventId:'topic',context:{publicEvents:[event],sessionEvents:[],inbox:[],inProgress:{speechId:'1',text:'正在收束的台词。'}}}),client.generate({kind:'host',eventId:'host',context:{host:{inbox:[],memory:''}}}));
    for(let i=0;i<100&&calls.length<4;i++)await new Promise(r=>setTimeout(r,5));assert.equal(calls.length,4);await assert.rejects(client.generate(aud('salt')),/unavailable/);assert(statuses.includes(429));for(const release of releases)release();const results=await Promise.all(running);assert.equal(results[2].status,'complete');assert.equal(results[2].anchorSpeechId,'1');assert.equal(results[3].action,'speak');
    const malicious={kind:'topic',eventId:'topic',context:{publicEvents:[event],sessionEvents:[],inbox:[],inProgress:{speechId:'1',text:'台词'},privateDeck:['hidden']}};await assert.rejects(client.generate(malicious),/unavailable/);assert.equal(calls.length,4);
  }finally{for(const release of releases)release();await Promise.allSettled(running);client.cancel();globalThis.fetch=original;}
});
await test('insufficient balance is explicit and suspends repeated requests across background lanes',async()=>{
  let fetches=0;const client=new AgentClient({fetchImpl:async(path)=>{fetches++;return path==='/api/agent/config'?Response.json({enabled:true}):Response.json({error:'agent_insufficient_balance',message:'DeepSeek 余额不足'},{status:402});}});
  await assert.rejects(client.generate({kind:'host'}),e=>e.code==='agent_insufficient_balance');
  for(const kind of ['audience','topic','host'])await assert.rejects(client.generate({kind}),e=>e.code==='agent_insufficient_balance');assert.equal(fetches,1);await client.configure();assert.equal(client.blockedUntil,0);
  const e=make(async()=>{throw Object.assign(Error('provider'),{code:'agent_insufficient_balance'});});try{await e._think();assert.equal(e.nextWake,e._clock+60000);assert.equal(e._state.agent.errorCode,'agent_insufficient_balance');}finally{e.destroy();}
  let now=0;const d=new AudienceDirector({now:()=>now,config:{maxConcurrent:2,maxPoolSize:3,intervalMs:1500},publish:()=>{},request:async()=>{throw Object.assign(Error('provider'),{code:'agent_insufficient_balance'});}});d.record({id:'public',type:'speech.completed',text:'公开内容'});d.tick();await flush();assert.equal(d.nextRequestAt,60000);now=3000;d.tick();assert.equal(d.requests.size,0);d.invalidate();
  const original=globalThis.fetch,origin='https://balance.test';globalThis.fetch=async()=>Response.json({error:{type:'insufficient_balance_error',message:'private provider details'}},{status:402});
  try{const res=await worker.fetch(new Request(origin+'/api/agent',{method:'POST',headers:{'Content-Type':'application/json',Origin:origin,'oai-authenticated-user-id':'balance'},body:JSON.stringify({requestId:crypto.randomUUID(),kind:'host',eventId:'host',context:{host:{inbox:[],memory:''}}})}),{DEEPSEEK_API_KEY:'fixture'});assert.equal(res.status,402);const body=await res.json();assert.equal(body.error,'agent_insufficient_balance');assert(!JSON.stringify(body).includes('private provider'));}finally{globalThis.fetch=original;}
});
console.log(`\n${count} background pipeline scenarios passed.`);
