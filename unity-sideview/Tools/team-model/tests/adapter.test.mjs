import test from 'node:test';
import assert from 'node:assert/strict';
import {chatRequestToResponses,responseForProfile} from '../adapter.mjs';
const profile={apiType:'responses',requestApiType:'chat',model:'ep-fixture'};
const completed=text=>({status:'completed',output:[{type:'message',content:[{type:'output_text',text}]}],usage:{input_tokens:10,output_tokens:3,total_tokens:13}});

test('live chat request preserves structured output and uses non-stored Responses input',()=>{
  const messages=[{role:'user',content:'你好'}];
  assert.deepEqual(chatRequestToResponses({model:'ep-fixture',messages,max_tokens:600,stream:true,response_format:{type:'json_object'},thinking:{type:'disabled'}}),{model:'ep-fixture',input:messages,temperature:undefined,max_output_tokens:600,stream:true,store:false,thinking:{type:'disabled'},text:{format:{type:'json_object'}}});
});
test('non-streaming Ark reply becomes the chat completion expected by the live agent',async()=>{
  const converted=await responseForProfile(Response.json(completed('{"speech":"你好"}')),profile),value=await converted.json();
  assert.equal(value.model,'ep-fixture');assert.equal(value.choices[0].message.content,'{"speech":"你好"}');assert.equal(value.choices[0].finish_reason,'stop');assert.equal(value.usage.total_tokens,13);
});
test('byte-split Ark stream keeps Unicode, stop marker, usage and DONE for the live parser',async()=>{
  const wire=Buffer.from('data: '+JSON.stringify({type:'response.output_text.delta',delta:'你好'})+'\n\ndata: '+JSON.stringify({type:'response.completed',response:completed('你好')})+'\n\n');
  const body=new ReadableStream({start(c){for(const byte of wire)c.enqueue(Uint8Array.of(byte));c.close();}});
  const converted=await responseForProfile(new Response(body,{headers:{'Content-Type':'text/event-stream'}}),profile);
  const frames=(await converted.text()).split('\n\n').filter(Boolean).map(line=>line.slice(6));
  assert.equal(JSON.parse(frames[0]).choices[0].delta.content,'你好');assert.equal(JSON.parse(frames[1]).choices[0].finish_reason,'stop');assert.equal(JSON.parse(frames[1]).usage.total_tokens,13);assert.equal(frames[2],'[DONE]');
});
test('interrupted, incomplete and divergent Ark replies cannot report a successful live completion',async()=>{
  for(const events of [[{type:'response.output_text.delta',delta:'半句'}],[{type:'response.completed',response:{status:'incomplete'}}],[{type:'response.output_text.delta',delta:'旧句'},{type:'response.completed',response:completed('新句')}]]){
    const source=events.map(event=>'data: '+JSON.stringify(event)+'\n\n').join('');
    const converted=await responseForProfile(new Response(source,{headers:{'Content-Type':'text/event-stream'}}),profile);
    await assert.rejects(converted.text());
  }
});
