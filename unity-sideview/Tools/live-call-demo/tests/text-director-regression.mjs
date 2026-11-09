import assert from 'node:assert/strict';
import {TextDirector} from '../ui/text-director.js';
if(!globalThis.CustomEvent)globalThis.CustomEvent=class extends Event{constructor(type,{detail}={}){super(type);this.detail=detail;}};
const real={setTimeout,clearTimeout,performance:globalThis.performance};let now=0,id=0;const timers=new Map();
globalThis.setTimeout=(fn,ms)=>{timers.set(++id,{fn,at:now+ms});return id;};globalThis.clearTimeout=id=>timers.delete(id);
Object.defineProperty(globalThis,'performance',{configurable:true,value:{now:()=>now}});
function step(ms){const end=now+ms;for(;;){const item=[...timers].filter(([,v])=>v.at<=end).sort((a,b)=>a[1].at-b[1].at)[0];if(!item)break;now=item[1].at;timers.delete(item[0]);item[1].fn();}now=end;}
try{
 const director=new TextDirector(),texts=[],completed=[];director.addEventListener('text',e=>texts.push(e.detail));director.addEventListener('complete',e=>completed.push(e.detail.id));
 director.speak({id:1,text:'晚上好，月亮小岛欢迎你🌙。'});step(250);const before=texts.at(-1).text;assert.ok(before.length>0&&before.length<17);
 director.pause();step(10000);assert.equal(texts.at(-1).text,before);assert.equal(completed.length,0);director.resume();step(5000);assert.equal(texts.at(-1).text,'晚上好，月亮小岛欢迎你🌙。');assert.deepEqual(completed,[1]);
 director.speak({id:2,text:'这段还没写完'});step(100);director.speak({id:3,text:'新回应'});step(5000);assert.deepEqual(completed,[1,3]);
 director.speak({id:4,text:'点一下立即展开整句话。'});director.finishNow();assert.equal(texts.at(-1).text,'点一下立即展开整句话。');assert.ok(!completed.includes(4));step(1500);assert.equal(completed.filter(i=>i===4).length,1);
 director.speak({id:5,text:'取消后不再写字'});director.cancel();step(5000);assert.ok(!completed.includes(5));assert.equal(timers.size,0);
 console.log('PASS text-only captions: pause/resume, interruption, finish-now and cancellation');
}finally{globalThis.setTimeout=real.setTimeout;globalThis.clearTimeout=real.clearTimeout;Object.defineProperty(globalThis,'performance',{configurable:true,value:real.performance});}
