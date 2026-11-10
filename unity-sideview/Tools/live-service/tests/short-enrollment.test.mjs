import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {NativeVoiceService} from '../server/voiceprint/native-service.mjs';
import {validateEnrollmentWav} from '../server/voiceprint/http.mjs';

function recording(seconds, speechSeconds) {
  const samples = seconds * 16000;
  const wav = Buffer.alloc(44 + 2 * samples);
  wav.write('RIFF'); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8);
  wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(16000, 24); wav.writeUInt32LE(32000, 28);
  wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34);
  wav.write('data', 36); wav.writeUInt32LE(samples * 2, 40);
  for (let i = 0; i < speechSeconds * 16000; i++) {
    wav.writeInt16LE(Math.round(Math.sin(i / 12) * 5000), 44 + 2 * i);
  }
  return wav;
}
test('short enrollment accepts five seconds and older long recordings', () => {
  assert.equal(validateEnrollmentWav(recording(5, 5)), 5);
  assert.equal(validateEnrollmentWav(recording(25, 15)), 25);
});
test('silence and insufficient speech cannot register a voice', () => {
  assert.throws(() => validateEnrollmentWav(recording(5, 0)), /invalid_speech/);
  assert.throws(() => validateEnrollmentWav(recording(6, 2)), /invalid_speech/);
  assert.throws(() => validateEnrollmentWav(recording(3, 3)), /invalid_duration/);
});

test('a five-second registration uses a valid provider name and persists for this owner', async t => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'z-short-enrollment-'));
  t.after(() => fs.rm(root, {recursive:true, force:true}));
  const sample = recording(5, 5);
  let requests = 0;
  const service = new NativeVoiceService({
    profileRoot: root,
    load: async () => ({validMode:true, rtcAppId:'fixture', missing:[]}),
    api: async (_config, method, payload) => {
      requests++;
      assert.equal(method, 'RegisterVoicePrint');
      assert.match(payload.AudioName, /^z_[a-f0-9]{30}$/);
      assert.deepEqual(Buffer.from(payload.Audio, 'base64'), sample);
      return {status:200, data:{Result:'fixture-profile'}};
    }
  });
  await service.enroll('owner-a', sample);
  await service.enroll('owner-a', sample);
  assert.equal(requests, 1);
  assert.equal((await service.config('owner-a')).voiceprintId, 'fixture-profile');
  assert.equal((await service.config('owner-b')).voiceprintId, '');
});
