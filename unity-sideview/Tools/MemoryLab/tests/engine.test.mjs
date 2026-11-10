import test from "node:test";
import assert from "node:assert/strict";
import { Store } from "../lib/store.mjs";
import { Catalog, clone } from "../lib/catalog.mjs";
import { Engine } from "../lib/engine.mjs";
import { strength, memoryView, memoryState } from "../lib/natural-memory.mjs";
const provider = { publicConfig: () => ({ configured: false }) };
const make = () => {
  const store = new Store(),
    engine = new Engine(store, new Catalog(), provider);
  engine.create("w");
  return engine;
};
test("a failed meeting decision leaves no running job and the next plan can succeed", async(t)=>{
  const e=make();t.after(()=>e.store.close());
  e.decide=()=>{throw new Error("test model failure");};
  await assert.rejects(e.plan("w","z",["linxu"]));
  assert(e.records(e.world("w"),"job").every(j=>j.status==="failed"));
  e.decide=c=>({...reply(c),dialogue:""});
  const next=await e.plan("w","z",["linxu"]);
  assert.equal(next.targetId,"");
  assert(!e.records(e.world("w"),"job").some(j=>j.status==="running"));
});
test("runtime omits an explicit no-communication sentinel but still rejects substantive invalid payloads",async(t)=>{
  const e=make();t.after(()=>e.store.close());
  const c=e.context(e.player("w","z","你好"));
  let output=reply(c);
  e.runtime.provider={publicConfig:()=>({configured:true}),request:()=>({}),call:async()=>({text:JSON.stringify(output),finishReason:"stop"})};
  for(const absent of [false,null]){output.info.communication=absent;assert(!Object.hasOwn((await e.runtime.generate(c)).info,"communication"));}
  for(const invalid of [true,{},"inform"]){output.info.communication=invalid;await assert.rejects(e.runtime.generate(c));}
});
test("the model agenda contract lists only current executable action IDs",t=>{
  const e=make();t.after(()=>e.store.close());
  const c=e.context(e.player("w","liyuan","我们已经接受地图委托了，等Z自己选地方"));
  const agenda=e.policy.outputContract(c).$defs.info.properties.agenda_update;
  assert.deepEqual(agenda.properties.next_action_id.enum,[null,...c.character_state.allowed_next_action_ids]);
  assert.deepEqual(agenda.properties.goal_id.enum,c.character_state.allowed_goals.map(g=>g.id||g));
});
test("actual bystanders restrict disclosure before a line is presented",t=>{
  const e=make();t.after(()=>e.store.close());
  e.place("w","axing","dock");
  const secret=e.observe("w","林序和阿星之间的私人安排",["linxu","axing"],{wake:false,disclose_to:["linxu","axing"]});
  const job=e.job("w","linxu",secret.id,{environment:{actual_audience:["linxu","axing","z"],present_characters:["linxu","axing"]}});
  const c=e.context(job);
  assert(!c.character_state.disclosure_by_recipient.axing.includes(secret.id));
  const o=reply(c,"communicate","axing");o.info.communication.shared_claim_ids=[secret.id];
  assert.throws(()=>e.policy.validate(c,o),/不允许|旁听者/);
  assert.equal(e.records(e.world("w"),"proposal").length,0);
});
export function reply(c, type = "wait", target) {
  const a = c.available_actions.find(
    (a) => a.type === type && (!target || a.target === target),
  );
  assert(a, "Missing action " + type + " " + target);
  const info = clone(c.character_state.info);
  info.changes = {};
  info.memory_updates = [];
  info.knowledge_updates = [];
  let dialogue = c.interaction.mode === "player" ? "我知道了。" : "";
  if (type === "communicate") {
    dialogue = "我们可以聊聊。";
    info.communication = {
      intent: "inform",
      shared_claim_ids: [],
      source_event_ids: [c.interaction.source_event_id],
    };
  }
  if (c.environment.social_opportunity) {
    info.social_decision = {
      choice: type === "communicate" ? "engage" : "keep_to_self",
      reason: "按自己的安排选择",
      source_event_ids: [c.interaction.source_event_id],
    };
    if (type !== "communicate") dialogue = "";
  }
  if (c.character_state.cooperation_requests.length) {
    const request = c.character_state.cooperation_requests[0];
    info.task_participation = {
      task_id: request.task_id,
      choice: "postponed",
      contribution: "稍后再决定",
      step_ids: [],
      reason: "现在另有安排",
      source_event_ids: [c.interaction.source_event_id],
    };
  }
  return {
    dialogue,
    action: {
      action_id: a.id,
      type: a.type,
      target: a.target,
      parameters: a.parameters,
      description: "按当前情况选择下一步",
    },
    info,
  };
}
async function turn(e, actor, text, decide = (c) => reply(c)) {
  const j = e.player("w", actor, text);
  e.decide = decide;
  const r = (await e.run("w", 1, j.id))[0];
  assert.equal(r.status, "proposed", r.validation_error);
  e.commit("w", r.proposalId);
  return r;
}
function offer(c, task) {
  const o = reply(c, "offer_task", "player"),
    a = c.available_actions.find((a) => a.id === "offer_" + task);
  assert(a);
  o.action = {
    ...o.action,
    action_id: a.id,
    type: a.type,
    target: a.target,
    parameters: a.parameters,
  };
  return o;
}
async function active(e, owner = "axing", task = "tools") {
  await turn(e, owner, "有什么委托？", (c) => offer(c, task));
  const chosen = e.tasks.list("w").find((t) => t.status === "offered").id;
  if (task !== chosen) {
    e.tasks.offer("w", task, owner);
  }
  const j = e.taskResponse("w", task, true);
  e.decide = (c) => reply(c);
  const r = (await e.run("w", 1, j.id))[0];
  assert.equal(r.status, "proposed", r.validation_error);
  e.commit("w", r.proposalId);
}

test("eight current identities, private profile isolation and mapped public occupations", (t) => {
  const e = make();
  t.after(() => e.store.close());
  assert.deepEqual(
    e.snapshot("w").actors.map((a) => a.name),
    ["Z", "梨园", "李庭", "阿星", "正义", "小小晴", "留洋", "周伯伯"],
  );
  const j = e.player("w", "z", "咖啡店老板是谁");
  const c = e.context(j);
  e.policy.validateInput(c);
  assert.equal(c.character_state.public_identity.occupation, "主播");
  assert.match(e.catalog.prompt("z"), /李庭：咖啡店老板/);
  assert.doesNotMatch(e.catalog.prompt("z"), /他很早就决定创造Z，亲手参与实现/);
  assert(!e.catalog.actors.has("npc_aya"));
});
test("generated dialogue is not an actual memory or metric change before presentation", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  const j = e.player("w", "z", "谢谢");
  e.decide = (c) => {
    const o = reply(c);
    o.info.affinity = 70;
    o.info.changes.affinity = {
      delta: 20,
      reason: "这次实际关心",
      source_event_ids: [c.interaction.source_event_id],
    };
    return o;
  };
  const r = (await e.run("w", 1, j.id))[0];
  assert.equal(r.status, "proposed");
  assert.equal(e.actor("w", "z").info.affinity, 50);
  assert.equal(
    e.memory.history(e.world("w"), "z").filter((x) => x.speakerId === "z")
      .length,
    0,
  );
  e.commit("w", r.proposalId);
  assert.equal(e.actor("w", "z").info.affinity, 70);
  assert.equal(
    e.memory.history(e.world("w"), "z").filter((x) => x.speakerId === "z")
      .length,
    1,
  );
  e.commit("w", r.proposalId);
  assert.equal(e.actor("w", "z").info.affinity, 70);
});
test("cancelled proposal never becomes speech, a promise or a task", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  const j = e.player("w", "axing", "给我任务");
  e.decide = (c) => reply(c, "offer_task");
  const r = (await e.run("w", 1, j.id))[0];
  e.focus("w", "z", "new-window");
  assert.throws(() => e.commit("w", r.proposalId), /取消|状态/);
  assert(e.tasks.list("w").every((t) => t.status === "available"));
});
test("rules offer co-location but the actor can choose to keep to self", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.place("w", "axing", "dock");
  e.scan("w");
  e.decide = (c) => reply(c);
  const r = (await e.run("w", 1))[0];
  assert.equal(r.output.info.social_decision.choice, "keep_to_self");
  e.commit("w", r.proposalId);
  assert.equal(e.records(e.world("w"), "conversation").length, 0);
});
test("player focuses Z while Linxu and Axing continue their own exchange", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.place("w", "axing", "dock");
  e.focus("w", "z", "player");
  e.scan("w");
  e.decide = (c) => reply(c, "communicate");
  let r = (await e.run("w", 1))[0];
  assert.equal(r.actor, "axing");
  e.commit("w", r.proposalId);
  r = (await e.run("w", 1))[0];
  assert.equal(r.actor, "linxu");
  assert.equal(r.output.action.target, "axing");
  e.commit("w", r.proposalId);
  assert.equal(e.world("w").focus.actor, "z");
  assert.equal(
    e.memory.history(e.world("w"), "z").filter((m) => m.speakerId === "axing")
      .length,
    0,
  );
});
test("pending incoming job pauses under focus and resumes after releasing it", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.place("w", "axing", "dock");
  e.decide = (c) => reply(c, "communicate");
  const r = (await e.run("w", 1))[0];
  e.commit("w", r.proposalId);
  e.focus("w", "linxu", "one");
  assert(!e.ready("w").some((j) => j.actor === "linxu"));
  e.focus("w", null, "one");
  assert(
    e.ready("w").some((j) => j.actor === "linxu" && j.mode === "npc_message"),
  );
});
test("an old window cannot release a newer player conversation", (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.focus("w", "z", "one");
  e.focus("w", "linxu", "two");
  assert.equal(e.focus("w", null, "one"), false);
  assert.equal(e.world("w").focus.actor, "linxu");
});
test("ignored cancellation and changed actor versions fence late model results", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  const j = e.player("w", "z", "旧问题", "one");
  let release;
  e.decide = (c) => new Promise((r) => (release = () => r(reply(c))));
  const pending = e.run("w", 1, j.id);
  await new Promise((r) => setImmediate(r));
  e.focus("w", "z", "two");
  release();
  assert.equal((await pending)[0].status, "cancelled");
  assert.equal(e.actor("w", "z").info.affinity, 50);
});
test("private observations never enter another actor context", (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.observe("w", "只有林序见过的证据", ["linxu"], {
    kind: "fact",
    wake: false,
  });
  const j = e.player("w", "z", "刚才什么事？");
  const c = e.context(j);
  assert(!JSON.stringify(c.character_state).includes("只有林序见过的证据"));
});
test("known verified facts and occupations survive episodic detail loss", (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.observe("w", "你已亲眼核实这一条记录", ["z"], {
    kind: "fact",
    wake: false,
  });
  const w = e.world("w");
  w.hour = 1000;
  e.save(w);
  assert(
    e.memory.facts(w, "z").some((m) => m.summary === "你已亲眼核实这一条记录"),
  );
  const memory = e.records(w, "memory")[0];
  assert(strength(memory, w.hour).clarity < 35);
  assert.notEqual(memoryView(memory, w, "").content, memory.content);
  assert.match(e.catalog.prompt("z"), /李庭：咖啡店老板/);
});
test("memory source text and subjective reflections remain independently inspectable", (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.player("w", "z", "我愿意陪你去码头。");
  const before = JSON.stringify(e.checkpoint("w"));
  const view = e.memory.inspect(e.world("w"), "z");
  assert(view.memories[0].evidence[0].source.includes("我愿意"));
  assert.equal(JSON.stringify(e.checkpoint("w")), before);
});
test("dual memory axes, cue limits and invalidated reflections", () => {
  const m = {
    id: "m",
    actorId: "z",
    epoch: 1,
    kind: "hearsay",
    content: "李庭说周三14:30在咖啡店见",
    gist: "李庭提过见面的事情",
    createdHour: 0,
    importance: 1,
    emotion: 0,
  };
  assert(strength(m, 80).availability > strength(m, 80).clarity);
  const view = memoryView(m, { epoch: 1, hour: 300 }, "");
  assert.equal(view.content, m.gist);
  const r = { ...m, id: "r", kind: "reflection", dependencies: ["m"] };
  assert.equal(
    memoryState(r, { epoch: 1, hour: 0 }, [{ ...m, supersededBy: "new" }, r])
      .code,
    "review",
  );
});
test("clock ticks do not publish Day4 or advance the day", (t) => {
  const e = make();
  t.after(() => e.store.close());
  e.advance("w", 3);
  assert.equal(e.world("w").day, 1);
  for (let n = 0; n < 3; n++) e.day("w");
  assert.equal(e.world("w").day, 4);
  assert(
    !e
      .records(e.world("w"), "event")
      .some((m) => m.text.includes("毁灭") || m.text.includes("撤离")),
  );
});
test("map navigation cannot cross the bay or continue south through the middle connector", (t) => {
  const e = make();
  t.after(() => e.store.close());
  assert.deepEqual(e.catalog.route("dock", "boardwalk"), [
    "dock",
    "visitor_center",
    "main_street",
    "seafront_west",
    "boardwalk",
  ]);
  assert(!e.graph.public_middle.includes("old_bookshop"));
  assert.equal(e.catalog.route("beach", "hilltop_platform"), null);
});
test("move is only completed by a matching executor receipt", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  const r = await turn(e, "linxu", "向游客中心走", (c) =>
    reply(c, "move", "visitor_center"),
  );
  const id = e.actor("w", "linxu").pending_action;
  assert.equal(e.actor("w", "linxu").scene, "dock");
  assert.throws(() => e.actionReceipt("w", id, true, { scene: "cafe" }));
  e.actionReceipt("w", id, true, { scene: "visitor_center" });
  assert.equal(e.actor("w", "linxu").scene, "visitor_center");
  e.actionReceipt("w", id, true, { scene: "visitor_center" });
  assert.throws(() => e.actionReceipt("w", id, false, {}));
});
test("tasks are player-only and acceptance does not make NPCs willing", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  assert.throws(() => e.tasks.offer("w", "tools", "axing", "z"));
  await active(e);
  assert.equal(e.tasks.state("w", "tools").status, "active");
  assert.equal(e.tasks.collaborators("w", "tools").length, 0);
  assert.throws(() => e.tasks.respond("w", "tools", true, "z"));
});
test("each invited NPC chooses only their own scope and ordinary steps still need receipts", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  await active(e);
  const j = e.invite("w", "tools", "z", "愿意帮我找扳手吗");
  e.decide = (c) => {
    const o = reply(c);
    o.info.task_participation = {
      task_id: "tools",
      choice: "willing",
      contribution: "只帮找扳手",
      step_ids: ["wrench"],
      reason: "现在愿意",
      source_event_ids: [c.interaction.source_event_id],
    };
    return o;
  };
  const r = (await e.run("w", 1, j.id))[0];
  assert.equal(r.status, "proposed", r.validation_error);
  e.commit("w", r.proposalId);
  assert.equal(e.tasks.state("w", "tools").status, "active");
  assert.deepEqual(e.tasks.list("w", "axing")[0].progress, {});
  assert.throws(() =>
    e.taskReceipt("w", "tools", "manual", "wrong", true, "z", {}),
  );
  e.taskReceipt("w", "tools", "wrench", "real", true, "z", {});
});
test("refusal can stop for now and a new exchange can reopen the same task", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  await turn(e, "axing", "什么任务", (c) => offer(c, "tools"));
  const j = e.taskResponse("w", "tools", false);
  e.decide = (c) => {
    const o = reply(c);
    o.info.task_followup = {
      task_id: "tools",
      choice: "stop",
      reason: "先尊重拒绝",
      source_event_ids: [c.interaction.source_event_id],
    };
    return o;
  };
  let r = (await e.run("w", 1, j.id))[0];
  e.commit("w", r.proposalId);
  assert(!e.context(j).available_actions.some((a) => a.id === "offer_tools"));
  r = await turn(e, "axing", "现在有空了", (c) => {
    const o = offer(c, "tools");
    o.info.task_followup = {
      task_id: "tools",
      choice: "reoffer",
      reason: "玩家明确有空再谈",
      source_event_ids: [c.interaction.source_event_id],
    };
    return o;
  });
  assert.equal(e.tasks.state("w", "tools").status, "offered");
});
test("Z cannot output NPC support, and Zhou cannot gain escape willingness", (t) => {
  const e = make();
  t.after(() => e.store.close());
  let c = e.context(e.player("w", "z", "你愿意吗")),
    o = reply(c);
  o.info.persuasion = 100;
  assert.throws(() => e.policy.validate(c, o));
  c = e.context(e.player("w", "zhou_bobo", "帮Z逃出去"));
  o = reply(c);
  o.info.persuasion = 80;
  o.info.support_willingness = "willing";
  assert.throws(() => e.policy.validate(c, o));
});
test("schema, source and delta checks reject forged output", (t) => {
  const e = make();
  t.after(() => e.store.close());
  const c = e.context(e.player("w", "z", "你好"));
  let o = reply(c);
  o.info.trust = 70;
  o.info.changes.trust = {
    delta: 20,
    reason: "不属于本人的信息",
    source_event_ids: ["secret"],
  };
  assert.throws(() => e.policy.validate(c, o));
  o = reply(c);
  o.action.parameters = { teleport: true };
  assert.throws(() => e.policy.validate(c, o));
  o = reply(c);
  o.info.changes.trust = {
    delta: 0,
    reason: "无变化",
    source_event_ids: [c.interaction.source_event_id],
  };
  assert.throws(() => e.policy.validate(c, o));
});
test("checkpoint reload restores prior facts and drops newer events and replies", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  await turn(e, "z", "先记录这件事");
  const cp = e.checkpoint("w");
  e.observe("w", "这是读档后应消失的新事件", ["z"], {
    kind: "fact",
    wake: false,
  });
  e.open("w", "new-run", cp);
  assert(
    !e.memory
      .facts(e.world("w"), "z")
      .some((m) => m.summary.includes("应消失")),
  );
  assert.equal(e.world("w").runId, "new-run");
  assert.equal(e.actor("w", "z").info.affinity, 50);
});
test("checkpoint rejects wrong roster and future memories", (t) => {
  const e = make();
  t.after(() => e.store.close());
  const cp = e.checkpoint("w");
  cp.world.actors.npc_aya = {};
  assert.throws(() => e.open("w", "new", cp), /名单/);
  delete cp.world.actors.npc_aya;
  cp.records.memory.push({ id: "future", createdHour: 99 });
  assert.throws(() => e.open("w", "new", cp), /未来/);
});
test("official task progress and willingness restore with the same save", async (t) => {
  const e = make();
  t.after(() => e.store.close());
  await active(e);
  const cp = e.checkpoint("w");
  e.tasks.enable("w", "departure_prep");
  e.open("w", "restore", cp);
  assert.equal(e.tasks.state("w", "tools").status, "active");
  assert.equal(e.tasks.state("w", "departure_prep").enabled, 0);
});

test('opening without a checkpoint cannot adopt newer server progress', async t => {
  const e=make();t.after(()=>e.store.close());
  await turn(e,'z','这句话不是空存档里应该存在的经历');
  const w=e.world('w');w.actors.z.info.affinity=85;e.save(w);
  e.open('w','empty-checkpoint',null);
  assert.equal(e.world('w').seq,0);
  assert.equal(e.actor('w','z').info.affinity,50);
  assert.equal(e.memory.history(e.world('w'),'z').length,0);
});

test('repeated task receipts do not create new knowledge or progress',async t=>{
  const e=make();t.after(()=>e.store.close());await active(e);
  e.taskReceipt('w','tools','wrench','receipt-once',true,'player',{});
  const seq=e.world('w').seq;
  e.taskReceipt('w','tools','wrench','receipt-once',true,'player',{});
  assert.equal(e.world('w').seq,seq);
});
