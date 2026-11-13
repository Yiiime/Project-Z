// Explicit paid-provider smoke test. Start the relocated app with --serve first.
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {execFileSync, spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {hostEnvironment} from './config.mjs';
const [hostArg, dataArg] = process.argv.slice(2);
if (!hostArg || !dataArg) throw Error('Usage: verify-live.mjs <ProjectZHost> <isolated-data-directory>');
const host = path.resolve(hostArg), runtime = path.resolve(dataArg);
const manifest = JSON.parse(fs.readFileSync(path.join(host, 'manifest.json')));
const state = JSON.parse(fs.readFileSync(path.join(runtime, 'latest-run.json')));
const config = JSON.parse(fs.readFileSync(path.join(host, 'Config/providers.json')));
const tools = path.join(runtime, 'releases', manifest.packageId, 'Tools');
const env = hostEnvironment(config, {host, runtime, session:state.session, tools, memoryPort:new URL(state.memoryUrl).port, livePort:new URL(state.liveUrl).port});
Object.assign(process.env, env);
const report = {sourceRevision:manifest.sourceRevision, relocated:true, microphoneUsed:false};
async function check(name, action) {
  try { report[name] = {ok:true, ...await action()}; }
  catch (error) { report[name] = {ok:false, code:/^[a-z_]{1,80}$/.test(error.code || error.message) ? error.code || error.message : 'check_failed'}; }
  console.log(JSON.stringify({check:name, ...report[name]}));
}
const importTool = rel => import(pathToFileURL(path.join(tools, rel)));
await check('model', async () => {
  const r=await fetch(state.memoryUrl+'/api/config/test', {method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:'{}',signal:AbortSignal.timeout(15000)});
  const b=await r.json(); if(!r.ok || !b.configured || b.team?.enabled) throw Error('model_request_failed');
  return {provider:b.provider, realResponse:true, teamLoginRequired:false};
});
let zAudio;
await check('npcSpeech', async () => {
  const {SpeechService,loadSpeechConfig}=await importTool('MemoryLab/lib/speech.mjs');
  const service=new SpeechService(), actors=Object.keys(loadSpeechConfig().voices), results=[];
  for(const actorId of actors) {
    const audio=await service.synthesize({worldId:'portable-qa',runId:randomUUID(),proposalId:randomUUID(),actorId,text:'你好，欢迎来到月亮岛。'});
    if(!audio.base64)throw Error('tts_empty');
    if(actorId==='z')zAudio=Buffer.from(audio.base64,'base64');
    results.push(actorId);
  }
  return {actors:results, generatedAudio:true};
});
await check('recognitionAndDecoder', async () => {
  if(!zAudio)throw Error('tts_audio_missing');
  const audio=execFileSync(env.FFMPEG_PATH,['-v','error','-f','s16le','-ar','24000','-ac','1','-i','pipe:0','-ar','16000','-ac','1','-f','s16le','pipe:1'],{input:zAudio,maxBuffer:1024*1024});
  const {transcribePcm}=await importTool('live-service/server/voice/index.mjs');
  const result=await transcribePcm(audio,{apiKey:config.VOLC_ASR_API_KEY,uid:randomUUID(),signal:AbortSignal.timeout(30000)});
  if(!result.text)throw Error('asr_empty');
  return {recognized:true, decoder:'bundled-ffmpeg', syntheticAudio:true};
});
await check('nativeVoice', async () => {
  const result=await new Promise((resolve,reject)=>{
    const child=spawn(path.join(host,'Runtime/node'),[path.join(tools,'live-service/scripts/check-native-voice.mjs'),'--live'],{env,stdio:['ignore','pipe','pipe']});
    let out=''; child.stdout.on('data',b=>out+=b); child.stderr.resume();
    child.once('error',reject); child.once('exit',()=>{try{resolve(JSON.parse(out));}catch{reject(Error('voice_check_failed'));}});
  });
  if(!result.rtc?.ok||!result.mmpg?.ok)throw Error('voice_provider_failed');
  return {rtc:true,mmpg:true,stage:result.mmpg.stage};
});
await check('music', async () => {
  const {providerCall}=await importTool('live-service/server/song-library/netease.mjs');
  const status=await providerCall('auth_status',{});if(!status.authenticated)throw Error('music_session_invalid');
  const search=await providerCall('cloudsearch',{keywords:'颜人中 晚安',limit:5});
  if(!search.result?.songs?.length)throw Error('music_search_failed');
  return {authenticated:true,searchWorks:true};
});
await check('liveSession', async () => {
  const connection=JSON.parse(fs.readFileSync(path.join(state.session,'connection.json')));
  const r=await fetch(state.liveUrl+'/v1/live/sessions',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+connection.token,'Idempotency-Key':randomUUID()},body:JSON.stringify({protocolVersion:1,mode:'private',capabilities:{audioEnabled:false}}),signal:AbortSignal.timeout(20000)});
  if(r.status!==201)throw Error('live_session_failed');
  const s=await r.json();if(!s.sessionId)throw Error('live_snapshot_invalid');
  return {created:true};
});
fs.writeFileSync(path.join(runtime,'verification.json'),JSON.stringify(report,null,2));
if(Object.values(report).some(r=>r?.ok===false))process.exitCode=1;
