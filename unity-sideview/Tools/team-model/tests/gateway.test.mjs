import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createGateway } from "../server.mjs";
import { TeamClient } from "../client.mjs";

const profile = { provider: "deepseek", baseUrl: "https://api.deepseek.com", model: "team-fixed-model", apiType: "chat", apiKey: "fixture-model-credential" };
function fixture(t, overrides = {}) {
  let clock = Date.now(), permitted = true, unavailable = false, user = { id: 17, login: "member" };
  const requests = [];
  const fake = async (url, options = {}) => {
    requests.push({ url, options });
    if(unavailable && url.startsWith("https://api.github.com/")) throw Error("offline");
    if (url === "https://github.com/login/device/code") return Response.json({ device_code: "upstream-device-private", user_code: "ABCD-EFGH", verification_uri: "https://github.com/login/device", expires_in: 900, interval: 5 });
    if (url === "https://github.com/login/oauth/access_token") return Response.json({ access_token: "upstream-github-private" });
    if (url === "https://api.github.com/user") { assert.equal(options.headers.Authorization, "Bearer upstream-github-private"); return Response.json(user); }
    if (url === "https://api.github.com/applications/fixture-client/token") {
      assert.equal(options.headers.Authorization,"Basic "+Buffer.from("fixture-client:fixture-client-secret").toString("base64"));
      const supplied=JSON.parse(options.body).access_token;
      return Response.json({app:{client_id:supplied==='foreign-app-token'?'different-app':'fixture-client'},scopes:supplied==='overprivileged-token'?['repo']:[],user});
    }
    if (url.endsWith("/collaborators/member")) return new Response(null, { status: permitted ? 204 : 404 });
    if (url.endsWith("/collaborators/member/permission")) return Response.json({ permission: "read", user: { id: 17, login: "member" } });
    if (["https://api.deepseek.com/chat/completions", "https://ark-cn-beijing.bytedance.net/api/v3/responses"].includes(url)) return overrides.model ? overrides.model(url, options) : Response.json({ choices: [{ message: { content: "接口协议测试" }, finish_reason: "stop" }] });
    throw Error("Unexpected fixture URL: " + url);
  };
  const config = { repository: "Yiiime/Project-Z", github: { clientId: "fixture-client", clientSecret:"fixture-client-secret", membershipToken: "fixture-membership-credential" }, profiles: { npc: profile }, ...overrides.config };
  const app = createGateway({ config, fetchImpl: fake, now: () => clock, allowHttpForTests: true, ...(overrides.dbPath ? { dbPath: overrides.dbPath } : {}) });
  t.after(() => { if(!app.server.listening)return;app.server.closeAllConnections(); return new Promise(resolve => app.server.close(resolve)); });
  const listen = () => new Promise(resolve => app.server.listen(0, "127.0.0.1", resolve));
  const post = async (route, body = {}, token, extra = {}) => fetch(`http://127.0.0.1:${app.server.address().port}${route}`, { method: "POST", headers: { "Content-Type": "application/json", ...(token ? { Authorization: "Bearer " + token } : {}), ...extra }, body: JSON.stringify(body) });
  const login = async () => { const start = await (await post("/auth/start")).json(); clock += 5100; const response = await post("/auth/poll", { id: start.id }); return { start, response, result: await response.json() }; };
  return { app, config, requests, listen, post, login, oauthFetch:fake, now:()=>clock, advance: ms => clock += ms, remove: () => permitted = false, outage: () => unavailable = true, rename: () => user = { id: 18, login: "member" } };
}

test("production startup requires TLS and private GitHub configuration", () => {
  assert.throws(() => createGateway({ config: {} }), /HTTPS/);
});

test("only a verified project member receives a token; model credentials stay at the gateway", async t => {
  const f = fixture(t); await f.listen();
  const { start, result, response } = await f.login();
  assert.equal(response.status, 200);
  assert.equal(new URLSearchParams(f.requests.find(x=>x.url.endsWith('/login/device/code')).options.body).has('scope'),false);
  assert.equal(start.device_code, undefined);
  assert.equal(result.access_token, undefined);
  assert.match(result.token, /^[\w-]{43}$/);
  const record = f.app.auth.db.prepare("SELECT * FROM sessions").get();
  assert.notEqual(record.digest, result.token);
  const status = await (await f.post("/auth/status", {}, result.token)).json();
  assert.equal(status.profiles.npc.apiKey, undefined);
  const r = await f.post("/v1/npc", { model: "attacker-model", messages: [{ role: "user", content: "hello" }], baseUrl: "https://attacker.invalid", apiKey: "client-key", max_tokens: 100 }, result.token);
  assert.equal(r.status, 200);
  const sent = f.requests.find(x => x.url === "https://api.deepseek.com/chat/completions");
  assert.equal(sent.options.headers.Authorization, "Bearer fixture-model-credential");
  assert.equal(sent.options.redirect, "error");
  assert.deepEqual(JSON.parse(sent.options.body), { model: "team-fixed-model", messages: [{ role: "user", content: "hello" }], max_tokens: 100 });
});

test("anonymous callers and cross-origin requests cannot consume the model", async t => {
  const f = fixture(t); await f.listen();
  assert.equal((await f.post("/v1/npc", { login: "member" })).status, 401);
  assert.equal((await f.post("/auth/start", {}, undefined, { Origin: "https://attacker.invalid" })).status, 403);
  assert.equal(f.requests.length, 0);
});

test("outsiders and reassigned GitHub usernames fail membership checks", async t => {
  const f = fixture(t); await f.listen(); f.remove();
  assert.equal((await f.login()).response.status, 403);
  assert.equal(f.app.auth.db.prepare("SELECT count(*) AS n FROM sessions").get().n, 0);
  const other = fixture(t); await other.listen(); other.rename();
  assert.equal((await other.login()).response.status, 403);
});

test("membership is rechecked within one minute and all removed member sessions are revoked", async t => {
  const f = fixture(t); await f.listen();
  const a = (await f.login()).result, b = (await f.login()).result;
  f.remove(); f.advance(60_001);
  assert.equal((await f.post("/auth/status", {}, a.token)).status, 403);
  assert.equal((await f.post("/auth/status", {}, b.token)).status, 401);
  assert.equal(f.app.auth.db.prepare("SELECT count(*) AS n FROM sessions").get().n, 0);
});

test("polling honors GitHub's interval and stale login requests expire", async t => {
  const f = fixture(t); await f.listen();
  const start = await (await f.post("/auth/start")).json();
  assert.equal((await (await f.post("/auth/poll", { id: start.id })).json()).pending, true);
  assert.equal(f.requests.some(x => x.url.includes("access_token")), false);
  f.advance(901_000);
  assert.equal((await f.post("/auth/poll", { id: start.id })).status, 401);
});

test("upstream errors do not expose upstream response bodies or credentials", async t => {
  const f = fixture(t, { model: async () => Response.json({ secret: "fixture-model-credential" }, { status: 401 }) }); await f.listen();
  const { token } = (await f.login()).result;
  const r = await f.post("/v1/npc", { messages: [{ role: "user", content: "hello" }] }, token);
  assert.equal(r.status, 502);
  assert.doesNotMatch(await r.text(), /fixture-model-credential/);
});

test("daily limits persist in the database and logout invalidates the credential", async t => {
  const f = fixture(t, { config: { dailyLimit: 1 } }); await f.listen();
  const { token } = (await f.login()).result;
  const input = { messages: [{ role: "user", content: "hello" }] };
  assert.equal((await f.post("/v1/npc", input, token)).status, 200);
  assert.equal((await f.post("/v1/npc", input, token)).status, 429);
  assert.equal((await f.post("/auth/logout", {}, token)).status, 200);
  assert.equal((await f.post("/auth/status", {}, token)).status, 401);
});

test("concurrency is bounded while an upstream response is in flight", async t => {
  let unblock, entered;
  const started = new Promise(resolve => entered = resolve);
  const f = fixture(t, { config: { maxConcurrent: 1 }, model: async () => { entered(); await new Promise(resolve => unblock = resolve); return Response.json({ ok: true }); } }); await f.listen();
  const { token } = (await f.login()).result;
  const input = { messages: [{ role: "user", content: "hello" }] };
  const first = f.post("/v1/npc", input, token);
  await started;
  assert.equal((await f.post("/v1/npc", input, token)).status, 429);
  unblock(); assert.equal((await first).status, 200);
});

test("local client stores only a scoped service credential and clears it on revoked membership", async t => {
  const directory = mkdtempSync(join(tmpdir(), "z-team-client-"));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const f = fixture(t); await f.listen();
  const config = { origin: `http://127.0.0.1:${f.app.server.address().port}`, repository: "Yiiime/Project-Z", profiles: { npc: { ...profile, apiKey: undefined } } };
  const client = new TeamClient(config, { sessionDirectory: directory, oauthFetch:f.oauthFetch, now:f.now, allowHttpForTests: true });
  const start = await client.startLogin(); assert.equal(start.id, undefined);assert.equal(start.deviceCode,undefined);
  f.advance(5100); assert.equal((await client.pollLogin()).connected, true);
  assert.doesNotMatch(readFileSync(client.sessionFile, "utf8"), /fixture-model-credential|upstream-github-private/);
  const remote = new TeamClient({ ...config, origin: "http://127.0.0.1:1" }, { sessionDirectory: directory, allowHttpForTests: true });
  assert.equal(remote.session(), null);
  f.remove(); f.advance(60_001);
  await assert.rejects(client.verify(), /没有项目使用权限/);
  assert.equal(client.session(), null);
});

test("tokens issued to another application or with broader scopes cannot become team sessions", async t=>{
  const f=fixture(t);await f.listen();
  for(const accessToken of ['foreign-app-token','overprivileged-token'])assert.equal((await f.post('/auth/exchange',{accessToken})).status,401);
  assert.equal(f.app.auth.db.prepare('SELECT count(*) AS n FROM sessions').get().n,0);
});

test("GitHub failure after the cache expires does not grant continued model access", async t => {
  const f=fixture(t); await f.listen();const {token}=(await f.login()).result;
  f.outage();f.advance(60_001);
  assert.equal((await f.post("/v1/npc",{messages:[{role:"user",content:"hello"}]},token)).status,503);
  assert.equal(f.requests.some(r=>r.url.includes("api.deepseek.com")),false);
});

test("Ark Responses streaming uses the configured endpoint and disables provider storage", async t => {
  const ark={provider:"ark",baseUrl:"https://ark-cn-beijing.bytedance.net/api/v3",model:"ep-fixture",apiType:"responses",apiKey:"fixture-ark-credential"};
  const stream='data: {"type":"response.output_text.delta","delta":"你好"}\n\ndata: {"type":"response.completed","response":{"status":"completed","output":[]}}\n\n';
  const f=fixture(t,{config:{profiles:{npc:ark}},model:async(url,options)=>{
    assert.equal(url,"https://ark-cn-beijing.bytedance.net/api/v3/responses");
    const body=JSON.parse(options.body);assert.equal(body.store,false);assert.equal(body.model,"ep-fixture");assert.equal(body.stream,true);
    return new Response(stream,{headers:{"Content-Type":"text/event-stream"}});
  }});await f.listen();const {token}=(await f.login()).result;
  const response=await f.post("/v1/npc",{input:[{role:"user",content:"hello"}],stream:true,store:true},token);
  assert.equal(response.status,200);assert.equal(await response.text(),stream);
});

test("service sessions survive restart without persisting GitHub tokens", async t => {
  const directory=mkdtempSync(join(tmpdir(),"z-team-db-"));
  const dbPath=join(directory,"sessions.sqlite");const first=fixture(t,{dbPath});await first.listen();
  const {token}=(await first.login()).result;
  first.app.server.closeAllConnections();await new Promise(resolve=>first.app.server.close(resolve));
  const second=fixture(t,{dbPath});await second.listen();
  t.after(()=>rmSync(directory,{recursive:true,force:true}));
  assert.equal((await second.post("/auth/status",{},token)).status,200);
  assert.doesNotMatch(readFileSync(dbPath).toString("utf8"),/upstream-github-private|upstream-device-private/);
});
