import test from "node:test";
import assert from "node:assert/strict";
import { Store } from "../lib/store.mjs";
import { Catalog, clone } from "../lib/catalog.mjs";
import { Engine } from "../lib/engine.mjs";
import {
  LIUYANG_PORTRAIT_EXPRESSIONS,
  portraitExpression,
  supportsPortraitExpression,
} from "../lib/portrait-expression.mjs";

class FixtureProvider {
  constructor(scenario = "default") {
    this.scenario = scenario;
    this.expression = "starry_curious";
    this.calls = [];
    this.config = { topK: 6 };
  }
  publicConfig() {
    return { configured: true };
  }
  request(messages, options = {}) {
    return { messages, ...options };
  }
  async call(request) {
    this.calls.push(request);
    const context = JSON.parse(request.messages.at(-1).content);
    const action = context.available_actions.find((a) => a.type === "wait");
    const info = clone(context.character_state.info);
    info.changes = {};
    info.memory_updates = [];
    info.knowledge_updates = [];
    let dialogue = `${context.character_state.public_identity.name}：今天想和你聊聊。`;
    let expression;
    if (context.actor_id === "z") {
      if (this.scenario === "smile") {
        if (/大哭|痛哭|哭/.test(context.interaction.input)) {
          dialogue = "不了，我不想大哭，平静聊聊就好。";
          expression = "bawl";
        } else {
          dialogue = "哈哈，这么突然？好吧，笑一个——不过我更习惯躲在相机后面……";
        }
      } else if (this.scenario === "negative_quote") {
        dialogue = "她笑了，不过我没笑。";
      } else if (this.scenario === "negative_correction") {
        dialogue = "不是笑，我只是说‘笑’这个词。";
      } else if (this.scenario === "negative_report") {
        dialogue = "你别怕，小孩在哭。";
      } else {
        expression = this.expression;
      }
    } else if (context.actor_id === "liting") {
      if (this.scenario === "smile") {
        dialogue = "哈哈，今天的豆子还不错。";
      } else {
        expression = this.expression;
      }
    } else if (context.actor_id === "liyuan") {
      expression = this.expression;
    } else if (context.actor_id === "linxu") {
      expression = this.expression;
    }
    return {
      text: JSON.stringify({
        dialogue,
        action: {
          action_id: action.id,
          type: action.type,
          target: action.target,
          parameters: action.parameters,
          description: "按当前情况保持交谈",
        },
        info,
        ...(expression === undefined ? {} : { expression }),
      }),
      finishReason: "stop",
    };
  }
}

class InvalidEmotionProvider extends FixtureProvider {
  constructor(invalidCalls = 1) {
    super("default");
    this.invalidCalls = invalidCalls;
  }
  async call(request, options) {
    const response = await super.call(request, options);
    if (this.invalidCalls > 0) {
      this.invalidCalls--;
      const output = JSON.parse(response.text);
      output.info.emotion = "smile";
      return { ...response, text: JSON.stringify(output) };
    }
    return response;
  }
}

function setup(
  t,
  scenario = "default",
  actor = "z",
  provider = new FixtureProvider(scenario),
) {
  const store = new Store();
  const engine = new Engine(store, new Catalog(), provider);
  engine.create("w");
  engine.focus("w", actor, "player");
  t.after(() => store.close());
  return { engine, provider };
}

async function playerReply(engine, actor, text) {
  const job = engine.player("w", actor, text);
  const result = (await engine.run("w", 1, job.id))[0];
  assert.equal(result.status, "proposed", result.validation_error);
  return result;
}

test("Z portrait metadata stays outside action/info and infers direct laughter", async (t) => {
  const { engine } = setup(t, "smile");
  const first = await playerReply(engine, "z", "你笑一下");
  assert.equal(first.expression, "happy");
  assert.deepEqual(Object.keys(first.output).sort(), ["action", "dialogue", "info"]);
  assert.equal(first.output.expression, undefined);

  engine.commit("w", first.proposalId);
  const second = await playerReply(engine, "z", "你大哭一下");
  assert.equal(second.expression, null);

  const events = engine.records(engine.world("w"), "event");
  assert.ok(events.some((event) => event.speaker === "player" && event.text === "你笑一下"));
  assert.ok(events.some((event) => event.speaker === "z" && event.text === first.output.dialogue));
});

test("player denial, correction, quotes, and third-party words do not force an expression", async (t) => {
  for (const scenario of ["negative_report", "negative_quote", "negative_correction"]) {
    const { engine } = setup(t, scenario);
    const result = await playerReply(engine, "z", "你笑一下");
    assert.equal(result.expression, null, scenario);
  }
  const { engine } = setup(t, "smile");
  const result = await playerReply(engine, "z", "不要大哭，先说正事");
  assert.equal(result.expression, null);
});

test("Z expression aliases and allowlist survive a durable proposal restore", async (t) => {
  const { engine, provider } = setup(t);
  for (const [raw, expected] of [
    ["happy", "happy"],
    ["sad", "sad"],
    ["shy", "shy"],
    ["serious", "serious"],
    ["happy_wave", "happy_wave"],
    ["smile", "happy"],
    ["wave", "happy_wave"],
    ["cry", "cry"],
    ["bawl", "bawl"],
    ["surprise", "surprise"],
    ["starry_curious", "starry_curious"],
  ]) {
    provider.expression = raw;
    const result = await playerReply(engine, "z", "继续聊聊");
    assert.equal(result.expression, expected, raw);
    engine.commit("w", result.proposalId);
  }

  provider.expression = "invalid_expression";
  const invalid = await playerReply(engine, "z", "再聊一句");
  assert.equal(invalid.expression, null);
  const checkpoint = engine.checkpoint("w");
  const pending = checkpoint.records.proposal.at(-1);
  assert.equal(pending.portrait_expression, null);

  engine.open("w", "restored", checkpoint);
  const restored = engine.store.record(pending.id);
  assert.equal(restored.portrait_expression, null);
  engine.commit("w", pending.id, { replayDelivered: true });
  assert.equal(engine.store.record(pending.id).status, "committed");
});

test("Liyuan v04-r2 maps only the approved neutral, smile, and bawl states", async (t) => {
  const { engine, provider } = setup(t, "default", "liyuan");
  for (const [raw, expected] of [
    ["neutral", "neutral"],
    ["smile", "smile"],
    ["happy", "smile"],
    ["bawl", "bawl"],
    ["bawl_dry", "bawl"],
  ]) {
    provider.expression = raw;
    const result = await playerReply(engine, "liyuan", "继续聊聊");
    assert.equal(result.expression, expected, raw);
    assert.deepEqual(Object.keys(result.output).sort(), ["action", "dialogue", "info"]);
    engine.commit("w", result.proposalId);
  }
  for (const unsupported of ["laugh", "laughing", "cry", "crying", "starry_curious"]) {
    provider.expression = unsupported;
    const invalid = await playerReply(engine, "liyuan", "再聊一句");
    assert.equal(invalid.expression, "neutral", unsupported);
    engine.commit("w", invalid.proposalId);
  }
});

test("Liyuan keeps refused or unavailable roleplay neutral", () => {
  assert.equal(portraitExpression("liyuan", "bawl", "我哭不出来"), "neutral");
  assert.equal(portraitExpression("liyuan", "bawl", "呜……我以为再也见不到你了……（放声哭）你回来了。"), "bawl");
  assert.equal(
    portraitExpression("liyuan", "bawl", "这段戏我不接，先聊正事。"),
    "neutral",
  );
  assert.equal(
    portraitExpression("liyuan", "bawl", "我接这段戏，放声哭着把告别说完。"),
    "bawl",
  );
  assert.equal(portraitExpression("liyuan", "laugh", "我来大笑给你看"), "neutral");
});

test("Qingye c02 maps the approved eight dialogue expression keys", () => {
  for (const [raw, expected] of [
    ["neutral", "neutral"],
    ["smile", "smile"],
    ["smiling", "smile"],
    ["bawl", "bawl"],
    ["surprise", "surprise"],
    ["shock", "shock"],
    ["question", "question"],
    ["starry_curious", "starry_curious"],
    ["angry", "angry"],
  ])
    assert.equal(portraitExpression("xiaoxiaoqing", raw, "今天继续聊聊"), expected, raw);
  assert.equal(portraitExpression("xiaoxiaoqing", "bawl", "我不会哭"), "neutral");
  assert.equal(portraitExpression("xiaoxiaoqing", "laugh", "哈哈"), "neutral");
});

test("Linxu keeps c01 as a model resource, with five top-level portrait states", async (t) => {
  const { engine, provider } = setup(t, "default", "linxu");
  assert.equal(engine.catalog.actors.has("linxu"), true);
  assert.equal(engine.catalog.actors.has("c01"), false);
  assert.equal(engine.catalog.map.public_actors.c01, undefined);
  assert.equal(supportsPortraitExpression("linxu"), true);
  assert.equal(supportsPortraitExpression("c01"), false);
  assert.deepEqual([...LIUYANG_PORTRAIT_EXPRESSIONS], [
    "neutral",
    "smile",
    "troubled",
    "bawl",
    "sigh",
  ]);

  for (const state of LIUYANG_PORTRAIT_EXPRESSIONS) {
    provider.expression = state;
    const result = await playerReply(engine, "linxu", "继续聊聊");
    assert.equal(result.expression, state, state);
    assert.deepEqual(Object.keys(result.output).sort(), [
      "action",
      "dialogue",
      "info",
    ]);
    engine.commit("w", result.proposalId);
  }
});

test("Liyuan portrait prompt keeps normal speech neutral and leaves bawl timing to Unity", async (t) => {
  const { engine, provider } = setup(t, "default", "liyuan");
  await playerReply(engine, "liyuan", "你最近在画什么？");
  const prompt = provider.calls.at(-1).messages[0].content;
  const encodedContract = prompt.slice(
    prompt.lastIndexOf("输出JSON契约：") + "输出JSON契约：".length,
  );
  const contract = JSON.parse(encodedContract);
  assert.deepEqual(contract.$defs.output.properties.expression.enum, [
    "neutral",
    "smile",
    "bawl",
  ]);
  for (const label of ["neutral", "smile", "bawl", "bawl_dry", "bawl_tears"])
    assert.match(prompt, new RegExp(label));
  assert.match(prompt, /嘴角保持平直/);
  assert.match(prompt, /最终生成的台词/);
  assert.match(prompt, /玩家请求、否定、纠正/);
  assert.match(prompt, /邀请双方排练/);
  assert.match(prompt, /哭不出来/);
  assert.match(prompt, /大笑.*laugh\/laughing/);
  assert.match(prompt, /不要写进info、memory_updates或knowledge_updates/);
});

test("real-runtime prompt documents denial-safe Z expression selection", async (t) => {
  const { engine, provider } = setup(t, "smile");
  await playerReply(engine, "z", "你笑一下");
  const prompt = provider.calls.at(-1).messages[0].content;
  for (const label of [
    "happy",
    "sad",
    "shy",
    "serious",
    "happy_wave",
    "cry",
    "bawl",
    "surprise",
    "starry_curious",
  ])
    assert.match(prompt, new RegExp(label));
  assert.match(prompt, /否定/);
  assert.match(prompt, /纠正/);
  assert.match(prompt, /最终生成的台词/);
});

test("Liting r5 maps the complete playable vocabulary and leaves entry phases to Unity", async (t) => {
  const { engine, provider } = setup(t, "default", "liting");
  for (const [raw, expected] of [
    ["neutral", "neutral"],
    ["smile", "smile"],
    ["happy", "smile"],
    ["surprise", "surprise"],
    ["starry_curious", "starry_curious"],
    ["curious", "troubled"],
    ["troubled", "troubled"],
    ["blush", "blush"],
    ["angry", "angry"],
    ["disappointed", "disappointed"],
    ["cry", "cry"],
    ["bawl_dry", "bawl"],
    ["bawling", "bawl"],
    ["bawl", "bawl"],
    ["gloom", "gloom"],
    ["sigh_in", "sigh"],
    ["sigh", "sigh"],
    ["wink", "wink"],
    ["laugh", "laugh"],
  ]) {
    provider.expression = raw;
    const result = await playerReply(engine, "liting", "继续聊聊");
    assert.equal(result.expression, expected, raw);
    assert.deepEqual(Object.keys(result.output).sort(), ["action", "dialogue", "info"]);
    engine.commit("w", result.proposalId);
  }
});

test("real-runtime prompt documents Liting semantic keys and Unity-owned entry phases", async (t) => {
  const { engine, provider } = setup(t, "default", "liting");
  const job = engine.player("w", "liting", "咖啡还好吗？");
  const result = (await engine.run("w", 1, job.id))[0];
  assert.equal(result.status, "proposed", result.validation_error);
  const prompt = provider.calls.at(-1).messages[0].content;
  const encodedContract = prompt.slice(
    prompt.lastIndexOf("输出JSON契约：") + "输出JSON契约：".length,
  );
  const contract = JSON.parse(encodedContract);
  assert.deepEqual(contract.$defs.output.properties.expression.enum, [
    "neutral",
    "smile",
    "surprise",
    "starry_curious",
    "troubled",
    "blush",
    "angry",
    "disappointed",
    "cry",
    "bawl",
    "gloom",
    "sigh",
    "wink",
    "laugh",
  ]);
  for (const label of [
    "neutral",
    "smile",
    "surprise",
    "starry_curious",
    "troubled",
    "blush",
    "angry",
    "disappointed",
    "cry",
    "bawl",
    "gloom",
    "sigh",
    "wink",
    "laugh",
  ])
    assert.match(prompt, new RegExp(label));
  assert.match(prompt, /bawl_dry/);
  assert.match(prompt, /sigh_in/);
  assert.match(prompt, /Unity/);
  assert.match(prompt, /否定/);
  assert.match(prompt, /最终生成的台词/);
});

test("player portrait actors repair one invalid info emotion and return one real proposal", async (t) => {
  const provider = new InvalidEmotionProvider(1);
  const { engine } = setup(t, "default", "z", provider);
  const job = engine.player("w", "z", "你笑一下");
  const result = (await engine.run("w", 1, job.id))[0];
  assert.equal(result.status, "proposed", result.validation_error);
  assert.equal(provider.calls.length, 2);
  assert.equal(engine.records(engine.world("w"), "proposal").length, 1);
  assert.equal(
    engine.records(engine.world("w"), "event").filter((event) => event.speaker === "z")
      .length,
    0,
  );
  assert.match(provider.calls[1].messages[0].content, /\/info\/emotion/);
  assert.match(provider.calls[1].messages[0].content, /顶层 expression/);
});

test("a persistent invalid player output fails after one bounded repair without proposing speech", async (t) => {
  const provider = new InvalidEmotionProvider(2);
  const { engine } = setup(t, "default", "liting", provider);
  const job = engine.player("w", "liting", "你笑一下");
  const result = (await engine.run("w", 1, job.id))[0];
  assert.equal(result.status, "failed");
  assert.equal(provider.calls.length, 2);
  assert.match(result.validation_error, /输出格式错误：\/info\/emotion/);
  assert.equal(engine.records(engine.world("w"), "proposal").length, 0);
  assert.equal(
    engine.records(engine.world("w"), "event").filter((event) => event.speaker === "liting")
      .length,
    0,
  );
});

test("background portrait actors receive the info emotion boundary before validation", async (t) => {
  const provider = new InvalidEmotionProvider(1);
  const { engine } = setup(t, "default", "z", provider);
  const event = engine.addEvent("w", {
    text: "后台事件触发一次自主判断。",
    kind: "claim",
    speaker: "engine",
    visible: ["z"],
    disclose_to: ["z"],
  });
  const job = engine.job("w", "z", event.id, { mode: "world_event" });
  await assert.rejects(
    engine.runtime.generate(engine.context(job)),
    /输出格式错误：\/info\/emotion/,
  );
  assert.equal(provider.calls.length, 1);
  const prompt = provider.calls[0].messages[0].content;
  assert.match(prompt, /info\.emotion是角色状态字段/);
  assert.match(prompt, /头像表现只能放在顶层expression/);
});

test("Axing retries an invalid portrait reply once", async (t) => {
  const provider = new InvalidEmotionProvider(2);
  const { engine } = setup(t, "default", "axing", provider);
  const job = engine.player("w", "axing", "你笑一下");
  const result = (await engine.run("w", 1, job.id))[0];
  assert.equal(result.status, "failed");
  assert.equal(provider.calls.length, 2);
  assert.match(result.validation_error, /输出格式错误：\/info\/emotion/);
});

test("Liting direct laughter fallback is denial-safe", async (t) => {
  const { engine } = setup(t, "smile", "liting");
  const result = await playerReply(engine, "liting", "你笑一下");
  assert.equal(result.expression, "smile");
  assert.equal(portraitExpression("liting", null, "我不想笑。"), null);
});

test("an affirmative line from the standalone yields a smile, including when metadata is absent", () => {
  assert.equal(
    portraitExpression("z", null, "行，那就笑一个——不过你这要求也太突然了吧。"),
    "happy",
  );
  assert.equal(portraitExpression("z", null, "她说：“哈哈，我笑一个。”"), null);
  assert.equal(portraitExpression("z", "happy", "不了，我不想笑。"), null);
});

test("Axing and Zhoubo use their own bound expressions", () => {
  assert.equal(portraitExpression("axing", "repair"), "repair");
  assert.equal(portraitExpression("axing", "gloom", "你生气了吗？"), "neutral");
  assert.equal(portraitExpression("zhou_bobo", "沉脸"), "gloom");
  assert.equal(portraitExpression("zhou_bobo", "repair"), null);
  assert.equal(portraitExpression("zhou_bobo", null, "你黑化吧"), null);
});
