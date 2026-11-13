import test from 'node:test';
import assert from 'node:assert/strict';
import {addressMessage} from '../server/shared/addressing.js';
import {ConversationEngine} from '../server/shared/ui/conversation-engine.js';
import {normalizeAgentInput,parseAgentReply,buildAgentRequest} from '../server/shared/worker/agent-director.js';
function engine(t){const e=new ConversationEngine();e._state.paused=false;e.enabled=true;t.after(()=>e.destroy());return e;}
test('vocatives route Z and NPC messages; mentioning Z as a topic does not redirect',()=>{
 assert.deepEqual(addressMessage('你唱唱歌Z'),['z']);assert.deepEqual(addressMessage('@Z 唱个歌'),['z']);
 assert.deepEqual(addressMessage('正义，你觉得Z最近怎么样？'),['zhengyi']);
 assert.deepEqual(addressMessage('那你说呢',{previous:['zhengyi']}),['zhengyi']);
 assert.throws(()=>addressMessage('hi',{explicit:['unknown']}),/invalid_recipient/);
});
test('a reply to another NPC is public but is not placed in the host inbox',t=>{
 const e=engine(t);e.sendMessage('正义，你今天巡逻了吗？');const m=e.inbox[0];assert.deepEqual(m.recipientIds,['zhengyi']);assert.equal(e._hostContext(e.inbox).host.inbox.length,0);
 e.sendMessage('Z，你唱唱歌');const z=e.inbox.at(-1);assert.equal(e._hostContext(e.inbox).host.inbox[0].id,z.id);
 e.audience.publish({npc_id:'zhengyi',text:'巡逻完了，刚坐下。',intent:'reaction',recipientIds:['player'],reply_to:m.id});
 assert(!e.inbox.some(x=>x.id===m.id));assert(e.inbox.some(x=>x.id===z.id));
 assert.equal(e.session.events.at(-1).replyTo[0],m.id);assert.deepEqual(e.session.events.at(-1).recipientIds,['player']);
});
test('a directly addressed Z message survives other NPC responses and wait decisions',t=>{
 const e=engine(t);e.sendMessage('你唱唱歌Z');const id=e.inbox[0].id;e._acknowledge([id],'zhengyi');assert.equal(e.inbox.length,1);
 const value=normalizeAgentInput({requestId:crypto.randomUUID(),kind:'host',eventId:'test',context:e._hostContext(e.inbox)});
 const wait={action:'wait',speech:'',mood:'warm',gesture:'none',handledIds:[id],wakeAfterMs:2000,memory:'',agendaUpdates:[]};
 assert.throws(()=>parseAgentReply(wait,value),/direct_reply_required/);
 e._acknowledge([id],'z');assert.equal(e.inbox.length,0);
});
test('each addressed actor acknowledges only their own response',t=>{
 const e=engine(t);e.sendMessage('你们怎么看',{recipientIds:['z','zhengyi']});const m=e.inbox[0];e._acknowledge([m.id],'z');assert.equal(e.inbox.length,1);assert.deepEqual(e.inbox[0].pendingRecipientIds,['zhengyi']);assert.equal(e._hostContext(e.inbox).host.inbox.length,0);e._acknowledge([m.id],'zhengyi');assert.equal(e.inbox.length,0);
});
test('host retries with validation feedback and can recover after three invalid replies',async t=>{
 const e=engine(t);e._started=true;e.setSpeechDriven(true);e.audience.dialogue.configure({version:'qa'});e.sendMessage('你咋不理我了');const id=e.inbox[0].id;let calls=0;
 e.client={cancel(){},generate:async p=>{
  if(p.kind==='review')return {decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'qa'};
  calls++;if(calls>1)assert.equal(p.context.host.validationFeedback,'invalid_expression');
  if(calls<4)throw Object.assign(Error('invalid'),{code:'agent_invalid_reply',validationReason:'invalid_expression'});
  return {actorId:'z',expression:'neutral',delivery:'自然地回应。',action:'speak',speech:'我在，刚才没接上。你接着说。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[id],wakeAfterMs:2000,continuation:'await_event',memory:'',agendaUpdates:[]};
 }};
 for(let i=0;i<4;i++){e.nextWake=e._clock;await e._think();}
 assert.equal(calls,4);assert.equal(e.failureStreak,0);assert(e._active?.text.includes('我在'));assert.equal(e.inbox.length,1);
 e._finishSpeech();e._pumpSpeech();assert.equal(e.inbox.length,0);assert(e.session.events.some(ev=>ev.type==='speech.completed'&&ev.replyTo.includes(id)));
});

test('disabled audio removes playable song examples and returns an actionable validation reason',t=>{
 const e=engine(t);e.sendMessage('Z，你唱唱歌');const c=e._hostContext(e.inbox);c.mediaRuntime={audioEnabled:false,ttsAvailable:false};
 const v=normalizeAgentInput({requestId:crypto.randomUUID(),kind:'host',eventId:'qa',context:c});
 const request=buildAgentRequest(v);assert.deepEqual(request.input.songLibrary,[]);assert(request.system.includes('本轮没有可执行的唱歌工具'));
 const reply={actorId:'z',expression:'neutral',delivery:'平静地回答。',action:'tool',speech:'我试试。',mood:'warm',gesture:'none',handledIds:[e.inbox[0].id],wakeAfterMs:2000,memory:'',agendaUpdates:[],tool:{name:'start_singing',args:{songId:'any'}}};
 assert.throws(()=>parseAgentReply(reply,v),/invalid_tool_audio_disabled/);
});
