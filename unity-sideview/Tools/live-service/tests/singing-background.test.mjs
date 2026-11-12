import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {SingingService} from '../server/singing/service.mjs';
import {Z_VOICE,checkWav,validatePlan,readSingingResult} from '../server/singing/provider.mjs';
import {parseAgentReply,buildAgentRequest,handleAgent,normalizeAgentInput} from '../server/shared/worker/agent-director.js';
const root=fileURLToPath(new URL('../',import.meta.url)),owner='local-preview-owner';
const bytes=await fs.readFile(path.join(root,'server/assets/songs/wanan-yanrenzhong-vivi2-chorus.wav'));
const plan=(kind='chorus')=>({intent:'request',title:'晚安',artist:'颜人中',version:'',question:'',segment:{kind,ordinal:1,description:kind==='chorus'?'第一段完整副歌':'第一句',lyricStart:''}});
async function fixture(t,options={}){const directory=await fs.mkdtemp(path.join(os.tmpdir(),'z-singing-'));let service=await SingingService.open({root,directory,autoStart:false,sources:[],environment:async()=>({}),parse:async()=>plan(),generate:async()=>{throw Error('should_not_generate');},...options});t.after(async()=>{if(!service.closed)await service.close();await fs.rm(directory,{recursive:true,force:true});});return {get service(){return service;},directory,async reopen(){await service.close();service=await SingingService.open({root,directory,autoStart:false,sources:[],environment:async()=>({}),...options});return service;}};}
const submit=(s,sourceId='one',sessionId='day-one')=>s.submit({text:'唱一下颜人中《晚安》的副歌',sourceId,sessionId});
const generated=()=>({bytes,report:{voice:Z_VOICE.speaker,model:Z_VOICE.singingModel,enableMusic:true,generationDone:true}});
test('cached segment survives restart and dispatch has no singing playback ledger',async t=>{
 const f=await fixture(t),a=submit(f.service);assert.equal(submit(f.service).id,a.id);await f.service.tick();
 assert.equal(f.service.context(owner).tasks[0].status,'ready');assert.throws(()=>f.service.cancel(a.id,'someone-else'));
 await assert.rejects(()=>SingingService.open({root,directory:f.directory}),/already_running/);
 const s=await f.reopen();assert.equal(s.context('other').tasks.length,0);
 const dispatch=await s.reserve(a.id,owner);assert.equal(dispatch.song.sha256,checkWav(bytes).sha256);
 assert.equal(s.context(owner).tasks[0].status,'scheduled');assert.equal(s.read().playbacks.length,0);
 await assert.rejects(()=>s.reserve(a.id,owner),/not_ready/);
});
test('different range generates once; technical acceptance immediately readies all subsequent requests',async t=>{
 let calls=0;const {service:s}=await fixture(t,{parse:async()=>plan('verse'),generate:async()=>{calls++;return generated();}});
 const a=submit(s),b=submit(s,'two');await s.tick();await s.tick();
 assert.equal(calls,1);assert(s.context(owner).tasks.every(t=>t.status==='ready'));
 assert.equal(s.read().tasks[0].assetId,s.read().tasks[1].assetId);
 const asset=s.read().assets.at(-1);assert.equal(asset.approved,false);assert.equal(asset.acceptance.listeningVerified,false);
 assert.equal(s.context(owner).limits.fixedWaiting,false);assert.equal(s.context(owner).limits.review,'none');
 const dispatches=await Promise.allSettled([s.reserve(a.id,owner),s.reserve(b.id,owner)]);
 assert.equal(dispatches.filter(x=>x.status==='fulfilled').length,1);assert(s.read().tasks.every(t=>t.status==='scheduled'));
 submit(s,'three');await s.tick();assert.equal(calls,1);assert.equal(s.read().tasks.at(-1).status,'ready');
});
test('old shared awaiting-review tasks migrate together, explicit rejections remain blocked',async t=>{
 const f=await fixture(t);submit(f.service);submit(f.service,'two');
 f.service.change(s=>{const a={...structuredClone(s.assets[0]),id:'old-pending',approved:false};delete a.review;delete a.libraryAssetId;s.assets.push(a);for(const task of s.tasks){task.status='awaiting_review';task.assetId=a.id;}
 const rejected={...a,id:'old-rejected',review:{approved:false}};s.assets.push(rejected);});
 const s=await f.reopen();assert(s.read().tasks.every(t=>t.status==='ready'));
 assert.equal(s.read().assets.find(a=>a.id==='old-pending').acceptance.listeningVerified,false);
 await assert.rejects(()=>s.audio('old-rejected'),/asset_unavailable/);
});
test('cancel discards a late generation and does not revive task',async t=>{
 let release,started;const began=new Promise(r=>started=r),done=new Promise(r=>release=r);
 const {service:s}=await fixture(t,{parse:async()=>plan('verse'),generate:async()=>{started();return done;}});
 const a=submit(s),count=s.read().assets.length,work=s.tick();await began;
 s.cancel(a.id,owner);release(generated());await work;
 assert.equal(s.read().tasks[0].status,'cancelled');assert.equal(s.read().assets.length,count);
});
test('unknown paid result cannot be bypassed by another request; explicit retry remains possible',async t=>{
 let calls=0;const f=await fixture(t,{parse:async()=>plan('verse'),generate:async()=>{calls++;if(calls===1)throw Error('generation_outcome_unknown');return generated();}});
 const a=submit(f.service);await f.service.tick();submit(f.service,'again');await f.service.tick();assert.equal(calls,1);
 assert.throws(()=>f.service.retry(a.id,owner),/confirm_unknown/);
 f.service.retry(a.id,owner,{acknowledgeUnknown:true});await f.service.tick();assert.equal(calls,2);assert.equal(f.service.read().tasks[0].status,'ready');
});
test('restart records unknown attempt and original history; clarification preserves context',async t=>{
 const f=await fixture(t,{parse:async()=>({...plan(),segment:{...plan().segment,kind:'full'}})});const a=submit(f.service);await f.service.tick();assert.equal(f.service.read().tasks[0].status,'needs_input');
 f.service.clarify(a.id,owner,'第一句');assert.equal(JSON.parse(f.service.read().tasks[0].requestText).originalRequest,a.originalText);
 f.service.change(s=>{s.tasks[0].status='generating';s.tasks[0].attempt=1;});const s=await f.reopen();
 assert.equal(s.read().tasks[0].reason,'process_restarted_unknown');assert.throws(()=>s.retry(a.id,owner),/confirm_unknown/);
});
test('voice changes and tampered files block playback before dispatch',async t=>{
 const {service:s}=await fixture(t),a=submit(s);await s.tick();s.change(v=>v.voice.revision++);
 await assert.rejects(()=>s.reserve(a.id,owner),/asset_unavailable/);s.change(v=>v.voice.revision--);
 const asset=s.read().assets[0];await fs.writeFile(path.join(s.directory,'assets',asset.sha256+'.wav'),'changed');await assert.rejects(()=>s.audio(asset.id),/integrity/);
 assert.equal(s.read().tasks[0].status,'ready');
});
test('main agent can submit exact source and play only trusted ready tasks; NPC does not get private memory',()=>{
  const c={host:{inbox:[{id:'player1',mine:true,text:'唱第一句'}]},activeSkill:null,mediaRuntime:{audioEnabled:true},singing:{enabled:true,tasks:[{id:'task1',status:'ready'}]}};
  const base={action:'tool',speech:'我试着准备这段。',mood:'warm',handledIds:['player1'],wakeAfterMs:5000,memory:''};
  assert.equal(parseAgentReply({...base,tool:{name:'prepare_song',args:{messageId:'player1'}}},{kind:'host',context:c}).tool.name,'prepare_song');
  assert.throws(()=>parseAgentReply({...base,tool:{name:'prepare_song',args:{messageId:'npc'}}},{kind:'host',context:c}));
  assert.equal(parseAgentReply({...base,tool:{name:'cancel_song',args:{taskId:'task1',messageId:'player1'}}},{kind:'host',context:c}).tool.name,'cancel_song');
  assert.throws(()=>parseAgentReply({...base,tool:{name:'cancel_song',args:{taskId:'task1',messageId:'npc'}}},{kind:'host',context:c}));
  assert.equal(parseAgentReply({...base,tool:{name:'play_prepared_song',args:{taskId:'task1'}}},{kind:'host',context:c}).tool.name,'play_prepared_song');
  assert.throws(()=>parseAgentReply({...base,handledIds:[],tool:{name:'play_prepared_song',args:{taskId:'task1'}}},{kind:'host',context:c}));
  assert.equal(parseAgentReply({...base,handledIds:[],tool:{name:'play_prepared_song',args:{taskId:'task1'}}},{kind:'host',context:{...c,host:{inbox:[]}}}).tool.name,'play_prepared_song');
  assert.throws(()=>parseAgentReply({...base,tool:{name:'play_prepared_song',args:{taskId:'not-ready'}}},{kind:'host',context:c}));
  assert.match(buildAgentRequest({kind:'host',context:c}).system,/唱歌执行接口/);
  const prompt=buildAgentRequest({kind:'host',context:c});
  assert.doesNotMatch(prompt.system,/支持预制曲库播放，不支持动态歌声生成/);
  assert.deepEqual(prompt.input.songLibrary,[]);
  assert.throws(()=>parseAgentReply({...base,tool:{name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}},{kind:'host',context:c}));
  assert(buildAgentRequest({kind:'host',context:{...c,host:{inbox:[]}}}).input.songLibrary.length>0);

});
test('browser cannot inject trusted singing memory into model context',async()=>{
  const response=await handleAgent(new Request('http://127.0.0.1/api/agent',{method:'POST',headers:{Origin:'http://127.0.0.1','Content-Type':'application/json','oai-authenticated-user-id':'test'},body:JSON.stringify({requestId:crypto.randomUUID(),kind:'host',eventId:'one',context:{phase:'chat',host:{inbox:[]},singing:{enabled:true,tasks:[]}}})}),{DEEPSEEK_API_KEY:'fixture'},{fetch:()=>{throw Error('must_not_call');}});assert.equal(response.status,400);
});

test('singing reports preserve actionable provider failures without exposing payloads',async t=>{
  const directory=await fs.mkdtemp(path.join(os.tmpdir(),'z-singing-report-'));
  t.after(()=>fs.rm(directory,{recursive:true,force:true}));
  const reportFile=path.join(directory,'vivi-wanan-test.json');
  const check=async(report,code)=>{await fs.writeFile(reportFile,JSON.stringify(report));await assert.rejects(()=>readSingingResult(directory),error=>error.message===code);};
  await check({outcome:'handshake_rejected',httpStatus:403,providerResponse:JSON.stringify({error:{code:'45000030',message:'private provider payload'}})},'singing_provider_resource_not_granted');
  await check({outcome:'provider_error',events:[{error:JSON.stringify({code:45000030,message:'private payload'})}]},'singing_provider_resource_not_granted');
  await check({outcome:'handshake_rejected',httpStatus:401},'singing_provider_configuration_error');
  await check({outcome:'timeout',audioGenerated:true,generationDone:false},'generation_outcome_unknown');
  await check({outcome:'closed_without_audio_completion',audioGenerated:false},'singing_no_complete_audio');
  const {service:s}=await fixture(t,{parse:async()=>plan('verse'),generate:async()=>{throw Error('singing_provider_resource_not_granted');}});
  submit(s);await s.tick();const task=s.context(owner).tasks[0];
  assert.equal(task.status,'failed');assert.match(task.reason,/语音项目权限/);assert.doesNotMatch(task.reason,/private/);
});

function newSongSource({error}={}) {
 const descriptor={sourceId:'netease',externalId:'fixture-1',title:'测试新歌',artist:'测试歌手',version:'网易云录音 fixture-1',evidence:'synthetic test only',performers:[{id:1,name:'测试歌手'}],performerKey:'1',searchRank:1,officialEvidence:{eligible:true,basis:'test_fixture'},audio:{songId:'fixture-1'},lyrics:{songId:'fixture-1'}};
 return {id:'netease',searchOfficial:async()=>({candidates:[descriptor],inspectionComplete:true}),fetch:async(ref,kind)=>{if(error)throw Error(error);return kind==='audio'?bytes:Buffer.from('[00:00.00]测试歌词');}};
}
test('new song retrieves full references, immediately generates once, caches and survives restart',async t=>{
 let calls=0;const f=await fixture(t,{sources:[newSongSource()],parse:async()=>({...plan(),title:'测试新歌',artist:''}),generate:async(p)=>{calls++;assert.equal(p.artist,'测试歌手');return generated();}});
 submit(f.service);await f.service.tick();const task=f.service.read().tasks[0];assert.equal(task.status,'ready');assert.equal(calls,1);
 const m=f.service.library.material(task.recordingId);assert.equal(m.status,'ready');assert.equal(m.fullSongRetained,true);assert.equal(m.review,null);
 assert.deepEqual(f.service.read().events.filter(e=>e.taskId===task.id).map(e=>e.status),['queued','parsing','retrieving','generating','checking','ready']);
 submit(f.service,'again');await f.service.tick();assert.equal(calls,1);
 await f.reopen();assert.equal(f.service.read().tasks[1].status,'ready');await f.service.reserve(task.id,owner);
});
test('rejected references block both cached and new ranges, even when a resource later disappears',async t=>{
 const {service:s}=await fixture(t,{sources:[newSongSource()],parse:async()=>({...plan(),title:'测试新歌',artist:''}),generate:async()=>generated()});
 submit(s);await s.tick();const task=s.read().tasks[0],m=s.library.material(task.recordingId);
 s.library.reviewMaterial(task.recordingId,{approved:false,reviewer:'prior-review',note:'wrong reference',audioSha:m.audio.sha256,lyricsSha:m.lyrics.sha256});
 await assert.rejects(()=>s.audio(task.assetId),/material_previously_rejected/);
 assert.equal(s.library.state(task.recordingId,{kind:'chorus',ordinal:1}).assetId,null);
 s.library.db.prepare('UPDATE materials SET lyrics=NULL WHERE recording_id=?').run(task.recordingId);
 s.parse=async()=>({...plan('verse'),title:'测试新歌',artist:''});s.generate=async()=>{throw Error('must_not_generate');};
 submit(s,'new-range');await s.tick();assert.equal(s.read().tasks[1].reason,'material_previously_rejected');assert.equal(s.read().tasks[1].attempt,0);
});
test('expired login fails preparation before paid generation with actionable safe reason',async t=>{
 const {service:s}=await fixture(t,{sources:[newSongSource({error:'netease_login_expired'})],parse:async()=>({...plan(),title:'测试新歌',artist:''}),generate:async()=>{throw Error('must_not_generate');}});
 submit(s);await s.tick();assert.equal(s.read().tasks[0].reason,'netease_login_expired');assert.equal(s.read().tasks[0].attempt,0);assert.match(s.context(owner).tasks[0].reason,/登录已过期/);
});


test('conversation state keeps song facts but leaves maintenance diagnostics in management',async()=>{
 const {singingConversationState}=await import('../server/shared/worker/agent-director.js');
 const original={enabled:true,tasks:[{id:'one',status:'failed',reason:'private maintenance diagnostic',request:'唱晚安',question:''}],limits:{singleSegmentSeconds:90,providerLookup:'internal'}};
 const view=singingConversationState(original);
 assert.equal(view.tasks[0].status,'failed');assert.equal(view.tasks[0].request,'唱晚安');assert.equal(view.tasks[0].reason,undefined);
 assert.equal(original.tasks[0].reason,'private maintenance diagnostic');assert.equal(view.limits.providerLookup,undefined);
});


test('ready song can echo its historical request without handling a new message',()=>{
 const c={host:{inbox:[],session:{evidence:[{id:'old-player',type:'player.message',speakerRole:'player'}],unfinishedItems:[]}},activeSkill:null,mediaRuntime:{audioEnabled:true},singing:{enabled:true,tasks:[{id:'ready-song',status:'ready',sourceId:'old-player'}]}};
 const reply={action:'tool',speech:'好啦，我唱给你听。',mood:'warm',handledIds:['old-player'],wakeAfterMs:2000,memory:'',tool:{name:'play_prepared_song',args:{taskId:'ready-song'}}};
 assert.deepEqual(parseAgentReply(reply,{kind:'host',context:c}).handledIds,[]);
 assert.throws(()=>parseAgentReply({...reply,handledIds:['unknown']},{kind:'host',context:c}));
 assert.throws(()=>parseAgentReply(reply,{kind:'host',context:{...c,host:{...c.host,inbox:[{id:'new-player',mine:true,text:'等等'}]}}}));
});

test('review gets explicit execution facts and rejects audience tool claims',()=>{
 const payload={requestId:crypto.randomUUID(),kind:'review',eventId:'opening',context:{revision:1,candidate:{id:'opening',role:'host',speakerId:'Z',text:'这就唱',intent:'speak',replyTo:[],evidenceIds:[]}}};
 const speak=normalizeAgentInput(payload);assert.match(buildAgentRequest(speak).input.executionContext,/没有安排播放/);
 payload.context.candidate.intent='tool';payload.context.candidate.plannedTool={name:'play_prepared_song',targetId:'ready-task'};
 const play=normalizeAgentInput(payload);assert.match(buildAgentRequest(play).input.executionContext,/尝试执行所列播放工具/);
 payload.context.candidate.plannedTool.name='prepare_song';assert.match(buildAgentRequest(normalizeAgentInput(payload)).input.executionContext,/没有安排播放/);
 payload.context.candidate.role='audience';payload.context.candidate.speakerId='liting';assert.throws(()=>normalizeAgentInput(payload));
});
test('daytime tickets are only visible with matching save, day, owner and public receipt',async t=>{
 const {service}=await fixture(t);
 const binding={worldId:'island',day:2,songs:[{sourceId:'line'}]};
 const ticket=submit(service,'line','day:island:2');await service.tick();
 assert.equal(service.daytimeTasks(owner,binding)[0].id,ticket.id);
 assert.equal(service.daytimeTasks(owner,binding)[0].status,'ready');
 assert.equal(service.context(owner,{publicRoom:true}).tasks.length,0);
 for(const other of [{...binding,songs:[]},{...binding,worldId:'other'},{...binding,day:3}])
   assert.deepEqual(service.daytimeTasks(owner,other),[]);
 assert.deepEqual(service.daytimeTasks('other-owner',binding),[]);
});
