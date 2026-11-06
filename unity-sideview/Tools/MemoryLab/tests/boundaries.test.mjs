import test from 'node:test';
import assert from 'node:assert/strict';
import { Store } from '../lib/store.mjs';
import { GameEngine } from '../lib/game-engine.mjs';
import { FixtureProvider } from './fixtures.mjs';
import { memoryState } from '../lib/natural-memory.mjs';
function setup(t,day=1){const store=new Store(),provider=new FixtureProvider(),engine=new GameEngine(store,provider,{timeoutMs:150});t.after(()=>store.close());const input={worldId:'boundary_world',runId:'run1',day,period:0};engine.open(input);return {store,provider,engine,input};}
test('cancelled conversation rejects even a provider that ignores cancellation',async t=>{
 const {engine,provider,input}=setup(t);let release;provider.delay=new Promise(r=>release=r);provider.ignoreAbort=true;
 const pending=engine.utterance({...input,requestId:'delayed',actorId:'c01',recipientId:'c02',scene:'Street',conversationId:'cancelled'});
 await new Promise(r=>setImmediate(r));engine.cancel({...input,conversationId:'cancelled'});release();
 await assert.rejects(pending);assert.equal(engine.requests.size,0);
 assert.equal(JSON.parse(engine.response(engine.assert(input)).checkpoint).messages.length,0);
});
test('timeout releases the request slot and subsequent generation recovers',async t=>{
 const {engine,provider,input}=setup(t);provider.delayMs=220;
 await assert.rejects(engine.utterance({...input,requestId:'slow',actorId:'c01',recipientId:'c02',scene:'Street',conversationId:'slow_pair'}));
 assert.equal(engine.requests.size,0);provider.delayMs=0;
 assert.ok((await engine.utterance({...input,requestId:'retry',actorId:'c01',recipientId:'c02',scene:'Street',conversationId:'retry_pair'})).proposalId);
});
test('no configured model still accepts authoritative Day4, but never fabricates speech',async t=>{
 const {engine,provider,input}=setup(t,4);provider.publicConfig=()=>({configured:false});
 const delivered=await engine.sync({...input,eventData:{id:'boundary_world_day4',seq:1,kind:'day4',day:4,period:0}});
 assert.equal(JSON.parse(delivered.checkpoint).memories.filter(m=>m.origin==='public_day4').length,7);
 await assert.rejects(engine.utterance({...input,requestId:'no_model',actorId:'c01',recipientId:'c02',scene:'Street',conversationId:'none'}),/尚未连接/);
 assert.equal(provider.calls.length,0);
});
test('game-confirmed observation respects witnesses and is the only path to fulfilled status',async t=>{
 const {engine,store,input}=setup(t);const exp=engine.assert(input);
 const promise={id:'promise1',experimentId:exp.id,epoch:exp.epoch,actorId:'c01',speakerId:'player',kind:'commitment',content:'玩家答应来帮忙。',gist:'有人答应帮忙。',subject:'帮忙',sourceLabel:'玩家说',evidence:[],dependencies:[],createdHour:exp.hour,importance:2,emotion:0,keywords:['帮忙'],recallCount:0,detailRecallCount:0,commitmentStatus:'pending'};
 store.put('memory',promise);
 const observation=await engine.sync({...input,eventData:{id:'observed1',seq:1,day:1,period:0,kind:'observation',location:'Street',title:'测试世界结果',text:'游戏确认玩家完成了测试动作。',gist:'玩家完成了实际动作。',observers:['c01','player'],fulfilledId:promise.id}});
 const state=JSON.parse(observation.checkpoint);
 assert.ok(state.memories.some(m=>m.actorId==='c01'&&m.kind==='commitment'&&m.commitmentStatus==='fulfilled'));
 assert.ok(!state.memories.some(m=>m.actorId==='c02'&&m.origin==='observation'));
 assert.ok(state.memories.find(m=>m.id===promise.id).supersededBy);
});
test('a spoken completion claim cannot fulfil a pending promise or mutate a world fact',async t=>{
 const {engine,store,provider,input}=setup(t);const exp=engine.assert(input);
 store.put('memory',{id:'pending_promise',experimentId:exp.id,epoch:exp.epoch,actorId:'c01',speakerId:'player',kind:'commitment',content:'玩家答应完成修理。',gist:'玩家答应修理。',subject:'修理',sourceLabel:'玩家说',evidence:[],dependencies:[],createdHour:exp.hour,importance:2,emotion:0,keywords:['修理'],recallCount:0,detailRecallCount:0,commitmentStatus:'pending'});
 const base=provider.call.bind(provider);provider.call=async(req,options)=>{
  if(req.messages[0].content.includes('为观察者 NPC 整理')){const p=JSON.parse(req.messages[1].content),m=p.messages[0];return {text:JSON.stringify({memories:[{kind:'commitment',content:'玩家说已经完成修理。',gist:'玩家声称修好了。',subject:'修理',evidence:[{id:m.id,quote:m.text}],replaces:'pending_promise',commitmentStatus:'fulfilled'}],used_memories:[]})};}
  return base(req,options);
 };
 const result=await engine.sync({...input,eventData:{id:'claim',seq:1,kind:'utterance',day:1,period:0,location:'Street',conversationId:'claim_pair',speakerId:'player',recipientId:'c01',text:'我已经完成修理了。',observers:['c01','player']}});
 assert.ok(!JSON.parse(result.checkpoint).memories.some(m=>m.commitmentStatus==='fulfilled'));
 assert.ok(!JSON.parse(result.checkpoint).events.some(e=>e.gameKind==='quest_completed'));
});
test('faded details stay absent from generation and reflection requests',async t=>{
 const {engine,store,provider,input}=setup(t,20);const exp=engine.assert(input);
 store.put('memory',{id:'old_detail',experimentId:exp.id,epoch:exp.epoch,actorId:'c01',kind:'experience',content:'晴子搬过 BLUE_DETAIL_17 号箱。',gist:'晴子曾帮忙搬东西。',subject:'晴子',sourceLabel:'亲历',evidence:[],dependencies:[],createdHour:0,importance:2,emotion:0,keywords:['晴子'],recallCount:0,detailRecallCount:0});
 await engine.intent({...input,requestId:'faded_intent',actorId:'c01',scene:'Street',candidates:['c02']});
 assert.ok(!JSON.stringify(provider.calls).includes('BLUE_DETAIL_17'));
});
test('reflections preserve subjectivity and old evidence replacement invalidates an opinion',t=>{
 const {engine,store,input}=setup(t);const exp=engine.assert(input);
 const evidence={id:'evidence',experimentId:exp.id,epoch:exp.epoch,actorId:'c01',kind:'hearsay',content:'晴子说需要时间。',gist:'对方说需要时间。',createdHour:exp.hour,importance:1,emotion:0,supersededBy:'replacement'};
 const opinion={id:'opinion',experimentId:exp.id,epoch:exp.epoch,actorId:'c01',kind:'reflection',content:'我的看法（可能有误）：也许她正在忙。',gist:'我的看法：对方也许忙。',dependencies:['evidence'],createdHour:exp.hour,importance:2,emotion:0};
 assert.equal(memoryState(opinion,exp,[evidence,opinion]).code,'review');
});
test('checkpoint response revisions are monotonic to prevent out-of-order client cache rollback',t=>{
 const {engine,input}=setup(t);const a=engine.response(engine.assert(input)),b=engine.response(engine.assert(input));
 assert.ok(b.responseRevision>a.responseRevision);assert.equal(a.seq,b.seq);
});
