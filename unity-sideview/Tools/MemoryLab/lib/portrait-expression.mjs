import fs from "node:fs";
const catalog = JSON.parse(fs.readFileSync(new URL("../content/current/contracts/portrait-expressions.json", import.meta.url), "utf8"));
const expressionsByActor = new Map(Object.entries(catalog.actors).map(([id, actor]) => [id, Object.freeze(actor.expressions)]));
export const LITING_PORTRAIT_EXPRESSIONS = expressionsByActor.get("liting");
export const LIYUAN_PORTRAIT_EXPRESSIONS = expressionsByActor.get("liyuan");
export const LIUYANG_PORTRAIT_EXPRESSIONS = expressionsByActor.get("linxu");
export function supportsPortraitExpression(actorId) { return expressionsByActor.has(actorId); }
export function portraitExpressionKeys(actorId) { return [...(expressionsByActor.get(actorId) || [])]; }
export function portraitPromptRules(actorId) {
  const keys = portraitExpressionKeys(actorId);
  if (!keys.length) return " 不要输出expression或其他头像表现字段。";
  return ` 根据角色人设、关系、记忆、本人情绪和最终生成的台词，自主选择顶层expression。可用值严格为${keys.join("、")}，不允许别名、跨角色表情或内部动画阶段。neutral表示自然中性；没有给出表情时回到neutral。允许克制或复杂的情绪，不强制每句换脸。玩家请求、否定、纠正、引用和第三方描述只是理解语境的依据，不能按词触发表情；例如嘴上否认难过也可能真实难过，由角色判断。expression不是info.emotion，表情不能写进info、memory_updates或knowledge_updates充当世界事实。出声内容不夹动作和旁白；动画进入、保持、哭泣阶段由Unity处理。`;
}
// Strict transport allowlist. Older replies with missing or invalid metadata
// reset to neutral, never infer an expression from dialogue or accept aliases.
export function portraitExpression(actorId, value, _line = "") {
  const keys = portraitExpressionKeys(actorId);
  return keys.length ? (keys.includes(value) ? value : "neutral") : null;
}
