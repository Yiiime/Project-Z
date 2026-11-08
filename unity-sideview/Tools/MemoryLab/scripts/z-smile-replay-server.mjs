#!/usr/bin/env node
import { createServer } from "../server.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 && args[index + 1] ? args[index + 1] : fallback;
};
const port = Number(option("--port", "4332"));
const dbPath = option("--db", ":memory:");
if (!Number.isInteger(port) || port < 1 || port > 65535)
  throw new Error("--port 必须是 1 到 65535 的整数");

function validReply(context) {
  let action = context.available_actions.find((item) => item.type === "wait");
  if (
    context.interaction.mode === "npc_message" ||
    context.environment.social_opportunity
  ) {
    action =
      context.available_actions.find((item) => item.type === "communicate") ||
      action;
  }
  if (!action) action = context.available_actions[0];
  if (!action) throw new Error("当前上下文没有可执行动作");

  const info = structuredClone(context.character_state.info);
  if (action.type === "communicate") {
    info.communication = {
      intent: "inform",
      shared_claim_ids: [],
      source_event_ids: [context.interaction.source_event_id],
    };
    info.social_decision = {
      choice: "engage",
      reason: "愿意交谈",
      source_event_ids: [context.interaction.source_event_id],
    };
  }
  if (context.character_state.cooperation_requests.length) {
    const request = context.character_state.cooperation_requests[0];
    info.task_participation = {
      task_id: request.task_id,
      choice: "declined",
      contribution: "先做自己的事",
      step_ids: [],
      reason: "当前有别的安排",
      source_event_ids: [context.interaction.source_event_id],
    };
  }
  return {
    dialogue:
      action.type === "communicate"
        ? "可以聊聊。"
        : "认识，李庭是咖啡店老板。",
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

function replay(context) {
  const output = validReply(context);
  if (context.actor_id !== "z" || context.interaction.mode !== "player")
    return output;

  // Derive the sequence from persisted conversation history so restarting the
  // isolated service with the same DB does not reset the fixed replay.
  const turn = context.character_state.recent_conversation.filter(
    (message) => message.speakerId === "z",
  ).length;
  if (turn === 0) {
    output.dialogue = "哈哈，我先笑一下。";
    output.expression = "smile";
  } else {
    output.dialogue = "不了，我不想大哭。";
    output.expression = "neutral";
  }
  return output;
}

const app = createServer({
  dbPath,
  decide: replay,
  backgroundEnabled: false,
  providerFactory: () => ({
    mode: "simulated",
    config: { topK: 6 },
    publicConfig: () => ({ configured: true }),
  }),
});

app.server.listen(port, "127.0.0.1", () => {
  console.log(
    `mode=simulated url=http://127.0.0.1:${port} db=${dbPath}`,
  );
});
const close = () => {
  app.server.close(() => app.store.close());
};
process.once("SIGINT", close);
process.once("SIGTERM", close);
