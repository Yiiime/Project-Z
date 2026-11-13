import http from "node:http";
import https from "node:https";
import { Readable } from "node:stream";
import { createHash, randomBytes } from "node:crypto";
import { readFileSync, writeFileSync, mkdirSync, renameSync, unlinkSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { homedir } from "node:os";
import { fileURLToPath } from "node:url";
import { GatewayError, profileEndpoint } from "./common.mjs";

const root = dirname(fileURLToPath(import.meta.url));

export function secureRequest(url, { body, token, certificate, signal, allowHttpForTests = false } = {}) {
  const target = new URL(url);
  const test = allowHttpForTests && target.protocol === "http:" && target.hostname === "127.0.0.1";
  if ((!test && (target.protocol !== "https:" || !certificate)) || target.username || target.password)
    throw new GatewayError("团队服务需要可信 HTTPS 连接", 503);
  return new Promise((resolvePromise, reject) => {
    const transport = test ? http : https;
    const req = transport.request(target, {
      method: "POST", ...(test ? {} : { ca: certificate }), signal,
      headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    }, response => {
      const headers = Object.fromEntries(Object.entries(response.headers).filter(([, value]) => typeof value === "string"));
      resolvePromise(new Response(Readable.toWeb(response), { status: response.statusCode, headers }));
    });
    req.on("error", reject);
    req.end(typeof body === "string" ? body : JSON.stringify(body ?? {}));
  });
}

export class TeamClient {
  constructor(config, { certificate, sessionDirectory = join(homedir(), ".project-z/team-model"), requestImpl = secureRequest, allowHttpForTests = false } = {}) {
    const origin = new URL(config.origin);
    const test = allowHttpForTests && origin.protocol === "http:" && origin.hostname === "127.0.0.1";
    if ((!test && origin.protocol !== "https:") || origin.username || origin.password || origin.pathname !== "/" || origin.search || origin.hash)
      throw new GatewayError("团队服务地址无效", 503);
    this.config = config;
    this.origin = origin.origin;
    this.certificate = certificate;
    this.requestImpl = requestImpl;
    this.allowHttpForTests = allowHttpForTests;
    this.directory = sessionDirectory;
    this.sessionFile = join(sessionDirectory, createHash("sha256").update(this.origin + ":" + config.repository).digest("hex") + ".json");
    this.login = null;
    for (const profile of Object.values(config.profiles || {})) profileEndpoint(profile);
  }
  session() {
    try {
      const saved = JSON.parse(readFileSync(this.sessionFile, "utf8"));
      if (saved.origin !== this.origin || saved.repository !== this.config.repository || saved.expires <= Date.now() || !/^[A-Za-z0-9_-]{43}$/.test(saved.token)) return null;
      return saved;
    } catch { return null; }
  }
  publicStatus() {
    const session = this.session();
    return { enabled: true, connected: Boolean(session), repository: this.config.repository, user: session?.user || null, loginPending: Boolean(this.login) };
  }
  save(value) {
    mkdirSync(this.directory, { recursive: true, mode: 0o700 });
    const temporary = this.sessionFile + "." + randomBytes(8).toString("hex");
    writeFileSync(temporary, JSON.stringify({ ...value, origin: this.origin, repository: this.config.repository }), { mode: 0o600, flag: "wx" });
    renameSync(temporary, this.sessionFile);
  }
  clear() { try { unlinkSync(this.sessionFile); } catch (error) { if (error.code !== "ENOENT") throw error; } }
  async request(path, body, { anonymous = false, signal } = {}) {
    const session = this.session();
    if (!anonymous && !session) throw new GatewayError("请先登录 GitHub 连接团队模型", 401, "login_required");
    const combined = signal ? AbortSignal.any([signal, AbortSignal.timeout(125_000)]) : AbortSignal.timeout(125_000);
    let response;
    try {
      response = await this.requestImpl(this.origin + path, { body, token: anonymous ? undefined : session.token, certificate: this.certificate, signal: combined, allowHttpForTests: this.allowHttpForTests });
    } catch {
      if (signal?.aborted) throw new GatewayError("操作已取消", 409, "cancelled");
      throw new GatewayError("无法连接团队模型服务，请检查内网连接", 502, "gateway_unavailable");
    }
    if (!anonymous && [401, 403].includes(response.status)) this.clear();
    return response;
  }
  async json(path, body, options) {
    const response = await this.request(path, body, options);
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new GatewayError(typeof data.error === "string" ? data.error : "团队服务请求失败", response.status, data.code);
    return data;
  }
  async startLogin() {
    if (this.login?.expires > Date.now()) return { ...this.login, id: undefined };
    this.login = await this.json("/auth/start", {}, { anonymous: true });
    return { ...this.login, id: undefined };
  }
  async pollLogin() {
    if (!this.login) return { pending: false, ...this.publicStatus() };
    try {
      const result = await this.json("/auth/poll", { id: this.login.id }, { anonymous: true });
      if (result.pending) return { pending: true, interval: result.interval };
      this.save(result);
      this.login = null;
      return { pending: false, ...this.publicStatus() };
    } catch (error) { if ([401, 403].includes(error.status)) this.login = null; throw error; }
  }
  async logout() {
    try { if (this.session()) await this.json("/auth/logout", {}); }
    finally { this.clear(); this.login = null; }
  }
  async verify() {
    const status = await this.json("/auth/status", {});
    if (status.repository !== this.config.repository) throw new GatewayError("团队服务与项目不匹配", 503);
    return status;
  }
  modelFetch(profileName, fallback = fetch) {
    const profile = this.config.profiles[profileName];
    if (!profile) throw new GatewayError("缺少团队模型配置", 503);
    const endpoint = profileEndpoint(profile);
    return async (url, options = {}) => {
      if (String(url) !== endpoint) return fallback(url, options);
      if (options.method !== "POST") throw new GatewayError("模型请求方法无效");
      // Never forward the caller's upstream API Key or arbitrary headers.
      return this.request("/v1/" + profileName, options.body, { signal: options.signal });
    };
  }
}

export function loadTeamClient(configPath = process.env.Z_TEAM_MODEL_CONFIG || join(root, "client.json")) {
  const config = JSON.parse(readFileSync(configPath, "utf8"));
  if (!config.enabled) return null;
  const certificate = readFileSync(resolve(dirname(configPath), config.certificateFile), "utf8");
  return new TeamClient(config, { certificate, ...(process.env.Z_TEAM_SESSION_DIR ? { sessionDirectory: process.env.Z_TEAM_SESSION_DIR } : {}) });
}
