// Exercise the real frontend adapter against the built Worker, mocking only the provider.
import assert from 'node:assert/strict';
import worker from '../dist/server/index.js';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {AgentClient} from '../ui/agent-client.js';
const originalFetch=globalThis.fetch,origin='https://dialogue-transport.test';
const wires=[],spoken=[],upstream=[];
let verdict='allow';
const engine=new ConversationEngine();
try {
  globalThis.fetch=async(url,options)=>{
    assert.equal(url,'https://api.deepseek.com/chat/completions');
    const input=JSON.parse(JSON.parse(options.body).messages[1].content);upstream.push(input);
    const answer=input.candidate?{decision:verdict,reason:'离线审阅结论',evidence_ids:[]}:
      input.allowed_candidate_npc_ids?{status:'ok',npc_id:input.allowed_candidate_npc_ids[0],intent:'reaction',text:'我想听听你的看法',evidence_ids:[input.shared_context.at(-1).id]}:
      {action:'speak',speech:'你好，听见你的招呼了。',mood:'warm',gesture:'none',transition:'smooth',handledIds:input.context.host.inbox.map(m=>m.id),wakeAfterMs:8000,memory:'已打招呼'};
    return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(answer)}}]});
  };
  const client=new AgentClient({fetchImpl:async(path,options)=>{
    const body=JSON.parse(options.body);wires.push(body);
    const response=await worker.fetch(new Request(origin+path,{...options,headers:{...options.headers,Origin:origin,'oai-authenticated-user-id':'dialogue-transport'}}),{DEEPSEEK_API_KEY:'fixture'});
    assert.equal(response.status,200,`${body.kind}: ${await response.clone().text()}`);
    return response;
  }});
  const config=await (await worker.fetch(new Request(origin+'/api/agent/config'),{DEEPSEEK_API_KEY:'fixture'})).json();
  engine.configureAgent(client,config);engine.setSpeechDriven(true);engine.addEventListener('speak',e=>spoken.push(e.detail));engine.start();
  await engine._think();
  assert.deepEqual(wires.map(p=>p.kind),['host','review']);assert.equal(spoken.length,1);
  assert(!Object.hasOwn(wires[1].context,'experience'));assert.equal(wires[1].context.candidate.role,'host');
  engine.completeSpeech(engine._active.id);
  engine.sendMessage('你好');await engine._think();assert.equal(spoken.length,2);engine.completeSpeech(engine._active.id);assert.equal(engine.inbox.length,0);
  console.log('PASS real engine → client → built Worker → independent review → host speech and player receipt');
  engine.audience.tick();
  for(let i=0;i<80&&(engine.audience.inflight||engine.audience.reviewPending);i++)await new Promise(r=>setTimeout(r,5));
  assert.equal(engine.audience.candidates.length,1);engine.audience.tick();
  assert(engine._state.messages.some(m=>m.source==='agent-audience'));
  const audience=wires.find(p=>p.kind==='audience');assert.equal(audience.context.experience,'autonomous');
  const review=wires.find(p=>p.kind==='review'&&p.context.candidate.role==='audience');assert(review);assert(!Object.hasOwn(review.context,'experience'));
  console.log('PASS audience generation receives its adapter context; independent audience review keeps its own schema');
  verdict='drop';engine.sendMessage('再说一句');await engine._think();assert.equal(spoken.length,2);assert.equal(engine._active,null);
  assert(engine.inbox.some(m=>m.text==='再说一句'));
  console.log('PASS rejected host still stays silent and retains the player message after transport repair');
} finally {engine.destroy();globalThis.fetch=originalFetch;}
