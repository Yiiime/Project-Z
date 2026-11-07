// Explicit manual live smoke test; reuses the configured provider without printing credentials.
import fs from 'node:fs/promises';
import {handleAgent} from '../worker/agent-director.js';
import {renderDrawingScene} from '../ui/drawing-scene.js';
const root=new URL('../',import.meta.url),out=new URL('output/drawing-check/',root);
let secret=process.env.DEEPSEEK_API_KEY?.trim()||'';
for(const file of ['DeepSeek配置.env','.env.local']){
  if(secret)break;try{const source=await fs.readFile(new URL(file,root),'utf8');const raw=source.match(/^\s*(?:export\s+)?DEEPSEEK_API_KEY\s*=\s*(.*?)\s*$/m)?.[1]||'';secret=/^['"]/.test(raw)?raw.match(/^(['"])(.*?)\1\s*(?:#.*)?$/)?.[2]?.trim()||'':raw.replace(/\s+#.*$/,'').trim();}catch{}
}
if(!secret)throw Error('Missing configured DeepSeek key');
await fs.mkdir(out,{recursive:true});
const request='画一艘红色小帆船，蓝色海浪，上方一个黄色太阳，用简笔画表达。';
const origin='https://drawing-check.invalid',job={taskId:'boat-check',revision:0,request};
async function run(drawingJob,name){
  const started=Date.now();
  const req=new Request(origin+'/api/agent',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','oai-authenticated-user-id':'local-drawing-verification'},body:JSON.stringify({requestId:crypto.randomUUID(),kind:'artwork',eventId:name,message:'绘画执行检查',context:{drawingJob}})});
  const response=await handleAgent(req,{DEEPSEEK_API_KEY:secret},{fetch:async(url,init)=>{
    const r=await fetch(url,init);const data=await r.clone().json();
    await fs.writeFile(new URL(name+'-provider.json',out),JSON.stringify({status:r.status,finishReason:data.choices?.[0]?.finish_reason,content:data.choices?.[0]?.message?.content},null,2));return r;
  }});
  const body=await response.json();await fs.writeFile(new URL(name+'.json',out),JSON.stringify(body,null,2));
  console.log(JSON.stringify({test:name,status:response.status,outcome:body.status,error:body.error,elapsedMs:Date.now()-started,elements:body.scene?.elements.length,title:body.scene?.title}));
  if(body.scene)await fs.writeFile(new URL(name+'.svg',out),renderDrawingScene(body.scene,body.scene.stages.length-1));
  return body;
}
const first=await run(job,'boat');
if(first.scene&&process.argv.includes('--edit')){
  const targets=first.scene.elements.filter(e=>/船身|船体|船底|船舷/.test(e.label)).map(e=>e.id);
  if(targets.length)await run({...job,revision:1,base:first.scene,edit:{instruction:'只把红色船体改成绿色，其余海浪、太阳和船帆保持原样；同步更新画名中的颜色。',targetIds:targets}},'boat-edit');
  else console.log('No unambiguous hull objects found; edit test not submitted.');
}
