/** Offline Vivi playback tests. All audio, fetches and clocks are injected. */
import assert from 'node:assert/strict';
import {SpeechDirector} from '../ui/speech-director.js';
import {LiveEngine} from '../ui/live-engine.js';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {reviewReply} from './review-fixture.mjs';

if(!globalThis.CustomEvent)globalThis.CustomEvent=class extends Event{constructor(type,{detail}={}){super(type);this.detail=detail;}};
const original={setTimeout,clearTimeout,performance:globalThis.performance};
let now=0,timerId=0;const timers=new Map();
globalThis.setTimeout=(callback,delay=0)=>{const id=++timerId;timers.set(id,{callback,at:now+delay});return id;};
globalThis.clearTimeout=id=>timers.delete(id);
Object.defineProperty(globalThis,'performance',{configurable:true,value:{now:()=>now}});
function step(ms){const end=now+ms;for(;;){const next=[...timers].filter(([,v])=>v.at<=end).sort((a,b)=>a[1].at-b[1].at)[0];if(!next)break;now=next[1].at;timers.delete(next[0]);next[1].callback();}now=end;}
async function flush(){for(let index=0;index<150;index++)await Promise.resolve();}
function deferred(){let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});return{promise,resolve,reject};}
const config={enabled:true,provider:'Volcengine',voice:'Vivi 2.0',speaker:'zh_female_vv_uranus_bigtts',format:'mp3',maxTextLength:500};
function frames(payload,words=[{word:payload.text,startTime:0,endTime:4,confidence:.9}]){
  return [
    {type:'start',requestId:payload.requestId,format:'mp3',sampleRate:24000,source:'volcengine',speaker:config.speaker},
    {type:'audio',requestId:payload.requestId,index:0,data:Buffer.from([1,2]).toString('base64')},
    {type:'sentence',requestId:payload.requestId,sentence:{text:payload.text,words}},
    {type:'audio',requestId:payload.requestId,index:1,data:Buffer.from([3,4]).toString('base64')},
    {type:'done',requestId:payload.requestId},
  ];
}
function response(records,chunkSize=97){
  const data=new TextEncoder().encode(records.map(record=>JSON.stringify(record)).join('\n')+'\n');
  return new Response(new ReadableStream({start(controller){for(let at=0;at<data.length;at+=chunkSize)controller.enqueue(data.slice(at,at+chunkSize));controller.close();}}),{headers:{'Content-Type':'application/x-ndjson'}});
}
class AudioContextMock{
  constructor(){this.currentTime=0;this.state='suspended';this.sources=[];this.decodes=[];this.duration=4;this.resumeCalls=0;this.suspendCalls=0;this.level=.1;this.destination={};}
  createGain(){return{gain:{value:1,events:[],cancelScheduledValues(t){this.events.push(["cancel",t]);},setValueAtTime(v,t){this.value=v;this.events.push(["set",v,t]);},linearRampToValueAtTime(v,t){this.events.push(["ramp",v,t]);}},connect(target){this.target=target;},disconnect(){}};}
  createAnalyser(){const context=this;return{fftSize:256,connect(target){this.target=target;},getFloatTimeDomainData(samples){samples.fill(context.level);}};}
  resume(){this.resumeCalls++;this.state='running';return Promise.resolve();}
  suspend(){this.suspendCalls++;this.state='suspended';return Promise.resolve();}
  close(){this.state='closed';return Promise.resolve();}
  decodeAudioData(bytes){this.decodes.push([...new Uint8Array(bytes)]);return this.decode?this.decode(bytes):Promise.resolve({duration:this.duration});}
  createBufferSource(){
    const source={onended:null,stopped:0,disconnected:false,connect(target){this.target=target;},disconnect(){this.disconnected=true;},
      start(when,offset){this.started=true;this.offset=offset;},stop(){this.stopped++;},end(){this.onended?.();}};
    this.sources.push(source);return source;
  }
}
function harness({enabled=true,post,decode}={}){
  const context=new AudioContextMock();if(decode)context.decode=decode;
  const calls=[],texts=[],completed=[],activities=[],statuses=[];let contexts=0;
  const director=new SpeechDirector({
    fetchImpl:async(url,init={})=>{
      if(url==='/api/tts/config')return Response.json({...config,enabled});
      assert.equal(url,'/api/tts');assert.equal(init.method,'POST');assert.equal(init.headers['Content-Type'],'application/json');
      const payload=JSON.parse(init.body);assert.match(payload.requestId,/^[a-f0-9-]{36}$/i);
      assert(payload.text.length<=500);const call={payload,signal:init.signal};calls.push(call);
      return post?post(call,calls.length):response(frames(payload));
    },
    createAudioContext:()=>{contexts++;return context;},
    requestFrame:callback=>setTimeout(callback,16),cancelFrame:id=>clearTimeout(id),
  });
  for(const [type,list]of[['text',texts],['complete',completed],['activity',activities],['status',statuses]])director.addEventListener(type,event=>list.push(event.detail));
  return{director,context,calls,texts,completed,activities,statuses,get contexts(){return contexts;}};
}
async function withDirector(options,run){const h=harness(options);try{await run(h);}finally{h.director.destroy();}assert.equal(timers.size,0,'Destroy clears all caption, stream and frame timers');}
async function enabled(h){await h.director.configure();assert.equal(await h.director.setEnabled(true),true);}
const results=[];
async function check(name,run){await run();results.push(name);console.log(`PASS ${name}`);}
try{
  await check('thinking feedback reuses Vivi cache, stays outside reply events and yields to ready audio',()=>withDirector({},async h=>{
    await enabled(h);const feedback=[];h.director.addEventListener('call-feedback',e=>feedback.push(e.detail));
    await Promise.all([h.director.prepareCallThinking(),h.director.prepareCallThinking()]);assert.equal(h.calls.length,1);
    assert(h.director.playCallThinking());assert.equal(h.director.current,null);assert.equal(h.texts.length,0);assert.equal(h.completed.length,0);
    const cue=h.context.sources[0];assert(!h.director.playCallThinking());
    h.director.warmSpeech('answer','我听到你说的了。','warm');await flush();
    h.director.speak({id:900,text:'我听到你说的了。',mood:'warm',preparedAudioKey:'answer'});await flush();
    assert.equal(h.context.sources.length,1,'formal audio must not overlap feedback');step(80);await flush();
    assert.equal(cue.stopped,1);assert.equal(h.context.sources.length,2);assert.equal(h.calls.length,2,'cached cue does not generate another request');
    assert.equal(feedback.filter(e=>e.phase==='ended').length,1);assert.equal(h.completed.length,0,'feedback cannot finish a player reply');
    h.context.sources[1].end();assert.deepEqual(h.completed.map(e=>e.id),[900]);assert(h.director.playCallThinking());
    h.director.cancel();step(80);await flush();assert.equal(h.director.thinkingCue,null);assert.equal(h.calls.length,2);
  }));
  await check('thinking feedback never starts late from pending preparation, and mute/pause/destroy stop it',()=>withDirector({},async h=>{
    await enabled(h);const preparing=h.director.prepareCallThinking();assert.equal(h.director.playCallThinking(),false);await preparing;assert.equal(h.context.sources.length,0);
    assert(h.director.playCallThinking());h.director.pause();assert.equal(h.director.thinkingCue,null);assert(!h.director.playCallThinking());h.director.resume();await h.context.resume();
    assert(h.director.playCallThinking());await h.director.setEnabled(false);assert.equal(h.director.thinkingCue,null);assert(!h.director.playCallThinking());
  }));
  await check('soft yield fades old voice, reuses prepared acknowledgement and restores gain',()=>withDirector({},async h=>{
    await enabled(h);await h.director.prepareCallYield();assert.equal(h.calls.length,1);assert(h.director.callYieldAudio);
    h.director.speak({id:700,text:'这是还没说完的话。'});await flush();const old=h.context.sources.at(-1);
    let faded=false;const fading=h.director.fadeOut(180).then(()=>faded=true);step(179);assert(!faded);assert.equal(old.stopped,0);
    assert(h.director.gain.gain.events.some(e=>e[0]==='ramp'&&e[1]===0&&e[2]===.18));step(1);await fading;
    h.director.cancel();assert(old.stopped);h.director.speak({id:701,text:'好，你先说。',source:'call-yield'});await flush();
    assert.equal(h.calls.length,2,'cue reuses warmed audio without a late request');assert.equal(h.context.sources.length,2);assert.equal(h.director.gain.gain.value,h.director.volume);
  }));
  await check('unprepared yield uses immediate text and never starts a late audio request',()=>withDirector({},async h=>{
    await enabled(h);h.director.speak({id:702,text:'好，你先说。',source:'call-yield'});await flush();assert.equal(h.calls.length,0);step(10000);assert.deepEqual(h.completed.map(e=>e.id),[702]);
  }));
  await check('meter is before user gain and performance starts only after audio starts',()=>withDirector({},async h=>{
    const events=[];h.director.addEventListener('performance',e=>events.push(e.detail));
    await enabled(h);h.director.speak({id:101,text:'等声音开始再点头。',mood:'shy'});
    assert(!events.some(e=>e.phase==='speaking'));await flush();
    assert.equal(h.calls[0].payload.mood,'warm');assert.equal(h.context.sources[0].target,h.director.analyser);
    assert.equal(h.director.analyser.target,h.director.gain);assert.equal(h.director.gain.target,h.context.destination);
    assert.equal(events.filter(e=>e.phase==='speaking').length,1);
    h.director.setVolume(0);h.context.currentTime=1;step(16);assert(h.activities.at(-1).level>0);
    h.director.pause();h.director.resume();await flush();assert.equal(events.at(-1).resumed,true);
    h.context.sources.at(-1).end();assert.equal(events.at(-1).phase,'listening');
  }));
  await check('default captions and unconfigured voice never POST or instantiate audio',()=>withDirector({enabled:false},async h=>{
    await h.director.configure();h.director.speak({id:1,text:'晚上好，先聊两句。',mood:'happy'});step(10000);
    assert.equal(h.calls.length,0);assert.equal(h.contexts,0);assert.deepEqual(h.completed.map(e=>e.id),[1]);
    assert.equal(h.texts.at(-1).text,'晚上好，先聊两句。');assert.equal(h.director.enabled,false);
    assert.equal(await h.director.setEnabled(true),false);assert.equal(h.calls.length,0);assert.match(h.director.status.message,/尚未配置/);
  }));

  await check('enabling voice preserves the active caption and only synthesizes the next speech',()=>withDirector({},async h=>{
    await h.director.configure();
    const text='这一段先完整说完，下一段再听声音。';
    h.director.speak({id:50,text});step(280);
    const current=h.director.current,caption=h.director.fallback.current,before=h.texts.at(-1).text,count=h.texts.length;
    assert(before.length>0&&before.length<text.length);
    assert.equal(await h.director.setEnabled(true),true);await flush();
    assert.equal(h.director.current,current);assert.equal(h.director.current.id,50);assert.equal(h.director.fallback.current,caption);
    assert.equal(h.texts.at(-1).text,before);assert.equal(h.texts.length,count,'Enabling must not reset or rewrite the current caption');
    assert.equal(h.calls.length,0);assert.equal(h.context.sources.length,0);assert.equal(h.completed.length,0);
    step(10000);assert.equal(h.texts.at(-1).text,text);assert.deepEqual(h.completed.map(e=>e.id),[50]);
    const currentTexts=h.texts.filter(e=>e.id===50).map(e=>e.text);
    assert(currentTexts.every((value,index)=>index===0||value.startsWith(currentTexts[index-1])),'Every caption update continues the visible prefix');
    h.director.speak({id:51,text:'现在开始听我的声音。'});await flush();
    assert.equal(h.calls.length,1);assert.equal(h.calls[0].payload.text,'现在开始听我的声音。');assert.equal(h.context.sources.length,1);
    assert.deepEqual(h.completed.map(e=>e.id),[50]);h.context.sources[0].end();assert.deepEqual(h.completed.map(e=>e.id),[50,51]);
  }));

  await check('enabling voice while captions are paused preserves pause and resumes the same text',()=>withDirector({},async h=>{
    await h.director.configure();const text='暂停一下，这段字幕稍后继续。';h.director.speak({id:52,text});step(300);h.director.pause();
    const current=h.director.current,before=h.texts.at(-1).text;
    assert.equal(await h.director.setEnabled(true),true);step(10000);await flush();
    assert.equal(h.director.current,current);assert.equal(h.director.paused,true);assert.equal(h.texts.at(-1).text,before);
    assert.equal(h.calls.length,0);assert.equal(h.context.sources.length,0);assert.equal(h.completed.length,0);
    h.director.resume();step(10000);assert.equal(h.texts.at(-1).text,text);assert.deepEqual(h.completed.map(e=>e.id),[52]);
    assert.equal(h.calls.length,0);
  }));

  await check('enabling an already enabled voice leaves playing audio and its completion intact',()=>withDirector({},async h=>{
    await enabled(h);h.director.speak({id:53,text:'我正在把这段话说完整。'});await flush();h.context.currentTime=1.5;step(16);
    const current=h.director.current,source=h.context.sources[0],before=h.texts.at(-1).text,resumes=h.context.resumeCalls;
    assert.equal(await h.director.setEnabled(true),true);await flush();
    assert.equal(h.director.current,current);assert.equal(h.director.current.source,source);assert.equal(source.stopped,0);
    assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,1);assert.equal(h.context.resumeCalls,resumes);
    assert.equal(h.texts.at(-1).text,before);assert.equal(h.director.status.mode,'playing');assert.equal(h.completed.length,0);
    source.end();assert.deepEqual(h.completed.map(e=>e.id),[53]);
  }));

  await check('stream synthesis completion does not finish the show; actual audio time drives words and mouth activity',()=>withDirector({post:call=>response(frames(call.payload,[
    {word:'你',startTime:0,endTime:1,confidence:.9},{word:'好',startTime:1,endTime:1.5,confidence:.9},{word:'世界',startTime:3,endTime:4,confidence:.9},
  ]),7)},async h=>{
    await enabled(h);h.director.speak({id:2,text:'你好世界',mood:'happy'});await flush();
    assert.deepEqual(h.context.decodes,[[1,2,3,4]]);assert.equal(h.calls[0].payload.mood,'happy');assert.equal(h.context.sources.length,1);
    assert.equal(h.completed.length,0,'NDJSON done only means synthesis finished');
    h.context.currentTime=1.6;step(16);assert.equal(h.texts.at(-1).text,'你好');
    const before=h.texts.at(-1).text;step(2000);assert.equal(h.texts.at(-1).text,before,'Wall-clock time cannot run ahead of the audio clock');
    assert(h.activities.some(e=>e.id===2&&e.speaking&&e.level>.008));h.context.level=0;step(16);assert.equal(h.activities.at(-1).speaking,false);
    const end=h.context.sources[0].onended;h.context.sources[0].end();end();
    assert.deepEqual(h.completed.map(e=>e.id),[2]);assert.equal(h.texts.at(-1).text,'你好世界');
  }));

  await check('pause resumes a decoded buffer at its saved offset with no second synthesis',()=>withDirector({},async h=>{
    await enabled(h);h.director.speak({id:3,text:'暂停后继续。'});await flush();
    const first=h.context.sources[0],oldEnd=first.onended;h.context.currentTime=1.5;step(16);const before=h.texts.at(-1).text;
    h.director.pause();assert.equal(first.stopped,1);assert.equal(h.context.state,'suspended');step(6000);
    assert.equal(h.texts.at(-1).text,before);oldEnd();assert.equal(h.completed.length,0);
    h.director.resume();await flush();assert.equal(h.context.sources.length,2);assert.equal(h.context.sources[1].offset,1.5);
    assert.equal(h.calls.length,1);h.context.currentTime=2;step(16);assert(h.texts.at(-1).text.length>=before.length);
    h.context.sources[1].end();assert.deepEqual(h.completed.map(e=>e.id),[3]);
  }));

  await check('a new speech aborts an older request and late success cannot produce audio',()=>{
    const old=deferred();return withDirector({post:(call,index)=>index===1?old.promise:response(frames(call.payload))},async h=>{
      await enabled(h);h.director.speak({id:4,text:'旧的句子'});await flush();const first=h.calls[0];
      h.director.speak({id:5,text:'新的句子'});await flush();assert.equal(first.signal.aborted,true);assert.equal(h.context.sources.length,1);
      old.resolve(response(frames(first.payload)));await flush();assert.equal(h.context.sources.length,1);
      h.context.sources[0].end();assert.deepEqual(h.completed.map(e=>e.id),[5]);
    });
  });

  await check('a delayed AudioContext resume cannot start a second source after pause and resume',()=>withDirector({},async h=>{
    await enabled(h);const gate=deferred();let first=true;
    h.context.resume=()=>{if(first){first=false;return gate.promise;}return Promise.resolve();};
    h.director.speak({id:15,text:'恢复时只播一次。'});await flush();
    assert.equal(h.director.current.mode,'audio');assert.equal(h.context.sources.length,0);
    h.director.pause();h.director.resume();await flush();assert.equal(h.context.sources.length,1);
    gate.resolve();await flush();assert.equal(h.context.sources.length,1);assert.equal(h.calls.length,1);
    h.context.sources[0].end();assert.deepEqual(h.completed.map(e=>e.id),[15]);
  }));

  await check('real engine waits for the complete recording before a queued reply and never replays the finished phase',()=>withDirector({},async h=>{
    await enabled(h);const engine=new LiveEngine(),speeches=[];engine.setSpeechDriven(true);
    engine.addEventListener('speak',({detail})=>{speeches.push(detail);h.director.speak(detail);});
    engine.addEventListener('state',({detail})=>{if(!detail.currentSpeech)h.director.cancel({preserveRetained:true});});
    h.director.addEventListener('complete',({detail})=>engine.completeSpeech(detail.id));
    try{
      engine.start();clearInterval(engine._timer);engine._timer=null;await flush();
      const original=speeches[0],oldSource=h.context.sources[0],oldEnd=oldSource.onended;
      h.context.currentTime=1.25;step(16);const prefix=h.texts.at(-1).text;
      assert(prefix.length>0);assert.equal(original.resume,false);assert.equal(typeof original.utteranceKey,'string');
      assert.equal(engine.sendMessage('你好'),true);
      for(let elapsed=0;elapsed<800;elapsed+=160){step(160);engine._tick();}await flush();
      assert.equal(speeches.length,1,'A ready reply cannot replace the current recording');
      assert.equal(engine.getState().currentSpeechId,original.id);assert.equal(h.director.current.source,oldSource);
      assert.equal(oldSource.stopped,0);assert.equal(h.calls.length,1);assert.equal(h.texts.at(-1).text,prefix);
      h.context.currentTime=4;step(16);
      assert.equal(h.texts.at(-1).text,original.text);assert.equal(speeches.length,1,'Full captions are not natural audio completion');
      oldSource.end();await flush();
      const reply=speeches.at(-1),replySource=h.context.sources.at(-1);
      assert.equal(reply.scope,'reply');assert.notEqual(reply.id,original.id);assert.equal(h.calls.length,2);
      assert.deepEqual(h.completed.map(event=>event.id),[original.id]);
      oldEnd();assert.equal(engine.getState().currentSpeechId,reply.id);assert.equal(engine.completeSpeech(original.id),false);
      replySource.end();await flush();
      const next=speeches.at(-1);
      assert.equal(next.scope,'phase');assert.equal(next.resume,false);assert.notEqual(next.utteranceKey,original.utteranceKey);
      assert.equal(speeches.filter(item=>item.utteranceKey===original.utteranceKey).length,1,'The completed phase must not be resumed or synthesized again');
      assert.deepEqual(h.completed.map(event=>event.id),[original.id,reply.id]);
      h.director.cancel();assert.equal(h.director.retained.size,0);engine.replay();clearInterval(engine._timer);engine._timer=null;await flush();
      assert.equal(speeches.at(-1).resume,false);assert.notEqual(speeches.at(-1).utteranceKey,original.utteranceKey);
    }finally{engine.destroy();}
  }));

  await check('explicit continuation after an interrupted pending synthesis uses text without retrying that paid request',()=>{
    const pending=deferred();return withDirector({post:(call,index)=>index===1?pending.promise:response(frames(call.payload))},async h=>{
      await enabled(h);h.director.speak({id:20,utteranceKey:'phase-20',scope:'phase',text:'正在准备的剧情。'});await flush();
      h.director.speak({id:21,utteranceKey:'reply-21',scope:'reply',text:'先回应弹幕。'});await flush();h.context.sources[0].end();
      h.director.speak({id:22,utteranceKey:'phase-20',scope:'phase',resume:true,text:'正在准备的剧情。'});await flush();
      assert.equal(h.calls.length,2);assert.equal(h.director.current.mode,'text');assert.equal(h.calls[0].signal.aborted,true);
      pending.resolve(response(frames(h.calls[0].payload)));await flush();assert.equal(h.context.sources.length,1);
      step(10000);assert(h.completed.some(event=>event.id===22));assert(!h.completed.some(event=>event.id===20));
    });
  });

  await check('retention is bounded, expires while idle and never treats a repeated sentence as a continuation',()=>withDirector({},async h=>{
    await enabled(h);
    for(let index=0;index<6;index++){
      h.director.speak({id:30+index,utteranceKey:`phase-${index}`,scope:'phase',text:'同一句也可能是新的发言。'});await flush();
    }
    assert.equal(h.calls.length,6,'Same text under a fresh key is a new utterance');
    assert.equal(h.director.retained.size,4);assert(h.director.retainedBytes<=8*1024*1024);
    assert.equal(h.director.retained.has('phase-0'),false);
    h.director.speak({id:40,utteranceKey:'phase-0',scope:'phase',resume:true,text:'同一句也可能是新的发言。'});await flush();
    assert.equal(h.calls.length,6,'An evicted continuation falls back to text rather than charging again');
    assert.equal(h.director.current.mode,'text');step(120001);assert.equal(h.director.retained.size,0);assert.equal(h.director.retainedBytes,0);
    h.director.speak({id:41,utteranceKey:'phase-4',scope:'phase',resume:true,text:'同一句也可能是新的发言。'});assert.equal(h.calls.length,6);
    h.director.cancel();assert.equal(h.director.retained.size,0);assert.equal(h.director.retentionTimer,null);
  }));

  await check('pausing a pending request aborts it and resumes only captions without a paid retry',()=>{
    const pending=deferred();return withDirector({post:()=>pending.promise},async h=>{
      await enabled(h);h.director.speak({id:6,text:'等声音的时候暂停。'});await flush();h.director.pause();
      assert.equal(h.calls[0].signal.aborted,true);step(10000);assert.equal(h.completed.length,0);
      pending.resolve(response(frames(h.calls[0].payload)));await flush();h.director.resume();step(10000);await flush();
      assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,0);assert.deepEqual(h.completed.map(e=>e.id),[6]);
    });
  });

  await check('muting mid-audio keeps the displayed prefix, continues text and stops all sound',()=>withDirector({},async h=>{
    await enabled(h);h.director.speak({id:7,text:'字幕保持连贯，不要倒退。'});await flush();h.context.currentTime=2;step(16);
    const before=h.texts.at(-1).text,source=h.context.sources[0];await h.director.setEnabled(false);
    assert.equal(source.stopped,1);assert.equal(h.texts.at(-1).text,before);step(10000);
    assert.equal(h.texts.at(-1).text,'字幕保持连贯，不要倒退。');assert.deepEqual(h.completed.map(e=>e.id),[7]);
    h.director.speak({id:8,text:'静音后的下一句。'});step(10000);assert.equal(h.calls.length,1);assert.deepEqual(h.completed.map(e=>e.id),[7,8]);
  }));

  await check('HTTP and stream failures fall back to complete text without claiming audio played',async()=>{
    for(const failure of ['http','stream','missing-done','bad-index','wrong-request','decode'])await withDirector({
      decode:failure==='decode'?async()=>{throw new Error('invalid MP3');}:undefined,
      post:call=>{
        if(failure==='http')return Response.json({error:'not_configured'},{status:503});
        let records=frames(call.payload);
        if(failure==='stream')records=[records[0],{type:'error',requestId:call.payload.requestId,error:'provider_failed'}];
        if(failure==='missing-done')records.pop();
        if(failure==='bad-index')records[1].index=2;
        if(failure==='wrong-request')records[1].requestId='another-request';
        return response(records);
      },
    },async h=>{
      await enabled(h);h.director.speak({id:9,text:'失败也要把这句完整写完。'});await flush();assert.equal(h.director.status.mode,'fallback');
      assert.equal(h.context.sources.length,0);step(10000);assert.equal(h.texts.at(-1).text,'失败也要把这句完整写完。');assert.deepEqual(h.completed.map(e=>e.id),[9]);
    });
  });

  await check('a non-cooperative pending fetch times out into text and cannot surface after completion',()=>{
    const pending=deferred();return withDirector({post:()=>pending.promise},async h=>{
      await enabled(h);h.director.speak({id:10,text:'超时之后也继续。'});await flush();step(25001);
      assert.equal(h.director.status.reason,'timeout');assert.equal(h.calls[0].signal.aborted,true);step(10000);
      assert.deepEqual(h.completed.map(e=>e.id),[10]);pending.resolve(response(frames(h.calls[0].payload)));await flush();assert.equal(h.context.sources.length,0);
    });
  });

  await check('finish-now stops audio, reveals all captions and completes exactly once after the reading hold',()=>withDirector({},async h=>{
    await enabled(h);h.director.speak({id:11,text:'这句话可以立刻展开。'});await flush();const source=h.context.sources[0],oldEnd=source.onended;
    h.director.finishNow();assert.equal(source.stopped,1);assert.equal(h.texts.at(-1).text,'这句话可以立刻展开。');assert.equal(h.completed.length,0);
    oldEnd();step(1500);assert.deepEqual(h.completed.map(e=>e.id),[11]);
  }));

  await check('invalid or sparse word timestamps use the audio clock for the complete caption',async()=>{
    for(const words of [[],[{word:'你',startTime:2,endTime:1}], [{word:'你',startTime:0,endTime:1,confidence:.1}]])await withDirector({post:call=>response(frames(call.payload,words))},async h=>{
      await enabled(h);h.director.speak({id:12,text:'你好世界'});await flush();assert.equal(h.director.current.timeline,null);
      h.context.currentTime=2;step(16);assert.equal(h.texts.at(-1).text,'你好');h.context.sources[0].end();assert.equal(h.texts.at(-1).text,'你好世界');
    });
  });

  await check('late decoding after cancellation or pause cannot revive old audio',async()=>{
    for(const action of ['cancel','pause']){
      const decode=deferred();await withDirector({decode:()=>decode.promise},async h=>{
        await enabled(h);h.director.speak({id:13,text:'解码期间也可能离开。'});await flush();assert.equal(h.context.decodes.length,1);
        h.director[action]();decode.resolve({duration:4});await flush();assert.equal(h.context.sources.length,0);
        if(action==='pause'){h.director.resume();step(10000);assert.deepEqual(h.completed.map(e=>e.id),[13]);}
        else{step(10000);assert.equal(h.completed.length,0);}
      });
    }
  });

  await check('over-limit speech remains complete text and volume affects only the audio gain',()=>withDirector({},async h=>{
    await enabled(h);h.director.setVolume(.4);assert.equal(h.director.gain.gain.value,.4);
    const text='长'.repeat(501);h.director.speak({id:14,text});await flush();assert.equal(h.calls.length,0);assert.equal(h.director.status.reason,'long');
    h.director.finishNow();step(1500);assert.equal(h.texts.at(-1).text,text);assert.deepEqual(h.completed.map(e=>e.id),[14]);
  }));
  await check('candidate audio is silent until exact reviewed handoff and reuses one synthesis',()=>withDirector({},async h=>{
    await enabled(h);const text='提前准备的声音。';h.director.warmSpeech('candidate-1',text,'warm');await flush();
    assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,0);assert.equal(h.texts.length,0);assert.equal(h.director.current,null);
    h.director.warmSpeech('candidate-1',text,'warm');assert.equal(h.calls.length,1);
    h.director.speak({id:301,text,mood:'warm',preparedAudioKey:'candidate-1'});await flush();
    assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,1);assert.equal(h.director.current.mode,'audio');assert.equal(h.director.warm,null);
    h.context.sources[0].end();assert.deepEqual(h.completed.map(e=>e.id),[301]);
  }));
  const decode=deferred();
  await check('in-flight candidate synthesis is adopted without a duplicate request or early playback',()=>withDirector({decode:()=>decode.promise},async h=>{
    await enabled(h);h.director.warmSpeech('pending','等这份音频。','warm');await flush();
    h.director.speak({id:302,text:'等这份音频。',mood:'warm',preparedAudioKey:'pending'});assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,0);
    decode.resolve({duration:4});await flush();assert.equal(h.context.sources.length,1);assert.equal(h.calls.length,1);
  }));
  await check('discarded, muted, expired and replaced warm candidates never start playback',async()=>{
    for(const action of ['discard','mute','pause','expire','replace']){
      const d=deferred();await withDirector({decode:()=>d.promise},async h=>{
        await enabled(h);h.director.warmSpeech('obsolete','旧候选。','warm');await flush();
        if(action==='discard')h.director.dropWarmSpeech('obsolete');
        if(action==='mute')await h.director.setEnabled(false);
        if(action==='pause')h.director.pause();
        if(action==='expire')step(30001);
        if(action==='replace')h.director.warmSpeech('new','新候选。','happy');
        d.resolve({duration:4});await flush();assert.equal(h.context.sources.length,0);assert.equal(h.director.current,null);
        assert(action==='replace'?h.director.warm?.key==='new':h.director.warm===null);
      });
    }
  });
  await check('failed warm synthesis uses complete text without silently paying for a retry',()=>withDirector({post:()=>new Response('{}',{status:502})},async h=>{
    await enabled(h);h.director.warmSpeech('failed','这句话仍然完整显示。','warm');await flush();
    h.director.speak({id:303,text:'这句话仍然完整显示。',mood:'warm',preparedAudioKey:'failed'});await flush();step(10000);
    assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,0);assert.deepEqual(h.completed.map(e=>e.id),[303]);assert.equal(h.texts.at(-1).text,'这句话仍然完整显示。');
  }));
  await check('real conversation engine reviews warmed audio, forwards its key, and continues without a second synthesis',()=>withDirector({},async h=>{
    await enabled(h);const e=new ConversationEngine();let firstGate,gatePayload,hosts=0;
    e.configureSpeech(h.director);e.configureAgent({cancel(){},async generate(p){
      if(p.kind==='review'){if(!firstGate&&hosts===1){gatePayload=p;return new Promise(r=>firstGate=r);}return reviewReply(p);}
      hosts++;return {action:'speak',speech:hosts===1?'先讲喜欢的小屋。':'接着聊怎么布置。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:9000,continuation:'continue',memory:'',agendaUpdates:[]};
    }},{enabled:true});
    e.setSpeechDriven(true);e.addEventListener('speak',({detail})=>h.director.speak(detail));h.director.addEventListener('complete',({detail})=>e.completeSpeech(detail.id));e.start();clearInterval(e._timer);
    try{
      const thinking=e._think();await flush();assert.equal(h.calls.length,1);assert.equal(h.context.sources.length,0);firstGate(reviewReply(gatePayload));await thinking;await flush();
      assert.equal(h.context.sources.length,1);assert.equal(h.calls.length,1);assert.equal(h.director.current.detail.preparedAudioKey,gatePayload.candidate?.id||gatePayload.context.candidate.id);
      e._maybePrepare();await flush();assert(e.prepared);assert.equal(h.calls.length,2);assert.equal(h.context.sources.length,1);
      h.context.sources[0].end();e._clock=e.nextWake;await e._think();await flush();assert.equal(h.context.sources.length,2);assert.equal(h.calls.length,2);assert.equal(h.director.current.text,'接着聊怎么布置。');
    }finally{e.destroy();}
  }));
  console.log(`\n${results.length} Vivi playback regression scenarios passed.`);
}finally{
  globalThis.setTimeout=original.setTimeout;globalThis.clearTimeout=original.clearTimeout;
  Object.defineProperty(globalThis,'performance',{configurable:true,value:original.performance});
}
