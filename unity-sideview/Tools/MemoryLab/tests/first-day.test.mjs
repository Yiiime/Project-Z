import test from 'node:test';
import assert from 'node:assert/strict';
import {once} from 'node:events';
import {createServer} from '../server.mjs';
import {Store} from '../lib/store.mjs';
import {Catalog} from '../lib/catalog.mjs';
import {Engine} from '../lib/engine.mjs';
import {FirstDayAgent,storyContext,allowedStoryTools,validateWelcomeTask,FIRST_DAY_CAST} from '../lib/first-day.mjs';
const context=(phase='Live',extra={})=>({phase,version:1,playerName:'小海',greeted:[],...extra});
const capabilities=[{type:'talk_to',target:'linxu',location:'ancient_tree_square'},{type:'take_photo',target:'Square',location:'ancient_tree_square'}];
const generated=()=>({title:'这次相遇',description:'现场生成的两步委托',steps:[{type:'talk_to',target:'linxu',label:'和留洋聊聊今天的海'},{type:'take_photo',target:'Square',label:'拍下眼前的榕树'}]});
const make=t=>{const store=new Store(),catalog=new Catalog(),provider={publicConfig:()=>({configured:false})},engine=new Engine(store,catalog,provider);engine.create('one');engine.create('two');for(const world of ['one','two'])engine.updateEnvironment(world,'linxu',{sceneId:'Square',locationId:'ancient_tree_square',x:0,y:0,movementState:'idle'});t.after(()=>store.close());return {store,catalog,provider,engine};};
test('welcome task native offer, acceptance, dialogue, photo and reload protocol',async t=>{
  let generations=0;
  const app=createServer({dbPath:':memory:',backgroundEnabled:false,
    providerFactory:()=>({mode:'simulated-test',config:{topK:6},publicConfig:()=>({configured:true}),request:messages=>({messages}),call:async()=>{generations++;app.engine.updateEnvironment('welcome_native','axing',{sceneId:'Square',locationId:'ancient_tree_square',x:4,y:0,movementState:'idle'},{id:'during_task_generation',hostSeq:1,day:1,period:3});return {text:JSON.stringify(generated())};}}),
    decide:c=>{const a=c.available_actions.find(a=>a.type==='offer_task')||c.available_actions.find(a=>a.type==='wait');return {dialogue:'测试生成的现场交流',action:{action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:'测试模型行动'},info:structuredClone(c.character_state.info)};}});
  app.server.listen(0,'127.0.0.1');await once(app.server,'listening');
  t.after(async()=>{app.server.close();await once(app.server,'close');app.store.close();});
  const base={worldId:'welcome_native',runId:'run1',day:1,period:3};
  let ctx=context('WelcomeTask',{announced:true,currentActor:'z',conversation:'chat_z',place:'Square',photoIds:['old_photo']});
  const post=async(path,body={},ok=true)=>{const r=await fetch(`http://127.0.0.1:${app.server.address().port}/api/game/${path}`,{method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify({...base,details:{firstDay:ctx},...body})});const result=await r.json();if(ok)assert.equal(r.status,200,result.error);else assert.notEqual(r.status,200);return result;};
  const say=async(actor,conversation,id)=>post('sync',{eventData:{id,seq:app.engine.world(base.worldId).seq+1,day:1,period:3,kind:'utterance',speakerId:'player',recipientId:actor,conversationId:conversation,text:'今天有什么想一起做的事？',locationId:'ancient_tree_square',channel:'private_player_chat',observers:['player',actor]}});
  const commit=async(actor,conversation,id,p)=>post('sync',{eventData:{id,seq:app.engine.world(base.worldId).seq+1,day:1,period:3,kind:'utterance',speakerId:actor,recipientId:'player',conversationId:conversation,text:p.text,proposalId:p.proposalId,locationId:'ancient_tree_square',channel:'private_player_chat',observers:['player',actor]}});
  await post('open',{syncMode:'reference-v1'});
  app.engine.updateEnvironment(base.worldId,'linxu',{sceneId:'Square',locationId:'ancient_tree_square',x:0,y:0,movementState:'idle'});
  await post('sync',{eventData:{id:'story_'+base.worldId+'_encounter_chat_z',seq:app.engine.world(base.worldId).seq+1,day:1,period:3,kind:'story_event',conversationId:'chat_z',text:'小海实际走近Z准备交谈',verified:true,locationId:'ancient_tree_square',observers:['player','z']}});
  await post('focus',{actorId:'z'});
  const proposed=await post('utterance',{actorId:'z',recipientId:'player',conversationId:'chat_z',locationId:'ancient_tree_square'});
  const projection=JSON.parse(proposed.checkpoint);assert.equal(projection.version,3);
  const definition=projection.tasks.definitions[0];assert(definition.dynamic);
  assert.equal(projection.world.actors.axing.runtime_input_seq,1,'Task generation must preserve runtime reports received during its await');
  assert.equal(app.engine.tasks.state(base.worldId,definition.id).status,'available','Generation alone must not publish the task');
  await commit('z','chat_z','offer_said',proposed);
  assert.equal(app.engine.tasks.state(base.worldId,definition.id).status,'offered');
  await post('task/respond',{taskId:definition.id,accepted:true});
  const receipt=(step,action,target,evidence)=>({taskId:definition.id,stepId:step,receiptId:'receipt_'+step,succeeded:true,performerId:'player',details:{firstDay:ctx,action,target,evidence_id:evidence}});
  await post('task/receipt',receipt('step_1','talk_to','linxu','offer_said'),false);
  await post('cancel',{actorId:'z',conversationId:'chat_z'});
  ctx={...ctx,currentActor:'linxu',conversation:'chat_linxu'};
  await post('focus',{actorId:'linxu'});await say('linxu','chat_linxu','p2');
  const answer=await post('utterance',{actorId:'linxu',recipientId:'player',conversationId:'chat_linxu',locationId:'ancient_tree_square'});
  await commit('linxu','chat_linxu','real_reply',answer);
  await post('task/receipt',receipt('step_1','talk_to','linxu','real_reply'));
  await post('task/receipt',receipt('step_2','take_photo','Square','old_photo'),false);
  const cp=await post('checkpoint');base.runId='run2';await post('open',{checkpoint:cp.checkpoint});
  ctx={...ctx,photoIds:['old_photo','new_photo']};
  const done=await post('task/receipt',receipt('step_2','take_photo','Square','new_photo'));
  assert.equal(JSON.parse(done.checkpoint).tasks.state.find(r=>r.task===definition.id).status,'completed');
  ctx={...ctx,place:'Street',photoIds:['old_photo','new_photo','later_photo']};
  await post('task/receipt',receipt('step_2','take_photo','Square','new_photo'));
  assert.equal(generations,1);
});

test('stale task generation cannot register into a reopened conversation',async t=>{
  const {engine,catalog}=make(t);let resolve;
  const provider={publicConfig:()=>({configured:true}),request:()=>({}),call:()=>new Promise(r=>resolve=r)};
  const agent=new FirstDayAgent(engine,provider,catalog);agent.update('one',context('WelcomeTask',{currentActor:'z',conversation:'old'}));engine.focus('one','z','player');
  const pending=agent.prepareTask('one','z','old');agent.update('one',context('WelcomeTask',{currentActor:'z',conversation:'new'}));
  resolve({text:JSON.stringify(generated())});await assert.rejects(pending,/现场已改变/);assert.equal(engine.tasks.dynamicDefinitions('one').length,0);
});
test('onboarding gates require three distinct present actors and never require Liyuan',()=>{
  assert.throws(()=>storyContext(context('MeetIslanders',{greeted:['liyuan']})));
  assert(!allowedStoryTools(storyContext(context('MeetIslanders',{announced:true,greeted:Array(7).fill('z')}))).includes('show_fireworks'));
  assert(allowedStoryTools(storyContext(context('MeetIslanders',{announced:true,greeted:FIRST_DAY_CAST.slice(0,3)}))).includes('show_fireworks'));
  assert(!allowedStoryTools(storyContext(context('Harbor'))).includes('offer_walk'));
  assert(allowedStoryTools(storyContext(context('Harbor',{harborReady:true}))).includes('offer_walk'));
});
test('daily room only becomes available after four rests',()=>{
  for(let n=0;n<4;n++)assert(!allowedStoryTools(storyContext(context('DailyReady',{restActions:n}))).includes('open_live'));
  assert(allowedStoryTools(storyContext(context('DailyReady',{restActions:4}))).includes('open_live'));
  assert(allowedStoryTools(storyContext(context('EveningReady'))).includes('open_live'));
});
test('generated tasks are compositions of actual capabilities, not arbitrary text assertions',()=>{
  const task=validateWelcomeTask(generated(),'z',capabilities,'welcome_one');
  assert.deepEqual(task.steps[1].depends_on,['step_1']);
  for(const patch of [{type:'repair',target:'generator'},{type:'talk_to',target:'nonexistent'},{type:'take_photo',target:'Forest'}]){
    const raw=generated();raw.steps[0]={...raw.steps[0],...patch};assert.throws(()=>validateWelcomeTask(raw,'z',capabilities,'welcome_one'));
  }
  const duplicated=generated();duplicated.steps[1]={...duplicated.steps[0]};assert.throws(()=>validateWelcomeTask(duplicated,'z',capabilities,'welcome_one'));
});
test('dynamic definitions remain isolated even with the same task id and survive checkpoint restore',t=>{
  const {engine}=make(t),task=validateWelcomeTask(generated(),'z',capabilities,'welcome_one');
  engine.tasks.register('one',task);
  assert(!engine.tasks.list('two').some(t=>t.id===task.id));
  assert.throws(()=>engine.tasks.definition(task.id,'two'));
  engine.tasks.register('two',{...task,title:'另一份存档'});
  assert.equal(engine.tasks.definition(task.id,'one').title,task.title);
  assert.equal(engine.tasks.definition(task.id,'two').title,'另一份存档');
  const snapshot=engine.tasks.snapshot('one');engine.tasks.restore('one',{});
  assert.throws(()=>engine.tasks.definition(task.id,'one'));
  engine.tasks.restore('one',snapshot);assert.equal(engine.tasks.definition(task.id,'one').title,task.title);
});
test('dynamic completion requires acceptance, dependencies and matching action receipts',t=>{
  const {engine}=make(t),task=validateWelcomeTask(generated(),'z',capabilities,'welcome_one');engine.tasks.register('one',task);
  const evidence={action:'talk_to',target:'linxu',evidence_id:'actual_reply'};
  assert.throws(()=>engine.tasks.record('one',task.id,'step_1','r1',true,'player',evidence,{}));
  engine.tasks.offer('one',task.id,'z');engine.tasks.respond('one',task.id,true);
  assert.throws(()=>engine.tasks.record('one',task.id,'step_2','r2',true,'player',{action:'take_photo',target:'Square',evidence_id:'actual_photo'},{}));
  assert.throws(()=>engine.tasks.record('one',task.id,'step_1','bad',true,'player',{...evidence,target:'z'},{}));
  assert(engine.tasks.record('one',task.id,'step_1','r1',true,'player',evidence,{}));
  assert.equal(engine.tasks.record('one',task.id,'step_1','r1',true,'player',evidence,{}),false);
  assert(engine.tasks.record('one',task.id,'step_2','r2',true,'player',{action:'take_photo',target:'Square',evidence_id:'actual_photo'},{}));
  assert.equal(engine.tasks.state('one',task.id).status,'completed');
});
test('main Agent is called for scene decisions and cannot invoke a forbidden tool',async t=>{
  const {engine,catalog}=make(t);let calls=0,tool='open_live';
  const provider={publicConfig:()=>({configured:true}),request:messages=>({messages}),call:async()=>{calls++;return {text:JSON.stringify({tool,title:'',description:'',speech:''})};}};
  const agent=new FirstDayAgent(engine,provider,catalog),run=engine.world('one').runId;
  agent.update('one',context());
  assert.equal((await agent.plan('one',run,'request_1')).tool,'open_live');
  await agent.plan('one',run,'request_1');assert.equal(calls,1);
  agent.update('one',context('Harbor',{version:2}));
  await assert.rejects(agent.plan('one',run,'request_2'),/提案无效/);
});
test('late main Agent results cannot enter a replacement stage',async t=>{
  const {engine,catalog}=make(t);let resolve;
  const provider={publicConfig:()=>({configured:true}),request:()=>({}),call:()=>new Promise(r=>resolve=r)};
  const agent=new FirstDayAgent(engine,provider,catalog);agent.update('one',context());
  const pending=agent.plan('one',engine.world('one').runId,'late');
  agent.update('one',context('Harbor',{version:2}));resolve({text:JSON.stringify({tool:'open_live',speech:''})});
  await assert.rejects(pending,/过期/);assert(!engine.world('one').storyPlans?.late);
});

test('a second generated welcome task cannot be offered while one is accepted',t=>{
 const {engine}=make(t);engine.tasks.register('one',validateWelcomeTask(generated(),'z',capabilities,'welcome_first'));engine.tasks.register('one',validateWelcomeTask(generated(),'liting',capabilities,'welcome_second'));
 engine.tasks.offer('one','welcome_first','z');engine.tasks.respond('one','welcome_first',true);
 assert(!engine.tasks.options('one','liting','').some(a=>a.parameters.task_id==='welcome_second'));
 assert.throws(()=>engine.tasks.offer('one','welcome_second','liting'),/一项/);
});


test('first day director owns travel, daily conversations retain native escort capability', t=>{
 const {engine}=make(t),w=engine.world('one'),origin=w.actors.z.scene;
 w.firstDay=context('Harbor');engine.save(w);
 const j={mode:'player',event:'test',environment:{companion_routes:[[origin,'ancient_tree_square']],reachable_locations:['ancient_tree_square']}};
 assert(!engine.available(w,'z',j).some(a=>['escort_player','move'].includes(a.type)));
 w.firstDay=context('Complete');engine.save(w);
 assert(engine.available(w,'z',j).some(a=>a.type==='escort_player'));
});

test('prepare introduction while walking without publishing it',()=>{
 assert(allowedStoryTools(storyContext(context('WalkToSquare'))).includes('announce_player'));
 assert(!allowedStoryTools(storyContext(context('MeetIslanders',{announced:true,greeted:['z','linxu']}))).includes('show_fireworks'));
});

test('welcome proposals exclude departed residents and allow the newly arrived Liyuan',async t=>{
 const {engine,catalog}=make(t);const agent=new FirstDayAgent(engine,{},catalog);
 agent.update('one',context('WelcomeTask',{currentActor:'liyuan',conversation:'evening'}));engine.focus('one','liyuan','player');
 engine.updateEnvironment('one','linxu',{sceneId:'Square',locationId:'ancient_tree_square',x:0,y:0,movementState:'unavailable'});
 let choices;agent.generate=async(system,input)=>{choices=input.capabilities;return {title:'留个纪念',description:'拍下眼前的广场',steps:[{type:'take_photo',target:'Square',label:'拍一张照片'}]};};
 await agent.prepareTask('one','liyuan','evening');assert(!choices.some(c=>c.target==='linxu'));assert(engine.tasks.dynamicDefinitions('one').some(t=>t.owner==='liyuan'));
});


test('a resident leaving during task generation cannot become a required target',async t=>{
 const {engine,catalog}=make(t);const agent=new FirstDayAgent(engine,{},catalog);
 agent.update('one',context('WelcomeTask',{currentActor:'z',conversation:'departure-race'}));engine.focus('one','z','player');
 engine.updateEnvironment('one','linxu',{sceneId:'Square',locationId:'ancient_tree_square',x:0,y:0,movementState:'idle'});
 agent.generate=async()=>{engine.updateEnvironment('one','linxu',{sceneId:'Square',locationId:'ancient_tree_square',x:0,y:0,movementState:'unavailable'});return generated();};
 await assert.rejects(agent.prepareTask('one','z','departure-race'),/现场不存在/);assert.equal(engine.tasks.dynamicDefinitions('one').length,0);
});


test('same-run reconnect preserves runtime receipts newer than the client checkpoint',async t=>{
 const app=createServer({dbPath:':memory:',backgroundEnabled:false});app.server.listen(0,'127.0.0.1');await once(app.server,'listening');
 t.after(async()=>{app.server.close();await once(app.server,'close');app.store.close();});
 const base={worldId:'runtime_reconnect',runId:'run1',day:1,period:3,syncMode:'reference-v1'};
 const post=async(path,body={})=>{const r=await fetch(`http://127.0.0.1:${app.server.address().port}/api/game/${path}`,{method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify({...base,...body})});const result=await r.json();assert.equal(r.status,200,result.error);return result;};
 const opened=await post('open');const epoch=app.engine.world(base.worldId).epoch;
 const snapshot={sceneId:'Square',locationId:'ancient_tree_square',x:0,y:0,movementState:'idle'};
 const synced=await post('runtime/sync',{actorId:'z',runtimeEvent:{id:'physical_z_1',actorId:'z',seq:1,day:1,period:3,snapshot}});
 const reconnect=await post('open',{checkpoint:opened.checkpoint});
 assert.equal(app.engine.world(base.worldId).epoch,epoch);assert.equal(JSON.parse(reconnect.checkpoint).world.actors.z.runtime_input_seq,1);assert(reconnect.responseRevision>synced.responseRevision);
 await post('runtime/sync',{actorId:'z',runtimeEvent:{id:'physical_z_2',actorId:'z',seq:2,day:1,period:3,snapshot}});
 const loaded=await post('open',{runId:'run2',checkpoint:opened.checkpoint});assert.equal(JSON.parse(loaded.checkpoint).world.actors.z.runtime_input_seq,0);assert(app.engine.world(base.worldId).epoch>epoch);
});
