import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {LiveSession} from '../server/session.mjs';
import {ArtifactStore,Providers} from '../server/providers.mjs';
import {normalizeAgentInput,buildAgentRequest,parseAgentReply} from '../server/shared/worker/agent-director.js';
const frame=()=>new Promise(r=>setImmediate(r));
const body=(s,x)=>({protocolVersion:1,sessionId:s.id,epoch:s.epoch,...x});
const latest=(s,type)=>[...s.commands.values()].filter(c=>c.type===type).at(-1);
function receipt(s,c,status,extra={}){return s.receipt(body(s,{executorLeaseId:s.lease,commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{speechId:c.payload.speechId,modality:c.payload.modality,status,...extra}}));}
async function finish(s,c){receipt(s,c,'started',{positionMs:0,durationMs:2000});receipt(s,c,'completed',{positionMs:2000});await frame();}
async function fixture(t){
 const root=fs.mkdtempSync(path.join(os.tmpdir(),'z-session-control-'));
 const providers={env:{},config:async()=>({enabled:false}),generate:async(_id,p)=>p.kind==='session'?p.context.mode==='farewell'?{intent:'farewell',scope:'',speech:'刚才的话题先聊到这里。谢谢大家，晚安。'}:{intent:'keep',scope:'',speech:''}:p.kind==='review'?{decision:'allow'}:{action:'wait',speech:'',mood:'warm',handledIds:[],wakeAfterMs:120000}};
 const s=new LiveSession({root:path.join(root,'sessions'),providers,artifacts:new ArtifactStore(path.join(root,'artifacts')),songRoot:fileURLToPath(new URL('../server/assets/songs',import.meta.url))});
 await s.initialize();clearInterval(s.engine._timer);s.control(body(s,{action:'resume'}));
 t.after(()=>{s.close();fs.rmSync(root,{recursive:true,force:true});});return s;
}
async function stopAndBarrier(s){s.control(body(s,{action:'stop_session'}));const barrier=s.commands.get(s.sessionControl.barrier);receipt(s,barrier,'completed');await frame();await frame();return latest(s,'host.present');}

test('P00 freezes immediately, waits for actual stop barrier, then goodbye receipt ends exactly once',async t=>{
 const s=await fixture(t);s.engine._say('这段话还没有说完','warm','none',{source:'deepseek'});await frame();const old=latest(s,'host.present');
 s.control(body(s,{action:'stop_session'}));assert.equal(s.sessionControl.data.phase,'closing');assert.equal(old.status,'interrupted');
 await frame();assert.equal(latest(s,'host.present'),old);assert.equal(s.engine.audience.canPublish(),false);
 const deadline=s.sessionControl.data.deadlineAt;s.control(body(s,{action:'stop_session'}));assert.equal(s.sessionControl.data.deadlineAt,deadline);
 receipt(s,s.commands.get(s.sessionControl.barrier),'completed');await frame();await frame();const bye=latest(s,'host.present');assert.notEqual(bye,old);assert.equal(s.sessionControl.data.phase,'farewell');assert.equal(s.phase,'live');
 assert.throws(()=>receipt(s,old,'completed',{positionMs:2000}),/state_conflict/);
 await finish(s,bye);assert.equal(s.phase,'ended');assert.equal(s.sessionControl.data.endReason,'farewell_completed');assert.equal(s.engine._state.replyStatus,'cancelled');
 receipt(s,bye,'completed',{positionMs:2000});s.control(body(s,{action:'stop_session'}));assert.equal([...s.commands.values()].filter(c=>c.type==='session.ended').length,1);
 assert.throws(()=>s.input(body(s,{type:'viewer.message',payload:{text:'再来'}})),/session_paused/);
});

test('button works paused and silent; provider failure falls back to text without reopening',async t=>{
 const s=await fixture(t);s.providers.generate=async()=>{throw Error('offline');};s.control(body(s,{action:'pause'}));
 const bye=await stopAndBarrier(s);assert.equal(s.engine._state.paused,false);assert.equal(bye.payload.modality,'text');assert.match(bye.payload.text,/晚安/);await finish(s,bye);assert.equal(s.phase,'ended');
});

test('missing executor barrier reaches finite timeout and never starts a second voice',async t=>{
 const s=await fixture(t);s.control(body(s,{action:'stop_session'}));s.sessionControl.data.deadlineAt=Date.now()-1;s.maintenance();
 assert.equal(s.phase,'ended');assert.equal(s.sessionControl.data.endReason,'closing_timeout');assert.equal(latest(s,'host.present'),undefined);
});

test('singing is cancelled before P00 farewell and late media cannot revive it',async t=>{
 const s=await fixture(t);s.speech.enabled=true;s.speech.volume=1;assert(s.engine._executeTool({name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}));await frame();
 const prepare=latest(s,'media.prepare');const bye=await stopAndBarrier(s);assert.equal(s.talent.current,null);assert(bye);assert.throws(()=>receipt(s,prepare,'ready',{durationMs:32000}),/state_conflict/);
 assert.equal(s.engine._executeTool({name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}),false);
});

test('host closing has a real invitation window; only one scoped extension, fixed deadline',async t=>{
 const s=await fixture(t);s.sessionControl.opened();s.sessionControl.begin('host','今天我们先聊到这里。');
 receipt(s,s.commands.get(s.sessionControl.barrier),'completed');await frame();await finish(s,latest(s,'host.present'));
 assert.equal(s.sessionControl.data.phase,'closing');assert(s.sessionControl.data.readyAt>Date.now());
 s.providers.generate=async(_id,p)=>p.kind==='session'?{intent:'extend',scope:'把刚才的问题讲完',speech:''}:{decision:'allow'};
 s.input(body(s,{type:'viewer.message',payload:{text:'再陪我把这个问题说完'}}));await frame();await frame();
 assert.equal(s.sessionControl.data.phase,'live');assert.equal(s.sessionControl.data.extensionUsed,1);const deadline=s.sessionControl.data.extensionDeadline;
 const saved=s.persistData();const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());assert.equal(restored.sessionControl.data.extensionUsed,1);assert.equal(restored.sessionControl.data.extensionDeadline,deadline);
 s.sessionControl.data.extensionDeadline=Date.now()-1;s.maintenance();assert.equal(s.sessionControl.data.phase,'closing');assert.equal(s.sessionControl.data.extensionUsed,1);
 receipt(s,s.commands.get(s.sessionControl.barrier),'completed');await frame();assert.equal(s.sessionControl.extend('made-up','再聊'),false);
});

test('P00 wins over a delayed extension response and does not reset deadline',async t=>{
 const s=await fixture(t);s.sessionControl.begin('host','今晚聊到这里。');receipt(s,s.commands.get(s.sessionControl.barrier),'completed');await frame();
 let resolve;s.providers.generate=async()=>new Promise(r=>resolve=r);
 s.input(body(s,{type:'viewer.message',payload:{text:'再聊一会'}}));await frame();s.control(body(s,{action:'stop_session'}));const deadline=s.sessionControl.data.deadlineAt;
 resolve({intent:'extend',scope:'多聊一点',speech:''});await frame();assert.equal(s.sessionControl.data.closeReason,'player');assert.equal(s.sessionControl.data.extensionUsed,0);assert.equal(s.sessionControl.data.deadlineAt,deadline);
});

test('player message semantic lane works while playing, and keep does not stop a song',async t=>{
 const s=await fixture(t);s.engine.enabled=true;s.engine._state.media={id:'song',kind:'singing',phase:'playing'};
 s.providers.generate=async(_id,p)=>{assert.equal(p.kind,'session');return {intent:p.context.message.text==='下播吧'?'stop':'keep',scope:'',speech:''};};
 s.input(body(s,{type:'viewer.message',payload:{text:'晚安这首先别唱了'}}));await frame();assert.equal(s.sessionControl.data.phase,'live');
 s.input(body(s,{type:'viewer.message',payload:{text:'下播吧'}}));await frame();assert.equal(s.sessionControl.data.closeReason,'player');
});

test('restart during closing closes old session without replaying a goodbye',async t=>{
 const s=await fixture(t);s.control(body(s,{action:'stop_session'}));const saved=s.persistData();
 const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());await restored.initialize();clearInterval(restored.engine._timer);
 assert.equal(restored.phase,'ended');assert.equal(restored.sessionControl.data.phase,'ended');assert.equal(latest(restored,'host.present'),undefined);
});

test('host decisions are version checked and must have a real player source for stop',async t=>{
 const s=await fixture(t);s.sessionControl.opened();const context=s.engine._hostContext([]);
 assert.equal(s.sessionControl.decision({sessionDecision:{intent:'stop',sourceMessageId:'npc'},handledIds:[]},context),true);assert.equal(s.sessionControl.data.phase,'live');
 s.sessionControl.data.version++;
 s.sessionControl.decision({sessionDecision:{intent:'close'},speech:'再见',handledIds:[]},context);assert.equal(s.sessionControl.data.phase,'live');
});

test('real host parser and bundle expose skill and roundtrip lifecycle proposal',async t=>{
 const s=await fixture(t);s.sessionControl.opened();const value=normalizeAgentInput({requestId:crypto.randomUUID(),kind:'host',eventId:'test',context:s.engine._hostContext([])});
 const request=buildAgentRequest(value);assert(request.system.includes('直播进程管理'));assert(request.system.includes('sessionDecision'));
 const result=parseAgentReply({action:'speak',speech:'今天先聊到这里。',mood:'warm',handledIds:[],wakeAfterMs:2000,sessionDecision:{intent:'close',sourceMessageId:null,scope:'',reason:'交流已有落点'}},value);
 assert.equal(result.sessionDecision.intent,'close');
 assert.throws(()=>parseAgentReply({...result,sessionDecision:{intent:'stop',sourceMessageId:'fake',scope:'',reason:'fake'}},value));
});

test('session requests traverse real authenticated provider protocol with separate lane',async t=>{
 const s=await fixture(t);let seen;
 const p=new Providers({DEEPSEEK_API_KEY:'test-only'},{fetch:async(_url,options)=>{seen=JSON.parse(options.body);return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify({intent:'farewell',scope:'',speech:'谢谢大家，晚安。'})}}]});}});
 const result=await p.generate(s.id,{requestId:crypto.randomUUID(),eventId:'farewell',kind:'session',context:s.sessionControl.context('farewell')});
 assert.equal(result.intent,'farewell');assert(seen.messages[0].content.includes('直播进程管理'));assert(/json/i.test(seen.messages.map(m=>m.content).join(' ')),'JSON-mode provider requires an explicit JSON instruction');
});


test('closing rejects new call input and ended sessions stay ended after restart',async t=>{
 const s=await fixture(t);s.control(body(s,{action:'stop_session'}));
 for(const type of ['call.join','call.input','call.audience','call.story'])assert.throws(()=>s.input(body(s,{type,payload:{state:'listening'}})),/session_closing/);
 s.sessionControl.end('test');const saved=s.persistData();
 const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());await restored.initialize();clearInterval(restored.engine._timer);
 assert.equal(restored.phase,'ended');restored.control(body(restored,{action:'resume'}));assert.equal(restored.phase,'ended');assert.equal(restored.engine._state.paused,true);
});


test('muting a pending farewell replaces it with one text goodbye inside the same deadline',async t=>{
 const s=await fixture(t);const first=await stopAndBarrier(s),deadline=s.sessionControl.data.deadlineAt;
 s.input(body(s,{type:'client.capabilities_changed',payload:{audioEnabled:false}}));await frame();const text=latest(s,'host.present');
 assert.notEqual(text,first);assert.equal(text.payload.modality,'text');assert.equal(s.sessionControl.data.deadlineAt,deadline);
 await finish(s,text);assert.equal(s.sessionControl.data.endReason,'farewell_completed');
});

test('host stop decision bypasses active-media speech gate but still requires real input',async t=>{
 const s=await fixture(t);s.engine.enabled=true;s.input(body(s,{type:'viewer.message',payload:{text:'今晚下播吧'}}));
 const context=s.engine._hostContext(s.engine.inbox),source=context.host.inbox.find(m=>m.mine);Object.assign(context,{activeSkill:'singing',taskId:'song',phase:'playing',media:{id:'song',kind:'singing',title:'测试歌',phase:'playing',playbackId:'test-play',artifactId:'test-artifact',revision:0,positionMs:0,durationMs:2000,lineIndex:-1,reason:''}});
 const value=normalizeAgentInput({requestId:crypto.randomUUID(),kind:'host',eventId:'stop-song',context});
 const result=parseAgentReply({action:'speak',speech:'好，今晚就到这里。',mood:'warm',handledIds:[source.id],wakeAfterMs:2000,sessionDecision:{intent:'stop',sourceMessageId:source.id,scope:'',reason:'真人明确结束整场'}},value);
 assert.equal(result.sessionDecision.intent,'stop');
});

test('actual main-agent publish path commits host closing only after semantic review',async t=>{
 const s=await fixture(t);const e=s.engine;e.enabled=true;s.sessionControl.opened();e.nextWake=0;
 let allowed=false;
 s.providers.generate=async(_id,p)=>p.kind==='host'?{action:'speak',speech:'刚才的问题都聊清楚了，今晚先到这里。',mood:'warm',handledIds:[],wakeAfterMs:2000,continuation:'leave_space',agendaUpdates:[],sessionDecision:{intent:'close',sourceMessageId:null,scope:'',reason:'交流已有落点'}}:{decision:allowed?'allow':'drop',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'test-only'};
 await e._think();assert.equal(s.sessionControl.data.phase,'live');assert.equal(latest(s,'host.present'),undefined);
 allowed=true;e.nextWake=0;await e._think();assert.equal(s.sessionControl.data.phase,'closing');assert.equal(s.sessionControl.data.closeReason,'host');
 assert.equal(latest(s,'host.present'),undefined);receipt(s,s.commands.get(s.sessionControl.barrier),'completed');await frame();assert.match(latest(s,'host.present').payload.text,/聊清楚/);
});

test('time checkpoints invalidate old work, wake host once and reach real request payload',async t=>{
 const s=await fixture(t),c=s.sessionControl,start=c.data.startedAt;let now=start;
 c.now=()=>now;
 const checkpoints=[[60000,'develop'],[180000,'advance'],[300000,'resolve'],[420000,'wrap']];
 for(const [elapsed,beat] of checkpoints){
  const pending=new AbortController();s.engine.pending=pending;s.engine.nextWake=Infinity;
  now=start+elapsed;c.tick();assert(pending.signal.aborted);assert.equal(s.engine.nextWake,s.engine._clock);
  const version=c.data.version;c.tick();assert.equal(c.data.version,version,'one invalidation per boundary');
  const request=buildAgentRequest(normalizeAgentInput({requestId:crypto.randomUUID(),kind:'host',eventId:'time',context:s.engine._hostContext([])}));
  assert.equal(request.input.context.host.sessionControl.timing.beat,beat);
  assert.equal(request.input.context.host.sessionControl.timing.hardRemainingMs,600000-elapsed);
  assert(request.system.includes('时间节点与剧情推进'));
 }
});

test('silent or stalled host is forced to close at eight minutes even paused and playing',async t=>{
 const s=await fixture(t),c=s.sessionControl,start=c.data.startedAt;let now=start+479999;c.now=()=>now;
 s.speech.enabled=true;s.speech.volume=1;assert(s.engine._executeTool({name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}));await frame();s.engine.pending=new AbortController();
 s.control(body(s,{action:'pause'}));c.tick();assert(c.normal);
 now++;c.tick();assert.equal(c.data.phase,'closing');assert.equal(c.data.closeReason,'host');
 assert.equal(c.data.deadlineAt,start+525000);assert.equal(s.engine.pending,null);
 assert(s.commands.get(c.barrier));assert.equal(s.engine._state.paused,false);
 receipt(s,s.commands.get(c.barrier),'completed');await frame();const announcement=latest(s,'host.present');assert(announcement);
 await finish(s,announcement);now+=6001;c.tick();await frame();await frame();
 assert.equal(c.data.phase,'farewell');await finish(s,latest(s,'host.present'));assert.equal(s.phase,'ended');
});

test('eight-minute extension is clamped to 9:15 and farewell cannot exceed ten minutes',async t=>{
 const s=await fixture(t),c=s.sessionControl,start=c.data.startedAt;let now=start+480000;c.now=()=>now;c.tick();
 receipt(s,s.commands.get(c.barrier),'completed');await frame();await finish(s,latest(s,'host.present'));
 now=start+485000;
 const message=s.engine._message('把晚霞说完好吗',{mine:true,name:'你'});s.engine._public('player.message',message.text,{id:message.id,speakerRole:'player',speakerId:'player'});
 assert(c.extend(message.id,'把晚霞说完'));assert.equal(c.data.extensionDeadline,start+555000);
 assert.equal(c.snapshot().remainingMs,70000);c.tick();assert.equal(c.data.phase,'live','target time must not instantly cancel accepted extension');
 now=start+555000;c.tick();assert.equal(c.data.phase,'closing');assert.equal(c.data.deadlineAt,start+600000);
 assert.equal(c.extend(message.id,'再多聊一点'),false);
 now=start+600000;c.tick();assert.equal(c.data.endReason,'session_hard_limit');assert.equal(s.phase,'ended');
});

test('absolute limit survives restart and ends without any executor or model response',async t=>{
 const s=await fixture(t),c=s.sessionControl;const saved=s.persistData();
 saved.sessionControl.startedAt=Date.now()-610000;saved.sessionControl.targetAt=saved.sessionControl.startedAt+480000;saved.sessionControl.hardAt=saved.sessionControl.startedAt+600000;
 const restored=new LiveSession({id:s.id,root:s.root,providers:s.providers,artifacts:s.artifacts,songRoot:s.songRoot,saved});t.after(()=>restored.close());await restored.initialize();clearInterval(restored.engine._timer);
 assert.equal(restored.sessionControl.data.hardAt,saved.sessionControl.hardAt);assert.equal(restored.phase,'ended');assert.equal(restored.sessionControl.data.endReason,'session_hard_limit');
 assert.equal(latest(restored,'host.present'),undefined);
});

test('P00 overrides timed invitation; even delayed semantic extension cannot extend it',async t=>{
 const s=await fixture(t),c=s.sessionControl;let now=c.data.startedAt+480000;c.now=()=>now;c.tick();
 receipt(s,s.commands.get(c.barrier),'completed');await frame();let resolve;s.providers.generate=async()=>new Promise(r=>resolve=r);
 s.input(body(s,{type:'viewer.message',payload:{text:'再聊一下'}}));await frame();c.stop();const deadline=c.data.deadlineAt;
 resolve({intent:'extend',scope:'再聊晚霞',speech:''});await frame();now+=1000;c.tick();
 assert.equal(c.data.closeReason,'player');assert.equal(c.data.extensionUsed,0);assert.equal(c.data.deadlineAt,deadline);
});
