import assert from 'node:assert/strict';
import {CallConversationEngine} from '../ui/call-engine.js';
import {CALL_STORY} from '../ui/call-contract.js';
import {handleAgent,parseAgentReply,buildAgentRequest} from '../worker/agent-director.js';
import {reviewReply} from './review-fixture.mjs';
if(!globalThis.CustomEvent)globalThis.CustomEvent=class extends Event{constructor(t,{detail}){super(t);this.detail=detail;}};
const result=(extra={})=>({action:'speak',speech:'来，坐一会儿。今天想从哪里聊起？',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:9000,continuation:'await_event',memory:'',...extra});
const make=()=>{const e=new CallConversationEngine();e.configureAgent({cancel(){},async generate(p){return p.kind==='review'?reviewReply(p):p.kind==='audience'?{status:'skip',items:[]}:result({handledIds:p.context.host.inbox.map(m=>m.id)});}},{enabled:true,policy:{audience:{handoff:true}},topic:{enabled:true}});e.setSpeechDriven(true);e.start();clearInterval(e._timer);return e;};
let e=make();e._public('player.message','今天刚做完一个项目。',{speakerRole:'player',speakerId:'player'});assert(e.joinCall());assert(!e.joinCall());assert.equal(e.handoffEnabled,false);assert(e.session.evidence().some(x=>x.text.includes('项目')));await e._think();assert(e._active);e.completeSpeech(e._active.id);assert.equal(e.callNeedsTurn,false);e.nextWake=0;await e._think();assert.equal(e._active,null,'no unsolicited speech loop');
e._receive({id:'npc1',name:'晚风',text:'我们想听歌！'},false,'wind');assert.equal(e.inbox.length,0);assert.equal(e._state.call.turns,0);assert.equal(e.callNeedsTurn,false);
e.sendMessage('今天想聊聊工作');const mid=e.inbox[0].id;await e._think();const speech=e._active;e.recordSpeechProgress(speech.id,'来');assert(e.facts.some(x=>x.type==='speech.progress'&&x.text==='来')); e.setCallInput('listening');assert.equal(e._active,null);assert.equal(e._state.currentSpeech,'');assert(!e.session.evidence().some(x=>x.id===`speech-${speech.id}`&&x.type==='speech.completed'));assert(e.inbox.some(x=>x.id===mid),'interruption does not claim an answered turn');assert.equal(e._continuations.length,0);e.callNeedsTurn=true;e.nextWake=0;await e._think();assert.equal(e._active,null,'microphone owns turn');
e.setCallInput('idle');e.sendMessage('我再补充一句');await e._think();assert(e._active);e.completeSpeech(e._active.id);assert.equal(e.inbox.length,0);
assert(e.offerCallStory());assert.equal(e._state.call.story.step,0);assert(e.chooseCallStory('rest',1));assert.equal(e._state.call.story.step,1);assert.equal(e.chooseCallStory('moon',1),false,'stale/double click');assert(e.chooseCallStory('moon',2));assert.equal(e._state.call.story.status,'completed');assert.equal(e.offerCallStory(),false);assert.equal(e._state.play.active,null,'a postcard is not a generated artwork');
assert(e.endCall());assert.equal(e.handoffEnabled,true);assert.equal(e.topicEnabled,true);assert.equal(e.chooseCallStory('moon',3),false);assert.equal(e._state.call.active,false);assert.equal(e.inbox.length,0);e.destroy();
// Aborted LLM completions cannot speak or run tools in a later call.
e=make();e.joinCall();let resolve;const original=e.client;e.client={cancel(){},generate:()=>new Promise(r=>resolve=r)};const pending=e._think();e.endCall();e.joinCall();resolve(result({action:'tool',tool:{name:'offer_call_story',args:{}}}));await pending;assert.equal(e._active,null);assert.equal(e._state.call.story,null);e.client=original;e.destroy();
// Media presentation and old onComplete tools are stopped by input ownership.
e=make();e.joinCall();let cancelled=0,played=0,ran=0;e.speechAudio={cancel(){cancelled++;},dropWarmSpeech(){}};e._say('这句话没有说完','warm','none',{onComplete:()=>ran++});e.interruptCall();e._pumpSpeech();assert.equal(ran,0);assert.equal(cancelled,1);e.talent={play(){played++;},cancel(){},finish(){},pause(){},resume(){},destroy(){}};e._state.media={phase:'ready'};e.setCallInput('listening');e._tick();assert.equal(played,0);e._state.media=null;e.setPaused(true);assert.equal(e._state.call.input,'idle');e.destroy();
// A declared continuation may execute the requested skill after the preceding speech.
e=make();e.joinCall();e.sendMessage('请画一只抱月亮的猫');let rounds=0;const client=e.client;e.client={cancel(){},async generate(p){if(p.kind==='review')return reviewReply(p);rounds++;return result({speech:rounds===1?'我来画一只抱着月亮的小猫。':'现在把画板打开。',handledIds:p.context.host.inbox.map(m=>m.id),continuation:'continue'});}};
await e._think();e.completeSpeech(e._active.id);assert.equal(e.callNeedsTurn,true);e.nextWake=0;await e._think();assert.equal(rounds,2);e.completeSpeech(e._active.id);assert.equal(e.callNeedsTurn,false,'only one follow-up, no monologue loop');e.client=client;e.destroy();
const origin='https://call.test';const call={active:true,id:crypto.randomUUID(),input:'idle',turns:1,audienceOpen:false,story:null};
const value={requestId:crypto.randomUUID(),kind:'host',eventId:'call-test',message:'决定下一步',context:{phase:'chat',host:{inbox:[],memory:''},call}};
const request=v=>new Request(origin+'/api/agent',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','oai-authenticated-user-id':'call-tests'},body:JSON.stringify(v)});
let captured;
const options={fetch:async(url,init)=>{captured=JSON.parse(init.body);return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(result({action:'tool',tool:{name:'offer_call_story',args:{}}}))}}]});}};
let res=await handleAgent(request(value),{DEEPSEEK_API_KEY:'fixture'},options);assert.equal(res.status,200,await res.clone().text());assert.equal((await res.json()).tool.name,'offer_call_story');assert.match(captured.messages[0].content,/连麦陪伴/);assert.match(captured.messages[0].content,/Vivi|声线/);assert.match(captured.messages[0].content,/最终宿主适配/);assert.equal(JSON.parse(captured.messages[1].content).context.call.catalog.id,CALL_STORY.id);
for(const badCall of [{...call,active:false},{...call,input:'invented'},{...call,catalog:{id:'invented'}},{...call,story:{id:CALL_STORY.id,step:1,status:'completed',choices:['rest']}},{...call,story:{id:CALL_STORY.id,step:0,status:'offered',choices:['rest']}}]){res=await handleAgent(request({...value,requestId:crypto.randomUUID(),context:{...value.context,call:badCall}}),{DEEPSEEK_API_KEY:'fixture'},options);assert.equal(res.status,400);}
assert.throws(()=>parseAgentReply(result({action:'tool',tool:{name:'offer_call_story',args:{}}}),{...value,context:{host:{inbox:[]},phase:'chat'}}));
assert.throws(()=>parseAgentReply(result({action:'tool',tool:{name:'offer_call_story',args:{}}}),{...value,context:{...value.context,call:{...call,story:{status:'completed'}}}}));
assert.doesNotMatch(buildAgentRequest({...value,context:{host:{inbox:[]}}}).system,/【连麦方法】/);
console.log('PASS call mode: shared history, guest priority, input ownership, playback interruption, stale work, story choices, hangup and strict API contracts');
// Reactions keep their own cadence during listening, host reasoning and playback.
e=make();e.joinCall();e.sendMessage('今天工作有点累');e.callNeedsTurn=false;
const audiencePayloads=[];let reactions=0;
e.client={cancel(){},async generate(p){if(p.kind==='review')return reviewReply(p);if(p.kind==='audience'){audiencePayloads.push(p);return {status:'ready',items:[{npc_id:p.context.audience.eligibleNpcIds[0],text:'先歇一歇 '+(++reactions),intent:'reaction',evidence_refs:[p.context.publicEvents.at(-1).id]}]};}return result();}};
const flush=async()=>{for(let i=0;i<14;i++)await Promise.resolve();};
for(let i=0;i<4;i++){
 e._clock=i*5100;e._state.call.input=i<2?'listening':'idle';e.pending=i===2?new AbortController():null;
 if(i===3){e._say('慢慢说，我在听。');e.recordSpeechProgress(e._active.id,'慢慢说');}
 e.audience.tick();await flush();
 if(i===3)e.recordSpeechProgress(e._active.id,'慢慢说，我在');
 e.audience.tick();await flush();e.audience.tick();
}
assert.equal(e.callAudience.length,4,'no two-reaction budget and no idle-only gate');assert.equal(e.callNeedsTurn,false,'reactions never take the guest floor');
assert(audiencePayloads.at(-1).context.publicEvents.some(x=>x.type==='player.message'&&x.text==='今天工作有点累'));
assert(audiencePayloads.at(-1).context.publicEvents.some(x=>x.type==='speech.progress'&&x.text==='慢慢说'));
assert(!JSON.stringify(audiencePayloads).includes('慢慢说，我在听。'),'no unplayed host preview');e.destroy();
console.log('PASS call audience: more than two reactions across listening, reasoning and playback; both public speakers, no planned speech');

// The old voice fades before yielding; no tool or handled-input completion leaks through.
e=make();e.joinCall();e.sendMessage('今天工作很累');await e._think();
const oldSpeech=e._active;let finishFade,cancels=0,oldTool=0;
oldSpeech.onComplete=()=>oldTool++;e.recordSpeechProgress(oldSpeech.id,'来，坐');
e.speechAudio={fadeOut(ms){assert.equal(ms,180);return new Promise(r=>finishFade=r);},cancel(){cancels++;},warmSpeech(){return null;},dropWarmSpeech(){}};
const yielding=e.yieldCallFloor();assert.equal(e._active,oldSpeech);assert.equal(cancels,0);assert.equal(e.callYield.phase,'fading');assert.equal(e._active.onComplete,null);
await e.yieldCallFloor();assert.equal(cancels,0,'repeated speech events do not restart fade');
finishFade();await yielding;assert.equal(cancels,1);assert.equal(e._active,null,'no spoken acknowledgement');assert.equal(e.callYield,null);assert.equal(oldTool,0);assert.equal(e.inbox.length,1);
e.sendMessage('更准确地说，是开会太久');assert.equal(e.inbox.length,2);e.nextWake=0;await e._think();assert(e._active,'reply has no acknowledgement gate');e.completeSpeech(e._active.id);assert.equal(e.inbox.length,0);e.destroy();
e=make();e.joinCall();e._say('还没说完');e.speechAudio={fadeOut(){return new Promise(r=>finishFade=r);},cancel(){},dropWarmSpeech(){}};const leaving=e.yieldCallFloor();e.endCall();e.joinCall();finishFade();assert.equal(await leaving,false);assert.equal(e._active,null,'late fade cannot speak in a new call');e.destroy();
console.log('PASS silent interruption: soft fade, no acknowledgement, combined pending input, no old tools and stale fade guard');

// A fast submission during the fade must still schedule its eventual answer.
e=make();e.joinCall();e._say('先讲半句话');e.speechAudio={fadeOut(){return new Promise(r=>finishFade=r);},cancel(){},warmSpeech(){return null;},dropWarmSpeech(){}};const fast=e.yieldCallFloor();e.sendMessage('我已经补充完了');finishFade();await fast;assert.equal(e._state.call.input,'idle');assert(e.callNeedsTurn);assert.equal(e._active,null);e.nextWake=0;await e._think();assert(e._active);e.destroy();
console.log('PASS fast input during fade is answered without an acknowledgement');
