import {
  ZHOU_BOBO_PORTRAIT_EXPRESSIONS,
  ZHOU_BOBO_PORTRAIT_PROMPT_RULES,
  normalizeZhouBoboPortraitExpression,
} from "./zhou-bobo-portrait.mjs";

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

// c03 梨园 v04-r2 is a separate half-body model.  Keep its deliberately
// small game-facing vocabulary separate from the richer portrait sets above:
// normal speech is flat/neutral, while the approved smile and bawl states are
// the only two non-neutral states this integration can request.
export const LIYUAN_PORTRAIT_EXPRESSIONS = Object.freeze([
  "neutral",
  "smile",
  "bawl",
]);

export const LIYUAN_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  happy: "smile",
  smiling: "smile",
  warm: "smile",
  sob: "bawl",
  big_cry: "bawl",
  bawling: "bawl",
  bawl_dry: "bawl",
  bawl_tears: "bawl",
  bawl_tears_preview: "bawl",
  idle: "neutral",
  flat: "neutral",
});

// c01 怀表 v05-r03 是 linxu 挂载的实际模型资源。语义键与 Unity 的 dialogue-map 保持一致；
// 进入、保持、说话峰值和哭泣阶段由 Unity 控制器接管，服务端只发最终状态。
export const LIUYANG_PORTRAIT_EXPRESSIONS = Object.freeze([
  "neutral",
  "smile",
  "troubled",
  "bawl",
  "sigh",
]);

export const LIUYANG_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  happy: "smile",
  warm: "smile",
  smiling: "smile",
  curious: "troubled",
  confused: "troubled",
  questioning: "troubled",
  puzzled: "troubled",
  sob: "bawl",
  big_cry: "bawl",
  bawling: "bawl",
  bawl_dry: "bawl",
  sigh_in: "sigh",
  sighing: "sigh",
});

// c02 晴野 v02-r6.1 uses a small dialogue-facing vocabulary. Unity owns
// entry/hold/exit choreography, so this adapter carries only the semantic key
// selected by 晴野's own generated reply.
export const QINGYE_PORTRAIT_EXPRESSIONS = Object.freeze([
  "neutral",
  "smile",
  "bawl",
  "surprise",
  "shock",
  "question",
  "starry_curious",
  "angry",
]);

export const QINGYE_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  smiling: "smile",
  bawling: "bawl",
  big_cry: "bawl",
  astonished: "surprise",
  surprised: "surprise",
  shocked: "shock",
  startled: "shock",
  questioning: "question",
  curious_starry: "starry_curious",
  angry_face: "angry",
});

// c06 正义的 Street 运行时使用同一套对白表情语义；六态选择器和哭泣
// 的闭眼→落泪阶段由 Unity 控制器接管，服务端只发最终对白状态键。
export const ZHENGYI_PORTRAIT_EXPRESSIONS = Object.freeze([
  "neutral",
  "smile",
  "surprise",
  "shock",
  "puzzled",
  "curious",
  "angry",
  "shy",
  "bawl",
]);

export const ZHENGYI_PORTRAIT_EXPRESSION_ALIASES = Object.freeze({
  happy: "smile",
  smiling: "smile",
  surprised: "surprise",
  astonished: "surprise",
  shocked: "shock",
  troubled: "puzzled",
  confused: "puzzled",
  questioning: "puzzled",
  question: "puzzled",
  starry: "curious",
  starry_curious: "curious",
  blush: "shy",
  bawling: "bawl",
  cry: "bawl",
  sob: "bawl",
  big_cry: "bawl",
  bawl_dry: "bawl",
  bawl_tears: "bawl",
  bawl_tears_preview: "bawl",
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
const LIYUAN_EXPRESSION_SET = new Set(LIYUAN_PORTRAIT_EXPRESSIONS);
const LIUYANG_EXPRESSION_SET = new Set(LIUYANG_PORTRAIT_EXPRESSIONS);
const QINGYE_EXPRESSION_SET = new Set(QINGYE_PORTRAIT_EXPRESSIONS);
const ZHENGYI_EXPRESSION_SET = new Set(ZHENGYI_PORTRAIT_EXPRESSIONS);
const AXING_EXPRESSIONS = Object.freeze(["neutral", "repair", "puzzled", "angry", "surprise", "sigh", "smile"]);
const expressionsByActor = new Map([
  ["z", Z_PORTRAIT_EXPRESSIONS],
  ["liyuan", LIYUAN_PORTRAIT_EXPRESSIONS],
  ["liting", LITING_PORTRAIT_EXPRESSIONS],
  ["axing", AXING_EXPRESSIONS],
  ["zhengyi", ZHENGYI_PORTRAIT_EXPRESSIONS],
  ["xiaoxiaoqing", QINGYE_PORTRAIT_EXPRESSIONS],
  ["linxu", LIUYANG_PORTRAIT_EXPRESSIONS],
  ["zhou_bobo", ZHOU_BOBO_PORTRAIT_EXPRESSIONS],
]);

export function portraitExpressionKeys(actorId) {
  return expressionsByActor.get(actorId);
}

const negatedCue = (line, cues) =>
  cues.some((cue) =>
    new RegExp(
      `(?:不|没|未|无|别|不要|不想|不能|无法|拒绝|不愿意|不太愿意)[^。！？!?\\n]{0,8}${cue}`,
    ).test(line) ||
    // A direct refusal can put the negation after the cue: "我哭不出来".
    new RegExp(
      `${cue}(?:不出来|不下去|不起来|不了|不动|不成|不能|无法|没有|没法)`,
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

const refusesRoleplay = (line) =>
  /(?:戏|排练|角色)[^。！？!?\n]{0,6}(?:不接|不演|不排|拒绝)|(?:不接|不演|不排|拒绝)[^。！？!?\n]{0,6}(?:戏|排练|角色)/.test(
    line,
  );

// Match refusal of the performance itself. “见不到你了……（放声哭）”
// negates seeing someone, not the performed expression.
const refusesLiyuanPerformance = (line, action) => {
  const negative = "(?:不(?:太)?(?:想|会|肯|能|愿意)?|没(?:有|想|法)?|未|无法|别|不要|拒绝)";
  const modifier = "(?:再|真的|当场|这样|刻意|勉强|放声|放肆|大声|微微|露出|给你|让我){0,2}";
  return new RegExp(negative + modifier + action).test(line) ||
    new RegExp(action + "(?:倒不至于|不出来|不起来|不下去|不了|不动|不成|不能|没法)").test(line);
};

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

/**
 * 梨园's fallback is intentionally limited to an explicit first-person
 * laugh.  Ordinary words such as "哭" are not enough to infer bawl: the real
 * provider must opt into that state through the structured expression field.
 */
function inferLiyuanPortraitExpression(value) {
  return inferPortraitExpression(value) === "happy" ? "smile" : null;
}

export function supportsPortraitExpression(actorId) {
  return expressionsByActor.has(actorId);
}

export function portraitPromptRules(actorId) {
  if (actorId === "zhou_bobo") return ZHOU_BOBO_PORTRAIT_PROMPT_RULES;
  if (actorId === "axing")
    return " 阿星按自己本轮台词的表现选择顶层 expression。专注检修用 repair，疑惑侧目用 puzzled，生气制止用 angry，意外警觉用 surprise，无奈叹气用 sigh，安心浅笑用 smile。平静时用 neutral。玩家的请求和引用不能替阿星决定表情。";
  if (actorId === "z")
    return " 根据Z最终生成的台词选择顶层字段expression。可用值为" +
      Z_PORTRAIT_EXPRESSIONS.join("、") +
      "和neutral。Z真的笑出声或答应当场微笑时填happy。玩家请求、否定、纠正、引用他人都不能强制触发表情。Z明确拒绝哭时用neutral。";
  if (actorId === "liyuan")
    return " 根据梨园最终生成的台词选择顶层字段expression。可用值为neutral、smile、bawl。梨园平时说话嘴角保持平直，默认用neutral；只有梨园自己确实带笑或当场答应微笑时才用smile，真正情绪性哭泣才用bawl。若玩家邀请双方排练，梨园可以自己决定接不接戏；只有他确实接戏并在戏中演出放声哭时才用bawl，演出微笑才用smile，拒绝、说哭不出来或不接戏都保持neutral。项目没有独立的大笑状态，不能把玩家说的大笑或模型的laugh/laughing降级成smile；未明确演出就用neutral。bawl_dry与bawl_tears是Unity层负责的进入与落泪阶段，服务端只输出bawl。排练和假想剧情只服务本次台词，不要写进info、memory_updates或knowledge_updates，也不要升级成世界事实。该字段只描述本次梨园台词后的头像表现，不是action/info的一部分。只根据梨园最终生成的台词和确实听到的上下文选择；玩家请求、否定、纠正、引号或第三方提到笑/哭的词不能强制表情，明确拒绝或否认时不要误选smile或bawl。省略或neutral表示回到中性。其他角色不要给expression。";
  if (actorId === "liting")
    return " 根据李庭最终生成的台词选择顶层字段expression。可用值为neutral、smile、surprise、starry_curious、troubled、blush、angry、disappointed、cry、bawl、gloom、sigh、wink、laugh。服务端兼容明确别名，但模型优先输出这些游戏语义键；疑惑用troubled（curious仅为兼容别名），微笑用smile，大哭用bawl，叹气用sigh。bawl_dry与sigh_in只是Unity层负责的进入阶段，不要输出这两个阶段键。该字段只描述本次李庭台词后的头像表现，不是action/info的一部分。只根据李庭最终生成的台词和确实听到的上下文选择；玩家请求、否定、纠正、引号或第三方提到的词不能强制表情，明确拒绝或否认时不要误选哭、大哭或叹气。省略或neutral表示回到中性。其他角色不要给expression。";
  if (actorId === "linxu")
    return " 根据留洋最终生成的台词选择顶层字段expression。当前 c01 实际资源只允许neutral、smile、bawl、troubled、sigh。发现异常、需要追问或判断未定时用troubled；事情反复出问题、计划受阻，留洋本人出现短暂无奈、疲惫或认命但仍继续处理时用sigh。留洋亲口承认自己累了、想暂时歇口气，随后又交代一件正事，整句仍可用sigh。只讨论故障原因时可用neutral或troubled，玩家说累不替留洋决定；微笑用smile，大哭用bawl。哭泣的闭嘴、起泪与叹气的前后阶段由Unity按已审模型时序控制，服务端不要输出阶段键。惊讶、星星眼、生气、害羞、失落和黑化尚未绑定，不能输出这些待设计状态。该字段只描述本次留洋台词后的头像表现，不是action/info的一部分。只根据留洋最终生成的台词和确实听到的上下文选择；玩家请求、否定、纠正、引号或第三方提到的词不能强制触发表情，明确拒绝或否认时不要误选哭、大哭或叹气。省略或neutral表示回到中性。其他角色不要给expression。";
  if (actorId === "zhengyi")
    return " 根据正义最终生成的台词选择顶层字段expression。可用值为neutral、smile、surprise、shock、puzzled、curious、angry、shy、bawl。surprise与shock是两个独立状态，不能互换；puzzled表示疑惑，curious表示好奇，smile表示含笑，bawl表示大哭。bawl的闭眼与起泪由Unity按时序控制，不要输出bawl_dry或bawl_tears_preview阶段键。只根据正义最终生成的台词和确实听到的上下文选择；玩家请求、否定、纠正、引号或第三方提到的词不能强制表情，明确拒绝哭时不要选bawl。省略或neutral表示回到中性。其他角色不要给expression。";
  if (actorId === "xiaoxiaoqing")
    return " 根据晴野最终生成的台词选择顶层字段expression。可用值为neutral、smile、bawl、surprise、shock、question、starry_curious、angry。微笑用smile，大哭用bawl，惊讶用surprise，震惊用shock，疑惑用question，好奇星星眼用starry_curious，生气用angry。该字段只描述本次晴野台词后的头像表现，不是action/info的一部分。只根据晴野最终生成的台词和确实听到的上下文选择；玩家请求、否定、纠正、引号或第三方提到的词不能强制触发表情。省略或neutral表示回到中性。";
  return " 不要输出expression或其他头像表现字段。";
}

/**
 * Normalize the optional model-side portrait field into the allowlisted
 * top-level HTTP metadata. The inner V1.2 output remains dialogue/action/info.
 */
export function portraitExpression(actorId, value, line = "") {
  if (actorId === "zhou_bobo") return normalizeZhouBoboPortraitExpression(value, line);
  if (actorId === "axing") return AXING_EXPRESSIONS.includes(value) ? value : "neutral";
  if (!supportsPortraitExpression(actorId)) return null;
  if (actorId === "xiaoxiaoqing") {
    const normalized =
      typeof value === "string"
        ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
        : "";
    if (!normalized || normalized === "neutral") return "neutral";
    const canonical =
      QINGYE_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized;
    if (!QINGYE_EXPRESSION_SET.has(canonical)) return "neutral";
    if (canonical === "bawl" && negatedCue(String(line).toLowerCase(), CRY_CUES))
      return "neutral";
    return canonical;
  }
  const liting = actorId === "liting";
  const liyuan = actorId === "liyuan";
  const c01Model = actorId === "linxu";
  const zhengyi = actorId === "zhengyi";
  if (zhengyi) {
    const normalized =
      typeof value === "string"
        ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
        : "";
    if (!normalized || normalized === "neutral") return normalized === "neutral" ? "neutral" : null;
    const canonical = ZHENGYI_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized;
    if (!ZHENGYI_EXPRESSION_SET.has(canonical)) return null;
    if (canonical === "bawl" && negatedCue(String(line).toLowerCase(), CRY_CUES)) return "neutral";
    return canonical;
  }
  if (c01Model) {
    const normalized =
      typeof value === "string"
        ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
        : "";
    if (!normalized || normalized === "neutral") return "neutral";
    const canonical =
      LIUYANG_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized;
    if (!LIUYANG_EXPRESSION_SET.has(canonical)) return "neutral";
    const outputLine = String(line).toLowerCase();
    if (
      (canonical === "cry" || canonical === "bawl") &&
      negatedCue(outputLine, CRY_CUES)
    )
      return "neutral";
    if (canonical === "sigh" && negatedCue(outputLine, SIGH_CUES))
      return "neutral";
    return canonical;
  }
  const fallback = () =>
    liting
      ? inferLitingPortraitExpression(line)
      : liyuan
        ? inferLiyuanPortraitExpression(line)
        : inferPortraitExpression(line);
  const normalized =
    typeof value === "string"
      ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
      : "";
  if (liyuan) {
    if (!normalized) return fallback();
    const canonical = LIYUAN_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized;
    if (!LIYUAN_EXPRESSION_SET.has(canonical)) return "neutral";
    const outputLine = String(line).toLowerCase();
    if (
      canonical === "smile" &&
      (refusesLiyuanPerformance(outputLine, "(?:笑|微笑)") || refusesRoleplay(outputLine))
    )
      return "neutral";
    if (
      canonical === "bawl" &&
      (refusesLiyuanPerformance(outputLine, "(?:哭|大哭|痛哭|落泪|流泪)") || refusesRoleplay(outputLine))
    )
      return "neutral";
    return canonical;
  }
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
