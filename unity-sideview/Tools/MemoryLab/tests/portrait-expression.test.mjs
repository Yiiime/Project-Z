import test from "node:test";
import assert from "node:assert/strict";
import { Store } from "../lib/store.mjs";
import { Catalog, clone } from "../lib/catalog.mjs";
import { Engine } from "../lib/engine.mjs";
import { portraitExpression } from "../lib/portrait-expression.mjs";

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

test("non-Z actors cannot export portrait metadata", async (t) => {
  const { engine, provider } = setup(t);
  provider.expression = "bawl";
  const result = await playerReply(engine, "liyuan", "继续聊聊");
  assert.equal(result.expression, null);
  assert.equal(result.output.expression, undefined);
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

test("other player actors keep the single-attempt path", async (t) => {
  const provider = new InvalidEmotionProvider(2);
  const { engine } = setup(t, "default", "liyuan", provider);
  const job = engine.player("w", "liyuan", "你笑一下");
  const result = (await engine.run("w", 1, job.id))[0];
  assert.equal(result.status, "failed");
  assert.equal(provider.calls.length, 1);
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
