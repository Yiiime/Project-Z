// 周伯伯新版沉脸只走 NPC 自己的结构化回复字段。
// 不从玩家原话、触发词或 info.emotion 推断头像状态。
export const ZHOU_BOBO_PORTRAIT_EXPRESSIONS = Object.freeze([
  "neutral",
  "gloom",
]);

export const ZHOU_BOBO_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  dark: "gloom",
  black: "gloom",
  blackened: "gloom",
  gloom_face: "gloom",
  gloomy: "gloom",
  "沉脸": "gloom",
  "黑化": "gloom",
});

const EXPRESSION_SET = new Set(ZHOU_BOBO_PORTRAIT_EXPRESSIONS);

/**
 * Keep this adapter deliberately narrow. `line` is accepted for a shared
 * call-site signature, but never used to infer a state from dialogue text.
 */
export function normalizeZhouBoboPortraitExpression(value, _line = "") {
  const normalized =
    typeof value === "string"
      ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
      : "";
  if (!normalized) return null;
  const canonical =
    ZHOU_BOBO_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized;
  return EXPRESSION_SET.has(canonical) ? canonical : null;
}

export const ZHOU_BOBO_PORTRAIT_PROMPT_RULES =
  " 根据周伯伯最终生成的台词选择顶层字段expression。可用值为neutral、gloom。gloom只表示周伯伯本轮确实采用新版沉脸状态；平时、省略或neutral回到中性。该字段只描述周伯伯自己的NPC回复后的头像表现，不是action或info的一部分。只按周伯伯最终生成的结构化回复选择，不从玩家请求、否定、纠正、引用、触发词或info.emotion推断；不要新增其他表情键。其他角色不要给expression。";
