// Synthetic RTC input -> authenticated MMPG/SV -> real Agent/review -> Vivi.
// Dynamic test-only voiceprint; never registers a persistent player profile.
import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
import {loadVoiceprintConfig} from '../server/voiceprint/config.mjs';
import {buildVoiceprintSession} from '../server/voiceprint/session.mjs';
import {connectMmpg} from '../server/voiceprint/volc-client.mjs';
import {issueRtcToken} from '../server/voiceprint/rtc-token.mjs';
import {VoiceprintTurnGate} from '../server/voiceprint/turn-gate.mjs';
const {chromium}=await import(process.env.Z_PLAYWRIGHT_MODULE||'playwright');
const root=fileURLToPath(new URL('../',import.meta.url)),config=await loadVoiceprintConfig(root),label=process.env.Z_BENCH_LABEL||'after',source=process.env.Z_BENCH_UI||path.join(root,'ui');
const queries=['今天开了一天会，好累啊。','我明天要做汇报，有点紧张。','今晚终于能早点下班了，想放松一下。'];
const indices=process.env.Z_BENCH_QUERY_INDEX===undefined?[0,1,2]:[Number(process.env.Z_BENCH_QUERY_INDEX)];
if(indices.some(i=>!Number.isInteger(i)||i<0||i>=queries.length))throw Error('invalid_query_index');
const training='今天我来到月亮小岛，想和你聊一会儿。办公室里有很多声音，但是我希望你能听清我的话。我们可以慢慢聊聊今天发生的事情，也可以一起想想明天的计划。';
const report={at:new Date().toISOString(),label,scope:'synthetic_RTC_speech_end_to_first_non_silent_browser_audio',microphoneUsed:false,persistentVoiceprintCreated:false,rows:[]};
let browser,socket,registered=false,created=false,page,active=false,providerFailed=false;
const gate=new VoiceprintTurnGate({holdMs:300});
const consume=events=>{for(const e of events)if(active&&e.type==='transcript'){void page.evaluate(e=>window.acceptTranscript(e),e).catch(()=>{providerFailed=true;});}};
const flush=setInterval(()=>consume(gate.flush()),25);
const until=async(predicate,ms)=>{const end=Date.now()+ms;while(!await predicate()){if(providerFailed)throw Error('provider_failed');if(Date.now()>end)throw Error('timeout');await new Promise(r=>setTimeout(r,100));}};
try{
 let clips;
 const cached=path.join(root,'outputs/latency-synthetic-clips.json');
 try{clips=JSON.parse(await fs.readFile(cached,'utf8'));if(clips.texts.join('\n')!==[training,...queries].join('\n'))throw Error('different_samples');}
 catch{
  clips={texts:[training,...queries],audio:[]};for(const text of clips.texts){const response=await fetch('http://127.0.0.1:4174/api/tts',{method:'POST',headers:{Origin:'http://127.0.0.1:4174','Content-Type':'application/json'},body:JSON.stringify({requestId:crypto.randomUUID(),text,mood:'warm'})});if(!response.ok)throw Error('sample_tts_failed');const records=(await response.text()).trim().split('\n').map(JSON.parse);if(!records.some(r=>r.type==='done'))throw Error('sample_tts_incomplete');clips.audio.push(Buffer.concat(records.filter(r=>r.type==='audio').map(r=>Buffer.from(r.data,'base64'))).toString('base64'));}await fs.mkdir(path.join(root,'outputs'),{recursive:true});await fs.writeFile(cached,JSON.stringify(clips));
 }
 const id=crypto.randomUUID().replaceAll('-',''),roomId='z_bench_'+id,userId='p_'+id,agentId='z_'+id;
 socket=connectMmpg(config);socket.on('error',()=>{providerFailed=true;});socket.on('open',()=>{const req=buildVoiceprintSession({...config,missing:[]},{roomId,userId,agentId});req.session.AudioConfig.VoicePrint={Mode:1,EnableSV:true,VoiceDuration:4,ProcessMode:2,SVMode:2};socket.send(JSON.stringify(req));});
 socket.on('message',b=>{try{const e=JSON.parse(b);if(e.type==='session.created')created=true;if(e.type==='error')providerFailed=true;if(e.type==='user.audio.speaker.voiceprint'&&e.status===2)registered=true;consume(gate.receive(e));}catch{providerFailed=true;}});await until(()=>created,20000);
 browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--autoplay-policy=no-user-gesture-required']});page=await browser.newPage();
 await page.route('**/__latency/*.js',async route=>route.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(source,path.basename(new URL(route.request().url()).pathname)))}));
 await page.route('http://127.0.0.1:4174/',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><title>RTC latency test</title>'}));await page.goto('http://127.0.0.1:4174/');
 await page.evaluate(async({rtc,clips})=>{
  const {default:RTC,AudioSourceType,StreamIndex}=await import('/api/voiceprint/rtc.js');
  const rtcEngine=RTC.createEngine(rtc.appId);await rtcEngine.joinRoom(rtc.token,rtc.roomId,{userId:rtc.userId},{isAutoPublish:true,isAutoSubscribeAudio:false,isAutoSubscribeVideo:false});
  const context=new AudioContext();await context.resume();const target=context.createMediaStreamDestination();
  await rtcEngine.setAudioSourceType(StreamIndex.STREAM_INDEX_MAIN,AudioSourceType.AUDIO_SOURCE_TYPE_EXTERNAL);await rtcEngine.setExternalAudioTrack(StreamIndex.STREAM_INDEX_MAIN,target.stream.getAudioTracks()[0]);
  const audio=await Promise.all(clips.map(async b64=>context.decodeAudioData(Uint8Array.from(atob(b64),c=>c.charCodeAt(0)).buffer)));
  window.playInput=index=>new Promise(resolve=>{const s=context.createBufferSource();s.buffer=audio[index];s.connect(target);s.onended=()=>{window.inputEndedAt=performance.now();resolve();};s.start();});
  const {CallConversationEngine}=await import('/__latency/call-engine.js');const {SpeechDirector}=await import('/__latency/speech-director.js');const {AgentClient}=await import('/__latency/agent-client.js');
  window.startTrial=async()=>{
   window.lastResult=null;window.inputEndedAt=null;const engine=new CallConversationEngine(),voice=new SpeechDirector(),timings=[];let transcript='',finalAt,reply,streaming=false;
   const client=new AgentClient({onTiming:t=>timings.push({kind:t.kind,durationMs:t.durationMs,status:t.status})});const conf=await client.configure();engine.configureAgent(client,{...conf,singingBackground:false});engine.configureSpeech(voice);engine.setSpeechDriven(true);await voice.configure();await voice.setEnabled(true);
   engine.addEventListener('speak',({detail})=>{reply=detail.text;voice.speak(detail);});voice.addEventListener('text',({detail})=>engine.recordSpeechProgress(detail.id,detail.text));voice.addEventListener('complete',({detail})=>engine.completeSpeech(detail.id));voice.addEventListener('performance',({detail})=>{if(detail.phase==='speaking')streaming=detail.streaming===true;});
   voice.addEventListener('activity',({detail})=>{if(detail.level>.008&&finalAt&&window.inputEndedAt!==null&&!window.lastResult&&engine.getState().call.input==='idle'){const at=performance.now();window.lastResult={transcript,reply,streaming,timings,asrTailMs:Math.round(finalAt-window.inputEndedAt),finalTranscriptToSoundMs:Math.round(at-finalAt),speechEndToSoundMs:Math.round(at-window.inputEndedAt)};}});
   window.acceptTranscript=event=>{if(window.lastResult)return;if(event.final){transcript+=event.text;finalAt=performance.now();engine.sendMessage(event.text);}else if(engine._active)void engine.yieldCallFloor();else engine.setCallInput('listening');};window.finishTrial=()=>{engine.destroy();voice.destroy();client.cancel();};engine.start();engine.joinCall();engine.setCallInput('listening');engine.audience.tick=()=>{};
  };
 },{rtc:{appId:config.rtcAppId,roomId,userId,token:issueRtcToken({appId:config.rtcAppId,appKey:config.rtcAppKey,roomId,userId})},clips:clips.audio});
 // Finish both training passages before exposing any transcript to the Agent.
 await page.evaluate(()=>window.playInput(0));await new Promise(r=>setTimeout(r,2000));await page.evaluate(()=>window.playInput(0));await until(()=>registered,15000);await new Promise(r=>setTimeout(r,2500));
 for(const i of indices){
  gate.reset();await page.evaluate(()=>window.startTrial());active=true;await page.evaluate(i=>window.playInput(i+1),i);
  await until(()=>page.evaluate(()=>!!window.lastResult),45000);active=false;const result=await page.evaluate(()=>window.lastResult);report.rows.push({input:queries[i],...result});await page.evaluate(()=>window.finishTrial());await new Promise(r=>setTimeout(r,1500));
 }
 const normalize=s=>s.replace(/[^\p{L}\p{N}]/gu,'');
 report.ok=report.rows.length===indices.length&&report.rows.every(r=>r.speechEndToSoundMs>0&&r.asrTailMs>=0&&normalize(r.transcript)===normalize(r.input));
}catch(error){report.ok=false;report.code=/^[a-z_]+$/.test(error.message)?error.message:'measurement_failed';}
finally{clearInterval(flush);if(browser)await browser.close();if(socket?.readyState===1){socket.send(JSON.stringify({event_id:crypto.randomUUID(),type:'session.close'}));socket.close();setTimeout(()=>socket.terminate(),1000).unref();}else socket?.terminate();await fs.mkdir(path.join(root,'outputs'),{recursive:true});await fs.writeFile(path.join(root,'outputs/call-e2e-'+label+'.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));if(!report.ok)process.exitCode=1;}
