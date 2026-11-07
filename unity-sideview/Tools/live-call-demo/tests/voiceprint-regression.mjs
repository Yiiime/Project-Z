import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { VoiceprintTurnGate } from '../server/voiceprint/turn-gate.mjs';
import { VOICEPRINT_KEYS, loadVoiceprintConfig, publicVoiceprintConfig } from '../server/voiceprint/config.mjs';
import { buildVoiceprintSession } from '../server/voiceprint/session.mjs';

let time = 0;
const gate = new VoiceprintTurnGate({ now: () => time });
const event = (type, conv_id = 'a', extra = {}) => gate.receive({ type, conv_id, ...extra });
const create = id => event('user.conversation.created', id);
const start = (id, active = true) => event('user.audio.transcription.started', id, { sv_active: active });
const text = (id, value, final = false) => event('user.audio.transcription.' + (final ? 'done' : 'delta'), id, { transcript: value });
const verify = id => event('user.audio.speaker.verification', id);

// A colleague can produce fluent, final ASR text. That alone must do nothing.
create('colleague'); start('colleague'); time = 1000;
assert.deepEqual(text('colleague', '这个项目下午讨论。', true), []);
assert.deepEqual(gate.flush(), []);
assert.deepEqual(event('user.conversation.canceled', 'colleague', { reason: 'speaker_mismatch' }), []);
assert.deepEqual(verify('colleague'), []);

create('player'); start('player');
assert.deepEqual(text('player', '等一下'), []);
assert.deepEqual(verify('player'), [], 'verification still observes the hold window');
time += 350;
assert.deepEqual(gate.flush().map(x => x.type), ['player-speaking', 'transcript']);
assert.deepEqual(gate.flush(), [], 'no repeated interruption or repeated subtitle');
assert.deepEqual(event('user.audio.transcription.segment', 'player', { transcript: '等一下，我想补充' }).map(x => x.final), [false]);
const final = text('player', '等一下，我想补充一句。', true);
assert.equal(final.length, 1); assert.equal(final[0].final, true);
assert.deepEqual(text('player', '重复定稿', true), []);

// Verification is bound to one conv_id, not a sticky session-wide flag.
create('next'); start('next'); time += 1000;
assert.deepEqual(verify('player'), []);
assert.deepEqual(text('next', '不能继承上一句的身份'), []);
assert.equal(gate.active.verified, false);
create('inactive'); start('inactive', false); verify('inactive');
assert.deepEqual(text('inactive', '声纹未生效不能降级放行', true), []);
create('missing'); event('user.audio.transcription.started', 'missing'); verify('missing');
assert.deepEqual(text('missing', '缺少标记也不能放行', true), []);

// A verification event may precede the text/start packet; all conditions still apply.
create('ordered'); verify('ordered'); text('ordered', '不是，我想说另一件事');
assert.deepEqual(gate.flush(), []); start('ordered'); time += 350;
assert.equal(gate.flush()[0].type, 'player-speaking');
assert.equal(event('user.conversation.canceled', 'ordered')[0].type, 'discard');
assert.deepEqual(text('ordered', '取消后的迟到定稿', true), []);

create('filler'); start('filler'); verify('filler'); time += 1000;
assert.deepEqual(text('filler', '嗯……'), []);
assert.equal(text('filler', '停')[0].type, 'player-speaking', 'verified short stop commands work');
gate.reset(); assert.deepEqual(verify('filler'), []);
create('timeout'); start('timeout'); text('timeout', '尚未验证'); time += 30001;
assert.deepEqual(verify('timeout'), []); assert.equal(gate.active, null);
console.log('PASS voiceprint gate: bystander rejection, inactive verification, per-turn identity, event ordering, drafts/finals, duplicate/stale events, short commands, timeout and reset');

const folder = await fs.mkdtemp(path.join(os.tmpdir(), 'z-voiceprint-'));
try {
  await fs.writeFile(path.join(folder, '火山声纹配置.env'), 'VOLC_RTC_APP_ID=\nVOLC_RTC_APP_KEY=\n');
  await fs.writeFile(path.join(folder, '火山识别配置.env'), 'VOLC_ASR_API_KEY=existing-api-key\n');
  let config = await loadVoiceprintConfig(folder, {});
  assert.equal(config.rtcAppId, ''); assert.equal(config.asrAccessToken, '');
  assert.equal(config.asrApiKey, 'existing-api-key');
  assert.equal(config.missing.length, 5);
  assert.throws(() => buildVoiceprintSession(config, {}), /not_configured/);
  const env = Object.fromEntries(VOICEPRINT_KEYS.map(key => [key, 'fixture-' + key]));
  env.Z_CALL_VOICE_MODE = 'voiceprint';
  config = await loadVoiceprintConfig(folder, env);
  const publicConfig = publicVoiceprintConfig(config);
  assert.equal(publicConfig.configured, true); assert.equal(publicConfig.active, false);
  for (const value of Object.values(env).filter(x => x.startsWith('fixture-'))) assert(!JSON.stringify(publicConfig).includes(value));
  const request = buildVoiceprintSession(config, { roomId: 'room_test', userId: 'player', agentId: 'z_agent' });
  assert.equal(request.session.AudioConfig.VoicePrint.EnableSV, true);
  assert.deepEqual(request.session.AudioConfig.VoicePrint.IdList, [config.voiceprintId]);
  assert.equal(request.session.AudioConfig.AudioFormat.AnsMode, 0);
  assert.equal(request.session.AudioConfig.VADConfig.ForceBeginThreshold, 0);
  assert.equal(request.session.ASRConfig.ProviderParams.Credential.ApiResourceId, 'volc.seedasr.sauc.duration');
  assert.equal(request.session.ASRConfig.ProviderParams.Credential.ApiKey, config.asrApiKey);
  assert(!('AccessToken' in request.session.ASRConfig.ProviderParams.Credential),'API Key is not a legacy AccessToken');
  const legacy=buildVoiceprintSession({...config,asrSource:'legacy'}, {roomId:'r',userId:'p',agentId:'z'});
  assert.equal(legacy.session.ASRConfig.ProviderParams.Credential.AccessToken,config.asrAccessToken);
  assert(!('ApiKey' in legacy.session.ASRConfig.ProviderParams.Credential));
  assert(!('LLMConfig' in request.session)); assert(!('TTSConfig' in request.session));
  assert.throws(() => buildVoiceprintSession(config, { roomId: 'room', userId: 'same', agentId: 'same' }), /identity/);
  assert.equal(publicVoiceprintConfig(await loadVoiceprintConfig(folder, { ...env, Z_CALL_VOICE_MODE: 'typo' })).configured, false);
  await fs.writeFile(path.join(folder, '火山声纹配置.env'), "VOLC_RTC_APP_ID='local-id' # comment\n");
  assert.equal((await loadVoiceprintConfig(folder, {})).rtcAppId, 'local-id');
  assert.equal((await loadVoiceprintConfig(folder, { VOLC_RTC_APP_ID: 'environment-id' })).rtcAppId, 'environment-id');
  console.log('PASS voiceprint configuration: empty lines, ASR credential separation, environment precedence, no secret disclosure and MMPG session request');
} finally { await fs.rm(folder, { recursive: true, force: true }); }
