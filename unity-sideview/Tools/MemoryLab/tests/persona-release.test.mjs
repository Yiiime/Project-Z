import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {Catalog,clone} from '../lib/catalog.mjs';
import {Store} from '../lib/store.mjs';
import {Engine} from '../lib/engine.mjs';
import {memoryProjection,privateOffer} from '../lib/live-memory.mjs';
const setup=t=>{const e=new Engine(new Store(),new Catalog(),{publicConfig:()=>({configured:false})});e.create('w');t.after(()=>e.store.close());return e;};
function response(c,target='liyuan'){
 const a=c.available_actions.find(a=>a.type==='wait');
 return {dialogue:'我之前可能误会他了。',action:{action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:'先问清楚'},info:{...clone(c.character_state.info),changes:{},peer_relation_changes:[{target_actor_id:target,affinity_delta:10,trust_delta:5,reason:'听到新的解释，暂时改观但还想当面问清',source_event_ids:[c.interaction.source_event_id]}]}};
}
test('new game uses directed story relationships without changing player starting bounds',t=>{
 const e=setup(t),q=e.actor('w','xiaoxiaoqing'),l=e.actor('w','linxu');
 assert(q.peer_relations.linxu.trust>q.peer_relations.liyuan.trust);
 assert.notEqual(q.peer_relations.linxu.affinity,l.peer_relations.xiaoxiaoqing.affinity);
 assert.equal(q.info.affinity,50);assert.equal(q.info.trust,50);
 for(const a of e.snapshot('w').actors)assert.equal(Object.keys(e.actor('w',a.id).peer_relations).length,7);
 assert.equal(e.actor('w','zhou_bobo').info.support_willingness,'unwilling');
});
test('player hearsay can change only this actor view after commit, with source and history retained',async t=>{
 const e=setup(t),before=clone(e.actor('w','xiaoxiaoqing')),reverse=clone(e.actor('w','liyuan').peer_relations.xiaoxiaoqing);
 const j=e.player('w','xiaoxiaoqing','我看见梨园给花浇水了，你可以去问问他。');e.decide=c=>response(c);
 const p=(await e.run('w',1,j.id))[0];assert.equal(p.status,'proposed',p.validation_error);
 assert.deepEqual(e.actor('w','xiaoxiaoqing').peer_relations,before.peer_relations);
 e.commit('w',p.proposalId);const q=e.actor('w','xiaoxiaoqing');
 assert.equal(q.peer_relations.liyuan.affinity,before.peer_relations.liyuan.affinity+10);
 assert.equal(q.peer_relations.liyuan.history.at(-1).source_actor_id,'player');
 assert.equal(q.peer_relations.liyuan.history.at(-1).source_kind,'claim');
 assert.deepEqual(q.info,before.info);assert.deepEqual(e.actor('w','liyuan').peer_relations.xiaoxiaoqing,reverse);
 assert(!e.memory.facts(e.world('w'),'xiaoxiaoqing').some(f=>f.summary.includes('浇水')));
 const changed=clone(q.peer_relations);e.commit('w',p.proposalId);assert.deepEqual(e.actor('w','xiaoxiaoqing').peer_relations,changed);
 const again=response(e.context(j));assert.throws(()=>e.policy.validate(e.context(j),again),/重复计分/);
});
test('relationship changes reject forged sources, unknown targets, nulls and out of range values',t=>{
 const e=setup(t),c=e.context(e.player('w','xiaoxiaoqing','说说梨园吧'));
 for(const modify of [o=>o.info.peer_relation_changes[0].source_event_ids=['private-other'],o=>o.info.peer_relation_changes[0].target_actor_id='new_npc',o=>o.info.peer_relation_changes[0].trust_delta=100,o=>o.info.peer_relation_changes[0].affinity_delta=null]){
  const o=response(c);modify(o);assert.throws(()=>e.policy.validate(c,o));
 }
 const old=clone(c);old.character_state.peer_relations.liyuan.trust=null;assert.throws(()=>e.policy.validate(old,response(old)));
});
test('old checkpoint keeps experienced edges, nulls, memory and task progress while filling missing edges',t=>{
 const e=setup(t);e.player('w','linxu','我们约好明天聊船。');e.tasks.enable('w','flowers');e.tasks.offer('w','flowers','xiaoxiaoqing');e.tasks.respond('w','flowers',true);
 const cp=e.checkpoint('w');cp.world.actors.linxu.name='林序';cp.world.actors.linxu.peer_relations={z:{affinity:9,trust:null,impression_formed:true,history:[{reason:'旧局经历'}]}};
 e.open('w','restored',cp);const a=e.actor('w','linxu');
 assert.equal(a.name,'留洋');assert.deepEqual(a.peer_relations.z,cp.world.actors.linxu.peer_relations.z);
 assert(a.peer_relations.xiaoxiaoqing.trust>50);assert.equal(e.tasks.state('w','flowers').status,'active');
 assert(e.memory.history(e.world('w'),'linxu').some(m=>m.text.includes('聊船')));
 const j=e.player('w','xiaoxiaoqing','上次送花的委托继续');assert(e.context(j).tasks.some(t=>t.id==='flowers'&&t.status==='active'));
});
test('new games do not offer retired tasks or preload future plots and family anchors',t=>{
 const e=setup(t);
 for(const id of ['map_blank','map_trial','map_packing','flowers'])assert(!e.tasks.state('w',id).enabled);
 for(const actor of ['liyuan','xiaoxiaoqing']){
  const c=e.context(e.player('w',actor,'有什么事需要帮忙'));assert(!c.tasks.some(t=>['map_blank','flowers'].includes(t.id)));
  assert(!c.available_actions.some(a=>['offer_map_blank','offer_flowers'].includes(a.id)));
 }
 const projection=memoryProjection(e.world('w'),[]);assert(!privateOffer(projection,'爸妈家里人',''));assert(!projection.stories.some(s=>s.id==='family_choices'));
 assert(!e.catalog.registry.common_documents.some(p=>p.includes('story-stages')));assert.equal(e.records(e.world('w'),'event').filter(e=>e.kind==='fact').length,0);
});
test('Unity public roster preserves IDs, motion and occupations without corporate secrets',t=>{
 const e=setup(t),r=JSON.parse(readFileSync(new URL('../../../Assets/Seabreeze/Resources/Memory/characters.json',import.meta.url)));
 assert.deepEqual(r.actors.map(a=>a.id),[...e.catalog.actors.keys()]);
 for(const a of r.actors){const p=e.catalog.identity(a.id);assert.equal(a.displayName,p.name);assert.equal(a.occupation,p.occupation);}
 assert.equal(r.actors.find(a=>a.id==='zhengyi').motion,'Art/ZhengyiMotion');
 assert(!JSON.stringify(r).match(/YLY|Luna|夺权|巡警演员/));
 const z=e.catalog.prompt('z'),zhou=e.catalog.profile('zhou_bobo'),liyuan=e.catalog.profile('liyuan');
 assert(!z.includes(zhou));assert(!z.includes(liyuan));assert(!e.catalog.sharedWorld().match(/YLY|Luna|夺权|制造者/));
});
