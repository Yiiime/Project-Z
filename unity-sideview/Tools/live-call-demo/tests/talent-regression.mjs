import {reviewReply} from './review-fixture.mjs';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {TalentDirector} from '../ui/talent-director.js';
import {SpeechDirector} from '../ui/speech-director.js';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {TALENT_CATALOG,validatePerformance} from '../ui/talent-config.js';
import {parseAgentReply,handleAgent,buildAgentRequest} from '../worker/agent-director.js';
import {fileURLToPath} from 'node:url';
import {buildSongAssets} from '../scripts/song-assets.mjs';

const flush=async()=>{for(let i=0;i<10;i++){await new Promise(r=>setTimeout(r,1));for(let j=0;j<20;j++)await Promise.resolve();}};
const seedBytes=await fs.readFile(new URL('../ui/audio/songs/wanan-yanrenzhong-vivi2-chorus.wav',import.meta.url));
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {resolve,promise};};
function buffer(duration=1,level=.1){const data=new Float32Array(Math.round(duration*1000)).fill(level);return {duration,length:data.length,sampleRate:1000,numberOfChannels:1,getChannelData:()=>data};}
function harness(options={}){
  const calls=[],sources=[],events=[],frames=new Map();let frameId=0;
  const context={currentTime:0,sampleRate:1000,resume:async()=>{},
    createBuffer:(channels,length)=>buffer(length/1000,0),decodeAudioData:async()=>buffer(2),
    createBufferSource:()=>{const source={connect(){},disconnect(){},start(at,offset){this.offset=offset;},stop(){this.stopped=true;},end(){this.onended?.();}};sources.push(source);return source;}};
  const speech={enabled:true,available:true,volume:.85,context,audioContext:()=>context,samples:new Float32Array(8),analyser:{getFloatTimeDomainData(a){a.fill(.1);}},
    async prepareAudio(text,mood,{signal}){calls.push({text,mood,signal});return options.prepare?options.prepare(text,signal):{buffer:buffer()};}};
  const director=new TalentDirector({speech,catalog:options.catalog||TALENT_CATALOG,fetchImpl:options.fetch||(async()=>new Response(seedBytes,{headers:{'Content-Type':'audio/wav'}})),
    requestFrame:fn=>{frames.set(++frameId,fn);return frameId;},cancelFrame:id=>frames.delete(id)});
  director.addEventListener('state',e=>events.push(e.detail));
  return {director,speech,context,calls,sources,events,advance(ms){context.currentTime+=ms/1000;const f=[...frames.values()];frames.clear();f.forEach(fn=>fn());}};
}
const script={title:'窗边的来信',scriptType:'original',lines:[{text:'信到了。',mood:'thinking',gesture:'chin',pauseAfterMs:200},{text:'原来是你写的。',mood:'happy',gesture:'present',pauseAfterMs:0}]};
const host={kind:'host',context:{activeSkill:null,host:{inbox:[]},mediaRuntime:{audioEnabled:true,ttsAvailable:true}}};
const decision=tool=>({action:'tool',speech:'我来演一小段。',mood:'warm',handledIds:[],wakeAfterMs:5000,memory:'',tool});

assert.throws(()=>parseAgentReply(decision({name:'start_performance',args:script}),host),e=>e.code==='capability_disabled');
assert.throws(()=>parseAgentReply(decision({name:'start_singing',args:{songId:'invented'}}),host));
assert.throws(()=>parseAgentReply(decision({name:'start_singing',args:{songId:TALENT_CATALOG.songs[0].id}}),{...host,context:{...host.context,mediaRuntime:{audioEnabled:false,ttsAvailable:true}}}));
assert.throws(()=>validatePerformance({...script,lines:[{...script.lines[0],gesture:'sampleM26'}]}));
assert.throws(()=>validatePerformance({...script,scriptType:'user_provided',sourceMessageId:'npc'},[{id:'npc',mine:false,text:'信到了。原来是你写的。'}]));
validatePerformance({...script,scriptType:'user_provided',sourceMessageId:'human'},[{id:'human',mine:true,text:'演一下：信到了。原来是你写的。'}]);
console.log('PASS host tools validate audio availability, script provenance, gesture permissions and unknown songs');

{
  const songs=TALENT_CATALOG.songs;
  assert.equal(songs.length,1);const song=songs[0];assert.equal(song.title,'晚安');assert.match(song.version,/颜人中/);
  const root=fileURLToPath(new URL('../',import.meta.url)).replace(/\/$/,'');
  const packaged=await buildSongAssets(root);const asset=packaged.assets[song.src];
  assert.equal(asset.type,'audio/wav');
  const wav=Buffer.from(asset.body,'base64');assert.equal(wav.toString('ascii',0,4),'RIFF');
  assert.equal(wav.readUInt32LE(24),24000);assert.equal(wav.readUInt32LE(40),wav.length-44);
  assert.equal(createHash('sha256').update(wav).digest('hex'),song.sha256);
  const approval=JSON.parse(await fs.readFile(path.join(root,song.reviewRef),'utf8'));
  assert.equal(approval.approved,true);assert.equal(approval.sha256,song.sha256);
  const request=buildAgentRequest({...host,context:{...host.context,publicEvents:[]}});
  assert.deepEqual(request.input.songLibrary.map(s=>s.id),[song.id]);
  assert.equal(parseAgentReply(decision({name:'start_singing',args:{songId:song.id}}),host).tool.args.songId,song.id);
  assert.throws(()=>parseAgentReply(decision({name:'start_singing',args:{songId:song.id}}),{...host,context:{...host.context,mediaRuntime:{audioEnabled:false}}}));
}
console.log('PASS sole reviewed WanAn asset is packaged byte-for-byte and available to autonomous host decisions');

{
  const h=harness(),engine=new ConversationEngine(),calls=[];
  engine.configureTalent(h.director);engine.setSpeechDriven(true);engine.start();
  engine.configureAgent({cancel(){},async generate(p){
    if(p.kind==='review')return reviewReply(p);
    calls.push(p);
    if(p.kind==='audience')return {status:'skip',items:[]};
    assert.equal(p.context.media.phase,'completed');
    assert(p.context.publicEvents.some(e=>e.type==='talent.completed'));
    return {...decision({name:'finish_activity',args:{taskId:p.context.taskId,cancel:false}}),
      speech:'你刚才说想聊明天的旅行，我们接着聊。',handledIds:p.context.host.inbox.map(m=>m.id)};
  }},{enabled:true});
  assert(engine._executeTool({name:'start_singing',args:{songId:TALENT_CATALOG.songs[0].id}}));
  await flush();engine._tick();await flush();assert.equal(h.director.current.phase,'playing');
  assert(!engine.facts.some(e=>e.type==='talent.completed'));assert.equal(h.calls.length,0);
  engine.sendMessage('唱完聊聊明天的旅行吧');assert.equal(engine.inbox.length,1);
  h.sources[0].end();await engine._think();
  assert.match(engine._active.text,/旅行/);assert.equal(calls.filter(p=>p.kind==='host').length,1);
  engine.completeSpeech(engine._active.id);
  assert.equal(engine._state.media,null);assert.equal(engine.inbox.length,0);assert.equal(h.sources.length,1);
  engine.destroy();
}
console.log('PASS real playback-end receipt wakes host with waiting messages; natural reply releases song without automatic replay');

{
 const h=harness();assert.equal(h.director.start('voice-performance',script),false);
 assert.equal(h.calls.length,0);assert.equal(h.sources.length,0);h.director.destroy();
}
console.log('PASS disabled voice performance does not synthesize or play audio');

const fixtureSong={id:'fixture',title:'测试音频',version:'test-v1',section:'测试片段',src:'/audio/songs/fixture.mp3',approved:true,aCappella:true,voiceIdentity:'z-vivi',voiceVersion:1,reviewRef:'test-only'};
{
  const catalog={...TALENT_CATALOG,songs:[fixtureSong]},h=harness({catalog});
  assert(h.director.start('singing',{songId:'fixture'}));await flush();assert.equal(h.director.current.phase,'ready');assert.equal(h.calls.length,0,'Songs never use TTS');
  await h.director.play();h.sources[0].end();assert.equal(h.director.current.phase,'completed');h.director.destroy();
  const missing=harness({catalog,fetch:async()=>new Response('missing',{status:404})});missing.director.start('singing',{songId:'fixture'});await flush();assert.equal(missing.director.current.phase,'failed');missing.director.destroy();
}
console.log('PASS prerecorded song playback and missing-file failure without fake TTS singing');

{
  const h=harness(),engine=new ConversationEngine();engine.configureTalent(h.director);engine.setSpeechDriven(true);engine.start();
  assert(engine._executeTool({name:'start_singing',args:{songId:TALENT_CATALOG.songs[0].id}}));await flush();
  engine._say('等我把这句话说完。','warm','none');engine._tick();assert.equal(h.sources.length,0);
  engine.completeSpeech(engine._active.id);engine._tick();await flush();assert.equal(h.sources.length,1);
  assert.equal(engine._executeTool({name:'start_drawing',args:{subject:'cat',accessory:'bow'}}),false);
  engine._say('这句话排到后面。','warm','none');assert.equal(engine._active,null);
  h.sources[0].end();engine._tick();assert.equal(engine._active.text,'这句话排到后面。');
  assert.equal(engine.facts.filter(e=>e.type==='talent.completed').length,1);
  engine.replay();assert.equal(engine._state.media,null);engine.destroy();
}
console.log('PASS conversation audio mutex, speech boundary start, foreground task exclusion and reset');

{
  const h=harness(),engine=new ConversationEngine();engine.configureTalent(h.director);engine.setSpeechDriven(true);engine.start();
  engine._executeTool({name:'start_singing',args:{songId:TALENT_CATALOG.songs[0].id}});await flush();engine._tick();await flush();engine.sendMessage('停止表演');
  const taskId=engine._state.media.id;
  engine.configureAgent({cancel(){},async generate(p){if(p.kind==='review')return reviewReply(p);return {...decision({name:'finish_activity',args:{taskId,cancel:true}}),handledIds:[engine.inbox[0].id],speech:'好，先停在这里。'};}},{enabled:true});
  await engine._think();assert(h.sources[0].stopped);assert.equal(engine._active.text,'好，先停在这里。');assert(!engine.facts.some(e=>e.type==='talent.completed'));engine.destroy();
}
console.log('PASS agent-directed stop executes before host response without claiming completion');

{
  let subtitleMismatch=false;
  const director=new SpeechDirector({fetchImpl:async(url,init)=>{
    const body=JSON.parse(init.body);const records=[{type:'start',requestId:body.requestId,format:'mp3',source:'volcengine',speaker:'zh_female_vv_uranus_bigtts'},
      {type:'audio',requestId:body.requestId,index:0,data:'AQI='},{type:'sentence',requestId:body.requestId,sentence:{words:[{word:subtitleMismatch?'错误台词':body.text}]}},{type:'done',requestId:body.requestId}];
    return new Response(records.map(r=>JSON.stringify(r)).join('\n'),{headers:{'Content-Type':'application/x-ndjson'}});
  }});
  director.enabled=true;director.available=true;director.audioContext=()=>({decodeAudioData:async()=>buffer()});
  const out=await director.prepareAudio('不要把动作提示读出来。','warm');assert.equal(out.buffer.duration,1);assert.equal(director.current,null);
  subtitleMismatch=true;await assert.rejects(()=>director.prepareAudio('正确台词','warm'),/transcript_unverified/);assert.equal(director.current,null);
  director.available=false;await assert.rejects(()=>director.prepareAudio('你好','warm'));director.destroy();
}
console.log('PASS isolated preparation reuses TTS transport, requires subtitle evidence and never starts chat fallback');

{
  const req=new Request('https://demo.test/api/agent',{method:'POST',headers:{Origin:'https://demo.test','Content-Type':'application/json','oai-authenticated-user-id':'talent-test'},body:JSON.stringify({requestId:crypto.randomUUID(),kind:'host',eventId:'wake',message:'决定下一步',context:{phase:'chat',mediaRuntime:{audioEnabled:true,ttsAvailable:true},host:{inbox:[],memory:''}}})});
  const response=await handleAgent(req,{DEEPSEEK_API_KEY:'fixture'},{fetch:async()=>Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(decision({name:'start_singing',args:{songId:TALENT_CATALOG.songs[0].id}}))}}]})});
  assert.equal(response.status,200);assert.equal((await response.json()).tool.name,'start_singing');
  const playing={...host,context:{...host.context,taskId:'task',activeSkill:'singing',media:{phase:'playing'}}};
  assert.throws(()=>parseAgentReply({...decision(null),action:'speak',tool:undefined},playing));
  assert.equal(parseAgentReply(decision({name:'finish_activity',args:{taskId:'task',cancel:true}}),playing).tool.args.cancel,true);
}
{
  const root=await fs.mkdtemp(path.join(os.tmpdir(),'z-song-test-'));
  try{
    await fs.mkdir(path.join(root,'ui/audio/songs'),{recursive:true});const bytes=Buffer.from('test only, not a real song');
    await fs.writeFile(path.join(root,'ui/audio/songs/fixture.mp3'),bytes);
    const catalog={...TALENT_CATALOG,songs:[{...fixtureSong,sha256:createHash('sha256').update(bytes).digest('hex')}]};
    const result=await buildSongAssets(await fs.realpath(root),catalog);assert(result.assets[fixtureSong.src]);
    catalog.songs[0].sha256='changed';const realRoot=await fs.realpath(root);await assert.rejects(()=>buildSongAssets(realRoot,catalog));
  }finally{await fs.rm(root,{recursive:true,force:true});}
}
console.log('PASS host API and reviewed song asset packaging (offline fixtures only)');
