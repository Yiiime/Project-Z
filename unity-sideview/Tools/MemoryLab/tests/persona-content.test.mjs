import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {Catalog} from '../lib/catalog.mjs';
import {Engine} from '../lib/engine.mjs';
import {Store} from '../lib/store.mjs';
import {NPCS} from '../../live-service/server/shared/worker/island-roster.js';
import {Z_PERSONA} from '../../live-service/server/shared/worker/z-persona.js';

const provider = {publicConfig: () => ({configured:false})};
const retired = ['map_blank','map_trial','map_packing','flowers'];
const make = (t,catalog = new Catalog()) => {
  const e = new Engine(new Store(),catalog,provider);
  t.after(() => e.store.close());
  return e;
};

test('display names and public professions agree across Unity, live and the stable actor catalog', () => {
  const c = new Catalog();
  const unity = JSON.parse(readFileSync(new URL('../../../Assets/Seabreeze/Resources/Memory/characters.json',import.meta.url)));
  assert.deepEqual([...c.actors.keys()], ['z','liyuan','liting','axing','zhengyi','xiaoxiaoqing','linxu','zhou_bobo']);
  assert.equal(c.actor('linxu').name,'留洋');
  assert.equal(c.actor('xiaoxiaoqing').name,'晴野');
  assert.equal(c.registry.legacy_aliases.qingye,'xiaoxiaoqing');
  for (const a of unity.actors) {
    assert.equal(a.displayName,c.actor(a.id).name);
    assert.equal(a.occupation,c.identity(a.id).occupation);
  }
  for (const a of NPCS) {
    assert.equal(a.display_name,c.actor(a.npc_id).name);
    assert.equal(a.profile,c.profile(a.npc_id));
  }
  assert.equal(Z_PERSONA,c.prompt('z'));
  assert.deepEqual(c.registry.live_policy.mandatory_actor_ids,[...c.actors.keys()]);
  const map=c.mapContext('z_home','去小小晴的花店和林序家');
  assert(map.asked_routes.some(p=>p.destination==='flower_shop'));
  assert(map.asked_routes.some(p=>p.destination==='linxu_home'));
});

test('creator truth, selective past and future stage scripts stay out of unrelated prompts', () => {
  const c = new Catalog();
  assert.match(c.profile('liyuan'),/最初的创造者YLY/);
  assert.match(c.profile('zhou_bobo'),/不知道如今住在森林小屋的梨园就是YLY/);
  assert.match(c.profile('zhengyi'),/亲眼看到梨园偷偷/);
  assert.match(c.profile('xiaoxiaoqing'),/不知道他曾偷偷浇水/);
  const publicText=c.sharedWorld();
  assert.doesNotMatch(publicText,/YLY|夺权|Luna|演员|监控者/);
  for (const id of c.actors.keys()) {
    const prompt=c.prompt(id);
    if (!['liyuan','zhou_bobo'].includes(id)) assert.doesNotMatch(prompt,/YLY|夺权|第二代Luna/);
    assert.doesNotMatch(prompt,/黄玫瑰②|求证后的首个16:00|换取恢复色彩感知|下个月花园养护耗材/);
  }
  assert(!c.registry.common_documents.some(p=>p.includes('story-stages')));
});

test('new game relationships represent different viewpoints without forcing support or prior facts', t => {
  const e=make(t);e.open('new-personas','run',null);
  const q=e.actor('new-personas','xiaoxiaoqing'),j=e.actor('new-personas','zhengyi');
  assert(q.peer_relations.linxu.trust>q.peer_relations.liyuan.trust);
  assert(j.peer_relations.liyuan.trust>j.peer_relations.zhou_bobo.trust);
  assert.notDeepEqual(q.peer_relations.linxu,e.actor('new-personas','linxu').peer_relations.xiaoxiaoqing);
  assert.equal(e.actor('new-personas','zhou_bobo').info.support_willingness,'unwilling');
  assert.equal(e.actor('new-personas','linxu').info.support_willingness,'unassessed');
  assert(!e.snapshot('new-personas').history.xiaoxiaoqing.some(x=>x.text?.includes('采购')));
});

test('retired and unreached tasks are absent from fresh model context and action options', t => {
  const e=make(t);e.open('new-personas','run',null);
  for(const actor of ['liyuan','xiaoxiaoqing','linxu']) {
    const c=e.context(e.player('new-personas',actor,'有什么要帮忙的？'));
    assert(!c.tasks.some(t=>retired.includes(t.id)||t.id==='departure_prep'));
    assert(!c.available_actions.some(a=>retired.includes(a.parameters?.task_id)));
  }
  assert(retired.every(id=>!e.tasks.state('new-personas',id).enabled));
  e.tasks.enable('new-personas','departure_prep');
  const c=e.context(e.player('new-personas','linxu','我们商量出航准备'));
  assert(c.tasks.some(t=>t.id==='departure_prep'));
});

test('legacy checkpoint keeps relationship history, task state and events when persona content changes', t => {
  const oldCatalog=new Catalog();
  for(const task of oldCatalog.tasks) if(retired.includes(task.id)) task.enabled_on_start=true;
  const old=make(t,oldCatalog);old.open('legacy','before',null);
  old.tasks.offer('legacy','map_blank','liyuan');old.tasks.respond('legacy','map_blank',true);
  const event=old.observe('legacy','这是一段旧局亲历，只属于Z。',['z'],{kind:'fact',wake:false});
  const w=old.world('legacy');w.actors.xiaoxiaoqing.peer_relations.liyuan.trust=91;old.save(w);
  const cp=old.checkpoint('legacy');
  const fresh=make(t);fresh.open('legacy','after',cp);
  assert.equal(fresh.actor('legacy','xiaoxiaoqing').peer_relations.liyuan.trust,91);
  assert.equal(fresh.tasks.state('legacy','map_blank').status,'active');
  assert.equal(fresh.tasks.state('legacy','map_blank').enabled,1);
  assert.deepEqual(fresh.event('legacy',event.id).visible,['z']);
  const c=fresh.context(fresh.player('legacy','liyuan','继续之前的委托'));
  assert(c.tasks.some(t=>t.id==='map_blank'&&t.status==='active'));
});
