import { randomBytes, createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { GatewayError } from "./common.mjs";

const hash = value => createHash("sha256").update(value).digest("hex");
const secret = () => randomBytes(32).toString("base64url");
const loginPattern = /^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,38})$/;

export class TeamAuth {
  constructor({ repository, clientId, clientSecret, membershipToken, dbPath = ":memory:", fetchImpl = fetch, now = Date.now }) {
    if (!/^[\w.-]+\/[\w.-]+$/.test(repository || "") || !clientId || !clientSecret || !membershipToken || [clientId,clientSecret,membershipToken].some(value=>typeof value!=="string"||value.startsWith("REPLACE_")))
      throw new GatewayError("缺少 GitHub 团队授权配置", 503, "configuration_error");
    this.repository = repository;
    this.clientId = clientId;
    this.clientSecret = clientSecret;
    this.membershipToken = membershipToken;
    this.fetch = fetchImpl;
    this.now = now;
    this.pending = new Map();
    this.members = new Map();
    this.db = new DatabaseSync(dbPath);
    this.db.exec(`PRAGMA journal_mode=WAL;
      CREATE TABLE IF NOT EXISTS sessions (digest TEXT PRIMARY KEY, user_id INTEGER NOT NULL, login TEXT NOT NULL, expires INTEGER NOT NULL);
      CREATE TABLE IF NOT EXISTS usage (user_id INTEGER NOT NULL, day INTEGER NOT NULL, count INTEGER NOT NULL, PRIMARY KEY(user_id,day));`);
  }

  async github(url, { token, form, basic, payload } = {}) {
    let response;
    try {
      response = await this.fetch(url, {
        method: form || payload ? "POST" : "GET", redirect: "error", signal: AbortSignal.timeout(10000),
        headers: { Accept: "application/json", "User-Agent": "Project-Z-Team-Model", "X-GitHub-Api-Version": "2022-11-28", ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(basic ? { Authorization: `Basic ${basic}` } : {}), ...(form ? { "Content-Type": "application/x-www-form-urlencoded" } : {}), ...(payload ? { "Content-Type": "application/json" } : {}) },
        ...(form ? { body: new URLSearchParams(form).toString() } : payload ? { body: JSON.stringify(payload) } : {}),
      });
    } catch { throw new GatewayError("暂时无法核验 GitHub 身份，请稍后重试", 503, "github_unavailable"); }
    if (response.status === 204) return { status: 204, body: {} };
    const body = await response.json().catch(() => ({}));
    return { status: response.status, body };
  }

  async member(user, force = false) {
    const known = this.members.get(user.id);
    if (!force && known?.until > this.now() && known.login === user.login) return;
    if (!Number.isSafeInteger(user.id) || !loginPattern.test(user.login))
      throw new GatewayError("GitHub 身份无效", 403, "not_member");
    // Explicit membership remains required even if the repository becomes public.
    const route = `https://api.github.com/repos/${this.repository}/collaborators/${user.login}`;
    const [check,permission] = await Promise.all([this.github(route,{token:this.membershipToken}),this.github(route+"/permission",{token:this.membershipToken})]);
    if (check.status === 404) return this.deny(user.id);
    if (check.status !== 204) throw new GatewayError("暂时无法核验项目权限，请稍后重试", 503, "github_unavailable");
    if (permission.status === 404) return this.deny(user.id);
    if (permission.status !== 200) throw new GatewayError("暂时无法核验项目权限，请稍后重试", 503, "github_unavailable");
    if (permission.body.user?.id !== user.id || !["read", "write", "admin", "maintain", "triage"].includes(permission.body.permission))
      return this.deny(user.id);
    this.members.set(user.id, { login: user.login, until: this.now() + 60_000 });
  }

  deny(id) {
    this.db.prepare("DELETE FROM sessions WHERE user_id=?").run(id);
    this.members.delete(id);
    throw new GatewayError("此 GitHub 账号没有项目使用权限", 403, "not_member");
  }

  async exchange(accessToken) {
    if(typeof accessToken!=="string" || !accessToken || accessToken.length>4096 || /\s/.test(accessToken))
      throw new GatewayError("GitHub 登录凭据无效",401,"login_required");
    // Check the token's issuer with the application's own secret. A token issued
    // to another OAuth app must never become a Project Z service session.
    const identity=await this.github(`https://api.github.com/applications/${this.clientId}/token`,{
      basic:Buffer.from(`${this.clientId}:${this.clientSecret}`).toString("base64"),payload:{access_token:accessToken}
    });
    if([404,422].includes(identity.status))throw new GatewayError("GitHub 登录凭据无效或不属于此应用",401,"login_required");
    if(identity.status!==200)throw new GatewayError("暂时无法核验 GitHub 登录应用",503,"github_unavailable");
    if(identity.body.app?.client_id!==this.clientId || !Array.isArray(identity.body.scopes) || identity.body.scopes.filter(Boolean).length)
      throw new GatewayError("登录凭据不属于此应用或权限范围不匹配",401,"login_required");
    const user={id:identity.body.user?.id,login:identity.body.user?.login};
    await this.member(user,true);
    const token=secret(),expires=this.now()+30*86400_000;
    this.db.prepare("DELETE FROM sessions WHERE expires<=?").run(this.now());
    this.db.prepare("INSERT INTO sessions VALUES(?,?,?,?)").run(hash(token),user.id,user.login,expires);
    return {token,expires,user};
  }

  async start() {
    for (const [key, value] of this.pending) if (value.expires <= this.now()) this.pending.delete(key);
    if (this.pending.size >= 100) throw new GatewayError("登录请求较多，请稍后重试", 429, "rate_limited");
    const { status, body } = await this.github("https://github.com/login/device/code", { form: { client_id: this.clientId } });
    if (status !== 200 || !body.device_code || !body.user_code || body.verification_uri !== "https://github.com/login/device")
      throw new GatewayError("GitHub 登录尚未就绪，请联系项目维护者", 503, "github_unavailable");
    const id = secret(), interval = Math.max(5, Number(body.interval) || 5);
    const expires = this.now() + Math.min(900, Number(body.expires_in) || 900) * 1000;
    this.pending.set(hash(id), { code: body.device_code, expires, interval, next: this.now() + interval * 1000, busy: false });
    return { id, userCode: body.user_code, verificationUri: body.verification_uri, interval, expires };
  }

  async poll(id) {
    if (typeof id !== "string" || id.length > 100) throw new GatewayError("登录请求无效", 401, "login_expired");
    const digest = hash(id), state = this.pending.get(digest);
    if (!state || state.expires <= this.now()) {
      this.pending.delete(digest);
      throw new GatewayError("本次登录已过期，请重新登录", 401, "login_expired");
    }
    if (state.busy || state.next > this.now()) return { pending: true, interval: state.interval };
    state.busy = true;
    state.next = this.now() + state.interval * 1000;
    try {
      const result = await this.github("https://github.com/login/oauth/access_token", { form: { client_id: this.clientId, device_code: state.code, grant_type: "urn:ietf:params:oauth:grant-type:device_code" } });
      if (result.body.error === "authorization_pending") return { pending: true, interval: state.interval };
      if (result.body.error === "slow_down") {
        state.interval += 5;
        state.next = this.now() + state.interval * 1000;
        return { pending: true, interval: state.interval };
      }
      if (result.status !== 200 || !result.body.access_token) {
        this.pending.delete(digest);
        throw new GatewayError("GitHub 授权未完成，请重新登录", 401, "login_expired");
      }
      this.pending.delete(digest);
      return await this.exchange(result.body.access_token);
    } finally { state.busy = false; }
  }

  async authenticate(header) {
    const token = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(header || "")?.[1];
    const row = token && this.db.prepare("SELECT * FROM sessions WHERE digest=? AND expires>?").get(hash(token), this.now());
    if (!row) throw new GatewayError("请先登录 GitHub 连接团队模型", 401, "login_required");
    const user = { id: row.user_id, login: row.login };
    await this.member(user);
    return { ...user, digest: row.digest, expires: row.expires };
  }

  charge(user, limit = 5000) {
    const day = Math.floor(this.now() / 86400_000);
    this.db.prepare("DELETE FROM usage WHERE day<?").run(day - 2);
    const used = this.db.prepare("SELECT count FROM usage WHERE user_id=? AND day=?").get(user.id, day)?.count || 0;
    if (used >= limit) throw new GatewayError("今日团队模型调用次数已达上限", 429, "daily_limit");
    this.db.prepare("INSERT INTO usage VALUES(?,?,1) ON CONFLICT(user_id,day) DO UPDATE SET count=count+1").run(user.id, day);
  }
  logout(user) { this.db.prepare("DELETE FROM sessions WHERE digest=?").run(user.digest); }
  close() { this.db.close(); }
}
