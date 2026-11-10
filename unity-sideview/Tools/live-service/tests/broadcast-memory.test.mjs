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
