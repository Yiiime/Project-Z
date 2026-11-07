// Browser decoder and lifecycle verification using synthetic Vivi audio only.
import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const {chromium}=await import(process.env.Z_PLAYWRIGHT_MODULE||'playwright');
const root=fileURLToPath(new URL('../',import.meta.url)),text='今晚我们慢慢聊，想到什么就说什么，不用着急。';
const response=await fetch('http://127.0.0.1:4174/api/tts',{method:'POST',headers:{Origin:'http://127.0.0.1:4174','Content-Type':'application/json'},body:JSON.stringify({requestId:crypto.randomUUID(),text,mood:'warm'})});
if(!response.ok)throw Error('fixture_tts_failed');const records=(await response.text()).trim().split('\n').map(JSON.parse);if(!records.some(x=>x.type==='done'))throw Error('fixture_incomplete');
const browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
try{
 const page=await browser.newPage();await page.route('**/__audio/*.js',async route=>route.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(root,'ui',path.basename(new URL(route.request().url()).pathname)))}));
 await page.route('http://127.0.0.1:4174/',route=>route.fulfill({body:'<!doctype html><title>Streaming audio checks</title>',contentType:'text/html'}));await page.goto('http://127.0.0.1:4174/');
 const report=await page.evaluate(async({records,text})=>{
  const {SpeechDirector,Mp3Stream}=await import('/__audio/speech-director.js');const check=(v,m)=>{if(!v)throw Error(m);};check(Mp3Stream.supported(),'MSE MP3 unsupported');
  let release,wireEnded=false,posts=0;const config={enabled:true,provider:'Volcengine',voice:'Vivi 2.0',speaker:'zh_female_vv_uranus_bigtts',format:'mp3',maxTextLength:500};
  const voice=new SpeechDirector({fetchImpl:async(url,init)=>{
   if(url.endsWith('/config'))return Response.json(config);posts++;const requestId=JSON.parse(init.body).requestId;
   return new Response(new ReadableStream({start(c){const encoder=new TextEncoder();for(const r of records.filter(r=>r.type!=='done'))c.enqueue(encoder.encode(JSON.stringify({...r,requestId})+'\n'));release=(type='done')=>{wireEnded=true;c.enqueue(encoder.encode(JSON.stringify({type,requestId})+'\n'));c.close();};}}),{headers:{'Content-Type':'application/x-ndjson'}});
  }});
  const until=async fn=>{const end=performance.now()+10000;while(!fn()){if(performance.now()>end)throw Error('timeout');await new Promise(r=>setTimeout(r,20));}};
  let audible=0,completed=0;voice.addEventListener('activity',e=>{if(e.detail.level>.008)audible++;});voice.addEventListener('complete',()=>completed++);
  try{
   await voice.configure();await voice.setEnabled(true);voice.warmSpeech('approved',text,'warm',{streaming:true});await voice.warm.promise;
   check(audible===0&&voice.warm.stream.audio.paused,'unapproved warm stream played');
   voice.speak({id:1,text,mood:'warm',preparedAudioKey:'approved'});await until(()=>audible>0);check(!wireEnded,'must play before done');
   check(voice.current.index<text.length,'did not reveal unheard full caption');
   const stream=voice.current.stream;voice.pause();const stoppedAt=stream.audio.currentTime;await new Promise(r=>setTimeout(r,150));check(Math.abs(stream.audio.currentTime-stoppedAt)<.03,'pause advanced');voice.resume();await until(()=>stream.audio.currentTime>stoppedAt+.1);
   release();await until(()=>completed===1);check(stream.closed,'completion did not release stream');check(posts===1,'synthesis duplicated');
   voice.warmSpeech('cancel',text,'warm',{streaming:true});await voice.warm.promise;voice.speak({id:2,text,mood:'warm',preparedAudioKey:'cancel'});await until(()=>voice.current?.stream&&!voice.current.stream.audio.paused);const cancelled=voice.current.stream;
   await voice.fadeOut(180);voice.cancel();check(cancelled.closed&&cancelled.audio.paused,'cancel did not stop');check(completed===1,'cancellation completed old turn');
   voice.warmSpeech('stale',text,'warm',{streaming:true});const stale=voice.warm.stream;voice.dropWarmSpeech();check(stale.closed,'discard retained decoder');
   voice.warmSpeech('error',text,'warm',{streaming:true});await voice.warm.promise;voice.speak({id:3,text,mood:'warm',preparedAudioKey:'error'});await until(()=>voice.current?.stream&&!voice.current.stream.audio.paused);const failed=voice.current.stream;release('error');await until(()=>voice.current.mode==='text');check(failed.closed&&failed.audio.paused,'provider error left audio playing');
   return {ok:true,playedBeforeDone:true,silentUntilApproved:true,pauseResume:true,softCancel:true,cleanup:true,oneSynthesis:true,lateProviderErrorStopsAudio:true};
  }finally{voice.destroy();}
 },{records,text});console.log(JSON.stringify(report));
}finally{await browser.close();}
