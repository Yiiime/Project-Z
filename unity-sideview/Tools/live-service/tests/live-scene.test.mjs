import test from 'node:test';
import assert from 'node:assert/strict';
import {LiveScene,validateLiveScene} from '../server/live-scene.mjs';
import {Providers} from '../server/providers.mjs';
const input={mode:'arrival',playerName:'小海',worldId:'a'.repeat(32),day:1};
function fixture(){const commands=[],facts=[];const host={command:(type,payload)=>{const c={commandId:String(commands.length),payload,type};commands.push(c);return c;},engine:{_clock:10,_public:(type,text)=>facts.push({type,text})}};return {scene:new LiveScene(host,input),commands,facts,host};}
test('arrival progresses only through selected tools, displayed receipts and player acceptance',()=>{
  const {scene,commands,facts}=fixture();
  assert(!scene.execute({name:'depart_island'}));assert(!scene.accept());
  assert(scene.execute({name:'draw_guest',args:{}}));assert.deepEqual(scene.allowed(),[]);assert.equal(facts.length,0);
  scene.receipt(commands[0],{status:'displayed'});assert(facts[0].text.includes('小海'));
  assert(scene.execute({name:'offer_arrival',args:{title:'模型生成标题',description:'模型生成邀请'}}));
  assert(!scene.accept());scene.receipt(commands[1],{status:'displayed'});
  assert(scene.accept());assert(scene.accept());assert.equal(facts.length,2);
  assert(scene.execute({name:'depart_island',args:{}}));scene.receipt(commands[2],{status:'displayed'});
  assert.equal(scene.phase,'departed');assert.deepEqual(scene.allowed(),[]);
});
test('a failed draw is not a win; retry and restart preserve honest scene state',()=>{
  const {scene,commands,host,facts}=fixture();scene.execute({name:'draw_guest',args:{}});
  scene.receipt(commands[0],{status:'failed'});assert.equal(facts.length,0);assert.deepEqual(scene.allowed(),['draw_guest']);
  const restored=new LiveScene(host,null,scene.snapshot());assert.equal(restored.phase,'opening');
  assert.throws(()=>restored.receipt(commands[0],{status:'displayed'}),/stale/);
});
test('first evening and daily broadcasts cannot run the arrival lottery',()=>{
  const {host}=fixture();for(const mode of ['first_evening','daily']){
    const scene=new LiveScene(host,{...input,mode});assert.deepEqual(scene.allowed(),[]);assert(!scene.execute({name:'draw_guest'}));
  }
  assert.throws(()=>validateLiveScene({...input,day:0}));assert.throws(()=>validateLiveScene({...input,worldId:'other'}));
});

test('release reviewer receives trusted scene facts without host output instructions',async()=>{
  let system;
  const providers=new Providers({DEEPSEEK_API_KEY:'test-key'},{fetch:async(_url,options)=>{
    const messages=JSON.parse(options.body).messages;system=messages[0].content;
    assert.equal(JSON.parse(messages[1].content).scene.playerName,'小海');
    return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify({decision:'allow',reason:'Scene-backed invitation',evidence_ids:[]})}}]});
  }});
  const result=await providers.generate('test',{kind:'review',requestId:crypto.randomUUID(),eventId:'review',context:{revision:1,publicEvents:[],sessionEvents:[],waiting_messages:[],candidate:{id:'candidate',role:'host',speakerId:'Z',text:'欢迎来小岛住七天。',intent:'speak',replyTo:[],evidenceIds:[]}}},{sceneContext:{...input,phase:'drawn',tools:['offer_arrival']}});
  assert.equal(result.decision,'allow');
  assert(system.includes('不生成主持台词或工具调用'));
  assert(!system.includes('调用工具时必须action=tool'));
});
