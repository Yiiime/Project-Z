// Real Vivi and Agent, controlled ASR events. Feedback and formal answer clocks are separate.
import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const {chromium}=await import(process.env.Z_PLAYWRIGHT_MODULE||'playwright');
const root=fileURLToPath(new URL('../',import.meta.url)),browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/__feedback/*.js',async r=>r.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(root,'ui',path.basename(new URL(r.request().url()).pathname)))}));
 await page.route('http://127.0.0.1:4174/',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><div class="sidebar"></div><div id="speech-box"></div><form id="chat-form"><input id="chat-input"><button id="send-message"></button></form><div id="reply-status"></div>'}));await page.goto('http://127.0.0.1:4174/');
 const rows=await page.evaluate(async()=>{
  const {CallConversationEngine}=await import('/__feedback/call-engine.js'),{SpeechDirector,CALL_THINKING_TEXT}=await import('/__feedback/speech-director.js'),{AgentClient}=await import('/__feedback/agent-client.js'),{mountCallUI}=await import('/__feedback/call-ui.js');
  const sleep=ms=>new Promise(r=>setTimeout(r,ms)),until=async fn=>{const end=performance.now()+30000;while(!fn()){if(performance.now()>end)throw Error('timeout');await sleep(20);}};
  const rows=[];let cached;
  for(const scenario of ['normal','interrupt','hangup']){
   let rec;class Recognition{constructor(){this.persistent=true;}start(){rec=this;this.onstart?.();}abort(){}stop(){this.onend?.();}}
   const engine=new CallConversationEngine(),voice=new SpeechDirector(),client=new AgentClient(),failures=[],feedback=[],answers=[];let start,feedbackSound,answerSound,overlap=false,hostRequests=0;
   const check=(v,m)=>{if(!v)failures.push(m);};const config=await client.configure();engine.configureAgent(client,{...config,singingBackground:false});engine.configureSpeech(voice);engine.setSpeechDriven(true);await voice.configure();await voice.setEnabled(true);
   if(cached)voice.thinkingAudio=cached;else cached=await voice.prepareCallThinking();check(!!cached,'Vivi cue not cached');
   const generate=client.generate.bind(client);client.generate=(p,o)=>{if(p.kind==='host')hostRequests++;return generate(p,o);};
   voice.addEventListener('call-feedback',({detail})=>{feedback.push(detail);if(detail.phase==='activity'&&detail.level>.008)feedbackSound??=performance.now();if(detail.phase==='speaking'){check(!!engine.pending&&hostRequests>0,'cue did not run alongside host request');check(engine.inbox.length===1,'cue consumed player request');}});
   engine.addEventListener('speak',({detail})=>{answers.push(detail.text);voice.speak(detail);});voice.addEventListener('text',({detail})=>engine.recordSpeechProgress(detail.id,detail.text));voice.addEventListener('complete',({detail})=>engine.completeSpeech(detail.id));voice.addEventListener('activity',({detail})=>{if(detail.level>.008){answerSound??=performance.now();if(voice.thinkingCue)overlap=true;}});
   const think=engine._think.bind(engine);engine._think=(...a)=>engine.getState().call.active?think(...a):undefined;
   const ui=mountCallUI({engine,director:voice,Recognition,toast(){},unpause(){}});engine.start();document.querySelector('#call-mic').click();engine.setCallInput('listening');engine.audience.tick=()=>{};
   const input=(text,final)=>{const r=[{transcript:text,confidence:1}];r.isFinal=final;rec.onresult({results:[r],resultIndex:0,conversationId:final?'first':'second',speakerVerified:true,turnFinal:final});};
   try{
    start=performance.now();input('我有点紧张，用一句话陪陪我吧。',true);check(document.querySelector('#call-transcript').textContent.includes(CALL_THINKING_TEXT),'caption not replaced');await until(()=>feedbackSound!==undefined);
    if(scenario==='normal'){await until(()=>engine.inbox.length===0&&!engine.pending&&!engine._active);check(answerSound>feedbackSound,'formal answer clock mixed with cue');check(!overlap,'cue overlaps answer');check(!engine.session.evidence().some(e=>e.text.includes(CALL_THINKING_TEXT)),'cue entered conversation evidence');}
    else{if(scenario==='interrupt')input('等一下，我还想补充',false);else document.querySelector('#call-mic').click();await sleep(250);check(!voice.thinkingCue,'cue survived interruption');await sleep(1200);check(answers.length===0,'old answer leaked after cancellation');}
    check(feedback.filter(e=>e.phase==='speaking').length===1,'repeated cue');check(document.querySelectorAll('.call-dialogue-row').length<=2,'extra dialogue row');
    rows.push({scenario,ok:!failures.length,failures,cueDurationMs:Math.round(cached.buffer.duration*1000),feedbackSoundMs:Math.round(feedbackSound-start),formalAnswerSoundMs:answerSound?Math.round(answerSound-start):null,answers,overlap});
   }catch(e){rows.push({scenario,ok:false,error:e.message,failures,answers});}finally{ui.destroy();engine.destroy();voice.destroy();client.cancel();}
  }
  return rows;
 });
 const report={at:new Date().toISOString(),scope:'live_Vivi_and_Agent_controlled_ASR_cached_feedback',microphoneUsed:false,rows,pageErrors:errors,ok:rows.every(r=>r.ok)&&!errors.length};await fs.writeFile(path.join(root,'outputs/thinking-feedback-browser.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));if(!report.ok)process.exitCode=1;
}finally{await browser.close();}
