import assert from 'node:assert/strict';
import {AgentClient} from '../ui/agent-client.js';
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
const flush=async()=>{for(let i=0;i<10;i++)await Promise.resolve();};
const payload=(id,kind='decision')=>({requestId:id,kind,eventId:'event-'+id,message:'这是一条测试消息',context:{phase:'free'}});
const reply=id=>Response.json({requestId:id,action:'respond',speech:'收到。'});
let count=0;
async function test(name,fn){await fn();count++;console.log(`PASS ${name}`);}
await test('host requests serialize, waiting players precede queued talent, audience has an independent lane',async()=>{
 const pending=new Map(),seen=[];
 const client=new AgentClient({fetchImpl:async(_,options)=>{const request=JSON.parse(options.body),job=deferred();seen.push(request.requestId);pending.set(request.requestId,job);return job.promise;}});
 const first=client.generate(payload('first','talent')),talent=client.generate(payload('talent','talent')),player=client.generate(payload('player')),audience=client.generate(payload('npc','audience'));
 assert.deepEqual(seen,['first','npc']);pending.get('npc').resolve(reply('npc'));await audience;
 pending.get('first').resolve(reply('first'));await first;await flush();assert.deepEqual(seen,['first','npc','player']);
 pending.get('player').resolve(reply('player'));await player;await flush();assert.deepEqual(seen,['first','npc','player','talent']);
 pending.get('talent').resolve(reply('talent'));await talent;assert.equal(client.controllers.size,0);
});
await test('cancel rejects both in-flight fetch and queued decisions without dispatching the queue',async()=>{
 const seen=[];
 const client=new AgentClient({fetchImpl:(_,options)=>{seen.push(JSON.parse(options.body).requestId);return new Promise((_,reject)=>options.signal.addEventListener('abort',()=>reject(new Error('aborted')),{once:true}));}});
 const active=client.generate(payload('active')),queued=client.generate(payload('queued'));
 const results=Promise.allSettled([active,queued]);client.cancel();const settled=await results;
 assert.ok(settled.every(r=>r.status==='rejected'));assert.deepEqual(seen,['active']);assert.equal(client.controllers.size,0);
});
await test('a cancelled queued task never reaches the API',async()=>{
 const waiting=deferred(),seen=[],controller=new AbortController();
 const client=new AgentClient({fetchImpl:(_,options)=>{seen.push(JSON.parse(options.body).requestId);return waiting.promise;}});
 const active=client.generate(payload('active'));const skipped=client.generate(payload('skipped'),{signal:controller.signal});
 const rejected=assert.rejects(skipped,/expired|cancel/);controller.abort();waiting.resolve(reply('active'));await active;await rejected;assert.deepEqual(seen,['active']);
});
await test('mismatched response identity is rejected',async()=>{
 const client=new AgentClient({fetchImpl:async()=>reply('unrelated')});await assert.rejects(client.generate(payload('expected')),/unavailable/);
});
await test('the helper creates and validates one stable request ID when caller omits it',async()=>{
 let sent;
 const client=new AgentClient({fetchImpl:async(_,options)=>{sent=JSON.parse(options.body);return reply(sent.requestId);}});
 const result=await client.generate({kind:'decision',eventId:'no-id',message:'你好',context:{phase:'free'}});
 assert.ok(sent.requestId);assert.equal(result.requestId,sent.requestId);
});
await test('artwork exposes only safe error codes and has an independent lane',async()=>{
 const pending=deferred();
 const client=new AgentClient({fetchImpl:async(_,options)=>JSON.parse(options.body).kind==='artwork'?Response.json({error:'agent_invalid_reply',message:'private upstream detail'},{status:502}):pending.promise});
 const host=client.generate(payload('host','host'));
 await assert.rejects(client.generate(payload('art','artwork')),error=>error.message==='agent_invalid_reply');
 pending.resolve(reply('host'));await host;
 const unsafe=new AgentClient({fetchImpl:async()=>Response.json({error:'secret',message:'private detail'},{status:502})});
 await assert.rejects(unsafe.generate(payload('art','artwork')),error=>error.message==='agent_unavailable');
});
await test('stream errors preserve safe diagnostic codes and balance backoff',async()=>{
 for(const code of ['agent_invalid_reply','agent_insufficient_balance','private-provider-detail']){
  let calls=0;const timings=[];const client=new AgentClient({onTiming:t=>timings.push(t),fetchImpl:async(_,options)=>{calls++;return new Response(JSON.stringify({type:'error',requestId:JSON.parse(options.body).requestId,error:code})+'\n',{headers:{'Content-Type':'application/x-ndjson'}});}});
  const expected=code.startsWith('private')?'agent_unavailable':code;
  await assert.rejects(client.generate(payload('stream','host'),{onSentence(){}}),e=>e.code===expected);assert.equal(timings[0].errorCode,expected);
  if(code==='agent_insufficient_balance'){await assert.rejects(client.generate(payload('again','host'),{onSentence(){}}));assert.equal(calls,1);}
 }
});
console.log(`\n${count} agent client scenarios passed.`);
