import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {validateDrawingScene,applyDrawingPatch,renderDrawingScene} from '../ui/drawing-scene.js';
import {DrawingDirector} from '../ui/drawing-director.js';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {handleAgent,parseAgentReply} from '../worker/agent-director.js';
import {parseDrawingResult} from '../worker/drawing-planner.js';
const shape=(id,stage=0)=>({id,label:id,stage,stroke:'#665d75',fill:'none',strokeWidth:3,commands:[['M',50,80],['L',200,120],['L',200,180]]});
const scene={title:'测试树林',stages:['树干','树冠'],elements:[shape('trunk'),shape('leaves',1)]};
const clone=v=>structuredClone(v),flush=async()=>{for(let i=0;i<60;i++)await Promise.resolve();};
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {resolve,promise};};
const job={taskId:'art-1',revision:0,request:'画两棵树'};
assert.deepEqual(validateDrawingScene(scene),scene);assert.match(renderDrawingScene(scene,0),/trunk/);assert.doesNotMatch(renderDrawingScene(scene,0),/leaves/);
for(const change of [e=>e.commands.push(['javascript',1]),e=>e.commands[0][1]=NaN,e=>e.commands[1][2]=361,e=>e.fill='url(https://evil)',e=>e.onclick='alert(1)',e=>e.commands=[['M',0,0]],e=>e.id='"><script',e=>e.label='<script>']){
 const bad=clone(scene);change(bad.elements[0]);assert.throws(()=>validateDrawingScene(bad));
}
const duplicate=clone(scene);duplicate.elements[1].id='trunk';assert.throws(()=>validateDrawingScene(duplicate));
assert.throws(()=>parseDrawingResult({scene:{...scene,stages:['a','b','empty']}},job));
const changed={...shape('leaves',1),stroke:'#2266ff'};
const patch={title:'测试树林',upserts:[changed],removeIds:[]},edited=applyDrawingPatch(scene,patch,['leaves']);
assert.deepEqual(edited.elements[0],scene.elements[0]);assert.equal(edited.elements[1].stroke,'#2266ff');
assert.throws(()=>applyDrawingPatch(scene,patch,['trunk']));assert.throws(()=>applyDrawingPatch(scene,{upserts:[],removeIds:['trunk']},['leaves']));
assert.throws(()=>applyDrawingPatch(scene,{upserts:[shape('leaves',1)],removeIds:[]},['leaves']));
assert.deepEqual(validateDrawingScene(applyDrawingPatch(scene,{upserts:[],removeIds:['leaves']},['leaves'])).elements,[shape('trunk')]);
console.log('PASS safe geometry, no executable SVG, cumulative stages and byte-preserving local edits');

{
 let clock=0,pending=deferred();const events=[],states=[];
 const d=new DrawingDirector({generate:()=>pending.promise,changed:s=>states.push(s?.phase),event:(type,text)=>events.push({type,text}),now:()=>clock});
 assert(d.start({request:'画树'}));assert.equal(d.current.open,false);assert.equal(d.current.phase,'preparing');
 const id=d.current.id;pending.resolve({status:'ready',taskId:id,revision:0,scene});await flush();assert.equal(d.current.phase,'ready');assert.equal(d.current.open,false);
 assert.equal(d.displayed(id,0,0),false);d.current.open=true;assert(d.displayed(id,0,0));assert(!d.displayed(id,0,0));
 d.current.open=false;clock=2000;d.advance();assert.equal(d.current.stage,0);
 d.current.open=true;d.setPaused(true);d.advance();assert.equal(d.current.stage,0);d.setPaused(false);d.advance();assert.equal(d.current.stage,1);
 assert(!events.some(e=>e.type==='drawing.displayed'&&JSON.parse(e.text).final));
 assert(d.displayed(id,0,1));assert.equal(d.current.phase,'completed');assert.equal(d.current.checks.semantic,'unverified');
 const before=d.current.svg;pending=deferred();assert(d.edit({taskId:id,baseRevision:0,instruction:'树冠改蓝色',targetIds:['leaves']}));
 assert.equal(d.current.svg,before);assert.equal(d.current.displayedRevision,0);assert(!d.displayed(id,0,1));assert(!d.edit({taskId:id,baseRevision:0,instruction:'过时修改',targetIds:['leaves']}));
 pending.resolve({status:'ready',taskId:id,revision:1,scene:edited});await flush();assert.equal(d.current.phase,'ready');assert(d.displayed(id,1,1));assert.match(d.current.svg,/#2266ff/);
 pending=deferred();assert(d.edit({taskId:id,baseRevision:1,instruction:'再修改',targetIds:['leaves']}));d.cancel();
 pending.resolve({status:'ready',taskId:id,revision:2,scene});await flush();assert.equal(d.current.phase,'cancelled');assert.match(d.current.svg,/#2266ff/);
 d.reset();assert.equal(d.current,null);
}
console.log('PASS hidden/paused panels, actual display completion, revisions, retained previous image and cancelled late results');

{
 const d=new DrawingDirector({generate:async()=>{throw Error('agent_invalid_reply');},changed(){},event(){}});
 assert(d.start({request:'画树'}));await flush();assert.equal(d.current.phase,'failed');assert.match(d.current.reason,/笔画未通过校验/);assert.equal(d.current.scene,null);
}

{
 const e=new ConversationEngine();let calls=0;
 e.configureAgent({cancel(){},async generate(){calls++;throw Error('must not run');}},{enabled:true});
 assert.equal(e.requestDrawing({request:'画树林'}),false);
 assert.equal(e._executeTool({name:'start_drawing',args:{request:'画树林'}}),false);
 assert.equal(e.editDrawing({instruction:'改成蓝色'}),false);
 assert.equal(calls,0);assert.equal(e._state.play.active,null);e.destroy();
}
{
 let calls=0;const origin='https://disabled-drawing.test';
 const request=new Request(origin+'/api/agent',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','oai-authenticated-user-id':'drawing-test'},body:JSON.stringify({requestId:randomUUID(),kind:'artwork',eventId:'generate',message:'画图',context:{drawingJob:job}})});
 const response=await handleAgent(request,{DEEPSEEK_API_KEY:'fixture'},{fetch:async()=>{calls++;throw Error('must not run');}});
 assert.equal(response.status,400);assert.equal((await response.json()).error,'capability_disabled');assert.equal(calls,0);
}
console.log('PASS retained drawing primitives; release blocks UI, host tools and artwork API without provider calls');
