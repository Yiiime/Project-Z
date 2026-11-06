import test from 'node:test';
import assert from 'node:assert/strict';
import { Store } from '../lib/store.mjs';
import { GameEngine,BOOK } from '../lib/game-engine.mjs';
import { PUBLIC_DAY4 } from '../lib/game-protocol.mjs';
import { strength,memoryState,retrievalCandidates } from '../lib/natural-memory.mjs';
import { FixtureProvider } from './fixtures.mjs';
function setup(t,day=1){const s=new Store(),p=new FixtureProvider(),e=new GameEngine(s,p);t.after(()=>s.close());const input={worldId:'worldA',runId:'runA',day,period:0};const opened=e.open(input);return {s,p,e,input,opened};}
async function emit(e,input,event){return e.sync({...input,eventData:{day:input.day,period:input.period,...event}});}
test('actual seven-person roster, public profiles and private knowledge are isolated',async t=>{
 const {e,p,input}=setup(t);assert.deepEqual(BOOK.actors.map(a=>a.name),['留洋','晴子','梨园','伊停','周结','正义','星亮']);
 await e.intent({...input,requestId:'decision1',actorId:'c01',scene:'Street',candidates:[{id:'c05'}]});
 const req=p.calls.find(c=>c.messages[0].content.includes('主动找一个在场NPC'));
 const payload=JSON.parse(req.messages[1].content);
 assert.equal(payload.candidates[0].role,'照相馆老板');assert.equal(payload.publicFacts.length,0);
 assert.ok(!JSON.stringify(payload).includes('幕后导演'));assert.ok(!JSON.stringify(payload).includes('撤岛与替代安排'));
 assert.throws(()=>e.actor('z'));assert.throws(()=>e.actor('aning'));
});
test('Day4 is authoritative, covers all seven off-scene actors, and is idempotent after save/load',async t=>{
 const {e,input}=setup(t,3);const day4={...input,day:4};
 await emit(e,day4,{id:'clock1',seq:1,kind:'clock'});
 const event={id:'worldA_day4',seq:2,kind:'day4',day:4,period:0};
 const state=await emit(e,day4,event);const snap=JSON.parse(state.checkpoint);
 assert.equal(snap.memories.filter(m=>m.origin==='public_day4').length,7);
 assert.equal(snap.memories.filter(m=>m.origin==='staff_day4').length,7);
 for(const a of BOOK.actors)assert.deepEqual(e.context(e.assert(day4),a.id,{scene:'Street'}).publicFacts,[PUBLIC_DAY4]);
 await emit(e,day4,event);assert.equal(JSON.parse(e.response(e.assert(day4)).checkpoint).memories.length,snap.memories.length);
 const loaded=e.open({...day4,runId:'runB',checkpoint:state.checkpoint});
 await emit(e,{...day4,runId:'runB'},event);assert.equal(JSON.parse(loaded.checkpoint).memories.length,snap.memories.length);
});
test('both speakers are generated separately, proposed lines are not memories, hearing is scoped',async t=>{
 const {e,p,input}=setup(t);const base={...input,scene:'Street',conversationId:'conversation1',topic:'最近的事情',observers:['c01','c02','c03','player']};
 const a=await e.utterance({...base,requestId:'utter1',actorId:'c01',recipientId:'c02',turn:0});
 assert.equal(JSON.parse(a.checkpoint).messages.length,0);
 await emit(e,input,{id:'line1',seq:1,kind:'utterance',location:'Street',conversationId:base.conversationId,speakerId:'c01',recipientId:'c02',text:a.text,proposalId:a.proposalId,observers:base.observers});
 const b=await e.utterance({...base,requestId:'utter2',actorId:'c02',recipientId:'c01',turn:1,observers:['c01','c02']});
 const end=await emit(e,input,{id:'line2',seq:2,kind:'utterance',location:'Street',conversationId:base.conversationId,speakerId:'c02',recipientId:'c01',text:b.text,proposalId:b.proposalId,observers:['c01','c02']});
 const memories=JSON.parse(end.checkpoint).memories;
 assert.ok(memories.some(m=>m.actorId==='c01'&&m.kind==='hearsay'&&m.speakerId==='c02'));
 assert.ok(memories.some(m=>m.actorId==='c02'&&m.kind==='hearsay'&&m.speakerId==='c01'));
 assert.ok(memories.some(m=>m.actorId==='c03'&&m.kind==='hearsay'&&m.speakerId==='c01'));
 assert.ok(!memories.some(m=>m.actorId==='c03'&&m.kind==='hearsay'&&m.speakerId==='c02'));
 assert.ok(!memories.some(m=>m.actorId==='c04'&&m.origin==='dialogue'));
 const speakers=p.calls.filter(c=>c.messages[0].content.includes('你已经与对方走到交谈距离')).map(c=>JSON.parse(c.messages[1].content).self.id);
 assert.deepEqual(speakers,['c01','c02']);
});
test('remembered exchange influences a later target decision rather than only a debug display',async t=>{
 const {e,input}=setup(t);const options={...input,actorId:'c01',scene:'Street',candidates:[{id:'c02'},{id:'c03'}]};
 assert.equal((await e.intent({...options,requestId:'before1'})).targetId,'c02');
 const spoken=await e.utterance({...input,actorId:'c03',recipientId:'c01',scene:'Street',conversationId:'past_chat',requestId:'past1',topic:'想再一起聊聊'});
 await emit(e,input,{id:'pastLine',seq:1,kind:'utterance',location:'Street',conversationId:'past_chat',speakerId:'c03',recipientId:'c01',text:spoken.text,proposalId:spoken.proposalId,observers:['c03','c01']});
 assert.equal((await e.intent({...options,requestId:'after1'})).targetId,'c03');
});
test('loading an earlier checkpoint fences late replies and removes future events and memory',async t=>{
 const {e,p,input,opened}=setup(t);const day4={...input,day:4};
 await emit(e,day4,{id:'worldA_day4',seq:1,kind:'day4'});
 let release;p.delay=new Promise(r=>release=r);
 const pending=e.utterance({...day4,actorId:'c01',recipientId:'c02',scene:'Street',conversationId:'late',requestId:'late_reply'});
 await new Promise(r=>setImmediate(r));
 e.open({...input,runId:'restored',checkpoint:opened.checkpoint});release();
 await assert.rejects(pending);
 const state=JSON.parse(e.response(e.assert({...input,runId:'restored'})).checkpoint);
 assert.ok(!state.events.some(x=>x.gameKind==='day4'));assert.ok(!state.memories.some(x=>x.origin==='public_day4'));
 assert.equal(state.messages.length,0);
 const future=JSON.parse(opened.checkpoint);future.memories[0].createdHour=999;
 assert.throws(()=>e.open({...input,runId:'invalid_future',checkpoint:JSON.stringify(future)}),/未来/);
});
test('world event validation, sequence and duplicate content cannot be changed by model output',async t=>{
 const {e,input}=setup(t);
 await assert.rejects(emit(e,input,{id:'worldA_day4',seq:1,kind:'day4'}));
 await assert.rejects(emit(e,input,{id:'fake',seq:1,kind:'quest_completed',text:'模型说已完成'}));
 await emit(e,input,{id:'clock1',seq:1,kind:'clock'});
 await assert.rejects(emit(e,input,{id:'clock1',seq:1,kind:'clock',text:'changed'}),/不同/);
 await assert.rejects(emit(e,input,{id:'clock3',seq:3,kind:'clock'}),/顺序/);
});
test('natural forgetting keeps availability and clarity separate and has no six-day deletion',()=>{
 const m={id:'m',epoch:1,actorId:'c01',kind:'experience',createdHour:0,importance:1,emotion:0,content:'具体细节：蓝色船绳在17号箱。',gist:'曾经准备过航行物资。',keywords:['航行']};
 const values=strength(m,170);assert.notEqual(values.availability,values.clarity);
 assert.notEqual(memoryState(m,{epoch:1,hour:170}).code,'archived');
 const r=retrievalCandidates([m],{epoch:1,hour:170},'航行物资','c01');
 assert.equal(r.candidates[0].detailLevel,'gist');assert.ok(!JSON.stringify(r.candidates).includes('17号箱'));
 assert.equal(memoryState(m,{epoch:1,hour:-1}).code,'future');
});
