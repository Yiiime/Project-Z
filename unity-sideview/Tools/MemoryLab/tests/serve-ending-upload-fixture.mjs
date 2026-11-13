import { createServer } from '../server.mjs';
import { authoredFile } from '../lib/ending.mjs';
import { writeFileSync } from 'node:fs';

const output=process.argv[2];
if(!output?.startsWith('/private/tmp/'))throw Error('Fixture output must be an isolated temporary path');
const provider={mode:'simulated',config:{topK:6},publicConfig:()=>({configured:true,mode:'simulated',model:'ending-upload-fixture'}),request:m=>m,
  call:async messages=>{
    const input=JSON.parse(messages[1].content);
    if(input.blocks)return {text:JSON.stringify({title:'上传后的第七晚',playerName:'字小节',context:'第七晚在码头，回望已经共同经历的事。',lastMessage:'',
      world:{departureReady:false,departureDetail:'',stayDetail:'未来尚无保证',leaveOutcome:'pending',leaveAftermath:'',stayOutcome:'uncertain',stayAftermath:''},
      blocks:input.blocks.map((b,i)=>({id:b.id,title:'新记忆'+(i+1),day:i+1,kind:'memory',zKnows:!b.text.includes('作者幕后')}))})};
    if(input.dailyPage)return {text:JSON.stringify({dailyPages:[{day:input.day,title:'第'+input.day+'天',text:'这一天发生了一件具体的事。\n我把当时听到和看到的内容放在一起。\n\n我还在想这件事对我意味着什么。'}]})};
    if(input.character_state){
      const a=input.available_actions.find(a=>a.type==='wait');
      return {text:JSON.stringify({dialogue:'',action:{action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:'等待'},info:{...input.character_state.info,changes:{},memory_updates:[],knowledge_updates:[]}})};
    }
    if(input.choice)return {text:JSON.stringify({recollections:input.memories.map(m=>({memoryId:m.id,text:'我记得这件事。'+m.text}))})};
    const uploaded=input.memories.some(m=>m.text.includes('NEW_UPLOAD_QA'));
    return {text:JSON.stringify({choice:uploaded?'stay':'leave',title:uploaded?'这一次，我带着新记忆':'这些日子仍然在这里',opening:'让我把这些经历再想一遍。',reason:'这是我的选择，经历会留在我的记忆里。',reasonMemoryIds:[],decision:uploaded?'我选择留下，明天还需要继续确认。':'我选择离开，真正的出发还需要确认。',lastLine:uploaded?'新上传的记忆，我也记住了。':'这一步，让我自己来。'})};
  }};
const app=createServer({dbPath:':memory:',providerFactory:()=>provider,backgroundEnabled:false,endingScenarioPath:authoredFile});
const open=app.endingUploads.open.bind(app.endingUploads);
app.endingUploads.open=(input,origin)=>{const url=open(input,origin);writeFileSync(output+'.upload-url',url,{mode:0o600});return url;};
app.server.listen(0,'127.0.0.1',()=>{
  const value={port:app.server.address().port,pid:process.pid};writeFileSync(output,JSON.stringify(value));console.log(JSON.stringify(value));
});
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>{app.server.closeAllConnections();app.server.close(()=>{app.store.close();process.exit(0);});});
