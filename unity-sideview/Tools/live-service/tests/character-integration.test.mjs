import test from 'node:test';
import assert from 'node:assert/strict';
import {once} from 'node:events';
import {createServer} from '../../MemoryLab/server.mjs';
import {CharacterBridge} from '../server/character-bridge.mjs';
import {Providers} from '../server/providers.mjs';
import {NPCS} from '../server/shared/worker/island-roster.js';
import {AudienceDirector} from '../server/shared/ui/audience-director.js';
async function setup(t){
 const app=createServer({dbPath:':memory:',providerFactory:()=>({publicConfig:()=>({configured:false}),config:{}}),backgroundEnabled:false});
 app.server.listen(0,'127.0.0.1');await once(app.server,'listening');t.after(async()=>{app.server.close();await once(app.server,'close');app.store.close();});
 const base='http://127.0.0.1:'+app.server.address().port;
 const post=async(route,data)=>{const r=await fetch(base+'/api/game/'+route,{method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify(data)});const body=await r.json();assert(r.ok,JSON.stringify(body));return body;};
 const scope={worldId:'integrated',runId:'run1',day:1,period:0,syncMode:'reference-v1',weather:'晴'};
 await post('open',scope);const {binding}=await post('live/open',scope);
 const bridge=new CharacterBridge({...binding,memoryUrl:base});return {app,base,post,scope,bridge};
}
test('real host provider adapter consumes three parts and commits Info only after actual presentation',async t=>{
 const {app,bridge}=await setup(t);const player={id:'p1',type:'player.message',speakerRole:'player',speakerId:'player',text:'今天想和你聊聊。'};
 bridge.record(player);await bridge.flush();let generated;
 const provider=new Providers({DEEPSEEK_API_KEY:'fixture-only'},{fetch:async(_url,options)=>{
  const data=JSON.parse(options.body),input=JSON.parse(data.messages[1].content);assert(input.character_context);assert(data.messages[0].content.includes('月亮岛'));
  const c=input.character_context;generated={dialogue:'好啊，你说。',action:{actorId:'z',delivery:'自然温和地回应，语速平稳。',expression:'neutral',action:'speak',mood:'warm',gesture:'none',transition:'smooth',handledIds:['inbox_1'],wakeAfterMs:2000,continuation:'await_event',agendaUpdates:[],tool:null},info:{emotion:'relaxed',changes:{trust:{delta:1,reason:'愿意听我说话',source_event_ids:[c.interaction.source_event_id]}}}};
  return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(generated)}}]});
 }});
 const before=app.engine.actor('integrated','z').info.trust;
 const reply=await provider.generate('actor-integration',{requestId:crypto.randomUUID(),kind:'host',eventId:'p1',context:{publicEvents:[player],host:{inbox:[{id:'p1',name:'你',mine:true,text:player.text}]}}},{character:bridge});
 assert.equal(reply.speech,generated.dialogue);assert(!reply.info);assert.equal(app.engine.actor('integrated','z').info.trust,before);
 // Identical text from another publication must never consume this decision.
 bridge.record({id:'other',type:'speech.completed',speakerId:'Z',speakerRole:'host',text:reply.speech});await bridge.flush();assert.equal(app.engine.actor('integrated','z').info.trust,before);
 bridge.bindPublication('s1',reply.characterProposalId,'z',reply.speech);
 bridge.record({id:'s1',type:'speech.progress',speakerId:'Z',speakerRole:'host',text:reply.speech});await bridge.flush();assert.equal(app.engine.actor('integrated','z').info.trust,before);
 bridge.record({id:'s1',type:'speech.completed',speakerId:'Z',speakerRole:'host',text:reply.speech});await bridge.flush();assert.equal(app.engine.actor('integrated','z').info.trust,before+1);
 bridge.record({id:'s1',type:'speech.completed',speakerId:'Z',speakerRole:'host',text:reply.speech});await bridge.flush();assert.equal(app.engine.actor('integrated','z').info.trust,before+1);
});
test('every island viewer takes a scheduler turn, with no optional room membership',async()=>{
 let now=0;const scheduled=[];const d=new AudienceDirector({now:()=>now,request:async p=>{scheduled.push(p.context.audience.eligibleNpcIds[0]);return {status:'skip',items:[]};},publish:()=>{},config:{singleActorRequests:true,intervalMs:1,generationDeadlineMs:10000,maxConcurrent:1,maxPoolSize:1}});
 d.record({id:'hello',type:'player.message',text:'大家好。',speakerRole:'player',speakerId:'player'});
 for(let i=0;i<7;i++){now+=10;d.tick();await new Promise(r=>setImmediate(r));}
 assert.deepEqual(new Set(scheduled),new Set(NPCS.map(n=>n.npc_id)));assert.equal(scheduled.length,7);
});
test('audience proposals retain their owner through the draft pool and commit once after publication',async t=>{
 const {app,bridge}=await setup(t);
 const message={id:'audience_input',type:'player.message',speakerRole:'player',speakerId:'player',text:'留洋，晚上好。'};
 bridge.record(message);await bridge.flush();
 const actor='linxu',before=app.engine.actor('integrated',actor).info.trust;
 const providers=new Providers({DEEPSEEK_API_KEY:'fixture-only'},{fetch:async(_url,options)=>{
  const input=JSON.parse(JSON.parse(options.body).messages[1].content),c=input.character_context;
  return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify({dialogue:'晚上好。',action:{type:'send_danmaku',intent:'atmosphere',evidence_ids:[message.id],actorId:actor,delivery:'平静自然地打招呼。',expression:'neutral'},info:{emotion:'relaxed',support_willingness:c.character_state.info.support_willingness,changes:{trust:{delta:1,reason:'收到实际招呼',source_event_ids:[c.interaction.source_event_id]}}}})}}]});
 }});
 const payload={requestId:crypto.randomUUID(),kind:'audience',eventId:message.id,context:{publicEvents:[message],audience:{eligibleNpcIds:[actor],allowAdvance:false}}};
 const reply=await providers.generate('audience-integration',payload,{character:bridge});
 assert(reply.items[0].characterProposalId);assert.equal(app.engine.actor('integrated',actor).info.trust,before);
 const director=new AudienceDirector({request:async()=>reply,publish:()=>{},canCommit:()=>false,now:()=>0,config:{singleActorRequests:true}});
 director.record(message);director._generate([actor],0);await new Promise(resolve=>setImmediate(resolve));
 assert.equal(director.pool[0].item.characterProposalId,reply.items[0].characterProposalId);
 const item=director.pool[0].item;bridge.bindPublication('audience_display',item.characterProposalId,actor,item.text);
 bridge.record({id:'audience_display',type:'npc.message',speakerRole:'island',speakerId:actor,text:item.text});await bridge.flush();
 assert.equal(app.engine.actor('integrated',actor).info.trust,before+1);
 bridge.record({id:'audience_display',type:'npc.message',speakerRole:'island',speakerId:actor,text:item.text});await bridge.flush();
 assert.equal(app.engine.actor('integrated',actor).info.trust,before+1);
});
test('reference saves retain old snapshots, avoid transferring private history, and reopen exact history',async t=>{
 const {app,post,scope,bridge}=await setup(t);
 for(let n=0;n<20;n++)bridge.record({id:'p'+n,type:'player.message',speakerRole:'player',speakerId:'player',text:'这一轮真实发出的弹幕'+n});
 await bridge.flush();const snapshot=await post('checkpoint',scope),cp=JSON.parse(snapshot.checkpoint);
 assert.equal(cp.version,3);assert(cp.ref);assert(!cp.records.memory);assert(!snapshot.checkpoint.includes('这一轮真实发出的弹幕'));assert(snapshot.checkpoint.length<20000);
 const thawed=app.store.thawCheckpoint(cp.ref);assert(thawed.records.event.some(e=>e.text==='这一轮真实发出的弹幕19'));
 app.engine.observe(scope.worldId,'只有留洋知道的私事',['linxu'],{wake:false});
 const projection=await bridge.post('projection');
 assert(JSON.stringify(projection).includes('这一轮真实发出的弹幕'));assert(!JSON.stringify(projection).includes('只有留洋知道的私事'));
 const reopened=await post('open',{...scope,runId:'run2',checkpoint:snapshot.checkpoint});assert.equal(reopened.runId,'run2');
 assert(app.engine.memory.history(app.engine.world('integrated'),'linxu').some(m=>JSON.stringify(m).includes('弹幕19')));
 await assert.rejects(bridge.post('observe',{event:{id:'stale',actorId:'linxu',text:'不应出现'}}));
});

test('displayed virtual audience does not block subsequent character preparation',async t=>{
 const {app,bridge}=await setup(t);
 bridge.record({id:'virtual_display',type:'npc.message',speakerRole:'audience',speakerId:'virtual_comment1',text:'晚安，明天见'});
 await bridge.flush();assert.equal(bridge.pending.length,0);
 const prepared=await bridge.prepare({kind:'host',context:{publicEvents:[]}});assert.equal(prepared.actorId,'z');
 const events=app.engine.records(app.engine.world('integrated'),'event');
 const event=events.find(e=>e.text==='直播间虚拟观众弹幕：晚安，明天见');
 assert.equal(event.speaker,'engine');assert.equal(event.kind,'observation');
 assert(!Object.hasOwn(app.engine.world('integrated').actors,'virtual_comment1'));
});
