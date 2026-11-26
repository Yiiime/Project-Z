// GitHub identifies the member. Only game-issued credentials cross the native client boundary.
import {createHash, randomBytes, randomInt} from 'node:crypto';
import {body, digest, equal, json} from '../LanPlay/protocol.mjs';

const opaque = () => randomBytes(32).toString('base64url');
const challengeFor = verifier => createHash('sha256').update(verifier).digest('base64url');
const validOpaque = value => typeof value === 'string' && /^[A-Za-z0-9_-]{43}$/.test(value);
const minute = 60_000;
const transactionTTL = 5 * minute;
const accessTTL = 15 * minute;
const refreshTTL = 30 * 24 * 60 * minute;

function fail(status, reason) { throw Object.assign(new Error(reason), {status, reason}); }

function page(res, status, title, content) {
  // Titles and markup are fixed strings. The pairing form below inserts only our random ID.
  res.writeHead(status, {'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store',
    'Referrer-Policy':'no-referrer','X-Content-Type-Options':'nosniff','X-Frame-Options':'DENY',
    'Content-Security-Policy':"default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self' https://github.com"});
  res.end('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Project-Z 登录</title><main><h1>' + title + '</h1>' + content + '</main></html>');
}

function redirect(res, location) {
  res.writeHead(303, {Location:location,'Cache-Control':'no-store','Referrer-Policy':'no-referrer'});
  res.end();
}

async function github(url, options) {
  try {
    const response = await fetch(url, {
      ...options, redirect:'error', signal:AbortSignal.timeout(10_000),
      headers:{Accept:'application/json','User-Agent':'Project-Z-Game','X-GitHub-Api-Version':'2022-11-28', ...options.headers},
    });
    if (response.ok) return await response.json();
  } catch { /* Do not include provider bodies, request URLs or headers in errors. */ }
  fail(503, 'github_unavailable');
}

function requestCookie(req, name) {
  const entry = (req.headers.cookie || '').split(';').map(part => part.trim()).find(part => part.startsWith(name + '='));
  return entry?.slice(name.length + 1);
}

function browserCookie(id) { return '__Host-pz-login-' + id.slice(0, 12); }

function setCookie(res, id, value, seconds) {
  res.setHeader('Set-Cookie', browserCookie(id) + '=' + value + '; Path=/; Secure; HttpOnly; SameSite=Lax; Max-Age=' + seconds);
}

async function readJson(req) {
  if (req.headers.origin || (req.headers['content-type'] || '').split(';')[0].toLowerCase() !== 'application/json')
    fail(415, 'json_required');
  const bytes = await body(req, 2048);
  let value;
  try { value = JSON.parse(bytes.toString('utf8')); } catch { fail(400, 'invalid_json'); }
  if (!value || typeof value !== 'object' || Array.isArray(value)) fail(400, 'invalid_json');
  return value;
}

export function createCloudAuth({db, origin, clientId, clientSecret, allowedIds}) {
  if (new URL(origin).origin !== origin || !origin.startsWith('https://') || typeof allowedIds !== 'function')
    throw new Error('invalid_cloud_auth_configuration');
  const callback = origin + '/auth/github/callback';
  const pending = new Map();
  const starts = new Map();
  db.exec(`
    PRAGMA foreign_keys=ON;
    CREATE TABLE IF NOT EXISTS cloud_sessions (
      session_id TEXT PRIMARY KEY, member_id TEXT NOT NULL, access_hash TEXT UNIQUE NOT NULL,
      refresh_hash TEXT UNIQUE NOT NULL, access_until INTEGER NOT NULL, refresh_until INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS cloud_sessions_member ON cloud_sessions(member_id);
    CREATE TABLE IF NOT EXISTS cloud_rotated_refresh (
      digest TEXT PRIMARY KEY, session_id TEXT NOT NULL REFERENCES cloud_sessions(session_id) ON DELETE CASCADE
    );
    CREATE INDEX IF NOT EXISTS cloud_rotated_session ON cloud_rotated_refresh(session_id);
  `);
  const revoke = db.prepare('DELETE FROM cloud_sessions WHERE member_id=?');
  function allowed(id) {
    if (allowedIds().has(id)) return true;
    revoke.run(id);
    return false;
  }

  function member(req) {
    const access = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(req.headers.authorization || '')?.[1];
    if (!access) return null;
    const row = db.prepare('SELECT session_id,member_id FROM cloud_sessions WHERE access_hash=? AND access_until>?').get(digest(access), Date.now());
    return row && allowed(row.member_id) ? {id:row.member_id,sessionId:row.session_id} : null;
  }

  // A stream survives access-token rotation, but its concrete device session must still exist.
  function isActive(identity) {
    if (!identity || !allowed(identity.id)) return false;
    return !!db.prepare('SELECT 1 FROM cloud_sessions WHERE session_id=? AND member_id=? AND refresh_until>?').get(identity.sessionId, identity.id, Date.now());
  }

  function start(req, res, input) {
    if (!clientId || !clientSecret) fail(503, 'oauth_not_configured');
    if (!validOpaque(input.challenge)) fail(400, 'invalid_challenge');
    const port = input.loopbackPort ?? 0;
    if (port !== 0 && (!Number.isInteger(port) || port < 1024 || port > 65535)) fail(400,'invalid_loopback_port');
    const now = Date.now();
    for (const [key, item] of pending) if (item.until <= now) pending.delete(key);
    for (const [key, bucket] of starts) if (bucket.until <= now) starts.delete(key);
    if (pending.size >= 100 || starts.size >= 1024) fail(429, 'login_busy');
    // nginx replaces X-Real-IP on its private upstream. Missing headers share one conservative bucket.
    const source = req.headers['x-real-ip'] || req.socket.remoteAddress;
    const bucket = starts.get(source) || {count:0,until:now + minute};
    starts.set(source,bucket);
    if (++bucket.count > 12) fail(429,'login_busy');

    const id = opaque();
    const userCode = String(randomInt(1_000_000)).padStart(6,'0');
    pending.set(id,{challenge:input.challenge, port, until:now + transactionTTL, userCode, attempts:0, stage:'pair'});
    json(res,200,{id, browserUrl:origin + '/auth/github/start?id=' + id, userCode});
  }

  function transaction(id) {
    const item = validOpaque(id) && pending.get(id);
    if (!item || item.until <= Date.now()) {
      pending.delete(id);
      fail(410,'login_expired');
    }
    return item;
  }

  function form(req, res, id) {
    const item = transaction(id);
    if (item.stage !== 'pair') fail(409,'login_already_started');
    const name = browserCookie(id);
    const old = requestCookie(req,name);
    if (!validOpaque(old) || !equal(digest(old),item.cookieHash || '')) {
      const value = opaque();
      item.cookieHash = digest(value);
      setCookie(res,id,value,transactionTTL / 1000);
    }
    // Browser privacy policies may send Origin: null on a form with no-referrer.
    const formToken = opaque();
    item.formTokenHash = digest(formToken);
    page(res,200,'连接 Project-Z','<p>请输入你正在使用的 Unity 游戏窗口显示的 6 位配对码。</p><form method="post" action="/auth/github/start"><input type="hidden" name="id" value="' + id + '"><input type="hidden" name="formToken" value="' + formToken + '"><label>配对码 <input name="userCode" type="text" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="off" required></label> <button>用 GitHub 登录</button></form>');
  }

  function boundBrowser(req,id,item) {
    const value = requestCookie(req,browserCookie(id));
    return validOpaque(value) && equal(digest(value),item.cookieHash || '');
  }

  async function authorize(req,res) {
    const submittedOrigin = req.headers.origin;
    if ((submittedOrigin && submittedOrigin !== 'null' && submittedOrigin !== origin) ||
        (req.headers['content-type'] || '').split(';')[0] !== 'application/x-www-form-urlencoded')
      fail(403,'browser_mismatch');
    const fields = new URLSearchParams((await body(req, 1024)).toString());
    const id = fields.get('id');
    const item = transaction(id);
    const formToken = fields.get('formToken');
    if (item.stage !== 'pair' || !boundBrowser(req,id,item) || !validOpaque(formToken) ||
        !equal(digest(formToken),item.formTokenHash || '')) fail(403,'browser_mismatch');
    if (!equal(fields.get('userCode') || '',item.userCode)) {
      if (++item.attempts >= 5) item.stage = 'denied';
      page(res,403,'配对码不匹配','<p>请返回 Unity 核对配对码；若已多次输错，请重新发起登录。</p>');
      return;
    }
    const nonce = opaque();
    item.stateHash = digest(nonce);
    item.verifier = opaque();
    item.stage = 'github';
    const target = new URL('https://github.com/login/oauth/authorize');
    target.search = new URLSearchParams({client_id:clientId,redirect_uri:callback,state:id + '.' + nonce,
      code_challenge:challengeFor(item.verifier),code_challenge_method:'S256',scope:'',allow_signup:'false'}).toString();
    redirect(res,target.href);
  }

  async function finish(req,res,url) {
    const fields = (url.searchParams.get('state') || '').split('.');
    if (fields.length !== 2 || !fields.every(validOpaque)) fail(403,'browser_mismatch');
    const [id,nonce] = fields;
    const item = transaction(id);
    if (item.stage !== 'github' || !equal(digest(nonce),item.stateHash) || !boundBrowser(req,id,item)) fail(403,'browser_mismatch');
    item.stage = 'exchanging';
    setCookie(res,id,'',0);
    try {
      const code = url.searchParams.get('code');
      if (url.searchParams.has('error') || !code || code.length > 512) fail(403,'authorization_denied');
      const grant = await github('https://github.com/login/oauth/access_token',{method:'POST',
        body:new URLSearchParams({client_id:clientId,client_secret:clientSecret,redirect_uri:callback,code,code_verifier:item.verifier})});
      if (grant.token_type?.toLowerCase() !== 'bearer' || typeof grant.access_token !== 'string' || !grant.access_token || (grant.scope || '').trim())
        fail(403,'authorization_denied');
      // Only the directly redeemed, app-bound code is accepted. No client-supplied GitHub tokens.
      const user = await github('https://api.github.com/user',{headers:{Authorization:'Bearer ' + grant.access_token}});
      if (!Number.isSafeInteger(user.id) || user.id < 1 || !allowed(String(user.id))) fail(403,'not_a_member');
      if (item.until <= Date.now()) fail(410,'login_expired');
      item.memberId = String(user.id);
      item.stage = 'ready';
      if (item.port) redirect(res,'http://127.0.0.1:' + item.port + '/project-z/complete');
      else page(res,200,'已登录 Project-Z','<p>请返回刚才的 Unity 游戏窗口继续。</p>');
    } catch (err) {
      item.stage = 'denied';
      throw err;
    } finally {
      delete item.verifier;
      delete item.stateHash;
      delete item.cookieHash;
    }
  }

  function issue(memberId) {
    const now = Date.now();
    db.prepare('DELETE FROM cloud_sessions WHERE refresh_until<=?').run(now);
    const count = db.prepare('SELECT count(*) AS n FROM cloud_sessions WHERE member_id=?').get(memberId).n;
    if (count >= 32) fail(429,'device_session_limit');
    const accessToken = opaque(), refreshToken = opaque();
    db.prepare('INSERT INTO cloud_sessions VALUES(?,?,?,?,?,?)').run(opaque(),memberId,digest(accessToken),digest(refreshToken),now + accessTTL,now + refreshTTL);
    return {memberId,accessToken,refreshToken,expiresIn:accessTTL / 1000};
  }

  function claim(res,input) {
    const item = transaction(input.id);
    if (!validOpaque(input.verifier) || !equal(challengeFor(input.verifier),item.challenge)) fail(403,'verifier_mismatch');
    if (item.stage === 'denied') { pending.delete(input.id); fail(403,'authorization_denied'); }
    if (item.stage !== 'ready') return json(res,202,{pending:true});
    pending.delete(input.id);
    if (!allowed(item.memberId)) fail(403,'not_a_member');
    json(res,200,issue(item.memberId));
  }

  function refresh(res,input) {
    if (!validOpaque(input.refreshToken)) fail(401,'login_required');
    const hash = digest(input.refreshToken);
    const row = db.prepare('SELECT * FROM cloud_sessions WHERE refresh_hash=? AND refresh_until>?').get(hash,Date.now() + 31_000);
    if (!row) {
      // Reuse of a previously spent refresh token revokes only this device session.
      db.prepare('DELETE FROM cloud_sessions WHERE session_id=(SELECT session_id FROM cloud_rotated_refresh WHERE digest=?)').run(hash);
      fail(401,'login_required');
    }
    if (!allowed(row.member_id)) fail(403,'not_a_member');
    const accessToken = opaque(), refreshToken = opaque();
    const accessUntil = Math.min(Date.now() + accessTTL,row.refresh_until);
    db.exec('BEGIN IMMEDIATE');
    try {
      const update = db.prepare('UPDATE cloud_sessions SET access_hash=?,refresh_hash=?,access_until=? WHERE session_id=? AND refresh_hash=?');
      if (update.run(digest(accessToken),digest(refreshToken),accessUntil,row.session_id,hash).changes !== 1) fail(401,'login_required');
      db.prepare('INSERT INTO cloud_rotated_refresh VALUES(?,?)').run(hash,row.session_id);
      db.prepare('DELETE FROM cloud_rotated_refresh WHERE session_id=? AND rowid NOT IN (SELECT rowid FROM cloud_rotated_refresh WHERE session_id=? ORDER BY rowid DESC LIMIT 256)').run(row.session_id,row.session_id);
      db.exec('COMMIT');
    } catch (err) { db.exec('ROLLBACK'); throw err; }
    json(res,200,{memberId:row.member_id,accessToken,refreshToken,expiresIn:Math.max(0,Math.floor((accessUntil-Date.now())/1000))});
  }

  async function handle(req,res,path) {
    const url = new URL(req.url,origin);
    path ??= url.pathname;
    if (!path.startsWith('/auth/')) return false;
    const browser = path === '/auth/github/start' || path === '/auth/github/callback';
    try {
      if (browser) {
        if (path === '/auth/github/start' && req.method === 'GET') form(req,res,url.searchParams.get('id'));
        else if (path === '/auth/github/start' && req.method === 'POST') await authorize(req,res);
        else if (path === '/auth/github/callback' && req.method === 'GET') await finish(req,res,url);
        else fail(405,'method_not_allowed');
      } else if (!['/auth/start','/auth/claim','/auth/refresh','/auth/logout'].includes(path)) fail(404,'not_found');
      else if (req.method !== 'POST') fail(405,'method_not_allowed');
      else if (path === '/auth/logout') {
        if (req.headers.origin) fail(403,'forbidden');
        const current = member(req);
        if (!current) fail(401,'login_required');
        db.prepare('DELETE FROM cloud_sessions WHERE session_id=?').run(current.sessionId);
        json(res,200,{ok:true});
      } else {
        const input = await readJson(req);
        if (path === '/auth/start') start(req,res,input);
        else if (path === '/auth/claim') claim(res,input);
        else refresh(res,input);
      }
    } catch (err) {
      const status = err.status || 503;
      if (browser) page(res,status,'Project-Z 登录未完成','<p>请返回 Unity 重新登录，或联系维护者确认账号已加入成员名单。</p>');
      else json(res,status,{error:err.reason || (status === 413 ? 'body_too_large' : 'auth_unavailable')});
    }
    return true;
  }

  return {handle,member,isActive};
}
