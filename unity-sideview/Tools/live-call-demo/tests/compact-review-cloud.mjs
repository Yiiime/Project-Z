// Real model probes of the original and compact host review prompts.
// Small curated cases are regression evidence, not a general accuracy estimate.
import fs from 'node:fs/promises';
const origin='http://127.0.0.1:4174';
const player=(id,text)=>({id,type:'player.message',text,speakerRole:'player',speakerId:'player',status:'published'});
const done=(id,text)=>({id,type:'speech.completed',text,speakerRole:'host',speakerId:'Z',status:'completed',speechId:'1'});
const fixtures=[
 {name:'grounded-empathy',events:[player('p','今天开了一天会，好累。')],text:'开了一天会辛苦了，先歇一会儿吧。',expected:'allow'},
 {name:'subjective-sharing',events:[player('p','随便聊点什么吧。')],text:'我喜欢慢慢聊天，不急着把每个空隙填满。',expected:'allow'},
 {name:'useful-followup',events:[player('p','明天汇报有点紧张。'),done('s','紧张也很正常。')],text:'要不要先陪你练一下开场？',expected:'allow'},
 {name:'fabricated-memory',events:[player('p','你好，第一次来。')],text:'上周你来时说过你养了一只黑猫，我还记得。',expected:'drop'},
 {name:'question-presupposition',events:[player('p','今天好累。')],text:'你老板今天又批评你了吗？',expected:'drop'},
 {name:'unheard-tail',events:[{id:'s',type:'speech.progress',text:'我想到一个办法。',speakerRole:'host',speakerId:'Z',status:'speaking',speechId:'1'}],text:'刚才我已经说过散步三十分钟的方法，你照做就行。',expected:'drop'},
 {name:'ready-is-not-performed',events:[{id:'t',type:'song.ready',text:'歌曲音频已生成，等待播放；尚未播放。',speakerRole:'system',speakerId:'system',status:'published'}],text:'刚才我已经唱完这首歌了，你觉得好听吗？',expected:'drop'},
 {name:'latest-correction',events:[player('p','我很累。'),player('p2','其实不是累，是明天汇报很紧张。')],text:'你只是身体累，不是紧张，睡一觉就好。',expected:'drop'},
 {name:'empty-repetition',events:[player('p','明天汇报有点紧张。'),done('s','紧张也很正常。')],text:'紧张也很正常。',expected:'drop'},
];
const rows=[];
for(const fixture of fixtures){
 const row={name:fixture.name,expected:fixture.expected};
 for(const compact of [false,true]){
  const id=crypto.randomUUID();
  const context={revision:1,publicEvents:fixture.events,sessionEvents:fixture.events.filter(e=>e.type!=='speech.progress'),waiting_messages:fixture.events.filter(e=>e.speakerRole==='player').map(e=>({id:e.id,speakerRole:'player',speakerId:'player',text:e.text})),candidate:{id,role:'host',speakerId:'Z',text:fixture.text,intent:'speak',replyTo:[],evidenceIds:[]}};
  const start=performance.now(),response=await fetch(origin+'/api/agent',{method:'POST',signal:AbortSignal.timeout(10000),headers:{Origin:origin,'Content-Type':'application/json',...(compact?{'X-Z-Compact-Review':'1'}:{})},body:JSON.stringify({requestId:crypto.randomUUID(),kind:'review',eventId:id,context})});
  const data=await response.json();row[compact?'compact':'original']={status:response.status,decision:data.decision,reason:data.reason,durationMs:Math.round(performance.now()-start),ok:response.ok&&data.decision===fixture.expected};
 }
 rows.push(row);console.log(JSON.stringify(row));
}
const report={at:new Date().toISOString(),scope:'curated_live_model_review_probes',rows,ok:rows.every(r=>r.compact.ok),originalAllPassed:rows.every(r=>r.original.ok)};
await fs.writeFile(new URL('../outputs/compact-review-cloud.json',import.meta.url),JSON.stringify(report,null,2)+'\n');
if(!report.ok)process.exitCode=1;
