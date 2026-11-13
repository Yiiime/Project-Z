import {test} from 'node:test';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {ArtifactStore,loadEnvironment,Providers} from '../server/providers.mjs';
import {LiveSession} from '../server/session.mjs';
import {normalizeAgentInput,parseAgentReply,buildAgentRequest} from '../server/shared/worker/agent-director.js';
const frame=()=>new Promise(r=>setImmediate(r));
async function fixture(t,memory=null){const root=fs.mkdtempSync(path.join(os.tmpdir(),'z-unity-test-'));const providers={env:{},config:async()=>({enabled:false}),generate:async()=>({action:'wait',speech:'',mood:'neutral',handledIds:[],wakeAfterMs:120000,memory:''})};const s=new LiveSession({root:path.join(root,'sessions'),memory,providers,artifacts:new ArtifactStore(path.join(root,'artifacts')),songRoot:fileURLToPath(new URL('../server/assets/songs',import.meta.url))});await s.initialize();clearInterval(s.engine._timer);t.after(()=>{s.close();fs.rmSync(root,{recursive:true,force:true});});s.control(envelope(s,{action:'resume'}));return s;}
const envelope=(s,extra={})=>({protocolVersion:1,sessionId:s.id,epoch:s.epoch,...extra});
const receipt=(s,c,status,extra={})=>s.receipt(envelope(s,{executorLeaseId:s.lease,commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{...Object.fromEntries(['artifactId','playbackId','speechId','stage','round','view','modality'].filter(k=>c.payload[k]!==undefined).map(k=>[k,c.payload[k]])),status,...extra}}));
const latest=(s,type)=>[...s.commands.values()].filter(c=>c.type===type).at(-1);

test('preparing talent snapshot is accepted by the actual agent input contract',async t=>{
 const s=await fixture(t);
 s.talent.current={id:crypto.randomUUID(),kind:'singing',title:'晚安 · 副歌',phase:'preparing'};
 s.talent.changed();
 const context=JSON.parse(JSON.stringify(s.engine._hostContext([])));
 assert.equal(context.media.playbackId,null);assert.equal(context.media.artifactId,null);
 assert.doesNotThrow(()=>normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'preparing-talent',kind:'host',message:'决定下一步',context}));
});

test('host completion requires actual execution; interruption never publishes completed speech',async t=>{const s=await fixture(t);s.engine._say('你好，今天怎么样？','warm','none',{source:'deepseek'});await frame();const c=latest(s,'host.present');assert(c);assert(!s.engine.facts.some(e=>e.type==='speech.completed'));assert.throws(()=>receipt(s,c,'completed',{positionMs:2000}),/invalid_transition/);receipt(s,c,'started',{durationMs:2000,positionMs:0});receipt(s,c,'progress',{positionMs:500,text:'你好'});assert(s.engine.facts.some(e=>e.type==='speech.progress'&&e.text==='你好'));receipt(s,c,'interrupted',{reason:'viewer_stop'});assert(!s.engine.facts.some(e=>e.type==='speech.completed'));assert.equal(s.engine._active,null);});

test('repeating disabled audio capabilities does not interrupt a text-only host line',async t=>{
 const s=await fixture(t);assert.equal(s.capabilities.audioEnabled,false);
 s.engine._say('晚上好，欢迎来到小屋。','warm','none',{source:'deepseek'});await frame();
 const c=latest(s,'host.present');receipt(s,c,'started',{durationMs:2000,positionMs:0});
 s.input(envelope(s,{type:'client.capabilities_changed',payload:{audioEnabled:false}}));
 assert.equal(c.status,'started');assert(s.engine._active);
 receipt(s,c,'progress',{positionMs:2000,text:c.payload.text});receipt(s,c,'completed',{positionMs:2000});
 assert(s.engine.facts.some(e=>e.type==='speech.completed'&&e.text===c.payload.text));
});

test('server restart changes epoch and invalidates old execution receipts',async t=>{const s=await fixture(t);s.engine._say('这句还没说完','warm','none',{source:'deepseek'});await frame();const c=latest(s,'host.present');const saved=s.persistData();const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());assert.equal(restored.epoch,s.epoch+1);assert(!restored.engine.facts.some(e=>e.type==='speech.completed'));assert.equal(restored.commands.get(c.commandId).status,'interrupted');assert.throws(()=>restored.receipt(envelope(s,{executorLeaseId:s.lease})),/stale_epoch/);});

test('song must really finish and be released before another talent can start',async t=>{
 const s=await fixture(t);s.speech.enabled=true;s.speech.volume=1;
 assert(s.engine._executeTool({name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}));
 await frame();const prepare=latest(s,'media.prepare');assert(prepare);receipt(s,prepare,'ready',{durationMs:32284});assert(s.talent.play());
 const play=latest(s,'media.play');receipt(s,play,'started',{durationMs:32284,positionMs:0});
 assert.throws(()=>receipt(s,play,'completed',{positionMs:1000}),/not_finished/);
 receipt(s,play,'progress',{positionMs:31000,lineIndex:0});receipt(s,play,'completed',{positionMs:32284});
 const nextSong={name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}};
 assert.equal(s.engine._executeTool(nextSong),false);
 assert(s.engine._executeTool({name:'finish_activity',args:{taskId:play.taskId,cancel:false}}));
 assert.equal(s.engine._state.media,null);assert.equal(latest(s,'activity.release').taskId,play.taskId);
 assert(s.engine._executeTool(nextSong));
});

test('character waiting follows agent reply state without imposing a schedule',async t=>{
 const s=await fixture(t);
 for(const status of ['idle','seen','pending','queued','replying']){
  s.engine._state.replyStatus=status;
  assert.equal(s.snapshot().performance.waiting,['pending','queued'].includes(status));
 }
});

test('call input cancels current host execution and hanging up invalidates guest context',async t=>{
 const s=await fixture(t);s.engine.enabled=true;
 s.engine._say('这句话正在播放','warm','none',{source:'deepseek'});await frame();
 const speech=latest(s,'host.present');receipt(s,speech,'started',{durationMs:2000,positionMs:0});
 s.input(envelope(s,{type:'call.join',payload:{}}));
 assert.equal(speech.status,'interrupted');assert(latest(s,'execution.cancel'));
 const version=s.snapshot().call.version;
 s.input(envelope(s,{type:'call.input',payload:{state:'listening'}}));assert(s.snapshot().call.version>version);
 assert.equal(s.snapshot().call.input,'listening');
 s.input(envelope(s,{type:'viewer.message',payload:{text:'刚才的故事想继续听'}}));
 assert.equal(s.snapshot().call.turns,1);assert.equal(s.snapshot().call.input,'idle');
 assert(s.engine.inbox.some(m=>m.text==='刚才的故事想继续听'));
 assert(!s.snapshot().room.messages.some(m=>m.text==='刚才的故事想继续听'));
 assert(s.engine.facts.some(f=>f.type==='player.message'&&f.text==='刚才的故事想继续听'));
 s.input(envelope(s,{type:'call.end',payload:{}}));assert.equal(s.snapshot().call.active,false);assert.deepEqual(s.engine.inbox,[]);
 assert(!s.snapshot().room.messages.some(m=>m.text==='刚才的故事想继续听'));
 assert(!s.engine.facts.some(f=>f.type==='speech.completed'));
});

test('pause cancels a listening turn and end prevents delayed inputs',async t=>{
 const s=await fixture(t);s.engine.enabled=true;s.input(envelope(s,{type:'call.join',payload:{}}));
 s.input(envelope(s,{type:'call.input',payload:{state:'listening'}}));const version=s.snapshot().call.version;
 s.control(envelope(s,{action:'pause'}));assert(s.snapshot().call.version>version);assert.equal(s.snapshot().call.input,'idle');
 s.control(envelope(s,{action:'end'}));assert.equal(s.snapshot().call.active,false);
 assert.throws(()=>s.input(envelope(s,{type:'viewer.message',payload:{text:'迟到语音'}})),/session_paused/);
});

test('call waits for the host turn and suspension hangs up before accepting more audio',async t=>{
 const s=await fixture(t);s.engine.enabled=true;s.input(envelope(s,{type:'call.join',payload:{}}));
 s.engine.callNeedsTurn=true;assert.equal(s.snapshot().call.busy,true);
 s.engine.callNeedsTurn=false;s.engine.pending=null;s.engine._queue=[];s.engine._continuations=[];assert.equal(s.snapshot().call.busy,false);
 const version=s.engine.callVersion;s.control(envelope(s,{action:'pause'}));assert.equal(s.snapshot().call.active,false);assert(s.engine.callVersion>version);
 s.control(envelope(s,{action:'resume'}));assert.equal(s.snapshot().call.active,false);
});


test('local service reads .env and gives local overrides precedence',t=>{
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'z-env-test-'));t.after(()=>fs.rmSync(root,{recursive:true,force:true}));
 const keys=['DEEPSEEK_API_KEY','VOLC_TTS_API_KEY','VOLC_ASR_API_KEY','NETEASE_SESSION_FILE','FFMPEG_PATH','Z_NETEASE_AUTO_RENEW','Z_SONG_CATALOG'];const saved=Object.fromEntries(keys.map(k=>[k,process.env[k]]));
 t.after(()=>{for(const k of keys)if(saved[k]===undefined)delete process.env[k];else process.env[k]=saved[k];});for(const k of keys)delete process.env[k];
 fs.writeFileSync(path.join(root,'.env'),'DEEPSEEK_API_KEY="test-text"\nVOLC_TTS_API_KEY=test-voice\nexport VOLC_ASR_API_KEY=test-asr # local\n');
 assert.deepEqual(loadEnvironment(root),{DEEPSEEK_API_KEY:'test-text',VOLC_TTS_API_KEY:'test-voice',VOLC_ASR_API_KEY:'test-asr',NETEASE_SESSION_FILE:'',FFMPEG_PATH:'',Z_NETEASE_AUTO_RENEW:'',Z_SONG_CATALOG:''});
 fs.writeFileSync(path.join(root,'.env.local'),'DEEPSEEK_API_KEY=test-override\nNETEASE_SESSION_FILE=/fixture/private-session.json\nFFMPEG_PATH=/fixture/ffmpeg\nZ_NETEASE_AUTO_RENEW=0\n');assert.equal(loadEnvironment(root).DEEPSEEK_API_KEY,'test-override');
 assert.equal(loadEnvironment(root).NETEASE_SESSION_FILE,'/fixture/private-session.json');assert.equal(loadEnvironment(root).FFMPEG_PATH,'/fixture/ffmpeg');assert.equal(loadEnvironment(root).Z_NETEASE_AUTO_RENEW,'0');
 process.env.DEEPSEEK_API_KEY='test-process';assert.equal(loadEnvironment(root).DEEPSEEK_API_KEY,'test-process');
});


test('voice entitlement failures reach the native status without provider payloads',async()=>{
 const providers=new Providers({VOLC_TTS_API_KEY:'test-only'},{fetch:async()=>new Response('data: '+JSON.stringify({code:45000030,message:'provider-private-details'})+'\n\n',{headers:{'Content-Type':'text/event-stream'}})});
 await assert.rejects(providers.synthesize('test-owner','晚上好','warm'),{message:'tts_provider_resource_not_granted'});
 assert.equal(providers.voiceError,'tts_provider_resource_not_granted');
});

const retiredTools=['start_tarot','choose_tarot','start_drawing','edit_drawing','set_panel','start_performance'];
test('retired talent tools cannot run in the room or call; chat remains available',async t=>{
 const s=await fixture(t);s.engine.enabled=true;
 for(const call of [false,true]){
  if(call)assert(s.engine.joinCall());
  for(const name of retiredTools)assert.equal(s.engine._executeTool({name,args:{}}),false,name);
  assert.equal(s.talent.start('voice-performance',{title:'test',lines:[]}),false);
  assert(s.engine.sendMessage('我们聊聊今天吧'));
  assert.equal(s.snapshot().activeTask,null);assert.equal(s.snapshot().recentArtwork,null);assert.equal(s.snapshot().tarot,null);
 }
 assert(![...s.commands.values()].some(c=>['drawing.present','tarot.present'].includes(c.type)));
 for(const type of ['tarot.selection','tarot.pause','panel.intent','activity.finish'])assert.throws(()=>s.input(envelope(s,{type,payload:{taskId:'old'}})),/state_conflict/);
});

test('host tool contract and advertised capabilities only expose singing',async t=>{
 const s=await fixture(t),context=normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'test',kind:'host',context:s.engine._hostContext([])}).context;
 const reply={action:'tool',speech:'我试试。',mood:'warm',handledIds:[],wakeAfterMs:2000,memory:''};
 for(const name of retiredTools)assert.throws(()=>parseAgentReply({...reply,tool:{name,args:{}}},{kind:'host',context}));
 for(const kind of ['artwork','talent','decision'])assert.throws(()=>normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'test',kind,context:{phase:'chat'}}));
 const host=buildAgentRequest({kind:'host',context});
 assert.deepEqual(host.input.capabilities.map(c=>c.id).sort(),['audience-reaction','live-session-control','singing']);
 assert(!retiredTools.some(name=>host.system.includes(name)));
 const audience=buildAgentRequest(normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'event',kind:'audience',context:{phase:'chat',publicEvents:[]}}));
 assert.deepEqual(audience.input.capabilities.map(c=>c.id),['singing']);
 assert(!audience.system.includes('戴围巾的小猫'));
 // Wording is generated, not rejected by a blacklist in the output parser.
 assert.equal(parseAgentReply({...reply,action:'speak',speech:'我想想，唱给你听。'},{kind:'host',context}).speech,'我想想，唱给你听。');
});

test('old activities and unfinished voice performances never resume after restart',async t=>{
 const s=await fixture(t);
 for(const kind of ['tarot','drawing','voice-performance']){
  const saved=s.persistData();saved.engine=structuredClone(saved.engine);
  saved.engine.state.play={active:kind==='voice-performance'?null:kind,[kind]:{id:'old',open:true,phase:'preparing'},queue:[{kind}],history:[]};
  saved.engine.memory='an obsolete talent promise';saved.engine.session.items=[{id:'old',status:'open',sources:[]}];
  saved.engine.media=kind==='voice-performance'?{id:'old',kind,phase:'preparing'}:null;
  const restored=new LiveSession({id:crypto.randomUUID(),root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());
  const state=restored.snapshot();assert.equal(state.activeTask,null);assert.equal(state.tarot,null);assert.equal(state.recentArtwork,null);assert.deepEqual(state.panels,{tarot:false,drawing:false});
  assert.equal(restored.engine.memory,'');assert.deepEqual(restored.engine.session.items,[]);
  assert(!state.pendingCommands.some(c=>['drawing.present','tarot.present','media.prepare','media.play'].includes(c.type)));
 }
});

test('five minutes of audience display receipts follow room, call and singing pace',async t=>{
 const s=await fixture(t),e=s.engine,a=e.audience,frameTime=160;
 const policy=await new Providers({DEEPSEEK_API_KEY:'fixture-only'}).config(s.id);
 let serial=0,showing=null;
 const displays=[],phases=new Set();
 const client={generate:async p=>{
   normalizeAgentInput(p);
   if(p.kind==='review')return {decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:policy.review.version};
   assert.equal(p.kind,'audience');phases.add(p.context.phase);
   const npc=p.context.audience.eligibleNpcIds[0];
   return {status:'ready',items:[{npc_id:npc,text:'今晚的歌单 '+(++serial),intent:'atmosphere',evidence_refs:[p.eventId]}]};
 },cancel(){}};
 e.configureAgent(client,{...policy,singingBackground:false});a.random=()=>0.5;
 s.on('event',event=>{if(event.type==='audience.present'){assert.equal(showing,null);showing=s.commands.get(event.commandId);}});
 for(let ms=0;ms<300000;ms+=frameTime){
   e._clock=ms;
   if(ms===120000)assert(e.joinCall());
   if(ms===180000){e._state.media={kind:'singing',phase:'playing'};e._public('talent.playing','点的歌开始播放了。');}
   if(ms===216000){e._state.media=null;e._public('talent.completed','这首歌唱完了。');assert(e.endCall());}
   if(showing){
     assert(!e.facts.some(f=>f.id===showing.payload.messageId),'generation alone is not public');
     receipt(s,showing,'displayed');displays.push([ms,showing.payload.npcId]);showing=null;
   }
   a.tick();await frame();
 }
 const period=(a,b)=>displays.filter(([t])=>a<=t&&t<b),count=(a,b)=>period(a,b).length;
 const since=new Map();for(const [when,npc] of displays){assert(when-(since.get(npc)??-Infinity)>=20000);since.set(npc,when);}
 const sample={opening:count(0,15000),chatMinute:count(60000,120000),callMinute:count(120000,180000),song36s:count(180000,216000),total:displays.length,names:since.size};
 t.diagnostic(JSON.stringify(sample));assert.deepEqual([...phases].sort(),['call','chat','opening','performing']);
 assert(sample.opening>=3&&sample.opening<=4);assert(sample.chatMinute>=12&&sample.chatMinute<=18);
 assert(sample.callMinute>=8&&sample.callMinute<=12);assert(sample.song36s>=8&&sample.song36s<=12);
 assert.equal(sample.names,7);assert(new Set(period(60000,120000).map(x=>x[1])).size>=5);
 assert(!e.inbox.some(m=>!m.mine));
 const before=serial;s.control(envelope(s,{action:'end'}));e._clock+=30000;a.tick();await frame();assert.equal(serial,before);
});


test('daytime recap opens on a host turn with no chat, is saved only on delivery and survives refresh/restart',async t=>{
 const memory={worldId:'a'.repeat(32),runId:'b'.repeat(32),day:2,today:['上午在咖啡店附近和岛上的朋友说了会儿话']};
 const s=await fixture(t,memory);s.engine._tick();await frame();
 const first=latest(s,'host.present');assert.match(first.payload.text,/晚上好/);assert.doesNotMatch(first.payload.text,/咖啡店|朋友/);
 assert(!s.engine.facts.some(e=>['player.message','npc.message'].includes(e.type)));
 assert(!s.broadcast.recapSpoken);receipt(s,first,'interrupted',{reason:'aborted'});
 assert(s.broadcast.opening());s.engine._clock+=5000;s.engine._tick();await frame();
 const c=latest(s,'host.present');assert.notEqual(first.commandId,c.commandId);
 receipt(s,c,'started',{durationMs:2000,positionMs:0});receipt(s,c,'completed',{positionMs:2000});
 assert(s.broadcast.recapSpoken);assert.equal(s.broadcast.pending.filter(r=>r.recapDay===2).length,1);
 s.broadcast.refresh(memory);s.engine._tick();assert.equal(latest(s,'host.present').commandId,c.commandId);
 const restored=new LiveSession({root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved:s.persistData()});
 t.after(()=>restored.close());assert.equal(restored.broadcast.opening(),null);
});
test('an empty day stays quiet; a late daytime checkpoint schedules its own host turn',async t=>{
 const m={worldId:'a'.repeat(32),runId:'b'.repeat(32),day:1};
 const s=await fixture(t,m);s.engine._tick();assert(!latest(s,'host.present'));
 s.heartbeat(envelope(s,{executorLeaseId:s.lease,memory:{...m,today:['和岛上的朋友说了会儿话']}}));
 s.engine._tick();await frame();assert.match(latest(s,'host.present').payload.text,/晚上好/);
});

test('a verified outing leaves a quiet beat before the host brings up her song without chat',async t=>{
 const memory={worldId:'a'.repeat(32),runId:'b'.repeat(32),day:1,
   today:['和岛上的朋友一起去看了地图上还没有标出来的地方','聊到了咖啡拉花'],songs:[{title:'夏夜最后的烟火',sourceId:'p'}]};
 const s=await fixture(t,memory);await frame();
 const e=s.engine,first=latest(s,'host.present');
 assert.match(first.payload.text,/地图/);assert.doesNotMatch(first.payload.text,/咖啡|歌/);
 receipt(s,first,'started',{durationMs:2000,positionMs:0});receipt(s,first,'completed',{positionMs:2000});
 e._clock+=10000;e._tick();await frame();assert.equal(latest(s,'host.present'),first);
 e._clock+=4500;e._tick();await frame();const song=latest(s,'host.present');
 assert.match(song.payload.text,/还答应学一首歌.*夏夜最后的烟火/);
 assert(!e.facts.some(f=>['npc.message','player.message'].includes(f.type)));
});

test('arrival tools pass the real parser and executor only after presentation and player confirmation',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'arrival',playerName:'小海',worldId:'a'.repeat(32),day:1});s.engine.liveScene=s.scene;
 const value=()=>({kind:'host',context:{...normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'arrival',kind:'host',context:s.engine._hostContext([])}).context,scene:s.scene.binding()}});
 const parsed=(name,args={})=>parseAgentReply({action:'tool',speech:'模型生成的本轮主持内容',mood:'warm',handledIds:[],wakeAfterMs:2000,memory:'',tool:{name,args}},value());
 assert.throws(()=>parsed('depart_island'));assert.throws(()=>parsed('draw_guest'));
 for(let i=0;i<3;i++){
  s.engine._say('今天海边的风很舒服，你们今天过得怎么样？','warm','nod',{source:'deepseek',transition:'smooth'});await frame();
  const host=latest(s,'host.present');s.engine._onHostSpeechCommitted(host.payload.speechId,{virtualAudience:[{name:'海风'+i,text:'刚下班，来听你聊聊天'},{name:'月光'+i,text:'今天也有被海风治愈'}]});
  receipt(s,host,'started',{durationMs:2000,positionMs:0});receipt(s,host,'completed',{positionMs:2000});
  for(const comment of [...s.commands.values()].filter(c=>c.type==='virtual_audience.present'&&c.status==='sent'))receipt(s,comment,'displayed');
 }
 assert.equal(s.scene.warmup.speeches,3);assert.equal(s.scene.warmup.messages,6);assert.doesNotThrow(()=>parsed('draw_guest'));
 s.engine._clock+=45000;
 const draw=parsed('draw_guest');assert(s.engine._executeTool(draw.tool));
 let c=latest(s,'story.present');assert.equal(c.expiresAt,null);assert.equal(s.scene.phase,'opening');s.maintenance();assert.equal(c.status,'sent');assert.throws(()=>receipt(s,c,'displayed'),/invalid_draw_evidence/);receipt(s,c,'displayed',{playerActivated:true,playerName:'小海'});assert.equal(s.scene.phase,'drawn');
 assert(s.engine._executeTool(parsed('offer_arrival',{title:'现场邀请',description:'七天的小岛生活'}).tool));
 c=latest(s,'story.present');receipt(s,c,'displayed');assert.equal(s.scene.phase,'offered');
 assert.throws(()=>parsed('depart_island'));
 s.input(envelope(s,{type:'story.accept_arrival',payload:{}}));assert.equal(s.scene.phase,'accepted');
 assert(s.engine._executeTool(parsed('depart_island').tool));receipt(s,latest(s,'story.present'),'displayed');assert.equal(s.scene.phase,'departed');
 assert.equal(s.snapshot().publicEvents.filter(e=>e.type==='npc.message').length,6);
 const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved:s.persistData()});t.after(()=>restored.close());assert.equal(restored.snapshot().story.phase,'departed');
});

test('onboarding has no timed program and a generated farewell is retained as subtitle fallback',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 for(const mode of ['arrival','first_evening']){
  s.scene=new LiveScene(s,{mode,playerName:'小海',worldId:'a'.repeat(32),day:1});s.engine.liveScene=s.scene;
  s.sessionControl.data.startedAt=Date.now()-3600000;s.sessionControl.data.targetAt=Date.now()-600000;s.sessionControl.data.hardAt=Date.now()-300000;
  s.sessionControl.tick();assert(s.sessionControl.normal);assert(!s.sessionControl.snapshot().timing);
 }
 s.sessionControl.data.phase='farewell';s.sessionControl.data.farewellText='这句来自本场模型';s.sessionControl.audioDisabled();await frame();assert.equal(latest(s,'host.present').payload.text,'这句来自本场模型');
});


test('virtual viewers wait for reviewed host playback and drop interrupted reactions',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'arrival',playerName:'小海',worldId:'a'.repeat(32),day:1});s.engine.liveScene=s.scene;
 const reaction={virtualAudience:[{name:'海风',text:'七天听起来真不错'}]};
 s.engine._onHostSpeechCommitted(1,reaction);
 assert.equal(latest(s,'virtual_audience.present'),undefined);
 s.engine._public('speech.interrupted','未播完',{speechId:'1'});
 assert.equal(latest(s,'virtual_audience.present'),undefined);
 s.engine._onHostSpeechCommitted(2,reaction);
 s.engine._public('speech.completed','来岛上住七天',{speechId:'2',speakerId:'Z'});
 assert.equal(latest(s,'virtual_audience.present').payload.text,'七天听起来真不错');
 const count=s.commands.size;s.engine._public('speech.completed','重复回执',{speechId:'2',speakerId:'Z'});
 assert.equal(s.commands.size,count);
});


test('visible virtual chat enters the actual host inbox and invalidates stale planning',async t=>{
 const s=await fixture(t);s.engine.topicProgress={status:'complete',reason:'old',nextDirection:'',anchorSpeechId:'1',evidenceIds:['old-event']};
 const c=s.command('virtual_audience.present',{messageId:'virtual-message',name:'海盐汽水',text:'今天过得怎么样？'});
 receipt(s,c,'displayed');
 assert.equal(s.engine.topicProgress,null);
 const inbox=JSON.parse(JSON.stringify(s.engine._hostContext(s.engine.inbox)));
 assert.equal(inbox.host.inbox[0].id,'virtual-message');
 assert.equal(inbox.host.inbox[0].text,'今天过得怎么样？');
 assert.doesNotThrow(()=>normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'visible-chat',kind:'host',message:'回应弹幕',context:inbox}));
 assert.equal(s.engine.session.evidence().filter(e=>e.id==='virtual-message').length,1);
 receipt(s,c,'displayed');assert.equal(s.engine.inbox.length,1);
});


test('host reply to a virtual viewer remains valid for the next real decision',async t=>{
 const s=await fixture(t),id=crypto.randomUUID(),speaker='virtual_'+id;
 const message=s.engine._message('今天怎么样？',{id,name:'晚风',type:'chat'});s.engine._receive(message,false,speaker,{recipientIds:['z']});
 s.engine._public('speech.completed','晚上好！',{id:'speech-1',speakerRole:'host',speakerId:'Z',speechId:'1',replyTo:[id]});
 const context=JSON.parse(JSON.stringify(s.engine._hostContext(s.engine.inbox)));
 assert.deepEqual(context.publicEvents.at(-1).recipientIds,[speaker]);
 assert.doesNotThrow(()=>normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'reply-virtual',kind:'host',message:'继续',context}));
 context.publicEvents.at(-1).recipientIds=['virtual_untrusted'];assert.throws(()=>normalizeAgentInput({requestId:crypto.randomUUID(),kind:'host',context}));
});

test('topic assessment sends public inbox fields without internal delivery state',async t=>{
 const s=await fixture(t),id=crypto.randomUUID();
 s.engine._receive(s.engine._message('聊聊海边吧',{id,name:'海风',type:'chat'}),false,'virtual_'+id,{recipientIds:['z']});
 s.engine.enabled=true;s.engine.topicEnabled=true;s.engine._active={id:12,text:'海风吹过来了。'};
 s.engine.speechPlan={speechId:12,result:{action:'speak',continuation:'continue',handledIds:[]}};
 let payload;s.engine.client={cancel(){},generate:async p=>{payload=p;normalizeAgentInput(p);return {status:'expanding',reason:'回应观众',nextDirection:'',evidenceIds:[id],anchorSpeechId:'12'};}};
 s.engine._maybeAssessTopic();await frame();
 assert.equal(payload.kind,'topic');assert(!('pendingRecipientIds' in payload.context.inbox[0]));assert.equal(s.engine.topicProgress.status,'expanding');
 s.engine._active=null;s.engine.speechPlan=null;
});

test('first evening virtual audience publishes alongside resident lane, and close waits for song',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'first_evening',playerName:'小海',worldId:'a'.repeat(32),day:1});s.engine.liveScene=s.scene;
 s.engine._onHostSpeechCommitted(1,{virtualAudience:[{name:'海风',text:'今天认识了新朋友呀'}]});
 s.engine._public('speech.completed','真实播放完成',{speechId:'1'});
 assert.equal(latest(s,'virtual_audience.present').payload.name,'海风');
 s.sessionControl.data.eveningStartClock=0;s.engine._clock=140000;
 s.sessionControl.tick();assert(s.sessionControl.normal,'do not cut off the song before its completion receipt');
 s.speech.enabled=true;s.speech.volume=1;
 assert(s.engine._executeTool({name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}));await frame();
 receipt(s,latest(s,'media.prepare'),'ready',{durationMs:32284});assert(s.talent.play());
 const play=latest(s,'media.play');receipt(s,play,'started',{durationMs:32284,positionMs:0});
 receipt(s,play,'progress',{positionMs:31000,lineIndex:0});receipt(s,play,'completed',{positionMs:32284});
 assert(s.sessionControl.data.eveningSongComplete);assert.equal(s.engine._state.media.phase,'completed');s.engine._active=null;
 s.sessionControl.tick();assert.equal(s.sessionControl.data.phase,'closing');
});


test('showcase limits audience selection and cues the song after a minute unless the player is waiting',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'first_evening',playerName:'小海',worldId:'a'.repeat(32),day:1});s.engine.liveScene=s.scene;
 s.sessionControl.data.eveningStartClock=0;
 const inbox=Array.from({length:12},(_,i)=>({id:'n'+i,text:'公开弹幕'+i,mine:false,recipientIds:['z']}));
 inbox.unshift({id:'player1',text:'我还有个问题',mine:true,recipientIds:['z']});
 assert.deepEqual(s.engine._hostContext(inbox).host.inbox.map(m=>m.id),['player1','n9','n10','n11']);
 assert(s.engine._wakeDelay({wakeAfterMs:9000,continuation:'leave_space'})<=2000);
 s.engine._clock=59999;assert.equal(s.scene.binding().songDue,false);
 s.engine._clock=60000;s.engine.inbox=[inbox[0]];assert.equal(s.scene.binding().songDue,false);
 s.engine.inbox=[];assert.equal(s.scene.binding().songDue,true);
 s.sessionControl.data.lastPlayerClock=58000;assert.equal(s.scene.binding().songDue,false);
 s.engine._clock=63000;assert.equal(s.scene.binding().songDue,true);
 s.sessionControl.tick();assert.equal(s.sessionControl.data.eveningSongRequested,true);
});
test('lottery animation starts on the announcement playback receipt without marking the draw complete',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'arrival',playerName:'小海',worldId:'b'.repeat(32),day:1});s.engine.liveScene=s.scene;
 s.scene.warmup.speeches=2;s.scene.warmup.messages=2;
 s.engine._say('我想给大家一个惊喜。','warm','none',{source:'deepseek'});await frame();
 s.engine.speechPlan={result:{action:'tool',tool:{name:'draw_guest',args:{}}}};
 const host=latest(s,'host.present');assert.equal(latest(s,'story.present'),undefined);
 receipt(s,host,'started',{durationMs:2000,positionMs:0});assert.equal(latest(s,'story.present').payload.tool,'draw_guest');
 assert.equal(s.scene.phase,'opening');assert.equal(s.scene.completedSpeeches.size,0);
});

test('first evening host and reviewer share already-public daytime memories before retelling',async t=>{
 const s=await fixture(t,{worldId:'c'.repeat(32),runId:'d'.repeat(32),day:1,entries:[]});const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'first_evening',playerName:'小海',worldId:'c'.repeat(32),day:1});s.engine.liveScene=s.scene;
 const fact={id:'arrival',text:'小海抵达码头，与 Z 见面。',speaker:'engine',layer:'daily',day:1};
 s.broadcast.binding.today=[fact.text];s.broadcast.binding.entries=[fact];s.broadcast.lastTopic={text:'今天风凉',id:'wind'};
 let binding;s.providers.generate=async(_id,_p,o)=>{binding=o.broadcastContext;return {};};
 for(const kind of ['host','review']){
  await s.engine.client.generate({kind,context:kind==='host'?{host:{inbox:[]}}:{candidate:{role:'host'}}},{});
  assert.deepEqual(binding.today,[fact.text]);assert(binding.entries.some(e=>e.id===fact.id));
 }
 assert.equal(s.broadcast.told.size,0,'reading a memory does not mark it as retold');
});

test('host reviewer accepts a server-bound public memory reference and rejects invented evidence',()=>{
 const value={kind:'review',context:{candidate:{id:'candidate',role:'host'},revision:1,publicEvents:[],broadcast:{entries:[{id:'public-day-event'}]}}};
 const answer={decision:'allow',reason:'引用了当天公开事件',evidence_ids:['public-day-event']};
 assert.deepEqual(parseAgentReply(JSON.stringify(answer),value).evidenceIds,['public-day-event']);
 assert.throws(()=>parseAgentReply(JSON.stringify({...answer,evidence_ids:['invented-event']}),value));
 assert.throws(()=>parseAgentReply(JSON.stringify({...answer,echo_of:null}),{...value,context:{...value.context,candidate:{id:'candidate',role:'audience'}}}));
});

test('quiet first-evening minute uses the requested song introduction without another generation wait',async t=>{
 const s=await fixture(t);const {LiveScene}=await import('../server/live-scene.mjs');
 s.scene=new LiveScene(s,{mode:'first_evening',playerName:'小海',worldId:'e'.repeat(32),day:1});s.engine.liveScene=s.scene;s.sessionControl.data.eveningStartClock=0;s.engine._clock=60000;
 s.providers.generate=()=>{throw Error('Unexpected model generation before requested transition');};
 const cue=await s.engine.client.generate({kind:'host',context:{host:{inbox:[]}}},{});
 assert.equal(cue.speech,'给大家唱一首《晚安》，祝大家晚安。');assert.equal(cue.tool.name,'start_singing');assert.deepEqual(cue.handledIds,[]);
 assert.equal(latest(s,'media.play'),undefined,'the normal review and real introduction playback still precede singing');
});
