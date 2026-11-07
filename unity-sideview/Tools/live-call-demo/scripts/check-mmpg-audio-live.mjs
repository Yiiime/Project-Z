// Explicit cloud smoke test. Synthetic Vivi audio only; no getUserMedia and no saved voiceprint.
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import { loadVoiceprintConfig } from '../server/voiceprint/config.mjs';
import { buildVoiceprintSession } from '../server/voiceprint/session.mjs';
import { connectMmpg } from '../server/voiceprint/volc-client.mjs';
import { issueRtcToken } from '../server/voiceprint/rtc-token.mjs';
import { VoiceprintTurnGate } from '../server/voiceprint/turn-gate.mjs';
const { chromium } = await import(process.env.Z_PLAYWRIGHT_MODULE || 'playwright');
const root = fileURLToPath(new URL('../', import.meta.url)), config = await loadVoiceprintConfig(root);
const probeAuth=config.asrSource;
const redact=value=>{let text=String(value??'');for(const secret of [config.asrApiKey,config.rtcAppId,config.rtcAppKey,config.accessKey,config.secretKey,config.asrAccessToken].filter(Boolean))text=text.split(secret).join('[redacted]');return text.replace(/((?:token|secret|api.?key|authorization)["']?\s*[:=]\s*["']?)[^\s,"'}]+/gi,'$1[redacted]').slice(0,700);};
const id = randomUUID().replaceAll('-', ''), roomId = 'z_audio_test_' + id, userId = 'p_' + id, agentId = 'z_' + id;
const report = { at: new Date().toISOString(), microphoneUsed: false, voiceprintPersisted: false, probeAuth, rtcJoined: false, events: [], acceptedTurns: [] };
const gate = new VoiceprintTurnGate({ holdMs: 300 });
let browser, socket, providerReady, rejectReady, server;
const ready = new Promise((a,b) => { providerReady=a; rejectReady=b; }); ready.catch(()=>{});
const timer = setTimeout(()=>rejectReady(Error('mmpg_timeout')),20000);
const consume = events => { for(const event of events)if(event.type==='transcript'&&event.final)report.acceptedTurns.push(event.text); };
const flush=setInterval(()=>consume(gate.flush()),25);
try {
  const sample='今天我来到月亮小岛，想和你聊一会儿。办公室里有很多声音，但是我希望你能听清我的话。我们可以慢慢聊聊今天发生的事情，也可以一起想想明天的计划。';
  const response=await fetch('http://127.0.0.1:4174/api/tts',{method:'POST',headers:{Origin:'http://127.0.0.1:4174','Content-Type':'application/json'},body:JSON.stringify({requestId:randomUUID(),text:sample,mood:'warm'})});
  if(!response.ok)throw Error('tts_failed');
  const chunks=[];for(const line of (await response.text()).trim().split('\n')){const e=JSON.parse(line);if(e.type==='audio')chunks.push(Buffer.from(e.data,'base64'));}
  if(!chunks.length)throw Error('tts_empty');const audio=Buffer.concat(chunks);
  const sdk=await fs.readFile(path.join(root,'node_modules/@volcengine/rtc/index.esm.min.js'));
  const rtc={appId:config.rtcAppId,roomId,userId,token:issueRtcToken({appId:config.rtcAppId,appKey:config.rtcAppKey,roomId,userId})};
  server=http.createServer((req,res)=>{if(req.url==='/sdk.js'){res.writeHead(200,{'Content-Type':'text/javascript'});res.end(sdk);}else if(req.url==='/audio'){res.writeHead(200,{'Content-Type':'audio/mpeg'});res.end(audio);}else if(req.url==='/rtc'){res.writeHead(200,{'Content-Type':'application/json','Cache-Control':'no-store'});res.end(JSON.stringify(rtc));}else res.end('<!doctype html><title>RTC synthetic validation</title>');});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  socket=connectMmpg(config);
  socket.on('open',()=>{const request=buildVoiceprintSession({...config,missing:[]},{roomId,userId,agentId});request.session.AudioConfig.VoicePrint={Mode:1,EnableSV:true,VoiceDuration:4,ProcessMode:2,SVMode:2};socket.send(JSON.stringify(request));});
  socket.on('error',()=>rejectReady(Error('mmpg_connection_failed')));
  socket.on('unexpected-response',(req,res)=>{res.resume();rejectReady(Error('mmpg_http_'+res.statusCode));});
  socket.on('message',bytes=>{const event=JSON.parse(bytes);
    if(event.type==='session.created'){clearTimeout(timer);providerReady();}
    if(!event.type.endsWith('.delta'))report.events.push({type:event.type,...(event.sv_active!==undefined?{sv_active:event.sv_active}:{}),...(event.status!==undefined?{status:event.status}:{}),...(event.reason?{reason:event.reason}:{}),...(event.type==='error'?{errorCode:event.code,message:redact(event.message),fields:Object.keys(event)}:{}),...(event.type==='user.audio.transcription.done'?{transcript:event.transcript}: {})});
    consume(gate.receive(event));
    if(event.type==='error'){(report.providerErrors??=[]).push(redact(JSON.stringify(event.error)));report.providerError??=redact(JSON.stringify(event.error));rejectReady(Error('mmpg_provider_error'));}
  });
  await ready;
  browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
  const page=await browser.newPage();await page.goto('http://127.0.0.1:'+server.address().port);
  const joined=await page.evaluate(async()=>{
    try {
      const {default:RTC,AudioSourceType,StreamIndex}=await import('/sdk.js');
      const c=await (await fetch('/rtc')).json();const engine=window.testEngine=RTC.createEngine(c.appId);window.testRTC=RTC;
      await engine.joinRoom(c.token,c.roomId,{userId:c.userId},{isAutoPublish:true,isAutoSubscribeAudio:false,isAutoSubscribeVideo:false});
      const context=window.testContext=new AudioContext();await context.resume();
      const audio=await context.decodeAudioData(await (await fetch('/audio')).arrayBuffer());
      const target=context.createMediaStreamDestination();
      await engine.setAudioSourceType(StreamIndex.STREAM_INDEX_MAIN,AudioSourceType.AUDIO_SOURCE_TYPE_EXTERNAL);
      await engine.setExternalAudioTrack(StreamIndex.STREAM_INDEX_MAIN,target.stream.getAudioTracks()[0]);
      const play=()=>{const s=context.createBufferSource();s.buffer=audio;s.connect(target);s.start();};
      // Dynamic test registration may complete during round two; round three
      // checks identity after it is active. Production uses a registered ID.
      play();window.secondPlayback=setTimeout(play,(audio.duration+3)*1000);
      window.thirdPlayback=setTimeout(play,(audio.duration+3)*2000);
      return {ok:true,audioSeconds:audio.duration};
    } catch(e) { return {ok:false,code:typeof e.code==='number'?e.code:'browser_setup_failed'}; }
  });
  report.rtcJoined=joined.ok;report.playback=joined;
  if(!joined.ok)throw Error('rtc_join_or_audio_failed');
  const deadline=Date.now()+Math.min(70000,(joined.audioSeconds*3+10)*1000);
  while(Date.now()<deadline&&!report.providerError&&!report.acceptedTurns.length)await new Promise(resolve=>setTimeout(resolve,250));
  report.ok=report.rtcJoined&&report.acceptedTurns.length>0;
} catch(error){report.ok=false;report.code=/^[a-z_0-9]+$/.test(error.message)?error.message:'live_test_failed';}
finally {
  clearTimeout(timer);clearInterval(flush);
  if(browser)await browser.close();
  if(socket?.readyState===1){socket.send(JSON.stringify({event_id:randomUUID(),type:'session.close'}));socket.close();setTimeout(()=>socket.terminate(),1000).unref();}else socket?.terminate();
  if(server)await new Promise(resolve=>server.close(resolve));
  await fs.mkdir(path.join(root,'outputs'),{recursive:true});await fs.writeFile(path.join(root,'outputs/mmpg-audio-validation-'+probeAuth+'.json'),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report,null,2));if(!report.ok)process.exitCode=1;
}
