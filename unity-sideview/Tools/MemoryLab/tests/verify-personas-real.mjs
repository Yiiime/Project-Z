// Opt-in paid model smoke test. Uses an isolated in-memory save, never real player history.
import fs from 'node:fs';
import {createServer} from '../server.mjs';
import {Providers} from '../../live-service/server/providers.mjs';
const configPath=process.env.Z_PERSONA_REAL_CONFIG;
if(!configPath)throw Error('Set Z_PERSONA_REAL_CONFIG to an existing private model config file');
const output=process.env.Z_PERSONA_REAL_OUTPUT||'/private/tmp/pr54-real-personas.json';
if(!output.startsWith('/private/tmp/'))throw Error('Use a temporary report path');
const config=JSON.parse(fs.readFileSync(configPath,'utf8'));
const app=createServer({dbPath:':memory:',backgroundEnabled:false,configPath});
const e=app.engine;e.create('persona_real');
const prompts={z:'花店是谁开的，船长叫什么？你平时和他们熟吗？',liyuan:'你平时做什么？你怎么看周伯伯？',liting:'Z说跟你闹过别扭，你们以前是什么关系？',axing:'你做什么工作，你能听见岛上所有人的私聊吗？',zhengyi:'梨园为什么总躲着人，你觉得他怎么样？',xiaoxiaoqing:'留洋是你朋友吗？压花册是你发明的吗，能安全送人吗？',linxu:'你的船是谁的？如果Z不愿意走，你会替她安排吗？',zhou_bobo:'你开照相馆的？听说你是看着Z长大的，你能帮我们带她离岛吗？'};
const report={island:[],live:[],passed:true};
for(const [actor,text] of Object.entries(prompts)){
 const start=Date.now();try{
  const job=e.player('persona_real',actor,text);const r=(await e.run('persona_real',1,job.id))[0];
  if(r.status!=='proposed')throw Error(r.validation_error||r.status);
  e.commit('persona_real',r.proposalId);
  report.island.push({actor,question:text,milliseconds:Date.now()-start,result:r.output});
  console.log(actor+': schema accepted ('+(Date.now()-start)+' ms)');
 }catch(error){report.passed=false;report.island.push({actor,error:error.message,milliseconds:Date.now()-start});console.log(actor+': '+error.message);}
}
const live=new Providers({DEEPSEEK_API_KEY:config.apiKey});
const ids=[...e.catalog.actors.keys()].filter(id=>id!=='z');
for(const actor of ids){
 const name=e.catalog.actor(actor).name,event={id:'hello-'+actor,type:'speech.completed',text:name+'也来了，给新来的朋友讲一句你平时在岛上做的事吧。',speakerRole:'host',speakerId:'Z'};
 const p={requestId:crypto.randomUUID(),kind:'audience',eventId:event.id,context:{phase:'chat',publicEvents:[event],audience:{eligibleNpcIds:[actor]}}};
 const start=Date.now();try{
  const result=await live.generate('persona-release-real',p);report.live.push({actor,milliseconds:Date.now()-start,result});console.log('live '+actor+': '+result.status);
 }catch(error){report.passed=false;report.live.push({actor,error:error.message,milliseconds:Date.now()-start});console.log('live '+actor+': '+error.message);}
}
fs.writeFileSync(output,JSON.stringify(report,null,2));e.cancel('persona_real');app.store.close();
if(!report.passed)process.exitCode=1;
