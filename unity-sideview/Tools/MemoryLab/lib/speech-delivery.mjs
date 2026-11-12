// Presentation metadata only. Never infer delivery from emotion, expression or text.
export const MAX_DELIVERY_CHARS = 1000; // Application budget, not a provider limit.
export function speechDelivery(value) {
  if (value == null || value === "" || (typeof value === "string" && !value.trim()))
    return { delivery: "", warning: "" };
  if (typeof value !== "string" || [...value].length > MAX_DELIVERY_CHARS || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/u.test(value))
    return { delivery: "", warning: "tts_delivery_invalid" };
  return { delivery: value, warning: "" };
}
