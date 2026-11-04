import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createLiveServer} from '../server/http.mjs';
import {ArtifactStore,Providers} from '../server/providers.mjs';
import {SingingService} from '../server/singing/service.mjs';
import {LiveSongLibrary} from '../server/song-library/index.mjs';
const root=fileURLToPath(new URL('../',import.meta.url));
const plan={intent:'request',title:'晚安',artist:'颜人中',version:'',question:'',segment:{kind:'chorus',ordinal:1,description:'第一段完整副歌',lyricStart:''}};
async function fixture(t,{transcribe}={}) {
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'z-native-http-'));
 const singing=await SingingService.open({root,directory:path.join(directory,'singing'),autoStart:false,environment:()=>({}),parse:async()=>plan});
 const library=new LiveSongLibrary({root,directory:path.join(directory,'library'),singing});
 const providers=new Providers({VOLC_ASR_API_KEY:'fake-for-injected-asr'},{singing,singingContext:()=>singing.context('local-owner')});
 const {server,sessions}=await createLiveServer({root:path.join(directory,'sessions'),providers,artifacts:new ArtifactStore(path.join(directory,'artifacts')),songRoot:path.join(root,'server/assets/songs'),token:'fixture-token',library,transcribe});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));
 t.after(async()=>{server.closeAllConnections();await new Promise(r=>server.close(r));await singing.close();library.close();fs.rmSync(directory,{recursive:true,force:true});});
 const url=`http://127.0.0.1:${server.address().port}`;
 const request=(route,body,key=crypto.randomUUID())=>fetch(url+route,{method:body?'POST':'GET',headers:{Authorization:'Bearer fixture-token',...(body?{'Content-Type':'application/json','Idempotency-Key':key}:{})},...(body?{body:JSON.stringify(body)}:{})});
 const response=await request('/v1/live/sessions',{protocolVersion:1,mode:'private',capabilities:{audioEnabled:true}}),state=await response.json(),s=sessions.get(state.sessionId);clearInterval(s.engine._timer);
 const envelope=extra=>({protocolVersion:1,sessionId:s.id,epoch:s.epoch,...extra});
 s.control(envelope({action:'resume'}));return {request,url,s,singing,library,envelope};
}
test('native gateway authenticates, deduplicates song requests and previews without marking playback',async t=>{
 const f=await fixture(t);
 assert.equal((await fetch(f.url+'/api/singing/state')).status,401);
 const body={text:'唱晚安第一段副歌',sourceId:'request-one',sessionId:f.s.id},key=crypto.randomUUID();
 const first=await(await f.request('/api/singing/tasks',body,key)).json(),second=await(await f.request('/api/singing/tasks',body,key)).json();assert.equal(first.id,second.id);
 assert.equal((await f.request('/api/singing/tasks',{...body,text:'换首歌'},key)).status,409);
 await f.singing.tick();const task=f.singing.context('local-owner').tasks[0];assert.equal(task.status,'ready');
 const meta=await(await f.request(`/v1/live/sessions/${f.s.id}/song-preview/${task.assetId}`)).json();
 const audio=await f.request(`/v1/live/sessions/${f.s.id}/artifacts/${meta.artifactId}`);assert.equal(audio.status,200);
 assert.equal(f.singing.read().playbacks.length,0);assert.equal(f.singing.context('local-owner').tasks[0].status,'ready');
 const found=await(await f.request('/api/song-library/lookup',{title:'晚安',artist:'颜人中'})).json();assert.equal(found.states[0].status,'segment_ready');
 const verse=await f.library.lookup({title:'晚安',segment:{kind:'verse',ordinal:1}});assert.equal(verse.states[0].status,'song_partially_mastered');
 assert.equal(verse.states[0].material.status==='ready',false);
});
test('ASR admits one current listening turn and discards its result after hangup',async t=>{
 let started,finish;const began=new Promise(r=>started=r);
 const f=await fixture(t,{transcribe:async(bytes,{signal})=>{assert.equal(bytes.length,3200);started();return new Promise(resolve=>{finish=()=>resolve({text:'真实最终文本'});signal.addEventListener('abort',finish,{once:true});});}});
 f.s.engine.enabled=true;f.s.input(f.envelope({type:'call.join',payload:{}}));f.s.input(f.envelope({type:'call.input',payload:{state:'listening'}}));
 const send=version=>fetch(f.url+`/v1/live/sessions/${f.s.id}/asr`,{method:'POST',headers:{Authorization:'Bearer fixture-token','Content-Type':'audio/pcm','X-Live-Call-Version':String(version)},body:Buffer.alloc(3200)});
 assert.equal((await send(-1)).status,409);const pending=send(f.s.engine.callVersion);await began;
 assert.equal((await send(f.s.engine.callVersion)).status,409);
 f.s.input(f.envelope({type:'call.end',payload:{}}));finish();assert.equal((await pending).status,409);assert.equal(f.s.engine.inbox.length,0);
});
test('a reviewed song completes only through native playback receipts',async t=>{
 const f=await fixture(t),s=f.s;
 const task=f.singing.submit({text:'晚安的副歌',sourceId:'source',sessionId:s.id,owner:'local-owner'});await f.singing.tick();
 assert(await s.engine._executeSinging({name:'play_prepared_song',args:{taskId:task.id}}));
 await new Promise(r=>setTimeout(r,20));const latest=type=>[...s.commands.values()].filter(c=>c.type===type).at(-1);
 const receipt=(c,status,extra={})=>s.receipt(f.envelope({executorLeaseId:s.lease,commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{artifactId:c.payload.artifactId,playbackId:c.payload.playbackId,status,...extra}}));
 receipt(latest('media.prepare'),'ready',{durationMs:32284});assert(s.talent.play());
 receipt(latest('media.play'),'started',{positionMs:0,durationMs:32284});
 receipt(latest('media.play'),'completed',{positionMs:32284});await s.engine.singingReceipts;
 assert.equal(f.singing.context('local-owner').tasks[0].status,'completed');
});
test('final ASR text waits for client confirmation and ended sessions do not exhaust the entry limit',async t=>{
 const f=await fixture(t,{transcribe:async()=>({text:'确认后再发送'})}),s=f.s;
 s.engine.enabled=true;s.input(f.envelope({type:'call.join',payload:{}}));s.input(f.envelope({type:'call.input',payload:{state:'listening'}}));
 const response=await fetch(f.url+`/v1/live/sessions/${s.id}/asr`,{method:'POST',headers:{Authorization:'Bearer fixture-token','Content-Type':'audio/pcm','X-Live-Call-Version':String(s.engine.callVersion)},body:Buffer.alloc(3200)});
 assert.equal(response.status,200);assert.equal((await response.json()).text,'确认后再发送');assert.equal(s.engine.inbox.length,0);
 s.input(f.envelope({type:'viewer.message',payload:{text:'确认后再发送'}}));assert.equal(s.engine.inbox.at(-1).text,'确认后再发送');
 s.control(f.envelope({action:'end'}));
 for(let i=0;i<21;i++){
  const created=await f.request('/v1/live/sessions',{protocolVersion:1,mode:'private',capabilities:{audioEnabled:false}});assert.equal(created.status,201);
  const state=await created.json();assert.equal((await f.request(`/v1/live/sessions/${state.sessionId}/control`,{protocolVersion:1,sessionId:state.sessionId,epoch:state.epoch,action:'end'})).status,202);
 }
});
