export class GatewayError extends Error {
  constructor(message, status = 400, code = "invalid_request") {
    super(message);
    this.status = status;
    this.code = code;
  }
}

export function profileEndpoint(profile) {
  const url = new URL(profile.baseUrl);
  const path = url.pathname.replace(/\/$/, "");
  const valid = profile.provider === "deepseek"
    ? url.hostname === "api.deepseek.com" && ["", "/v1"].includes(path) && profile.apiType === "chat"
    : profile.provider === "ark" && (url.hostname === "ark-cn-beijing.bytedance.net" || /^ark\.[a-z0-9-]+\.volces\.com$/.test(url.hostname)) && path === "/api/v3" && ["chat", "responses"].includes(profile.apiType);
  const requestType=profile.requestApiType||profile.apiType;
  if (!valid || !['chat','responses'].includes(requestType) || (requestType==='responses'&&profile.apiType!=='responses') || url.protocol !== "https:" || url.port || url.username || url.password || url.search || url.hash || !/^[\w.-]{1,160}$/.test(profile.model))
    throw new GatewayError("模型服务配置无效", 503, "configuration_error");
  return url.href.replace(/\/$/, "") + (profile.apiType === "responses" ? "/responses" : "/chat/completions");
}

export function publicProfile(profile) {
  profileEndpoint(profile);
  return {...Object.fromEntries(["provider", "baseUrl", "model", "apiType"].map(key => [key, profile[key]])),...(profile.requestApiType?{requestApiType:profile.requestApiType}:{})};
}

export async function readJson(req, limit = 400_000) {
  if (!/^application\/json(?:;|$)/i.test(req.headers["content-type"] || ""))
    throw new GatewayError("需要 JSON 请求", 415);
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > limit) throw new GatewayError("请求过大", 413);
    chunks.push(chunk);
  }
  try {
    const value = JSON.parse(Buffer.concat(chunks).toString("utf8"));
    if (!value || typeof value !== "object" || Array.isArray(value)) throw Error();
    return value;
  } catch { throw new GatewayError("JSON 请求无效"); }
}

export function json(res, status, body) {
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
  });
  res.end(JSON.stringify(body));
}
