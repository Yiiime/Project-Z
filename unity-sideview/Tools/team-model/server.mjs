import http from "node:http";
import https from "node:https";
import { readFileSync, mkdirSync, chmodSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { once } from "node:events";
import { TeamAuth } from "./auth.mjs";
import { chatRequestToResponses, responseForProfile } from "./adapter.mjs";
import { GatewayError, json, profileEndpoint, publicProfile, readJson } from "./common.mjs";

export function createGateway({ config, dbPath = ":memory:", fetchImpl = fetch, now = Date.now, tls, allowHttpForTests = false }) {
  if (!tls && !allowHttpForTests) throw new GatewayError("团队服务必须使用 HTTPS", 503);
  const profiles = Object.fromEntries(Object.entries(config.profiles || {}).filter(([name]) => ["npc", "live"].includes(name)));
  for (const profile of Object.values(profiles)) {
    profileEndpoint(profile);
    if (!profile.apiKey || profile.apiKey.startsWith("REPLACE_") || /\s/.test(profile.apiKey)) throw new GatewayError("缺少服务端模型 Key", 503);
  }
  if (!profiles.npc) throw new GatewayError("缺少 NPC 模型配置", 503);
  const auth = new TeamAuth({ ...config.github, repository: config.repository, dbPath, fetchImpl, now });
  const starts = new Map(), active = new Map();
  let total = 0;
  const maxConcurrent = config.maxConcurrent ?? 8, perMember = config.perMemberConcurrent ?? 3, dailyLimit = config.dailyLimit ?? 5000;
  for (const value of [maxConcurrent, perMember, dailyLimit])
    if (!Number.isSafeInteger(value) || value < 1) throw new GatewayError("调用上限配置无效", 503);

  function limitLogin(req) {
    const minute = Math.floor(now() / 60_000), address = req.socket.remoteAddress;
    for (const [key, value] of starts) if (value.minute !== minute) starts.delete(key);
    const bucket = starts.get(address) || { minute, count: 0 };
    if (++bucket.count > 12) throw new GatewayError("登录请求过于频繁，请稍后重试", 429, "rate_limited");
    starts.set(address, bucket);
  }

  async function model(req, res, user, profile, input) {
    if (total >= maxConcurrent || (active.get(user.id) || 0) >= perMember)
      throw new GatewayError("团队模型繁忙，请稍后重试", 429, "rate_limited");
    const responses = (profile.requestApiType||profile.apiType) === "responses";
    const messages = responses ? input.input : input.messages;
    if (!Array.isArray(messages) || messages.length < 1 || messages.length > 200 || messages.some(item => !item || !["system", "user", "assistant", "developer"].includes(item.role) || typeof item.content !== "string"))
      throw new GatewayError("对话内容格式无效");
    if (input.stream !== undefined && typeof input.stream !== "boolean") throw new GatewayError("流式选项无效");
    const requested = responses ? input.max_output_tokens : input.max_tokens;
    if (requested !== undefined && (!Number.isInteger(requested) || requested < 1 || requested > 8192)) throw new GatewayError("输出长度超出团队调用范围");
    const permitted = responses
      ? ["input", "temperature", "max_output_tokens", "stream", "thinking", "text"]
      : ["messages", "temperature", "max_tokens", "stream", "thinking", "response_format", "stream_options"];
    let body = Object.fromEntries(permitted.filter(key => input[key] !== undefined).map(key => [key, input[key]]));
    body.model = profile.model;
    if (responses) { body.store = false; body.max_output_tokens ??= 2048; }
    else body.max_tokens ??= 2048;
    if(profile.apiType==='responses'&&!responses)body=chatRequestToResponses(body);
    auth.charge(user, dailyLimit);
    total++;
    active.set(user.id, (active.get(user.id) || 0) + 1);
    const controller = new AbortController();
    const cancel = () => controller.abort();
    res.on("close", cancel);
    const timer = setTimeout(cancel, 120_000);
    try {
      let upstream = await fetchImpl(profileEndpoint(profile), {
        method: "POST", redirect: "error", signal: controller.signal,
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${profile.apiKey}` },
        body: JSON.stringify(body),
      });
      if (!upstream.ok) {
        await upstream.body?.cancel();
        throw new GatewayError(upstream.status === 429 ? "模型服务限流，请稍后重试" : "团队模型暂时不可用，请联系维护者", upstream.status === 429 ? 429 : 502, "model_unavailable");
      }
      upstream=await responseForProfile(upstream,profile);
      const contentType = upstream.headers.get("content-type") || "";
      if (!/^(application\/json|text\/event-stream)(?:;|$)/i.test(contentType) || !upstream.body)
        throw new GatewayError("模型服务返回格式异常", 502, "model_unavailable");
      res.writeHead(200, { "Content-Type": contentType, "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", "X-Accel-Buffering": "no" });
      let size = 0;
      for await (const chunk of upstream.body) {
        size += chunk.length;
        if (size > 2_000_000 || controller.signal.aborted) throw new GatewayError("模型响应中断", 502);
        if (!res.write(chunk)) await once(res, "drain", { signal: controller.signal });
      }
      res.end();
    } catch (error) {
      if (res.headersSent || res.destroyed) res.destroy();
      else if (error instanceof GatewayError) throw error;
      else throw new GatewayError("团队模型连接失败，请稍后重试", 502, "model_unavailable");
    } finally {
      clearTimeout(timer);
      res.off("close", cancel);
      controller.abort();
      total--;
      const count = active.get(user.id) - 1;
      if (count) active.set(user.id, count); else active.delete(user.id);
    }
  }

  const handler = async (req, res) => {
    try {
      // Only native/local-service clients use this gateway. No browser CORS access.
      if (req.headers.origin) throw new GatewayError("不允许此请求来源", 403);
      if (req.method === "GET" && req.url === "/health") return json(res, 200, { ok: true, app: "project-z-team-model", protocol: 1 });
      if (req.method !== "POST") throw new GatewayError("接口不存在", 404);
      if (req.url === "/auth/config") { await readJson(req, 1024); return json(res, 200, { clientId: config.github.clientId, repository: config.repository }); }
      if (req.url === "/auth/exchange") { limitLogin(req); const input=await readJson(req,8192); return json(res,200,await auth.exchange(input.accessToken)); }
      if (req.url === "/auth/start") { limitLogin(req); await readJson(req, 1024); return json(res, 200, await auth.start()); }
      if (req.url === "/auth/poll") { const input = await readJson(req, 1024); return json(res, 200, await auth.poll(input.id)); }
      const user = await auth.authenticate(req.headers.authorization);
      const input = await readJson(req);
      if (req.url === "/auth/status") return json(res, 200, { user: { id: user.id, login: user.login }, expires:user.expires, repository: config.repository, profiles: Object.fromEntries(Object.entries(profiles).map(([name, profile]) => [name, publicProfile(profile)])) });
      if (req.url === "/auth/logout") { auth.logout(user); return json(res, 200, { ok: true }); }
      const name = /^\/v1\/(npc|live)$/.exec(req.url)?.[1];
      if (!name || !profiles[name]) throw new GatewayError("此模型入口未开放", 404);
      await model(req, res, user, profiles[name], input);
    } catch (error) {
      if (res.headersSent || res.destroyed) return res.destroy();
      const safe = error instanceof GatewayError ? error : new GatewayError("服务暂时不可用", 503, "service_unavailable");
      json(res, safe.status, { error: safe.message, code: safe.code });
    }
  };
  const server = tls ? https.createServer(tls, handler) : http.createServer(handler);
  server.requestTimeout = 130_000;
  server.headersTimeout = 15_000;
  server.once("close", () => auth.close());
  return { server, auth };
}

if (import.meta.url === pathToFileURL(resolve(process.argv[1] || "")).href) {
  const root = dirname(fileURLToPath(import.meta.url));
  const configPath = process.env.Z_TEAM_SERVER_CONFIG || resolve(root, ".private/server.json");
  const config = JSON.parse(readFileSync(configPath, "utf8"));
  const data = process.env.Z_TEAM_DATA || resolve(root, ".data");
  mkdirSync(data, { recursive: true, mode: 0o700 });
  chmodSync(data, 0o700);
  const privatePath = file => resolve(dirname(configPath), file);
  const app = createGateway({ config, dbPath: resolve(data, "sessions.sqlite"), tls: { key: readFileSync(privatePath(config.tlsKeyFile)), cert: readFileSync(privatePath(config.tlsCertFile)) } });
  const host = config.host || "10.37.20.9", port = config.port || 4338;
  app.server.listen(port, host, () => console.log(`Project Z team model listening on https://${host}:${port}`));
  for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, () => {
    app.server.close(() => process.exit(0));
    setTimeout(() => { app.server.closeAllConnections(); process.exit(0); }, 5000).unref();
  });
}
