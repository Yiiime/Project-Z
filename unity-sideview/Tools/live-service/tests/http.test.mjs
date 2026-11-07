import {test} from 'node:test';
import {parseAgentReply,normalizeAgentInput} from '../server/shared/worker/agent-director.js';
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
async function fixture(t,{transcribe,tokens}={}) {
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'z-native-http-'));
 const singing=await SingingService.open({root,directory:path.join(directory,'singing'),autoStart:false,sources:[],environment:()=>({}),parse:async()=>plan});
 const library=new LiveSongLibrary({root,directory:path.join(directory,'library'),singing});
 const providers=new Providers({VOLC_ASR_API_KEY:'fake-for-injected-asr'},{singing,singingContext:()=>singing.context('local-owner')});
 const {server,sessions}=await createLiveServer({root:path.join(directory,'sessions'),providers,artifacts:new ArtifactStore(path.join(directory,'artifacts')),songRoot:path.join(root,'server/assets/songs'),token:'fixture-token',tokens,library,transcribe});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));
 t.after(async()=>{server.closeAllConnections();await new Promise(r=>server.close(r));await singing.close();library.close();fs.rmSync(directory,{recursive:true,force:true});});
 const url=`http://127.0.0.1:${server.address().port}`;
 const request=(route,body,key=crypto.randomUUID(),token='fixture-token')=>fetch(url+route,{method:body?'POST':'GET',headers:{Authorization:'Bearer '+token,...(body?{'Content-Type':'application/json','Idempotency-Key':key}:{})},...(body?{body:JSON.stringify(body)}:{})});
 const response=await request('/v1/live/sessions',{protocolVersion:1,mode:'private',capabilities:{audioEnabled:true}}),state=await response.json(),s=sessions.get(state.sessionId);clearInterval(s.engine._timer);
 const envelope=extra=>({protocolVersion:1,sessionId:s.id,epoch:s.epoch,...extra});
 s.control(envelope({action:'resume'}));return {request,url,s,singing,library,envelope};
}
test('native gateway authenticates, deduplicates requests and removes review and preview routes',async t=>{
 const f=await fixture(t);
 assert.equal((await fetch(f.url+'/api/singing/state')).status,401);
 const body={text:'唱晚安第一段副歌',sourceId:'request-one',sessionId:f.s.id},key=crypto.randomUUID();
 const first=await(await f.request('/api/singing/tasks',body,key)).json(),second=await(await f.request('/api/singing/tasks',body,key)).json();assert.equal(first.id,second.id);
 assert.equal((await f.request('/api/singing/tasks',{...body,text:'换首歌'},key)).status,409);
 await f.singing.tick();const task=f.singing.context('local-owner').tasks[0];assert.equal(task.status,'ready');
 const start=await f.request(`/v1/live/sessions/${f.s.id}/inputs`,f.envelope({type:'song.preview',payload:{assetId:task.assetId}}));assert.equal(start.status,409);
 assert.equal((await f.request(`/api/singing/tasks/${task.id}/review`,{approved:true})).status,404);
 assert.equal((await f.request('/api/singing/playbacks/fake/receipt',{phase:'completed'})).status,404);
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
test('chat-driven song uses existing media lifecycle and returns to chat without singing receipts',async t=>{
 const f=await fixture(t),s=f.s;
 const task=f.singing.submit({text:'晚安的副歌',sourceId:'source',sessionId:s.id,owner:'local-owner'});await f.singing.tick();
 assert(await s.engine._executeSinging({name:'play_prepared_song',args:{taskId:task.id}}));
 await new Promise(r=>setTimeout(r,20));const latest=type=>[...s.commands.values()].filter(c=>c.type===type).at(-1);
 const receipt=(c,status,extra={})=>s.receipt(f.envelope({executorLeaseId:s.lease,commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{artifactId:c.payload.artifactId,playbackId:c.payload.playbackId,status,...extra}}));
 receipt(latest('media.prepare'),'ready',{durationMs:32284});assert(s.talent.play());
 receipt(latest('media.play'),'started',{positionMs:0,durationMs:32284});
 receipt(latest('media.play'),'completed',{positionMs:32284});
 assert.equal(f.singing.context('local-owner').tasks[0].status,'scheduled');assert.equal(f.singing.read().playbacks.length,0);
 assert(s.talent.finish());s.engine._say('继续聊天','warm','none',{source:'deepseek'});await new Promise(r=>setImmediate(r));assert.equal(latest('host.present').payload.text,'继续聊天');
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


test('dictation returns a draft without joining or publishing and rejects a late result after joining',async t=>{
 let finish,started;let delayed=false;const began=new Promise(r=>started=r);
 const f=await fixture(t,{transcribe:async(bytes,{signal})=>{
  if(!delayed)return {text:'语音输入草稿'};
  started();return new Promise(resolve=>{finish=()=>resolve({text:'迟到的草稿'});signal.addEventListener('abort',finish,{once:true});});
 }}),s=f.s;
 const send=()=>fetch(f.url+`/v1/live/sessions/${s.id}/dictation`,{method:'POST',headers:{Authorization:'Bearer fixture-token','Content-Type':'audio/pcm','X-Live-Call-Version':String(s.engine.callVersion)},body:Buffer.alloc(3200)});
 const response=await send();assert.equal(response.status,200);assert.equal((await response.json()).text,'语音输入草稿');
 assert.equal(s.snapshot().call.active,false);assert.equal(s.engine.inbox.length,0);assert(!s.snapshot().room.messages.some(m=>m.text==='语音输入草稿'));
 s.engine.enabled=true;s.input(f.envelope({type:'viewer.message',payload:{text:'语音输入草稿'}}));assert(s.snapshot().room.messages.some(m=>m.text==='语音输入草稿'));
 delayed=true;const pending=send();await began;s.input(f.envelope({type:'call.join',payload:{}}));finish();
 assert.equal((await pending).status,409);assert.equal((await send()).status,409);assert(!s.engine.inbox.some(m=>m.text==='迟到的草稿'));
});

test('office identities isolate sessions, voice, event streams and singing operations',async t=>{
 const f=await fixture(t,{tokens:{alice:'fixture-token',bob:'bob-token'}});
 const other=(route,body,key='same-key-123')=>f.request(route,body,key,'bob-token');
 for(const suffix of ['', '/events','/artifacts/'+ 'a'.repeat(64)])assert.equal((await other('/v1/live/sessions/'+f.s.id+suffix)).status,404);
 assert.equal((await other('/v1/live/sessions/'+f.s.id+'/control',f.envelope({action:'end'}))).status,404);
 assert.equal((await fetch(f.url+'/v1/live/sessions/'+f.s.id+'/asr',{method:'POST',headers:{Authorization:'Bearer bob-token','Content-Type':'audio/pcm'},body:Buffer.alloc(32)})).status,404);
 const body={protocolVersion:1,mode:'private',capabilities:{}};
 const a=await(await f.request('/v1/live/sessions',body,'same-create-key')).json();
 const b=await(await other('/v1/live/sessions',body,'same-create-key')).json();assert.notEqual(a.sessionId,b.sessionId);
 const song={text:'唱晚安',sourceId:'same-source',sessionId:f.s.id};
 const ta=await(await f.request('/api/singing/tasks',song,'same-task-key')).json();
 const tb=await(await other('/api/singing/tasks',song,'same-task-key')).json();assert.notEqual(ta.id,tb.id);
 const state=await(await other('/api/singing/state')).json();assert.deepEqual(state.tasks.map(x=>x.id),[tb.id]);
 assert.equal((await other('/api/singing/tasks/'+ta.id+'/cancel',{})).status,409);
 assert.equal((await f.request('/health',null,undefined,'wrong-token')).status,401);
 assert.equal(f.s.owner,'alice');assert.equal(f.s.providers.options.owner,'alice');
 assert.equal((await f.s.providers.singingFetch('/api/singing/state').then(r=>r.json())).tasks[0].id,ta.id);
 const raced=await Promise.all(Array.from({length:4},(_,i)=>other('/v1/live/sessions',body,'race-key-'+i)));
 assert.equal(raced.filter(r=>r.status===201).length,1);assert.equal(raced.filter(r=>r.status===429).length,3);
});

async function hostSongTurn(f,{emptyTool=false,deny=false}={}) {
 const s=f.s,e=s.engine;
 const task=f.singing.submit({text:'晚安的副歌',sourceId:'original-order',sessionId:s.id,owner:'local-owner'});await f.singing.tick();
 e.enabled=true;e.nextWake=e._clock;
 s.input(f.envelope({type:'viewer.message',payload:{text:'你知道花店晴大姐吗'}}));
 s.input(f.envelope({type:'viewer.message',payload:{text:'你快唱啊'}}));
 e.client.generate=async p=>{
  if(p.kind==='review'){
   const checked=normalizeAgentInput(p);assert.equal(checked.context.candidate.plannedTool?.name,emptyTool?undefined:'play_prepared_song');
   return {decision:deny?'reject':'allow',reason:'fixture',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:e.audience.dialogue.version||'handoff-test'};
  }
  assert.equal(p.kind,'host');
  return parseAgentReply({action:emptyTool?'speak':'tool',speech:'这就唱，晴大姐的事留到唱完聊。',mood:'warm',gesture:'none',handledIds:p.context.host.inbox.filter(m=>m.mine).map(m=>m.id),wakeAfterMs:2000,memory:'唱完接回晴大姐的话题',...(emptyTool?{}:{tool:{name:'play_prepared_song',args:{taskId:task.id}}})},{...p,context:{...p.context,singing:f.singing.context('local-owner')}});
 };
 await e._think();await new Promise(r=>setImmediate(r));return task;
}
const lastCommand=(s,type)=>[...s.commands.values()].filter(c=>c.type===type).at(-1);
function acknowledge(f,c,status,extra={}){return f.s.receipt(f.envelope({executorLeaseId:f.s.lease,commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{...Object.fromEntries(['speechId','modality','artifactId','playbackId'].filter(k=>c.payload[k]!==undefined).map(k=>[k,c.payload[k]])),status,...extra}}));}
async function finishIntro(f){const c=lastCommand(f.s,'host.present');acknowledge(f,c,'started',{durationMs:3000,positionMs:0});acknowledge(f,c,'completed',{positionMs:3000});await new Promise(r=>setTimeout(r,30));}

test('ready song follows the reviewed intro despite chat arriving during speech or dispatch',async t=>{
 const f=await fixture(t),s=f.s,e=s.engine,task=await hostSongTurn(f);
 assert(lastCommand(s,'host.present'));assert(!lastCommand(s,'media.prepare'));
 s.input(f.envelope({type:'viewer.message',payload:{text:'唱完再聊也行'}}));
 const fetch=e.client.fetch;let release,entered;
 const started=new Promise(r=>entered=r),gate=new Promise(r=>release=r);
 e.client.fetch=async(route,options)=>{if(route.endsWith('/play')){entered();await gate;}return fetch(route,options);};
 const finishing=finishIntro(f);await started;
 s.input(f.envelope({type:'viewer.message',payload:{text:'我等着听'}}));release();await finishing;
 assert.equal(e._state.agent.errorCode,null);
 assert.equal(f.singing.context('local-owner').tasks.find(x=>x.id===task.id).status,'scheduled');
 const prepare=lastCommand(s,'media.prepare');assert(prepare);
 acknowledge(f,prepare,'ready',{durationMs:32285});e.enabled=false;e._tick();
 const play=lastCommand(s,'media.play');assert(play);assert.equal([...s.commands.values()].filter(c=>c.type==='media.play').length,1);
 acknowledge(f,play,'started',{durationMs:32285,positionMs:0});acknowledge(f,play,'completed',{positionMs:32285});
 assert.equal(s.talent.snapshot().phase,'completed');assert(e.inbox.some(m=>m.text==='我等着听'));
});

test('pausing the committed introduction still revokes its pending playback',async t=>{
 const f=await fixture(t);await hostSongTurn(f);f.s.engine.setPaused(true);f.s.engine.setPaused(false);
 await finishIntro(f);assert(!lastCommand(f.s,'media.prepare'));assert.equal(f.singing.context('local-owner').tasks[0].status,'ready');
});

test('review rejection of a speech-only opening publishes neither promise nor playback',async t=>{
 const f=await fixture(t);await hostSongTurn(f,{emptyTool:true,deny:true});
 assert(!lastCommand(f.s,'host.present'));assert(!lastCommand(f.s,'media.prepare'));assert.equal(f.singing.context('local-owner').tasks[0].status,'ready');
});
