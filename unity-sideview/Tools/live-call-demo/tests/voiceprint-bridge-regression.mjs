import assert from 'node:assert/strict';
import http from 'node:http';
import { EventEmitter, once } from 'node:events';
import WebSocket from 'ws';
import { attachVoiceprintBridge } from '../server/voiceprint/bridge.mjs';
import { attachAsrBridge } from '../server/asr/bridge.mjs';

class Provider extends EventEmitter {
  constructor() { super(); this.readyState=1; this.sent=[]; queueMicrotask(()=>this.emit('open')); }
  send(data) { const event=JSON.parse(data);this.sent.push(event);if(event.type==='session.create')queueMicrotask(()=>this.event({type:'session.created'})); }
  event(event) { this.emit('message',Buffer.from(JSON.stringify(event))); }
  close() { this.readyState=3; this.emit('close'); }
  terminate() { this.close(); }
}
const server=http.createServer();server.listen(0,'127.0.0.1');await once(server,'listening');const port=server.address().port;
const config={mode:'voiceprint',validMode:true,missing:[],asrSource:'api-key',asrApiKey:'private-asr-key',rtcAppId:'public-app',rtcAppKey:'private-app-key',accessKey:'private-ak',secretKey:'private-sk',voiceprintId:'private-profile'};
let provider;
// Both upgrade listeners must coexist without the old ASR handler rejecting RTC.
const legacy=attachAsrBridge(server,{port,loadConfig:async()=>({enabled:false})});
const bridge=attachVoiceprintBridge(server,{port,loadConfig:async()=>config,connect:()=>provider=new Provider(),makeToken:()=> 'short-lived-room-token'});
const clients=[];
const waitUntil=async predicate=>{const deadline=Date.now()+2000;while(!predicate()){assert(Date.now()<deadline,'event timeout');await new Promise(r=>setTimeout(r,10));}};
async function open(){const ws=new WebSocket(`ws://127.0.0.1:${port}/api/voiceprint/stream`,{origin:`http://127.0.0.1:${port}`});clients.push(ws);const messages=[];ws.on('message',data=>messages.push(JSON.parse(data)));await waitUntil(()=>messages.some(x=>x.type==='rtc'));ws.send(JSON.stringify({type:'joined'}));await waitUntil(()=>messages.some(x=>x.type==='ready'));return {ws,messages,provider};}
try {
  const wrong=new WebSocket(`ws://127.0.0.1:${port}/api/voiceprint/stream`,{origin:'https://other.example'});clients.push(wrong);wrong.on('error',()=>{});const [,response]=await once(wrong,'unexpected-response');assert.equal(response.statusCode,403);response.resume();wrong.terminate();
  const first=await open();assert.equal(first.provider.sent[0].session.ASRConfig.ProviderParams.Credential.ApiKey,'private-asr-key');
  const event=(type,id,extra={})=>first.provider.event({type,conv_id:id,...extra});
  event('user.conversation.created','colleague');event('user.audio.transcription.started','colleague',{sv_active:true});event('user.audio.transcription.done','colleague',{transcript:'旁人说的话'});
  await new Promise(r=>setTimeout(r,340));assert(!first.messages.some(x=>x.type==='transcript'||x.type==='player-speaking'));
  event('user.conversation.canceled','colleague');event('user.conversation.created','player');event('user.audio.transcription.started','player',{sv_active:true});event('user.audio.speaker.verification','colleague');event('user.audio.transcription.delta','player',{transcript:'等一下'});
  await new Promise(r=>setTimeout(r,340));assert(!first.messages.some(x=>x.type==='transcript'));
  event('user.audio.speaker.verification','player');await waitUntil(()=>first.messages.some(x=>x.type==='transcript'));
  event('user.audio.transcription.done','player',{transcript:'等一下，我想补充。'});await waitUntil(()=>first.messages.some(x=>x.final));
  assert.equal(first.messages.filter(x=>x.type==='player-speaking').length,1);
  for(const key of ['private-asr-key','private-app-key','private-ak','private-sk','private-profile'])assert(!JSON.stringify(first.messages).includes(key));
  first.ws.send(JSON.stringify({type:'user.audio.speaker.verification',conv_id:'forged'}));await once(first.ws,'close');assert(first.messages.some(x=>x.code==='voiceprint_invalid_control'));assert.equal(first.provider.readyState,3);assert.equal(first.provider.sent.at(-1).type,'session.close');
  const second=await open();second.provider.event({type:'user.conversation.created',conv_id:'inactive'});second.provider.event({type:'user.audio.transcription.started',conv_id:'inactive',sv_active:false});await once(second.ws,'close');assert(second.messages.some(x=>x.code==='voiceprint_verification_inactive'));assert(!second.messages.some(x=>x.type==='transcript'));
  const third=await open();third.ws.close();await once(third.ws,'close');await waitUntil(()=>third.provider.readyState===3);
  console.log('PASS protected loopback bridge: shared upgrade routing, origin, API Key, per-turn identity, deferred barge-in, no secret leakage, forged control rejection, inactive verification and hangup cleanup');
} finally { for(const client of clients)client.terminate();bridge.close();legacy.close();server.close();await once(server,'close'); }
