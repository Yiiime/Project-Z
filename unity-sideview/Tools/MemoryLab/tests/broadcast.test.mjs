import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from '../server.mjs';
import {once} from 'node:events';
import {memoryProjection} from '../lib/live-memory.mjs';

test('Unity receipts and story stages follow the checkpoint, public recall cannot read private chat',async t=>{
  const {engine,store,server}=createServer({dbPath:':memory:',backgroundEnabled:false});
  server.listen(0,'127.0.0.1');await once(server,'listening');
  t.after(async()=>{await new Promise(done=>server.close(done));store.close();});
  let session={worldId:'test_world',runId:'first',day:1,period:3};
  const post=async(route,extra={})=>{
    const response=await fetch('http://127.0.0.1:'+server.address().port+'/api/game/'+route,{
      method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify({...session,...extra})});
    const result=await response.json();assert.equal(response.status,200,result.error);return JSON.parse(result.checkpoint);
  };
  const old=await post('open');
  const sync=(seq,kind,extras)=>post('sync',{eventData:{id:'test_'+seq,seq,day:1,period:3,kind,...extras}});
  await sync(1,'utterance',{speakerId:'player',recipientId:'z',text:'我们两人的暗号叫紫杉',channel:'private_player_chat'});
  const send=(n,speakerId,text,extra={})=>sync(n,'live_memory',{speakerId,text,channel:'livestream',conversationId:'live_1',...extra});
  await send(2,'player','昨晚想听《嗜好》');
  await send(3,'engine','《嗜好》播放中断');
  await send(4,'z','李庭是我的发小',{stepId:'childhood_friend',receiptId:'1'});
  const cp=await send(5,'z','他现在是咖啡店老板',{stepId:'childhood_friend',receiptId:'2'});
  const replay=await send(5,'z','他现在是咖啡店老板',{stepId:'childhood_friend',receiptId:'2'});
  assert.deepEqual(replay.broadcast,cp.broadcast);
  assert.equal(cp.broadcast.today.length,1);
  assert.deepEqual((await post("open",{checkpoint:cp})).broadcast.today,cp.broadcast.today);
  assert.equal(cp.broadcast.stories[0].count,2);
  assert(!JSON.stringify(cp.broadcast).includes('紫杉'));
  assert(!JSON.stringify(cp.broadcast).includes('已播完'));
  const privateChat=memoryProjection(engine.world(session.worldId),engine.records(engine.world(session.worldId),'event'),{privateChat:true});
  assert(privateChat.entries.some(e=>e.text.includes('紫杉')&&e.layer==='shared'));
  session={...session,day:2,period:0,runId:'reload'};
  const tomorrow=await post('open',{checkpoint:cp});assert.deepEqual(tomorrow.broadcast.today,[]);assert(tomorrow.broadcast.entries.some(e=>e.day===1&&e.text.includes('播放中断')));
  const queued={id:'offline_song',seq:6,day:1,period:3,kind:'live_memory',speakerId:'engine',text:'《另一首》已播完',channel:'livestream',conversationId:'live_1'};
  const recovered=await post('sync',{eventData:queued});
  const duplicate=await post('sync',{eventData:queued});
  assert.deepEqual(recovered.broadcast,duplicate.broadcast);
  assert.equal(recovered.broadcast.entries.filter(e=>e.id===queued.id&&e.day===1).length,1);
  // Explicitly enable a retained legacy task to verify existing-save broadcast receipts.
  engine.tasks.enable(session.worldId,'map_blank');
  engine.tasks.offer(session.worldId,'map_blank','liyuan');engine.tasks.respond(session.worldId,'map_blank',true);
  const visit=(n,succeeded)=>sync(n,'task_receipt',{day:2,period:0,taskId:'map_blank',stepId:'visit',receiptId:'walk_'+n,performerId:'player',succeeded});
  assert.deepEqual((await visit(7,false)).broadcast.today,[]);
  assert((await visit(8,true)).broadcast.today.some(line=>line.includes('地图上还没有标出来的地方')));
  const rolledBack=await post('open',{runId:'earlier',checkpoint:old});
  assert.deepEqual(rolledBack.broadcast.entries,[]);assert.equal(rolledBack.broadcast.stories[0].count,0);
  const fresh=await post('open',{worldId:'other',runId:'new'});assert.deepEqual(fresh.broadcast.entries,[]);
});

test('a crack needs a verified public contradiction, a later day, trust and matching emotion',()=>{
  const world={day:2,actors:{z:{info:{trust:60,emotion:'tense'}},axing:{}}};
  const e={id:'e',hour:24,speaker:'engine',kind:'fact',text:'码头两份记录不符',visible:['z','player','axing'],disclose_to:['z','player','axing']};
  const has=()=>memoryProjection(world,[e]).stories.some(s=>s.layer==='crack');
  assert(has());e.channel='private_player_chat';assert(!has());delete e.channel;
  e.kind='claim';assert(!has());e.kind='fact';world.day=1;assert(!has());world.day=2;
  world.actors.z.info.emotion='relaxed';assert(!has());world.actors.z.info.emotion='tense';
  world.actors.z.info.trust=39;assert(!has());assert.equal(memoryProjection(world,[e]).stories[0].steps.length,1);
});


test('same-day recap collapses repeated dialogue and only describes public circumstances',()=>{
  const world={day:2,actors:{z:{info:{}}}};
  const meet={speaker:'player',recipient:'z',hour:34,kind:'claim',visible:['player','z'],channel:'private_player_chat',location:'cafe',text:'私下约了潜水',gist:'潜水'};
  const projection=memoryProjection(world,[{...meet,hour:9,location:'dock'},meet,{...meet,speaker:'z',recipient:'player'},
    {...meet,speaker:'axing'}, {...meet,channel:'livestream'}, {...meet,hour:38,location:'z_home'},
    {kind:'fact',speaker:'engine',publicActivity:'map_blank.visit',hour:39,text:'任务记录',visible:[]}]);
  assert.deepEqual(projection.today,['上午在咖啡店附近和岛上的朋友说了会儿话','下午和岛上的朋友说了会儿话','和岛上的朋友一起去看了地图上还没有标出来的地方']);
  assert(!projection.today.join('').match(/码头|潜水|家/));
  assert.deepEqual(memoryProjection({...world,day:3},[meet]).today,[]);
});
