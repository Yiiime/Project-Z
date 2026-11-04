import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {SingingService} from '../server/singing/service.mjs';
import {Z_VOICE,checkWav,validatePlan,readSingingResult} from '../server/singing/provider.mjs';
import {parseAgentReply,buildAgentRequest,handleAgent} from '../server/shared/worker/agent-director.js';
const root=fileURLToPath(new URL('../',import.meta.url)),owner='local-preview-owner';
const bytes=await fs.readFile(path.join(root,'server/assets/songs/wanan-yanrenzhong-vivi2-chorus.wav'));
const plan=(kind='chorus')=>({intent:'request',title:'晚安',artist:'颜人中',version:'',question:'',segment:{kind,ordinal:1,description:kind==='chorus'?'第一段完整副歌':'第一句',lyricStart:''}});
async function fixture(t,options={}){const directory=await fs.mkdtemp(path.join(os.tmpdir(),'z-singing-'));let service=await SingingService.open({root,directory,autoStart:false,environment:async()=>({}),parse:async()=>plan(),generate:async()=>{throw Error('should_not_generate');},...options});t.after(async()=>{if(!service.closed)await service.close();await fs.rm(directory,{recursive:true,force:true});});return {get service(){return service;},directory,async reopen(){await service.close();service=await SingingService.open({root,directory,autoStart:false,environment:async()=>({}),...options});return service;}};}
const submit=(s,sourceId='one',sessionId='day-one')=>s.submit({text:'唱一下颜人中《晚安》的副歌',sourceId,sessionId});
test('same segment is reused without paid generation; state and ownership survive restart',async t=>{const f=await fixture(t);const a=submit(f.service);assert.equal(submit(f.service).id,a.id);await f.service.tick();const task=f.service.context(owner).tasks[0];assert.equal(task.status,'ready');assert.equal(task.attempt,0);assert.throws(()=>f.service.cancel(a.id,'someone-else'));
  await assert.rejects(()=>SingingService.open({root,directory:f.directory,environment:async()=>({})}),/already_running/);
  const s=await f.reopen();assert.equal(s.context(owner).tasks[0].status,'ready');assert.equal(s.context('another-owner').tasks.length,0);const playback=s.reserve(a.id,owner,{sessionId:'day-two',operationId:'play-one'});assert.equal(playback.song.sha256,checkWav(bytes).sha256);assert.equal(playback.sessionId,'day-two');});
test('first line does not silently hit chorus; generation stays unapproved until all review checks pass',async t=>{let calls=0;const f=await fixture(t,{parse:async()=>plan('first_line'),generate:async()=>{calls++;return {bytes,report:{voice:Z_VOICE.speaker,model:Z_VOICE.singingModel,enableMusic:true,generationDone:true}};}});const s=f.service,a=submit(s);await s.tick();assert.equal(calls,1);assert.equal(s.context(owner).tasks[0].status,'awaiting_review');assert.throws(()=>s.reserve(a.id,owner,{sessionId:'two',operationId:'x'}),/not_ready/);assert.throws(()=>s.review(a.id,owner,{approved:true,note:'ok',checks:{voice:true}}),/incomplete/);
  s.review(a.id,owner,{approved:false,note:'返回了副歌，范围错误',checks:{segment:false}});assert.equal(s.context(owner).tasks[0].status,'failed');assert.equal(s.read().events.at(-1).note,'返回了副歌，范围错误');await f.reopen();assert.equal(f.service.context(owner).tasks[0].reason,'试听未通过');});
test('approved generated segment enters persistent cache; original failure history remains',async t=>{let calls=0;const f=await fixture(t,{parse:async()=>plan('verse'),generate:async()=>{calls++;return {bytes,report:{voice:Z_VOICE.speaker,model:Z_VOICE.singingModel,enableMusic:true,generationDone:true}};}});const s=f.service,a=submit(s);await s.tick();s.review(a.id,owner,{approved:true,note:'Injected audio review fixture; not real semantic verification',checks:{song:true,segment:true,voice:true,aCappella:true,ending:true}});submit(s,'second');await s.tick();assert.equal(calls,1);assert.equal(s.context(owner).tasks[1].assetId,s.context(owner).tasks[0].assetId);assert.equal(s.context(owner).knowledge.at(-1).independentScoreAvailable,false);});
test('cancel discards late generation and cannot revive task',async t=>{let release;const promise=new Promise(r=>release=r);const f=await fixture(t,{parse:async()=>plan('verse'),generate:async()=>promise});const s=f.service,a=submit(s),work=s.tick();while(s.context(owner).tasks[0].status!=='generating')await new Promise(r=>setTimeout(r,1));s.cancel(a.id,owner);release({bytes,report:{voice:Z_VOICE.speaker,model:Z_VOICE.singingModel,enableMusic:true,generationDone:true}});await work;assert.equal(s.context(owner).tasks[0].status,'cancelled');assert.equal(s.read().assets.length,1);});
test('restart during generating records unknown; retry requires explicit acknowledgement',async t=>{const f=await fixture(t);const a=submit(f.service);f.service.change(s=>{s.tasks[0].status='generating';s.tasks[0].attempt=1;});const s=await f.reopen();assert.equal(s.context(owner).tasks[0].status,'failed');assert.throws(()=>s.retry(a.id,owner),/confirm_unknown/);s.retry(a.id,owner,{acknowledgeUnknown:true});assert.equal(s.context(owner).tasks[0].status,'queued');assert(s.read().events.some(e=>e.code==='process_restarted_unknown'));});
test('playback receipts require actual start, matching instance and duration; duplicates do not finish twice',async t=>{const {service:s}=await fixture(t);const a=submit(s);await s.tick();const p=s.reserve(a.id,owner,{sessionId:'day-two',operationId:'play1'});assert.equal(s.reserve(a.id,owner,{sessionId:'day-two',operationId:'play1'}).playbackId,p.playbackId);
  const duration=checkWav(bytes).durationMs;assert.throws(()=>s.receipt(p.playbackId,owner,{eventId:'early',phase:'completed',positionMs:duration}),/invalid_completion/);s.receipt(p.playbackId,owner,{eventId:'start',phase:'playing',positionMs:0});assert.throws(()=>s.receipt(p.playbackId,owner,{eventId:'partial',phase:'completed',positionMs:100}),/invalid_completion/);
  const done={eventId:'end',phase:'completed',positionMs:duration};s.receipt(p.playbackId,owner,done);s.receipt(p.playbackId,owner,done);assert.equal(s.read().events.filter(e=>e.status==='completed').length,1);const p2=s.reserve(a.id,owner,{sessionId:'day-three',operationId:'play2'});assert.notEqual(p2.playbackId,p.playbackId);s.receipt(p2.playbackId,owner,{eventId:'stop',phase:'interrupted',positionMs:200});assert.equal(s.context(owner).tasks[0].status,'ready');assert.equal(s.read().events.at(-1).positionMs,200);});
test('voice change and tampered audio block playback',async t=>{const {service:s}=await fixture(t);const a=submit(s);await s.tick();s.change(v=>v.voice.revision++);assert.throws(()=>s.reserve(a.id,owner,{sessionId:'x',operationId:'x'}),/voice_configuration/);const asset=s.read().assets[0];await fs.writeFile(path.join(s.directory,'assets',asset.sha256+'.wav'),'changed');await assert.rejects(()=>s.audio(asset.id),/integrity/);});
test('main agent can submit exact source and play only trusted ready tasks; NPC does not get private memory',()=>{
  const c={host:{inbox:[{id:'player1',mine:true,text:'唱第一句'}]},activeSkill:null,mediaRuntime:{audioEnabled:true},singing:{enabled:true,tasks:[{id:'task1',status:'ready'}]}};
  const base={action:'tool',speech:'我试着准备这段。',mood:'warm',handledIds:['player1'],wakeAfterMs:5000,memory:''};
  assert.equal(parseAgentReply({...base,tool:{name:'prepare_song',args:{messageId:'player1'}}},{kind:'host',context:c}).tool.name,'prepare_song');
  assert.throws(()=>parseAgentReply({...base,tool:{name:'prepare_song',args:{messageId:'npc'}}},{kind:'host',context:c}));
  assert.throws(()=>parseAgentReply({...base,tool:{name:'play_prepared_song',args:{taskId:'task1'}}},{kind:'host',context:c}));
  assert.equal(parseAgentReply({...base,handledIds:[],tool:{name:'play_prepared_song',args:{taskId:'task1'}}},{kind:'host',context:{...c,host:{inbox:[]}}}).tool.name,'play_prepared_song');
  assert.throws(()=>parseAgentReply({...base,tool:{name:'play_prepared_song',args:{taskId:'not-ready'}}},{kind:'host',context:c}));
  assert.match(buildAgentRequest({kind:'host',context:c}).system,/后台唱歌适配/);
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
  assert.equal(task.status,'failed');assert.match(task.reason,/volc\.speech\.dialog/);assert.doesNotMatch(task.reason,/private/);
});
