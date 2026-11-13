import { createServer } from '../server.mjs';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const flag = name => process.argv[process.argv.indexOf(name) + 1];
const output = process.argv.includes('--output') ? resolve(flag('--output')) : null;
if (output && !output.startsWith('/private/tmp/')) throw new Error('Use an isolated /private/tmp verification directory');
const app = createServer({
  dbPath: ':memory:', backgroundEnabled: false,
  providerFactory: () => ({ mode: 'simulated-test', config: { topK: 6 }, publicConfig: () => ({ configured: true }) }),
  decide: c => {
    let a = c.available_actions.find(a => a.type === 'wait');
    const info={emotion:c.character_state.info.emotion,changes:{},...(c.actor_id==='z'?{}:{support_willingness:c.character_state.info.support_willingness})};
    let dialogue=c.interaction.mode==='player'?(/老板/.test(c.interaction.input)?'李庭是咖啡店老板。':c.environment.map_context.current_place.name+'，我就在这里。'):'';
    if(/暗号/.test(c.interaction.input)&&JSON.stringify([c.character_state.recent_experiences,c.character_state.memories]).includes('蓝色风筝'))dialogue='你刚才说的暗号是蓝色风筝。';
    if(c.environment.social_opportunity)info.social_decision={choice:'keep_to_self',reason:'验收保持各自状态',source_event_ids:[c.interaction.source_event_id]};
    if(c.character_state.pending_action?.type==='escort_player'){
      const pending=c.character_state.pending_action;
      if(/取消|别去|不去|停下/.test(c.interaction.input)){a=c.available_actions.find(a=>a.type==='cancel_action');info.travel_decision={choice:'cancel',destination_id:pending.target};dialogue='好，先停下来。';}
      else if(c.environment.companion_request){info.travel_decision={choice:'continue',destination_id:pending.target};dialogue='我们继续走，到了再聊。';}
    }else if(c.environment.companion_request){
      const escort=c.available_actions.find(a=>a.type==='escort_player'&&c.environment.companion_request.destination_ids.includes(a.target));
      if(escort){a=escort;info.travel_decision={choice:'accept',destination_id:escort.target};dialogue='好，我们一起走，到了再聊。';}
      else{info.travel_decision={choice:'decline',destination_id:c.environment.companion_request.destination_ids[0]};dialogue='现在先留在这里。';}
    }
    return {dialogue,action:{option_id:a.id},info};
  },
});
app.engine.create('runtime_preview');
app.engine.updateEnvironment('runtime_preview','linxu',{sceneId:'Harbor',locationId:'dock',locationName:'码头',x:6,y:0,movementState:'arrived',reason:'movement_arrived'});
app.engine.observe('runtime_preview','引擎确认林序实际到达码头。',['linxu'],{wake:false,kind:'fact'});
app.server.listen(process.argv.includes('--port') ? Number(flag('--port')) : 0, '127.0.0.1', () => {
  const url = 'http://127.0.0.1:' + app.server.address().port;
  if(output){mkdirSync(output,{recursive:true});writeFileSync(output+'/service-url.txt',url);writeFileSync(output+'/service-pid.txt',String(process.pid));}
  console.log(JSON.stringify({pid:process.pid,url,viewer:url+'/runtimes',mode:'simulated-test',database:':memory:'}));
});
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>app.server.close(()=>{app.store.close();process.exit(0);}));
