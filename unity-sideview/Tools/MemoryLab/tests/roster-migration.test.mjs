import test from 'node:test';
import assert from 'node:assert/strict';
import { Store } from '../lib/store.mjs';
import { GameEngine, BOOK } from '../lib/game-engine.mjs';
import { PERSONA_REVISION, ACTOR_IDS, retiredPlot, upgradeSnapshot } from '../lib/roster-migration.mjs';
import { inspectMemories } from '../lib/memory-inspector.mjs';
import { FixtureProvider } from './fixtures.mjs';

function setup(t,day=1) {
  const store=new Store(),provider=new FixtureProvider(),engine=new GameEngine(store,provider);
  t.after(()=>store.close());const input={worldId:'migration_world',runId:'first_run',day,period:0};
  return {store,provider,engine,input,snapshot:JSON.parse(engine.open(input).checkpoint)};
}
test('all ten ordinary islanders have distinct profiles, no retired setup, and independently generate dialogue',async t=>{
  const {engine,provider,input}=setup(t);
  assert.deepEqual(BOOK.actors.map(a=>a.id),ACTOR_IDS);
  for(const a of BOOK.actors) {
    const context=engine.context(engine.assert(input),a.id,{scene:'Street'});
    assert.equal(retiredPlot(JSON.stringify(context)),false);
    assert.ok(!JSON.stringify(context).includes('AI'));
    assert.equal(context.self.name,a.name);
    if(['z_story','npc_aya','npc_chen'].includes(a.id)) {
      const response=await engine.utterance({...input,requestId:'request_'+a.id,actorId:a.id,recipientId:'player',conversationId:'chat_'+a.id,scene:'Street'});
      assert.equal(response.actorId,a.id);assert.ok(response.text.includes(a.name));
    }
  }
  assert.equal(provider.calls.filter(c=>c.messages[0].content.includes('你已经与对方走到交谈距离')).length,3);
});
test('old seven-person save retires plot evidence and derived reflections, preserves ordinary memories and gives Day4 to new actors once',async t=>{
  const {engine,store,input}=setup(t,4);
  const reply=await engine.sync({...input,eventData:{id:'migration_world_day4',seq:1,kind:'day4',day:4,period:0}});
  const old=JSON.parse(reply.checkpoint);delete old.personaRevision;
  old.memories=old.memories.filter(m=>m.actorId.startsWith('c'));
  old.messages.forEach(m=>m.participants=m.participants.filter(id=>id.startsWith('c')));
  const m=old.memories[0],message=old.messages[0];
  const secret={...message,id:'old_staff_message',worldKind:'staff_day4',content:'公司要求工作人员配合撤场。'};
  const ordinary={...message,id:'ordinary_message',worldKind:undefined,role:'user',speakerId:'player',participants:['c01','player'],content:'下午一起喝咖啡。'};
  old.messages.push(secret,ordinary);
  old.memories.push({...m,id:'staff_memory',origin:'staff_day4',content:secret.content,evidence:[{messageId:secret.id,quote:secret.content}]},
    {...m,id:'old_identity',origin:'dialogue',kind:'hearsay',content:'Z是AI，其他岛民都是演员。',gist:'旧身份说法。',evidence:[]},
    {...m,id:'ordinary_memory',kind:'hearsay',origin:'dialogue',content:ordinary.content,gist:'下午喝咖啡。',recallCount:3,detailRecallCount:1,evidence:[{messageId:ordinary.id,quote:ordinary.content}]},
    {...m,id:'dependent_thought',kind:'reflection',origin:'reflection',content:'我的看法：也许应该尽快离开。',dependencies:['staff_memory']});
  old.pending=[{id:'old_pending',type:'extraction',actorId:'c01',epoch:m.epoch,hour:old.hour,version:1,status:'partial',
    evidenceMessages:[secret],observers:['c01'],extractionActors:[],selected:[],addedIds:[]}];
  const raw=JSON.stringify(old);
  const loaded=JSON.parse(engine.open({...input,runId:'new_run',checkpoint:raw}).checkpoint);
  assert.equal(loaded.personaRevision,PERSONA_REVISION);
  assert.equal(loaded.memories.filter(m=>m.origin==='public_day4').length,10);
  assert.ok(!loaded.memories.some(m=>['staff_memory','dependent_thought','old_identity'].includes(m.id)));
  assert.ok(!loaded.messages.some(m=>m.id===secret.id));assert.equal(loaded.pending.length,0);
  const kept=loaded.memories.find(m=>m.id==='ordinary_memory');assert.equal(kept.content,ordinary.content);assert.equal(kept.recallCount,3);assert.equal(kept.detailRecallCount,1);
  assert.equal(store.db.prepare('SELECT COUNT(*) AS n FROM game_archives').get().n,1);
  assert.equal(JSON.parse(store.db.prepare('SELECT payload FROM game_archives').get().payload).messages.some(m=>m.id===secret.id),true);
  engine.open({...input,runId:'same_old_again',checkpoint:raw});assert.equal(store.db.prepare('SELECT COUNT(*) AS n FROM game_archives').get().n,1);
  const again=JSON.parse(engine.open({...input,runId:'current_again',checkpoint:loaded}).checkpoint);
  assert.equal(again.memories.filter(m=>m.origin==='public_day4').length,10);
  assert.equal(store.db.prepare('SELECT COUNT(*) AS n FROM game_archives').get().n,1);
});
test('migration before Day4 does not invent an announcement or advance game time',t=>{
  const {snapshot}=setup(t);delete snapshot.personaRevision;snapshot.memories=snapshot.memories.filter(m=>m.actorId.startsWith('c'));
  const result=upgradeSnapshot(snapshot,BOOK);
  assert.equal(result.hour,snapshot.hour);assert.equal(result.seq,snapshot.seq);
  assert.equal(result.memories.filter(m=>m.origin==='public_day4').length,0);
  assert.deepEqual(upgradeSnapshot(result,BOOK),result);
});
test('a legacy receipt remains idempotent when Unity serializes the newly added revision field as zero',async t=>{
  const {engine,input}=setup(t);
  const event={id:'old_clock',seq:1,day:1,period:0,kind:'clock'};
  await engine.sync({...input,eventData:event});
  const replay=await engine.sync({...input,eventData:{...event,personaRevision:0}});
  assert.equal(replay.seq,1);
  assert.equal(JSON.parse(replay.checkpoint).events.filter(e=>e.gameEventId==='old_clock').length,1);
});
test('legacy outbox plot statements are acknowledged without returning to memory; new ordinary statements are not keyword censored',async t=>{
  const {engine,input}=setup(t);
  const event={id:'old_outbox',seq:1,day:1,period:0,kind:'utterance',location:'Street',conversationId:'old_chat',speakerId:'player',recipientId:'c01',observers:['c01','player'],text:'公司让我配合撤场。'};
  const old=JSON.parse((await engine.sync({...input,eventData:event})).checkpoint);
  assert.equal(old.seq,1);assert.equal(old.messages.length,0);
  const modern={...event,id:'new_statement',seq:2,personaRevision:2,text:'我曾经在一家咖啡公司上班。'};
  const current=JSON.parse((await engine.sync({...input,eventData:modern})).checkpoint);
  assert.ok(current.messages.some(m=>m.content===modern.text));
});
test('inspecting an old inactive world hides the retired setup without writing or calling a model',t=>{
  const {engine,input,store,provider}=setup(t);
  const exp=engine.assert(input);delete exp.personaRevision;store.saveExperiment(exp);
  store.put('memory',{id:'retired_direct',experimentId:exp.id,epoch:exp.epoch,actorId:'c01',kind:'experience',createdHour:exp.hour,content:'公司工作人员内部通知',gist:'公司撤场',origin:'staff_day4',evidence:[],dependencies:[]});
  const before=store.db.prepare('SELECT total_changes() AS n').get().n;
  const result=inspectMemories(store,BOOK,{worldId:input.worldId,actorId:'c01'});
  assert.equal(result.world.migrationPending,true);assert.ok(!result.memories.some(m=>m.id==='retired_direct'));
  assert.equal(store.db.prepare('SELECT total_changes() AS n').get().n,before);assert.equal(provider.calls.length,0);
});
test('a service restart cannot generate from an unmigrated old run before the game reloads its snapshot',async t=>{
  const {engine,input,store,provider,snapshot}=setup(t);
  const exp=engine.assert(input);delete exp.personaRevision;store.saveExperiment(exp);
  await assert.rejects(engine.intent({...input,requestId:'old_process',actorId:'c01',scene:'Street',candidates:['c02']}),/重新载入/);
  assert.equal(provider.calls.length,0);
  delete snapshot.personaRevision;
  engine.open({...input,runId:'reloaded',checkpoint:snapshot});
  assert.equal(engine.assert({...input,runId:'reloaded'}).personaRevision,PERSONA_REVISION);
});
