import {test} from 'node:test';
import assert from 'node:assert/strict';
import {EventEmitter} from 'node:events';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {NativeVoiceService} from '../server/voiceprint/native-service.mjs';
import {PcmSpeech} from '../server/pcm-speech.mjs';
import {Providers,loadEnvironment} from '../server/providers.mjs';
import {CallConversationEngine} from '../server/shared/ui/call-engine.js';

async function callFixture(t){
 const e=new CallConversationEngine();e.setSpeechDriven(true);e.configureAgent({sentenceStreaming:true,cancel(){},generate:async()=>{throw Error('unset');}},{enabled:true});e.start();clearInterval(e._timer);e.setPaused(false);e.joinCall();e.sendMessage('今天办公室好吵。');t.after(()=>e.destroy());return e;
}
function approve(p){return {decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'test'};}
async function until(check){for(let i=0;i<200&&!check();i++)await new Promise(r=>setTimeout(r,5));assert(check(),'condition timed out');}
test('native call scheduler plays every sentence once and commits memory only after the final receipt',async t=>{
 const e=await callFixture(t),played=[],reviewed=[];let release;
 e.addEventListener('speak',({detail})=>played.push(detail));
 e.client.generate=async(p,o)=>{if(p.kind==='review'){reviewed.push(p.context.candidate.text);return approve(p);}
  const base={mood:'warm',gesture:'none',transition:'smooth',handledIds:e.inbox.map(m=>m.id),audienceMessageIds:[]};
  o.onSentence({index:0,...base,text:'今天真是辛苦了。'});o.onSentence({index:1,...base,text:'我们慢慢聊。'});
  await new Promise(r=>release=r);return {action:'speak',...base,speech:'今天真是辛苦了。我们慢慢聊。',wakeAfterMs:2000,continuation:'leave_space',memory:'本轮已回应',agendaUpdates:[]};
 };
 const thinking=e._think();await until(()=>played.length===1);assert.equal(e.memory,'');assert.equal(reviewed.length,1);
 e.recordSpeechProgress(played[0].id,'今天真是');e.completeSpeech(played[0].id);await until(()=>played.length===2);assert.equal(e.memory,'');assert.equal(e.inbox.length,1);
 release();await tick();e.completeSpeech(played[1].id);await thinking;
 assert.deepEqual(played.map(s=>s.text),['今天真是辛苦了。','我们慢慢聊。']);assert.equal(e.inbox.length,0);assert.equal(e.memory,'本轮已回应');
});
test('quiet barge-in cancels a queued second sentence and preserves the played prefix plus new player input',async t=>{
 const e=await callFixture(t),played=[];let release;
 e.speechAudio={cancel(){},dropWarmSpeech(){},warmSpeech(){},fadeOut:()=>new Promise(r=>release=r)};
 e.addEventListener('speak',({detail})=>played.push(detail));
 e.client.generate=async(p,o)=>{if(p.kind==='review')return approve(p);const base={mood:'warm',gesture:'none',transition:'smooth',handledIds:e.inbox.map(m=>m.id),audienceMessageIds:[]};for(const [index,text] of ['今天真是辛苦了。','我们慢慢聊。'].entries())o.onSentence({index,text,...base});return {action:'speak',...base,speech:'今天真是辛苦了。我们慢慢聊。',wakeAfterMs:2000,continuation:'leave_space',memory:'不可提交',agendaUpdates:[]};};
 const thinking=e._think();await until(()=>played.length===1);e.recordSpeechProgress(played[0].id,'今天真是');const fade=e.yieldCallFloor();e.sendMessage('我还想补充一件事。');release();await fade;await thinking;
 assert.equal(played.length,1);assert.equal(e.memory,'');assert(e.inbox.some(m=>m.text==='今天办公室好吵。'));assert(e.inbox.some(m=>m.text==='我还想补充一件事。'));
 assert(e.facts.some(f=>f.type==='speech.progress'&&f.text==='今天真是'));assert(!e.facts.some(f=>f.type==='speech.completed'));assert.equal(e._active,null);
});

const tick=()=>new Promise(resolve=>setImmediate(resolve));
test('compact review reaches the real provider adapter with candidate and deduplicated evidence intact',async()=>{
 let called=false;
 const p=new Providers({DEEPSEEK_API_KEY:'test-review'},{fetch:async(_url,options)=>{called=true;const input=JSON.parse(JSON.parse(options.body).messages[1].content);assert.equal(input.candidate.text,'听起来这一天很忙。');assert.equal(input.publicEvents.length,1);assert.equal(input.historyOmitted,0);assert.equal(input.speakerNames.salt,'海盐汽水');assert(input.executionContext);return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify({decision:'allow'})}}]});}});
 const event={id:'p1',type:'player.message',text:'今天很忙',speakerRole:'player',speakerId:'player'};
 const result=await p.generate('compact-native-test',{requestId:crypto.randomUUID(),kind:'review',eventId:'candidate',context:{revision:1,publicEvents:[event],sessionEvents:[event],waiting_messages:[],candidate:{id:'candidate',role:'host',speakerId:'Z',text:'听起来这一天很忙。',intent:'speak',replyTo:['p1'],evidenceIds:[]}}},{compactReview:true});
 assert(called);assert.equal(result.decision,'allow');
});
test('blank native config template cannot consume the next credential line',async t=>{
 const root=await fs.mkdtemp(path.join(os.tmpdir(),'z-empty-native-config-'));t.after(()=>fs.rm(root,{recursive:true,force:true}));
 await fs.writeFile(path.join(root,'.env.local'),'DEEPSEEK_API_KEY=\nVOLC_TTS_API_KEY= # not configured\r\nVOLC_ASR_API_KEY=\nZ_CALL_VOICE_MODE=voiceprint\n');
 const env=loadEnvironment(root);for(const key of ['DEEPSEEK_API_KEY','VOLC_TTS_API_KEY','VOLC_ASR_API_KEY'])if(!process.env[key])assert.equal(env[key],'');
});
test('native voice identity gates interruption/final input, hides room credentials and rejects stale calls',async t=>{
 const root=await fs.mkdtemp(path.join(os.tmpdir(),'z-rtc-native-'));t.after(()=>fs.rm(root,{recursive:true,force:true}));
 const socket=new EventEmitter();socket.readyState=1;socket.send=()=>{};socket.close=()=>{};socket.terminate=()=>{};
 const config={mode:'voiceprint',validMode:true,asrSource:'api-key',missing:[],rtcAppId:'app',rtcAppKey:'secret',voiceprintId:'voice',asrApiKey:'secret'};
 const service=new NativeVoiceService({configRoot:root,profileRoot:root,load:async()=>({...config,missing:[]}),connect:()=>socket,makeToken:()=> 'room-token'});t.after(()=>service.close());
 const events=[],inputs=[],s={id:'session',owner:'local-owner',phase:'live',leaseUntil:Date.now()+60000,sessionControl:{normal:true},engine:{_state:{call:{id:'call',active:true}},yieldCallFloor(){events.push('yield');},setCallInput(){},endCall(){this._state.call.active=false;}},event:(type,p)=>events.push(p),input:b=>inputs.push(b)};
 const rtc=await service.start(s);assert.equal(rtc.token,'room-token');assert.equal((await service.start(s)).token,rtc.token);assert(!JSON.stringify(events).includes('room-token'));
 socket.emit('open');socket.emit('message',JSON.stringify({type:'session.created'}));service.joined(s,'call');assert.equal(events.at(-1).type,'ready');
 const send=(type,extra={})=>socket.emit('message',JSON.stringify({type,conv_id:'one',...extra}));
 send('user.conversation.created');send('user.audio.transcription.started',{sv_active:true});send('user.audio.transcription.delta',{transcript:'办公室有点吵。'});
 await new Promise(r=>setTimeout(r,320));assert(!events.includes('yield'));assert.equal(inputs.length,0);
 send('user.audio.speaker.verification');assert(events.includes('yield'));send('user.audio.transcription.done',{transcript:'办公室有点吵。'});assert.equal(inputs.length,1);assert.equal(inputs[0].payload.text,'办公室有点吵。');
 send('user.audio.transcription.done',{transcript:'办公室有点吵。'});assert.equal(inputs.length,1);
 s.engine._state.call.id='new-call';send('user.conversation.created');assert.equal(service.links.size,0);assert.throws(()=>service.joined(s,'call'),/stale_voice_turn/);
 const remote=await service.config('another-player');assert.equal(remote.voiceprintId,'');assert(remote.missing.includes('VOLC_VOICEPRINT_ID'));
});

test('PCM stream exposes early audio but requires exact subtitle and successful final completion',async()=>{
 let push;const body=new ReadableStream({start(c){push=e=>c.enqueue(new TextEncoder().encode(JSON.stringify(e)+'\n'));}});
 const speech=new PcmSpeech('你好。',async()=>new Response(body));await tick();
 push({type:'start',format:'pcm',sampleRate:24000});push({type:'audio',index:0,data:Buffer.alloc(4800).toString('base64')});await tick();
 assert.equal(speech.bytes,4800);assert.equal(speech.done,false);assert.equal(speech.records[1].type,'audio');
 push({type:'sentence',sentence:{words:[{word:'你好',startTime:0,endTime:.1}]}});push({type:'done'});await speech.ready;assert.equal(speech.durationMs,100);speech.close();
 const mismatch=new PcmSpeech('你好',async()=>new Response(JSON.stringify({type:'start',format:'pcm',sampleRate:24000})+'\n'+JSON.stringify({type:'audio',index:0,data:'AAAAAA=='})+'\n'+JSON.stringify({type:'sentence',sentence:{words:[{word:'再见'}]}})+'\n'+JSON.stringify({type:'done'})+'\n'));
 await assert.rejects(mismatch.ready,/transcript_unverified/);assert.equal(mismatch.done,false);assert.equal(mismatch.records.at(-1).type,'error');mismatch.close();
});

test('native host sends a reviewed-sentence candidate before final JSON and restores short message IDs',async()=>{
 const original=crypto.randomUUID(),seen=[];let finish;
 const result={action:'speak',sessionDecision:null,mood:'warm',gesture:'none',transition:'smooth',handledIds:['inbox_1'],audienceMessageIds:[],speech:'我听见你的问题了。你想从哪里开始？',wakeAfterMs:2000,continuation:'leave_space',memory:'',agendaUpdates:[],tool:null};
 const providers=new Providers({DEEPSEEK_API_KEY:'test'},{fetch:async(_url,options)=>{
  assert.equal(JSON.parse(options.body).stream,true);const text=JSON.stringify(result),cut=text.indexOf('你想')+1;
  return new Response(new ReadableStream({start(c){const send=content=>c.enqueue(new TextEncoder().encode('data: '+JSON.stringify({choices:[{delta:{content}}]})+'\n\n'));send(text.slice(0,cut));finish=()=>{send(text.slice(cut));c.enqueue(new TextEncoder().encode('data: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n'));c.close();};}}));
 }});
 const payload={requestId:crypto.randomUUID(),kind:'host',eventId:'turn',context:{call:{active:true,id:crypto.randomUUID(),input:'idle',turns:1,audienceOpen:false,story:null,audienceCandidates:[],audienceReason:'none'},host:{inbox:[{id:original,name:'你',mine:true,text:'怎么开始'}]},mediaRuntime:{audioEnabled:true,ttsAvailable:true}}};
 const promise=providers.generate('stream-owner',payload,{onSentence:s=>seen.push(s)});for(let i=0;i<100&&!seen.length;i++)await new Promise(r=>setTimeout(r,5));
 const countBeforeFinal=seen.length;finish();assert.equal(countBeforeFinal,1);assert.equal(seen[0].text,'我听见你的问题了。');assert.deepEqual(seen[0].handledIds,[original]);const reply=await promise;
 assert.equal(seen.length,2);assert.equal(seen.map(s=>s.text).join(''),reply.speech);assert.deepEqual(reply.handledIds,[original]);
});
