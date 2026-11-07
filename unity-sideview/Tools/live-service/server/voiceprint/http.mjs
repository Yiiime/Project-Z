import fs from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { loadVoiceprintConfig, publicVoiceprintConfig } from './config.mjs';
import { rtcApi, safeProviderCode } from './volc-client.mjs';

export function validateEnrollmentWav(audio) {
  if (audio.length < 44 || audio.toString('ascii', 0, 4) !== 'RIFF' || audio.toString('ascii', 8, 12) !== 'WAVE') throw Error('voiceprint_invalid_wav');
  let format, data;
  for (let p = 12; p + 8 <= audio.length;) {
    const size = audio.readUInt32LE(p + 4), end = p + 8 + size;
    if (end > audio.length) throw Error('voiceprint_invalid_wav');
    const tag = audio.toString('ascii', p, p + 4);
    if (tag === 'fmt ' && size >= 16) format = audio.subarray(p + 8, end);
    if (tag === 'data') data = audio.subarray(p + 8, end);
    p = end + (size % 2);
  }
  if (!format || !data || format.readUInt16LE(0) !== 1 || format.readUInt16LE(2) !== 1 || format.readUInt32LE(4) !== 16000 || format.readUInt16LE(14) !== 16 || data.length % 2) throw Error('voiceprint_invalid_wav');
  const seconds = data.length / 32000;
  if (seconds < 15 || seconds > 30) throw Error('voiceprint_invalid_duration');
  return seconds;
}

export function createVoiceprintHttp(root, { api = rtcApi } = {}) {
  let registering = false;
  return async request => {
    const pathname = new URL(request.url).pathname;
    if (!['/api/voiceprint/config', '/api/voiceprint/enroll'].includes(pathname)) return null;
    const reply = (body, status = 200) => Response.json(body, { status, headers: { 'Cache-Control': 'no-store' } });
    const config = await loadVoiceprintConfig(root);
    if (pathname.endsWith('/config') && request.method === 'GET') return reply(publicVoiceprintConfig(config));
    if (!pathname.endsWith('/enroll') || request.method !== 'POST') return reply({ error: 'method_not_allowed' }, 405);
    if (request.headers.get('Origin') !== new URL(request.url).origin) return reply({ error: 'invalid_origin' }, 403);
    if (registering) return reply({ error: 'voiceprint_enrollment_busy' }, 409);
    if (!config.validMode || config.missing.some(key => key !== 'VOLC_VOICEPRINT_ID')) return reply({ error: 'voiceprint_not_configured' }, 503);
    if (config.voiceprintId) return reply({ enrolled: true });
    registering = true;
    try {
      const audio = Buffer.from(await request.arrayBuffer());
      if (audio.length > 1000000) return reply({ error: 'voiceprint_audio_too_large' }, 413);
      validateEnrollmentWav(audio);
      const { status, data } = await api(config, 'RegisterVoicePrint', { AudioFormat: 0, Audio: audio.toString('base64'), AudioName: 'z_player_' + randomUUID().slice(0, 12), Version: 2 });
      const error = data?.ResponseMetadata?.Error;
      if (status !== 200 || error || typeof data.Result !== 'string' || !data.Result) return reply({ error: 'voiceprint_registration_failed', providerCode: safeProviderCode(error?.Code) }, 502);
      const directory = path.join(root, 'data/voiceprint');
      await fs.mkdir(directory, { recursive: true, mode: 0o700 });
      const temp = path.join(directory, randomUUID() + '.tmp');
      await fs.writeFile(temp, JSON.stringify({ appId: config.rtcAppId, id: data.Result, version: 2, createdAt: new Date().toISOString() }), { mode: 0o600 });
      await fs.rename(temp, path.join(directory, 'profile.json'));
      // The recording was held only in memory; only the returned voiceprint ID is retained.
      return reply({ enrolled: true });
    } catch (error) {
      return reply({ error: /^voiceprint_invalid_/.test(error.message) ? error.message : 'voiceprint_registration_failed' }, 400);
    } finally { registering = false; }
  };
}
