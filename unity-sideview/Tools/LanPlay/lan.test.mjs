import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import https from 'node:https';
import {execFileSync} from 'node:child_process';
import {createGateway} from './gateway.mjs';
import {createRelay} from './relay.mjs';
import {allowed} from './protocol.mjs';

test('team LAN invitation also requires current GitHub membership and binds the player identity',async t=>{
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'z-team-lan-'));
  fs.writeFileSync(root+'/cert.cnf','[req]\ndistinguished_name=dn\nx509_extensions=ext\nprompt=no\n[dn]\nCN=Team LAN test\n[ext]\nsubjectAltName=IP:127.0.0.1\nbasicConstraints=critical,CA:TRUE\n');
  execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-days','1','-config',root+'/cert.cnf','-keyout',root+'/key','-out',root+'/cert'],{stdio:'ignore'});
  const cert=fs.readFileSync(root+'/cert');let revoked=false;
  const backend=http.createServer((req,res)=>{res.setHeader('Content-Type','application/json');res.end(JSON.stringify({ok:true,leakedTeamHeader:req.headers['x-z-team-token']||null}));});
  await new Promise(r=>backend.listen(0,'127.0.0.1',r));
  const gateway=createGateway({key:fs.readFileSync(root+'/key'),cert,invitation:'a'.repeat(64),registryFile:root+'/players.json',verifyMember:async token=>!revoked&&['member-a','member-b'].includes(token)?{token,user:{id:token==='member-a'?1:2,login:token},expires:Date.now()+60000}:null,backend:async(id,member)=>{assert.equal(member.user.id,1);return {memory:`http://127.0.0.1:${backend.address().port}`,live:`http://127.0.0.1:${backend.address().port}`,liveToken:'internal'};}});
  await new Promise(r=>gateway.server.listen(0,'127.0.0.1',r));
  t.after(()=>{for(const s of [gateway.server,backend]){s.closeAllConnections();s.close();}fs.rmSync(root,{recursive:true,force:true});});
  const send=(route,authorization,member,data)=>new Promise((resolve,reject)=>{
    const req=https.request(`https://127.0.0.1:${gateway.server.address().port}`+route,{method:data?'POST':'GET',ca:cert,headers:{Authorization:'Bearer '+authorization,'Content-Type':'application/json',...(member?{'X-Z-Team-Token':member}:{})}},res=>{const chunks=[];res.on('data',b=>chunks.push(b));res.on('end',()=>resolve({status:res.statusCode,body:JSON.parse(Buffer.concat(chunks))}));});req.on('error',reject);req.end(data?JSON.stringify(data):undefined);
  });
  const identity={id:'1'.repeat(32),secret:'2'.repeat(64)};
  assert.equal((await send('/register','a'.repeat(64),null,identity)).status,403);
  assert.equal((await send('/register','a'.repeat(64),'member-a',identity)).status,200);
  assert.equal((await send('/register','a'.repeat(64),'member-b',identity)).status,403);
  assert.equal((await send('/memory/api/health',identity.secret,null)).status,403);
  const accepted=await send('/memory/api/health',identity.secret,'member-a');assert.equal(accepted.status,200);assert.equal(accepted.body.leakedTeamHeader,null);
  assert.equal((await send('/memory/api/health',identity.secret,'member-b')).status,403);
  revoked=true;assert.equal((await send('/memory/api/health',identity.secret,'member-a')).status,403);
});

test('LAN gateway: TLS, player isolation, streaming, cancellation, route boundaries and binding', async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'z-lan-test-'));
  const cnf = path.join(root, 'openssl.cnf');
  fs.writeFileSync(cnf, '[req]\ndistinguished_name=dn\nx509_extensions=ext\nprompt=no\n[dn]\nCN=LAN test\n[ext]\nsubjectAltName=IP:127.0.0.1\nbasicConstraints=critical,CA:TRUE\n');
  execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-config', cnf,
    '-keyout', root + '/key', '-out', root + '/cert'], {stdio: 'ignore'});
  const cert = fs.readFileSync(root + '/cert'), key = fs.readFileSync(root + '/key');
  const backends = new Map(), servers = [], relays = [];
  let finishStream, observedClose, onClose;
  const closed = new Promise(resolve => {onClose = resolve;});
  const backend = async id => {
    if (backends.has(id)) return backends.get(id);
    let state = null;
    const server = http.createServer(async (req, res) => {
      if (req.url.endsWith('/events')) {
        res.writeHead(200, {'Content-Type': 'text/event-stream'});
        res.write('data: first\n\n'); finishStream = () => res.end('data: last\n\n');
        res.once('close', () => {observedClose = true; onClose();}); return;
      }
      const chunks = []; for await (const chunk of req) chunks.push(chunk);
      const data = Buffer.concat(chunks).length ? JSON.parse(Buffer.concat(chunks)) : {};
      if (req.url === '/api/game/open') state = data.name;
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ok: true, name: state, binding: data.characterBinding, authorization: req.headers.authorization}));
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); servers.push(server);
    const address = `http://127.0.0.1:${server.address().port}`;
    const result = {memory: address, live: address, liveToken: 'private-backend-token'}; backends.set(id, result); return result;
  };
  const gateway = createGateway({key, cert, invitation: 'a'.repeat(64), registryFile: root + '/players.json', backend, maxPlayers: 2});
  await new Promise(resolve => gateway.server.listen(0, '127.0.0.1', resolve));
  const config = {serverUrl: `https://127.0.0.1:${gateway.server.address().port}`, certificate: cert.toString(), invitation: 'a'.repeat(64)};
  t.after(() => {for (const relay of relays) relay.close(); for (const server of [...servers, gateway.server]) {server.closeAllConnections(); server.close();} fs.rmSync(root, {recursive: true, force: true});});
  const request = (url, options = {}) => new Promise((resolve, reject) => {
    const req = https.request(url, {ca: cert, ...options}, res => {res.resume(); res.once('end', () => resolve(res.statusCode));}); req.on('error', reject); req.end();
  });
  await t.test('rejects anonymous access', async () => assert.equal(await request(config.serverUrl + '/memory/api/health'), 401));
  await t.test('rejects untrusted TLS certificates', async () => {
    await assert.rejects(request(config.serverUrl + '/memory/api/health', {ca: []}), /self.signed|certificate/i);
  });
  const first = await createRelay(config, root + '/first'), second = await createRelay(config, root + '/second'); relays.push(first, second);
  const post = (relay, action, data) => fetch(relay.state.memoryUrl + '/api/game/' + action, {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(data)}).then(r => r.json());
  await t.test('same world id has independent state for two players', async () => {
    await post(first, 'open', {worldId: 'same-world', name: 'A'}); await post(second, 'open', {worldId: 'same-world', name: 'B'});
    assert.equal((await post(first, 'state', {})).name, 'A'); assert.equal((await post(second, 'state', {})).name, 'B');
    assert.equal(backends.size, 2);
  });
  await t.test('blocks configuration, lab and live bridge internals', async () => {
    for (const route of ['/api/config', '/api/lab/reset', '/api/game/live/propose']) {
      const result = await fetch(first.state.memoryUrl + route, {method: 'POST', body: '{}'}); assert.equal(result.status, 403);
    }
    assert.equal(allowed('live', 'POST', '/api/singing/tasks'), false);
    assert.equal((await fetch(first.state.memoryUrl + '/api/health', {headers: {Origin: 'https://unrelated.example'}})).status, 403);
  });
  const liveHeaders = {Authorization: 'Bearer ' + first.state.connection.token, 'Content-Type': 'application/json'};
  await t.test('live binding always uses the owning host backend', async () => {
    const result = await fetch(first.state.connection.baseUrl + '/v1/live/sessions', {method: 'POST', headers: liveHeaders,
      body: JSON.stringify({characterBinding: {memoryUrl: 'http://127.0.0.1:1', worldId: 'test'}})}).then(r => r.json());
    const identity = JSON.parse(fs.readFileSync(root + '/first/identity.json'));
    assert.equal(result.binding.memoryUrl, backends.get(identity.id).memory);
    assert.equal(result.authorization, 'Bearer private-backend-token');
  });
  const events = first.state.connection.baseUrl + '/v1/live/sessions/' + 'a'.repeat(36) + '/events';
  await t.test('SSE first chunk arrives before stream completion', async () => {
    const response = await fetch(events, {headers: liveHeaders}); const reader = response.body.getReader();
    const firstChunk = await reader.read(); assert.match(new TextDecoder().decode(firstChunk.value), /first/); assert.equal(firstChunk.done, false);
    finishStream(); let text = ''; while (true) {const chunk = await reader.read(); if (chunk.done) break; text += new TextDecoder().decode(chunk.value);} assert.match(text, /last/);
  });
  await t.test('client cancellation closes upstream', async () => {
    observedClose = false;
    const controller = new AbortController(); const response = await fetch(events, {headers: liveHeaders, signal: controller.signal});
    await response.body.getReader().read(); controller.abort();
    for (let i = 0; i < 30 && !observedClose; i++) await new Promise(resolve => setTimeout(resolve, 10));
    assert.equal(observedClose, true); await closed;
  });
  await t.test('reconnect retains identity and new installations respect capacity', async () => {
    first.close(); const again = await createRelay(config, root + '/first'); relays.push(again);
    assert.equal((await post(again, 'state', {})).name, 'A');
    await assert.rejects(createRelay(config, root + '/third'), /名额已满/);
    assert.equal(Object.keys(JSON.parse(fs.readFileSync(root + '/players.json'))).length, 2);
  });
});
