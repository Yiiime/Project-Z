import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {EXPRESSION_CATALOG} from '../server/shared/expression-catalog.js';
import {expressionKeys, utteranceMetadata} from '../server/shared/utterance.js';
import {normalizeAgentInput, parseAgentReply, adaptAudienceReply, buildAgentRequest} from '../server/shared/worker/agent-director.js';
import {handleTts} from '../server/shared/worker/volc-tts.js';
import {Providers, RemoteSpeech, ArtifactStore} from '../server/providers.mjs';
import {LiveSession} from '../server/session.mjs';
import {approveCallCandidate} from '../server/shared/ui/call-review.js';

const meta=(actorId='z',expression='neutral',delivery='自然平静，语速平稳。')=>({actorId,expression,delivery});
const input=()=>normalizeAgentInput({requestId:crypto.randomUUID(),eventId:'turn',kind:'host',context:{host:{inbox:[]}}});
const host=()=>({...meta(),action:'speak',speech:'你去吧，我在这里等你。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:2000,memory:''});
const tick=()=>new Promise(resolve=>setImmediate(resolve));

test('generated catalog exactly matches source, including active portrait subsets',()=>{
  const source=JSON.parse(fs.readFileSync(new URL('../../MemoryLab/content/current/contracts/portrait-expressions.json',import.meta.url),'utf8'));
  assert.deepEqual(EXPRESSION_CATALOG,source);
  assert.equal(Object.keys(source.actors).length,8);
  for(const [id,actor] of Object.entries(source.actors)) {
    assert.deepEqual(expressionKeys(id),actor.expressions);
    for(const value of actor.expressions)assert.deepEqual(utteranceMetadata(id,meta(id,value)),meta(id,value));
    for(const value of Object.values(source.actors).flatMap(a=>a.expressions))
      if(!actor.expressions.includes(value))assert.throws(()=>utteranceMetadata(id,meta(id,value)),/invalid_expression/);
    for(const value of ['smiling','bawl_dry','沉脸','NEUTRAL',null,42])assert.throws(()=>utteranceMetadata(id,meta(id,value)),/invalid_expression/);
  }
  assert.deepEqual(expressionKeys('zhou_bobo','Memory/Portraits/ZhouboFinal-v03'),['neutral']);
  assert.throws(()=>utteranceMetadata('zhou_bobo',meta('zhou_bobo','gloom'),{activePortrait:'Memory/Portraits/ZhouboFinal-v03'}),/invalid_expression/);
  assert.throws(()=>utteranceMetadata('c01',meta('c01')),/invalid_actor_id/);
  assert.throws(()=>utteranceMetadata('z',meta('linxu')),/actor_id_mismatch/);
});

test('new model host/repair/farewell require complete metadata; legacy defaults are isolated',()=>{
  const v=input();
  assert.equal(parseAgentReply(host(),v,{requireUtterance:true}).delivery,host().delivery);
  for(const field of ['actorId','delivery','expression']){
    const reply=host();delete reply[field];assert.throws(()=>parseAgentReply(reply,v,{requireUtterance:true}));
  }
  assert.throws(()=>parseAgentReply({...host(),expression:'smile'},v,{requireUtterance:true}));
  assert.throws(()=>parseAgentReply({...host(),delivery:'\n'},v,{requireUtterance:true}));
  assert.throws(()=>parseAgentReply({...host(),delivery:'a'.repeat(161)},v,{requireUtterance:true}));
  const legacy=host();for(const field of ['actorId','delivery','expression'])delete legacy[field];
  assert.equal(parseAgentReply(legacy,v).expression,'neutral');
  const repair={kind:'repair',context:{alreadySpoken:[]}};
  assert.equal(parseAgentReply({speech:'我记不清了。',...meta('z','sad')},repair,{requireUtterance:true}).expression,'sad');
  assert.throws(()=>parseAgentReply({speech:'我记不清了。'},repair,{requireUtterance:true}));
  const farewell={kind:'session',context:{mode:'farewell'}};
  assert.throws(()=>parseAgentReply({intent:'farewell',speech:'晚安。'},farewell,{requireUtterance:true}));
  assert.equal(parseAgentReply({intent:'farewell',speech:'晚安。',...meta()},farewell,{requireUtterance:true}).actorId,'z');
});

test('all seven island viewers keep their own stable actor ID and presentation through adaptation',()=>{
  for(const id of Object.keys(EXPRESSION_CATALOG.actors).filter(id=>id!=='z')){
    const v=normalizeAgentInput({requestId:crypto.randomUUID(),kind:'audience',eventId:'public',context:{publicEvents:[{id:'public',type:'speech.completed',text:'今天你们怎么样？',speakerRole:'host',speakerId:'Z'}],audience:{eligibleNpcIds:[id]}}});
    const raw={status:'ok',npc_id:id,intent:'reaction',text:'还不错。',evidence_ids:['public'],...meta(id)};
    const result=parseAgentReply(adaptAudienceReply(raw,v),v,{requireUtterance:true}).items[0];
    assert.equal(result.actorId,id);assert.equal(result.delivery,raw.delivery);assert.equal(result.expression,'neutral');
    assert.match(buildAgentRequest(v).system,/不机械模仿对方情绪/);
    assert.throws(()=>parseAgentReply(adaptAudienceReply({...raw,actorId:'z'},v),v,{requireUtterance:true}));
  }
});

test('model invocation never accepts an old or invalid expression packet',async()=>{
  const bad=host();delete bad.delivery;
  const providers=new Providers({DEEPSEEK_API_KEY:'test-only'},{fetch:async()=>Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(bad)}}]})});
  await assert.rejects(()=>providers.generate('strict-test',input()),e=>e.code==='agent_invalid_reply');
});

test('speech repair replaces delivery/expression atomically and warms the new performance',async()=>{
  const candidate={id:'candidate',role:'host',speakerId:'Z',text:'我去年去过北京。',intent:'speak',replyTo:[],evidenceIds:[],...meta('z','happy','兴奋地说。')};
  let reviews=0;const warmed=[];
  const engine={facts:[],session:{evidence:()=>[]},speechAudio:{dropWarmSpeech(){}},_warmCandidate:(id,r)=>warmed.push({...r}),
    audience:{dialogue:{snapshot:()=>({publicEvents:[],waiting_messages:[],revision:0}),log(){},inspect:async(c,o)=>{
      if(++reviews===1){o.onOutcome('rejected',{issue:'unsupported_past',span:'去年去过北京',reason:'没有这段经历的依据'});return null;}return {};
    }}},client:{generate:async()=>({speech:'我还挺想去北京看看。',...meta('z','starry_curious','带一点向往，轻声说。')})}};
  const approved=await approveCallCandidate(engine,{candidate,result:host(),valid:()=>true});
  assert.equal(approved.repaired,true);assert.equal(approved.candidate.expression,'starry_curious');
  assert.equal(warmed.at(-1).speech,approved.candidate.text);assert.equal(warmed.at(-1).delivery,approved.candidate.delivery);
});

test('TTS sends audible guidance only in context_texts; synthesized text stays exact',async()=>{
  let seen;const request=new Request('https://z.test/api/tts',{method:'POST',headers:{Origin:'https://z.test','Content-Type':'application/json','oai-authenticated-user-id':crypto.randomUUID()},body:JSON.stringify({requestId:crypto.randomUUID(),text:'行，你去吧。',mood:'happy',delivery:'舍不得但故作无所谓，音量稍低。'})});
  const response=await handleTts(request,{VOLC_TTS_API_KEY:'test-only'},{fetch:async(url,options)=>{
    seen=JSON.parse(options.body);
    const packets=[{code:0,data:'AAAAAA=='},{code:20000000}].map(p=>'data: '+JSON.stringify(p)+'\n\n').join('');
    return new Response(packets,{headers:{'Content-Type':'text/event-stream'}});
  }});
  assert.equal(response.status,200);await response.text();
  assert.equal(seen.req_params.text,'行，你去吧。');
  assert.deepEqual(JSON.parse(seen.req_params.additions).context_texts,['舍不得但故作无所谓，音量稍低。']);
  assert.equal(seen.req_params.audio_params.speech_rate,0);
});

test('prepared audio cannot be reused for the same text with different delivery',async()=>{
  const calls=[],session={id:'test',capabilities:{audioEnabled:true},artifacts:{put:()=>({artifactId:crypto.randomUUID()})},artifactIds:new Set()};
  const providers={env:{VOLC_TTS_API_KEY:'test-only'},synthesize:async(id,text,mood,signal,delivery)=>{calls.push({text,delivery});return {bytes:Buffer.alloc(4),contentType:'audio/mpeg',words:[]};}};
  const speech=new RemoteSpeech(session,providers);
  await speech.warmSpeech('same','你去吧。','warm',{delivery:'平静地说。'});
  await speech.claim('same','你去吧。','warm',undefined,'压低声音，有点不舍。');
  assert.deepEqual(calls.map(x=>x.delivery),['平静地说。','压低声音，有点不舍。']);
  await speech.warmSpeech('other','你好。','warm',{delivery:'平静地说。'});
  await speech.claim('other','你好。','warm',undefined,'平静地说。');
  assert.equal(calls.length,3);speech.dropWarmSpeech();
});

test('host and audience execution packets persist metadata without putting it in visible text or memory',async t=>{
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'z-delivery-'));
  const providers={env:{},config:async()=>({enabled:false})};
  const session=new LiveSession({root,providers,artifacts:new ArtifactStore(path.join(root,'artifacts')),songRoot:fileURLToPath(new URL('../server/assets/songs',import.meta.url))});
  t.after(()=>{session.close();fs.rmSync(root,{recursive:true,force:true});});await session.initialize();clearInterval(session.engine._timer);
  session.control({protocolVersion:1,sessionId:session.id,epoch:session.epoch,action:'resume'});
  session.engine._say('行，你去吧。','warm','none',{...meta('z','sad','舍不得，音量稍低。'),source:'deepseek'});await tick();
  const command=[...session.commands.values()].find(c=>c.type==='host.present');
  assert.equal(command.payload.delivery,'舍不得，音量稍低。');assert.equal(command.payload.expression,'sad');assert.equal(command.payload.actorId,'z');
  assert.equal(command.payload.text,'行，你去吧。');
  assert.equal(session.snapshot().pendingCommands.find(c=>c.type==='host.present').payload.delivery,command.payload.delivery);
  const saved=JSON.parse(fs.readFileSync(path.join(root,session.id+'.json'),'utf8'));
  assert.equal(saved.commands.find(([id])=>id===command.commandId)[1].payload.expression,'sad');
  assert(!JSON.stringify(saved.engine.facts).includes(command.payload.delivery));
  assert(!JSON.stringify(session.snapshot().room.messages).includes(command.payload.delivery));
  const publication=session.engine.audience.publish({npc_id:'linxu',text:'带件外套吧。',evidence_refs:[],...meta('linxu','troubled','关切，语速放缓。')});publication.catch(()=>{});
  const audience=[...session.commands.values()].find(c=>c.type==='audience.present');
  assert.equal(audience.payload.actorId,'linxu');assert.equal(audience.payload.expression,'troubled');assert.equal(audience.payload.delivery,'关切，语速放缓。');
  const restored=new LiveSession({id:session.id,root,providers,artifacts:session.artifacts,songRoot:session.songRoot,saved:session.persistData()});
  assert.equal(restored.commands.get(command.commandId).payload.delivery,command.payload.delivery);restored.close();
});


test('streaming does not release a sentence until all metadata is present and valid',async()=>{
  for(const expression of ['smile',undefined]){
    const emitted=[];const result={action:'speak',sessionDecision:null,actorId:'z',delivery:'平静地说。',...(expression===undefined?{}:{expression}),mood:'warm',gesture:'none',transition:'smooth',handledIds:[],audienceMessageIds:[],speech:'你好。',wakeAfterMs:2000};
    const service=new Providers({DEEPSEEK_API_KEY:'test-only'},{fetch:async()=>new Response('data: '+JSON.stringify({choices:[{delta:{content:JSON.stringify(result)}}]})+'\n\ndata: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n')});
    const payload=input();payload.context.call={active:true,id:crypto.randomUUID(),input:'idle',turns:1,audienceOpen:false,audienceCandidates:[],audienceReason:'none'};
    await assert.rejects(()=>service.generate(crypto.randomUUID(),payload,{onSentence:s=>emitted.push(s)}));
    assert.deepEqual(emitted,[]);
  }
});
