import test from "node:test";
import assert from "node:assert/strict";
import { once } from "node:events";
import { createServer } from "../server.mjs";
function reply(c) {
  let action = c.available_actions.find((a) => a.type === "wait");
  if (c.interaction.mode === "npc_message" || c.environment.social_opportunity)
    action =
      c.available_actions.find((a) => a.type === "communicate") || action;
  const info = structuredClone(c.character_state.info);
  if (action.type === "communicate") {
    info.communication = {
      intent: "inform",
      shared_claim_ids: [],
      source_event_ids: [c.interaction.source_event_id],
    };
    info.social_decision = {
      choice: "engage",
      reason: "愿意交谈",
      source_event_ids: [c.interaction.source_event_id],
    };
  }
  if (c.character_state.cooperation_requests.length) {
    const q = c.character_state.cooperation_requests[0];
    info.task_participation = {
      task_id: q.task_id,
      choice: "declined",
      contribution: "先做自己的事",
      step_ids: [],
      reason: "当前有别的安排",
      source_event_ids: [c.interaction.source_event_id],
    };
  }
  return {
    dialogue:
      action.type === "communicate" ? "可以聊聊。" : "认识，李庭是咖啡店老板。",
    action: {
      action_id: action.id,
      type: action.type,
      target: action.target,
      parameters: action.parameters,
      description: "回答当前问题",
    },
    info,
  };
}
function zReply(c) {
  const output = reply(c);
  if (c.actor_id === "z") output.expression = "happy";
  return output;
}
function litingReply(c) {
  const output = reply(c);
  if (c.actor_id === "liting") output.expression = "troubled";
  return output;
}
async function setup(t, decide = reply) {
  const app = createServer({
    dbPath: ":memory:",
    providerFactory: () => ({
      mode: "simulated-test",
      publicConfig: () => ({ configured: true }),
      config: { topK: 6 },
    }),
    decide,
    backgroundEnabled: false,
  });
  app.server.listen(0, "127.0.0.1");
  await once(app.server, "listening");
  const base = "http://127.0.0.1:" + app.server.address().port;
  t.after(async () => {
    app.server.close();
    await once(app.server, "close");
    app.store.close();
  });
  const post = async (path, body = {}, native = false) => {
    const r = await fetch(base + "/api/" + path, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(native ? { "X-Z-Memory": "1" } : { "X-Test-Token": app.token }),
      },
      body: JSON.stringify(body),
    });
    return { code: r.status, data: await r.json() };
  };
  return { app, post, base };
}
const base = { worldId: "native", runId: "r1", day: 1, period: 0 };
test("Unity supplies actual bystanders to the model before generating NPC speech",async t=>{
  let observed=false;
  const {post}=await setup(t,c=>{observed=true;assert.deepEqual(c.environment.actual_audience,["linxu","axing","z"]);return reply(c);});
  await post("game/open",base,true);
  await post("game/sync",{...base,eventData:{id:"meeting",seq:1,day:1,period:0,kind:"observation",text:"林序和阿星在码头停下交谈，Z站在旁边。",observers:["linxu","axing","z"],discloseTo:["linxu","axing","z"],locationId:"dock",conversationId:"pair"}},true);
  const r=await post("game/utterance",{...base,actorId:"linxu",recipientId:"axing",conversationId:"pair",scene:"Street",locationId:"dock",observers:["linxu","axing","z"]},true);
  assert.equal(r.code,200,r.data.error);assert(observed);
});
const event = (id, seq, kind = "utterance") => ({
  id,
  seq,
  kind,
  day: 1,
  period: 0,
  speakerId: "player",
  recipientId: "z",
  conversationId: "c1",
  text: "咖啡店老板是谁",
  locationId: "z_home",
  channel: "private_player_chat",
  observers: ["z", "player"],
});
test('HTTP utterance binds the actual input and rejects another conversation or an engine speaker',async t=>{
 const {post,app}=await setup(t);await post('game/open',base,true);await post('game/focus',{...base,actorId:'z'},true);
 assert.equal((await post('game/sync',{...base,eventData:event('source',1)},true)).code,200);
 const request={...base,actorId:'z',recipientId:'player',conversationId:'c1',sourceEventId:'source'};
 for(const patch of [{conversationId:'unrelated'},{actorId:'linxu'},{sourceEventId:'missing'}])
  assert.equal((await post('game/utterance',{...request,...patch},true)).code,400);
 assert.equal((await post('game/sync',{...base,eventData:{...event('invalid',2),speakerId:'engine'}},true)).code,400);
 assert.equal(app.engine.world('native').seq,1);
 assert.equal((await post('game/utterance',request,true)).code,200);
});
test("native open, focus, spoken input, proposed output and actual speech are separate", async (t) => {
  const { app, post } = await setup(t);
  assert.equal((await post("game/open", base, true)).code, 200);
  await post("game/focus", { ...base, actorId: "z" }, true);
  await post("game/sync", { ...base, eventData: event("p1", 1) }, true);
  const generated = await post(
    "game/utterance",
    {
      ...base,
      actorId: "z",
      recipientId: "player",
      conversationId: "c1",
      requestId: "q1",
      scene: "Street",
      locationId: "z_home",
    },
    true,
  );
  assert.equal(generated.code, 200, generated.data.error);
  assert.equal(generated.data.result.dialogue, "认识，李庭是咖啡店老板。");
  assert.equal(
    app.engine.memory
      .history(app.engine.world("native"), "z")
      .filter((m) => m.speakerId === "z").length,
    0,
  );
  const said = {
    ...event("n1", 2),
    speakerId: "z",
    recipientId: "player",
    text: generated.data.text,
    proposalId: generated.data.proposalId,
  };
  const ack = await post("game/sync", { ...base, eventData: said }, true);
  assert.equal(ack.code, 200, ack.data.error);
  assert.equal(ack.data.seq, 2);
  assert.equal(
    app.engine.memory
      .history(app.engine.world("native"), "z")
      .filter((m) => m.speakerId === "z").length,
    1,
  );
});
test("player reply exposes explicit Z expression and keeps it across event replay", async (t) => {
  const { app, post } = await setup(t, zReply);
  await post("game/open", base, true);
  await post("game/focus", { ...base, actorId: "z" }, true);
  await post("game/sync", { ...base, eventData: event("p1", 1) }, true);
  const generated = await post(
    "game/utterance",
    {
      ...base,
      actorId: "z",
      recipientId: "player",
      conversationId: "c1",
      requestId: "expression-q",
      scene: "Street",
      locationId: "z_home",
    },
    true,
  );
  assert.equal(generated.code, 200, generated.data.error);
  assert.equal(generated.data.expression, "happy");
  assert.deepEqual(Object.keys(generated.data.result).sort(), [
    "action",
    "dialogue",
    "info",
  ]);
  const proposal = app.store.record(generated.data.proposalId);
  assert.equal(proposal.portrait_expression, "happy");
  const said = {
    ...event("n-expression", 2),
    speakerId: "z",
    recipientId: "player",
    text: generated.data.text,
    proposalId: generated.data.proposalId,
  };
  const ack = await post("game/sync", { ...base, eventData: said }, true);
  assert.equal(ack.code, 200, ack.data.error);
  assert.equal(ack.data.expression, "happy");
  const replay = await post("game/sync", { ...base, eventData: said }, true);
  assert.equal(replay.code, 200, replay.data.error);
  assert.equal(replay.data.expression, "happy");
});
test("native Liting reply exposes r5 semantic expression and keeps it across event replay", async (t) => {
  const { app, post } = await setup(t, litingReply);
  const litingBase = { ...base, runId: "liting-r1" };
  const litingEvent = {
    ...event("liting-player", 1),
    recipientId: "liting",
    conversationId: "liting-c1",
    text: "咖啡店老板今天在吗？",
    locationId: "cafe",
  };
  await post("game/open", litingBase, true);
  await post("game/focus", { ...litingBase, actorId: "liting" }, true);
  await post("game/sync", { ...litingBase, eventData: litingEvent }, true);
  const generated = await post(
    "game/utterance",
    {
      ...litingBase,
      actorId: "liting",
      recipientId: "player",
      conversationId: "liting-c1",
      requestId: "liting-expression-q",
      scene: "Street",
      locationId: "cafe",
    },
    true,
  );
  assert.equal(generated.code, 200, generated.data.error);
  assert.equal(generated.data.expression, "troubled");
  assert.deepEqual(Object.keys(generated.data.result).sort(), [
    "action",
    "dialogue",
    "info",
  ]);
  const proposal = app.store.record(generated.data.proposalId);
  assert.equal(proposal.portrait_expression, "troubled");
  const said = {
    ...litingEvent,
    id: "liting-npc",
    seq: 2,
    speakerId: "liting",
    recipientId: "player",
    text: generated.data.text,
    proposalId: generated.data.proposalId,
    observers: ["liting", "player"],
  };
  const ack = await post("game/sync", { ...litingBase, eventData: said }, true);
  assert.equal(ack.code, 200, ack.data.error);
  assert.equal(ack.data.expression, "troubled");
  const replay = await post("game/sync", { ...litingBase, eventData: said }, true);
  assert.equal(replay.code, 200, replay.data.error);
  assert.equal(replay.data.expression, "troubled");
});
test("event replay is idempotent and conflicting content is rejected", async (t) => {
  const { post } = await setup(t);
  await post("game/open", base, true);
  const original = { ...base, eventData: event("same", 1) };
  assert.equal((await post("game/sync", original, true)).data.seq, 1);
  assert.equal((await post("game/sync", original, true)).data.seq, 1);
  assert.equal(
    (
      await post(
        "game/sync",
        { ...base, eventData: { ...event("same", 1), text: "换成另一句话" } },
        true,
      )
    ).code,
    400,
  );
  assert.equal(
    (await post("game/sync", { ...base, eventData: event("gap", 3) }, true))
      .code,
    400,
  );
});
test("private player lines do not become nearby actors memories", async (t) => {
  const { app, post } = await setup(t);
  await post("game/open", base, true);
  await post(
    "game/sync",
    {
      ...base,
      eventData: {
        ...event("private", 1),
        observers: ["z", "player", "linxu"],
      },
    },
    true,
  );
  assert.equal(
    app.engine.memory.history(app.engine.world("native"), "linxu").length,
    0,
  );
});
test("unsupported hardcoded Day4 and model world actions cannot change date or knowledge", async (t) => {
  const { app, post } = await setup(t);
  await post("game/open", base, true);
  const r = await post(
    "game/sync",
    { ...base, day: 4, eventData: { ...event("day4", 1, "day4"), day: 4 } },
    true,
  );
  assert.equal(r.code, 400);
  assert.equal(app.engine.world("native").day, 1);
  assert.equal(app.engine.world("native").seq, 0);
});
test("story events are delivered only to the supplied observers", async (t) => {
  const { app, post } = await setup(t);
  await post("game/open", base, true);
  const r = await post(
    "game/sync",
    {
      ...base,
      eventData: {
        ...event("story", 1, "story_event"),
        text: "林序亲眼看见这件事",
        observers: ["linxu"],
        verified: true,
      },
    },
    true,
  );
  assert.equal(r.code, 200, r.data.error);
  assert.equal(
    app.engine.memory.facts(app.engine.world("native"), "linxu").length,
    1,
  );
  assert.equal(
    app.engine.memory.facts(app.engine.world("native"), "z").length,
    0,
  );
});
test("restored outbox may replay previously approved actual speech, while old run requests fail", async (t) => {
  const { post } = await setup(t, zReply);
  await post("game/open", base, true);
  await post("game/focus", { ...base, actorId: "z" }, true);
  await post("game/sync", { ...base, eventData: event("p1", 1) }, true);
  const generated = (
    await post(
      "game/utterance",
      {
        ...base,
        actorId: "z",
        recipientId: "player",
        conversationId: "c1",
        scene: "Street",
      },
      true,
    )
  ).data;
  assert.equal(generated.expression, "happy");
  const cp = generated.checkpoint;
  const b2 = { ...base, runId: "r2" };
  const restored = await post("game/open", { ...b2, checkpoint: cp }, true);
  assert.equal(restored.code, 200, restored.data.error);
  assert.equal((await post("game/checkpoint", base, true)).code, 400);
  const ack = await post(
    "game/sync",
    {
      ...b2,
      eventData: {
        ...event("actual-after-restore", 2),
        speakerId: "z",
        recipientId: "player",
        proposalId: generated.proposalId,
        text: generated.text,
      },
    },
    true,
  );
  assert.equal(ack.code, 200, ack.data.error);
  assert.equal(ack.data.expression, "happy");
  assert.equal(ack.data.seq, 2);
});
test("browser receives dialogue only while the backend retains and commits all three parts", async (t) => {
  const { app, post } = await setup(t);
  const created = (await post("new", { actor: "z", client: "browser" })).data;
  const r = await post("turn", {
    session: created.session,
    actor: "z",
    client: "browser",
    text: "老板是谁",
  });
  assert.equal(r.code, 200);
  assert.equal(r.data.results[0].status, "proposed");
  assert.deepEqual(Object.keys(r.data.results[0].output), ["dialogue"]);
  assert.equal("actions" in r.data, false);
  assert.equal("social_decisions" in r.data, false);
  assert.ok(r.data.actors.every((a) => !("info" in a) && !("peer_relations" in a)));
  const proposal = app.store.record(r.data.results[0].proposalId);
  assert.deepEqual(Object.keys(proposal.output).sort(), ["action", "dialogue", "info"]);
  assert.equal(proposal.status, "pending");
  const ack = await post("proposal/commit", {
    session: created.session,
    proposalId: r.data.results[0].proposalId,
  });
  assert.equal(ack.code, 200, ack.data.error);
  assert.equal(app.store.record(proposal.id).status, "committed");
  assert.deepEqual(app.engine.world(created.session).actors.z.info, proposal.output.info);
  assert.equal(
    app.engine.memory
      .history(app.engine.world(created.session), "z")
      .filter((m) => m.speakerId === "z").length,
    1,
  );
});
test("model generation can reply without waiting for memory consolidation", async (t) => {
  const { app, post } = await setup(t);
  await post("game/open", base, true);
  await post("game/focus", { ...base, actorId: "z" }, true);
  await post("game/sync", { ...base, eventData: event("p1", 1) }, true);
  let resolve;
  app.engine.memory.consolidator.extract = () =>
    new Promise((r) => (resolve = r));
  const working = app.engine.memory.consolidate("world_native", 1);
  await new Promise((r) => setImmediate(r));
  const r = await post(
    "game/utterance",
    {
      ...base,
      actorId: "z",
      recipientId: "player",
      conversationId: "c1",
      scene: "Street",
    },
    true,
  );
  assert.equal(r.code, 200, r.data.error);
  resolve();
  await working;
});

test('NPC planning does not consume Unity event sequence numbers',async t=>{
  const {app,post}=await setup(t,c=>{const o=reply(c);if(c.environment.intent_only)o.dialogue='';return o;});
  await post('game/open',base,true);
  const planned=await post('game/intent',{...base,actorId:'linxu',candidates:['z'],conversationId:'planning_1',scene:'Street'},true);
  assert.equal(planned.code,200,planned.data.error);
  assert.equal(planned.data.seq,0);
  assert.equal(app.engine.world('native').seq,0);
  assert.equal((await post('game/sync',{...base,eventData:event('after_plan',1)},true)).data.seq,1);
});

test('task choices use the same durable Unity event queue as spoken lines',async t=>{
  const {app,post}=await setup(t);
  await post('game/open',base,true);await post('game/focus',{...base,actorId:'axing'},true);
  app.engine.tasks.offer('native','tools','axing');
  const accepted=await post('game/sync',{...base,eventData:{id:'accept',seq:1,kind:'task_response',day:1,period:0,taskId:'tools',accepted:true,responderId:'player',conversationId:'task-player'}},true);
  assert.equal(accepted.code,200,accepted.data.error);assert.equal(accepted.data.seq,1);
  assert.equal(app.engine.tasks.state('native','tools').status,'active');
  const invited=await post('game/sync',{...base,eventData:{id:'invite',seq:2,kind:'task_invite',day:1,period:0,taskId:'tools',actorId:'z',text:'愿意陪我找工具吗？',conversationId:'task-player-z'}},true);
  assert.equal(invited.code,200,invited.data.error);assert.equal(invited.data.seq,2);
  assert.equal(app.engine.tasks.collaborators('native','tools')[0].status,'invited');
});

test("Z progresses from public childhood detail to a private follow-up only after actual speech",async t=>{
  let offer;
  const {post}=await setup(t,context=>{
    offer=context.z_story;
    const result=reply(context);
    if(offer)result.dialogue=offer.line+"。";
    return result;
  });
  await post("game/open",base,true);
  await post("game/sync",{...base,eventData:{id:"first_public",seq:1,kind:"live_memory",speakerId:"z",
    text:"李庭是我的发小。",day:1,period:0,channel:"livestream",stepId:"childhood_friend",receiptId:"1"}},true);
  await post("game/focus",{...base,actorId:"z"},true);
  await post("game/sync",{...base,eventData:{...event("followup",2),text:"你和李庭后来怎么样了？"}},true);
  const ask=await post("game/utterance",{...base,actorId:"z",recipientId:"player",conversationId:"c1",scene:"Street"},true);
  assert.equal(ask.code,200,ask.data.error);assert.equal(offer.count,1);
  const checkpoint=JSON.parse(ask.data.checkpoint);
  assert.equal(checkpoint.world.zNarrated.length,1);
  const heard=await post("game/sync",{...base,eventData:{...event("actually_spoken",3),speakerId:"z",recipientId:"player",
    proposalId:ask.data.proposalId,text:ask.data.text}},true);
  assert.equal(heard.code,200,heard.data.error);
  const after=JSON.parse(heard.data.checkpoint);
  assert.equal(after.world.zNarrated.at(-1).step,2);
  assert.equal(after.broadcast.stories.find(s=>s.id==="childhood_friend").count,1);
  assert(!after.broadcast.entries.some(e=>e.id==="actually_spoken"));
});

test("switching between Z and Liting keeps private chat history and portrait replies with their speaker", async (t) => {
  const seen = [];
  const { app, post } = await setup(t, (context) => {
    seen.push(context);
    return context.actor_id === "z" ? zReply(context) : litingReply(context);
  });
  const world = { ...base, worldId: "two-portraits" };
  const turns = [
    { actor: "z", location: "z_home", marker: "Z_PRIVATE_741", expression: "happy" },
    { actor: "liting", location: "cafe", marker: "LITING_PRIVATE_219", expression: "troubled" },
    { actor: "z", location: "z_home", marker: "Z_RETURN_806", expression: "happy" },
  ];
  const native = async (endpoint, payload) => {
    const response = await post("game/" + endpoint, { ...world, ...payload }, true);
    assert.equal(response.code, 200, response.data.error);
    return response.data;
  };
  await native("open", {});
  for (const [index, turn] of turns.entries()) {
    const conversationId = "player-session-" + index;
    await native("focus", { actorId: turn.actor });
    const input = {
      ...event("input-" + index, index * 2 + 1),
      conversationId, recipientId: turn.actor, locationId: turn.location, text: turn.marker,
      observers: ["z", "liting", "player"],
    };
    await native("sync", { eventData: input });
    const line = await native("utterance", {
      actorId: turn.actor, recipientId: "player", conversationId, locationId: turn.location,
      scene: turn.actor === "z" ? "Street" : "Cafe", channel: input.channel,
    });
    assert.equal(line.actorId, turn.actor);
    assert.equal(line.expression, turn.expression);
    const context = seen.at(-1);
    assert.equal(context.actor_id, turn.actor);
    assert.equal(context.interaction.conversation_id, conversationId);
    assert.ok(context.character_state.recent_conversation.some((m) => m.text === turn.marker));
    for (const other of turns.filter((candidate) => candidate.actor !== turn.actor))
      assert.ok(!JSON.stringify(context).includes(other.marker), "private text leaked to " + turn.actor);
    await native("sync", { eventData: {
      ...input, id: "reply-" + index, seq: input.seq + 1,
      speakerId: turn.actor, recipientId: "player", text: line.text, proposalId: line.proposalId,
    } });
  }
  assert.equal(seen.length, turns.length);
  for (const actor of ["z", "liting"]) {
    const history = app.engine.memory.history(app.engine.world(world.worldId), actor);
    for (const turn of turns)
      assert.equal(history.some((m) => m.text === turn.marker), turn.actor === actor);
  }
});


test("native HTTP never forwards a Z expression alias outside the user enum",async t=>{
  const {app,post}=await setup(t,c=>({...reply(c),expression:"smile"}));
  await post("game/open",base,true);await post("game/focus",{...base,actorId:"z"},true);
  await post("game/sync",{...base,eventData:event("strict-player",1)},true);
  const generated=await post("game/utterance",{...base,actorId:"z",recipientId:"player",conversationId:"c1",requestId:"strict-expression",scene:"Street",locationId:"z_home"},true);
  assert.equal(generated.code,200,generated.data.error);assert.equal(generated.data.expression,"neutral");
  assert.equal(app.store.record(generated.data.proposalId).portrait_expression,"neutral");
});
