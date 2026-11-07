import assert from 'node:assert/strict';
import {CallConversationEngine} from '../ui/call-engine.js';
import {handleAgent,parseAgentReply} from '../worker/agent-director.js';
import {reviewReply} from './review-fixture.mjs';
if(!globalThis.CustomEvent)globalThis.CustomEvent=class extends Event{constructor(t,{detail}){super(t);this.detail=detail;}};
const payloads=[];
const reply=p=>({action:'speak',speech:p.context.call.audienceCandidates.length?'你先歇一会儿。晚风来信也说开会很累，今晚就慢慢聊。':'嗯，你慢慢说。',mood:'warm',gesture:'none',transition:'smooth',handledIds:p.context.host.inbox.map(x=>x.id),wakeAfterMs:9000,continuation:'await_event',memory:'',audienceMessageIds:p.context.call.audienceCandidates.slice(0,1).map(m=>m.id)});
const e=new CallConversationEngine();e.configureAgent({cancel(){},async generate(p){if(p.kind==='review')return reviewReply(p);if(p.kind==='audience')return {status:'skip',items:[]};payloads.push(p);return reply(p);}},{enabled:true});e.setSpeechDriven(true);e.start();clearInterval(e._timer);e.joinCall();
const add=(id,text)=>e._receive({id,name:'晚风来信',text},false,'wind');
try{
 e._clock=1000;e.sendMessage('今天开会很累');add('npc-1','开会辛苦了，先休息一下');await e._think();assert.equal(payloads.at(-1).context.call.audienceCandidates.length,0,'first turn stays with player');e.completeSpeech(e._active.id);
 e._clock=22000;e.sendMessage('主要是坐得太久');add('npc-2','开会后散散步吧');await e._think();const current=payloads.at(-1);assert.equal(current.context.call.audienceReason,'cadence');assert.equal(current.context.call.audienceCandidates.length,2);assert(current.context.host.inbox.every(m=>m.mine),'NPCs are not player requests');assert.equal(e._active.source,'deepseek');const selected=reply(current).audienceMessageIds[0];e.completeSpeech(e._active.id);assert(e.callAudienceUsed.has(selected));
 e._clock=23000;e.sendMessage('先和你聊会儿');await e._think();assert.equal(payloads.at(-1).context.call.audienceCandidates.length,0,'twenty-second cooldown');e.completeSpeech(e._active.id);
 e.sendMessage('看看弹幕怎么说');add('npc-3','我也喜欢开会后看月亮');await e._think();assert.equal(payloads.at(-1).context.call.audienceReason,'invited');const interrupted=e._active.id;e.recordSpeechProgress(interrupted,'你先歇');e.setCallInput('listening');assert.equal(e._active,null);assert(e.inbox.some(m=>m.mine),'reading audience cannot consume an interrupted guest turn');assert(!e._callAudienceEligible());
 e.setCallInput('idle');e._state.media={phase:'playing'};assert(!e._callAudienceEligible(),'no host TTS over a song');e._state.media=null;
 // Server rejects unknown citations and candidates without published evidence.
 const reqBody={...current,requestId:crypto.randomUUID()};const origin='http://call.test';const request=body=>new Request(origin+'/api/agent',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','oai-authenticated-user-id':'test'},body:JSON.stringify(body)});
 const ok=await handleAgent(request(reqBody),{DEEPSEEK_API_KEY:'test'},{fetch:async()=>Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(reply(current))}}]})});assert.equal(ok.status,200,await ok.clone().text());assert.equal((await ok.json()).audienceMessageIds.length,1);
 assert.throws(()=>parseAgentReply({...reply(current),speech:'大家都让你休息一下。'},current));
 assert.throws(()=>parseAgentReply({...reply(current),audienceMessageIds:['invented']},current));assert.throws(()=>parseAgentReply({...reply(current),handledIds:[]},current));
 const tampered=structuredClone(reqBody);tampered.requestId=crypto.randomUUID();tampered.context.call.audienceCandidates[0].text='从未发布的弹幕';const denied=await handleAgent(request(tampered),{DEEPSEEK_API_KEY:'test'},{fetch:()=>{throw Error('should not reach provider');}});assert.equal(denied.status,400);
 e.callAudience.forEach(m=>m.at=0);e._clock=100000;e._state.call.audienceUntil=100001;assert.equal(e._callAudienceCandidates(current.context).length,0,'expired audience messages cannot be read');
 console.log('PASS multiparty: two-turn/20s cadence, explicit invitation, guest-first combined reply, evidence-bound citations, deduplication, expiry, interruption and singing exclusion');
}finally{e.destroy();}
