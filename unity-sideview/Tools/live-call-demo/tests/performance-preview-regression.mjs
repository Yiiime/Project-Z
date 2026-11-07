import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {webcrypto} from 'node:crypto';
import {LIVE2D_CONFIG} from '../ui/live2d-config.js';
import {HARU_ACTION_LIBRARY} from '../ui/live2d-actions.js';
import {resolvePreviewIntent,generatePreviewLine} from '../ui/performance-intent.js';
class Element {
 constructor(){this.children=[];this.dataset={};this.handlers={};this.value='auto';this.textContent='';this.hidden=false;this.disabled=false;this.style={};this.selectedOptions=[{textContent:'自动'}];}
 append(...children){this.children.push(...children);}replaceChildren(...children){this.children=children;}setAttribute(){}addEventListener(n,fn){this.handlers[n]=fn;}scrollIntoView(){}focus(){}
}
const elements=new Map(),el=id=>{if(!elements.has(id))elements.set(id,new Element());return elements.get(id);};
const speeches=[];
class Stage{
 constructor(){this.status='ready';this.handlers={};this.paused=false;}
 addEventListener(n,fn){this.handlers[n]=fn;} async setMode(){this.handlers.change?.({detail:{status:'ready',message:'ready'}});}
 setSpeech(s){speeches.push(s);}setPerformance(){}setActivity(){}setPaused(p){this.paused=p;}getPerformanceState(){return {};}resize(){}
}
let providerCalls=0;const pending=[];
const context={Live2DStage:Stage,LIVE2D_CONFIG,HARU_ACTION_LIBRARY,resolvePreviewIntent,generatePreviewLine,structuredClone,performance:{now:()=>0},crypto:webcrypto,AbortController,setTimeout,clearTimeout,clearInterval,setInterval:()=>0,requestAnimationFrame:()=>0,cancelAnimationFrame:()=>{},document:{getElementById:el,createElement:()=>new Element()},window:{addEventListener(){}},fetch:async(url,options)=>{
 if(url.endsWith('/config'))return {ok:true,json:async()=>({enabled:true,model:'fixture-model'})};
 providerCalls++;return new Promise(resolve=>pending.push({resolve,options}));
}};
const html=fs.readFileSync(new URL('../ui/live2d-preview.html',import.meta.url),'utf8');
const script=html.split('<script type="module">')[1].split('</script>')[0].replace(/^import .*;\r?\n/gm,'');
await vm.runInNewContext('(async()=>{'+script+'})()',context);await Promise.resolve();
const sentence='谢谢你的夸奖，我有点不好意思。';
const plan={source:'deepseek',model:'fixture-model',text:sentence,segments:[{text:sentence,mood:'shy',gesture:'coverMouth',transition:'smooth',fit:'supported',reason:'害羞回应',missingAction:''}]};
el('custom-text').value=sentence;
const first=el('analyze-line').onclick();assert.equal(providerCalls,1);assert.equal(el('analyze-line').disabled,true);
el('custom-text').value='另一句';el('custom-text').handlers.input();assert.equal(pending[0].options.signal.aborted,true);
pending[0].resolve({ok:true,json:async()=>plan});await first;assert(!speeches.some(s=>s.gesture==='coverMouth'),'An obsolete result cannot take over the current preview');
el('custom-text').value=sentence;const second=el('analyze-line').onclick();pending[1].resolve({ok:true,json:async()=>plan});await second;
assert.equal(el('ai-report').hidden,false);assert(speeches.some(s=>s.gesture==='coverMouth'));assert.equal(providerCalls,2);
el('replay-plan').onclick();el('ai-segments').children[0].onclick();assert.equal(providerCalls,2,'Replay and segment audition must not request DeepSeek again');
const third=el('analyze-line').onclick();el('generate-custom').onclick();assert.equal(pending[2].options.signal.aborted,true);pending[2].resolve({ok:true,json:async()=>plan});await third;
const before=speeches.length;const fourth=el('analyze-line').onclick();pending[3].resolve({ok:false,json:async()=>({message:'服务暂时不可用'})});await fourth;
assert.equal(speeches.length,before+1,'Failure only stops the old performance, never starts a rule-based fallback');assert(el('ai-status').textContent.includes('未自动改用本地规则'));
console.log('PASS late analysis cancellation, successful playback, zero-request replay, random-input cancellation and explicit failure without local fallback');
