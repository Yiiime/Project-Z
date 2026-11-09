import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {performance} from 'node:perf_hooks';
import {Providers,loadEnvironment} from '../server/providers.mjs';
import {reviewCorpus,corpusPayload} from '../tests/review-corpus.mjs';
if(!process.argv.includes('--live')||!process.env.Z_REVIEW_BASELINE)throw Error('Require --live and Z_REVIEW_BASELINE (unchanged baseline service directory).');
const Before=(await import(pathToFileURL(path.join(process.env.Z_REVIEW_BASELINE,'server/providers.mjs')))).Providers;
const root=fileURLToPath(new URL('../',import.meta.url)),env=loadEnvironment(process.env.Z_LIVE_CONFIG_DIR||root);
const historical=process.argv.includes('--historical');
const output=path.join(root,'verification',historical?'release-review-historical.json':'release-review-ab.json');
let cases=reviewCorpus;
if(historical){
 const original=JSON.parse(await fs.readFile(path.join(root,'verification/native-call-benchmark-20261107.json'),'utf8')).cases.find(row=>row.name==='接上文与弹幕');
 const item={expected:'allow',player:original.input,text:original.measuredSegment,segment:true,audience:'我累的时候会先伸个懒腰。',
  extra:[{id:'prior-player',type:'player.message',text:'今天连开了五个会，脑子都有点空了。',speakerRole:'player',speakerId:'player',status:'published'},
   {id:'prior-host',type:'speech.completed',text:'那我们先慢慢聊一会儿。',speakerRole:'host',speakerId:'Z',status:'completed',speechId:'prior-host'}]};
 cases=Array.from({length:3},(_,index)=>({...item,id:'historical-'+(index+1)}));
}
const report={testedAt:new Date().toISOString(),modelUnchanged:true,microphoneUsed:false,method:'Fixed labeled synthetic candidates, alternating baseline/current order, same text and evidence. One expansion allowed; no candidate rewriting in verdict comparison.',cases:[]};
for(const [index,item] of cases.entries()){
 const row={id:item.id,expected:item.expected,text:item.text};
 for(const mode of index%2?['after','before']:['before','after']){
  const metrics=[],provider=new (mode==='after'?Providers:Before)(env,{singingContext:()=>({enabled:true,tasks:item.tasks||[],knowledge:[],limits:{singleSegmentSeconds:90}}),metrics:m=>metrics.push(m)});
  const payload=corpusPayload(item);if(mode==='before')delete payload.context.candidate.segment;
  const start=performance.now();let calls=1;
  try{let reply=await provider.generate('review-ab-'+crypto.randomUUID(),payload,{compactReview:true});
   if(mode==='after'&&reply.decision==='need_context'){calls++;reply=await provider.generate('review-ab-'+crypto.randomUUID(),{...payload,requestId:crypto.randomUUID()},{compactReview:true,reviewExpanded:true});}
   row[mode]={decision:reply.decision,correct:reply.decision===item.expected,ms:Math.round(performance.now()-start),calls,reason:reply.reason,issue:reply.issue,span:reply.span,metrics};
  }catch(error){row[mode]={decision:'error',correct:false,ms:Math.round(performance.now()-start),calls,error:error.code||'request_failed',metrics};}
 }
 report.cases.push(row);await fs.mkdir(path.dirname(output),{recursive:true});await fs.writeFile(output,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({id:row.id,expected:row.expected,before:row.before.decision,after:row.after.decision,beforeMs:row.before.ms,afterMs:row.after.ms}));
}
report.summary=Object.fromEntries(['before','after'].map(mode=>[mode,{total:report.cases.length,correct:report.cases.filter(r=>r[mode].correct).length,falseReject:report.cases.filter(r=>r.expected==='allow'&&r[mode].decision==='drop').length,falseAllow:report.cases.filter(r=>r.expected==='drop'&&r[mode].decision==='allow').length,errors:report.cases.filter(r=>!['allow','drop'].includes(r[mode].decision)).length,meanMs:Math.round(report.cases.reduce((n,r)=>n+r[mode].ms,0)/report.cases.length)}]));
await fs.writeFile(output,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report.summary,null,2));
