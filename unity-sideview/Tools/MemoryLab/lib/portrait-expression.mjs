const Z_PROFILE_EXPRESSIONS = Object.freeze([
  "happy",
  "sad",
  "shy",
  "serious",
  "happy_wave",
]);

const Z_AUTHORED_MOTIONS = Object.freeze([
  "cry",
  "bawl",
  "surprise",
  "starry_curious",
]);

// c04 李庭 v02-r5 is a separate model from Z. Keep its game-facing keys
// separate from Z's older compatibility vocabulary: the Unity presenter owns
// the two-stage entry choreography for bawl and sigh.
export const LITING_PORTRAIT_EXPRESSIONS = Object.freeze([
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

export const LITING_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  happy: "smile",
  warm: "smile",
  smiling: "smile",
  curious: "troubled",
  confused: "troubled",
  questioning: "troubled",
  thinking: "troubled",
  worried: "troubled",
  starry: "starry_curious",
  curious_starry: "starry_curious",
  shy: "blush",
  sad: "disappointed",
  crying: "cry",
  weep: "cry",
  sob: "bawl",
  big_cry: "bawl",
  bawling: "bawl",
  bawl_dry: "bawl",
  astonished: "surprise",
  surprised: "surprise",
  angry_face: "angry",
  dark: "gloom",
  sigh_in: "sigh",
  sighing: "sigh",
  big_laugh: "laugh",
  laughing: "laugh",
});

export const Z_PORTRAIT_EXPRESSIONS = Object.freeze([
  ...Z_PROFILE_EXPRESSIONS,
  ...Z_AUTHORED_MOTIONS,
]);

export const Z_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  smile: "happy",
  wave: "happy_wave",
  happywave: "happy_wave",
  blush: "shy",
  stern: "serious",
  weep: "cry",
  sob: "bawl",
  astonished: "surprise",
  curious: "starry_curious",
});

const EXPRESSION_SET = new Set(Z_PORTRAIT_EXPRESSIONS);
const LITING_EXPRESSION_SET = new Set(LITING_PORTRAIT_EXPRESSIONS);
const PORTRAIT_ACTOR_IDS = new Set(["z", "liting"]);

const negatedCue = (line, cues) =>
  cues.some((cue) =>
    new RegExp(
      `(?:不|没|未|无|别|不要|不想|不能|无法|拒绝|不愿意|不太愿意)[^。！？!?\\n]{0,8}${cue}`,
    ).test(line),
  );

const hasCue = (line, cues) => cues.some((cue) => new RegExp(cue).test(line));

const CRY_CUES = [
  "大哭",
  "痛哭",
  "嚎啕",
  "崩溃大哭",
  "哭",
  "流泪",
  "眼泪",
  "呜呜",
  "抽泣",
];

const SIGH_CUES = ["叹气", "叹息", "无奈"];

const DIRECT_LAUGH_CUES = [
  "(?:^|[，,。.!！?？、\\s])(?:那(?:我)?就|我这就|那我来|这就)(?:给你)?笑(?:一下|一个|给你看)",
  "^(?:哈哈+|呵呵+|嘿嘿+|嘻嘻+)(?:[，,。.!！?？、\\s]|$)",
  "(?:^|[，,。.!！?？、\\s])(?:好|好吧|可以|行|那我|我来|我先)[，,。.!！?？、\\s]{0,2}(?:我?[来先]?)?笑(?:一下|一个|给你看)?",
  "(?:^|[，,。.!！?？、\\s])我(?:来|先)?笑(?:一下|一个|给你看)?",
];

function agreesToSmileNow(value) {
  if (typeof value !== "string") return false;
  const ownWords = value.replace(/“[^”]*”|「[^」]*」|‘[^’]*’|"[^"]*"/g, "");
  // Keep an explicit refusal. Only a limit on duration or future occasions
  // can follow a present agreement without cancelling it.
  const present = ownWords
    .replace(/(?:不过|但是|但)?(?:别|不要)让我(?:一直|始终)笑/g, "")
    .replace(/(?:不过|但是|但)?(?:可)?不是(?:每回|每次)都(?:白)?笑/g, "");
  if (negatedCue(present, ["笑"]) || /(?:算了|我收回|我反悔|逗你的)/.test(present))
    return false;
  const agreement = /(?:^|[。！？!?\n])\s*(?:(?:笑一下|笑一个)[？?]\s*(?:好|行|可以)[啊呀吧]?[，,、\s]*给你|(?:我(?:就|来|先)?)?笑给你看)(?=$|[。，,、！!；;—…\s])/;
  return agreement.test(present);
}

/**
 * Infer only from Z's own generated line. Player wording is deliberately not
 * passed here, so a request, denial, correction, or quoted third-party word
 * cannot force a portrait state.
 */
export function inferPortraitExpression(value) {
  const line =
    typeof value === "string"
      ? value
          .trim()
          .toLowerCase()
          .replace(/“[^”]*”|「[^」]*」|‘[^’]*’|"[^"]*"/g, "")
      : "";
  if (!line) return null;
  if (!negatedCue(line, ["笑"]) && hasCue(line, DIRECT_LAUGH_CUES))
    return "happy";
  return null;
}

/**
 * The Liting model may omit the adapter field when the line itself is an
 * explicit laugh. Keep this fallback narrow: quoted, reported, or negated
 * uses of "笑" must not trigger a portrait state.
 */
function inferLitingPortraitExpression(value) {
  return inferPortraitExpression(value) === "happy" ? "smile" : null;
}

export function supportsPortraitExpression(actorId) {
  return PORTRAIT_ACTOR_IDS.has(actorId);
}

export function portraitPromptRules(actorId) {
  if (actorId === "z")
    return " 根据Z最终生成的台词选择顶层字段expression。可用值为" +
      Z_PORTRAIT_EXPRESSIONS.join("、") +
      "和neutral。Z真的笑出声或答应当场微笑时填happy。玩家请求、否定、纠正、引用他人都不能强制触发表情。Z明确拒绝哭时用neutral。";
  if (actorId === "liting")
    return " 根据李庭最终生成的台词选择顶层字段expression。可用值为neutral、smile、surprise、starry_curious、troubled、blush、angry、disappointed、cry、bawl、gloom、sigh、wink、laugh。服务端兼容明确别名，但模型优先输出这些游戏语义键；疑惑用troubled（curious仅为兼容别名），微笑用smile，大哭用bawl，叹气用sigh。bawl_dry与sigh_in只是Unity层负责的进入阶段，不要输出这两个阶段键。该字段只描述本次李庭台词后的头像表现，不是action/info的一部分。只根据李庭最终生成的台词和确实听到的上下文选择；玩家请求、否定、纠正、引号或第三方提到的词不能强制表情，明确拒绝或否认时不要误选哭、大哭或叹气。省略或neutral表示回到中性。其他角色不要给expression。";
  return " 不要输出expression或其他头像表现字段。";
}

/**
 * Normalize the optional model-side portrait field into the allowlisted
 * top-level HTTP metadata. The inner V1.2 output remains dialogue/action/info.
 */
export function portraitExpression(actorId, value, line = "") {
  if (!supportsPortraitExpression(actorId)) return null;
  const liting = actorId === "liting";
  const fallback = () =>
    liting ? inferLitingPortraitExpression(line) : inferPortraitExpression(line);
  const normalized =
    typeof value === "string"
      ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
      : "";
  if (!liting && (!normalized || normalized === "neutral" ||
      (Z_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized) === "happy") &&
      agreesToSmileNow(line)) return "happy";
  if (!normalized) return fallback();
  if (!liting && normalized === "neutral") return fallback();
  const aliases = liting
    ? LITING_PORTRAIT_EXPRESSION_ALIASES
    : Z_PORTRAIT_EXPRESSION_ALIASES;
  const expressionSet = liting ? LITING_EXPRESSION_SET : EXPRESSION_SET;
  const canonical = aliases[normalized] || normalized;
  if (!expressionSet.has(canonical)) return fallback();
  if (!liting && canonical === "happy" && negatedCue(String(line), ["笑"]))
    return null;
  if (
    (canonical === "cry" || canonical === "bawl") &&
    negatedCue(String(line).toLowerCase(), CRY_CUES)
  )
    return fallback();
  if (liting && canonical === "sigh" && negatedCue(String(line).toLowerCase(), SIGH_CUES))
    return fallback();
  return canonical;
}
