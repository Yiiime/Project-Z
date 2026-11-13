import test from 'node:test';
import assert from 'node:assert/strict';
import {Store} from '../lib/store.mjs';
import {Catalog} from '../lib/catalog.mjs';
import {Engine} from '../lib/engine.mjs';
function fixture(t){const e=new Engine(new Store(),new Catalog(),{publicConfig:()=>({configured:false})});e.create('w');t.after(()=>e.store.close());return e;}
function output(c,type='wait',target){const a=c.available_actions.find(a=>a.type===type&&(!target||a.target===target));assert(a);const info=structuredClone(c.character_state.info);Object.assign(info,{changes:{},memory_updates:[],knowledge_updates:[]});return {dialogue:'好，我们一起去。',action:{type:a.type,target:a.target,parameters:a.parameters,action_id:a.id,description:'按约定行动'},info};}
function job(e,text='一起去码头吧'){const j=e.player('w','z',text);j.environment={companion_routes:[['z_home','dock']]};e.store.put('job',j);return j;}
async function commit(e,j,make){e.decide=make;const r=(await e.run('w',1,j.id))[0];assert.equal(r.status,'proposed',r.validation_error);e.commit('w',r.proposalId);return r;}
const accept=c=>{const o=output(c,'escort_player','dock');o.info.travel_decision={choice:'accept',destination_id:'dock'};return o;};
test('player identity remains distinct from Z even after an old misidentification',async t=>{
 const e=fixture(t);const c=e.context(e.player('w','zhengyi','你不认识我了吗 我好想你'));
 assert.deepEqual(c.interaction.counterpart,{actor_id:'player',name:'玩家',role:'player'});assert.equal(c.player.is_z,false);
 const wrong=output(c);wrong.dialogue='认识啊，Z。你今天怎么突然这么说话？';assert.throws(()=>e.policy.validate(c,wrong),/身份边界/);
 let calls=0;const good={...output(c),dialogue:'抱歉，刚才叫错了。你接着说。'};
 e.runtime.provider={publicConfig:()=>({configured:true}),request:messages=>({messages}),call:async request=>{calls++;assert(request.messages[0].content.includes('player 永远是独立于 z'));return {text:JSON.stringify(calls===1?wrong:good),finishReason:'stop'};}};
 assert.equal((await e.runtime.generate(c)).dialogue,good.dialogue);assert.equal(calls,2);
 const mention={...good,dialogue:'Z刚才在码头，你是来找她的吗？'};assert.doesNotThrow(()=>e.policy.validate(c,mention));
});
test('acceptance owns a durable full destination and cannot finish without physical progress',async t=>{
 const e=fixture(t),j=job(e);await commit(e,j,accept);let a=e.actor('w','z');const id=a.pending_action;
 assert.equal(e.store.record(id).target,'dock');assert.equal(a.scene,'z_home');
 assert.throws(()=>e.actionReceipt('w',id,true,{scene:'dock'}),/逐段/);
 const c=e.context(e.player('w','z','走！'));assert.equal(c.character_state.pending_action.id,id);assert(!c.available_actions.some(x=>x.type==='escort_player'));
 await commit(e,e.store.record(c.turn_id),ctx=>({...output(ctx),dialogue:'走，等你关上对话框。'}));assert.equal(e.actor('w','z').previous_result.status,'pending');
 const cp=e.checkpoint('w');e.open('w','reload',cp);assert.equal(e.actor('w','z').pending_action,id);
 await assert.rejects(e.plan('w','z',['linxu']),/已承诺行动/);
 e.actionTransition('w',id,{scene:'dock',place:'Harbor',x:17,arrival_id:'Harbor_Street_return'},{id:'transition'});
 assert.equal(e.store.record(id).status,'pending');assert.equal(e.store.record(id).progress_index,0);assert.equal(e.store.record(id).physical_position.place,'Harbor');
 assert.throws(()=>e.actionReceipt('w',id,true,{scene:'dock'}),/逐段/);
 e.actionProgress('w',id,1,'dock',{id:'progress'});e.actionReceipt('w',id,true,{scene:'dock',summary:'实际到达码头'},'receipt');
 assert.equal(e.store.record(id).status,'succeeded');assert.equal(e.actor('w','z').scene,'dock');assert.equal(e.actor('w','z').pending_action,null);
});
test('refusal does not create locomotion, and accepted travel may later be cancelled',async t=>{
 const e=fixture(t);await commit(e,job(e),c=>{const o=output(c);o.dialogue='这会儿没空，晚点吧。';o.info.travel_decision={choice:'postpone',destination_id:'dock'};return o;});assert.equal(e.actor('w','z').pending_action,null);
 await commit(e,job(e),accept);const id=e.actor('w','z').pending_action;
 await commit(e,e.player('w','z','先别去了'),c=>{const o=output(c,'cancel_action',id);o.info.travel_decision={choice:'cancel',destination_id:'dock'};return o;});
 assert.equal(e.store.record(id).status,'cancelled');assert.equal(e.actor('w','z').pending_action,null);
 e.actionReceipt('w',id,true,{scene:'dock'},'late',{id:'late'});assert.equal(e.store.record(id).status,'cancelled');assert.equal(e.actor('w','z').scene,'z_home');
});
test('accepting in metadata with a wait or a single-hop move is rejected',t=>{
 const e=fixture(t),c=e.context(job(e));const o=output(c);o.info.travel_decision={choice:'accept',destination_id:'dock'};assert.throws(()=>e.policy.validate(c,o),/同行决定/);
 delete o.info.travel_decision;assert.throws(()=>e.policy.validate(c,o),/同行决定/);
});
