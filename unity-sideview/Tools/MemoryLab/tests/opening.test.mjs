import test from 'node:test';
import assert from 'node:assert/strict';
import { Store } from '../lib/store.mjs';
import { GameEngine } from '../lib/game-engine.mjs';
import { FixtureProvider } from './fixtures.mjs';
function setup(t){const store=new Store(),provider=new FixtureProvider(),engine=new GameEngine(store,provider);t.after(()=>store.close());const input={worldId:'opening_world',runId:'run_a',day:1,period:0};engine.open(input);return {store,provider,engine,input,exp:engine.assert(input)};}
const opening=input=>({...input,requestId:'opening_request',actorId:'c01',recipientId:'player',scene:'Street',conversationId:'player_meeting',opening:true});
function message(store,exp,id,text,actor='c01'){const m={id,experimentId:exp.id,epoch:exp.epoch,hour:exp.hour,role:'user',speakerId:'player',recipientId:actor,participants:['player',actor],content:text,status:'complete',origin:'player',conversationId:'earlier'};store.put('message',m);return m;}
function memory(store,exp,id,source,extra={}){const m={id,experimentId:exp.id,epoch:exp.epoch,actorId:'c01',speakerId:'player',kind:'commitment',content:source.content,gist:'玩家答应带花。',subject:'花',sourceLabel:'玩家明确答应',evidence:[{messageId:source.id,quote:source.content}],dependencies:[],createdHour:exp.hour,importance:2,emotion:0,keywords:['玩家','约定','花'],recallCount:0,detailRecallCount:0,commitmentStatus:'pending',origin:'dialogue',...extra};store.put('memory',m);return m;}
test('empty-memory opening is one model proposal, with no invented player message or memory before it is spoken',async t=>{
 const {store,engine,input,exp}=setup(t);store.clearRecords(exp.id);
 const line=await engine.utterance(opening(input));assert.equal(line.text,'你好，很高兴见到你。');
 assert.equal(store.records(exp.id,'message').length,0);assert.equal(store.records(exp.id,'memory').length,0);
 await engine.sync({...input,eventData:{id:'spoken_opening',seq:1,day:1,period:0,personaRevision:2,kind:'utterance',speakerId:'c01',recipientId:'player',conversationId:'player_meeting',location:'Street',text:line.text,proposalId:line.proposalId,observers:['player','c01']}});
 assert.equal(store.records(exp.id,'message').length,1);assert.equal(store.records(exp.id,'message')[0].speakerId,'c01');
 await assert.rejects(engine.utterance({...opening(input),requestId:'duplicate'}),/已经开始/);
});
test('opening gets a confirmed player commitment with evidence, then sees its actual game-confirmed completion',async t=>{
 const {store,engine,provider,input,exp}=setup(t);
 const source=message(store,exp,'promise_source','我答应把一束花带给你。');const promise=memory(store,exp,'promise',source);
 const line=await engine.utterance(opening(input));assert.match(line.text,/进展/);
 let p=JSON.parse(provider.calls.find(c=>c.messages[0].content.includes('你已经与对方走到交谈距离')).messages.at(-1).content);
 assert.equal(p.opening.commitments[0].commitmentStatus,'pending');assert.equal(p.opening.commitments[0].quotes[0].quote,source.content);
 await engine.sync({...input,eventData:{id:'game_delivery',seq:1,day:1,period:0,kind:'observation',location:'Street',title:'花已交付',text:'游戏确认玩家交付了约定的花束。',gist:'花束已经交付。',observers:['c01','player'],fulfilledId:promise.id}});
 const context=engine.openingContext(engine.assert(input),'c01');assert.equal(context.commitments.length,1);assert.equal(context.commitments[0].commitmentStatus,'fulfilled');assert.equal(context.commitments[0].quotes[0].speaker,'系统');
});
test('other characters commitments and faded details are not smuggled into the opening',t=>{
 const {store,engine,input,exp}=setup(t);const privateSource=message(store,exp,'other_promise','我答应给晴子送私人物品。','c02');
 memory(store,exp,'other_memory',privateSource,{actorId:'c02'});
 const heard={...privateSource,id:'overheard',speakerId:'c02',recipientId:'player',participants:['c01','c02','player'],content:'玩家答应帮我。'};store.put('message',heard);memory(store,exp,'overheard_memory',heard);
 const source=message(store,exp,'old_promise','我答应带来密封盒，编号7391。');memory(store,exp,'faded',source,{createdHour:exp.hour-180,lastRecalledAt:exp.hour,importance:1,gist:'我记得玩家答应带点东西。'});
 const context=engine.openingContext(exp,'c01');assert.deepEqual(context.commitments.map(m=>m.id),['faded']);
 assert.equal(context.commitments[0].detailLevel,'gist');assert.deepEqual(context.commitments[0].quotes,[]);
 // The actual recent exchange is deliberately a separate short-term channel;
 // make the evidence old too before checking that old detail stays forgotten.
 store.put('message',{...source,hour:exp.hour-180});
 assert.ok(!JSON.stringify(engine.openingContext(exp,'c01')).includes('7391'));
});
test('recent actual exchange is supplied to avoid repeated first-meeting greetings, while an unseen NPC has no such context',t=>{
 const {store,engine,exp}=setup(t);message(store,exp,'earlier_chat','刚才我们聊过花店。');
 assert.equal(engine.openingContext(exp,'c01').hasPreviousDirectConversation,true);
 assert.equal(engine.openingContext(exp,'c01').recentExchange[0].text,'刚才我们聊过花店。');
 assert.equal(engine.openingContext(exp,'c07').hasPreviousDirectConversation,false);assert.deepEqual(engine.openingContext(exp,'c07').recentExchange,[]);
});
test('cancelling an in-flight opening prevents a late model from creating dialogue or memory',async t=>{
 const {store,engine,provider,input,exp}=setup(t);store.clearRecords(exp.id);let release;provider.delay=new Promise(r=>release=r);provider.ignoreAbort=true;
 const pending=engine.utterance(opening(input));await new Promise(r=>setImmediate(r));engine.cancel({...input,conversationId:'player_meeting'});release();
 await assert.rejects(pending);assert.equal(store.records(exp.id,'message').length,0);assert.equal(store.records(exp.id,'memory').length,0);assert.equal(engine.requests.size,0);
});
test('an old event log alone does not bypass forgotten contact when no relevant memory remains',t=>{
 const {store,engine,exp}=setup(t);store.clearRecords(exp.id);
 const old=message(store,exp,'forgotten_chat','我们以前见过。');store.put('message',{...old,hour:exp.hour-1000});
 const context=engine.openingContext(exp,'c01');assert.equal(context.hasPreviousDirectConversation,false);assert.deepEqual(context.recentExchange,[]);
});
