import { Readable } from "node:stream";
import { randomUUID } from "node:crypto";
import { GatewayError } from "./common.mjs";

export function chatRequestToResponses(input) {
  return { model:input.model,input:input.messages,temperature:input.temperature,max_output_tokens:input.max_tokens??2048,stream:Boolean(input.stream),store:false,...(input.thinking?{thinking:input.thinking}:{}),...(input.response_format?{text:{format:input.response_format}}:{}) };
}
const outputText = value => (Array.isArray(value.output)?value.output:[]).filter(item=>item.type==='message').flatMap(item=>item.content||[]).filter(item=>item.type==='output_text'&&typeof item.text==='string').map(item=>item.text).join('');
const usage = value => value?{prompt_tokens:value.input_tokens??0,completion_tokens:value.output_tokens??0,total_tokens:value.total_tokens??((value.input_tokens??0)+(value.output_tokens??0))}:undefined;
function completed(value){if(value.error||value.status!=='completed')throw new GatewayError('方舟生成未完成，请重试',502,'model_unavailable');}

async function* chatStream(body, model) {
  const id='team_'+randomUUID(),created=Math.floor(Date.now()/1000),decoder=new TextDecoder('utf-8',{fatal:true});
  let pending='',lines=[],wire=0,text='',finished=false;
  const packet=(delta,finish,metrics)=>Buffer.from('data: '+JSON.stringify({id,object:'chat.completion.chunk',created,model,choices:[{index:0,delta,finish_reason:finish??null}],...(metrics?{usage:metrics}:{})})+'\n\n');
  function* event(raw){
    if(!raw||raw==='[DONE]')return;
    const data=JSON.parse(raw);
    if(data.error||['error','response.failed','response.incomplete'].includes(data.type))throw new GatewayError('方舟流式生成中断',502,'model_unavailable');
    if(finished)return;
    if(data.type==='response.output_text.delta'&&typeof data.delta==='string'){
      text+=data.delta;if(text.length>40000)throw new GatewayError('模型回复过长',502);
      yield packet({content:data.delta});
    }
    if(data.type==='response.completed'){
      const response=data.response||{};completed(response);
      const final=outputText(response);
      if(!text&&final){text=final;yield packet({content:final});}
      else if(final&&final!==text)throw new GatewayError('方舟流式内容与最终回复不一致',502);
      if(!text.trim())throw new GatewayError('模型没有返回文本',502);
      finished=true;yield packet({},'stop',usage(response.usage));yield Buffer.from('data: [DONE]\n\n');
    }
  }
  function* line(value){
    value=value.replace(/\r$/,'');
    if(!value){if(lines.length){const raw=lines.join('\n');lines=[];yield* event(raw);}}
    else if(value.startsWith('data:'))lines.push(value.slice(5).trimStart());
  }
  for await(const chunk of body){
    wire+=chunk.length;if(wire>2_000_000)throw new GatewayError('模型响应过大',502);
    pending+=decoder.decode(chunk,{stream:true});
    let at;while((at=pending.indexOf('\n'))>=0){yield* line(pending.slice(0,at));pending=pending.slice(at+1);}
  }
  pending+=decoder.decode();if(pending)yield* line(pending);yield* line('');
  if(!finished)throw new GatewayError('方舟流式回复未完成',502,'model_unavailable');
}

export async function responseForProfile(response, profile) {
  if(profile.apiType!=='responses'||profile.requestApiType!=='chat')return response;
  if(response.headers.get('content-type')?.includes('text/event-stream'))
    return new Response(Readable.toWeb(Readable.from(chatStream(response.body,profile.model))),{headers:{'Content-Type':'text/event-stream'}});
  const chunks=[];let size=0;
  for await(const chunk of response.body){size+=chunk.length;if(size>2_000_000)throw new GatewayError('模型响应过大',502);chunks.push(Buffer.from(chunk));}
  const value=JSON.parse(Buffer.concat(chunks).toString('utf8'));completed(value);
  const text=outputText(value);if(!text.trim())throw new GatewayError('模型没有返回文本',502);
  return Response.json({id:value.id||'team_'+randomUUID(),object:'chat.completion',created:Math.floor(Date.now()/1000),model:profile.model,choices:[{index:0,message:{role:'assistant',content:text},finish_reason:'stop'}],usage:usage(value.usage)});
}
