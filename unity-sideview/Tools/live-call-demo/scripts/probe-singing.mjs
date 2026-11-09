// Explicit, bounded capability probe. Does not alter the live demo or song catalog.
import https from 'node:https';
import {Z_VOICE_PROFILE} from '../ui/talent-config.js';
import fs from 'node:fs/promises';
import { randomBytes, createHash, randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
const root = new URL('../', import.meta.url);
let secret = process.env.VOLC_TTS_API_KEY?.trim() || '';
for (const file of ['火山语音配置.env', '.env.local']) {
  if (secret) break;
  try {
    const source = await fs.readFile(new URL(file, root), 'utf8');
    const raw = source.match(/^[ \t]*(?:export[ \t]+)?VOLC_TTS_API_KEY[ \t]*=[ \t]*([^\r\n]*)/m)?.[1]?.trim() || '';
    secret = /^['"]/.test(raw) ? (raw.match(/^(['"])(.*?)\1\s*(?:#.*)?$/)?.[2]?.trim() || '') : raw.replace(/(?:^|[ \t]+)#.*$/, '').trim();
  } catch {}
}
if (!secret) throw new Error('VOLC_TTS_API_KEY is missing');
const report = { checkedAt: new Date().toISOString(), endpoint: '/api/v3/duplex/realtime/dialogue', model: Z_VOICE_PROFILE.singingModel, voice: Z_VOICE_PROFILE.speaker, enableMusic: true, events: [], audioGenerated: false };
const generate = process.argv.includes('--generate');
const argument = name => { const i=process.argv.indexOf(name); return i<0?null:process.argv[i+1]; };
const requestFile=argument('--request-file');
const requestedText=requestFile?await fs.readFile(requestFile,'utf8'):null;
if(requestedText!==null&&(!requestedText.trim()||requestedText.length>1600))throw Error('Invalid singing request');
const runId = new Date().toISOString().replace(/[:.]/g, '-')+'-'+randomUUID().slice(0,8);
let queryAudio;
if (generate) {
  report.request = requestedText || '请用你自己的 Vivi 2.0 女声，清唱颜人中演唱的歌曲《晚安》的第一段完整副歌。请先从曲库确认歌曲和副歌，只唱这一段，保持原曲旋律和歌词，不要伴奏、和声、开场白或结束语，不模仿颜人中的声音。如果不支持，请直接说明。';
  // TTS only speaks the request into the S2S model; it is NOT the singing output.
  const response = await fetch('https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse', {
    method: 'POST', redirect: 'error', signal: AbortSignal.timeout(30000),
    headers: { 'Content-Type': 'application/json', 'X-Api-Key': secret, 'X-Api-Resource-Id': Z_VOICE_PROFILE.speakingModel, 'X-Api-Request-Id': randomUUID() },
    body: JSON.stringify({ user: { uid: 'z-singing-verification' }, req_params: { text: report.request, speaker: report.voice, audio_params: { format: 'pcm', sample_rate: 24000 } } }),
  });
  if (!response.ok) throw new Error(`Spoken request preparation failed: HTTP ${response.status}`);
  const audio = []; let bytes = 0, wire = 0, pendingText = '', completed = false;
  const decoder = new TextDecoder();
  for await (const chunk of response.body) {
    wire += chunk.length; if (wire > 8 * 1024 * 1024) throw new Error('Spoken request wire limit');
    pendingText += decoder.decode(chunk, { stream: true });
    const lines = pendingText.split('\n'); pendingText = lines.pop();
    for (const line of lines) {
      if (!line.startsWith('data:')) continue;
      const packet = JSON.parse(line.slice(5));
      if (packet.code === 20000000) { completed = true; continue; }
      if (packet.code !== 0) throw new Error(`Spoken request preparation failed: code ${packet.code}`);
      if (packet.data) { const part = Buffer.from(packet.data, 'base64'); bytes += part.length; if (bytes > 2 * 1024 * 1024) throw new Error('Spoken request size limit'); audio.push(part); }
    }
  }
  if (!completed || !bytes || bytes % 2) throw new Error('Incomplete spoken request');
  const pcm = Buffer.concat(audio);
  queryAudio = Buffer.alloc(Math.floor((pcm.length / 2) * 2 / 3) * 2);
  for (let i = 0; i < queryAudio.length / 2; i++) {
    const at = i * 1.5, left = Math.floor(at), right = Math.min(left + 1, pcm.length / 2 - 1), ratio = at - left;
    queryAudio.writeInt16LE(Math.round(pcm.readInt16LE(left * 2) * (1 - ratio) + pcm.readInt16LE(right * 2) * ratio), i * 2);
  }
  report.queryDurationSeconds = queryAudio.length / 32000;
  console.log(JSON.stringify({ stage: 'spoken_request_prepared', durationSeconds: report.queryDurationSeconds }));
}
const clean = value => String(value).split(secret).join('[REDACTED]').slice(0, 2400);
let socket, finished = false, pending = Buffer.alloc(0), fragments = [], fragmentBytes = 0;
const audioChunks = []; let audioBytes = 0, closeTimer, generationDone = false;
const output = argument('--output-dir') ? new URL('file://' + (await import('node:path')).resolve(argument('--output-dir')) + '/') : new URL('../output/singing-candidates/', root);
async function finish(outcome) {
  if (finished) return;
  finished = true; clearTimeout(deadline); clearTimeout(closeTimer); socket?.destroy(); request.destroy();
  report.outcome = outcome;
  await fs.mkdir(output, { recursive: true });
  if (audioBytes) {
    const pcm = Buffer.concat(audioChunks), header = Buffer.alloc(44);
    header.write('RIFF'); header.writeUInt32LE(36 + pcm.length, 4); header.write('WAVEfmt ', 8);
    header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22);
    header.writeUInt32LE(24000, 24); header.writeUInt32LE(48000, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34);
    header.write('data', 36); header.writeUInt32LE(pcm.length, 40);
    report.audioFile = `vivi-wanan-${runId}.wav`; report.durationSeconds = pcm.length / 48000;
    report.audioGenerated = true; report.generationDone = generationDone; report.approved = false;
    report.checks = { song: 'unverified', completeChorus: 'unverified', aCappella: 'unverified', voice: 'unverified' };
    await fs.writeFile(new URL(report.audioFile, output), Buffer.concat([header, pcm]));
  }
  await fs.writeFile(new URL(generate ? `vivi-wanan-${runId}.json` : 'vivi-wanan-capability.json', output), JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ ...report, events: report.events.length, transcript: undefined, inputTranscript: undefined }, null, 2));
}
async function uploadQuery() {
  const started = Date.now();
  for (let offset = 0; offset < queryAudio.length && !finished; offset += 640) {
    const wait = started + offset / 32 - Date.now(); if (wait > 0) await delay(wait);
    if (!finished) send({ type: 'input_audio_buffer.append', audio: queryAudio.subarray(offset, offset + 640).toString('base64') });
  }
  if (!finished) { send({ type: 'input_audio_buffer.commit' }); send({ type: 'input_audio_mute.commit' }); console.log('Spoken song request uploaded. Waiting for full response.'); }
}
function send(payload, opcode = 1) {
  const data = Buffer.isBuffer(payload) ? payload : Buffer.from(JSON.stringify(payload));
  const mask = randomBytes(4), n = data.length;
  const header = Buffer.alloc(n < 126 ? 2 : 4);
  header[0] = 0x80 | opcode; header[1] = 0x80 | (n < 126 ? n : 126);
  if (n >= 126) header.writeUInt16BE(n, 2);
  const masked = Buffer.from(data);
  for (let i = 0; i < n; i++) masked[i] ^= mask[i % 4];
  socket.write(Buffer.concat([header, mask, masked]));
}
function message(data) {
  let event;
  try { event = JSON.parse(data.toString()); } catch { return void finish('invalid_server_json'); }
  if (event.type !== 'response.output_audio.delta') report.events.push({ ...event, ...(event.error ? { error: clean(JSON.stringify(event.error)) } : {}) });
  if (event.type === 'session.created') {
    report.sessionCreated = true;
    if (generate) void uploadQuery().catch(() => finish('upload_failed'));
    else send({ type: 'session.close' });
  } else if (event.type === 'response.output_audio.delta' && generate) {
    const part = Buffer.from(event.delta || '', 'base64'); audioBytes += part.length;
    if (audioBytes > 180 * 48000) return void finish('audio_limit');
    audioChunks.push(part);
  } else if (event.type === 'response.output_text.delta') {
    report.transcript = (report.transcript || '') + (event.delta || '');
  } else if (event.type === 'response.output_audio.done' && generate) {
    generationDone = true;
    closeTimer = setTimeout(() => { if (!finished) send({ type: 'session.close' }); }, 2000);
  } else if (event.type === 'session.closed') void finish(generate ? (generationDone ? 'candidate_generated' : 'closed_without_audio_completion') : 'session_permission_verified');
  else if (event.type === 'error' || event.error) void finish('provider_error');
}
function receive(data) {
  pending = Buffer.concat([pending, data]);
  while (pending.length >= 2 && !finished) {
    const final = !!(pending[0] & 128), op = pending[0] & 15, masked = !!(pending[1] & 128);
    let len = pending[1] & 127, offset = 2;
    if (len === 126) { if (pending.length < 4) return; len = pending.readUInt16BE(2); offset = 4; }
    if (len === 127) { if (pending.length < 10) return; const wide = pending.readBigUInt64BE(2); if (wide > 4194304n) return void finish('frame_limit'); len = Number(wide); offset = 10; }
    if (masked || len > 4194304) return void finish('invalid_server_frame');
    if (pending.length < offset + len) return;
    const payload = pending.subarray(offset, offset + len); pending = pending.subarray(offset + len);
    if (op === 8) return void finish(report.sessionCreated ? 'session_permission_verified' : 'server_closed');
    if (op === 9) { send(payload, 10); continue; }
    if (op === 10) continue;
    if (![0, 1].includes(op)) return void finish('unexpected_frame');
    fragmentBytes += payload.length;
    if (fragmentBytes > 4194304) return void finish('message_limit');
    fragments.push(payload);
    if (final) { const complete = Buffer.concat(fragments); fragments = []; fragmentBytes = 0; message(complete); }
  }
}
const wsKey = randomBytes(16).toString('base64');
const request = https.request({ hostname: 'openspeech.bytedance.com', path: report.endpoint, method: 'GET', headers: {
  'X-Api-Key': secret, Connection: 'Upgrade', Upgrade: 'websocket', 'Sec-WebSocket-Version': '13', 'Sec-WebSocket-Key': wsKey,
} });
const deadline = setTimeout(() => void finish('timeout'), generate ? 180000 : 25000);
request.on('upgrade', (response, stream, head) => {
  socket = stream; report.httpStatus = response.statusCode;
  const accept = createHash('sha1').update(wsKey + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  if (response.headers['sec-websocket-accept'] !== accept) return void finish('invalid_handshake');
  socket.on('data', receive); socket.on('error', error => { report.networkError = error.code || 'socket_error'; void finish('network_error'); });
  socket.on('end', () => void finish(report.sessionCreated ? 'session_permission_verified' : 'connection_ended'));
  send({ type: 'session.create', session: { model: report.model,
    instructions: '你是 Z，保持 Vivi 2.0 的声音。用户点歌时从曲库检索准确版本，按要求单人无伴奏清唱，不模仿原唱，不添加开场白和结束语。',
    audio: { input: { format: { type: 'pcm', rate: 16000 } }, output: { format: { type: 'pcm_s16le', rate: 24000 }, voice: report.voice } } },
    extension: { dialog: { extra: { enable_music: true } } } });
  if (head.length) receive(head);
});
request.on('response', response => {
  report.httpStatus = response.statusCode; let body = '';
  response.on('data', chunk => { if (body.length < 8192) body += chunk.toString(); });
  response.on('end', () => { report.providerResponse = clean(body); void finish('handshake_rejected'); });
});
request.on('error', error => { if (!finished) { report.networkError = error.code || 'request_error'; void finish('network_error'); } });
request.end();
