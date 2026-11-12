import test from "node:test";
import assert from "node:assert/strict";
import { once } from "node:events";
import { readFileSync } from "node:fs";
import { createServer } from "../server.mjs";
import { SpeechService, speechPayload, readSpeechAudio } from "../lib/speech.mjs";
import { speechDelivery } from "../lib/speech-delivery.mjs";

const voices=JSON.parse(readFileSync(new URL("../tts-voices.json",import.meta.url)));
const config=()=>({enabled:true,apiKey:"fixture-secret-do-not-leak",voices});
const audio=Buffer.from([0,0,0,16,0,240,0,0]);
function sse({complete=true,bytes=audio,split=false}={}) {
  const text='event: 352\r\ndata: '+JSON.stringify({code:0,data:bytes.toString("base64")})+'\r\n\r\n'+
    (complete?'event: 152\ndata: {"code":20000000,"message":"合成结束"}\n\n':'');
  const raw=new TextEncoder().encode(text);
  return new Response(new ReadableStream({start(c){
    if(split)for(const b of raw)c.enqueue(new Uint8Array([b]));else c.enqueue(raw);c.close();
  }}),{headers:{"content-type":"text/event-stream"}});
}
test("delivery is optional and nonempty valid text is passed byte-for-byte without a mood template",()=>{
  for(const value of [undefined,null,""," \n\t",44,{},"x".repeat(1001),"bad\u0000value"])
    assert.equal(speechPayload(voices.liyuan,"没事。",value).req_params.additions,undefined);
  const delivery='  不舍，但故作轻松。\n句尾放轻。  ';
  const p=speechPayload(voices.liyuan,"没事。",delivery);
  assert.deepEqual(JSON.parse(p.req_params.additions),{context_texts:[delivery]});
  assert.equal(p.req_params.text,"没事。");
  assert.equal(p.req_params.model,"seed-tts-2.0-expressive");
  assert.equal(speechDelivery({}).warning,"tts_delivery_invalid");
  assert.throws(()=>speechPayload({...voices.liyuan,model:"seed-tts-2.0-standard"},"好。",delivery),{code:"tts_configuration_error"});
});
test("SSE parser handles byte-split UTF-8 and requires a terminal success with complete PCM samples",async()=>{
  assert.equal((await readSpeechAudio(sse({split:true}))).base64,audio.toString("base64"));
  await assert.rejects(readSpeechAudio(sse({complete:false})),{code:"tts_invalid_audio"});
  await assert.rejects(readSpeechAudio(sse({bytes:Buffer.from([1])})),{code:"tts_invalid_audio"});
  await assert.rejects(readSpeechAudio(sse({bytes:Buffer.alloc(0)})),{code:"tts_invalid_audio"});
});
test("one synthesis per proposal; duplicates share the result and a cancelled attempt is never retried",async()=>{
  let calls=0;const speech=new SpeechService({config,fetch:async()=>{calls++;return sse();}});
  const input={worldId:"w",runId:"r",proposalId:"p",conversationId:"c",actorId:"z",text:"你好。"};
  const results=await Promise.all([speech.synthesize(input),speech.synthesize(input)]);
  assert.equal(calls,1);assert.deepEqual(results[0],results[1]);
  await speech.synthesize(input);assert.equal(calls,1);
  speech.cancel("w","c","p");
  await assert.rejects(speech.synthesize(input),{code:"tts_cancelled"});assert.equal(calls,1);
});
test("disconnect and timeout cancel upstream, sanitize provider errors and do not auto retry",async()=>{
  let calls=0;const speech=new SpeechService({config,timeoutMs:10,fetch:async(_u,{signal})=>{
    calls++;return new Promise((_ok,fail)=>signal.addEventListener("abort",()=>fail(Error("fixture-secret-do-not-leak")),{once:true}));
  }});
  const input={worldId:"w",runId:"r",proposalId:"p",conversationId:"c",actorId:"z",text:"你好。"};
  await assert.rejects(speech.synthesize(input),{code:"tts_timeout"});
  await assert.rejects(speech.synthesize(input),{code:"tts_timeout"});assert.equal(calls,1);
  const controller=new AbortController();controller.abort();
  await assert.rejects(speech.synthesize({...input,proposalId:"p2"},controller.signal),{code:"tts_cancelled"});assert.equal(calls,1);
  const broken=new SpeechService({config,fetch:async()=>{throw Error("fixture-secret-do-not-leak");}});
  await assert.rejects(broken.synthesize(input),e=>e.code==="tts_failed"&&!e.message.includes("fixture-secret"));
});
test("a late cancellation of an old line does not cancel its replacement; disconnect aborts the live request",async()=>{
  const requests=[];
  const speech=new SpeechService({config,fetch:async(_url,{signal})=>new Promise((resolve,reject)=>{
    requests.push({signal,resolve});signal.addEventListener("abort",()=>reject(Error("aborted")),{once:true});
  })});
  const input={worldId:"w",runId:"r",proposalId:"old",conversationId:"c",actorId:"z",text:"你好。"};
  const old=speech.synthesize(input);const rejected=assert.rejects(old,{code:"tts_cancelled"});
  const disconnect=new AbortController();
  const current=speech.synthesize({...input,proposalId:"current"},disconnect.signal);
  await rejected;assert.equal(requests.length,2);
  speech.cancel("w","c","old");assert.equal(requests[1].signal.aborted,false);
  disconnect.abort();await assert.rejects(current,{code:"tts_cancelled"});assert.equal(speech.active.size,0);
});

function reply(c,delivery) {
  const a=c.available_actions.find(a=>a.type==="wait");
  return {dialogue:"你好，今天过得怎么样？",action:{action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:"回应"},
    info:structuredClone(c.character_state.info),...(delivery===undefined?{}:{delivery})};
}
async function setup(t,{delivery,request,realRuntime=false}={}) {
  const seen=[];
  const speech=new SpeechService({config,fetch:request|| (async(_url,options)=>{seen.push(options);return sse();})});
  const provider={mode:"simulated-test",config:{topK:6},publicConfig:()=>({configured:true}),request:(messages,options)=>({messages,...options}),
    call:async req=>({text:JSON.stringify(reply(JSON.parse(req.messages.at(-1).content),delivery)),finishReason:"stop"})};
  const app=createServer({dbPath:":memory:",speech,backgroundEnabled:false,providerFactory:()=>provider,
    ...(realRuntime?{}:{decide:c=>reply(c,delivery)})});
  app.server.listen(0,"127.0.0.1");await once(app.server,"listening");
  const base="http://127.0.0.1:"+app.server.address().port;
  t.after(async()=>{speech.cancel();app.server.close();app.server.closeAllConnections();await once(app.server,"close");app.store.close();});
  const post=async(path,body)=>{const r=await fetch(base+"/api/game/"+path,{method:"POST",headers:{"Content-Type":"application/json","X-Z-Memory":"1"},body:JSON.stringify(body)});return {code:r.status,...await r.json()};};
  return {app,speech,seen,post};
}
const base={worldId:"speech-test",runId:"run1",day:1,period:0};
async function proposed(post,actor="z",conversation="c1",seq=1) {
  await post("focus",{...base,actorId:actor});
  const event={id:"player-"+seq,seq,kind:"utterance",day:1,period:0,speakerId:"player",recipientId:actor,
    conversationId:conversation,channel:"private_player_chat",text:"你好。",locationId:"main_street"};
  assert.equal((await post("sync",{...base,eventData:event})).ok,true);
  const r=await post("utterance",{...base,actorId:actor,recipientId:"player",conversationId:conversation,channel:"private_player_chat",scene:"Street",locationId:"main_street"});
  assert.equal(r.ok,true,r.error);
  return {r,event,request:{...base,actorId:actor,conversationId:conversation,proposalId:r.proposalId}};
}
async function commit(post,p) {
  const r=await post("sync",{...base,eventData:{...p.event,id:"npc-"+p.event.seq,seq:p.event.seq+1,speakerId:p.request.actorId,recipientId:"player",text:p.r.text,proposalId:p.r.proposalId}});
  assert.equal(r.ok,true,r.error);
}
test("all eight island actors use their configured voice only after a committed private reply; chenchen stays server-only",async t=>{
  const {post,app,seen}=await setup(t);await post("open",base);
  assert(!app.catalog.actors.has("chenchen"));assert.equal(voices.chenchen.speaker,"S_7nhRdCXi2");
  let seq=1;
  for(const actor of app.catalog.actors.keys()) {
    const p=await proposed(post,actor,"c-"+actor,seq);seq+=2;
    assert.equal((await post("speech",p.request)).ok,false);assert.equal(p.r.delivery,"");
    await commit(post,p);
    const response=await post("speech",{...p.request,text:"客户端不能替换台词",delivery:"客户端不能替换指令"});
    assert.equal(response.ok,true,response.error);assert.equal(response.audio.format,"pcm_s16le");
    const sent=seen.at(-1),payload=JSON.parse(sent.body).req_params;
    assert.equal(payload.speaker,voices[actor].speaker);assert.equal(payload.text,p.r.text);assert.equal(payload.additions,undefined);
    assert.equal(sent.headers["X-Api-Resource-Id"],voices[actor].resourceId);
  }
  assert.equal(seen.length,8);
});
test("upstream delivery survives real ActorRuntime validation and proposal storage unchanged",async t=>{
  const delivery="  心情复杂，语速稍慢。\n最后一句很轻。  ";
  const {post,seen,app}=await setup(t,{delivery,realRuntime:true});await post("open",base);
  const p=await proposed(post);assert.equal(p.r.delivery,delivery);assert(!Object.hasOwn(p.r.result,"delivery"));
  await commit(post,p);assert.equal((await post("speech",p.request)).ok,true);
  assert.deepEqual(JSON.parse(JSON.parse(seen[0].body).req_params.additions),{context_texts:[delivery]});
  const snapshot=app.engine.checkpoint(base.worldId);
  assert(!snapshot.events?.some(e=>e.text===delivery));
});
test("invalid delivery falls back without blocking the turn or copying the emotion field",async t=>{
  const {post,seen}=await setup(t,{delivery:{emotion:"happy"}});await post("open",base);
  const p=await proposed(post);assert.equal(p.r.delivery,"");await commit(post,p);
  const r=await post("speech",p.request);assert.equal(r.ok,true);assert.equal(r.speechWarning,"tts_delivery_invalid");
  assert.equal(JSON.parse(seen[0].body).req_params.additions,undefined);
});
test("old world/run, nonplayer and older-turn requests cannot synthesize; focus changes invalidate cached audio",async t=>{
  const {post,seen,app}=await setup(t);await post("open",base);
  const p=await proposed(post);await commit(post,p);
  assert.equal((await post("speech",{...p.request,runId:"old"})).ok,false);
  assert.equal((await post("speech",{...p.request,worldId:"other"})).ok,false);
  const record=app.store.record(p.r.proposalId);record.context.interaction.mode="npc_message";app.store.put("proposal",record);
  assert.equal((await post("speech",p.request)).ok,false);
  record.context.interaction.mode="player";app.store.put("proposal",record);
  assert.equal((await post("speech",p.request)).ok,true);assert.equal(seen.length,1);
  await post("focus",{...base,actorId:"liting"});assert.equal((await post("speech",p.request)).ok,false);
  await post("focus",{...base,actorId:"z"});
  await post("sync",{...base,eventData:{...p.event,id:"next-player",seq:3,text:"再说一句。"}});
  assert.equal((await post("speech",p.request)).ok,false);assert.equal(seen.length,1);
});
test("conversation cancellation aborts an in-flight provider request and releases its slot",async t=>{
  let started;const began=new Promise(r=>started=r);let aborted=false;
  const {post,speech}=await setup(t,{request:async(_u,{signal})=>{started();return new Promise((_r,reject)=>signal.addEventListener("abort",()=>{aborted=true;reject(Error("aborted"));},{once:true}));}});
  await post("open",base);const p=await proposed(post);await commit(post,p);
  const pending=post("speech",p.request);await began;
  await post("speech/cancel",p.request);
  const result=await pending;assert.equal(result.speechError,"tts_cancelled");assert(aborted);assert.equal(speech.active.size,0);
});

test("advancing the game clock rejects a previously committed line even while focus remains",async t=>{
  const {post,seen}=await setup(t);await post("open",base);
  const p=await proposed(post);await commit(post,p);
  const clock=await post("sync",{...base,period:1,eventData:{id:"clock-next",seq:3,day:1,period:1,kind:"clock"}});
  assert.equal(clock.ok,true,clock.error);
  assert.equal((await post("speech",{...p.request,period:1})).ok,false);assert.equal(seen.length,0);
});
