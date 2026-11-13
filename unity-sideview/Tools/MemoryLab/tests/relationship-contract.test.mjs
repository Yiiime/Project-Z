import test from 'node:test';
import assert from 'node:assert/strict';
import {Engine} from '../lib/engine.mjs';
import {Store} from '../lib/store.mjs';
import {Catalog} from '../lib/catalog.mjs';
import {LiveRoom} from '../lib/live-room.mjs';
import {createLab} from '../lib/lab.mjs';
const setup=t=>{const e=new Engine(new Store(),new Catalog(),{publicConfig:()=>({configured:false})});e.open('relations','run',null);t.after(()=>e.store.close());return e;};
const compact=c=>({dialogue:c.interaction.mode==='player'?'我会自己判断。':'',action:{option_id:'wait_here'},info:{emotion:'serious',changes:{},...(c.actor_id==='z'?{}:{support_willingness:c.character_state.info.support_willingness})}});
const change=(c,target,affinity=-3,trust=-7)=>({target_actor_id:target,affinity_delta:affinity,trust_delta:trust,reason:'听到了新的说法，结合旧交情，我有所怀疑，但尚未核实。',source_event_ids:[c.interaction.source_event_id]});
test('player hearsay affects multiple directed relations, never absent NPC minds or verified facts',async t=>{
 const e=setup(t),before=e.actor('relations','zhou_bobo').peer_relations.linxu,j=e.player('relations','linxu','周伯伯让李庭拿走了钥匙，我只听别人说的。');
 e.decide=c=>{const r=compact(c);r.info.peer_relation_changes=[change(c,'zhou_bobo',-10,-20),change(c,'liting')];return r;};
 const result=(await e.run('relations',1,j.id))[0];assert.equal(result.status,'proposed',result.validation_error);
 assert.equal(e.actor('relations','linxu').peer_relations.zhou_bobo.trust,55);e.commit('relations',result.proposalId);
 const a=e.actor('relations','linxu');assert.equal(a.peer_relations.zhou_bobo.trust,35);assert.equal(a.peer_relations.liting.trust,43);
 assert.deepEqual(e.actor('relations','zhou_bobo').peer_relations.linxu,before);assert.equal(a.peer_relations.zhou_bobo.history[0].source_kind,'claim');assert(!e.memory.facts(e.world('relations'),'linxu').some(f=>f.summary.includes('钥匙')));
 e.commit('relations',result.proposalId);assert.equal(e.actor('relations','linxu').peer_relations.zhou_bobo.trust,35);
 const cp=e.checkpoint('relations');e.open('relations','reload',cp);assert.equal(e.actor('relations','linxu').peer_relations.zhou_bobo.trust,35);
});
test('relations require visible evidence, unique known targets, real changes and valid bounds',t=>{
 const e=setup(t),c=e.context(e.player('relations','linxu','你对朋友们的看法？'));
 for(const patches of [[change(c,'linxu')],[change(c,'liting'),change(c,'liting')],[change(c,'liting',0,0)],[change(c,'liting',-90,-7)],[{...change(c,'liting'),source_event_ids:['secret_unseen']}]] ){
  const r=compact(c);r.info.peer_relation_changes=patches;assert.throws(()=>e.policy.validate(c,r));
 }
});
test('the model contract requires the current trigger even when a visible older fact informs the judgment',t=>{
 const e=setup(t),fact=e.observe('relations','之前亲眼见到的情况',['linxu'],{kind:'fact',wake:false}),c=e.context(e.player('relations','linxu','那件事你怎么看？'));
 const output=compact(c);output.info.peer_relation_changes=[{...change(c,'zhou_bobo'),source_event_ids:[fact.id]}];
 assert.throws(()=>e.policy.validate(c,output));output.info.peer_relation_changes[0].source_event_ids.push(c.interaction.source_event_id);assert.doesNotThrow(()=>e.policy.validate(c,output));
});
test('later counterevidence repairs trust and migration preserves existing relation values',async t=>{
 const e=setup(t),w=e.world('relations');w.actors.linxu.peer_relations.zhou_bobo.trust=5;delete w.actors.linxu.peer_relations.liting;e.save(w);
 const j=e.player('relations','linxu','我刚才误会了，他是在保护Z。');e.decide=c=>{assert.equal(c.character_state.peer_relations.liting.trust,50);const r=compact(c);r.info.peer_relation_changes=[change(c,'zhou_bobo',2,40)];return r;};
 const result=(await e.run('relations',1,j.id))[0];assert.equal(result.status,'proposed',result.validation_error);e.commit('relations',result.proposalId);assert.equal(e.actor('relations','linxu').peer_relations.zhou_bobo.trust,45);
});
test('conversation source rejects cross-conversation and unread events, accepts actual meeting',t=>{
 const e=setup(t),job=e.player('relations','z','只在旧对话说过'),event=e.event('relations',job.event);
 assert.throws(()=>e.utteranceSource('relations','z','player','new_conversation'));assert.throws(()=>e.utteranceSource('relations','linxu','player',event.conversationId,event.id));
 assert.equal(e.utteranceSource('relations','z','player',event.conversationId,event.id).id,event.id);
 const meet=e.observe('relations','实际同场',['z','linxu'],{wake:false,conversationId:'meet'});assert.equal(e.utteranceSource('relations','z','linxu','meet',meet.id).id,meet.id);
});
test('malformed checkpoint is rejected before cancelling pending work or changing the current world',async t=>{
 const e=setup(t),j=e.player('relations','z','你好');e.decide=compact;
 const result=(await e.run('relations',1,j.id))[0];assert.equal(result.status,'proposed');
 const before=e.checkpoint('relations');
 for(const mutate of [cp=>delete cp.tasks.state,cp=>delete cp.records.event,cp=>cp.records.event={},cp=>cp.world.actors.z.peer_relations.linxu.trust=101,cp=>cp.records.unknown=[]]){
  const invalid=structuredClone(before);mutate(invalid);assert.throws(()=>e.open('relations','bad_run',invalid));
  assert.deepEqual(e.checkpoint('relations'),before);assert.equal(e.store.record(result.proposalId).status,'pending');
 }
});
test('live entry rejects incomplete action and unknown stage, accepts valid source-backed relation changes',t=>{
 const e=setup(t),live=new LiveRoom(e),b=live.open({worldId:'relations',runId:'run'}),prepared=live.prepare({...b,actorId:'linxu'}),c=prepared.context;
 assert.throws(()=>live.propose({...b,contextId:prepared.contextId,turn:{...compact(c),action:{type:'send_danmaku'},dialogue:'你好'}}));assert.throws(()=>live.observe({...b,event:{id:'e',actorId:'player',text:'你好',stage:'typo'}}));
 const turn={...compact(c),dialogue:'我暂时保留意见。',action:{type:'send_danmaku',intent:'reaction',evidence_ids:[]}};turn.info.peer_relation_changes=[change(c,'zhou_bobo')];const p=live.propose({...b,contextId:prepared.contextId,turn});assert(live.observe({...b,...p,event:{id:'e2',actorId:'linxu',text:turn.dialogue,stage:'message'}}).committed);
});
test('lab shares engine, isolates save and commits only after exact presentation without physical completion',async t=>{
 const e=setup(t),lab=createLab(e),created=await lab('new',{actorId:'linxu'}),p={worldId:created.worldId,runId:created.runId,actorId:'linxu'};
 e.decide=c=>{const r=compact(c);r.info.peer_relation_changes=[change(c,'zhou_bobo')];return r;};const r=await lab('turn',{...p,text:'听说周伯伯不愿意让Z出去。'}),proposal=r.results[0];assert.equal(proposal.status,'proposed');assert.equal(e.actor(p.worldId,'linxu').peer_relations.zhou_bobo.trust,55);
 await assert.rejects(lab('commit',{...p,proposalId:proposal.proposalId,dialogue:'另一句话'}));const committed=await lab('commit',{...p,proposalId:proposal.proposalId,dialogue:proposal.output.dialogue});assert.equal(committed.state.actors.find(a=>a.id==='linxu').peer_relations.zhou_bobo.trust,48);
 assert.equal(committed.records.action.length,0);assert.equal(e.actor('relations','linxu').peer_relations.zhou_bobo.trust,55);await assert.rejects(lab('inspect',{worldId:'relations',runId:'run'}));await assert.rejects(lab('encounter',{...p,targetId:'z'}));
});
test('lab NPC exchange continues after background reflection diagnostics and does not auto-scan extra encounters',async t=>{
 const e=setup(t),lab=createLab(e),created=await lab('new',{actorId:'linxu'}),p={worldId:created.worldId,runId:created.runId,actorId:'linxu'};
 await lab('focus',{...p,focusActorId:null});await lab('place',{...p,locationId:'dock'});await lab('place',{...p,actorId:'z',locationId:'dock'});
 e.decide=c=>{const r=compact(c),a=c.available_actions.find(a=>a.type==='communicate');r.dialogue='你好，今天怎么样？';r.action={option_id:a.id};r.info.communication={intent:'inform',shared_claim_ids:[],source_event_ids:[c.interaction.source_event_id]};r.info.social_decision={choice:'engage',reason:'实际同场，打个招呼',source_event_ids:[c.interaction.source_event_id]};return r;};
 let r=(await lab('encounter',{...p,targetId:'z'})).results[0];assert.equal(r.status,'proposed');const first=await lab('commit',{...p,proposalId:r.proposalId,dialogue:r.output.dialogue});
 const conv=first.records.proposal.find(x=>x.id===r.proposalId).conversation;
 const diagnostic=e.store.event(e.world(p.worldId),'reflection','林序整理了近期经历',{actorId:'linxu'});
 r=(await lab('continue',{...p,conversationId:conv})).results[0];assert.equal(r.status,'proposed');assert.equal(r.actor,'z');
 const next=await lab('commit',{...p,proposalId:r.proposalId,dialogue:r.output.dialogue});assert(next.records.event.some(x=>x.id===diagnostic.id));
 assert(!next.state.history.z.some(x=>x.id===diagnostic.id));assert.equal(next.records.event.filter(x=>x.kind==='encounter').length,1);
});
