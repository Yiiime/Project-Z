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

const DIRECT_LAUGH_CUES = [
  "(?:^|[，,。.!！?？、\\s])(?:那(?:我)?就|我这就|那我来|这就)(?:给你)?笑(?:一下|一个|给你看)",
  "^(?:哈哈+|呵呵+|嘿嘿+|嘻嘻+)(?:[，,。.!！?？、\\s]|$)",
  "(?:^|[，,。.!！?？、\\s])(?:好|好吧|可以|行|那我|我来|我先)[，,。.!！?？、\\s]{0,2}(?:我?[来先]?)?笑(?:一下|一个|给你看)?",
  "(?:^|[，,。.!！?？、\\s])我(?:来|先)?笑(?:一下|一个|给你看)?",
];

/**
 * Infer only from Z's own generated line. Player wording is deliberately not
 * passed here, so a request, denial, correction, or quoted third-party word
 * cannot force a portrait state.
 */
export function inferPortraitExpression(value) {
  const line = typeof value === "string" ? value.trim().toLowerCase().replace(/“[^”]*”|「[^」]*」|‘[^’]*’|"[^"]*"/g, "") : "";
  if (!line) return null;
  if (!negatedCue(line, ["笑"]) && hasCue(line, DIRECT_LAUGH_CUES))
    return "happy";
  return null;
}

/**
 * Normalize the optional model-side portrait field into the allowlisted
 * top-level HTTP metadata. The inner V1.2 output remains dialogue/action/info.
 */
export function portraitExpression(actorId, value, line = "") {
  if (actorId !== "z") return null;
  const fallback = () => inferPortraitExpression(line);
  const normalized =
    typeof value === "string"
      ? value.trim().toLowerCase().replace(/[\s-]+/g, "_")
      : "";
  if (!normalized || normalized === "neutral") return fallback();
  const canonical = Z_PORTRAIT_EXPRESSION_ALIASES[normalized] || normalized;
  if (!EXPRESSION_SET.has(canonical)) return fallback();
  if (canonical === "happy" && negatedCue(String(line), ["笑"])) return null;
  if (
    (canonical === "cry" || canonical === "bawl") &&
    negatedCue(String(line).toLowerCase(), CRY_CUES)
  )
    return fallback();
  return canonical;
}
