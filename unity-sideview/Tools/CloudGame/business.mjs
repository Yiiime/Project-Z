import {createHmac} from 'node:crypto';
import https from 'node:https';
import {allowed, body, json, proxy} from '../LanPlay/protocol.mjs';
import {GameBudget} from './budget.mjs';

const uuid = '[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}';
const livePath = new RegExp('^/v1/live/sessions/(' + uuid + ')(?:/(events|inputs|receipts|control|heartbeat|asr|dictation|voice/config|artifacts/[a-f0-9]{64}|audio-stream/' + uuid + '))?$');
const memoryWork = new Set(['sync', 'intent', 'utterance', 'story/plan']);
const fail = (status, message) => Object.assign(new Error(message), {status});

// LanPlay is the authority for the internal routes. The public edge narrows it to the
// authenticated Unity protocol; the private registration, settings and lab UI stay private.
function gameRoute(req) {
  const [pathname, query] = req.url.split('?');
  const match = /^\/(memory|live)(\/[A-Za-z0-9/-]+)$/.exec(pathname);
  if (!match || !allowed(match[1], req.method, match[2])) return null;
  const [, surface, path] = match;
  let room, operation;
  if (surface === 'live' && path !== '/health' && path !== '/v1/live/sessions') {
    const parts = livePath.exec(path);
    if (!parts) return null;
    [, room, operation] = parts;
  }
  if (query !== undefined && !(operation === 'events' && req.method === 'GET' && /^afterSeq=\d{1,15}$/.test(query))) return null;
  return {surface, path, room, operation};
}

function admission(route, method, envelope, length) {
  const counts = {};
  let roomToRenew;
  if (method === 'POST') {
    if (route.surface === 'memory') {
      const action = route.path.slice('/api/game/'.length);
      if (memoryWork.has(action)) counts.generationRequests = 1;
      if (action === 'speech' || action === 'speech/stream') counts.speechRequests = 1;
    } else if (route.operation === 'asr' || route.operation === 'dictation') {
      counts.asrSeconds = Math.ceil(length / 32000);
    } else if (route.path === '/v1/live/sessions') {
      counts.rooms = 1;
      counts.generationRequests = 1;
    } else if (route.operation === 'heartbeat' || (route.operation === 'control' && envelope?.action === 'resume')) {
      roomToRenew = route.room;
      if (route.operation === 'control') counts.generationRequests = 1;
    } else if (route.operation === 'inputs' || (route.operation === 'control' && envelope?.action === 'retry_reply')) {
      counts.generationRequests = 1;
    }
  }
  return {counts, roomToRenew};
}

function readRegistration(url, invitation, certificate, identity) {
  return new Promise((resolve, reject) => {
    const registration = https.request(new URL('/register', url), {
      method: 'POST', ca: certificate, rejectUnauthorized: true,
      headers: {'content-type': 'application/json', authorization: 'Bearer ' + invitation},
      signal: AbortSignal.timeout(8000),
    }, response => {
      response.resume();
      if (response.statusCode === 200) resolve();
      else reject(fail(response.statusCode === 429 ? 429 : 503, response.statusCode === 429 ? 'host_capacity' : 'game_registration_unavailable'));
    });
    registration.once('error', () => reject(fail(503, 'game_registration_unavailable')));
    registration.end(JSON.stringify(identity));
  });
}

export function createGameAccess({db, auth, host, identityKey}) {
  const privateOrigin = new URL(host.serverUrl);
  // The parallel worker has its own member registry, save directory and metered providers.
  // Accidentally using the old SSH-only host must fail closed.
  if (privateOrigin.href !== 'https://127.0.0.1:49461/' || host.teamModel !== false
      || !/^[a-f0-9]{64}$/.test(host.invitation) || !host.certificate?.includes('BEGIN CERTIFICATE')
      || identityKey.length !== 32) {
    throw new Error('invalid_private_game_configuration');
  }
  const budgets = new GameBudget(db);
  const registered = new Map();

  async function playerFor(member) {
    if (registered.has(member)) return registered.get(member);
    const derive = scope => createHmac('sha256', identityKey).update('project-z/cloud/' + scope + '/' + member).digest('hex');
    const identity = {id: derive('player-id').slice(0, 32), secret: derive('player-secret')};
    const pending = readRegistration(privateOrigin, host.invitation, host.certificate, identity).then(() => identity.secret);
    registered.set(member, pending);
    try { return await pending; }
    catch (error) { registered.delete(member); throw error; }
  }

  return async (req, res) => {
    const route = gameRoute(req);
    if (!route) return json(res, 404, {error: 'not_found'});
    const member = auth.member(req);
    if (!member) return json(res, 401, {error: 'login_required'});
    const id = String(member.id);
    if (!/^\d{1,20}$/.test(id)) throw fail(403, 'member_invalid');
    const release = budgets.enter(id);
    let monitor;
    try {
      // Cloud phase 2 exposes regular microphone recognition. Never autoselect a protected
      // RTC session on the strength of the server's private RTC configuration.
      if (route.operation === 'voice/config') {
        return json(res, 200, {configured: false, requested: false});
      }
      const isPcm = route.operation === 'asr' || route.operation === 'dictation';
      let bytes, envelope;
      if (req.method === 'POST') {
        const type = (req.headers['content-type'] || '').split(';')[0].trim().toLowerCase();
        if (type !== (isPcm ? 'audio/pcm' : 'application/json')) throw fail(415, 'unsupported_content_type');
        bytes = await body(req, isPcm ? 640000 : route.surface === 'live' ? 32768 : 8_000_000);
        if (isPcm) {
          if (!bytes.length || bytes.length % 2) throw fail(400, 'invalid_pcm');
        } else {
          try { envelope = JSON.parse(bytes); } catch { throw fail(400, 'invalid_json'); }
          if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) throw fail(400, 'invalid_json');
        }
      }
      const secret = await playerFor(id);
      if (res.destroyed) return;
      if (!auth.isActive(member)) return json(res, 401, {error: 'login_required'});
      const {counts, roomToRenew} = admission(route, req.method, envelope, bytes?.length);
      budgets.charge(id, counts, roomToRenew);
      // A response may be a long-lived SSE/PCM stream. Revocation also cancels its
      // downstream/upstream pipes without waiting for the next Unity request.
      monitor = setInterval(() => {
        try { if (auth.isActive(member)) return; } catch { /* fail closed */ }
        res.destroy();
      }, 5000);
      monitor.unref();
      const target = new URL(req.url, privateOrigin);
      await proxy(req, res, target, {bytes, ca: host.certificate, headers: {authorization: 'Bearer ' + secret}});
    } finally {
      clearInterval(monitor);
      release();
    }
  };
}
