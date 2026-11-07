// Opt-in, bounded real-provider diagnostic: one host, one review, one TTS at most.
// It sends only the synthetic sentence below and never opens the microphone.
import {performance} from 'node:perf_hooks';
import {fileURLToPath} from 'node:url';
import {Providers,loadEnvironment} from '../server/providers.mjs';
import {PcmSpeech} from '../server/pcm-speech.mjs';
if(!process.argv.includes('--live'))throw Error('Add --live to run up to 3 billable provider requests.');
const providers=new Providers(loadEnvironment(process.env.Z_LIVE_CONFIG_DIR||fileURLToPath(new URL('../',import.meta.url))));
const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),25000),start=performance.now();
const elapsed=()=>Math.round(performance.now()-start),id=crypto.randomUUID();
const event={id,type:'player.message',text:'今天办公室有点吵，我想在这里安静聊一会儿。',speakerRole:'player',speakerId:'player',status:'published'};
const payload={requestId:crypto.randomUUID(),kind:'host',eventId:id,message:'请回应这位连麦来客。',context:{phase:'chat',publicEvents:[event],call:{active:true,id:crypto.randomUUID(),input:'idle',turns:1,audienceOpen:false,story:null,audienceCandidates:[],audienceReason:'none'},host:{inbox:[{id,name:'连麦来客',mine:true,text:event.text}],memory:'',session:{sessionId:crypto.randomUUID(),evidence:[event],unfinishedItems:[],recentInitiatives:[],crossSessionMemory:{available:false,entries:[]}}},mediaRuntime:{audioEnabled:true,ttsAvailable:true}}};
const report={microphoneUsed:false,requests:{host:1,review:0,tts:0},note:'Synthetic text -> reviewed first sentence -> first server PCM. Excludes ASR, RTC uplink and Unity output.'};
let sentenceWork,stream;
const candidate=sentence=>{
  if(sentenceWork)return;report.firstSentenceMs=elapsed();report.requests.review++;
  sentenceWork=(async()=>{
   const review=await providers.generate('native-call-measure-'+id,{requestId:crypto.randomUUID(),kind:'review',eventId:'first-sentence',message:'审阅这句连麦答复',context:{revision:1,publicEvents:[event],sessionEvents:[event],waiting_messages:[{id,speakerRole:'player',speakerId:'player',text:event.text}],candidate:{id:'first-sentence',role:'host',speakerId:'Z',text:sentence.text,intent:'speak',replyTo:sentence.handledIds,evidenceIds:[]}}},{signal:controller.signal,compactReview:true});
   report.reviewMs=elapsed();report.reviewDecision=review.decision;if(review.decision!=='allow')return;
   report.requests.tts++;stream=new PcmSpeech(sentence.text,signal=>providers.synthesizeStream('native-call-measure-'+id,sentence.text,sentence.mood,signal));
   controller.signal.addEventListener('abort',()=>stream.close(),{once:true});stream.on('record',e=>{if(e.type==='audio'&&report.firstPcmMs===undefined)report.firstPcmMs=elapsed();});await stream.ready;report.ttsFinishedMs=elapsed();report.ttsComplete=stream.done;
  })();sentenceWork.catch(()=>{});
 };
try{
 const result=await providers.generate('native-call-measure-'+id,payload,{signal:controller.signal,onSentence:candidate});report.hostFinishedMs=elapsed();report.hostAction=result.action;report.path=sentenceWork?'early-sentence':'complete-reply-fallback';
 if(!sentenceWork&&result.action==='speak')candidate({text:result.speech,mood:result.mood,handledIds:result.handledIds});if(sentenceWork)await sentenceWork;
}catch(error){report.error=/^[a-z_]+$/.test(error.code||error.message)?error.code||error.message:'provider_request_failed';process.exitCode=1;}
finally{clearTimeout(timer);controller.abort();stream?.close();}
console.log(JSON.stringify(report,null,2));
