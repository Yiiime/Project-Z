// Opt-in live model test. Fictional public room only: no microphone, TTS, song generation or saves.
import {Providers,loadEnvironment} from '../server/providers.mjs';
import {AudienceDirector} from '../server/shared/ui/audience-director.js';
if(!process.argv.includes('--live'))throw Error('Add --live to run paid model verification for one synthetic room');
if(!process.env.Z_LIVE_CONFIG_DIR)throw Error('Provide Z_LIVE_CONFIG_DIR for this live model probe');
const provider=new Providers(loadEnvironment(process.env.Z_LIVE_CONFIG_DIR)),id=crypto.randomUUID(),config=await provider.config(id);
const version=config.review.version,lines=[],tallies={},reviews=[];
if(!config.enabled)throw Error('Model unavailable');
const facts=[
 {id:'intro',type:'speech.completed',speakerRole:'host',speakerId:'Z',text:'刚才有人说把歌留到夜车上听。我记下了，今天有人点了一首叫《晚潮》的歌。'},
 {id:'published',type:'npc.message',speakerRole:'audience',speakerId:'bean',text:'光听这歌名就觉得安静，适合睡前听。'}
];
for(const [who,text,expected] of [
 ['rain','这名字听起来像是会让人慢慢安静下来的歌。','drop'],
 ['wind','我上学时跟同桌一人戴一只耳机，听歌全靠凑一块儿。','allow'],
 ['tape','如果还有下一首，我想点《晴天》。','allow']
]){
 const candidate={id:crypto.randomUUID(),role:'audience',speakerId:who,text,intent:'reaction',evidenceIds:['intro'],replyTo:[]};
 const r=await provider.generate(id,{requestId:crypto.randomUUID(),eventId:candidate.id,kind:'review',message:'检查待发布的候选',context:{revision:2,publicEvents:facts,waiting_messages:[],candidate}});
 reviews.push({who,actual:r.decision,expected,reason:r.reason});
}
const start=Date.now(),a=new AudienceDirector({now:()=>Date.now()-start,phase:()=>'chat',config:config.policy.audience,
 request:async(p,opt)=>{try{const r=await provider.generate(id,p,opt);const k=r.decision||r.status;tallies[k]=(tallies[k]||0)+1;return r;}
 catch(e){tallies[e.code||'error']=(tallies[e.code||'error']||0)+1;throw e;}},
 publish:m=>{lines.push([Math.round((Date.now()-start)/1000),m.npc_id,m.text]);a.record({id:crypto.randomUUID(),type:'npc.message',speakerRole:'audience',speakerId:m.npc_id,text:m.text});}});
a.dialogue.configure({version});a.dialogue.setConcurrent(true);for(const f of facts)a.record(f);
let cued=false;while(Date.now()-start<65000){
 if(!cued&&Date.now()-start>=32000){a.record({id:'next',type:'speech.completed',speakerRole:'host',speakerId:'Z',text:'我以前攒零花钱买过一副蓝耳机。今天先不加歌，想听听大家平时都怎么听。'});cued=true;}
 a.tick();await new Promise(r=>setTimeout(r,160));
}
a.invalidate();console.log(JSON.stringify({reviews,tallies,lines},null,2));if(reviews.some(r=>r.actual!==r.expected)||!lines.length)process.exitCode=1;
