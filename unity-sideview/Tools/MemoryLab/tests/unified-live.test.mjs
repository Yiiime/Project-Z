import test from 'node:test';
import assert from 'node:assert/strict';
import {Engine} from '../lib/engine.mjs';
import {Store} from '../lib/store.mjs';
import {Catalog} from '../lib/catalog.mjs';
import {LiveRoom} from '../lib/live-room.mjs';
const setup=t=>{const e=new Engine(new Store(),new Catalog(),{publicConfig:()=>({configured:false})});e.open('live_world','run1',null,{initialSeed:'live_world'});t.after(()=>e.store.close());return {e,live:new LiveRoom(e)};};
const info=c=>({emotion:c.character_state.info.emotion,changes:{},...(c.actor_id==='z'?{}:{support_willingness:c.character_state.info.support_willingness})});
test('compact model turn expands only the selected action and computes numeric snapshots',t=>{
 const {e}=setup(t);const c=e.context(e.player('live_world','z','你好'));
 const compact={dialogue:'你好。',action:{option_id:'wait_here'},info:info(c)};
 compact.info.changes.trust={delta:7,reason:'愿意耐心听我讲完。',source_event_ids:[c.interaction.source_event_id]};
 const result=e.policy.validate(c,compact);assert.equal(result.info.trust,c.character_state.info.trust+7);assert.equal(result.action.type,'wait');
 assert.throws(()=>e.policy.validate(c,{...compact,action:{option_id:'invented'}}));
 assert.throws(()=>e.policy.validate(c,{...compact,info:{...compact.info,trust:100}}));
});
test('dynamic refusal requirement is enforced by the backend, not just the prompt',t=>{
 const {e}=setup(t);e.tasks.offer('live_world','dock_sort','linxu');const c=e.context(e.taskResponse('live_world','dock_sort',false));
 assert.throws(()=>e.policy.validate(c,{dialogue:'那就算了。',action:{option_id:'wait_here'},info:info(c)}));
});
test('planner receives actual dock, day, period and weather in one context',async t=>{
 const {e}=setup(t);const w=e.world('live_world');w.day=3;w.period=2;w.weather='大雨';e.save(w);let seen;
 e.decide=c=>{seen=c;return {dialogue:'',action:{action_id:'wait_here',type:'wait',target:null,parameters:{},description:'等雨停'},info:{...c.character_state.info,changes:{}}};};
 await e.plan('live_world','z',['linxu'],{locationId:'dock',sceneId:'Street'});
 assert.equal(seen.environment.scene,'dock');assert.equal(seen.environment.map_context.current_place.id,'dock');assert.equal(seen.environment.day,3);assert.equal(seen.environment.weather,'大雨');
});
test('all eight characters join; private memories remain personal; published danmaku reaches everyone',t=>{
 const {e,live}=setup(t);e.observe('live_world','只有林序知道的小纸条',['linxu'],{wake:false});const b=live.open({worldId:'live_world',runId:'run1'});
 assert.equal(b.actors.length,8);const z=live.prepare({...b,actorId:'z'});assert(!JSON.stringify(z.context).includes('只有林序知道的小纸条'));
 const lin=live.prepare({...b,actorId:'linxu'});assert(JSON.stringify(lin.context).includes('只有林序知道的小纸条'));
 const turn={dialogue:'晚上好。',action:{type:'send_danmaku',intent:'reaction',evidence_ids:[]},info:info(lin.context)};
 const proposal=live.propose({...b,contextId:lin.contextId,turn});
 assert(!e.records(e.world('live_world'),'event').some(ev=>ev.text==='晚上好。'));
 const result=live.observe({...b,...proposal,event:{id:'m1',actorId:'linxu',text:turn.dialogue}});assert(result.committed);
 const event=e.event('live_world',result.eventId);assert.equal(event.visible.length,9);assert.equal(event.channel,'livestream');
 for(const actor of b.actors)assert(e.memory.history(e.world('live_world'),actor).some(m=>JSON.stringify(m).includes('晚上好')));
 const seq=e.world('live_world').seq;live.observe({...b,...proposal,event:{id:'m1',actorId:'linxu',text:turn.dialogue}});assert.equal(e.world('live_world').seq,seq);
});
test('unpublished proposals do not alter state, old rooms cannot write after reload, mandatory members cannot leave',t=>{
 const {e,live}=setup(t);const b=live.open({worldId:'live_world',runId:'run1'}),c=live.prepare({...b,actorId:'z'});
 const before=e.actor('live_world','z').info.trust;const i=info(c.context);i.changes.trust={delta:1,reason:'新的感受',source_event_ids:[c.context.interaction.source_event_id]};
 live.propose({...b,contextId:c.contextId,turn:{dialogue:'我在听。',action:{action:'speak',mood:'neutral',handledIds:[],wakeAfterMs:4000},info:i}});assert.equal(e.actor('live_world','z').info.trust,before);
 const npc=live.prepare({...b,actorId:'linxu'});assert.throws(()=>live.propose({...b,contextId:npc.contextId,turn:{dialogue:'',action:{type:'leave'},info:info(npc.context)}}));
 live.close(b);const cp=e.checkpoint('live_world');e.open('live_world','run2',cp);
 assert.throws(()=>live.observe({...b,event:{id:'late',actorId:'linxu',text:'旧时间线'}}));
});
test('same world seed restores first impressions without copying newer state',t=>{
 const {e}=setup(t);const values=Object.values(e.world('live_world').actors).map(a=>a.info.trust);assert(new Set(values).size>1);assert(values.every(n=>n>=30&&n<=70));
 const cp=e.checkpoint('live_world');e.open('live_world','run2',cp);assert.deepEqual(Object.values(e.world('live_world').actors).map(a=>a.info.trust),values);
});
test('growing playback prefixes are one live observation, interrupted speech stores only heard words',t=>{
 const {e,live}=setup(t),b=live.open({worldId:'live_world',runId:'run1'});
 for(const text of ['你','你好','你好，岛上的朋友'])live.observe({...b,event:{id:'p'+text.length,publicationId:'speech-1',actorId:'z',text,stage:'progress'}});
 assert.equal(e.records(e.world('live_world'),'event').filter(x=>x.speaker==='z').length,0);
 const heard=live.prepare({...b,actorId:'linxu'}).context.environment.live_room.heard_in_progress;
 assert.equal(heard.length,1);assert.equal(heard[0].text,'你好，岛上的朋友');
 live.observe({...b,event:{id:'interrupt',publicationId:'speech-1',actorId:'z',text:'session_ended',stage:'interrupted'}});
 const published=e.records(e.world('live_world'),'event').filter(x=>x.speaker==='z');assert.equal(published.length,1);assert.equal(published[0].text,'你好，岛上的朋友');
 live.close(b);assert.equal(e.records(e.world('live_world'),'event').filter(x=>x.speaker==='z').length,1);
});
test('recent own experience crosses live and island without importing other private conversations',t=>{
 const {e,live}=setup(t);e.observe('live_world','不属于Z的私人经历',['linxu'],{wake:false});const b=live.open({worldId:'live_world',runId:'run1'});
 live.observe({...b,event:{id:'player1',actorId:'player',text:'这次见面的暗号叫蓝色风筝'}});live.close(b);
 const c=e.context(e.player('live_world','z','刚才直播的暗号叫什么？'));
 assert(c.character_state.recent_experiences.some(m=>m.text.includes('蓝色风筝')));
 assert(!JSON.stringify(c.character_state.recent_experiences).includes('不属于Z的私人经历'));
});
test('compact communication schema carries the same conditional requirement as canonical storage',t=>{
 const {e}=setup(t);e.place('live_world','axing','dock');e.place('live_world','linxu','dock');e.scan('live_world');
 const c=e.context(e.ready('live_world').find(j=>j.actor==='axing'));
 const action=c.available_actions.find(a=>a.type==='communicate');assert(action);
 const compact={dialogue:'船况怎样？',action:{option_id:action.id},info:{...info(c),social_decision:{choice:'engage',reason:'关心朋友的船况',source_event_ids:[c.interaction.source_event_id]}}};
 const validate=e.policy.ajv.compile(e.policy.modelContract(c));assert.equal(validate(compact),false);assert(validate.errors.some(e=>e.keyword==='required'&&e.params.missingProperty==='communication'));
});
