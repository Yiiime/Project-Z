import {test} from 'node:test';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {ArtifactStore,loadEnvironment,Providers} from '../server/providers.mjs';
import {LiveSession} from '../server/session.mjs';
import {normalizeAgentInput} from '../server/shared/worker/agent-director.js';
const frame=()=>new Promise(r=>setImmediate(r));
async function fixture(t){const root=fs.mkdtempSync(path.join(os.tmpdir(),'z-unity-test-'));const providers={env:{},config:async()=>({enabled:false}),generate:async()=>({action:'wait',speech:'',mood:'neutral',handledIds:[],wakeAfterMs:120000,memory:''})};const s=new LiveSession({root:path.join(root,'sessions'),providers,artifacts:new ArtifactStore(path.join(root,'artifacts')),songRoot:fileURLToPath(new URL('../server/assets/songs',import.meta.url))});await s.initialize();clearInterval(s.engine._timer);t.after(()=>{s.close();fs.rmSync(root,{recursive:true,force:true});});s.control(envelope(s,{action:'resume'}));return s;}
const envelope=(s,extra={})=>({protocolVersion:1,sessionId:s.id,epoch:s.epoch,...extra});
const receipt=(s,c,status,extra={})=>s.receipt(envelope(s,{executorLeaseId:s.lease,commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{...Object.fromEntries(['artifactId','playbackId','speechId','stage','round','view','modality'].filter(k=>c.payload[k]!==undefined).map(k=>[k,c.payload[k]])),status,...extra}}));
const latest=(s,type)=>[...s.commands.values()].filter(c=>c.type===type).at(-1);

test('preparing talent snapshot is accepted by the actual agent input contract',async t=>{
 const s=await fixture(t);
 s.talent.current={id:crypto.randomUUID(),kind:'voice-performance',title:'便利店小剧场',phase:'preparing'};
 s.talent.changed();
 const context=JSON.parse(JSON.stringify(s.engine._hostContext([])));
 assert.equal(context.media.playbackId,null);assert.equal(context.media.artifactId,null);
 assert.doesNotThrow(()=>normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'preparing-talent',kind:'host',message:'决定下一步',context}));
});

test('host completion requires actual execution; interruption never publishes completed speech',async t=>{const s=await fixture(t);s.engine._say('你好，今天怎么样？','warm','none',{source:'deepseek'});await frame();const c=latest(s,'host.present');assert(c);assert(!s.engine.facts.some(e=>e.type==='speech.completed'));assert.throws(()=>receipt(s,c,'completed',{positionMs:2000}),/invalid_transition/);receipt(s,c,'started',{durationMs:2000,positionMs:0});receipt(s,c,'progress',{positionMs:500,text:'你好'});assert(s.engine.facts.some(e=>e.type==='speech.progress'&&e.text==='你好'));receipt(s,c,'interrupted',{reason:'viewer_stop'});assert(!s.engine.facts.some(e=>e.type==='speech.completed'));assert.equal(s.engine._active,null);});

test('tarot viewer and host choices remain distinct; concealed deck is not sent',async t=>{const s=await fixture(t);s.engine.sendMessage('想看看新计划');const requesterId=s.engine.inbox.at(-1).id;assert(s.engine._executeTool({name:'start_tarot',args:{origin:'viewer',requesterId,question:'新计划',spread:'single'}}));const c=latest(s,'tarot.present');const task=s.engine._state.play.tarot;assert.equal(task.phase,'candidates_ready');assert.equal(c.payload.cards,undefined);assert(!JSON.stringify(s.snapshot()).includes('"deck"'));receipt(s,c,'displayed');assert.equal(task.phase,'awaiting_selection');assert.equal(s.engine._executeTool({name:'choose_tarot',args:{taskId:task.id,round:task.round,candidateId:task.candidates[0].id}}),false);s.input(envelope(s,{type:'tarot.selection',payload:{taskId:task.id,resourceRevision:0,round:task.round,candidateId:task.candidates[0].id}}));assert.throws(()=>s.input(envelope(s,{type:'tarot.selection',payload:{taskId:task.id,resourceRevision:0,round:task.round,candidateId:task.candidates[0].id}})),/state_conflict/);s.engine._afterSelection();assert.equal(s.engine._toolContext().cards,undefined);receipt(s,latest(s,'tarot.present'),'displayed');assert.equal(s.engine._toolContext().cards.length,1);});

test('drawing stays hidden until requested and advances only after a valid display receipt',async t=>{const s=await fixture(t);const scene={title:'一棵树',stages:['轮廓','树冠'],elements:[{id:'trunk',label:'树干',stage:0,stroke:'#334455',fill:'none',strokeWidth:3,commands:[['M',120,120],['L',120,300]]},{id:'leaves',label:'树冠',stage:1,stroke:'#335544',fill:'none',strokeWidth:3,commands:[['M',40,120],['L',200,120]]}]};s.engine.enabled=true;s.engine.client.generate=async p=>({status:'ready',taskId:p.context.drawingJob.taskId,revision:p.context.drawingJob.revision,scene});assert(s.engine.requestDrawing({request:'画一棵树'}));await frame();assert.equal(latest(s,'drawing.present'),undefined);const d=s.engine.drawings.current;s.engine.setPanel('drawing',true);let c=latest(s,'drawing.present');assert(c);assert.throws(()=>receipt(s,c,'displayed',{stage:1}),/invalid_evidence/);receipt(s,c,'displayed');assert.equal(d.phase,'drawing');s.engine._clock+=2000;s.engine.drawings.advance();c=latest(s,'drawing.present');assert.equal(c.payload.stage,1);receipt(s,c,'displayed');assert.equal(d.phase,'completed');});

test('server restart changes epoch and invalidates old execution receipts',async t=>{const s=await fixture(t);s.engine._say('这句还没说完','warm','none',{source:'deepseek'});await frame();const c=latest(s,'host.present');const saved=s.persistData();const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());assert.equal(restored.epoch,s.epoch+1);assert(!restored.engine.facts.some(e=>e.type==='speech.completed'));assert.equal(restored.commands.get(c.commandId).status,'interrupted');assert.throws(()=>restored.receipt(envelope(s,{executorLeaseId:s.lease})),/stale_epoch/);});

test('song must really finish and be released before another talent can start',async t=>{
 const s=await fixture(t);s.speech.enabled=true;s.speech.volume=1;
 assert(s.engine._executeTool({name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}));
 await frame();const prepare=latest(s,'media.prepare');assert(prepare);receipt(s,prepare,'ready',{durationMs:32284});assert(s.talent.play());
 const play=latest(s,'media.play');receipt(s,play,'started',{durationMs:32284,positionMs:0});
 assert.throws(()=>receipt(s,play,'completed',{positionMs:1000}),/not_finished/);
 receipt(s,play,'progress',{positionMs:31000,lineIndex:0});receipt(s,play,'completed',{positionMs:32284});
 const tarot={name:'start_tarot',args:{origin:'host',question:'唱完后的灵感',spread:'single'}};
 assert.equal(s.engine._executeTool(tarot),false);
 assert(s.engine._executeTool({name:'finish_activity',args:{taskId:play.taskId,cancel:false}}));
 assert.equal(s.engine._state.media,null);assert.equal(latest(s,'activity.release').taskId,play.taskId);
 assert(s.engine._executeTool(tarot));
});

test('three-card viewer selection and host choice both reveal then release to chat',async t=>{
 const s=await fixture(t);s.engine.sendMessage('我想分三次选牌');const requesterId=s.engine.inbox.at(-1).id;
 assert(s.engine._executeTool({name:'start_tarot',args:{origin:'viewer',requesterId,question:'新计划',spread:'three'}}));
 const task=s.engine._state.play.tarot;
 for(let round=1;round<=3;round++){
  const c=latest(s,'tarot.present');assert.equal(c.payload.round,round);assert.equal(c.payload.view,'backs');receipt(s,c,'displayed');
  assert.equal(s.engine._executeTool({name:'choose_tarot',args:{taskId:task.id,round,candidateId:task.candidates[0].id}}),false);
  s.input(envelope(s,{type:'tarot.selection',payload:{taskId:task.id,resourceRevision:0,round,candidateId:task.candidates[1].id}}));
  s.engine._afterSelection();if(round<3)assert.equal(s.snapshot().tarot.cards.length,0);
 }
 const revealed=latest(s,'tarot.present');assert.equal(revealed.payload.cards.length,3);assert.equal(revealed.payload.view,'revealed');
 receipt(s,revealed,'displayed');assert.equal(task.phase,'completed');
 assert(s.engine._executeTool({name:'finish_activity',args:{taskId:task.id,cancel:false}}));assert.equal(s.snapshot().activeTask,null);assert.equal(s.snapshot().panels.tarot,false);
 assert(s.engine._executeTool({name:'start_tarot',args:{origin:'host',question:'我的灵感',spread:'single'}}));
 const host=s.engine._state.play.tarot;receipt(s,latest(s,'tarot.present'),'displayed');
 assert.throws(()=>s.input(envelope(s,{type:'tarot.selection',payload:{taskId:host.id,resourceRevision:0,round:host.round,candidateId:host.candidates[0].id}})),/state_conflict/);
 assert(s.engine._executeTool({name:'choose_tarot',args:{taskId:host.id,round:host.round,candidateId:host.candidates[0].id}}));s.engine._afterSelection();receipt(s,latest(s,'tarot.present'),'displayed');
 assert(s.engine._executeTool({name:'finish_activity',args:{taskId:host.id,cancel:false}}));assert.equal(s.snapshot().activeTask,null);
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

test('native room exposes web message receipts and selection pause without pausing the room',async t=>{
 const s=await fixture(t);s.engine.sendMessage('我想看看新计划');const message=s.engine._state.messages.findLast(m=>m.mine);
 const room=s.snapshot().room;assert.equal(room.viewers,s.engine._state.viewers);assert.equal(room.messages.find(m=>m.id===message.id).replyStatus,'seen');assert.equal(room.voiceAvailable,false);
 assert(s.engine._executeTool({name:'start_tarot',args:{origin:'viewer',requesterId:message.id,question:'新计划',spread:'three'}}));
 const task=s.engine._state.play.tarot;receipt(s,latest(s,'tarot.present'),'displayed');
 s.input(envelope(s,{type:'tarot.pause',payload:{taskId:task.id}}));
 assert.equal(s.snapshot().tarot.paused,true);assert.equal(s.snapshot().tarot.spread,'three');assert.equal(s.snapshot().phase,'live');
 assert.throws(()=>s.input(envelope(s,{type:'tarot.selection',payload:{taskId:task.id,resourceRevision:0,round:task.round,candidateId:task.candidates[0].id}})),/state_conflict/);
 s.input(envelope(s,{type:'tarot.pause',payload:{taskId:task.id}}));assert.equal(s.snapshot().tarot.paused,false);
 assert.throws(()=>s.input(envelope(s,{type:'activity.finish',payload:{taskId:'expired'}})),/stale_revision/);
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
 const keys=['DEEPSEEK_API_KEY','VOLC_TTS_API_KEY','VOLC_ASR_API_KEY'];const saved=Object.fromEntries(keys.map(k=>[k,process.env[k]]));
 t.after(()=>{for(const k of keys)if(saved[k]===undefined)delete process.env[k];else process.env[k]=saved[k];});for(const k of keys)delete process.env[k];
 fs.writeFileSync(path.join(root,'.env'),'DEEPSEEK_API_KEY="test-text"\nVOLC_TTS_API_KEY=test-voice\nexport VOLC_ASR_API_KEY=test-asr # local\n');
 assert.deepEqual(loadEnvironment(root),{DEEPSEEK_API_KEY:'test-text',VOLC_TTS_API_KEY:'test-voice',VOLC_ASR_API_KEY:'test-asr'});
 fs.writeFileSync(path.join(root,'.env.local'),'DEEPSEEK_API_KEY=test-override\n');assert.equal(loadEnvironment(root).DEEPSEEK_API_KEY,'test-override');
 process.env.DEEPSEEK_API_KEY='test-process';assert.equal(loadEnvironment(root).DEEPSEEK_API_KEY,'test-process');
});


test('voice entitlement failures reach the native status without provider payloads',async()=>{
 const providers=new Providers({VOLC_TTS_API_KEY:'test-only'},{fetch:async()=>new Response('data: '+JSON.stringify({code:45000030,message:'provider-private-details'})+'\n\n',{headers:{'Content-Type':'text/event-stream'}})});
 await assert.rejects(providers.synthesize('test-owner','晚上好','warm'),{message:'tts_provider_resource_not_granted'});
 assert.equal(providers.voiceError,'tts_provider_resource_not_granted');
});
