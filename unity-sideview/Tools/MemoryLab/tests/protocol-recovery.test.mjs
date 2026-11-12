import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createServer } from '../server.mjs';
import { createLab } from '../lib/lab.mjs';
import { LiveRoom } from '../lib/live-room.mjs';
import { storageContract } from '../lib/checkpoint.mjs';

const world = {worldId:'recovery',runId:'run1',day:1,period:0,syncMode:'reference-v1'};
const info = c => ({emotion:'serious',changes:{},...(c.actor_id==='z'?{}:{support_willingness:c.actor_id==='zhou_bobo'?'unwilling':'considering'})});
const decide = c => ({dialogue:'我在听。',action:{option_id:'wait_here'},info:info(c)});
async function setup(t, responder=decide, dbPath=':memory:') {
  const app=createServer({dbPath,backgroundEnabled:false,decide:responder,providerFactory:()=>({publicConfig:()=>({configured:true}),config:{},mode:'test'})});
  app.server.listen(0,'127.0.0.1');await once(app.server,'listening');
  const origin='http://127.0.0.1:'+app.server.address().port;
  let closed=false;
  const close=async()=>{if(closed)return;closed=true;await new Promise(resolve=>app.server.close(resolve));app.store.close();};t.after(close);
  const post=async(path,data)=>{const r=await fetch(origin+'/api/game/'+path,{method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify({...world,...data})});return {code:r.status,...await r.json()};};
  return {app,e:app.engine,post,close};
}
const event=(id,seq,text='你好')=>({id,seq,text,kind:'utterance',speakerId:'player',recipientId:'z',conversationId:'conversation1',channel:'private_player_chat',locationId:'z_home',day:1,period:0});
const request=(id='q1',source='input1')=>({requestId:id,actorId:'z',recipientId:'player',conversationId:'conversation1',sourceEventId:source,locationId:'z_home'});
async function input(post){assert.equal((await post('open',{})).code,200);await post('focus',{actorId:'z'});await post('sync',{eventData:event('input1',1)});}
const ack=(proposal,id='ack1',seq=2)=>({...event(id,seq),speakerId:'z',recipientId:'player',text:proposal.result.dialogue,proposalId:proposal.proposalId});

test('generation replay coalesces calls, survives service restart and rejects request ID conflicts',async t=>{
 const dir=mkdtempSync(join(tmpdir(),'z-protocol-'));t.after(()=>rmSync(dir,{recursive:true,force:true}));let calls=0;
 let f=await setup(t,async c=>{calls++;await new Promise(r=>setTimeout(r,15));return decide(c);},join(dir,'test.sqlite'));
 await input(f.post);const [a,b]=await Promise.all([f.post('utterance',request()),f.post('utterance',request())]);
 assert.equal(a.code,200,a.error);assert.equal(b.proposalId,a.proposalId);assert.equal(calls,1);
 assert.equal((await f.post('utterance',{...request(),locationId:'dock'})).code,400);
 await f.close();f=await setup(t,c=>{calls++;return decide(c);},join(dir,'test.sqlite'));
 const recovered=await f.post('utterance',request());assert.equal(recovered.code,200,recovered.error);assert.equal(recovered.proposalId,a.proposalId);assert.equal(calls,1);
 const alias=await f.post('utterance',request('q-new'));assert.equal(alias.proposalId,a.proposalId);assert.equal(calls,1);
 await f.post('cancel',{conversationId:'conversation1'});assert.equal((await f.post('utterance',request())).code,400);
});

test('newer player input supersedes an unacknowledged old reply without applying it',async t=>{
 const {post,e}=await setup(t,c=>{const r=decide(c);r.info.changes.trust={delta:1,reason:'认真倾听',source_event_ids:[c.interaction.source_event_id]};return r;});
 await input(post);const before=e.world(world.worldId).actors.z.info.trust,old=await post('utterance',request());
 await post('sync',{eventData:event('input2',2,'再说一句')});const fresh=await post('utterance',request('q2','input2'));
 assert.equal(fresh.code,200,fresh.error);assert.notEqual(fresh.proposalId,old.proposalId);
 assert.equal(e.store.record(old.proposalId).status,'cancelled');assert.equal(e.world(world.worldId).actors.z.info.trust,before);
 assert.equal((await post('sync',{eventData:ack(old,'late',3)})).code,400);
 assert.equal((await post('sync',{eventData:ack(fresh,'fresh',3)})).code,200);assert.equal(e.world(world.worldId).actors.z.info.trust,before+1);
 assert.equal((await post('utterance',request('old-with-new-id','input1'))).code,400);
});

test('a persisted running job without a proposal resumes after process restart',async t=>{
 const dir=mkdtempSync(join(tmpdir(),'z-interrupted-job-'));t.after(()=>rmSync(dir,{recursive:true,force:true}));let calls=0;
 const decideCount=c=>{calls++;return decide(c);};let f=await setup(t,decideCount,join(dir,'test.sqlite'));
 await input(f.post);const first=await f.post('utterance',request()),proposal=f.e.store.record(first.proposalId),job=f.e.store.record(proposal.job);
 // Model the durable boundary before a generated proposal was stored.
 f.e.store.db.prepare('DELETE FROM records WHERE id=?').run(proposal.id);job.status='running';delete job.proposal_id;f.e.store.put('job',job);
 await f.close();f=await setup(t,decideCount,join(dir,'test.sqlite'));
 const resumed=await f.post('utterance',request());assert.equal(resumed.code,200,resumed.error);assert.equal(calls,2);
 assert.equal(f.e.store.record(resumed.proposalId).job,job.id);
 assert.equal((await f.post('utterance',request())).proposalId,resumed.proposalId);assert.equal(calls,2);
});

test('duplicate proposal receipts consume sequence without repeating memories or numeric changes',async t=>{
 const {post,e}=await setup(t);await input(post);const p=await post('utterance',request());
 assert.equal((await post('sync',{eventData:ack(p)})).seq,2);
 const count=e.records(e.world(world.worldId),'memory').length;
 assert.equal((await post('sync',{eventData:ack(p,'ack-new',3)})).seq,3);
 assert.equal(e.records(e.world(world.worldId),'memory').length,count);
 assert.equal(e.event(world.worldId,'ack-new').type,'receipt_replay');
 assert.equal((await post('sync',{eventData:ack(p,'ack-new',3)})).seq,3);
 assert.equal((await post('sync',{eventData:{...ack(p,'ack-new',3),text:'不同原文'}})).code,400);
 assert.equal((await post('sync',{eventData:event('next',4)})).seq,4);
 assert.equal((await post('utterance',request('next','next'))).code,200);
});

test('silent commit and presented speech bind exact conversation and channel',async t=>{
 const {post,e}=await setup(t);await input(post);const p=await post('utterance',request());
 assert.equal((await post('commit',{actorId:'z',proposalId:p.proposalId,observers:['z','player']})).code,400);
 for(const patch of [{conversationId:'other'},{channel:'public_announcement'},{speakerId:'linxu'},{text:'错字'}])
  assert.equal((await post('sync',{eventData:{...ack(p),...patch}})).code,400);
 assert.equal(e.store.record(p.proposalId).status,'pending');assert.equal(e.world(world.worldId).seq,1);
 assert.equal((await post('sync',{eventData:ack(p)})).code,200);
});

test('movement receipts update evidence once, duplicate receipts remain consumable',async t=>{
 const {post,e}=await setup(t,c=>({...decide(c),action:{option_id:c.available_actions.find(a=>a.type==='move'&&a.target==='flower_shop').id}}));
 await input(post);const p=await post('utterance',{...request(),walkableLocations:['z_home','residential_middle','public_middle','flower_shop']});
 assert.equal(p.code,200,p.error);await post('sync',{eventData:ack(p)});
 const actionId=e.world(world.worldId).actors.z.pending_action;
 const receipt={id:'arrived',seq:3,kind:'action_result',day:1,period:0,actionId,succeeded:true,locationId:'flower_shop',text:'已实际到达花店。'};
 assert.equal((await post('sync',{eventData:{...receipt,locationId:'public_middle'}})).code,400);
 assert.equal((await post('sync',{eventData:receipt})).seq,3);
 const count=e.records(e.world(world.worldId),'memory').length;
 assert.equal((await post('sync',{eventData:{...receipt,id:'again',seq:4}})).seq,4);
 assert.equal(e.records(e.world(world.worldId),'memory').length,count);
 assert.equal(e.world(world.worldId).actors.z.scene,'flower_shop');
 const c=e.context(e.player(world.worldId,'z','到了吗','unity_run1'));
 assert.equal(c.previous_action_result.action_id,actionId);assert(c.character_state.execution_evidence.some(a=>a.action_id===actionId));
});

test('playback terminal rejects divergent text and prevents late progress from reopening',async t=>{
 const {e}=await setup(t);e.open(world.worldId,world.runId,null);const live=new LiveRoom(e),binding=live.open(world);
 const base={publicationId:'speech1',actorId:'z'};
 live.observe({...binding,event:{...base,id:'p',stage:'progress',text:'已经播出'}});
 assert.throws(()=>live.observe({...binding,event:{...base,id:'bad',stage:'completed',text:'完全不同'}}),/前缀/);
 assert.equal(e.world(world.worldId).liveRoom.partial.speech1.text,'已经播出');
 const final=live.observe({...binding,event:{...base,id:'done',stage:'completed',text:'已经播出的完整句子'}});
 assert(live.observe({...binding,event:{...base,id:'done-new-id',stage:'completed',text:'已经播出的完整句子'}}).duplicate);
 assert(live.observe({...binding,event:{...base,id:'late',stage:'progress',text:'已经'}}).ignored);
 assert(live.observe({...binding,event:{...base,id:'interrupt',stage:'interrupted',text:'原因'}}).duplicate);
 assert.throws(()=>live.observe({...binding,event:{...base,id:'bad-speaker',actorId:'linxu',stage:'completed',text:'已经播出的完整句子'}}));
 assert.equal(e.records(e.world(world.worldId),'event').filter(r=>r.speaker==='z').length,1);
 assert.equal(e.event(world.worldId,final.eventId).text,'已经播出的完整句子');live.close(binding);
});

test('partial interruption records only heard words and never applies full proposed Info',async t=>{
 const {e}=await setup(t);e.open(world.worldId,world.runId,null);const live=new LiveRoom(e),binding=live.open(world),c=live.prepare({...binding,actorId:'z'});
 const state=info(c.context);state.changes.trust={delta:1,reason:'听见了新看法',source_event_ids:[c.context.interaction.source_event_id]};
 const p=live.propose({...binding,contextId:c.contextId,turn:{dialogue:'你好，我想解释一下。',action:{action:'speak',mood:'neutral',handledIds:[],wakeAfterMs:4000},info:state}}),before=e.world(world.worldId).actors.z.info.trust;
 live.observe({...binding,...p,event:{id:'p1',publicationId:'speech2',actorId:'z',stage:'progress',text:'你好'}});
 live.close(binding);assert.equal(e.world(world.worldId).actors.z.info.trust,before);
 assert.equal(e.records(e.world(world.worldId),'event').filter(r=>r.speaker==='z').at(-1).text,'你好');
});

test('a failed live commit rolls back Info before recording actually heard speech',async t=>{
 const {e}=await setup(t);e.open(world.worldId,world.runId,null);const live=new LiveRoom(e),binding=live.open(world),c=live.prepare({...binding,actorId:'z'});
 const state=info(c.context);state.changes.trust={delta:1,reason:'听见新看法',source_event_ids:[c.context.interaction.source_event_id]};
 const p=live.propose({...binding,contextId:c.contextId,turn:{dialogue:'我听到了。',action:{action:'speak',mood:'neutral',handledIds:[],wakeAfterMs:4000},info:state}}),before=e.world(world.worldId).actors.z.info.trust;
 const commit=live.commit.bind(live);live.commit=(...args)=>{commit(...args);throw Error('injected after commit');};
 const result=live.observe({...binding,...p,event:{id:'published',publicationId:'faulted-commit',actorId:'z',stage:'completed',text:'我听到了。'}});
 assert.equal(result.committed,false);assert.equal(e.world(world.worldId).actors.z.info.trust,before);
 assert.equal(e.store.record(p.proposalId).status,'cancelled');assert.equal(e.event(world.worldId,result.eventId).text,'我听到了。');
 assert.equal(e.records(e.world(world.worldId),'event').filter(r=>r.speaker==='z').length,1);
});

test('invalid checkpoints and unexpected restore failures leave the current world untouched',async t=>{
 const {post,e}=await setup(t);await input(post);const p=await post('utterance',request());e.tasks.list(world.worldId);const saved=e.checkpoint(world.worldId);
 for(const mutate of [cp=>cp.world.id='world_other',cp=>cp.world.actors.z.info={},cp=>cp.tasks.state[0].unknown_column='bad',cp=>cp.tasks.state[0].progress='not-json',cp=>cp.world.actors.z.pending_action='missing',cp=>cp.records.event.push({...cp.records.event[0]})]) {
  const invalid=structuredClone(saved);mutate(invalid);assert.throws(()=>e.open(world.worldId,'bad',invalid));assert.deepEqual(e.checkpoint(world.worldId),saved);
 }
 e.open('other','other-run',null);const other=e.observe('other','另一个世界的事件',['z'],{wake:false}),otherSaved=e.checkpoint('other');
 const collision=structuredClone(saved);collision.records.event[0].id=other.id;
 assert.throws(()=>e.open(world.worldId,'collision',collision),/冲突/);assert.deepEqual(e.checkpoint('other'),otherSaved);assert.deepEqual(e.checkpoint(world.worldId),saved);
 const restore=e.tasks.restore.bind(e.tasks);let aborted=0;e.runtime.cancel=()=>aborted++;
 e.tasks.restore=(...args)=>{restore(...args);throw Error('injected restore fault');};
 assert.throws(()=>e.open(world.worldId,'bad',saved),/injected/);assert.deepEqual(e.checkpoint(world.worldId),saved);assert.equal(aborted,0);
 e.tasks.restore=restore;assert.equal((await post('sync',{eventData:ack(p)})).code,200);
 const good=e.checkpoint(world.worldId);e.open(world.worldId,'run2',good);assert.equal(aborted,1);assert.equal(e.world(world.worldId).runId,'run2');
});

test('lab exports raw task state and diagnostics without access credentials; shared storage types validate them',async t=>{
 const {e}=await setup(t),lab=createLab(e),created=await lab('new',{actorId:'linxu'}),scope={worldId:created.worldId,runId:created.runId,actorId:'linxu'};
 e.tasks.offer(scope.worldId,'dock_sort','linxu');e.decide=c=>({...decide(c),info:{...info(c),task_followup:{task_id:'dock_sort',choice:'stop',reason:'尊重拒绝',source_event_ids:[c.interaction.source_event_id]}}});
 const response=await lab('task/respond',{...scope,taskId:'dock_sort',accepted:false}),p=response.results[0];assert.equal(p.status,'proposed',p.validation_error);
 await lab('commit',{...scope,proposalId:p.proposalId,dialogue:p.output.dialogue});
 const w=e.world(scope.worldId);e.store.put('trace',{id:'redaction-test',experimentId:w.id,epoch:w.epoch,hour:w.hour,requests:{authorization:'fake-sensitive',body:{apiKey:'fake-key'}},responses:{},addedIds:[]});
 const exported=await lab('inspect',scope);assert.equal(exported.exportVersion,1);assert.equal(exported.backend.taskCheckpoint.followups.length,1);
 assert.equal(exported.backend.world.initialSeed,w.initialSeed);assert(!JSON.stringify(exported).includes('fake-sensitive'));assert(!JSON.stringify(exported).includes('fake-key'));
 const schema=storageContract(e.catalog,e.policy),check=e.policy.ajv.compile({...schema,$ref:'#/$defs/PortableCheckpoint'});assert(check(e.checkpoint(scope.worldId)),JSON.stringify(check.errors));
 const job=e.records(w,'job').at(-1),jobCheck=e.policy.ajv.compile({...schema,$ref:'#/$defs/JobRecord'});
 assert(jobCheck({...job,error:'校验未通过',rejected_output:{bad:true}}));assert.equal(jobCheck({...job,error:42}),false);
 e.open(scope.worldId,'reload',e.checkpoint(scope.worldId));assert.equal(e.tasks.snapshot(scope.worldId).followups.length,1);
});
