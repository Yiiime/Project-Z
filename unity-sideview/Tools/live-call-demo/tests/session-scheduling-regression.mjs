import assert from 'node:assert/strict';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {SessionContinuity,validateAgendaUpdates} from '../ui/session-continuity.js';
import {AgentClient} from '../ui/agent-client.js';
import worker from '../dist/server/index.js';
import {reviewReply} from './review-fixture.mjs';
const flush=async()=>{for(let i=0;i<30;i++)await Promise.resolve();};
const answer=(extra={})=>({action:'speak',speech:'刚才聊到的小屋窗边，我想接着讲一小段。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:2000,continuation:'continue',memory:'接续窗边的话题',agendaUpdates:[],...extra});
const make=(generate)=>{const e=new ConversationEngine();e.configureAgent({generate,cancel(){}},{enabled:true});e.setSpeechDriven(true);e.start();clearInterval(e._timer);return e;};
const progress=e=>e.recordSpeechProgress(e._active.id,e._active.text.slice(0,Math.ceil(e._active.text.length*.5)));
const finish=e=>{e.completeSpeech(e._active.id);e._clock=e.nextWake;};
const test=async(name,run)=>{await run();console.log('PASS '+name);};
await test('prepare overlaps speech, never writes facts or agenda, then reviews against the completed live turn',async()=>{
  const calls=[];let resolveDraft;
  const e=make(async(p)=>{calls.push(p);if(p.kind==='review')return reviewReply(p);if(p.context.host.turn.mode==='prepare')return new Promise(r=>resolveDraft=r);return answer({agendaUpdates:[{id:null,goal:'接着聊窗边',nextStep:'分享下一段观察',status:'open',evidenceIds:['current_speech']}]});});
  try{
    await e._think();assert.equal(e.session.items.length,0);assert.equal(e.memory,'');
    progress(e);e._maybePrepare();assert(e.preparing);assert.equal(e.pending,null);assert.equal(e.audience.canCommit(),true);
    e._maybePrepare();assert.equal(calls.filter(c=>c.context?.host?.turn.mode==='prepare').length,1);
    resolveDraft(answer({speech:'接着刚才的话，我更喜欢窗边安静的那一刻。',continuation:'leave_space'}));await flush();
    assert(e.prepared);assert.equal(e.session.items.length,0);assert.equal(calls.filter(p=>p.kind==='review').length,1);
    assert(!e.facts.some(f=>f.text===e.prepared.result.speech));
    finish(e);assert.equal(e.session.items.length,1);assert(e.memory);await e._think();
    assert.equal(e._active.text,'接着刚才的话，我更喜欢窗边安静的那一刻。');
    assert.equal(calls.filter(p=>p.kind==='host').length,2);
    const gate=calls.filter(p=>p.kind==='review').at(-1);
    assert(gate.context.publicEvents.some(f=>f.type==='speech.completed'));
    assert(gate.context.sessionEvents.every(f=>f.type!=='speech.progress'));
    assert(!Object.hasOwn(gate.context,'host'));assert(!Object.hasOwn(gate.context,'experience'));
    finish(e);e._maybePrepare();assert(!e.preparing);
  }finally{e.destroy();}
});
await test('new player preempts an in-flight draft, remains pending, and wakes immediately after current speech',async()=>{
  let resolveDraft,signal;const e=make(async(p,o)=>{if(p.kind==='review')return reviewReply(p);if(p.context.host.turn.mode==='prepare'){signal=o.signal;return new Promise(r=>resolveDraft=r);}return answer({handledIds:p.context.host.inbox.map(m=>m.id)});});
  try{await e._think();progress(e);e._maybePrepare();e.sendMessage('先回答我的问题');assert(signal.aborted);resolveDraft(answer({speech:'失效的准备'}));await flush();assert(!e.prepared);assert.equal(e.inbox.length,1);e.completeSpeech(e._active.id);assert.equal(e.nextWake,e._clock);await e._think();assert.notEqual(e._active.text,'失效的准备');assert.equal(e.inbox.length,1);finish(e);assert.equal(e.inbox.length,0);}finally{e.destroy();}
});
await test('pause, replay, tool receipts and destroy invalidate prepared work; late provider replies stay inert',async()=>{
  for(const change of [e=>e.setPaused(true),e=>e.replay(),e=>e._toolEvent('画作已展示'),e=>e.destroy()]){
    let resolveDraft;const e=make(async p=>p.kind==='review'?reviewReply(p):p.context.host.turn.mode==='prepare'?new Promise(r=>resolveDraft=r):answer());
    try{await e._think();progress(e);e._maybePrepare();change(e);resolveDraft(answer());await flush();assert(!e.prepared);assert(!e.preparing);}finally{e.destroy();}
  }
});
await test('await-event and tool turns do not speculatively execute the next activity',async()=>{
  for(const continuation of ['await_event']){let hosts=0;const e=make(async p=>{if(p.kind==='review')return reviewReply(p);hosts++;return answer({continuation});});try{await e._think();progress(e);e._maybePrepare();assert.equal(hosts,1);}finally{e.destroy();}}
  let executed=0;const e=make(async p=>p.kind==='review'?reviewReply(p):answer({action:'tool',tool:{name:'test',args:{}}}));e._executeTool=()=>executed++;
  try{await e._think();progress(e);e._maybePrepare();assert.equal(executed,0);assert(!e.preparing);finish(e);assert.equal(executed,1);}finally{e.destroy();}
});
await test('rejected prepared candidate cannot publish, mutate agenda, or execute a tool',async()=>{
  let gates=0;const e=make(async p=>p.kind==='review'?reviewReply(p,++gates===1?'allow':'drop'):answer({speech:p.context.host.turn.mode==='prepare'?'后来要说的内容':'现在的内容'}));
  try{await e._think();progress(e);e._maybePrepare();await flush();finish(e);const memory=e.memory;await e._think();assert.equal(e._active,null);assert.equal(e.memory,memory);assert.equal(e.session.items.length,0);}finally{e.destroy();}
});
await test('session agenda keeps source evidence beyond the recent window, validates references and resets without cross-session data',async()=>{
  const s=new SessionContinuity(),source={id:'question',type:'player.message',text:'接下来聊聊画作',speakerRole:'player'};s.record(source);
  const snapshot=s.snapshot();s.apply([{id:null,goal:'聊画作',nextStep:'接回画后讨论',status:'open',evidenceIds:['question']}],snapshot);
  for(let i=0;i<90;i++)s.record({id:'new-'+i,type:'npc.message',text:'后续消息'});
  assert(s.snapshot().evidence.some(e=>e.id==='question'));assert.equal(s.snapshot().unfinishedItems.length,1);
  assert.throws(()=>validateAgendaUpdates([{id:null,goal:'不存在的过去',nextStep:'假回忆',status:'open',evidenceIds:['made-up']}],{evidence:s.evidence()}));
  assert.throws(()=>validateAgendaUpdates([{id:null,goal:'未播完',nextStep:'假完成',status:'open',evidenceIds:['current_speech']}],{}));
  s.record({id:'prefix',type:'speech.progress',text:'还没播完'});assert(!s.evidence().some(e=>e.id==='prefix'));
  s.reset();assert.equal(s.items.length,0);assert.equal(s.events.length,0);assert.deepEqual(s.snapshot().crossSessionMemory,{available:false,entries:[]});
});
await test('real client and built Worker accept session context, preparation, agenda updates and latest public review evidence',async()=>{
  const original=globalThis.fetch,wires=[];let spoken=0;
  globalThis.fetch=async(url,o)=>{const input=JSON.parse(JSON.parse(o.body).messages[1].content);const result=input.candidate?{decision:'allow',reason:'有本场依据并自然接续',evidence_ids:input.sessionEvents?.slice(0,1).map(e=>e.id)||[]}:answer({speech:input.context.host.turn.mode==='prepare'?'这是提前准备的下一段。':'这是当前正在说的话。',agendaUpdates:[{id:null,goal:'继续这个话题',nextStep:'补充细节',status:'open',evidenceIds:['current_speech']}]});return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(result)}}]});};
  const origin='https://session-transport.test';
  const client=new AgentClient({fetchImpl:async(path,o)=>{wires.push(JSON.parse(o.body));const res=await worker.fetch(new Request(origin+path,{...o,headers:{...o.headers,Origin:origin,'oai-authenticated-user-id':'session-test'}}),{DEEPSEEK_API_KEY:'fixture'});assert.equal(res.status,200,await res.clone().text());return res;}});
  const config=await(await worker.fetch(new Request(origin+'/api/agent/config'),{DEEPSEEK_API_KEY:'fixture'})).json();
  const e=new ConversationEngine();e.configureAgent(client,config);e.setSpeechDriven(true);e.start();clearInterval(e._timer);e.addEventListener('speak',()=>spoken++);
  try{await e._think();progress(e);e._maybePrepare();for(let i=0;i<100&&e.preparing;i++)await new Promise(r=>setTimeout(r,5));assert(e.prepared);assert.equal(spoken,1);finish(e);await e._think();assert.equal(spoken,2);assert.equal(e._active.text,'这是提前准备的下一段。');assert.equal(e.session.items.length,1);assert(wires.some(p=>p.context.host?.turn.mode==='prepare'));}finally{e.destroy();globalThis.fetch=original;}
});
await test('failed preparation does not retry while speaking and releases the next live decision',async()=>{
  let prepares=0,hosts=0;const e=make(async p=>{if(p.kind==='review')return reviewReply(p);if(p.context.host.turn.mode==='prepare'){prepares++;throw Error('temporary');}hosts++;return answer();});
  try{await e._think();progress(e);e._maybePrepare();await flush();e._maybePrepare();assert.equal(prepares,1);assert(!e.preparing);finish(e);await e._think();assert.equal(hosts,2);assert(e._active);}finally{e.destroy();}
});
await test('real client and Worker keep the preparation lane independent from foreground host decisions',async()=>{
  const original=globalThis.fetch;let release,started;const began=new Promise(r=>started=r),origin='https://prepare-lane.test';
  globalThis.fetch=async(url,o)=>{const input=JSON.parse(JSON.parse(o.body).messages[1].content);if(input.context.host.turn.mode==='prepare'){started();await new Promise(r=>release=r);}return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(answer())}}]});};
  const client=new AgentClient({fetchImpl:async(path,o)=>worker.fetch(new Request(origin+path,{...o,headers:{...o.headers,Origin:origin,'oai-authenticated-user-id':'prepare-lane'}}),{DEEPSEEK_API_KEY:'fixture'})});
  const payload=mode=>({kind:'host',eventId:'lane-'+mode,context:{host:{inbox:[],memory:'',turn:{mode,...(mode==='prepare'?{inProgress:{speechId:'1',text:'当前话'}}:{})}}}});
  try{const preparation=client.generate(payload('prepare'));await began;const foreground=await client.generate(payload('live'));assert.equal(foreground.action,'speak');release();assert.equal((await preparation).action,'speak');}finally{client.cancel();release?.();globalThis.fetch=original;}
});
await test('retained player evidence cannot re-enable a disabled talent',async()=>{
  const {parseAgentReply}=await import('../worker/agent-director.js');
  const e=make(async p=>p.kind==='review'?reviewReply(p):answer());
  try{
    e.sendMessage('稍后想让我自己选一张牌，看看最近的状态');const source=e.inbox[0];
    e.session.apply([{id:null,goal:'稍后抽牌',nextStep:'观众亲选',status:'deferred',evidenceIds:[source.id]}],e.session.snapshot());
    e.inbox=[];e._state.messages=[];
    const context=e._hostContext([]),reply=answer({action:'tool',tool:{name:'start_tarot',args:{origin:'viewer',question:'最近的状态',spread:'single',requesterId:source.id}}});
    assert.throws(()=>parseAgentReply(reply,{kind:'host',context}),e=>e.code==='capability_disabled');
    assert.equal(e._executeTool(reply.tool),false);assert.equal(e._state.play.tarot,null);
    context.host.session.evidence.find(x=>x.id===source.id).speakerRole='audience';assert.throws(()=>parseAgentReply(reply,{kind:'host',context}));
  }finally{e.destroy();}
});
await test('candidate generation starts before first caption and warms audio before final review',async()=>{
  const order=[],warm=new Map();const e=make(async p=>{order.push(p.kind==='host'?p.context.host.turn.mode:'review');return p.kind==='review'?reviewReply(p):answer();});
  e.configureSpeech(Object.assign(new EventTarget(),{warmSpeech(key,text){warm.set(key,text);order.push('warm');},dropWarmSpeech(key){if(key)warm.delete(key);else warm.clear();}}));
  try{await e._think();assert(order.indexOf('warm')<order.indexOf('review'));assert(!e.facts.some(f=>f.type==='speech.progress'));e._maybePrepare();await flush();assert(e.prepared);assert(warm.has(e.prepared.requestId));assert.equal(e.nextWake,e._clock+350);e.sendMessage('换个话题');assert.equal(warm.size,0);}finally{e.destroy();}
});
await test('a player arriving during speech starts preparation immediately even when Z planned to leave space',async()=>{
  const calls=[];const e=make(async p=>{calls.push(p);return p.kind==='review'?reviewReply(p):answer({continuation:'leave_space',wakeAfterMs:9000});});
  try{await e._think();const active=e._active.id;e.sendMessage('先讲我这个问题');e._maybePrepare();await flush();assert.equal(e._active.id,active);assert(e.prepared);const next=calls.find(p=>p.context.host?.turn.mode==='prepare');assert(next.context.host.inbox.some(m=>m.text==='先讲我这个问题'));assert.equal(e.memory,'');}finally{e.destroy();}
});
await test('continue uses a short technical gap while meaningful waiting retains the model duration',async()=>{
  const e=make(async()=>answer());try{assert.equal(e._wakeDelay(answer({wakeAfterMs:9000})),350);assert.equal(e._wakeDelay(answer({continuation:'leave_space',wakeAfterMs:9000})),9000);assert.equal(e._wakeDelay(answer({action:'wait',speech:'',wakeAfterMs:12000})),12000);}finally{e.destroy();}
});
await test('leave-space prepares a fallback but cannot shorten the response window',async()=>{
  const e=make(async p=>p.kind==='review'?reviewReply(p):answer({continuation:'leave_space',wakeAfterMs:9000}));
  try{await e._think();e._maybePrepare();await flush();assert(e.prepared);e.completeSpeech(e._active.id);assert.equal(e.nextWake,e._clock+9000);await e._think();assert.equal(e._active,null);assert(e.prepared);e._clock=e.nextWake;await e._think();assert(e._active);}finally{e.destroy();}
});
await test('temporary host failure uses bounded backoff instead of an immediate fifteen-second stall',async()=>{
  const e=make(async()=>{throw Object.assign(Error('temporary'),{code:'agent_busy'});});
  try{for(const expected of [1000,2000,4000,15000]){e.nextWake=e._clock;await e._think();assert.equal(e.nextWake-e._clock,expected);}assert.equal(e._active,null);}finally{e.destroy();}
});
