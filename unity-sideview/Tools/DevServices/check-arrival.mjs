// Real provider acceptance. Opening uses an unbound room. --bound also exercises character memory.
// A headless executor acknowledges captions and gift clicks; no visual or audio playback claim.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { setTimeout as wait } from 'node:timers/promises';

const options = process.argv.slice(2);
const bindCharacters = options.includes('--bound');
const connectionIndex = options.indexOf('--connection');
const connectionArg = connectionIndex < 0 ? '' : options[connectionIndex + 1];
if (!options.includes('--allow-real-api') || connectionIndex >= 0 && (!connectionArg || connectionArg.startsWith('--')) ||
    options.some((o, i) => !['--allow-real-api', '--bound', '--connection'].includes(o) && !(connectionIndex >= 0 && i === connectionIndex + 1))) {
  console.error('用法：check-arrival.mjs --allow-real-api [--bound] [--connection 连接文件]；将调用真实模型和语音服务。');
  process.exit(2);
}
const directory = connectionArg ? null : fs.mkdtempSync(path.join(os.tmpdir(), 'project-z-arrival-'));
const editor = connectionArg ? path.resolve(connectionArg) : path.join(directory, 'editor.json');
const id = () => randomUUID().replaceAll('-', '');
const name = '体验小海';
const scope = { worldId:id(), runId:id(), day:1, period:0, syncMode:'reference-v1',
  details:{firstDay:{phase:'Live', version:1000, openingStarted:true, playerName:name, greeted:[]}} };
const port = () => new Promise((resolve, reject) => {
  const server = net.createServer();
  server.once('error', reject);
  server.listen(0, '127.0.0.1', () => {
    const available = server.address().port;
    server.close(() => resolve(available));
  });
});
let launcher, exited;
if (directory) {
  const [memoryPort, livePort] = await Promise.all([port(), port()]);
  launcher = spawn(process.execPath, [fileURLToPath(new URL('./local.mjs', import.meta.url)), 'start'], {
    env: { ...process.env, Z_DEV_STATE_DIR:path.join(directory, 'state'), Z_DEV_EDITOR_FILE:editor, Z_MEMORY_PORT:String(memoryPort),
      Z_UNITY_PORT:String(livePort), Z_NETEASE_AUTO_RENEW:'0', NETEASE_SESSION_FILE:path.join(directory, 'unused-music-session.json') },
    stdio:['ignore','ignore','inherit','ipc']
  });
  exited = new Promise(resolve => launcher.once('exit', resolve));
}
let interrupted = false;
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { interrupted = true; });
const cleanup = [];
const runStarted = Date.now();
const report = { bound:false, host:0, audience:0, audioBytes:0, tools:[], elapsedMs:0 };
async function run() {
  for (let i = 0; !fs.existsSync(editor); i++) {
    if (!launcher) throw Error('client_connection_file_missing');
    if (interrupted || i === 160 || launcher.exitCode !== null || launcher.signalCode) throw Error('local_start_failed');
    await wait(200);
  }
  let connection;
  try { connection = JSON.parse(fs.readFileSync(editor, 'utf8')); } catch { throw Error('invalid_connection_file'); }
  const { memoryUrl, baseUrl, token } = connection;
  for (const value of [baseUrl, memoryUrl]) {
    const address = new URL(value);
    if (address.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(address.hostname) ||
        address.password || address.username || address.search || address.hash || address.pathname !== '/') throw Error('expected_loopback_relay');
  }
  if (!token || typeof token !== 'string') throw Error('missing_client_token');
  const request = async (url, body, live, binary = false) => {
    const response = await fetch(url, {
      method: body ? 'POST' : 'GET', redirect:'error', headers: { 'Content-Type':'application/json',
        ...(live ? {Authorization:'Bearer ' + token, 'Idempotency-Key':randomUUID()} : {'X-Z-Memory':'1'}) },
      ...(body ? {body:JSON.stringify(body)} : {}), signal:AbortSignal.timeout(40000)
    });
    if (!response.ok) {
      const details = await response.json().catch(() => ({}));
      const operation = new URL(url).pathname.split('/').at(-1);
      const label = /^[a-z_-]+$/.test(operation) ? operation : 'session';
      const reason = /^[a-z_]{1,64}$/.test(details.error || '') ? '_' + details.error : '';
      throw Error(label + '_http_' + response.status + reason);
    }
    return binary ? Buffer.from(await response.arrayBuffer()) : response.json();
  };
  const game = (action, extra) => request(memoryUrl + '/api/game/' + action, {...scope, requestId:id(), ...extra}, false);
  const memory = await request(memoryUrl + '/api/health', null, false), live = await request(baseUrl + '/health', null, true);
  if (!memory.configured || !live.agentConfigured || !live.ttsConfigured) throw Error('provider_configuration_missing');
  let binding;
  if (bindCharacters) {
    await game('open'); cleanup.push(() => game('close'));
    binding = (await game('live/open')).binding;
    cleanup.push(() => game('live/close', {roomId:binding.roomId, roomToken:binding.roomToken}));
  }
  const room = await request(baseUrl + '/v1/live/sessions', {protocolVersion:1, mode:'private',
    ...(binding ? {characterBinding:{...binding, memoryUrl}} : {}), story:{mode:'arrival', playerName:name, worldId:scope.worldId, day:1},
    capabilities:{audioEnabled:true, characterReady:true}}, true);
  const url = baseUrl + '/v1/live/sessions/' + room.sessionId;
  const post = (action, data) => request(url + '/' + action,
    {protocolVersion:1, sessionId:room.sessionId, epoch:room.epoch, executorLeaseId:room.executorLeaseId, ...data}, true);
  cleanup.push(() => post('control', {action:'end'}));
  report.bound = Boolean(binding);
  await post('control', {action:'resume'});
  const began = Date.now(), started = new Map();
  let heartbeat = 0, lastAgentError;
  while (Date.now() - began < 300000) {
    if (interrupted) throw Error('interrupted');
    if (Date.now() > heartbeat) { await post('heartbeat', {}); heartbeat = Date.now() + 8000; }
    const snapshot = await request(url, null, true);
    if (snapshot.story.phase === 'departed') {
      if (report.tools.join(',') !== 'draw_guest,offer_arrival,depart_island') throw Error('unexpected_story_order');
      report.elapsedMs = Date.now() - began;
      return;
    }
    if (snapshot.agent.errorCode && lastAgentError !== snapshot.agent.errorCode) {
      lastAgentError = snapshot.agent.errorCode;
      console.log(JSON.stringify({phase:snapshot.story.phase, agentError:lastAgentError}));
    }
    for (const c of snapshot.pendingCommands) {
      const p = c.payload;
      const receipt = (status, fields) => post('receipts', {commandId:c.commandId, taskId:c.taskId, resourceRevision:c.resourceRevision,
        payload:{status, ...fields}});
      if (c.type === 'host.present') {
        let playback = started.get(c.commandId);
        if (!playback) {
          let duration;
          if (p.modality === 'audio') {
            const bytes = await request(url + '/artifacts/' + p.audioArtifactId, null, true, true);
            if (!bytes.length || createHash('sha256').update(bytes).digest('hex') !== p.sha256) throw Error('invalid_audio_artifact');
            report.audioBytes += bytes.length;
            duration = Math.max(3000, Array.from(p.text).length * 145);
          } else if (p.modality === 'pcm-stream') {
            const bytes = await request(url + '/audio-stream/' + p.audioStreamId, null, true, true);
            const events = bytes.toString().trim().split('\n').map(line => JSON.parse(line)), last = events.at(-1);
            const size = events.reduce((n, e) => n + (e.type === 'audio' ? Buffer.from(e.data,'base64').length : 0), 0);
            if (last?.type !== 'done' || !size || !last.durationMs) throw Error('incomplete_pcm');
            duration = last.durationMs; report.audioBytes += size;
          } else throw Error('speech_fallback_' + (p.fallbackReason || 'text'));
          playback = {duration, until:Date.now() + duration}; started.set(c.commandId, playback);
          await receipt('started', {speechId:p.speechId, modality:p.modality, durationMs:duration, positionMs:0});
          if (!report.host) {
            report.firstHostMs = Date.now() - began;
            console.log(JSON.stringify({firstHostMs:report.firstHostMs, modality:p.modality}));
          }
          report.host++;
        } else if (Date.now() >= playback.until) {
          await receipt('completed', {speechId:p.speechId, modality:p.modality, positionMs:playback.duration});
        }
      } else if (c.type === 'virtual_audience.present') {
        const deadline = started.get(c.commandId) || Date.now() + (p.delayMs || 0); started.set(c.commandId, deadline);
        if (Date.now() >= deadline) { await receipt('displayed'); report.audience++; }
      } else if (c.type === 'story.present') {
        const tool = p.tool;
        if (tool === 'draw_guest' && !snapshot.story.warmup.ready) throw Error('premature_draw');
        await receipt('displayed', tool === 'draw_guest' ? {playerActivated:true, playerName:name} : {});
        report.tools.push(tool); console.log(JSON.stringify({tool, elapsedMs:Date.now() - began}));
        if (tool === 'offer_arrival') await post('inputs', {type:'story.accept_arrival',payload:{}});
      } else if (['media.resume','media.pause','execution.cancel'].includes(c.type)) await receipt('completed');
    }
    await wait(400);
  }
  throw Error(lastAgentError || 'arrival_timeout');
}
try {
  await run();
} catch (error) {
  report.elapsedMs = Date.now() - runStarted;
  console.error(JSON.stringify({passed:false, error:/^[a-z0-9_]+$/.test(error.message) ? error.message : 'check_failed', ...report}));
  process.exitCode = 1;
} finally {
  for (const action of cleanup.reverse()) {
    try { await action(); } catch { report.cleanupFailed = true; process.exitCode = 1; }
  }
  if (launcher?.connected) launcher.disconnect();
  if (exited) await exited;
  if (directory) fs.rmSync(directory, {recursive:true,force:true});
  if (!process.exitCode || report.cleanupFailed) console.log(JSON.stringify({passed:!process.exitCode, ...report}));
}
