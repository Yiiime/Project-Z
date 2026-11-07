// Actual UI handlers with fake recognition: no microphone or paid requests.
import assert from 'node:assert/strict';
import {mountCallUI} from '../ui/call-ui.js';
import {CallConversationEngine} from '../ui/call-engine.js';
const elements=new Map();
class Element extends EventTarget{
 constructor(){super();this.value='';this.maxLength=120;this.hidden=false;this.disabled=false;this.dataset={};this.children=[];this.attrs={};this.classList={toggle(){}};}
 set id(v){this._id=v;elements.set(v,this);}get id(){return this._id;}
 setAttribute(k,v){this.attrs[k]=v;}querySelector(id){return elements.get(id.slice(1));}prepend(x){this.children.unshift(x);}append(x){this.children.push(x);}insertBefore(x){this.children.push(x);}replaceChildren(){this.children=[];}remove(){}
}
for(const id of ['chat-form','chat-input','send-message','reply-status-text','call-story-question','call-story-choices','speech-box']){const e=new Element();e.id=id;}
const document=new EventTarget();Object.assign(document,{hidden:false,createElement:()=>new Element(),querySelector:()=>new Element(),getElementById:id=>elements.get(id)});const window=new EventTarget();globalThis.document=document;globalThis.window=window;
const originalSet=globalThis.setTimeout,originalClear=globalThis.clearTimeout;let timerId=0;const timers=new Map();globalThis.setTimeout=(fn,ms)=>{timers.set(++timerId,{fn,ms});return timerId;};globalThis.clearTimeout=id=>timers.delete(id);
const run=ms=>{for(const [id,t]of [...timers])if(t.ms===ms){timers.delete(id);t.fn();}};
const devices=[];class Recognition{constructor(){devices.push(this);}start(){this.started=true;}stop(){this.stopped=true;this.onend?.();}abort(){this.aborted=true;}}
globalThis.SpeechRecognition=Recognition;
if(!globalThis.CustomEvent)globalThis.CustomEvent=class extends Event{constructor(type,{detail}){super(type);this.detail=detail;}};
const engine=new CallConversationEngine();engine.enabled=true;engine.setSpeechDriven(true);engine.start();clearInterval(engine._timer);
const sent=[],toasts=[];engine.addEventListener('message',({detail})=>{if(detail.mine)sent.push(detail.text);});
const director=new EventTarget();director.setEnabled=async()=>{};director.cancel=()=>{};
let thinkingPlays=0,thinkingStops=0;director.playCallThinking=()=>{thinkingPlays++;return true;};director.stopCallThinking=()=>{thinkingStops++;};
const ui=mountCallUI({engine,director,Recognition,toast:t=>toasts.push(t),unpause(){}}),button=elements.get('call-mic');
const result=(text,confidence=.92,final=true)=>{const r=[{transcript:text,confidence}];r.isFinal=final;return {resultIndex:0,results:[r]};};
const latest=()=>devices.at(-1),guest=()=>ui.captions.rows.findLast(r=>r.role==='guest'),reply=()=>ui.captions.rows.at(-1);
const say=(full,played)=>{engine._say(full);const id=engine._active.id;director.dispatchEvent(new CustomEvent('text',{detail:{id,text:played}}));engine.recordSpeechProgress(id,played);return id;};
const complete=id=>{director.dispatchEvent(new CustomEvent('complete',{detail:{id}}));engine.completeSpeech(id);};
try{
 assert.equal(button.attrs['aria-label'],'上麦');button.onclick();assert(engine.getState().call.active);assert.equal(ui.captions.rows.length,0,'no placeholder before lexical text');assert.equal(devices.length,1);let r=latest();assert(r.continuous&&r.interimResults);assert(engine.callNeedsTurn,'arming recognition does not steal greeting');
 r.onresult(result('嗯',.9,false));assert.equal(ui.captions.rows.length,0,'ambient/filler is not a conversation');
 const idleDeadline=[...timers].find(([,t])=>t.ms===30000)?.[0];
 r.onresult(result('今天有点累',.9,false));assert(!timers.has(idleDeadline),'first speech resets the idle deadline so late speech is not cut off');assert.equal(sent.length,0);assert.equal(guest().status,'draft');assert.equal(reply().text,'正在听…');assert.equal(ui.captions.rows.length,2);
 r.onresult(result('今天有点累'));const timer=[...timers].find(([,t])=>t.ms===1200)?.[0];r.onresult(result('今天有点累'));assert(timers.has(timer),'identical transcript does not reset silence deadline');
 r.onend();assert.equal(sent.length,0);run(1200);assert.deepEqual(sent,['今天有点累']);assert.equal(guest().status,'final');assert.equal(reply().text,'我想想啊...');
 engine.pending=new AbortController();run(700);assert.equal(thinkingPlays,1,'slow pending reply gets one feedback cue');assert.equal(engine.inbox.length,1,'feedback is not an answer');assert.equal(engine.memory,'');engine.pending=null;
 assert(engine.facts.some(e=>e.type==='player.message'&&e.text==='今天有点累'));
 // The microphone reopens during reasoning; a supplement aborts the old host turn.
 run(200);r=latest();const pending=new AbortController();engine.pending=pending;
 r.onresult(result('尤其是工作'));assert(pending.signal.aborted);assert.equal(guest().text,'今天有点累\n尤其是工作');assert.equal(reply().text,'正在听…');assert.equal(engine.inbox.length,1);
 r.onend();run(1200);assert.equal(sent.length,2);assert.equal(engine.inbox.length,2,'supplement preserved as a new confirmed input, without resending old input');assert.equal(guest().text,'今天有点累\n尤其是工作');assert.equal(ui.captions.rows.length,2);
 // Listening continues during host speech; played echo does not steal the floor.
 const speechId=say('那先坐一会儿，后半句还没播。','那先坐一会儿');assert.equal(reply().text,'那先坐一会儿');assert.equal(reply().id,speechId);assert.equal(ui.captions.rows.length,2);const before=devices.length;run(200);assert.equal(devices.length,before+1,'ASR reopens during host speech');
 r=latest();r.onresult(result('那先坐一会儿',.9,false));assert.equal(engine._active.id,speechId,'played host echo is filtered');
 r.onresult(result('其实',.9,false));await Promise.resolve();await Promise.resolve();assert.equal(engine._active,null);assert.equal(engine.inbox.length,2,'interruption preserves unanswered earlier input');assert(!engine.session.evidence().some(x=>x.text.includes('后半句')));
 assert.equal(ui.captions.rows.length,2);assert(guest(),'silent yield retains player text');await engine.yieldCallFloor();assert.equal(engine._active,null,'repeated input never adds an acknowledgement');
 r=latest();r.onresult(result('模糊的名字',.2));r.onend();run(1200);assert.equal(engine.getState().call.input,'reviewing');assert.equal(guest().status,'review');assert.equal(elements.get('chat-input').value,'模糊的名字');assert.equal(sent.length,2);
 elements.get('call-commit').onclick();assert.equal(sent.at(-1),'模糊的名字');assert.equal(ui.captions.rows.length,2,'placeholder counts toward two rows');
 button.onclick();button.onclick();r=latest();r.onresult(result('因为'));r.onend();run(1200);assert.equal(engine.getState().call.input,'listening');run(2200);assert.equal(engine.getState().call.input,'reviewing');
 elements.get('call-continue').onclick();r=latest();r.onresult(result('今天下雨'));r.onend();run(1200);assert.equal(sent.at(-1),'因为今天下雨');
 button.onclick();button.onclick();r=latest();const late=r.onresult,end=r.onend;button.onclick();const count=sent.length;late(result('迟到文字'));end();run(1200);run(200);assert.equal(sent.length,count);assert(!engine.getState().call.active);
 button.onclick();r=latest();r.onresult(result('我还在说这一句',.9,false));r.onend();run(1200);assert.equal(sent.length,count);assert.equal(guest().text,'我还在说这一句');assert.equal(guest().status,'review');elements.get('call-commit').onclick();assert.equal(sent.at(-1),'我还在说这一句');
 button.onclick();button.onclick();r=latest();r.onresult({results:[Object.assign([{transcript:'前半句',confidence:.9}],{isFinal:true}),Object.assign([{transcript:'还有后半句',confidence:.9}],{isFinal:false})]});r.onend();run(1200);assert.equal(guest().text,'前半句还有后半句');assert.equal(guest().status,'review');
 elements.get('chat-input').value='';elements.get('call-commit').onclick();assert.equal(sent.length,count+1,'cleared edit is not a stale submission');elements.get('call-discard').onclick();run(200);
 r=latest();r.onresult(result('这是完整的话'));r.onacoustic({available:true,noisy:true,voiceLikely:true,rms:.12});run(1200);assert(!r.stopped);run(700);run(0);assert(r.stopped);assert.equal(guest().status,'review');assert.equal(sent.length,count+1,'energy cannot force commit');
 elements.get('call-discard').onclick();run(200);r=latest();r.onresult(result('长'.repeat(361)));r.onend();run(1200);assert.equal(guest().text.length,361);elements.get('call-commit').onclick();assert.equal(sent.length,count+1);assert.equal(guest().text.length,361);
 elements.get('call-discard').onclick();run(200);r=latest();r.onresult(result('还在说'));run(30000);assert.equal(guest().status,'review');assert.equal(sent.length,count+1,'hard recording deadline never means consent to submit');
 elements.get('call-discard').onclick();run(200);r=latest();r.onerror({error:'not-allowed'});assert(!engine.getState().call.active);assert.match(toasts.at(-1),/权限/);
 button.onclick();engine.sendMessage('这句来自键盘');assert.equal(guest().text,'这句来自键盘');assert.equal(reply().text,'我想想啊...');engine.sendMessage('键盘补充');assert.equal(guest().text,'这句来自键盘\n键盘补充');assert.equal(reply().text,'我想想啊...');assert.equal(ui.captions.rows.length,2,'typed supplement cannot put guest after the thinking bubble');const id=say('完整回复','完整回复');complete(id);assert.equal(reply().status,'completed');run(200);assert(latest().started);
 const nextHost=say('补接一条弹幕','补接一条弹幕');complete(nextHost);assert(guest(),'consecutive host replies retain the latest player row');assert.equal(ui.captions.rows.length,2);
 // Protected streaming keeps one capture across turns and commits on the provider's semantic final.
 run(200);r=latest();r.persistent=true;r.onstart();const persistent=r,beforeProtected=sent.length;
 r.onresult({...result('同事的声音'),conversationId:'office',turnFinal:true,speakerVerified:false});assert.equal(sent.length,beforeProtected);
 const protectedSay=(text,id,final)=>r.onresult({...result(text,.9,final),conversationId:id,turnFinal:final,speakerVerified:true});
 protectedSay('现在我在说话','v1',false);assert.equal(guest().status,'draft');
 protectedSay('现在我在说话。','v1',true);assert.equal(sent.at(-1),'现在我在说话。');assert(!persistent.aborted,'one connection survives a completed turn');
 protectedSay('这是第二句。','v2',true);assert.equal(sent.at(-1),'这是第二句。');assert(!sent.at(-1).includes('现在我在说话'));
 engine.pending=new AbortController();run(700);assert.equal(thinkingPlays,1,'twenty-second cooldown suppresses repeated feedback');engine.pending=null;
 const protectedHost=say('好，我们可以停一下。','好，我们可以停一下。');
 protectedSay('停','v3',false);await Promise.resolve();await Promise.resolve();assert.equal(engine._active,null,'verified one-character interruption yields silently');
 button.onclick();assert(persistent.aborted,'hangup closes persistent capture');button.onclick();
 assert(!elements.has('call-status')&&!elements.has('call-flow')&&!elements.has('call-mode')&&!elements.has('call-talk'),'no status strips or second microphone control');
 document.hidden=true;document.dispatchEvent(new Event('visibilitychange'));assert(!engine.getState().call.active);document.hidden=false;
 run(700);assert.equal(thinkingPlays,1,'no delayed cue after hangup');assert(thinkingStops>0);
 console.log('PASS call bubbles: delayed placeholder, live drafts, 1.2s/2.2s settlement, review, supplements, played prefixes, voice interruption, one acknowledgement, two rows, echo guard, hangup, privacy and stale callbacks');
}finally{ui.destroy();engine.destroy();globalThis.setTimeout=originalSet;globalThis.clearTimeout=originalClear;delete globalThis.SpeechRecognition;delete globalThis.document;delete globalThis.window;}
