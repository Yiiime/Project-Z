import assert from 'node:assert/strict';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {parseAgentReply,buildAgentRequest,handleAgent} from '../worker/agent-director.js';
import {LIVE_PROFILE,TALENT_CATALOG} from '../ui/talent-config.js';
const e=new ConversationEngine();e.start();
try {
 for(const name of ['start_tarot','start_drawing','start_performance','edit_drawing','choose_tarot','set_panel','unknown']) {
  assert.equal(e._executeTool({name,args:{}}),false,name);
 }
 assert.equal(e.requestTarot({origin:'host',question:'最近的状态',spread:'single'}),false);
 assert.equal(e.requestDrawing({request:'画树'}),false);
 assert.equal(e._state.play.active,null);
} finally {e.destroy();}
const value={kind:'host',context:{host:{inbox:[],round:0},activeSkill:null,mediaRuntime:{audioEnabled:true,ttsAvailable:true},publicEvents:[]}};
const base={action:'tool',speech:'我唱一段。',mood:'warm',handledIds:[],wakeAfterMs:5000,memory:''};
for(const [name,args] of [
 ['start_tarot',{origin:'host',question:'最近的状态',spread:'single'}],['start_drawing',{request:'画树'}],
 ['start_performance',{title:'台词',scriptType:'original',lines:[]}],['set_panel',{kind:'drawing',taskId:'x',open:true}]
]) assert.throws(()=>parseAgentReply({...base,tool:{name,args}},value),e=>e.code==='capability_disabled');
const tool={name:'start_singing',args:{songId:TALENT_CATALOG.songs[0].id}};
assert.equal(parseAgentReply({...base,tool},value).tool.name,'start_singing');
assert.throws(()=>parseAgentReply({...base,tool:{...tool,args:{songId:'invented'}}},value));
assert.throws(()=>parseAgentReply({...base,tool},{...value,context:{...value.context,mediaRuntime:{audioEnabled:false}}}));
const config=await (await handleAgent(new Request('https://demo.test/api/agent/config'),{})).json();
assert.equal(config.liveProfile,LIVE_PROFILE.id);
assert.deepEqual(config.capabilities.map(x=>x.id),['audience-reaction','singing']);
assert(config.registry.every(x=>LIVE_PROFILE.skills.includes(x.id)));
const prompt=buildAgentRequest(value);
assert(prompt.system.includes('本版能力：singing-only'));
const audience=buildAgentRequest({kind:'audience',eventId:'e',requestId:crypto.randomUUID(),context:{experience:'autonomous',publicEvents:[{id:'e',type:'player.message',text:'想听歌'}]}});
assert.deepEqual(audience.input.capabilities.map(x=>x.id),['singing']);
console.log('PASS singing-only profile, model validation, execution gates, song/audio checks and audience capabilities');
