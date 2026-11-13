import https from 'node:https';
import fs from 'node:fs';
import {allowed, body, digest, equal, json, proxy} from './protocol.mjs';

export function createGateway({key, cert, invitation, registryFile, backend, maxPlayers = 6, verifyMember = null}) {
  let registry = {};
  if (fs.existsSync(registryFile)) registry = JSON.parse(fs.readFileSync(registryFile, 'utf8'));
  const active = new Map();
  const save = () => {
    fs.writeFileSync(registryFile + '.tmp', JSON.stringify(registry, null, 2), {mode: 0o600});
    fs.renameSync(registryFile + '.tmp', registryFile);
  };
  const server = https.createServer({key, cert, minVersion: 'TLSv1.2'}, async (req, res) => {
    try {
      if (req.headers.origin || !req.url.startsWith('/') || req.url.startsWith('//')) return json(res, 403, {error: 'forbidden'});
      const url = new URL(req.url, 'https://gateway.local');
      if (url.pathname === '/register' && req.method === 'POST') {
        if (!equal(req.headers.authorization, 'Bearer ' + invitation)) return json(res, 401, {error: 'unauthorized'});
        const member=verifyMember?await verifyMember(req.headers['x-z-team-token']):null;
        if(verifyMember&&!member)return json(res,403,{error:'team_login_required'});
        const value = JSON.parse((await body(req, 2048)).toString());
        if (!/^[a-f0-9]{32}$/.test(value.id) || !/^[a-f0-9]{64}$/.test(value.secret)) return json(res, 400, {error: 'invalid_identity'});
        const hash = digest(value.secret), old = registry[value.id];
        if (old && (old.disabled || !equal(old.secretHash, hash))) return json(res, 403, {error: 'identity_rejected'});
        if(old?.githubId && old.githubId!==member?.user.id)return json(res,403,{error:'identity_rejected'});
        if (!old) {
          if (Object.keys(registry).length >= maxPlayers) return json(res, 429, {error: 'player_limit', message: '试玩名额已满，请联系主机管理员。'});
          registry[value.id] = {secretHash: hash, createdAt: new Date().toISOString()}; save();
        }
        if(member){registry[value.id].githubId=member.user.id;registry[value.id].githubLogin=member.user.login;save();}
        return json(res, 200, {ok: true, protocol: 1, playerId: value.id});
      }
      const credential = (req.headers.authorization || '').replace(/^Bearer /, '');
      const player = /^[a-f0-9]{64}$/.test(credential) && Object.keys(registry).find(id => !registry[id].disabled && equal(registry[id].secretHash, digest(credential)));
      if (!player) return json(res, 401, {error: 'unauthorized'});
      const member=verifyMember?await verifyMember(req.headers['x-z-team-token']):null;
      if(verifyMember&&(!member||registry[player].githubId!==member.user.id))return json(res,403,{error:'team_login_required'});
      delete req.headers['x-z-team-token'];
      const match = url.pathname.match(/^\/(memory|live)(\/.*)$/);
      if (!match || !allowed(match[1], req.method, match[2])) return json(res, 403, {error: 'route_forbidden'});
      const load = active.get(player) || {count: 0, since: Date.now(), requests: 0};
      if (Date.now() - load.since > 60000) {load.since = Date.now(); load.requests = 0;}
      if (load.count >= 64 || ++load.requests > 2400) return json(res, 429, {error: 'too_many_requests'});
      load.count++; active.set(player, load);
      try {
        const services = await backend(player,member);
        if (res.destroyed) return;
        const bytes = req.method === 'POST' ? await body(req) : undefined;
        let forwarded = bytes;
        if (match[1] === 'live' && req.method === 'POST' && match[2] === '/v1/live/sessions') {
          const payload = JSON.parse(bytes.toString());
          // Unity's binding contains the client's loopback URL. Only the host knows
          // this player's private backend port; caller-supplied targets are never used.
          if (payload.characterBinding) payload.characterBinding.memoryUrl = services.memory;
          forwarded = Buffer.from(JSON.stringify(payload));
        }
        const target = new URL(match[2] + url.search, services[match[1]]);
        if (target.protocol !== 'http:' || target.hostname !== '127.0.0.1') throw Error('invalid_backend');
        await proxy(req, res, target, {bytes: forwarded, headers: match[1] === 'live' ? {Authorization: 'Bearer ' + services.liveToken} : {'X-Z-Memory': '1'}});
      } finally {load.count--;}
    } catch (error) {json(res, error.status || 503, {error: error.status === 413 ? 'body_too_large' : 'host_unavailable', message: '主机正在连接角色服务，请稍后重试。'});}
  });
  server.requestTimeout = 30000; server.headersTimeout = 15000;
  return {server, registry};
}
