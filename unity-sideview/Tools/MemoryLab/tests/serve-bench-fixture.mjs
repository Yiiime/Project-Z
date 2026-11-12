import {createServer} from '../server.mjs';
const app = createServer({dbPath:process.env.BENCH_TEST_DB || ':memory:', backgroundEnabled:false,
  providerFactory:()=>({mode:'simulated',publicConfig:()=>({configured:true}),config:{topK:6}}),
  decide:c=>{
    const info=structuredClone(c.character_state.info);
    const offer=c.available_actions.find(a=>a.type==='offer_task'&&a.parameters.task_id==='street_corner_bench');
    const action=offer || c.available_actions.find(a=>a.type==='wait') || c.available_actions[0];
    return {dialogue:c.environment.intent_only?'':offer?'能帮我看看咖啡店与花店之间的长椅吗？在楼梯左侧，走到旁边按一次 E 就好，不用回来交任务。':'谢谢你，这份委托已经记下了。去楼梯左侧的长椅旁看看吧。',
      action:{action_id:action.id,type:action.type,target:action.target,parameters:action.parameters,description:'回应当前玩家'},info};
  }});
app.server.listen(Number(process.env.BENCH_TEST_PORT||4339),'127.0.0.1',()=>console.log('Bench fixture ready'));
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>{app.server.close();app.store.close();process.exit(0);});
