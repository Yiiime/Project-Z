// Explicit real-provider benchmark. Six independent synthetic conversations;
// at most one host, one first-segment review (plus one optional context expansion) and one Vivi request per case.
// Match production first-segment latency: TTS warms concurrently with review.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {performance} from 'node:perf_hooks';
import {Providers,loadEnvironment} from '../server/providers.mjs';
import {PcmSpeech} from '../server/pcm-speech.mjs';
import {AGENT_BUNDLE} from '../server/shared/worker/agent-bundle-data.js';

if(!process.argv.includes('--live'))throw Error('Add --live to run up to 24 billable requests.');
const service=fileURLToPath(new URL('../',import.meta.url));
const env=loadEnvironment(process.env.Z_LIVE_CONFIG_DIR||service);
const outputArg=process.argv.find(arg=>arg.startsWith('--output='));
const output=outputArg?path.resolve(outputArg.slice(9)):path.join(service,'verification/native-call-review-optimized.json');
const cases=[
 {name:'日常招呼',text:'晚上好，Z，今天想找你随便聊聊天。'},
 {name:'工作疲惫',text:'今天连开了五个会，有点累，想来这里歇一会儿。'},
 {name:'具体建议',text:'我有点担心明天的汇报讲不好，你能给我一个简单建议吗？'},
 {name:'聊音乐',text:'我最近喜欢听轻松一点的歌，你喜欢什么样的音乐？'},
 {name:'轻剧情',text:'我们玩一个很轻的小故事吧，你先开个头，我来接一句。'},
 {name:'接上文与弹幕',text:'对，刚才说的那几个会让我有点累。弹幕有什么放松的小建议吗？',history:true,audience:true},
];
const safe=error=>/^[a-z_]+$/.test(error?.code||error?.message)?error.code||error.message:'request_failed';
const stats=values=>values.length?{count:values.length,meanMs:Math.round(values.reduce((a,b)=>a+b,0)/values.length),medianMs:(()=>{const s=[...values].sort((a,b)=>a-b),m=Math.floor(s.length/2);return Math.round(s.length%2?s[m]:(s[m-1]+s[m])/2);})(),minMs:Math.min(...values),maxMs:Math.max(...values)}:{count:0};
const report={testedAt:new Date().toISOString(),model:AGENT_BUNDLE.policy.host.model,voice:'Vivi 2.0',microphoneUsed:false,synthetic:true,
 method:'Sequential independent contexts; no automatic retries. First TTS segment warms concurrently with its semantic review. Ready means review allowed AND at least 100 ms of server PCM available. No real playback, RTC/ASR, simulated audience generation load, song generation, or waiting filler included.',cases:[]};

for(const item of cases){
 const id=crypto.randomUUID(),sessionId=crypto.randomUUID(),identity='latency-batch-'+sessionId;
 const event={id,type:'player.message',text:item.text,speakerRole:'player',speakerId:'player',status:'published'};
 const history=item.history?[
  {id:'prior-player',type:'player.message',text:'今天连开了五个会，脑子都有点空了。',speakerRole:'player',speakerId:'player',status:'published'},
  {id:'prior-host',type:'speech.completed',text:'那我们先慢慢聊一会儿。',speakerRole:'host',speakerId:'Z',status:'completed',speechId:'prior-host'},
 ]:[];
 const audience=item.audience?[{id:'audience-1',type:'npc.message',text:'我累的时候会先伸个懒腰。',speakerRole:'audience',speakerId:'salt',status:'published'}]:[];
 const publicEvents=[...history,...audience,event];
 const context={phase:'chat',publicEvents,experience:'autonomous',call:{active:true,id:sessionId,input:'idle',turns:item.history?3:1,audienceOpen:!!item.audience,story:null,audienceCandidates:item.audience?[{id:'audience-1',npcId:'salt',name:'海盐汽水',text:audience[0].text}]:[],audienceReason:item.audience?'invited':'none'},
  host:{inbox:[{id,name:'连麦来客',mine:true,text:item.text}],memory:'',recentExpressions:[],turn:{mode:'live'},session:{sessionId,evidence:publicEvents,unfinishedItems:[],recentInitiatives:[],crossSessionMemory:{available:false,entries:[]}},
   sessionControl:{phase:'live',version:1,closeReason:null,extensionUsed:0,extensionActive:false,extensionScope:'',remainingMs:null,elapsedMs:60000,timing:{targetMs:480000,hardMs:600000,targetRemainingMs:420000,hardRemainingMs:540000,beat:'develop',beatLabel:'互动',directive:'承接来客当前的话题。',nextBeatInMs:60000}}},
  mediaRuntime:{audioEnabled:true,ttsAvailable:true}};
 const providers=new Providers(env,{singingContext:()=>({enabled:true,tasks:[],knowledge:[],limits:{singleSegmentSeconds:90}})});
 const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),25000),began=performance.now(),elapsed=()=>Math.round(performance.now()-began);
 const row={name:item.name,input:item.text,requests:{host:1,review:0,tts:0},success:false};let candidateWork,stream;
 const beginSegment=segment=>{
  if(candidateWork)return;row.firstSegmentTextMs=elapsed();row.measuredSegment=segment.text;row.measuredChars=[...segment.text].length;
  candidateWork=(async()=>{
   row.ttsStartedMs=elapsed();row.requests.tts++;
   stream=new PcmSpeech(segment.text,signal=>providers.synthesizeStream(identity,segment.text,segment.mood,signal));
   stream.ready.then(()=>{row.audioFinishedMs=elapsed();}).catch(()=>{});
   const cancel=()=>stream.close();controller.signal.addEventListener('abort',cancel,{once:true});
   stream.on('record',e=>{if(e.type==='audio'){row.firstPcmMs??=elapsed();if(stream.bytes>=4800)row.bufferReadyMs??=elapsed();}});
   row.reviewStartedMs=elapsed();row.requests.review++;
   try{
    const candidate={id:'candidate-'+id,role:'host',speakerId:'Z',text:segment.text,intent:segment.action||'speak',segment:{index:0,scope:segment.scope||'sentence'},replyTo:segment.handledIds,evidenceIds:[],...(segment.tool?{plannedTool:{name:segment.tool.name,targetId:segment.tool.args?.taskId||segment.tool.args?.songId||segment.tool.args?.messageId||''}}:{})};
    const reviewPayload={requestId:crypto.randomUUID(),kind:'review',eventId:candidate.id,message:'独立审阅待发布的直播发言',context:{revision:1,publicEvents,sessionEvents:publicEvents,waiting_messages:[{id,speakerRole:'player',speakerId:'player',text:item.text}],candidate}};
    const reviewOptions={signal:controller.signal,compactReview:true};
    let review=await providers.generate(identity,reviewPayload,reviewOptions);
    if(review.decision==='need_context'){
     row.requests.review++;
     review=await providers.generate(identity,{...reviewPayload,requestId:crypto.randomUUID()},{...reviewOptions,reviewExpanded:true});
    }
    row.reviewFinishedMs=elapsed();row.reviewDecision=review.decision;row.reviewReason=review.reason;
    if(review.decision!=='allow'){row.failure='review_rejected';stream.close();return;}
    await stream.ready;row.audioDurationMs=stream.durationMs;
    if(!stream.done||stream.error)throw Error('tts_incomplete');
    row.bufferReadyMs??=row.audioFinishedMs;
    row.firstPlayableMs=Math.max(row.reviewFinishedMs,row.bufferReadyMs);row.segmentCompleteAndApprovedMs=Math.max(row.reviewFinishedMs,row.audioFinishedMs);
    row.reviewServiceMs=row.reviewFinishedMs-row.reviewStartedMs;row.ttsToFirstPcmMs=row.firstPcmMs-row.ttsStartedMs;
   }finally{controller.signal.removeEventListener('abort',cancel);}
  })();candidateWork.catch(()=>{});
 };
 try{
  const result=await providers.generate(identity,{requestId:crypto.randomUUID(),kind:'host',eventId:id,message:'请决定直播下一步。',context},{signal:controller.signal,onSentence:beginSegment});
  row.fullTextMs=elapsed();row.action=result.action;row.reply=result.speech;row.replyChars=[...result.speech].length;row.path=candidateWork?'early-sentence':'complete-reply-fallback';
  if(!candidateWork&&['speak','tool'].includes(result.action))beginSegment({scope:'reply',text:result.speech,mood:result.mood,handledIds:result.handledIds,action:result.action,tool:result.tool});
  if(candidateWork)await candidateWork;else row.failure='no_speech';
  row.success=Number.isFinite(row.firstPlayableMs)&&!row.failure;
 }catch(error){row.failure=safe(error);}
 finally{clearTimeout(timer);controller.abort();stream?.close();if(candidateWork)await candidateWork.catch(()=>{});}
 report.cases.push(row);
 await fs.mkdir(path.dirname(output),{recursive:true});await fs.writeFile(output,JSON.stringify(report,null,2)+'\n');
 console.log(JSON.stringify({case:row.name,textMs:row.fullTextMs,playableMs:row.firstPlayableMs,audioCompleteMs:row.segmentCompleteAndApprovedMs,path:row.path,success:row.success,failure:row.failure}));
}
const successful=report.cases.filter(row=>row.success);
report.summary={attempts:report.cases.length,successes:successful.length,failures:report.cases.filter(row=>!row.success).map(({name,failure,reviewReason})=>({name,failure,reviewReason})),
 fullText:stats(report.cases.map(row=>row.fullTextMs).filter(Number.isFinite)),firstSegmentText:stats(report.cases.map(row=>row.firstSegmentTextMs).filter(Number.isFinite)),firstPlayable:stats(successful.map(row=>row.firstPlayableMs)),segmentCompleteAndApproved:stats(successful.map(row=>row.segmentCompleteAndApprovedMs)),reviewService:stats(successful.map(row=>row.reviewServiceMs)),ttsToFirstPcm:stats(successful.map(row=>row.ttsToFirstPcmMs))};
await fs.writeFile(output,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report.summary,null,2));
