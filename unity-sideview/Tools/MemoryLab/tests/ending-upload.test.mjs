import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createServer } from '../server.mjs';
import { authoredFile } from '../lib/ending.mjs';

async function fixture(t, hook) {
  const requests=[];
  const provider={publicConfig:()=>({configured:true}),config:{topK:6},request:m=>m,
    call:async messages=>{
      const input=JSON.parse(messages[1].content);requests.push(input);if(hook)await hook(input);
      if(input.blocks)return {text:JSON.stringify({title:'这份经历',playerName:'旅人',context:'第七晚，在码头回望。',lastMessage:'',
        world:{departureReady:false,departureDetail:'',stayDetail:'未来没有保证',leaveOutcome:'pending',leaveAftermath:'',stayOutcome:'uncertain',stayAftermath:''},
        blocks:input.blocks.map((b,i)=>({id:b.id,title:'经历'+(i+1),day:i+1,kind:'memory',zKnows:!b.text.includes('SECRET')}))})};
      if(input.dailyPage)return {text:JSON.stringify({dailyPages:[{day:input.day,title:'第'+input.day+'天',text:'这一天发生了一件具体的事。\n我把当时看到的细节记在这里。\n\n我还在慢慢想。'}]})};
      if(input.choice)return {text:JSON.stringify({recollections:input.memories.map(m=>({memoryId:m.id,text:'我记得：'+m.text}))})};
      const changed=input.memories.some(m=>m.text.includes('NEW_FILE'));
      return {text:JSON.stringify({choice:changed?'stay':'leave',title:'带着这些日子',opening:'让我回想一下。',reason:'这是我自己的选择。',reasonMemoryIds:[],decision:changed?'我想留下。':'我想离开。',lastLine:'这一步，我自己来。'})};
    }};
  const app=createServer({dbPath:':memory:',providerFactory:()=>provider,backgroundEnabled:false,endingScenarioPath:authoredFile});
  app.server.listen(0,'127.0.0.1');await once(app.server,'listening');
  const address=`http://127.0.0.1:${app.server.address().port}`;
  t.after(async()=>{app.server.closeAllConnections();await new Promise(r=>app.server.close(r));app.store.close();});
  const base={worldId:'upload_world',runId:'run_one',day:1,period:0};
  async function game(path,body=base){const r=await fetch(address+'/api/game/'+path,{method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify(body)});return {status:r.status,data:await r.json()};}
  await game('open');
  async function open(){const r=await game('ending-upload/open');assert.equal(r.status,200);return new URL(r.data.endingEditorUrl).hash.slice(1);}
  async function upload(token,path,body){const r=await fetch(address+'/api/ending-upload/'+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json','X-Ending-Upload':token},...(body?{body:JSON.stringify(body)}:{})});return {status:r.status,data:await r.json()};}
  const commit=(token,draft)=>upload(token,'use',{draftId:draft.data.draftId,expectedRevision:draft.data.expectedRevision});
  const job=()=>app.endingUploads.jobs.get(base.worldId+':'+base.runId);
  return {app,requests,base,game,open,upload,commit,job,address};
}

test('upload parses intact source, uses this save only, and automatically rebuilds from uploaded and actual shared memories',async t=>{
  const f=await fixture(t);const token=await f.open();
  await f.game('sync',{...f.base,eventData:{id:'real',seq:1,day:1,period:0,kind:'utterance',speakerId:'player',recipientId:'z',conversationId:'ours',channel:'private_player_chat',text:'ACTUAL_GAME_MEMORY'}});
  const before=f.app.engine.world(f.base.worldId);
  const raw='NEW_FILE：Z与玩家看过烟花。\n\nSECRET：作者没有告诉Z的幕后事情。';
  const draft=await f.upload(token,'import',{name:'七晚.md',text:raw});
  assert.equal(draft.status,200,draft.data.error);assert.equal(draft.data.scenario.sourceText,raw);
  assert.equal(draft.data.scenario.memories[1].zKnows,false);
  assert.equal(f.app.engine.world(f.base.worldId).endingMemorySource,undefined,'unconfirmed draft must not replace the source');
  const used=await f.commit(token,draft);assert.equal(used.status,200,used.data.error);
  await f.job().promise;
  const result=await f.upload(token,'status');assert.equal(result.data.source.phase,'complete');
  assert.equal(result.data.source.ending.decision.choice,'stay');
  const generated=f.requests.filter(r=>!r.blocks);
  assert.ok(generated.some(r=>JSON.stringify(r).includes('ACTUAL_GAME_MEMORY')));
  assert.ok(generated.every(r=>!JSON.stringify(r).includes('SECRET')));
  const after=f.app.engine.world(f.base.worldId);
  assert.equal(after.seq,before.seq);assert.deepEqual(after.actors,before.actors,'source upload is not a world action or relationship grant');
  assert.equal(after.endingMemorySource.originalText,raw);
  const other={...f.base,worldId:'other_world',runId:'other_run'};await f.game('open',other);
  assert.equal(f.app.engine.world(other.worldId).endingMemorySource,undefined);
});

test('reupload replaces the prior file, retains real experiences and old ending snapshots, and survives a checkpoint reload',async t=>{
  const f=await fixture(t);const token=await f.open();
  let draft=await f.upload(token,'import',{name:'first.txt',text:'OLD_FILE：前一次文件。'});await f.commit(token,draft);await f.job().promise;
  const first=(await f.upload(token,'status')).data.source.ending;
  draft=await f.upload(token,'import',{name:'second.txt',text:'NEW_FILE：重新上传的经历。'});await f.commit(token,draft);await f.job().promise;
  const next=(await f.game('ending-source')).data;
  assert.equal(next.ending.decision.choice,'stay');assert.notEqual(next.ending.id,first.id);
  assert.equal(next.endingPrevious.id,first.id);
  assert.ok(!JSON.stringify(next.ending).includes('OLD_FILE'));
  const restored=await f.game('open',{...f.base,runId:'restored',checkpoint:next.checkpoint});assert.equal(restored.status,200,restored.data.error);
  const source=await f.game('ending-source',{...f.base,runId:'restored'});
  assert.equal(source.data.endingSource.sourceName,'second.txt');assert.equal(source.data.ending.id,next.ending.id);
  const invalid=await f.upload(token,'status');assert.equal(invalid.status,400,'old upload capability cannot follow another run');
});

test('two upload windows cannot silently overwrite one another after parsing',async t=>{
  const f=await fixture(t),a=await f.open(),b=await f.open();
  const first=await f.upload(a,'import',{name:'a.txt',text:'NEW_FILE：甲。'});
  const stale=await f.upload(b,'import',{name:'b.txt',text:'OLD_FILE：乙。'});
  await f.commit(a,first);await f.job().promise;
  const conflict=await f.commit(b,stale);assert.equal(conflict.status,400);
  assert.equal(f.app.engine.world(f.base.worldId).endingMemorySource.fileName,'a.txt');
});

test('a superseded generation cannot publish an ending for the new source',async t=>{
  let entered,release;const ready=new Promise(r=>entered=r),wait=new Promise(r=>release=r);
  const f=await fixture(t,async input=>{if(input.memories&&!input.choice&&input.memories.some(m=>m.text.includes('PAUSE_OLD'))){entered();await wait;}});
  const token=await f.open();let d=await f.upload(token,'import',{name:'old.txt',text:'PAUSE_OLD：旧文件。'});await f.commit(token,d);const old=f.job();await ready;
  d=await f.upload(token,'import',{name:'new.txt',text:'NEW_FILE：新的最后一晚。'});await f.commit(token,d);await f.job().promise;release();await old.promise;
  assert.equal(old.status,'failed');
  const source=(await f.upload(token,'status')).data.source;
  assert.equal(source.sourceName,'new.txt');assert.equal(source.phase,'complete');assert.equal(source.ending.decision.choice,'stay');
  assert.ok(!f.app.engine.world(f.base.worldId).seventhNightEndings[old.id]);
});

test('new actual experiences change the ending context, and reset-without-checkpoint clears the uploaded source',async t=>{
  const f=await fixture(t),token=await f.open();const d=await f.upload(token,'import',{name:'known.txt',text:'NEW_FILE：已知经历。'});await f.commit(token,d);await f.job().promise;
  const before=(await f.game('ending-source')).data.endingSource.contextRevision;
  await f.game('sync',{...f.base,eventData:{id:'later',seq:1,day:1,period:0,kind:'utterance',speakerId:'player',recipientId:'z',conversationId:'later',channel:'private_player_chat',text:'LATER_GAME_MEMORY'}});
  const changed=(await f.game('ending-source')).data;assert.notEqual(changed.endingSource.contextRevision,before);assert.equal(changed.ending,undefined);
  const regenerated=await f.game('ending',{...f.base,requestId:'later-ending',details:{mode:'model'}});
  assert.ok(JSON.stringify(regenerated.data.ending).includes('LATER_GAME_MEMORY'));
  await f.game('open',{...f.base,runId:'fresh'});
  assert.equal(f.app.engine.world(f.base.worldId).endingMemorySource,undefined);
});

test('uploaded files can be exported and reimported without duplicating live game memory IDs',async t=>{
  const f=await fixture(t),token=await f.open();const d=await f.upload(token,'import',{name:'original.txt',text:'NEW_FILE：一段记忆。'});await f.commit(token,d);await f.job().promise;
  const file=(await f.upload(token,'export')).data.scenario;
  const imported=await f.upload(token,'import',{name:'backup.json',text:JSON.stringify(file)});assert.equal(imported.status,200,imported.data.error);
  await f.commit(token,imported);await f.job().promise;
  assert.equal((await f.upload(token,'status')).data.source.ending.memoryCount,1);
});

test('unauthorized, malformed and oversize uploads fail without changing the save',async t=>{
  const f=await fixture(t),token=await f.open();
  assert.equal((await f.upload('wrong','status')).status,400);
  assert.equal((await f.upload(token,'import',{name:'bad.exe',text:'x'})).status,400);
  assert.equal((await f.upload(token,'import',{name:'bad.json',text:'broken'})).status,400);
  assert.equal((await f.upload(token,'import',{name:'big.txt',text:'字'.repeat(70000)})).status,400);
  assert.equal(f.app.engine.world(f.base.worldId).endingMemorySource,undefined);
});

test('structured uploads above the old forty-memory limit retain every memory across generation batches',async t=>{
  const f=await fixture(t),token=await f.open();
  const scenario=f.app.endings.authored(f.app.engine.world(f.base.worldId));
  scenario.memories=Array.from({length:80},(_,i)=>({id:'uploaded_'+i,day:i%7+1,kind:'memory',zKnows:true,title:'经历'+i,text:'NEW_FILE：这是第'+i+'段完整经历。'}));
  const draft=await f.upload(token,'import',{name:'all-memories.json',text:JSON.stringify(scenario)});
  assert.equal(draft.status,200,draft.data.error);await f.commit(token,draft);await f.job().promise;
  const ending=(await f.upload(token,'status')).data.source.ending;
  assert.equal(ending.memoryCount,80);assert.equal(new Set(ending.recollections.map(m=>m.memoryId)).size,80);
  assert.deepEqual(new Set(ending.recollections.map(m=>m.memoryId)),new Set(scenario.memories.map(m=>m.id)));
});
