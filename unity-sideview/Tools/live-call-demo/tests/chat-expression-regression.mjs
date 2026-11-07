import {reviewReply} from './review-fixture.mjs';
import assert from 'node:assert/strict';
import {ConversationEngine} from '../ui/conversation-engine.js';
import {TALENT_MOODS,TALENT_GESTURES,EXPRESSION_GESTURES} from '../ui/talent-config.js';
import {parseAgentReply,buildAgentRequest,handleAgent} from '../worker/agent-director.js';

const response=(extra={})=>({action:'speak',speech:'我在听，你慢慢说。',mood:'warm',gesture:'none',transition:'smooth',handledIds:[],wakeAfterMs:8000,memory:'',...extra});
const value={kind:'host',context:{host:{inbox:[],recentExpressions:[]},publicEvents:[]}};
assert.equal(TALENT_MOODS.length,8);assert.equal(TALENT_GESTURES.length,17);
assert.equal(EXPRESSION_GESTURES.filter(x=>x.id!=='none').length,16);
for(const mood of TALENT_MOODS)for(const gesture of TALENT_GESTURES){
  const result=parseAgentReply(response({mood,gesture}),value);assert.equal(result.mood,mood);assert.equal(result.gesture,gesture);
}
assert.equal(parseAgentReply(response({gesture:undefined,transition:undefined}),value).gesture,'none');
assert.equal(parseAgentReply(response({gesture:undefined,transition:undefined}),value).transition,'smooth');
for(const gesture of ['explain','sampleM26','cheek','dance'])assert.throws(()=>parseAgentReply(response({gesture}),value));
assert.throws(()=>parseAgentReply(response({transition:'random'}),value));
assert.throws(()=>parseAgentReply(response({action:'wait',speech:'',gesture:'handWave'}),value));
assert.equal(parseAgentReply(response({action:'wait',speech:''}),value).gesture,'none');
console.log('PASS all 8 moods and 16 actions plus none, safe defaults, unavailable actions and wait validation');

const engine=new ConversationEngine(),sent=[],spoken=[];let next=response();
engine.configureAgent({cancel(){},async generate(payload){if(payload.kind==='review')return reviewReply(payload);sent.push(payload);return next;}},{enabled:true});
engine.setSpeechDriven(true);engine.addEventListener('speak',event=>spoken.push(event.detail));engine.start();
for(let index=0;index<TALENT_GESTURES.length;index++){
  next=response({mood:TALENT_MOODS[index%8],gesture:TALENT_GESTURES[index],transition:index===2?'sudden':'smooth'});
  engine.nextWake=0;await engine._think();
  assert.equal(spoken.at(-1).gesture,next.gesture);assert.equal(spoken.at(-1).mood,next.mood);assert.equal(spoken.at(-1).transition,next.transition);
  assert.equal(engine._active.gesture,next.gesture);engine.completeSpeech(engine._active.id);
}
assert.equal(engine.recentExpressions.length,6);assert.equal(sent.at(-1).context.host.recentExpressions.length,6);
const history=structuredClone(engine.recentExpressions),count=spoken.length;
next=response({action:'wait',speech:''});engine.nextWake=0;await engine._think();assert.equal(spoken.length,count);assert.deepEqual(engine.recentExpressions,history);
next=response({gesture:'sampleM26'});engine.nextWake=0;await engine._think();assert.equal(spoken.length,count,'Browser revalidates unavailable actions');
engine.replay();assert.equal(engine.recentExpressions.length,0);engine.destroy();
console.log('PASS ordinary chat cues propagate to speech playback; bounded history; wait has no action; reset clears history');

const late=new ConversationEngine();let resolve;
late.configureAgent({cancel(){},generate:()=>new Promise(r=>resolve=r)},{enabled:true});late.start();
const pending=late._think();late.sendMessage('先等一下。');resolve(response({gesture:'startle',transition:'sudden'}));await pending;
assert.equal(late.recentExpressions.length,0);assert.equal(late._active,null);late.destroy();
console.log('PASS stale model decision cannot trigger a late reaction or enter expression history');

const envelope=buildAgentRequest(value);
assert.equal(envelope.input.expressionResources.gestures.length,17);
assert(envelope.input.expressionResources.gestures.every(g=>g.label));
assert.match(envelope.system,/不要关键词匹配或轮流展示动作库/);
const origin='https://expression-fixture.test';
const request=history=>new Request(origin+'/api/agent',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','oai-authenticated-user-id':'expression-test'},
  body:JSON.stringify({requestId:crypto.randomUUID(),eventId:'wake',kind:'host',message:'决定下一步',context:{phase:'chat',host:{inbox:[],memory:'',recentExpressions:history}}})});
let calls=0;
const fetch=async(_,init)=>{calls++;const body=JSON.parse(init.body),input=JSON.parse(body.messages[1].content);assert.equal(input.context.host.recentExpressions[0].gesture,'none');return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(response({mood:'shy',gesture:'coverMouth'}))}}]});};
const good=await handleAgent(request([{mood:'warm',gesture:'none',transition:'smooth',speech:'接着聊吧。'}]),{DEEPSEEK_API_KEY:'fixture'},{fetch});
assert.equal(good.status,200);assert.equal((await good.json()).gesture,'coverMouth');
const bad=await handleAgent(request([{mood:'warm',gesture:'unknown',transition:'smooth',speech:'接着聊吧。'}]),{DEEPSEEK_API_KEY:'fixture'},{fetch});
assert.equal(bad.status,400);assert.equal(calls,1);
console.log('PASS host API supplies labeled actions and recent context, validates history, returns contextual expression cues');
