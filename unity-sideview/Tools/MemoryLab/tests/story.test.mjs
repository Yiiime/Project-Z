import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,readFileSync,rmSync} from 'node:fs';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {inflateSync} from 'node:zlib';
import {StoryTaskService,validateStoryTask} from '../lib/story-task-service.mjs';
import {renderDrawing,validateDrawing} from '../lib/story-raster.mjs';
const drawing={shapes:[{type:'rect',x:100,y:170,w:240,h:210,fill:'#756A53'},{type:'ellipse',x:120,y:130,w:200,h:100,fill:'#B69462'},{type:'polygon',points:[[140,210],[300,210],[270,350]],fill:'#D0BB88'}]};
test('AI drawing data is bounded and cannot contain scripts or external resources',()=>{
 assert.throws(()=>validateDrawing({shapes:[...drawing.shapes,{type:'script',fill:'#FFFFFF'}]}));
 assert.throws(()=>validateDrawing({shapes:[...drawing.shapes,{type:'rect',x:-1,y:0,w:20,h:20,fill:'#FFFFFF'}]}));
 assert.throws(()=>validateDrawing({shapes:Array(70).fill(drawing.shapes[0])}));
 assert.throws(()=>validateStoryTask({title:'a',request:'b',item:'same',container:'same'}));
 assert.throws(()=>validateStoryTask({title:'a',request:'b',item:''}));
});
test('rasterized generated drawing is a real transparent RGBA PNG with visible foreground',()=>{
 const png=renderDrawing(drawing);assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');assert.equal(png.readUInt32BE(16),512);assert.equal(png[25],6);
 let offset=8,raw;while(offset<png.length){const len=png.readUInt32BE(offset),type=png.subarray(offset+4,offset+8).toString();if(type==='IDAT')raw=inflateSync(png.subarray(offset+8,offset+8+len));offset+=12+len;}
 assert.equal(raw[1+3],0);assert.equal(raw[250*(512*4+1)+1+200*4+3],255);
 assert.throws(()=>renderDrawing({shapes:[{type:'rect',x:0,y:0,w:512,h:512,fill:'#FFFFFF'},...drawing.shapes]}));
});
test('generated task and actual image files persist; request replay and cross-world access are fenced',async()=>{
 const root=mkdtempSync(join(tmpdir(),'z-story-test-'));let calls=0;
 const provider={request:(messages)=>messages,call:async messages=>{calls++;return {text:JSON.stringify(messages[0].role==='system'?{title:'收好工具',request:'把工具放回盒子。',item:'铜色工具',container:'蓝色盒子'}:drawing)};}};
 const options={root,provider,catalog:{actor:()=>({}),profile:()=> '船长'},engine:{world:()=>({}),records:()=>[]}};
 try{
 const service=new StoryTaskService(options);service.assetMode='vector';
 const input={worldId:'world-a',requestId:'request-1',actorId:'linxu',scene:'Street'};
 const job=await service.plan(input);assert.equal(job.status,'generating');
 for(let i=0;i<100&&service.get(job.id,'world-a').status==='generating';i++)await new Promise(r=>setTimeout(r,5));
 assert.equal(service.get(job.id,'world-a').status,'ready');assert.equal(readFileSync(join(root,job.id,'item.png')).subarray(0,4).toString('hex'),'89504e47');
 assert.equal((await service.plan(input)).id,job.id);assert.equal(calls,3);
 assert.throws(()=>service.get(job.id,'world-b'));assert.throws(()=>service.get('../secret','world-a'));
 const restored=new StoryTaskService(options);assert.equal(restored.get(job.id,'world-a').status,'ready');assert.equal((await restored.plan(input)).id,job.id);assert.equal(calls,3);
 }finally{rmSync(root,{recursive:true,force:true});}
});
