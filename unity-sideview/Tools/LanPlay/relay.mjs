import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import https from 'node:https';
import {randomBytes} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {allowed, digest, equal, json, proxy} from './protocol.mjs';
import {loadTeamClient} from './team-access.mjs';

export async function createRelay(config, runtime, {team=null} = {}) {
  const origin = new URL(config.serverUrl);
  if (origin.protocol !== 'https:' || origin.username || origin.password || origin.pathname !== '/') throw Error('invalid_server_url');
  if (!config.certificate || !/^[a-f0-9]{64}$/.test(config.invitation)) throw Error('invalid_connection_config');
  fs.mkdirSync(runtime, {recursive: true, mode: 0o700});
  const identityFile = path.join(runtime, 'identity.json');
  let identity;
  if (fs.existsSync(identityFile)) identity = JSON.parse(fs.readFileSync(identityFile));
  else {identity = {id: randomBytes(16).toString('hex'), secret: randomBytes(32).toString('hex')}; fs.writeFileSync(identityFile, JSON.stringify(identity), {mode: 0o600});}
  await new Promise((resolve, reject) => {
    const req = https.request(new URL('/register', origin), {method: 'POST', ca: config.certificate, rejectUnauthorized: true,
      headers: {'Content-Type': 'application/json', Authorization: 'Bearer ' + config.invitation,...(team?{'X-Z-Team-Token':team.session()?.token||''}:{})}, timeout: 10000}, res => {
      const chunks = []; res.on('data', chunk => chunks.push(chunk));
      res.on('end', () => {if (res.statusCode === 200) resolve(); else reject(Error(res.statusCode === 429 ? '试玩名额已满，请联系主机管理员。' : '主机拒绝连接，请索取最新试玩包。'));});
    });
    req.on('timeout', () => req.destroy(Error('主机连接超时，请连接相同局域网，并确认主机启动。')));
    req.on('error', reject); req.end(JSON.stringify(identity));
  });
  const localToken = randomBytes(32).toString('hex'), servers = [];
  try {
    for (const surface of ['memory', 'live']) {
      const server = http.createServer(async (req, res) => {
        if (!/^(127\.0\.0\.1|localhost):\d+$/.test(req.headers.host || '') || req.headers.origin)
          return json(res, 403, {error: 'local_only'});
        if (surface === 'live' && !equal(req.headers.authorization, 'Bearer ' + localToken)) return json(res, 401, {error: 'unauthorized'});
        if (req.url === '/relay-health' && req.method === 'GET') return json(res, 200, {ok: true, fingerprint: digest(JSON.stringify(config))});
        const url = new URL(req.url, 'http://127.0.0.1');
        if (!allowed(surface, req.method, url.pathname)) return json(res, 403, {error: 'route_forbidden'});
        if(team&&!team.session())return json(res,401,{error:'team_login_required'});
        await proxy(req, res, new URL('/' + surface + url.pathname + url.search, origin), {
          ca: config.certificate, headers: {Authorization: 'Bearer ' + identity.secret,...(team?{'X-Z-Team-Token':team.session().token}:{})}});
      });
      await new Promise((resolve, reject) => {server.once('error', reject); server.listen(0, '127.0.0.1', resolve);});
      servers.push(server);
    }
  } catch (error) {for (const server of servers) server.close(); throw error;}
  const connection = {baseUrl: `http://127.0.0.1:${servers[1].address().port}`, token: localToken};
  const state = {pid: process.pid, memoryUrl: `http://127.0.0.1:${servers[0].address().port}`, connection,
    fingerprint: digest(JSON.stringify(config)),teamFingerprint:team?digest(team.origin+team.certificate):null};
  fs.writeFileSync(path.join(runtime, 'connection.json'), JSON.stringify(connection), {mode: 0o600});
  fs.writeFileSync(path.join(runtime, 'relay.json'), JSON.stringify(state), {mode: 0o600});
  const close = () => {for (const server of servers) {server.closeAllConnections(); server.close();}};
  return {state, close};
}
if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  try {
    const relay = await createRelay(JSON.parse(fs.readFileSync(process.argv[2])), process.argv[3],{team:loadTeamClient()});
    for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, relay.close);
    console.log('已连接小岛主机。模型和语音由主机提供。');
  } catch (error) {console.error('连接失败：' + error.message); process.exitCode = 1;}
}
