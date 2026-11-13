// Transport acceptance only: uses fictional text and a fresh test world, never
// claims that downloaded speech was heard or that a story stage was completed.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {randomUUID, createHash} from 'node:crypto';

if (!process.argv.includes('--allow-real-api')) throw Error('Pass --allow-real-api only with host authorization.');
const runtime = path.resolve(process.argv[2]);
const relay = JSON.parse(fs.readFileSync(path.join(runtime, 'relay.json')));
const scope = {worldId: randomUUID().replaceAll('-', ''), runId: randomUUID().replaceAll('-', ''), day: 1, period: 0};
const report = {scope: 'Real model/TTS through LAN TLS gateway, with synthetic publication receipts for transport testing. No rendered gameplay claim.', passed: false};
const post = async (action, value = {}) => {
  const r = await fetch(relay.memoryUrl + '/api/game/' + action, {method: 'POST', headers: {'Content-Type': 'application/json', 'X-Z-Memory': '1'},
    body: JSON.stringify({...scope, requestId: randomUUID(), ...value}), signal: AbortSignal.timeout(65000)});
  const reply = await r.json(); assert(r.ok && reply.ok, action + ': ' + (reply.error || r.status)); return reply;
};
const live = async (route, payload) => {
  const response = await fetch(relay.connection.baseUrl + route, {method: payload ? 'POST' : 'GET',
    headers: {Authorization: 'Bearer ' + relay.connection.token, ...(payload ? {'Content-Type': 'application/json', 'Idempotency-Key': randomUUID()} : {})},
    ...(payload ? {body: JSON.stringify(payload)} : {}), signal: AbortSignal.timeout(30000)});
  assert(response.ok, 'Live transport: ' + response.status + (response.ok ? '' : ' ' + await response.text())); return response;
};
let room, binding;
try {
  await post('open'); await post('focus', {actorId: 'z'});
  const conversationId = 'qa_' + randomUUID().replaceAll('-', '');
  const event = {id: randomUUID(), seq: 1, kind: 'utterance', day: 1, period: 0, speakerId: 'player', recipientId: 'z',
    conversationId, channel: 'private_player_chat', text: '你好，我叫小海，很高兴认识你。', locationId: 'main_street'};
  await post('sync', {eventData: event});
  const reply = await post('utterance', {actorId: 'z', recipientId: 'player', conversationId, channel: 'private_player_chat', scene: 'Street', locationId: 'main_street'});
  assert(reply.proposalId && reply.text); report.dialogue = {generated: true, characters: reply.text.length};
  await post('sync', {eventData: {...event, id: randomUUID(), seq: 2, speakerId: 'z', recipientId: 'player', text: reply.text, proposalId: reply.proposalId}});
  const started = performance.now();
  const speech = await fetch(relay.memoryUrl + '/api/game/speech/stream', {method: 'POST', headers: {'Content-Type': 'application/json', 'X-Z-Memory': '1'},
    body: JSON.stringify({...scope, actorId: 'z', conversationId, proposalId: reply.proposalId}), signal: AbortSignal.timeout(45000)});
  assert(speech.ok); let pending = '', audioBytes = 0, firstAudioMs = null, done = false, packets = 0;
  const decoder = new TextDecoder();
  for await (const chunk of speech.body) {
    pending += decoder.decode(chunk, {stream: true});
    let newline;
    while ((newline = pending.indexOf('\n')) >= 0) {
      const text = pending.slice(0, newline); pending = pending.slice(newline + 1); if (!text) continue;
      const packet = JSON.parse(text); packets++;
      if (packet.type === 'error') throw Error(packet.error);
      if (packet.type === 'audio') {firstAudioMs ??= Math.round(performance.now() - started); audioBytes += Buffer.from(packet.audio?.base64 || '', 'base64').length;}
      if (packet.type === 'done') done = true;
    }
  }
  assert(done && audioBytes > 0, 'No complete streamed speech');
  report.speech = {audioBytes, packets, firstAudioMs, completeMs: Math.round(performance.now() - started)};
  console.log('Real dialogue and streamed TTS passed.');
  await post('focus', {actorId: null});
  binding = (await post('live/open')).binding; assert(binding);
  room = await (await live('/v1/live/sessions', {protocolVersion: 1, mode: 'private', capabilities: {audioEnabled: true, characterReady: true},
    characterBinding: {...binding, memoryUrl: relay.memoryUrl}, story: {mode: 'daily', playerName: '小海', worldId: scope.worldId, day: 1}})).json();
  const envelope = extra => ({protocolVersion: 1, sessionId: room.sessionId, epoch: room.epoch, executorLeaseId: room.executorLeaseId, ...extra});
  const route = '/v1/live/sessions/' + room.sessionId;
  await live(route + '/control', envelope({action: 'resume'}));
  const events = await live(route + '/events?afterSeq=0'); const reader = events.body.getReader();
  const first = await reader.read(); assert(first.value.length); await reader.cancel(); report.liveEvents = true;
  let host;
  for (let i = 0; i < 70; i++) {
    const state = await (await live(route)).json(); host = state.pendingCommands.find(c => c.type === 'host.present' && c.payload.audioArtifactId);
    if (host) break;
    if (i % 8 === 0) await live(route + '/heartbeat', envelope({}));
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  assert(host, 'Real live host audio was not produced');
  const artifact = await live(route + '/artifacts/' + host.payload.audioArtifactId);
  const bytes = Buffer.from(await artifact.arrayBuffer());
  assert.equal(createHash('sha256').update(bytes).digest('hex'), host.payload.sha256); assert(bytes.length > 0);
  report.liveHost = {generated: true, audioBytes: bytes.length, hashVerified: true};
  report.passed = true; console.log('Real live character binding, events and audio artifact passed.');
} finally {
  if (room) {try {await live('/v1/live/sessions/' + room.sessionId + '/control', {protocolVersion: 1, sessionId: room.sessionId, epoch: room.epoch, action: 'end'});} catch {}}
  if (binding) {try {await post('live/close', {roomId: binding.roomId, roomToken: binding.roomToken});} catch {}}
  try {await post('close');} catch {}
  fs.writeFileSync(path.join(runtime, 'real-verification.json'), JSON.stringify(report, null, 2));
}
