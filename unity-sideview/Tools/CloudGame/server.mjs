import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import {DatabaseSync} from 'node:sqlite';
import {json} from '../LanPlay/protocol.mjs';
import {createCloudAuth} from './auth.mjs';
import {createGameAccess} from './business.mjs';

const origin = 'https://api.litingyi.com';
const credentials = process.env.CREDENTIALS_DIRECTORY;
if (!credentials) throw Error('systemd_credentials_required');
process.umask(0o077);
const state = process.env.STATE_DIRECTORY || '/var/lib/project-z-cloud';
const readCredential = name => fs.readFileSync(path.join(credentials, name));
const db = new DatabaseSync(path.join(state, 'cloud.sqlite'));
db.exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=3000;');
function allowedIds() {
  const values = JSON.parse(fs.readFileSync('/etc/project-z-cloud/members.json', 'utf8'));
  if (!Array.isArray(values) || values.some(id => typeof id !== 'string' || !/^\d{1,20}$/.test(id))) {
    throw Error('invalid_member_configuration');
  }
  return new Set(values);
}
allowedIds(); // A malformed or unreadable allowlist must fail closed at startup and after an edit.
const {clientId, clientSecret} = JSON.parse(readCredential('oauth.json'));
if (!clientId || !clientSecret) throw Error('github_configuration_required');
const auth = createCloudAuth({db, origin, clientId, clientSecret, allowedIds});
const game = createGameAccess({db, auth, host: JSON.parse(readCredential('game-host.json')), identityKey: readCredential('identity-key')});

const server = http.createServer({maxHeaderSize: 16384}, async (req, res) => {
  try {
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Referrer-Policy', 'no-referrer');
    if (req.headers.host !== 'api.litingyi.com') return json(res, 421, {error: 'invalid_host'});
    const raw = req.url;
    const mark = raw.indexOf('?');
    const pathname = mark < 0 ? raw : raw.slice(0, mark);
    if (!/^\/[A-Za-z0-9/-]+$/.test(pathname) || pathname.includes('//') || raw.length > 4096 || raw.includes('#') || (mark >= 0 && raw.indexOf('?', mark + 1) !== -1)) {
      return json(res, 404, {error: 'not_found'});
    }
    if (pathname.startsWith('/auth/')) {
      if (await auth.handle(req, res, pathname)) return;
      return json(res, 404, {error: 'not_found'});
    }
    if (req.headers.origin || !['GET', 'POST'].includes(req.method)) return json(res, 403, {error: 'forbidden'});
    await game(req, res);
  } catch (error) {
    if (res.headersSent) { res.destroy(); return; }
    const status = [400,401,403,404,409,410,413,415,429,503].includes(error.status) ? error.status : 503;
    const message = error.status === 413 ? 'body_too_large' : error.status === status && /^[a-zA-Z0-9_]{1,64}$/.test(error.message) ? error.message : 'service_unavailable';
    json(res, status, {error: message});
  }
});
server.headersTimeout = 10000;
server.requestTimeout = 30000;
server.maxHeadersCount = 50;
server.listen(49510, '127.0.0.1', () => console.log('Project Z cloud gateway listening on loopback.'));
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  server.close(() => { db.close(); });
  server.closeAllConnections();
});
