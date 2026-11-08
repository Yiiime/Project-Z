import test from "node:test";
import assert from "node:assert/strict";
import { Store } from "../lib/store.mjs";
import { Catalog, clone } from "../lib/catalog.mjs";
import { Engine } from "../lib/engine.mjs";

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

function setup(t, scenario = "default") {
  const store = new Store();
  const provider = new FixtureProvider(scenario);
  const engine = new Engine(store, new Catalog(), provider);
  engine.create("w");
  engine.focus("w", "z", "player");
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
