import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import {test} from 'node:test';
const source=readFileSync(process.env.Z_LIVE_WEB_SOURCE,'utf8').split('// BEGIN UNITY LIVE BRIDGE')[1].split('// END UNITY LIVE BRIDGE')[0];
const mount=vm.runInNewContext(source.slice(source.indexOf('function mountUnityLiveBridge'))+';mountUnityLiveBridge',{URLSearchParams,Map,JSON});
function setup(query='?embed=unity&bridge=1&session='+'a'.repeat(32)){
 const events=()=>({listeners:new Map(),addEventListener(k,f){this.listeners.set(k,f);},removeEventListener(k,f){if(this.listeners.get(k)===f)this.listeners.delete(k);},emit(k,data){this.listeners.get(k)?.(data);}});
 const sent=[],pauses=[],destroyed=[],muted=[];
 const transport={...events(),postMessage:json=>sent.push(JSON.parse(json))};
 const window={...events(),location:{search:query},vuplex:transport};
 const destroy=name=>({destroy:()=>destroyed.push(name)});
 const engine={...events(),...destroy('engine')};
 const options={window,engine,director:{...destroy('voice'),setEnabled:v=>muted.push(v)},talent:{},characterStage:destroy('character'),chatClient:{cancel:()=>destroyed.push('requests')},setHostPaused:v=>pauses.push(v),onClose:()=>destroyed.push('clock')};
 const bridge=mount(options);
 const send=(type,extra={})=>transport.emit('message',{data:JSON.stringify({channel:'project-z-live',version:1,sessionId:'a'.repeat(32),type,paused:false,muted:true,...extra})});
 return {bridge,window,engine,send,sent,pauses,destroyed,muted};
}
test('ordinary web visits remain unaffected',()=>{const c=setup('');assert.equal(c.bridge,null);assert.equal(c.pauses.length,0);});
test('embedded app waits for a matching Unity handshake',()=>{const c=setup();assert.deepEqual(c.pauses,[true]);assert.equal(c.sent[0].type,'ready');c.send('start',{sessionId:'b'.repeat(32)});c.send('visibility');assert.deepEqual(c.pauses,[true]);c.send('start');assert.deepEqual(c.pauses,[true,false]);assert.deepEqual(c.muted,[false,false]);});
test('background/foreground toggles host pause without destroying session',()=>{const c=setup();c.send('start');c.send('visibility',{paused:true});c.send('visibility',{paused:false});assert.deepEqual(c.pauses,[true,false,true,false]);assert.equal(c.destroyed.length,0);});
test('close cancels requests and tears down sound/model/timers exactly once',()=>{const c=setup();c.send('start');c.send('close');c.bridge.dispose();c.send('start');assert.deepEqual(c.destroyed,['requests','engine','voice','character','clock']);assert.equal(c.pauses.at(-1),true);assert.equal(c.sent.at(-1).type,'closed');});
test('escape while composing does not exit; normal Escape closes',()=>{const c=setup();c.window.emit('keydown',{key:'Escape',isComposing:true});assert.equal(c.destroyed.length,0);c.window.emit('keydown',{key:'Escape',preventDefault(){}});assert.equal(c.sent.at(-1).type,'closeRequested');assert.equal(c.destroyed.length,5);});
test('skill notifications are deduplicated and do not expose chat text',()=>{const c=setup();c.send('start');const detail={media:{kind:'singing',id:'song1',phase:'completed',text:'private'}};c.engine.emit('state',{detail});c.engine.emit('state',{detail});const events=c.sent.filter(x=>x.type==='skillState');assert.equal(events.length,1);assert.equal(events[0].text,undefined);assert.equal(events[0].phase,'completed');});
