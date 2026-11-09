import assert from 'node:assert/strict';
import {handleAgent,parseAgentReply,hostStreamPrefix,completeHostSentences,compactReviewInput} from '../worker/agent-director.js';
import {AgentClient} from '../ui/agent-client.js';
import {CallConversationEngine} from '../ui/call-engine.js';
import {reviewReply} from './review-fixture.mjs';
const defer=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve};};
const flush=async()=>{for(let i=0;i<100;i++)await Promise.resolve();};
const header={action:'speak',mood:'warm',gesture:'nod',transition:'smooth',handledIds:['player'],audienceMessageIds:[]};
const result={...header,speech:'今天辛苦了。想说说哪件事最累吗？',wakeAfterMs:2000,continuation:'await_event',memory:'玩家今天累。',agendaUpdates:[]};
const wire=JSON.stringify(result),beforeSpeech=wire.indexOf('今天'),cut=wire.indexOf('想说');
for(let i=1;i<=wire.length;i++){
 const prefix=hostStreamPrefix(wire.slice(0,i));if(prefix.fields.speech!==undefined)assert(result.speech.startsWith(prefix.fields.speech));
}
assert.deepEqual(completeHostSentences('她说“先等等！再说。”然后点头。下一句'),['她说“先等等！再说。”然后点头。']);
assert.deepEqual(completeHostSentences('我想……'),[]);
assert.deepEqual(completeHostSentences('版本1.2很好。继续'),['版本1.2很好。']);
assert.equal(hostStreamPrefix(''+wire.slice(0,beforeSpeech)+'\\u4').fields.speech,'');
assert.throws(()=>hostStreamPrefix('{"action":"speak","action":"tool"}'),/duplicate/);
const fact={id:'f',type:'player.message',text:'别唱歌',speakerRole:'player',speakerId:'p',sequence:1,status:'published',occurredAt:1};
const compact=compactReviewInput({publicEvents:[fact],sessionEvents:[fact,{...fact,text:'可以唱歌',occurredAt:0}],waiting_messages:[{id:'p',text:'其实还是别唱'}]});
assert.equal(compact.sessionEvents.length,1);assert.equal(compact.sessionEvents[0].text,'可以唱歌');assert.equal(compact.waiting_messages[0].text,'其实还是别唱');assert(!('sequence' in compact.publicEvents[0]));

const payload=()=>({requestId:crypto.randomUUID(),kind:'host',eventId:'test',context:{phase:'chat',call:{active:true,id:crypto.randomUUID(),input:'idle',turns:1,audienceOpen:false,story:null},host:{inbox:[{id:'player',name:'玩家',text:'今天好累',mine:true}],memory:''}}});
const encoder=new TextEncoder(),sse=content=>encoder.encode('data: '+JSON.stringify({choices:[{delta:{content},finish_reason:null}]})+'\n\n');
let supply,calls=0,requestBody;
const input=payload();
const response=await handleAgent(new Request('http://local/api/agent',{method:'POST',headers:{Origin:'http://local','Content-Type':'application/json',Accept:'application/x-ndjson','oai-authenticated-user-id':'sentence-test'},body:JSON.stringify(input)}),{DEEPSEEK_API_KEY:'secret-fixture'},{fetch:async(_url,init)=>{
 calls++;requestBody=JSON.parse(init.body);return new Response(new ReadableStream({start(c){supply=c;}}),{headers:{'Content-Type':'text/event-stream'}});
}});
assert.match(response.headers.get('Content-Type'),/ndjson/);const reader=response.body.getReader();await flush();
supply.enqueue(sse(wire.slice(0,cut+1)));const first=JSON.parse(new TextDecoder().decode((await reader.read()).value));assert.equal(first.type,'sentence');assert.equal(first.text,'今天辛苦了。');assert.equal(requestBody.stream,true);assert.equal(requestBody.thinking.type,'disabled');
supply.enqueue(sse(wire.slice(cut+1)));supply.enqueue(encoder.encode('data: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n'));supply.close();
const rest=[];for(;;){const chunk=await reader.read();if(chunk.done)break;rest.push(...new TextDecoder().decode(chunk.value).trim().split('\n').map(JSON.parse));}
assert.equal(rest.filter(e=>e.type==='sentence').length,1);assert.equal(rest.at(-1).result.speech,result.speech);assert.equal(calls,1);
const replay=await handleAgent(new Request('http://local/api/agent',{method:'POST',headers:{Origin:'http://local','Content-Type':'application/json',Accept:'application/x-ndjson','oai-authenticated-user-id':'sentence-test'},body:JSON.stringify(input)}),{DEEPSEEK_API_KEY:'secret-fixture'},{fetch:()=>assert.fail('must not generate twice')});assert.equal((await replay.text()).trim().split('\n').length,3);

const spaced={...result,speech:'今天辛苦了。 想说说哪件事最累吗？'};
const spacedResponse=await handleAgent(new Request('http://local/api/agent',{method:'POST',headers:{Origin:'http://local','Content-Type':'application/json',Accept:'application/x-ndjson','oai-authenticated-user-id':'space-test'},body:JSON.stringify(payload())}),{DEEPSEEK_API_KEY:'secret-fixture'},{fetch:async()=>new Response(new ReadableStream({start(c){c.enqueue(sse(JSON.stringify(spaced)));c.enqueue(encoder.encode('data: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n'));c.close();}}))});
const spacedEvents=(await spacedResponse.text()).trim().split('\n').map(JSON.parse);assert.equal(spacedEvents.at(-1).type,'result');assert.equal(spacedEvents[1].text,'想说说哪件事最累吗？');

// Transport failures never produce a committed final decision; cancellation reaches upstream.
for(const mode of ['truncated','length','abort']){
 const controller=new AbortController();let upstreamSignal,cancelled=false;
 const broken=await handleAgent(new Request('http://local/api/agent',{method:'POST',signal:controller.signal,headers:{Origin:'http://local','Content-Type':'application/json',Accept:'application/x-ndjson','oai-authenticated-user-id':'broken-'+mode},body:JSON.stringify(payload())}),{DEEPSEEK_API_KEY:'secret-fixture'},{fetch:async(_url,init)=>{
  upstreamSignal=init.signal;return new Response(new ReadableStream({start(c){
   c.enqueue(sse(wire.slice(0,cut+1)));
   if(mode==='length')c.enqueue(encoder.encode('data: '+JSON.stringify({choices:[{delta:{},finish_reason:'length'}]})+'\n\ndata: [DONE]\n\n'));
   if(mode!=='abort')c.close();
  },cancel(){cancelled=true;}}));
 }});
 if(mode==='abort'){await flush();controller.abort();await broken.text();await flush();assert(upstreamSignal.aborted);assert(cancelled);}
 else{const events=(await broken.text()).trim().split('\n').map(JSON.parse);assert.equal(events.at(-1).type,'error');assert(!events.some(e=>e.type==='result'));}
}

// Browser transport delivers a whole sentence before the final metadata arrives.
let downstream;const client=new AgentClient({fetchImpl:async()=>new Response(new ReadableStream({start(c){downstream=c;}}),{headers:{'Content-Type':'application/x-ndjson'}})});const got=[];
const pending=client.generate(input,{onSentence:s=>got.push(s.text)});await flush();downstream.enqueue(encoder.encode(JSON.stringify({...first,requestId:input.requestId})+'\n'));await flush();assert.deepEqual(got,['今天辛苦了。']);downstream.enqueue(encoder.encode(JSON.stringify({type:'result',requestId:input.requestId,result:{...result,requestId:input.requestId}})+'\n'));downstream.close();await pending;client.cancel();

// Real scheduler: do not mark a player answered until all sentences finish.
for(const mode of ['complete','barge-in','soft-yield','drop-second','invalid-final','tool']){
 const engine=new CallConversationEngine(),final=defer();let callback,hostPayload,reviewCount=0,executed=0;
 engine.configureAgent({sentenceStreaming:true,cancel(){},generate(p,options){if(p.kind==='review'){reviewCount++;assert.equal(options.compactReview,true);return Promise.resolve(reviewReply(p,mode==='drop-second'&&reviewCount===2?'drop':'allow'));}hostPayload=p;callback=options.onSentence;return final.promise;}},{enabled:true});
 engine.start();engine.setSpeechDriven(true);engine.joinCall();engine.sendMessage('今天好累');engine.audience.tick=()=>{};engine._executeTool=()=>executed++;
 const thinking=engine._think();await flush();const ids=hostPayload.context.host.inbox.map(m=>m.id),base={...result,handledIds:ids};
 if(mode==='tool'){
  final.resolve({...base,action:'tool',speech:'我来准备。',tool:{name:'prepare_song',args:{messageId:ids[0]}}});await thinking;assert.equal(executed,0);engine.completeSpeech(engine._active.id);assert.equal(executed,1);engine.destroy();continue;
 }
 callback({index:0,text:'今天辛苦了。',...header,handledIds:ids});await flush();assert.equal(engine._active.text,'今天辛苦了。');assert.equal(engine.memory,'');assert.equal(engine.inbox.length,1);
 const firstId=engine._active.id;engine.recordSpeechProgress(firstId,'今天辛苦了。');
 callback({index:1,text:'想说说哪件事最累吗？',...header,handledIds:ids});
 if(mode==='soft-yield'){
  const fade=defer();let cancelled=0;engine.speechAudio={fadeOut:()=>fade.promise,cancel(){cancelled++;},dropWarmSpeech(){}};
  const yielding=engine.yieldCallFloor();assert.equal(cancelled,0);assert.equal(engine._active.id,firstId);
  final.resolve(base);await thinking;assert.equal(cancelled,0);assert.equal(engine._active.id,firstId);
  fade.resolve();await yielding;assert.equal(cancelled,1);assert.equal(engine._active,null);assert.equal(engine.memory,'');
 }else if(mode==='barge-in'){
  engine.setCallInput('listening');engine.sendMessage('不是累，是有点紧张');final.resolve(base);await thinking;await flush();assert.equal(engine._active,null);assert.equal(engine.memory,'');assert.equal(engine.inbox.length,2);assert(!engine.session.evidence().some(e=>e.text.includes('哪件事最累')));
 }else if(mode==='invalid-final'){
  final.resolve({...base,speech:'different'});await thinking;assert.equal(engine._active,null);assert.equal(engine.inbox.length,1);assert.equal(engine.memory,'');
 }else{
  engine.completeSpeech(firstId);await flush();final.resolve(base);await flush();
  if(mode==='drop-second'){await thinking;assert.equal(engine._active,null);assert.equal(engine.memory,'');assert.equal(engine.inbox.length,1);assert.equal(engine.getState().agent.source,'retrying');assert.equal(engine.getState().agent.errorCode,'agent_reply_rejected');assert.equal(engine._hostContext(engine.inbox).host.revisionFeedback.candidate,'想说说哪件事最累吗？');assert.equal(engine._hostContext(engine.inbox).host.revisionFeedback.reason,'fixture verdict');engine.sendMessage('换个话题');assert.equal(engine._hostContext(engine.inbox).host.revisionFeedback,undefined);}
  else{assert.equal(engine._active.text,'想说说哪件事最累吗？');assert.equal(engine.inbox.length,1);engine.recordSpeechProgress(engine._active.id,engine._active.text);engine.completeSpeech(engine._active.id);await thinking;assert.equal(engine.inbox.length,0);assert.equal(engine.memory,base.memory);}
 }
 engine.destroy();
}
// A new question can be answered without falsely marking older questions answered.
{
 const engine=new CallConversationEngine();let hostPayload;
 engine.configureAgent({cancel(){},generate(p){if(p.kind==='review')return Promise.resolve(reviewReply(p));hostPayload=p;return Promise.resolve({...result,handledIds:[p.context.host.inbox.at(-1).id]});}},{enabled:true});
 engine.start();clearInterval(engine._timer);engine.setSpeechDriven(true);engine.joinCall();engine.sendMessage('刚才那个问题先放着');const old=engine.inbox[0].id;engine.sendMessage('现在想聊聊紧张');const latest=engine.inbox.at(-1).id;
 await engine._think();assert(engine._active);assert.doesNotThrow(()=>parseAgentReply({...result,handledIds:[latest]},hostPayload));assert.throws(()=>parseAgentReply({...result,handledIds:[old]},hostPayload));
 engine.recordSpeechProgress(engine._active.id,engine._active.text);engine.completeSpeech(engine._active.id);assert.deepEqual(engine.inbox.map(m=>m.id),[old]);assert(engine.session.evidence().some(e=>e.type==='player.message'&&e.text.includes('刚才那个问题')));engine.destroy();
}
console.log('PASS incremental JSON/SSE, quoting and duplicate keys, lossless review compaction, same-ID replay, client early delivery, per-sentence review, heard-prefix evidence, barge-in cancellation, rejected/invalid tails and tool gating');
