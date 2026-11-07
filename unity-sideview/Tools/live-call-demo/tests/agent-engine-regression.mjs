import {reviewReply} from './review-fixture.mjs';
import assert from 'node:assert/strict';
import {AgentEngine} from '../ui/agent-engine.js';

if (!globalThis.CustomEvent) globalThis.CustomEvent = class extends Event {constructor(type,{detail}) {super(type);this.detail=detail;}};
const original={performance:globalThis.performance,setInterval,clearInterval};
let now=1000;
Object.defineProperty(globalThis,'performance',{configurable:true,value:{now:()=>now}});
globalThis.setInterval=()=>1;globalThis.clearInterval=()=>{};
const flush=async()=>{for(let i=0;i<12;i++)await Promise.resolve();};
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
function create(handler,enabled=true,options={}) {
  const e=new AgentEngine({storage:null}),calls=[];
  e._lastTick=now;e._nextChatAt=Infinity;e._nextViewerAt=Infinity;e.setSpeechDriven(true);
  const client={cancel(){},generate(payload,opts){if(payload.kind==='review')return Promise.resolve(reviewReply(payload));calls.push(payload);if(payload.kind==='audience')return options.audienceHandler?.(payload,opts)??Promise.resolve({status:'skip',items:[]});return handler(payload,opts);}};
  const config={enabled,policy:{skills:{'entertainment-tarot':{selectionReminderAfterMs:15000,selectionWaitMs:45000}},...(options.audiencePolicy?{audience:options.audiencePolicy}:{})}};
  e.configureAgent(client,config);
  return {e,calls,client,config};
}
function step(e,ms){for(let elapsed=0;elapsed<ms;elapsed+=160){now+=Math.min(160,ms-elapsed);e._tick();}}
async function runTicks(e,ms){for(let elapsed=0;elapsed<ms;elapsed+=160){step(e,Math.min(160,ms-elapsed));await flush();}}
const npcMessages=e=>e.getState().messages.filter(message=>message.source==='agent-audience');
const fastAudience={intervalMs:640,npcPublishCooldownMs:0};
function audienceReply(payload,serial,includeAdvance=false){return {status:'ready',items:[...(includeAdvance?[{npc_id:'wind',text:`先听我的问题${serial}`,intent:'advance',evidence_refs:[payload.eventId]}]:[]),{npc_id:'moon',text:`我在这里陪着大家${serial}`,intent:'reaction',evidence_refs:[payload.eventId]}]};}
function finish(e){const id=e.getState().currentSpeechId;if(id)e.completeSpeech(id);e._pumpSpeech();}
async function until(e,predicate,max=80){for(let i=0;i<max&&!predicate();i++){finish(e);step(e,160);await flush();}assert.ok(predicate(),'Expected state transition');}
function decision(payload,extra={}){return {selectedEventId:payload.eventId,action:'respond',skillId:null,args:{},speech:'我听到了，我们慢慢聊。',mood:'warm',...extra};}
function rejectedReceipt(e,receive){const before=structuredClone(e.publicFacts);assert.equal(receive(),false);assert.deepEqual(e.publicFacts,before,'Rejected display receipts must not enter public history');}
let count=0;
async function test(name,fn){await fn();count++;console.log(`PASS ${name}`);}
try{
 await test('a player skill intent starts only after the complete acknowledgement and remains viewer-owned',async()=>{
  const {e,calls}=create(p=>Promise.resolve(decision(p,{action:'request_skill',skillId:'entertainment-tarot',args:{origin:'host',question:'开始新计划可以关注什么？',spread:'single'}})));
  assert.equal(e.sendMessage('帮我用塔罗看看新计划'),true);step(e,800);await flush();
  assert.equal(calls.filter(c=>c.kind==='decision').length,1);
  assert.equal(e.getState().play.active,null);assert.equal(e.getState().replyStatus,'replying');
  const id=e.getState().currentSpeechId;step(e,20000);assert.equal(e.getState().currentSpeechId,id);assert.equal(e.getState().play.active,null);
  finish(e);assert.equal(e.getState().play.active,'tarot');assert.equal(e.getState().play.tarot.origin,'viewer');e.destroy();
 });
 await test('ordinary model response does not start a talent and includes only permitted public context',async()=>{
  const {e,calls}=create(p=>Promise.resolve(decision(p)));
  e.sendMessage('今天想和你聊聊天。');step(e,800);await flush();
  const request=calls.find(p=>p.kind==='decision');assert.ok(request);assert.equal(request.context.activeSkill,null);
  assert.equal(request.context.cards,undefined);assert.equal(request.context.candidates,undefined);
  finish(e);assert.equal(e.getState().play.active,null);assert.equal(e.getState().agent.source,'deepseek');e.destroy();
 });
 await test('a completed player reply becomes public evidence at the next audience cadence',async()=>{
  const {e,calls}=create(p=>Promise.resolve(decision(p)),true,{audiencePolicy:fastAudience});
  e.sendMessage('今天想和你聊聊天。');step(e,800);await flush();
  assert.equal(e._active?.scope,'reply');assert.equal(e.audience.canPublish(),true);assert.equal(e.audience.canAdvance(),false);
  const speechId=e.getState().currentSpeechId,speech=e.getState().currentSpeech;
  assert.ok(speechId);assert.equal(e.completeSpeech(speechId),true);
  assert.equal(e._hasPendingPlayerChat(),false);assert.notEqual(e._active?.scope,'reply');
  assert.equal(e.audience.canPublish(),true);assert.equal(e.audience.canAdvance(),true);
  const event=e.publicFacts.at(-1);
  assert.equal(event.type,'speech.completed');assert.equal(event.text,speech);
  await runTicks(e,800);
  const reaction=calls.find(p=>p.kind==='audience'&&p.eventId===event.id);
  assert.ok(reaction,'The scheduled reaction must reach audience generation');
  const published=reaction.context.publicEvents.find(item=>item.id===event.id);assert.deepEqual({id:published.id,type:published.type,text:published.text},event);assert.equal(published.speakerRole,'host');e.destroy();
 });
 await test('offline singing and voice requests report unavailable without claiming performance',async()=>{
  const {e}=create(()=>Promise.reject(new Error('offline')),false);
  for(const [message,skill] of [['唱一首歌','singing'],['帮我配音一段台词','voice-performance']]){
   const result=e._localDecision(message,'local-test');assert.equal(result.action,'unavailable');assert.equal(result.skillId,skill);assert.match(result.speech,/没有接好|不能开始/);
  }
  assert.equal(e._executeIntent({skillId:'singing',args:{}}),false);assert.equal(e.getState().play.active,null);e.destroy();
 });
 await test('host sees only opaque candidates, while interpretations require the display acknowledgement',async()=>{
  const {e,calls}=create(p=>Promise.resolve(p.context.phase==='awaiting_selection'?{action:'choose_host',candidateId:p.context.candidates[1].id,speech:'我选中间。'}:{action:'interpret',speech:'这张牌可以提供一个思考角度。'}));
  e.requestTarot({origin:'host'});await until(e,()=>e.getState().play.tarot.phase==='candidates_ready');
  const t=e.getState().play.tarot;
  rejectedReceipt(e,()=>e.candidatesDisplayed('stale-task',t.round));
  rejectedReceipt(e,()=>e.candidatesDisplayed(t.id,t.round+1));
  rejectedReceipt(e,()=>e.tarotDisplayed(t.id));
  assert.equal(e.candidatesDisplayed(t.id,t.round),true);
  const candidatesReceipt=e.publicFacts.at(-1);
  assert.equal(candidatesReceipt.type,'tarot.candidates.displayed');assert.ok(candidatesReceipt.id);
  rejectedReceipt(e,()=>e.candidatesDisplayed(t.id,t.round));
  await flush();
  const selection=calls.find(p=>p.kind==='talent');assert.equal(selection.context.cards,undefined);assert.equal(selection.context.candidates.length,3);
  assert.deepEqual(selection.context.publicEvents.find(event=>event.id===candidatesReceipt.id),candidatesReceipt);
  assert.deepEqual(Object.keys(selection.context.candidates[0]).sort(),['id','label']);
  assert.equal(e.chooseTarot(t.id,t.round,t.candidates[0].id),false);
  await until(e,()=>e.getState().play.tarot.phase==='selection_locked');step(e,800);await flush();
  assert.equal(calls.filter(p=>p.kind==='talent').length,1,'No interpretation before visible receipt');
  assert.equal(e._safeContext().cards,undefined);
  rejectedReceipt(e,()=>e.tarotDisplayed('stale-task'));
  e.setPanel('tarot',false);rejectedReceipt(e,()=>e.tarotDisplayed(t.id));e.setPanel('tarot',true);
  assert.equal(e.tarotDisplayed(t.id),true);
  const cardReceipt=e.publicFacts.at(-1),interpretation=calls.filter(p=>p.kind==='talent')[1];
  assert.equal(cardReceipt.type,'tarot.displayed');assert.ok(cardReceipt.id);
  assert.ok(interpretation,'Display acknowledgement must synchronously start interpretation');
  assert.deepEqual(interpretation.context.publicEvents.find(event=>event.id===cardReceipt.id),cardReceipt,'The interpretation request already contains the public card display ID');
  assert.equal(interpretation.context.cards.length,1);assert.equal(interpretation.context.phase,'interpreting');
  assert.ok(cardReceipt.text.includes(interpretation.context.cards[0].title),'Display evidence must identify the actual public card');
  rejectedReceipt(e,()=>e.tarotDisplayed(t.id));
  assert.equal(calls.filter(p=>p.kind==='talent').length,2,'Duplicate receipt must not repeat interpretation');
  await flush();e.destroy();
 });
 await test('drawing context excludes prepared-but-undisplayed details and preserves the last shown stage',async()=>{
  const {e}=create(()=>Promise.resolve({action:'follow_up',speech:'画好了。'}),false);
  e.requestDrawing({subject:'cat',accessory:'bow'});await until(e,()=>e.getState().play.drawing.stage===0);
  const d=e.getState().play.drawing;assert.equal(e._safeContext().drawing,undefined);
  e.drawingDisplayed(d.id,0,0);let context=e._safeContext().drawing;
  assert.equal(context.stage,0);assert.equal(context.title,'小猫轮廓');assert.equal(context.accessory,undefined);
  step(e,14000);context=e._safeContext().drawing;assert.equal(context.stage,0);assert.equal(context.accessory,undefined);
  e.drawingDisplayed(d.id,0,2);context=e._safeContext().drawing;assert.equal(context.stage,2);assert.equal(context.accessory,'bow');e.destroy();
 });
 await test('drawing completion evidence precedes its synchronous talent request and rejects invalid receipts',async()=>{
  const {e,calls}=create(()=>Promise.resolve({action:'follow_up',speech:'小猫画好了，你想给它起什么名字？'}));
  e.requestDrawing({subject:'cat',accessory:'bow'});await until(e,()=>e.getState().play.drawing.stage===0);
  const d=e.getState().play.drawing;
  rejectedReceipt(e,()=>e.drawingDisplayed('stale-task',d.revision,0));
  rejectedReceipt(e,()=>e.drawingDisplayed(d.id,d.revision+1,0));
  rejectedReceipt(e,()=>e.drawingDisplayed(d.id,d.revision,2));
  assert.equal(e.drawingDisplayed(d.id,d.revision,0),true);
  rejectedReceipt(e,()=>e.drawingDisplayed(d.id,d.revision,0));
  step(e,14000);assert.equal(e.getState().play.drawing.stage,2);
  assert.equal(calls.filter(p=>p.kind==='talent').length,0,'Prepared drawing must not trigger completion commentary');
  rejectedReceipt(e,()=>e.drawingDisplayed(d.id,d.revision,1));
  e.setPanel('drawing',false);rejectedReceipt(e,()=>e.drawingDisplayed(d.id,d.revision,2));e.setPanel('drawing',true);
  assert.equal(e.drawingDisplayed(d.id,d.revision,2),true);
  const receipt=e.publicFacts.at(-1),completion=calls.find(p=>p.kind==='talent');
  assert.equal(receipt.type,'drawing.displayed');assert.ok(receipt.id);assert.match(receipt.text,/戴蝴蝶结的小猫.*完成图/);
  assert.ok(completion,'Completion display acknowledgement must synchronously start talent follow-up');
  assert.deepEqual(completion.context.publicEvents.find(event=>event.id===receipt.id),receipt,'The completion request already contains the final drawing display ID');
  assert.equal(completion.context.phase,'completed');assert.equal(completion.context.drawing.stage,2);assert.equal(completion.context.drawing.accessory,'bow');
  rejectedReceipt(e,()=>e.drawingDisplayed(d.id,d.revision,2));
  assert.equal(calls.filter(p=>p.kind==='talent').length,1,'Duplicate receipt must not repeat drawing follow-up');
  await flush();e.destroy();
 });
 await test('viewer selection reminds at 15 seconds and pauses the same candidates at 45 seconds',async()=>{
  const {e}=create(()=>Promise.reject(new Error('offline')),false);
  e.requestTarot({question:'如何开始新计划？'});await until(e,()=>e.getState().play.tarot.phase==='candidates_ready');
  const t=e.getState().play.tarot;e.candidatesDisplayed(t.id,t.round);step(e,14900);assert.equal(e.getState().play.tarot.reminded,false);
  step(e,160);assert.equal(e.getState().play.tarot.reminded,true);assert.equal(e.getState().play.tarot.paused,false);
  step(e,30000);const after=e.getState().play.tarot;assert.equal(after.paused,true);assert.deepEqual(after.candidates,t.candidates);assert.equal(after.slots.length,0);
  assert.equal(e.chooseTarot(t.id,t.round,t.candidates[0].id),false);e.destroy();
 });
 await test('replay rejects a noncooperative late model decision',async()=>{
  const pending=deferred();let payload;const {e}=create(p=>{payload=p;return pending.promise;});
  e.sendMessage('帮我画一只小猫');step(e,800);await flush();assert.ok(payload);
  e.replay();pending.resolve(decision(payload,{action:'request_skill',skillId:'simple-drawing',args:{subject:'cat',accessory:'scarf'}}));await flush();
  assert.equal(e.getState().play.drawing,null);assert.notEqual(e.getState().currentSpeech,'我听到了，我们慢慢聊。');e.destroy();
 });
 await test('speech enters public history only at actual completion',async()=>{
  const {e}=create(()=>Promise.reject(new Error('offline')),false);
  e._say('公开前不能被观众看到这句。');assert.equal(e.publicFacts.length,0);step(e,20000);assert.equal(e.publicFacts.length,0);
  finish(e);assert.equal(e.publicFacts.length,1);assert.equal(e.publicFacts[0].type,'speech.completed');e.destroy();
 });
 await test('NPC advances cannot create a task owned by the human player',async()=>{
  const {e}=create(p=>Promise.resolve(decision(p,{action:'request_skill',skillId:'entertainment-tarot',args:{question:'帮我看看方向',spread:'single'}})));
  e._publishNpc({npc_id:'wind',text:'帮我看看下一步的方向？',intent:'advance',evidence_refs:[]});
  step(e,800);await flush();finish(e);assert.equal(e.getState().play.active,null);e.destroy();
 });
 await test('host idle generation allows audience reactions while holding host-directed advances',async()=>{
  const pending=deferred();let serial=0;
  const {e,calls}=create(()=>pending.promise,true,{audiencePolicy:fastAudience,audienceHandler:p=>Promise.resolve(audienceReply(p,++serial,true))});
  e._public('speech.completed','我想听听大家今天在忙什么。');
  e._clock=13000;e._maybeIdle();assert.equal(e._idlePending,true);assert.equal(e.audience.canPublish(),true);assert.equal(e.audience.canAdvance(),false);
  await runTicks(e,960);assert.ok(npcMessages(e).length>0);assert.ok(npcMessages(e).every(message=>message.text.startsWith('我在这里')));
  assert.equal(calls.filter(p=>p.kind==='decision').length,1,'NPC reactions must not queue another host request during idle generation');
  e.destroy();pending.reject(new Error('cancelled'));await flush();
 });
 await test('starting once seeds continuous introductory audience chat without new host events',async()=>{
  let serial=0;const {e,calls}=create(p=>Promise.resolve(decision(p)),true,{audiencePolicy:fastAudience,audienceHandler:p=>Promise.resolve(audienceReply(p,++serial))});
  e.start();e.start();assert.equal(e.publicFacts.filter(event=>event.type==='session.opened').length,1);
  const facts=structuredClone(e.publicFacts),speechId=e.getState().currentSpeechId;
  await runTicks(e,2240);
  assert.equal(e.getState().stage,'intro');assert.equal(e.getState().currentSpeechId,speechId);
  assert.ok(npcMessages(e).length>=3,'Audience must continue without another completed speech or display event');
  assert.deepEqual(e.publicFacts,facts,'NPC publication must not manufacture new public facts');
  assert.ok(calls.filter(p=>p.kind==='audience').every(p=>p.context.publicEvents.some(event=>event.type==='session.opened')));e.destroy();
 });
 for(const phase of ['free','tarot','drawing','lottery'])await test(`continuous audience chat publishes throughout ${phase}`,async()=>{
  let serial=0;const {e}=create(p=>Promise.resolve(decision(p)),true,{audiencePolicy:fastAudience,audienceHandler:p=>Promise.resolve(audienceReply(p,++serial))});
  e._nextIdleAt=Infinity;e._public('session.opened','直播间已开播，观众正在陪伴 Z。');
  if(phase==='free')e._setStage('free');
  if(phase==='tarot'){
   e.requestTarot({question:'新计划该怎么开始？'});await until(e,()=>e.getState().play.tarot.phase==='candidates_ready');
   const t=e.getState().play.tarot;assert.equal(e.candidatesDisplayed(t.id,t.round),true);
  }
  if(phase==='drawing'){
   e.requestDrawing({subject:'cat',accessory:'bow'});await until(e,()=>e.getState().play.drawing.stage===0);
   const d=e.getState().play.drawing;assert.equal(e.drawingDisplayed(d.id,d.revision,0),true);
  }
  if(phase==='lottery')assert.equal(e.triggerStory(),true);
  const before=npcMessages(e).length,facts=structuredClone(e.publicFacts);
  await runTicks(e,2240);assert.ok(npcMessages(e).length>=before+3,`${phase} must not gate reaction messages`);
  assert.deepEqual(e.publicFacts,facts);
  if(phase==='tarot')assert.equal(e.getState().play.tarot.phase,'awaiting_selection');
  if(phase==='drawing')assert.equal(e.getState().play.drawing.stage,0);
  if(phase==='lottery')assert.equal(e.getState().lottery.open,true);
  e.destroy();
 });
 await test('player generation and active replies retain host priority while reactions continue',async()=>{
  const pending=deferred();let serial=0,includeAdvance=false;
  const {e,calls}=create(()=>pending.promise,true,{audiencePolicy:fastAudience,audienceHandler:p=>Promise.resolve(audienceReply(p,++serial,includeAdvance))});
  e.sendMessage('我今天有点累，可以聊聊吗？');await runTicks(e,800);
  assert.equal(e._hasPendingPlayerChat(),true);assert.equal(e.audience.canAdvance(),false);includeAdvance=true;
  let before=npcMessages(e).length;await runTicks(e,960);assert.ok(npcMessages(e).length>before);
  const hostRequest=calls.find(p=>p.kind==='decision');pending.resolve(decision(hostRequest));await flush();
  assert.equal(e._active?.scope,'reply');const replyId=e.getState().currentSpeechId;
  assert.equal(e.audience.canPublish(),true);assert.equal(e.audience.canAdvance(),false);
  before=npcMessages(e).length;await runTicks(e,960);assert.ok(npcMessages(e).length>before);
  assert.equal(e.getState().currentSpeechId,replyId,'Audience messages must not interrupt the real player reply');
  assert.ok(npcMessages(e).every(message=>message.text.startsWith('我在这里')),'Blocked advances must not publish');
  assert.equal(calls.filter(p=>p.kind==='decision').length,1,'NPCs must not compete for host responses');
  assert.equal(e._chatCandidates.length,0);e.destroy();
 });
 await test('reconnecting starts audience cadence from existing opening facts',async()=>{
  let serial=0;const {e,calls,client,config}=create(p=>Promise.resolve(decision(p)),false,{audiencePolicy:fastAudience,audienceHandler:p=>Promise.resolve(audienceReply(p,++serial))});
  e.start();const facts=structuredClone(e.publicFacts);await runTicks(e,960);assert.equal(calls.length,0);
  e.configureAgent(client,{...config,enabled:true});await runTicks(e,960);
  assert.ok(npcMessages(e).length>0);assert.deepEqual(e.publicFacts,facts);e.destroy();
 });
 for(const state of ['paused','ended','stopped','destroyed'])await test(`${state} blocks late audience results and subsequent generation`,async()=>{
  const pending=deferred();let request;
  const {e,calls}=create(p=>Promise.resolve(decision(p)),true,{audiencePolicy:fastAudience,audienceHandler:p=>{request=p;return pending.promise;}});
  e.start();await runTicks(e,160);assert.ok(request);
  if(state==='paused')e.setPaused(true);else if(state==='destroyed')e.destroy();else e._state.stage=state;
  assert.equal(e.audience.canPublish(),false);
  pending.resolve(audienceReply(request,1));await flush();await runTicks(e,1600);
  assert.equal(npcMessages(e).length,0);assert.equal(calls.filter(p=>p.kind==='audience').length,1);e.destroy();
 });
 console.log(`\n${count} agent engine integration scenarios passed.`);
}finally{Object.defineProperty(globalThis,'performance',{configurable:true,value:original.performance});globalThis.setInterval=original.setInterval;globalThis.clearInterval=original.clearInterval;}
