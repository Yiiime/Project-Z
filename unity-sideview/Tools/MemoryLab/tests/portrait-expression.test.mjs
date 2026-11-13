import fs from "node:fs";
import {portraitExpressionKeys,portraitPromptRules} from "../lib/portrait-expression.mjs";
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
    const context = JSON.parse(request.messages.find(m=>m.role==="user"&&m.content.startsWith("{")).content);
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


const expectedCatalog=JSON.parse(fs.readFileSync(new URL("../content/current/contracts/portrait-expressions.json",import.meta.url),"utf8"));
test("all eight actors expose exactly the user catalog; aliases and cross-actor keys are neutral",()=>{
  for(const [actor,entry] of Object.entries(expectedCatalog.actors)){
    assert.deepEqual(portraitExpressionKeys(actor),entry.expressions);
    for(const value of entry.expressions)assert.equal(portraitExpression(actor,value),value);
    for(const other of Object.values(expectedCatalog.actors).flatMap(x=>x.expressions))
      if(!entry.expressions.includes(other))assert.equal(portraitExpression(actor,other),"neutral");
    for(const invalid of [undefined,null,"SMILE","bawl_dry","smiling","沉脸","__proto__"])
      assert.equal(portraitExpression(actor,invalid),"neutral");
    assert.match(portraitPromptRules(actor),/人设、关系、记忆/);
  }
  assert.equal(supportsPortraitExpression("c01"),false);
  assert.equal(portraitExpression("c01","neutral"),null);
});
test("text does not force or veto the NPC's chosen expression",()=>{
  assert.equal(portraitExpression("z",undefined,"哈哈我笑一个。"),"neutral");
  assert.equal(portraitExpression("z","sad","我没难过，你去吧。"),"sad");
  assert.equal(portraitExpression("liyuan","bawl","我不会哭的。"),"bawl");
});
test("runtime proposals keep presentation out of action/info and preserve it after restore",async t=>{
  const {engine,provider}=setup(t);
  for(const key of expectedCatalog.actors.z.expressions){
    provider.expression=key;const result=await playerReply(engine,"z","继续聊聊");
    assert.equal(result.expression,key);assert.deepEqual(Object.keys(result.output).sort(),["action","dialogue","info"]);
    engine.commit("w",result.proposalId);
  }
  provider.expression="smile";
  const result=await playerReply(engine,"z","你笑一下");assert.equal(result.expression,"neutral");
  const checkpoint=engine.checkpoint("w");engine.open("w","restored",checkpoint);
  assert.equal(engine.store.record(result.proposalId).portrait_expression,"neutral");
});
test("model prompt contract uses actor-specific values",async t=>{
  for(const actor of ["z","liyuan","liting","linxu"]){
    const {engine,provider}=setup(t,"default",actor);await playerReply(engine,actor,"你最近怎么样？");
    const prompt=provider.calls.at(-1).messages[0].content;
    const contract=JSON.parse(prompt.slice(prompt.lastIndexOf("输出JSON契约：")+"输出JSON契约：".length));
    assert.deepEqual(contract.$defs.output.properties.expression.enum,expectedCatalog.actors[actor].expressions);
  }
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


test("compact action output repairs invalid state emotion without relaxing the contract", async t => {
  class CompactProvider extends InvalidEmotionProvider {
    async call(request,options) {
      const response=await super.call(request,options),output=JSON.parse(response.text);
      const context=JSON.parse(request.messages[1].content);
      const wait=context.available_actions.find(a=>a.type==='wait');
      output.action={option_id:wait.id};
      output.info={emotion:output.info.emotion,changes:{}};
      return {...response,text:JSON.stringify(output)};
    }
  }
  const provider=new CompactProvider(1);const {engine}=setup(t,"default","z",provider);
  const job=engine.player("w","z","你好，很高兴来岛上。");
  const result=(await engine.run("w",1,job.id))[0];
  assert.equal(result.status,"proposed",result.validation_error);
  assert.equal(provider.calls.length,2);
  assert.match(provider.calls[1].messages[0].content,/角色回合契约错误：\/info\/emotion/);
});
