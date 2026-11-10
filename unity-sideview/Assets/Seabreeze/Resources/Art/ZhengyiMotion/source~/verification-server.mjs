// Simulated provider for the graphics verifier, never an ordinary game fallback.
import {createServer} from '../../../../../../Tools/MemoryLab/server.mjs';
import {resolve,join,sep} from 'node:path';
import {tmpdir} from 'node:os';
import {mkdirSync,writeFileSync} from 'node:fs';
const argument=name=>{const i=process.argv.indexOf(name);return i>=0?process.argv[i+1]:'';};
const requested=argument('--data');
if(!requested)throw new Error('Provide --data with an isolated temporary directory.');
const data=resolve(requested);
if(!['/tmp/','/private/tmp/',resolve(tmpdir())+sep].some(root=>data.startsWith(root)))throw new Error('Test data must be under a temporary directory.');
mkdirSync(data,{recursive:true});
function reply(c) {
  let action = c.available_actions.find((a) => a.type === "wait");
  if (c.interaction.mode === "npc_message" || c.environment.social_opportunity)
    action =
      c.available_actions.find((a) => a.type === "communicate") || action;
  const info = structuredClone(c.character_state.info);
  if (action.type === "communicate") {
    info.communication = {
      intent: "inform",
      shared_claim_ids: [],
      source_event_ids: [c.interaction.source_event_id],
    };
    info.social_decision = {
      choice: "engage",
      reason: "愿意交谈",
      source_event_ids: [c.interaction.source_event_id],
    };
  }
  if (c.character_state.cooperation_requests.length) {
    const q = c.character_state.cooperation_requests[0];
    info.task_participation = {
      task_id: q.task_id,
      choice: "declined",
      contribution: "先做自己的事",
      step_ids: [],
      reason: "当前有别的安排",
      source_event_ids: [c.interaction.source_event_id],
    };
  }
  return {
    dialogue:
      action.type === "communicate" ? "可以聊聊。" : "认识，李庭是咖啡店老板。",
    action: {
      action_id: action.id,
      type: action.type,
      target: action.target,
      parameters: action.parameters,
      description: "回答当前问题",
    },
    info,
  };
}

const decide=c=>{
  const value=reply(c);
  if(c.environment.intent_only){
    value.dialogue='';
    const a=c.available_actions.find(a=>a.type==='schedule_meeting');
    if(a)value.action={action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:'聊聊今天的岛上见闻'};
  }
  return value;
};
const app=createServer({dbPath:join(data,'fixture.sqlite'),configPath:join(data,'unused-model.json'),providerFactory:()=>({mode:'simulated',publicConfig:()=>({configured:true}),config:{topK:6}}),decide,backgroundEnabled:false});
app.server.listen(0,'127.0.0.1',()=>{
  const url='http://127.0.0.1:'+app.server.address().port;
  writeFileSync(join(data,'service-url'),url+'\n');
  console.log('SIMULATED verification service: '+url);
});
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>app.server.close(()=>{app.store.close();process.exit(0);}));
