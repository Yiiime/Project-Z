import {portraitExpressionKeys, portraitPromptRules, portraitExpression} from "./portrait-expression.mjs";
export const ZHOU_BOBO_PORTRAIT_EXPRESSIONS = Object.freeze(portraitExpressionKeys("zhou_bobo"));
export const ZHOU_BOBO_PORTRAIT_PROMPT_RULES = portraitPromptRules("zhou_bobo");
export function normalizeZhouBoboPortraitExpression(value, line = "") {
  return portraitExpression("zhou_bobo", value, line);
}
