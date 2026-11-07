// Isolated synthetic multi-turn replay: live Agent/review, no microphone or TTS.
import fs from 'node:fs/promises';
import {CallConversationEngine} from '../ui/call-engine.js';
import {AgentClient} from '../ui/agent-client.js';
const origin='http://127.0.0.1:4174',timings=[],responses=[],rows=[];
const client=new AgentClient({fetchImpl:async(url,init={})=>{const r=await fetch(origin+url,{...init,headers:{...init.headers,Origin:origin}});if(!r.ok){let error;try{error=(await r.clone().json()).error;}catch{}responses.push({status:r.status,error});}return r;},onTiming:t=>timings.push(t)});
const config=await client.configure();if(!config.enabled)throw Error('agent_not_configured');
const e=new CallConversationEngine();e.configureAgent(client,{...config,singingBackground:false});e.start();clearInterval(e._timer);e.setSpeechDriven(true);e.joinCall();e.audience.tick=()=>{};
e.addEventListener('speak',({detail})=>setTimeout(()=>{if(e._active?.id===detail.id){e.recordSpeechProgress(detail.id,detail.text);e.completeSpeech(detail.id);}},30));
try{
 for(const messages of [['哈喽哈喽！'],['标到花店奶奶家。'],['周伯伯吗？'],['你喜欢男生还是女生？'],['你喜欢花店晴野吗？'],['你不认识晴野吗？','那你认识船长吗？']]){
  for(const m of messages)e.sendMessage(m);
  const target=e.inbox.at(-1).id,attempts=[];
  for(let attempt=0;attempt<3&&e.inbox.some(m=>m.id===target);attempt++){
   e.nextWake=e._clock;const start=Date.now();await e._think();const trace=e.exportDialogueTrace().records;
   attempts.push({ms:Date.now()-start,source:e.getState().agent.source,code:e.getState().agent.errorCode,pending:e.inbox.length,recent:trace.filter(r=>['host.request.failed','review.result','review.unavailable'].includes(r.stage)).slice(-4).map(r=>({stage:r.stage,code:r.code,accepted:r.accepted,decision:r.reply?.decision,reason:r.reply?.reason}))});
  }
  const row={input:messages.join(' / '),answered:!e.inbox.some(m=>m.id===target),attempts};rows.push(row);console.log(JSON.stringify(row));
 }
}finally{
 e.destroy();client.cancel();const report={at:new Date().toISOString(),scope:'synthetic_multiturn_API_reproduction_not_original_browser_trace',audioSimulated:true,rows,responses,timings,ok:rows.length===6&&rows.every(r=>r.answered)};
 await fs.writeFile(new URL('../outputs/agent-connection-after.json',import.meta.url),JSON.stringify(report,null,2)+'\n');if(!report.ok)process.exitCode=1;
}
