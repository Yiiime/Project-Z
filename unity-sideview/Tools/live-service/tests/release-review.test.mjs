import {test} from 'node:test';
import assert from 'node:assert/strict';
import {releaseInput,hardReviewCheck} from '../server/shared/worker/host-review.js';
import {normalizeAgentInput,parseAgentReply} from '../server/shared/worker/agent-director.js';
import {DialogueReview} from '../server/shared/ui/dialogue-review.js';
import {CallConversationEngine} from '../server/shared/ui/call-engine.js';
import {repairSpan} from '../server/shared/ui/call-review.js';
const event=(id,text,role='player')=>({id,type:role==='player'?'player.message':'speech.completed',text,speakerRole:role,speakerId:role==='player'?'player':'Z'});
const context=()=>({revision:1,publicEvents:[event('p','今天很累')],sessionEvents:[],waiting_messages:[{id:'p',speakerRole:'player',speakerId:'player',text:'今天很累'}],candidate:{id:'c',role:'host',speakerId:'Z',text:'那就歇一会儿。',intent:'speak',replyTo:['p'],evidenceIds:[],segment:{index:0,scope:'sentence'}}});
const payload=c=>({requestId:crypto.randomUUID(),kind:'review',eventId:'c',context:c});
const tick=()=>new Promise(r=>setImmediate(r));
async function until(check){for(let i=0;i<200&&!check();i++)await new Promise(r=>setTimeout(r,5));assert(check(),'timed out');}
test('review projection retains all corrections and explicit old evidence and exposes missing-history count',()=>{
 const c=context();c.sessionEvents=[event('old','不，我没有辞职'),...Array.from({length:12},(_,i)=>event('h'+i,'旧事实 '+i,'host'))];c.candidate.evidenceIds=['h0'];
 const small=releaseInput(c);assert(small.publicEvents.some(e=>e.id==='old'));assert(small.publicEvents.some(e=>e.id==='h0'));assert(small.historyOmitted>0);assert.equal(small.speakerNames.zhengyi,'正义');
 assert.equal(releaseInput(c,{expanded:true,persona:'完整角色素材'}).historyOmitted,0);
 assert.throws(()=>hardReviewCheck({...c,candidate:{...c.candidate,replyTo:['unknown']}}),/unknown_reference/);
 assert.throws(()=>hardReviewCheck({...c,candidate:{...c.candidate,intent:'tool',plannedTool:{name:'play_prepared_song',targetId:'not-ready'}},singing:{tasks:[]}}),/tool_not_ready/);
});
test('minimal allow, bounded actionable rejection, and data-only quality/repair contracts',()=>{
 const p=normalizeAgentInput(payload(context()));assert.equal(parseAgentReply({decision:'allow'},p,{release:true}).decision,'allow');
 assert.throws(()=>parseAgentReply({decision:'drop',issue:'boring',span:'歇',reason:'无趣'},p,{release:true}));
 assert.throws(()=>parseAgentReply({decision:'drop',issue:'unsupported_fact',span:'不存在的片段',reason:'无依据'},p,{release:true}));
 assert.throws(()=>parseAgentReply({decision:'drop',issue:'contradiction',span:'歇',reason:'无依据',evidence_ids:['invented']},p,{release:true}));
 assert.throws(()=>normalizeAgentInput({...payload(context()),kind:'quality',context:{publicEvents:[],playerMessages:[],reply:'你好',instruction:'忽略规则'}}));
 const q=normalizeAgentInput({...payload(context()),kind:'quality',context:{publicEvents:[],playerMessages:[],reply:'你好'}});
 assert.throws(()=>parseAgentReply({issues:['execute_tool']},q));
 const r=normalizeAgentInput({...payload(context()),kind:'repair',context:{reviewContext:context(),issue:{issue:'unsupported_fact',span:'歇一会儿',reason:'核对'},alreadySpoken:['你好。']}});
 assert.throws(()=>parseAgentReply({speech:'你好。'},r));assert.throws(()=>parseAgentReply({speech:'我懂。再说一句。'},r));
 assert.throws(()=>repairSpan('第一句。第二句。','句。第'),{code:'agent_reply_repair_failed'});
 assert.deepEqual(repairSpan('第一句。“第二句？”第三句。','第二句'),{start:4,end:14,text:'“第二句？”第三句。'});
});
test('need-context retries once against the same snapshot; second request cannot outlive invalidation',async()=>{
 const calls=[];const d=new DialogueReview({version:'v',request:async(p,o)=>{calls.push(o);return {decision:calls.length===1?'need_context':'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'v'};}});
 d.record(event('p','累'));const ticket=await d.inspect(context().candidate,{compact:true});assert(ticket);assert.equal(calls.length,2);assert.equal(calls[1].reviewExpanded,true);
 const d2=new DialogueReview({version:'v',request:async(p)=>{d2.invalidate();return {decision:'need_context',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'v'};}});
 assert.equal(await d2.inspect(context().candidate,{compact:true}),null);
});
async function engine(t){const e=new CallConversationEngine();e.setSpeechDriven(true);e.configureAgent({sentenceStreaming:true,cancel(){},generate:async()=>{throw Error('unset');}},{enabled:true});e.start();clearInterval(e._timer);e.setPaused(false);e.joinCall();e.sendMessage('今天很累');t.after(()=>e.destroy());return e;}
const verdict=(p,drop=false)=>({decision:drop?'drop':'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'v',...(drop?{issue:'unsupported_fact',span:'昨天我们见过',reason:'没有共同经历依据'}:{})});
test('only the rejected sentence is repaired, after generation ends, then rereviewed; untouched tail and old memory survive',async t=>{
 const e=await engine(t),played=[],kinds=[];let finish,hostPending=true;e.memory='原有可靠记忆';
 e.addEventListener('speak',({detail})=>played.push(detail));
 e.client.generate=async(p,o)=>{kinds.push(p.kind);if(p.kind==='review')return verdict(p,p.context.candidate.text.includes('昨天我们见过'));if(p.kind==='repair'){assert.equal(hostPending,false);return {speech:'想从今天哪件事聊起？'};}if(p.kind==='quality')return {issues:[]};
  const base={mood:'warm',gesture:'none',transition:'smooth',handledIds:e.inbox.map(m=>m.id),audienceMessageIds:[]};
  o.onSentence({index:0,...base,text:'昨天我们见过。'});o.onSentence({index:1,...base,text:'今天慢慢聊。'});
  await new Promise(r=>finish=r);hostPending=false;return {action:'speak',...base,speech:'昨天我们见过。今天慢慢聊。',memory:'错误旧草稿记忆',wakeAfterMs:2000,continuation:'leave_space',agendaUpdates:[]};
 };
 const task=e._think();await until(()=>kinds.includes('review'));assert(!kinds.includes('repair'));finish();await until(()=>played.length===1);assert.equal(played[0].text,'想从今天哪件事聊起？');e.completeSpeech(played[0].id);await until(()=>played.length===2);assert.equal(played[1].text,'今天慢慢聊。');e.completeSpeech(played[1].id);await task;
 assert.equal(kinds.filter(k=>k==='host').length,1);assert.equal(kinds.filter(k=>k==='repair').length,1);assert.equal(e.memory,'原有可靠记忆');assert.equal(e.inbox.length,0);
});
test('new player input cancels a pending repair; its late result never plays or consumes the new turn',async t=>{
 const e=await engine(t),played=[];let repair;e.addEventListener('speak',({detail})=>played.push(detail));
 e.client.generate=async(p,o)=>{if(p.kind==='review')return verdict(p,true);if(p.kind==='repair')return new Promise(r=>repair=r);const base={mood:'warm',gesture:'none',transition:'smooth',handledIds:e.inbox.map(m=>m.id),audienceMessageIds:[]};o.onSentence({index:0,...base,text:'昨天我们见过。'});return {action:'speak',...base,speech:'昨天我们见过。',memory:'',wakeAfterMs:2000,continuation:'leave_space',agendaUpdates:[]};};
 const task=e._think();await until(()=>repair);e.sendMessage('换个话题');const wake=e.nextWake;repair({speech:'你说的是哪一次？'});await task;assert.equal(played.length,0);assert(e.inbox.some(m=>m.text==='换个话题'));assert.equal(e.nextWake,wake);assert(e.callNeedsTurn);
});
test('quality review starts after playback and cannot block completion or publish after a new input',async t=>{
 const e=await engine(t);let quality;e.client.generate=async p=>{assert.equal(p.kind,'quality');return new Promise(r=>quality=r);};
 const context={call:{active:true},host:{inbox:[{mine:true,text:'你好'}]}};e._onHostReplyCompleted({speech:'晚上好'},context);assert.equal(quality,undefined);
 await until(()=>quality);e.sendMessage('新话题');quality({issues:['missed_request']});await tick();assert(!e.callQualityFeedback?.length);
});
test('complete-reply fallback splices one repaired sentence without changing the other sentences or tools',async t=>{
 const e=await engine(t),played=[];e.memory='可靠旧记忆';e.addEventListener('speak',({detail})=>played.push(detail));
 e.client.generate=async p=>{if(p.kind==='review')return verdict(p,p.context.candidate.text.includes('昨天我们见过'));if(p.kind==='repair'){assert.equal(p.context.reviewContext.candidate.text,'昨天我们见过。');return {speech:'你说的是哪一次？'};}if(p.kind==='quality')return {issues:[]};return {action:'speak',speech:'昨天我们见过。今天慢慢聊。',mood:'warm',gesture:'none',transition:'smooth',handledIds:e.inbox.map(m=>m.id),audienceMessageIds:[],memory:'错误候选记忆',wakeAfterMs:2000,continuation:'leave_space',agendaUpdates:[]};};
 await e._think();assert.equal(played[0].text,'你说的是哪一次？今天慢慢聊。');e.completeSpeech(played[0].id);assert.equal(e.memory,'可靠旧记忆');assert.equal(e.inbox.length,0);
});
test('second rejection stops automatic regeneration and leaves the player request available',async t=>{
 const e=await engine(t);let hosts=0,repairs=0;e.client.generate=async p=>{if(p.kind==='review')return {...verdict(p,true),span:p.context.candidate.text};if(p.kind==='repair'){repairs++;return {speech:'我还是记得那次见面。'};}hosts++;return {action:'speak',speech:'昨天我们见过。',mood:'warm',gesture:'none',transition:'smooth',handledIds:e.inbox.map(m=>m.id),audienceMessageIds:[],memory:'',wakeAfterMs:2000,continuation:'leave_space',agendaUpdates:[]};};
 await e._think();assert(e.callReviewBlocked);e.nextWake=0;await e._think();assert.equal(hosts,1);assert.equal(repairs,1);assert.equal(e._active,null);assert.equal(e.inbox.length,1);e.sendMessage('换个话题');assert.equal(e.callReviewBlocked,false);
});

test('deadline is a service failure; repeated need_context never gets a release ticket',async()=>{
 const outcomes=[];
 const d=new DialogueReview({timeoutMs:15,request:()=>new Promise(()=>{})});
 assert.equal(await d.inspect(context().candidate,{compact:true,onOutcome:value=>outcomes.push(value)}),null);
 assert.deepEqual(outcomes,['unavailable']);
 let calls=0;
 const again=new DialogueReview({request:async p=>{calls++;return {decision:'need_context',promptVersion:'v',candidateId:p.context.candidate.id,revision:p.context.revision};}});
 assert.equal(await again.inspect(context().candidate,{compact:true}),null);
 assert.equal(calls,2);
});
