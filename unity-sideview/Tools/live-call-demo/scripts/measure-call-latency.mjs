// Real Agent + review + Vivi playback. Input starts at finalized transcript,
// not microphone speech end. No player recording is made.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const {chromium}=await import(process.env.Z_PLAYWRIGHT_MODULE||'playwright');
const root=fileURLToPath(new URL('../',import.meta.url));
const source=process.env.Z_BENCH_UI||path.join(root,'ui'),label=process.env.Z_BENCH_LABEL||'after';
const browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
try{
 const page=await browser.newPage();
 await page.route('**/__latency/*.js',async route=>{const name=path.basename(new URL(route.request().url()).pathname);await route.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(source,name))});});
 await page.route('http://127.0.0.1:4174/',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><title>Call timing</title>'}));
 await page.goto('http://127.0.0.1:4174/');
 const rows=await page.evaluate(async()=>{
  const {CallConversationEngine}=await import('/__latency/call-engine.js');
  const {SpeechDirector}=await import('/__latency/speech-director.js');
  const {AgentClient}=await import('/__latency/agent-client.js');
  const queries=['今天开了一天会，好累啊。','我明天要做汇报，有点紧张。','今晚终于能早点下班了，想放松一下。'];
  const rows=[];
  for(const text of queries){
   const engine=new CallConversationEngine(),voice=new SpeechDirector(),timings=[];let start,firstPlay,firstSound,reply='',endTimer;
   const client=new AgentClient({onTiming:t=>timings.push(t)});
   const config=await client.configure();engine.configureAgent(client,{...config,singingBackground:false});engine.configureSpeech(voice);engine.setSpeechDriven(true);
   await voice.configure();await voice.setEnabled(true);
   const done=new Promise(resolve=>{endTimer=setTimeout(()=>resolve('timeout'),45000);});let resolveSound;
   const heard=new Promise(resolve=>resolveSound=resolve);
   engine.addEventListener('speak',({detail})=>{reply=detail.text;voice.speak(detail);});
   voice.addEventListener('text',({detail})=>engine.recordSpeechProgress(detail.id,detail.text));
   voice.addEventListener('complete',({detail})=>engine.completeSpeech(detail.id));
   voice.addEventListener('performance',({detail})=>{if(detail.phase==='speaking'&&detail.mode==='audio')firstPlay??=performance.now();});
   voice.addEventListener('activity',({detail})=>{if(detail.level>.008&&start!==undefined){firstSound??=performance.now();resolveSound('audio');}});
   engine.start();engine.joinCall();engine.audience.tick=()=>{}; // Isolate same player workload in both runs.
   start=performance.now();engine.sendMessage(text);
   const outcome=await Promise.race([heard,done]);clearTimeout(endTimer);
   rows.push({text,reply,replyChars:reply.length,outcome,finalTranscriptToPlaybackMs:firstPlay?Math.round(firstPlay-start):null,finalTranscriptToSoundMs:firstSound?Math.round(firstSound-start):null,timings:timings.map(({kind,durationMs,status})=>({kind,durationMs,status}))});
   engine.destroy();voice.destroy();client.cancel();
  }
  return rows;
 });
 const report={at:new Date().toISOString(),label,scope:'final_transcript_to_first_non_silent_browser_audio',microphoneUsed:false,rows};
 await fs.mkdir(path.join(root,'outputs'),{recursive:true});await fs.writeFile(path.join(root,'outputs/call-latency-'+label+'.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
 if(rows.some(r=>r.outcome!=='audio'))process.exitCode=1;
}finally{await browser.close();}
