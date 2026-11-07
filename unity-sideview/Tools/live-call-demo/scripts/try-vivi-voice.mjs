/** Explicit real Vivi 2.0 smoke test. Never logs or embeds the provider key. */
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { webcrypto, randomUUID } from 'node:crypto';
import { handleTts } from '../worker/volc-tts.js';

const root = fileURLToPath(new URL('../', import.meta.url));
let secret = process.env.VOLC_TTS_API_KEY?.trim() || '';
for (const file of ['火山语音配置.env', '.env.local']) {
  if (secret) break;
  try {
    const source = await fs.readFile(path.join(root, file), 'utf8');
    const match = source.match(/^\s*(?:export\s+)?VOLC_TTS_API_KEY\s*=\s*(.*?)\s*$/m);
    const raw = match?.[1] || '';
    secret = /^['"]/.test(raw) ? (raw.match(/^(['"])(.*?)\1\s*(?:#.*)?$/)?.[2]?.trim() || '') : raw.replace(/\s+#.*$/, '').trim();
  } catch {}
}
if (!secret) throw new Error('Fill 火山语音配置.env before running the explicit voice test.');
const text = '来啦，晚上好！我是 Z。别急着把今天过成一道难题，先坐一会儿，陪我聊两句。';
const origin = 'https://local-vivi-test.invalid';
const started = Date.now();
const response = await handleTts(new Request(origin + '/api/tts', {
  method: 'POST', headers: { Origin: origin, 'Content-Type': 'application/json', 'oai-authenticated-user-id': 'local-vivi-test-owner' },
  body: JSON.stringify({ requestId: randomUUID(), text, mood: 'happy' }),
}), { VOLC_TTS_API_KEY: secret }, { crypto: webcrypto, diagnostics() {} });
secret = '';
if (!response.ok) {
  const result = await response.json();
  throw new Error(`Vivi test failed: ${response.status} ${result.error || 'unknown'}`);
}
const chunks = [], sentences = [];
let pending = '', done = false, bytes = 0, firstAudioMs = null;
const reader = response.body.getReader(), decoder = new TextDecoder();
for (;;) {
  const next = await reader.read();
  pending += decoder.decode(next.value || new Uint8Array(), { stream: !next.done });
  const lines = pending.split('\n'); pending = lines.pop();
  for (const line of lines) {
    if (!line.trim()) continue;
    const event = JSON.parse(line);
    if (event.type === 'error') throw new Error(`Vivi test failed: ${event.error || 'stream_error'}`);
    if (event.type === 'audio') {
      if (firstAudioMs === null) firstAudioMs = Date.now() - started;
      const data = Buffer.from(event.data, 'base64'); bytes += data.length;
      if (bytes > 2 * 1024 * 1024) throw new Error('Test audio exceeds limit');
      chunks.push(data);
    }
    if (event.type === 'sentence') sentences.push(event.sentence);
    if (event.type === 'done') done = true;
  }
  if (next.done) break;
}
if (!done || !bytes) throw new Error('Vivi test did not finish with valid audio');
const output = path.resolve(root, '../output');
await fs.mkdir(output, { recursive: true });
await fs.writeFile(path.join(output, 'vivi2-z-demo.mp3'), Buffer.concat(chunks));
const report = { voice: 'Vivi 2.0', speaker: 'zh_female_vv_uranus_bigtts', text,
  audioBytes: bytes, firstAudioMs, completedMs: Date.now() - started, sentences };
await fs.writeFile(path.join(output, 'vivi2-z-demo.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ voice: report.voice, audioBytes: bytes, firstAudioMs, completedMs: report.completedMs,
  subtitleSentences: sentences.length, output: path.join(output, 'vivi2-z-demo.mp3') }));
