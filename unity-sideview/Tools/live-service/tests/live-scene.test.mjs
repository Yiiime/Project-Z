import test from 'node:test';
import assert from 'node:assert/strict';
import {LiveScene,validateLiveScene} from '../server/live-scene.mjs';
import {Providers} from '../server/providers.mjs';
const input={mode:'arrival',playerName:'小海',worldId:'a'.repeat(32),day:1};
function fixture(ready=true){const commands=[],facts=[];const host={command:(type,payload)=>{const c={commandId:String(commands.length),payload,type};commands.push(c);return c;},engine:{_clock:10,_public:(type,text)=>facts.push({type,text})}};const scene=new LiveScene(host,input);if(ready){for(let i=0;i<3;i++)scene.hostCompleted('s'+i);for(let i=0;i<4;i++)scene.audienceDisplayed();host.engine._clock+=45000;}return {scene,commands,facts,host};}
test('arrival progresses only through selected tools, displayed receipts and player acceptance',()=>{
  const {scene,commands,facts}=fixture();
  assert(!scene.execute({name:'depart_island'}));assert(!scene.accept());
  assert(scene.execute({name:'draw_guest',args:{}}));assert.deepEqual(scene.allowed(),[]);assert.equal(facts.length,0);
  scene.receipt(commands[0],{status:'displayed',playerActivated:true,playerName:input.playerName});assert(facts[0].text.includes('小海'));
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

test('arrival needs one visible audience round and two completed host turns, without a time gate',()=>{
 const {scene,host}=fixture(false);assert.deepEqual(scene.allowed(),[]);assert.equal(scene.execute({name:'draw_guest',args:{}}),false);
 scene.hostCompleted('s0');scene.hostCompleted('s0');assert.equal(scene.warmup.speeches,1);
 scene.audienceDisplayed();scene.audienceDisplayed();assert.deepEqual(scene.allowed(),[]);scene.hostCompleted('s1');
 assert.deepEqual(scene.allowed(),['draw_guest']);
 const restored=new LiveScene(host,null,scene.snapshot());assert.deepEqual(restored.allowed(),['draw_guest']);restored.hostCompleted('s0');assert.equal(restored.warmup.speeches,2);
});
test('time and chat alone cannot invent host completion',()=>{
 const {scene,host}=fixture(false);host.engine._clock+=60000;
 for(let i=0;i<6;i++)scene.audienceDisplayed();assert.deepEqual(scene.allowed(),[]);
 scene.hostCompleted('s0');assert.deepEqual(scene.allowed(),[]);
 scene.hostCompleted('s1');assert.deepEqual(scene.allowed(),['draw_guest']);
});
test('lottery result needs player activation and matching name',()=>{
 const {scene,commands,facts}=fixture();scene.execute({name:'draw_guest',args:{}});
 assert.throws(()=>scene.receipt(commands[0],{status:'displayed'}),/invalid_draw_evidence/);
 assert.throws(()=>scene.receipt(commands[0],{status:'displayed',playerActivated:true,playerName:'other'}),/invalid_draw_evidence/);
 assert.equal(scene.phase,'opening');assert.equal(facts.length,0);assert.equal(scene.pending,commands[0].commandId);
 scene.receipt(commands[0],{status:'displayed',playerActivated:true,playerName:input.playerName});assert.equal(scene.phase,'drawn');
});

test('first evening requires mixed virtual audience and a timed goodnight song',async()=>{
 const {scenePrompt}=await import('../server/live-scene.mjs');
 const prompt=scenePrompt({...input,mode:'first_evening'});
 assert(prompt.includes('150秒'));assert(prompt.includes('virtualAudience'));assert(prompt.includes('wanan-yanrenzhong-vivi2-chorus'));
});

test('collecting the displayed gift can accept immediately without another host turn',()=>{
 const {scene,commands}=fixture();scene.execute({name:'draw_guest',args:{}});
 assert.equal(scene.accept(),false);
 scene.receipt(commands[0],{status:'displayed',playerActivated:true,playerName:input.playerName});
 assert.equal(scene.accept(),true);assert.equal(scene.phase,'accepted');
});

test('completing the audience exchange invalidates prefetched warmup exactly once',()=>{
 const {scene,host}=fixture(false);let aborted=0,cancelled=0;
 host.engine.pending={abort(){aborted++;}};host.engine._cancelPreparation=()=>cancelled++;
 host.engine.speechPlan={oldWarmup:true};scene.hostCompleted('first');scene.audienceDisplayed();scene.audienceDisplayed();
 assert.equal(cancelled,0);scene.hostCompleted('second');
 assert.equal(aborted,1);assert.equal(cancelled,1);assert.equal(host.engine.speechPlan,null);
 scene.hostCompleted('second');scene.audienceDisplayed();assert.equal(cancelled,1);
 assert.deepEqual(scene.allowed(),['draw_guest']);
});

test('arrival cannot drift into singing or another warmup after the required exchange',async()=>{
 const {parseAgentReply}=await import('../server/shared/worker/agent-director.js');
 const context={host:{inbox:[]},scene:{...input,phase:'opening',tools:['draw_guest']},mediaRuntime:{audioEnabled:true,ttsAvailable:true}};
 const speech={actorId:'z',delivery:'轻松地说。',expression:'neutral',action:'speak',speech:'我想到一个惊喜。',mood:'warm',handledIds:[],wakeAfterMs:2000,memory:'',agendaUpdates:[],virtualAudience:[]};
 assert.throws(()=>parseAgentReply(speech,{kind:'host',context}),/arrival_lottery_required/);
 assert.throws(()=>parseAgentReply({...speech,action:'tool',tool:{name:'start_singing',args:{songId:'wanan-yanrenzhong-vivi2-chorus'}}},{kind:'host',context}),/invalid_arrival_tool/);
 assert.equal(parseAgentReply({...speech,action:'tool',tool:{name:'draw_guest',args:{}}},{kind:'host',context}).tool.name,'draw_guest');
});
test('first evening host must carry virtual chat in addition to the NPC lane',async()=>{
 const {parseAgentReply}=await import('../server/shared/worker/agent-director.js');
 const context={host:{inbox:[]},scene:{...input,mode:'first_evening',phase:'opening',tools:[]}};
 const speech={action:'speak',speech:'今晚一起聊聊。',mood:'warm',handledIds:[],wakeAfterMs:2000,memory:'',agendaUpdates:[]};
 assert.throws(()=>parseAgentReply(speech,{kind:'host',context}),/invalid_evening_virtual_audience/);
 assert.equal(parseAgentReply({...speech,virtualAudience:[{name:'海风',text:'晚上好呀'},{name:'月光',text:'来听你聊天'}]},{kind:'host',context}).virtualAudience.length,2);
});
