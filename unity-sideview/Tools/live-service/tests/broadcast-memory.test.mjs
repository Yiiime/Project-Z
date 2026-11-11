import test from 'node:test';
import assert from 'node:assert/strict';
import {BroadcastMemory} from '../server/broadcast-memory.mjs';
const binding={worldId:'a'.repeat(32),runId:'b'.repeat(32),day:2,entries:[{id:'receipt1',layer:'daily',
  day:1,speaker:'engine',text:'《嗜好》播放中断'}],stories:[{id:'friend',layer:'anchor',count:0,
  topics:['小时候'],steps:['李庭是我的发小','他现在是咖啡店老板','我们因为一次出行闹过别扭']}]};

test('heard story steps advance once, survive reconnect, and require a new player turn',()=>{
  const m=new BroadcastMemory(binding);
  m.record('player.message','小时候呢？',{id:'p1'});const first=m.recall().next;
  m.record('speech.progress','李庭是我');assert.equal(m.pending.length,1);
  m.record('speech.progress',first.line+'。后面');assert.equal(m.pending[1].change.step,1);
  m.record('speech.completed',first.line+'。');assert(!m.pending[2].change);
  assert.equal(m.recall().next,null);
  const resumed=new BroadcastMemory(binding,m.save());
  resumed.record('player.message','后来呢？',{id:'p2'});assert.equal(resumed.recall().next.step,2);
  resumed.acknowledge(3);assert.deepEqual(resumed.pending.map(x=>x.seq),[4]);
  resumed.record('player.message','昨天那首歌呢？',{id:'p3'});
  assert.equal(resumed.recall().entries[0].text,'《嗜好》播放中断');
  assert.throws(()=>resumed.refresh({...binding,runId:'c'.repeat(32)}));
});
test('media preparation and playback are separate receipts',()=>{
  const m=new BroadcastMemory(binding);
  for(const phase of ['ready','playing','interrupted'])m.record('talent.'+phase,'',{}, {kind:'singing',phase,title:'嗜好'});
  assert.deepEqual(m.pending.map(e=>e.text),['《嗜好》已准备好','《嗜好》开始播放','《嗜好》播放中断']);
  m.record('talent.completed','',{}, {kind:'singing',phase:'completed',title:'嗜好'});
  assert.equal(m.pending.at(-1).text,'《嗜好》已播完');
});

test('yesterday’s player quotes outrank host recap, farewell and the current question',()=>{
 const e=(day,speaker,text)=>({day,speaker,text,id:text,layer:'daily'});
 const m=new BroadcastMemory({...binding,entries:[e(1,'player','海风好舒服'),e(1,'player','想听歌'),e(1,'z','晚上好呀'),e(1,'z','今天跟朋友说了话'),e(1,'z','晚安'),e(2,'player','昨天我说了什么？')]});
 m.record('player.message','昨天我在直播间说了什么？',{id:'q'});
 assert.deepEqual(m.recall().entries.slice(0,2).map(e=>e.speaker),['player','player']);
 assert(m.recall().entries.every(e=>e.day===1));
});
test('song title follows its setup and uses the receipt available at the next turn',()=>{
 const m=new BroadcastMemory({...binding,songs:[{title:'夏夜最后的烟火',sourceId:'p'}]});
 let now=0,status='generating';m.clock=()=>now;
 m.songTasks=()=>[{sourceId:'p',status,plan:{segment:{description:'第一段完整副歌'}}}];
 assert.match(m.opening(),/能不能学首歌/);assert(!m.opening().includes('夏夜'));
 assert.deepEqual(m.recall().daySongs,[]);
 m.recapSpeech='1';m.recapItem=m.nextItem();m.record('speech.completed',m.opening(),{speechId:'1'});
 assert.equal(m.opening(),null);now+=7000;
 assert.match(m.opening(),/夏夜最后的烟火.*还在准备/);
 status='failed';assert.match(m.opening(),/没准备好/);
 status='ready';assert.match(m.opening(),/第一段完整副歌准备好了/);
 m.recapItem=m.nextItem();m.recapSpeech='2';m.record('speech.completed',m.opening(),{speechId:'2'});
 assert.equal(m.recall().daySongs[0].status,'ready');now+=30000;assert.equal(m.opening(),null);
 const checkpoint={...m.binding,toldItems:[...m.told],recapSpoken:true};
 assert.equal(new BroadcastMemory(checkpoint).opening(),null);
});
test('only public small talk matching a displayed audience or player message is offered',()=>{
 const m=new BroadcastMemory({...binding,today:['聊到了咖啡拉花','聊到了海边的潮汐']});
 assert.deepEqual(m.recall().today,[]);
 for(const type of ['npc.message','player.message']){
   m.record(type,'你也喜欢拉花吗？',{id:'q'});assert.deepEqual(m.recall().today,['聊到了咖啡拉花']);
   m.record('speech.completed','是啊。',{speechId:'5',replyTo:['q']});assert.deepEqual(m.recall().today,[]);
 }
});

test('a public topic Z chose to share gets a separate turn before the song',()=>{
 const m=new BroadcastMemory({...binding,today:['聊到了咖啡拉花'],highlights:['咖啡拉花'],songs:[{sourceId:'p',title:'夏夜最后的烟火'}]});
 let now=0;m.clock=()=>now;
 assert.match(m.opening(),/咖啡拉花/);assert.doesNotMatch(m.opening(),/歌/);
 m.recapItem=m.nextItem();m.recapSpeech='1';m.record('speech.completed',m.opening(),{speechId:'1'});
 now+=14000;assert.match(m.opening(),/还答应学一首歌.*夏夜最后的烟火/);
});
