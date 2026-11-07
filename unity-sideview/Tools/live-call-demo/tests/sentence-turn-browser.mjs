// Controlled ASR timing through the real call UI, real Agent/review and Vivi.
// This measures orchestration, not acoustic recognition quality.
import fs from 'node:fs/promises';import path from 'node:path';import {fileURLToPath} from 'node:url';
const {chromium}=await import(process.env.Z_PLAYWRIGHT_MODULE||'playwright');
const root=fileURLToPath(new URL('../',import.meta.url));
const browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
try{
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/__turn/*.js',async route=>route.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(root,'ui',path.basename(new URL(route.request().url()).pathname)))}));
 await page.route('http://127.0.0.1:4174/',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><div class="sidebar"></div><div id="speech-box"></div><form id="chat-form"><input id="chat-input" maxlength="120"><button id="send-message"></button></form><div id="reply-status"></div>'}));await page.goto('http://127.0.0.1:4174/');
 const rows=await page.evaluate(async()=>{
  const {CallConversationEngine}=await import('/__turn/call-engine.js'),{SpeechDirector}=await import('/__turn/speech-director.js'),{AgentClient}=await import('/__turn/agent-client.js'),{mountCallUI}=await import('/__turn/call-ui.js');
  const sleep=ms=>new Promise(r=>setTimeout(r,ms)),until=async(fn,ms=30000)=>{const end=performance.now()+ms;while(!fn()){if(performance.now()>end)throw Error('timeout');await sleep(25);}};
  const rows=[];
  for(const scenario of ['short','long-pause','revision','supplement','barge-in','hangup','two-sentences']){
   let recognition;class Recognition{constructor(){this.persistent=true;}start(){recognition=this;this.onstart?.();}abort(){this.closed=true;}stop(){this.abort();this.onend?.();}}
   const engine=new CallConversationEngine(),voice=new SpeechDirector(),client=new AgentClient(),submitted=[],heard=[],completed=[],proposals=[],calls=[],failures=[];let firstSound=null,finalAt=null;
   const check=(v,m)=>{if(!v)failures.push(m);};const config=await client.configure();engine.configureAgent(client,{...config,singingBackground:false});engine.configureSpeech(voice);engine.setSpeechDriven(true);await voice.configure();await voice.setEnabled(true);
   const original=client.generate.bind(client);client.generate=async(p,o)=>{const result=await original(p,o);if(p.kind==='host')proposals.push(result);return result;};
   engine.addEventListener('message',({detail})=>{if(detail.mine)submitted.push(detail.text);});engine.addEventListener('speak',({detail})=>{heard.push({id:detail.id,text:detail.text,at:performance.now()});voice.speak(detail);});
   voice.addEventListener('text',({detail})=>engine.recordSpeechProgress(detail.id,detail.text));voice.addEventListener('complete',({detail})=>{completed.push(detail.id);engine.completeSpeech(detail.id);});
   voice.addEventListener('activity',({detail})=>{if(detail.level>.008){firstSound??=performance.now();calls.push({id:detail.id,at:performance.now()});}});
   let toolCalls=0;engine._executeTool=()=>{toolCalls++;};
   // Scope this fixture to call turns; normal autonomous hosting resumes after hangup in the product.
   const think=engine._think.bind(engine);engine._think=(...args)=>engine.getState().call.active?think(...args):undefined;
   const ui=mountCallUI({engine,director:voice,Recognition,toast(){},unpause(){}});engine.start();document.querySelector('#call-mic').click();engine.setCallInput('listening');engine.audience.tick=()=>{};
   const input=(text,conv,final=false)=>{if(recognition.closed)throw Error('recognizer_closed');const row=[{transcript:text,confidence:1}];row.isFinal=final;recognition.onresult({results:[row],resultIndex:0,conversationId:conv,speakerVerified:true,turnFinal:final});if(final)finalAt=performance.now();};
   try{
    if(scenario==='short')input('我想聊聊天。','a',true);
    if(scenario==='long-pause'){input('我今天想聊的事情是','a');await sleep(1800);check(heard.length===0&&submitted.length===0,'spoke during unfinished pause');input('我今天想聊的事情是明天的汇报。','a',true);}
    if(scenario==='revision'){input('我想听歌','a');await sleep(300);input('我想聊聊工作，不唱歌。','a',true);}
    if(scenario==='supplement'){input('今天好累。','a',true);await sleep(300);input('不过','b');await sleep(300);input('不过其实是有点紧张，不是身体累。','b',true);}
    if(scenario==='barge-in'){
      input('我有点紧张，请先安慰我一句，再给一句建议。','a',true);await until(()=>firstSound!==null);const oldId=engine._active.id;
      input('等一下','b');const interruptAt=performance.now();await sleep(500);check(!calls.some(c=>c.id===oldId&&c.at>interruptAt+350),'old voice survived protected interruption');
      input('等一下，不用建议，陪我安静待会儿。','b',true);check(engine.inbox.some(m=>m.text.includes('不用建议')),'correction missing from inbox');
    }
    if(scenario==='hangup'){input('我今天有点紧张，想找你聊聊。','a',true);await sleep(200);document.querySelector('#call-mic').click();await sleep(3500);check(heard.length===0,'late reply after hangup');}
    if(scenario==='two-sentences')input('我明天要汇报，请用两个完整短句，先鼓励我，再给一个简单建议。','a',true);
    if(scenario!=='hangup')await until(()=>engine.inbox.length===0&&!engine.pending&&!engine._active,40000);
    check(toolCalls===0,'unrequested tool');check(document.querySelectorAll('.call-dialogue-row').length<=2,'panel exceeded two rows');
    check(heard.filter(s=>completed.includes(s.id)).every(s=>calls.some(c=>c.id===s.id)),'completed sentence had no audible audio');
    if(scenario==='revision')check(submitted.join('').replaceAll(',','，')==='我想聊聊工作，不唱歌。','draft incorrectly committed');
    if(scenario==='supplement')check(submitted.length===2&&submitted[1].includes('不是身体累'),'supplement lost');
    if(scenario==='two-sentences'){const plan=proposals.at(-1);check(heard.filter(s=>completed.includes(s.id)).map(s=>s.text).join('')===plan?.speech,'generated tail omitted or repeated');}
    rows.push({scenario,ok:!failures.length,failures,submitted,heard:heard.map(s=>({text:s.text,completed:completed.includes(s.id)})),proposals:proposals.map(p=>p.speech),firstSoundMs:finalAt&&firstSound&&firstSound>=finalAt?Math.round(firstSound-finalAt):null,trace:engine.exportDialogueTrace().records.filter(e=>/sentence|review.result|host.request.failed/.test(e.stage))});
   }catch(e){rows.push({scenario,ok:false,error:e.message,submitted,heard,trace:engine.exportDialogueTrace().records.filter(e=>/sentence|review.result|host.request.failed/.test(e.stage))});}
   finally{ui.destroy();engine.destroy();voice.destroy();client.cancel();}
  }
  return rows;
 });
 const report={at:new Date().toISOString(),scope:'controlled_ASR_events_actual_call_UI_and_live_models',microphoneUsed:false,acousticWERMeasured:false,rows,pageErrors:errors,ok:rows.every(r=>r.ok)&&!errors.length};
 await fs.writeFile(path.join(root,'outputs/sentence-turn-browser.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({ok:report.ok,rows:rows.map(({scenario,ok,failures,error,submitted,heard})=>({scenario,ok,failures,error,submitted,heard})),pageErrors:errors},null,2));if(!report.ok)process.exitCode=1;
}finally{await browser.close();}
