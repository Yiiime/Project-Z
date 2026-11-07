import assert from 'node:assert/strict';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {AudienceDirector} from '../ui/audience-director.js';
import {AgentClient} from '../ui/agent-client.js';
import worker from '../dist/server/index.js';
import {reviewReply} from './review-fixture.mjs';
const flush=async()=>{for(let i=0;i<50;i++)await Promise.resolve();};
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve};};
const speech='我想聊聊房间布置，接下来这个想法是给窗边留一把椅子。';
const audienceText='窗边还想放一盏阅读灯';
const responseText='阅读灯可以放在椅子旁边，我想让它方便随手开关。';
const answer=(extra={})=>({action:'speak',speech,mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:9000,continuation:'leave_space',memory:'',agendaUpdates:[],...extra});
const config={enabled:true,policy:{audience:{handoff:true,maxConcurrent:2,maxPoolSize:3,intervalMs:1500,generationDeadlineMs:3500,candidateTtlMs:12000,publishGapMs:2500}}};
function harness({generate,verdict='allow',noPreviewRef=false}={}){
  const calls=[],spoken=[],warm=new Map(),e=new ConversationEngine();
  e.configureAgent({cancel(){},generate:(p,o)=>{calls.push({p,o});if(generate)return generate(p,o);if(p.kind==='review')return Promise.resolve(reviewReply(p,verdict));if(p.kind==='audience')return Promise.resolve({status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],intent:'reaction',text:audienceText,evidence_refs:[!noPreviewRef&&p.context.audience.hostPreview?p.context.audience.hostPreview.id:p.context.publicEvents.at(-1).id]}]});return Promise.resolve(p.context.host.pendingAudience?answer({speech:responseText,handledIds:[p.context.host.pendingAudience.messageId]}):answer());}},config);
  e.configureSpeech(Object.assign(new EventTarget(),{warmSpeech(key,text){warm.set(key,text);},dropWarmSpeech(key){if(key)warm.delete(key);else warm.clear();}}));
  e.addEventListener('speak',({detail})=>spoken.push({text:detail.text,publicEvents:e.audience.getPublicEvents()}));e.setSpeechDriven(true);e.start();clearInterval(e._timer);return {e,calls,spoken,warm};
}
let count=0;const test=async(name,fn)=>{await fn();count++;console.log('PASS '+name);};
await test('approved host text reaches a separate preview before audio/captions; rejected host text never does',async()=>{
  const h=harness();try{await h.e._think();h.e.audience.tick();await flush();const wires=h.calls.filter(c=>c.p.kind==='audience').map(c=>c.p);assert.equal(wires.length,2);for(const p of wires){assert.equal(p.context.audience.hostPreview.text,speech);assert(!p.context.publicEvents.some(e=>e.text===speech));assert.equal(p.context.audience.hostPreview.status,'committed');}assert(!h.e.session.events.some(e=>e.text===speech));}finally{h.e.destroy();}
  const rejected=harness({verdict:'drop'});try{await rejected.e._think();assert.equal(rejected.e.audience.hostPreview,null);assert.equal(rejected.spoken.length,0);}finally{rejected.e.destroy();}
});
await test('even permissive reviewers cannot publish future-dependent comments during partial speech, including unreported dependencies',async()=>{
  for(const noPreviewRef of [false,true]){const h=harness({noPreviewRef});try{
    await h.e._think();h.e.audience.tick();await flush();const original=h.e._active.id;assert(h.e.audience.selected);assert(h.e.prepared);assert.equal(h.e.inbox.length,0);assert.equal(h.spoken.length,1);
    h.e.recordSpeechProgress(original,'我想聊聊房间布置');h.e.audience.tick();await h.e._think();await flush();assert.equal(h.spoken.length,1);assert(!h.calls.some(c=>c.p.kind==='review'&&c.p.context.candidate.role==='audience'));assert.equal(h.e._state.messages.filter(m=>m.source==='agent-audience').length,0);
    // No declared-ref heuristic can bypass the lifecycle dependency.
    h.e.completeSpeech(original);h.e.audience.tick();await flush();const gate=h.calls.find(c=>c.p.kind==='review'&&c.p.context.candidate.role==='audience').p;
    assert(gate.context.publicEvents.some(e=>e.type==='speech.completed'&&e.text===speech));assert(!JSON.stringify(gate.context).includes('hostPreview'));h.e.audience.tick();assert.equal(h.e._state.messages.filter(m=>m.source==='agent-audience').length,1);
  }finally{h.e.destroy();}}
});
await test('one selected unpublished comment prepares host text/audio, but only real publication permits speech',async()=>{
  const h=harness();try{
    await h.e._think();h.e.audience.tick();await flush();const dependency={...h.e.audiencePreview},draft=h.e.prepared;
    assert.equal(dependency.status,'pending');assert.equal(draft.context.host.pendingAudience.messageId,dependency.messageId);assert(!draft.context.host.inbox.some(m=>m.id===dependency.messageId));assert(h.warm.has(draft.requestId));assert.equal(h.e.memory,'');assert.equal(h.e.session.items.length,0);
    h.e.completeSpeech(h.e._active.id);h.e._clock=h.e.nextWake;await h.e._think();assert.equal(h.spoken.length,1);assert.equal(h.e.pending,null);
    h.e.audience.tick();await flush();h.e.audience.tick();assert(h.e.inbox.some(m=>m.id===dependency.messageId));assert.equal(h.e.audiencePreview.status,'published');assert.equal(h.e.nextWake,h.e._clock);
    await h.e._think();assert.equal(h.spoken.length,2);assert.equal(h.spoken[1].text,responseText);assert(h.spoken[1].publicEvents.some(e=>e.id===dependency.messageId&&e.text===audienceText));
    const review=h.calls.filter(c=>c.p.kind==='review'&&c.p.context.candidate.role==='host').at(-1).p;assert(!Object.hasOwn(review.context,'pendingAudience'));assert(review.context.publicEvents.some(e=>e.id===dependency.messageId));
  }finally{h.e.destroy();}
});
await test('host drafts and unpublished peer comments never flow back into audience generation or public history',async()=>{
  const h=harness();try{await h.e._think();h.e.audience.tick();await flush();assert.equal(h.e.prepared.result.speech,responseText);h.e._clock=1600;h.e.audience.tick();await flush();const wires=h.calls.filter(c=>c.p.kind==='audience').map(c=>c.p);assert.equal(wires.length,3);for(const p of wires){assert(!JSON.stringify(p).includes(responseText));assert(!JSON.stringify(p).includes(audienceText));}assert(!JSON.stringify(h.e.audience.getPublicEvents()).includes(responseText));assert(!JSON.stringify(h.e.session.snapshot()).includes(audienceText));}finally{h.e.destroy();}
});
await test('rejected audience cancels its prepared response and warmed audio without tool or memory effects',async()=>{
  let tools=0;const h=harness({generate:async p=>p.kind==='review'?reviewReply(p,p.context.candidate.role==='audience'?'drop':'allow'):p.kind==='audience'?{status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],intent:'reaction',text:audienceText,evidence_refs:[p.context.audience.hostPreview.id]}]}:p.context.host.pendingAudience?answer({action:'tool',speech:responseText,handledIds:[p.context.host.pendingAudience.messageId],tool:{name:'test',args:{}}}):answer()});h.e._executeTool=()=>tools++;
  try{await h.e._think();h.e.audience.tick();await flush();const draft=h.e.prepared;assert(h.warm.has(draft.requestId));h.e.completeSpeech(h.e._active.id);h.e.audience.tick();await flush();assert.equal(h.e.prepared,null);assert.equal(h.e.audiencePreview,null);assert(!h.warm.has(draft.requestId));assert.equal(tools,0);assert.equal(h.spoken.length,1);assert.equal(h.e.session.items.length,0);}finally{h.e.destroy();}
});
await test('new player, pause, replay, tool receipt and destroy revoke both directions, including late non-cooperative replies',async()=>{
  for(const change of [e=>e.sendMessage('我想先换个话题'),e=>e.setPaused(true),e=>e.replay(),e=>e._toolEvent('实际场景改变'),e=>e.destroy()]){
    const late=deferred();let preparingSignal;const h=harness({generate:(p,o)=>p.kind==='review'?Promise.resolve(reviewReply(p)):p.kind==='audience'?Promise.resolve({status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],intent:'reaction',text:audienceText,evidence_refs:[p.context.audience.hostPreview.id]}]}):p.context.host.pendingAudience?(preparingSignal=o.signal,late.promise):Promise.resolve(answer())});
    try{await h.e._think();h.e.audience.tick();await flush();assert(h.e.audiencePreview);change(h.e);assert(preparingSignal.aborted);late.resolve(answer({speech:'不可复活的迟到回应'}));await flush();assert.notEqual(h.e.prepared?.result.speech,'不可复活的迟到回应');assert.equal(h.e.audiencePreview,null);assert.equal(h.e.audience.hostPreview,null);assert(!h.spoken.some(s=>s.text==='不可复活的迟到回应'));}finally{h.e.destroy();}
  }
});
await test('idle host prewarms a selected comment while its review is pending without blocking that review',async()=>{
  const gate=deferred();let review;const h=harness({generate:async p=>p.kind==='review'?(review=p,gate.promise):p.kind==='audience'?{status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],intent:'reaction',text:audienceText,evidence_refs:[p.context.publicEvents[0].id]}]}:answer({speech:responseText,handledIds:[p.context.host.pendingAudience.messageId]})});
  try{h.e.nextWake=9000;h.e.audience.tick();await flush();assert(h.e.prepared);assert(!h.e.prepared.context.host.turn.inProgress);assert.equal(h.e.pending,null);h.e.nextWake=0;await h.e._think();assert.equal(h.spoken.length,0);gate.resolve(reviewReply(review));await flush();h.e.audience.tick();assert.equal(h.e.audiencePreview.status,'published');}finally{h.e.destroy();}
});
await test('expired or mismatched publication cannot authorize a prepared answer',async()=>{
  const h=harness();try{await h.e._think();h.e.audience.tick();await flush();const old=h.e.prepared,dep=old.audienceDependency;h.e.completeSpeech(h.e._active.id);h.e.audience.clearPreparation('replacement');h.e.prepared=old;h.e._public('npc.message','被替换的内容',{id:dep.messageId,speakerRole:'audience',speakerId:dep.npcId});h.e.prepared=old;h.e.nextWake=0;await h.e._think();assert(!h.spoken.some(s=>s.text===responseText));}finally{h.e.destroy();}
  let now=0;const ended=[];const d=new AudienceDirector({now:()=>now,config:{handoff:true},publish:()=>assert.fail('expired preview'),onSelectionEnd:e=>ended.push(e),onSelected:()=>{},request:async p=>({status:'ready',items:[{npc_id:'wind',text:'一条预备反馈',intent:'reaction',evidence_refs:[p.context.audience.hostPreview.id]}]})});d.record({id:'public',type:'session.opened',text:'直播开始'});d.setHostPreview({speechId:'9',text:'尚未说完的台词'});d.tick();await flush();now=120000;d.tick();assert(ended.some(e=>e.reason==='host_preview_expired'));assert.equal(d.selected,null);d.invalidate();
});
await test('published audience wakes the idle host and does not get overwritten by the old speech waiting interval',async()=>{
  const h=harness();try{await h.e._think();h.e.audience.clearPreparation('test_no_selection');h.e.audiencePreview=null;const m=h.e._message('接着刚才那个话题',{name:'海盐汽水',type:'chat'});h.e._receive(m,false,'salt');assert.equal(h.e.nextWake,h.e._clock);h.e.completeSpeech(h.e._active.id);assert.equal(h.e.nextWake,h.e._clock);}finally{h.e.destroy();}
});
await test('real client and built Worker preserve both preview schemas, public-only review, and exact message identity',async()=>{
  const origin='https://text-handoff.test',original=globalThis.fetch,wires=[],upstream=[];
  globalThis.fetch=async(url,o)=>{const input=JSON.parse(JSON.parse(o.body).messages[1].content);upstream.push(input);const result=input.candidate?{decision:'allow',reason:'fixture only',evidence_ids:[]}:input.allowed_candidate_npc_ids?{status:'ok',npc_id:input.allowed_candidate_npc_ids[0],intent:'reaction',text:audienceText,evidence_ids:[input.host_preview?.id||input.shared_context[0].id]}:input.context.host.pendingAudience?answer({speech:responseText,handledIds:[input.context.host.pendingAudience.messageId]}):answer();return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(result)}}]});};
  const client=new AgentClient({fetchImpl:async(path,o)=>{wires.push(JSON.parse(o.body));const response=await worker.fetch(new Request(origin+path,{...o,headers:{...o.headers,Origin:origin,'oai-authenticated-user-id':'handoff'}}),{DEEPSEEK_API_KEY:'fixture'});assert.equal(response.status,200,await response.clone().text());return response;}}),e=new ConversationEngine();
  try{const cfg=await(await worker.fetch(new Request(origin+'/api/agent/config'),{DEEPSEEK_API_KEY:'fixture'})).json();e.configureAgent(client,{...cfg,topic:{enabled:false}});e.setSpeechDriven(true);e.start();clearInterval(e._timer);await e._think();e.audience.tick();for(let i=0;i<100&&!e.prepared;i++)await new Promise(r=>setTimeout(r,5));assert(e.prepared);assert(e.prepared.context.host.pendingAudience);assert(upstream.some(p=>p.host_preview));assert(upstream.filter(p=>p.candidate).every(p=>!p.host_preview&&!p.pendingAudience));const dep=e.audiencePreview.messageId;e.completeSpeech(e._active.id);e.audience.tick();for(let i=0;i<100&&e.audience.reviewPending;i++)await new Promise(r=>setTimeout(r,5));e.audience.tick();await e._think();assert.equal(e._active.text,responseText);assert(e.inbox.some(m=>m.id===dep));
  }finally{e.destroy();globalThis.fetch=original;}
});
await test('topic assessment remains independent and cannot overwrite a selected audience preparation',async()=>{
  const judgment=deferred();let topicPayload,topicSignal;
  const h=harness({generate:(p,o)=>p.kind==='topic'?(topicPayload=p,topicSignal=o.signal,judgment.promise):p.kind==='review'?Promise.resolve(reviewReply(p)):p.kind==='audience'?Promise.resolve({status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],text:audienceText,intent:'reaction',evidence_refs:[p.context.audience.hostPreview.id]}]}):Promise.resolve(p.context.host.pendingAudience?answer({speech:responseText,handledIds:[p.context.host.pendingAudience.messageId]}):answer())});
  h.e.topicEnabled=true;
  try{await h.e._think();h.e._maybeAssessTopic();h.e.audience.tick();await flush();const prepared=h.e.prepared;assert(prepared);assert.equal(topicSignal.aborted,false);judgment.resolve({status:'complete',reason:'本段已收束',nextDirection:'可选择新的方向',evidenceIds:[],anchorSpeechId:topicPayload.context.inProgress.speechId});await flush();assert.equal(h.e.topicProgress.status,'complete');assert.equal(h.e.prepared,prepared);assert(!JSON.stringify(topicPayload).includes(audienceText));}finally{h.e.destroy();}
});
await test('Worker rejects extra hidden preview fields and pending audience outside preparation',async()=>{
  const origin='https://invalid-handoff.test';const request=body=>worker.fetch(new Request(origin+'/api/agent',{method:'POST',headers:{'Content-Type':'application/json',Origin:origin,'oai-authenticated-user-id':'invalid-handoff'},body:JSON.stringify({requestId:crypto.randomUUID(),eventId:'public',...body})}),{DEEPSEEK_API_KEY:'fixture'});
  const preview={id:'p',speechId:'1',publicEventId:'speech-1',text:speech,status:'committed'};
  for(const p of [{...preview,hiddenDeck:['secret']},{...preview,status:'draft'},{...preview,publicEventId:'another-speech'}])assert.equal((await request({kind:'audience',context:{publicEvents:[{id:'public',type:'session.opened',text:'开始'}],audience:{hostPreview:p}}})).status,400);
  const pendingAudience={id:'c',messageId:'m',npcId:'wind',text:audienceText,status:'pending'};assert.equal((await request({kind:'host',context:{host:{inbox:[],turn:{mode:'live'},pendingAudience}}})).status,400);
});
console.log(`\n${count} text handoff scenarios passed (transport/state fixtures; no live model-quality claim).`);
