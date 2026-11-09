import assert from 'node:assert/strict';
import {DialogueReview} from '../ui/dialogue-review.js';
import {AudienceDirector} from '../ui/audience-director.js';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {AgentClient} from '../ui/agent-client.js';
import {handleAgent} from '../worker/agent-director.js';
import {reviewReply} from './review-fixture.mjs';
const flush=async()=>{for(let i=0;i<32;i++)await Promise.resolve();};
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve};};
const event=(id='public-1',text='主播已经公开的表达')=>({id,type:'speech.completed',text});
const candidate=(role='audience',text='我想听一个不同的故事')=>({id:crypto.randomUUID(),role,speakerId:role==='host'?'Z':'moon',text,intent:role==='host'?'speak':'reaction',replyTo:[],evidenceIds:[]});
const hostResult=(extra={})=>({action:'speak',speech:'我有自己的想法，想接着说给你听。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:8000,memory:'已回应',...extra});
let count=0;const engines=[];
async function test(name,fn){try{await fn();count++;console.log('PASS '+name);}finally{for(const e of engines.splice(0))e.destroy();}}

await test('review uses latest attributed public snapshot; generation snapshot remains diagnostic only',async()=>{
  let wire;const gate=new DialogueReview({request:async p=>{wire=p;return reviewReply(p);}});
  gate.record(event());const generation=gate.snapshot();gate.record({id:'npc-1',type:'npc.message',text:'观众的猜测',speakerId:'salt'});
  const ticket=await gate.inspect(candidate(),{generationContext:generation});
  assert(ticket);assert.equal(wire.context.publicEvents.length,2);
  assert.equal(wire.context.publicEvents[0].speakerRole,'host');assert.equal(wire.context.publicEvents[1].speakerRole,'audience');
  assert(!('generationContext' in wire.context));assert.equal(gate.exportTrace().records.find(r=>r.stage==='review.request').generationContext.publicEvents.length,1);
});
await test('drop, malformed reply, mismatched candidate/revision/version and provider failure cannot authorize publication',async()=>{
  for(const mutate of [p=>reviewReply(p,'drop'),()=>({decision:'allow'}),p=>({...reviewReply(p),candidateId:'other'}),p=>({...reviewReply(p),revision:-1}),p=>({...reviewReply(p),promptVersion:'old'}),()=>{throw Error('offline');}]){
    const gate=new DialogueReview({version:'fixture-review-v1',request:async p=>mutate(p)});
    gate.record(event());assert.equal(await gate.inspect(candidate()),null);
  }
  const gate=new DialogueReview({request:()=>new Promise(()=>{}),timeoutMs:5});
  assert.equal(await gate.inspect(candidate()),null);
});
await test('new facts during review, after approval, and a competing commit invalidate old tickets',async()=>{
  let wire;const delayed=deferred(),gate=new DialogueReview({request:p=>{wire=p;return delayed.promise;}});
  gate.record(event());const pending=gate.inspect(candidate());await flush();gate.record(event('new','现场已变化'));delayed.resolve(reviewReply(wire));assert.equal(await pending,null);
  gate.request=async p=>reviewReply(p);const stale=await gate.inspect(candidate());gate.record(event('newer'));assert.equal(gate.commit(stale,()=>assert.fail('stale publish')),false);
  const first=await gate.inspect(candidate()),second=await gate.inspect(candidate('host'));let publications=0;
  assert(gate.commit(first,()=>publications++));assert.equal(gate.commit(second,()=>publications++),false);assert.equal(gate.commit(first,()=>publications++),false);assert.equal(publications,1);
});
await test('parent cancellation and reset reject a non-cooperative late reviewer',async()=>{
  for(const reset of [false,true]){
    const pending=deferred();let wire;const controller=new AbortController();
    const gate=new DialogueReview({request:p=>{wire=p;return pending.promise;}});
    const result=gate.inspect(candidate(),{signal:controller.signal});await flush();
    if(reset)gate.reset();else controller.abort();
    pending.resolve(reviewReply(wire));assert.equal(await result,null);if(reset)assert.equal(gate.exportTrace().records.length,0);
  }
});
await test('audience publication follows review verdict, including identical text approved in a new context',async()=>{
  let now=0,verdict='drop',calls=0;const published=[];
  const d=new AudienceDirector({now:()=>now,config:{npcPublishCooldownMs:0},publish:item=>published.push(item),request:async p=>{
    if(p.kind==='review'){calls++;return reviewReply(p,verdict);}
    return {status:'ready',items:[{npc_id:'moon',text:'一起鼓掌',intent:'atmosphere',evidence_refs:['public-1']}]};
  }});
  d.record(event());d.tick();await flush();d.tick();assert.equal(published.length,0);
  verdict='allow';now=6000;d.tick();await flush();d.tick();assert.equal(published.length,1);
  now=12000;d.tick();await flush();d.tick();assert.equal(published.length,2);assert.equal(calls,3);
  assert.equal(published[0].text,published[1].text); // No content blacklist or blanket text filter.
});
await test('audience approval is discarded on fresh speech or host priority before dispatch',async()=>{
  for(const changed of ['context','priority']){
    let canCommit=true;const published=[];
    const d=new AudienceDirector({canCommit:()=>canCommit,publish:x=>published.push(x),request:async p=>p.kind==='review'?reviewReply(p):{status:'ready',items:[{npc_id:'moon',text:'我喜欢这个主题',intent:'reaction',evidence_refs:['public-1']}]}});
    d.record(event());d.tick();await flush();assert.equal(d.candidates.length,1);
    if(changed==='context')d.record(event('later'));else canCommit=false;
    d.tick();assert.equal(published.length,0);d.invalidate();
  }
});
await test('rejected host speech neither changes memory/receipts nor executes an attached tool',async()=>{
  const e=new ConversationEngine();engines.push(e);let executed=0;
  e._executeTool=()=>{executed++;return true;};e.start();e.sendMessage('想玩一轮');
  const messageId=e.inbox[0].id;
  e.configureAgent({cancel(){},async generate(p){return p.kind==='review'?reviewReply(p,'drop'):hostResult({action:'tool',handledIds:[messageId],tool:{name:'start_tarot',args:{origin:'host'}}});}},{enabled:true});
  await e._think();assert.equal(e._active,null);assert.equal(executed,0);assert.equal(e.inbox[0].id,messageId);assert.equal(e.memory,'');
  assert(!e.facts.some(x=>x.type.startsWith('speech.')));
});
await test('host approval is bound to live context; a new player message cancels its review',async()=>{
  for(const role of ['player','audience']){
    const e=new ConversationEngine();engines.push(e);const pending=deferred();let review;
    e.configureAgent({cancel(){},generate(p){if(p.kind==='review'){review=p;return pending.promise;}return Promise.resolve(hostResult());}},{enabled:true});e.start();
    const thinking=e._think();await flush();assert(review);
    if(role==='player')e.sendMessage('补充一句');else{const m=e._message('我另有兴趣',{name:'观众'});e._receive(m,false,'salt');}
    pending.resolve(reviewReply(review));await thinking;assert.equal(e._active,null);assert.equal(e.memory,'');
  }
});
await test('host is reviewed independently and only displayed speech prefixes enter audience context',async()=>{
  const e=new ConversationEngine();engines.push(e);const calls=[];
  e.configureAgent({cancel(){},async generate(p){calls.push(p);return p.kind==='review'?reviewReply(p):hostResult({speech:'已经展示。尚未展示的后半句。'});}},{enabled:true});e.setSpeechDriven(true);e.start();
  await e._think();assert.deepEqual(calls.map(p=>p.kind),['host','review']);assert(e._active);
  assert(!JSON.stringify(e.audience.getPublicEvents()).includes('尚未展示'));
  e.recordSpeechProgress(e._active.id,'已经展示。');
  const progress=e.audience.getPublicEvents().find(x=>x.type==='speech.progress');assert.equal(progress.status,'speaking');assert.equal(progress.speakerRole,'host');
  assert(!JSON.stringify(e.audience.dialogue.snapshot()).includes('尚未展示'));
  e.completeSpeech(e._active.id);const completed=e.audience.getPublicEvents().filter(x=>x.speechId===progress.speechId);
  assert.equal(completed.length,1);assert.equal(completed[0].status,'completed');assert(completed[0].text.includes('尚未展示'));
});
await test('review API validates source metadata, isolates its prompt and binds verdicts server-side',async()=>{
  const origin='https://review-fixture.test',gate=new DialogueReview({request:async()=>{}});gate.record(event());
  const payload={requestId:crypto.randomUUID(),kind:'review',eventId:'review-1',context:{...gate.snapshot(),candidate:candidate('host')}};
  const req=body=>new Request(origin+'/api/agent',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','oai-authenticated-user-id':crypto.randomUUID()},body:JSON.stringify(body)});
  let calls=0;const fetch=async(_,init)=>{calls++;const body=JSON.parse(init.body);const input=JSON.parse(body.messages[1].content);
    assert.equal(input.candidate.role,'host');assert.equal(input.publicEvents[0].speakerRole,'host');assert(!body.messages[0].content.includes('你是自主统筹整场直播的 Z'));
    return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify({decision:'allow',reason:'独立审阅的模拟结论',evidence_ids:['public-1']})}}]});};
  const response=await handleAgent(req(payload),{DEEPSEEK_API_KEY:'fixture'},{fetch});assert.equal(response.status,200);const result=await response.json();
  assert.equal(result.candidateId,payload.context.candidate.id);assert.equal(result.revision,payload.context.revision);assert.match(result.promptVersion,/^[a-f0-9]{64}$/);
  for(const context of [{...payload.context,privateDraft:'not allowed'},{...payload.context,publicEvents:[{...payload.context.publicEvents[0],status:'unpublished'}]},{...payload.context,candidate:{...payload.context.candidate,speakerId:'moon'}}]){
    assert.equal((await handleAgent(req({...payload,requestId:crypto.randomUUID(),context}),{DEEPSEEK_API_KEY:'fixture'},{fetch})).status,400);
  }
  assert.equal(calls,1);
});
await test('review transport does not queue behind ongoing host generation',async()=>{
  const waiting=deferred(),seen=[];
  const client=new AgentClient({fetchImpl:async(_,options)=>{const p=JSON.parse(options.body);seen.push(p.kind);return p.kind==='review'?Response.json({...reviewReply(p)}):waiting.promise;}});
  const requestId=crypto.randomUUID();const host=client.generate({requestId,kind:'host'});
  await client.generate({requestId:crypto.randomUUID(),kind:'review',context:{candidate:candidate(),revision:0}});assert.deepEqual(seen,['host','review']);
  waiting.resolve(Response.json({requestId}));await host;
});
await test('trace is bounded, detached, and reset with the session',async()=>{
  const gate=new DialogueReview({request:async p=>reviewReply(p)});for(let i=0;i<150;i++)gate.log({stage:'fixture',index:i});
  const trace=gate.exportTrace();assert.equal(trace.records.length,120);trace.records[0].index=-1;assert.notEqual(gate.exportTrace().records[0].index,-1);
  gate.reset();assert.equal(gate.exportTrace().records.length,0);
});
console.log(`\n${count} dialogue review scenarios passed (offline transport and state checks; no model-quality claim).`);

// Concurrent call reviews accept only additive public facts from the other lane.
{
 const review=new DialogueReview({request:async p=>({decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'fixture'})});
 review.setConcurrent(true);
 review.record({id:'guest',type:'player.message',text:'累了'});
 review.record({id:'spoken',type:'speech.progress',speechId:'s1',text:'休息'});
 const audience={id:'reaction',role:'audience',speakerId:'moon',text:'歇一歇',evidenceIds:['guest'],replyTo:[]};
 const ticket=await review.inspect(audience);
 review.record({id:'spoken',type:'speech.progress',speechId:'s1',text:'休息一下'});
 review.invalidate('host');
 assert(review.commit(ticket,()=>{}),'monotonic playback and host-only cancellation preserve reviewed audience evidence');
 const hostTicket=await review.inspect({id:'host',role:'host',speakerId:'Z',text:'我听着',replyTo:['guest'],evidenceIds:['guest']});
 review.record({id:'npc',type:'npc.message',text:'歇一歇'});
 assert(review.commit(hostTicket,()=>{}),'a background reaction does not starve the host');
 const stale=await review.inspect({...audience,id:'stale'});review.record({id:'correction',type:'player.message',text:'刚刚说错了'});assert(!review.commit(stale,()=>{}),'a new confirmed player statement requires a fresh review');
 const changed=await review.inspect({...audience,id:'changed'});review.record({id:'spoken',type:'speech.progress',speechId:'s1',text:'这不是先前的前缀'});assert(!review.commit(changed,()=>{}),'rewriting evidence is not an additive update');
 const a=await review.inspect({...audience,id:'a'}),b=await review.inspect({...audience,id:'b'});assert(review.commit(a,()=>{}));assert(!review.commit(b,()=>{}),'same-lane concurrent tickets cannot both commit');
 const ended=await review.inspect({...audience,id:'ended'});review.setConcurrent(false);assert(!review.commit(ended,()=>{}),'call boundary invalidates tickets');
 console.log('PASS concurrent call review: additive prefixes, spectator appends, player corrections, reservations and call boundaries');
}
