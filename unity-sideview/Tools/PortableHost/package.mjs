import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {execFileSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {readSourceConfig, selectConfig} from './config.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../../..');
const tools = path.resolve(here, '..');
const args = process.argv.slice(2);
function arg(name, required = true) {
  const index = args.indexOf(name), value = index >= 0 ? args[index + 1] : undefined;
  if (required && (!value || value.startsWith('--'))) throw Error('Missing ' + name);
  return value ? path.resolve(value) : undefined;
}
function run(command, argv, options = {}) { return execFileSync(command, argv, {stdio:'pipe', ...options}); }
function secret(file, value) { fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', {mode:0o600}); fs.chmodSync(file, 0o600); }
const app = fs.realpathSync(arg('--app'));
if (app === repo || app.startsWith(repo + path.sep) || !app.endsWith('.app')) throw Error('Use an app outside the Git repository.');
const source = arg('--config-source'), voiceSource = arg('--voice-source');
const node = arg('--node'), ffmpegArm = arg('--ffmpeg-arm64'), ffmpegIntel = arg('--ffmpeg-x64');
const nodeLicense = arg('--node-license'), ffmpegLicense = arg('--ffmpeg-license');
const neteaseSource = arg('--netease-session', false), ttsSource = arg('--tts-config', false);
const host = path.join(app, 'Contents/Resources/ProjectZHost');
const plist = path.join(app, 'Contents/Info.plist');
const plistTool = '/usr/libexec/PlistBuddy';
const existingPlayer = run(plistTool, ['-c', 'Print :CFBundleExecutable', plist], {encoding:'utf8'}).trim();
const previous = fs.existsSync(path.join(host, 'manifest.json')) ? JSON.parse(fs.readFileSync(path.join(host, 'manifest.json'))) : null;
const playerExecutable = existingPlayer === 'ProjectZLauncher' ? previous?.playerExecutable : existingPlayer;
if (!playerExecutable || path.basename(playerExecutable) !== playerExecutable || !fs.existsSync(path.join(app, 'Contents/MacOS', playerExecutable))) throw Error('Original Unity player missing.');
const values = readSourceConfig(source);
const {loadVoiceprintConfig} = await import(pathToFileURL(path.join(tools, 'live-service/server/voiceprint/config.mjs')));
const voice = await loadVoiceprintConfig(voiceSource, {});
Object.assign(values, selectConfig({
  VOLC_RTC_APP_ID:voice.rtcAppId, VOLC_RTC_APP_KEY:voice.rtcAppKey,
  VOLC_ACCESSKEY:voice.accessKey, VOLC_SECRETKEY:voice.secretKey,
  VOLC_ASR_API_KEY:voice.asrApiKey || values.VOLC_ASR_API_KEY,
  VOLC_ASR_APP_ID:voice.asrAppId, VOLC_ASR_ACCESS_KEY:voice.asrAccessToken,
  VOLC_VOICEPRINT_ID:voice.voiceprintId, Z_CALL_VOICE_MODE:voice.mode, Z_CALL_ASR_SOURCE:voice.asrSource,
}));
if (['DEEPSEEK_API_KEY', 'VOLC_TTS_API_KEY', 'VOLC_ASR_API_KEY'].some(k => !values[k])) throw Error('Required model/speech configuration is missing.');
if (voice.mode !== 'voiceprint' || !voice.validMode || voice.missing.length) throw Error('Complete native voice configuration is required.');
fs.rmSync(host, {recursive:true, force:true});
for (const directory of ['Config', 'Runtime', 'Tools']) fs.mkdirSync(path.join(host, directory), {recursive:true, mode:0o700});
// Copy only versioned service inputs. Untracked .env, caches and user data cannot leak.
const tracked = run('git', ['ls-files', '-z', 'unity-sideview/Tools/MemoryLab', 'unity-sideview/Tools/live-service', 'unity-sideview/Tools/team-model'], {cwd:repo, encoding:'utf8'}).split('\0').filter(Boolean);
for (const relative of tracked) {
  const rel = path.relative('unity-sideview/Tools', relative);
  if (rel.split(path.sep).some(p => p.startsWith('.') || ['tests', 'test-results', 'verification', 'node_modules'].includes(p)) || /\.(?:env|log|sqlite|key|pem|p12)$/.test(rel)) continue;
  // Only the direct-provider adapter imports are needed; never distribute team login configuration.
  if (rel.startsWith('team-model/') && !['client.mjs', 'common.mjs', 'adapter.mjs'].includes(path.basename(rel))) continue;
  const target = path.join(host, 'Tools', rel);
  fs.mkdirSync(path.dirname(target), {recursive:true});
  fs.copyFileSync(path.join(repo, relative), target);
}
const npm = run('/usr/bin/which', ['npm'], {encoding:'utf8'}).trim();
for (const service of ['MemoryLab', 'live-service']) {
  run(process.execPath, [npm, 'ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund'], {cwd:path.join(host, 'Tools', service)});
}
for (const name of ['launch.mjs', 'service-runner.mjs', 'config.mjs']) fs.copyFileSync(path.join(here, name), path.join(host, name));
for (const [from, name] of [[node, 'node'], [ffmpegArm, 'ffmpeg-arm64'], [ffmpegIntel, 'ffmpeg-x64']]) {
  fs.copyFileSync(from, path.join(host, 'Runtime', name)); fs.chmodSync(path.join(host, 'Runtime', name), 0o755);
}
for (const arch of ['arm64', 'x86_64']) run('/usr/bin/lipo', [path.join(host, 'Runtime/node'), '-verify_arch', arch]);
run('/usr/bin/lipo', [path.join(host, 'Runtime/ffmpeg-arm64'), '-verify_arch', 'arm64']);
run('/usr/bin/lipo', [path.join(host, 'Runtime/ffmpeg-x64'), '-verify_arch', 'x86_64']);
fs.copyFileSync(nodeLicense, path.join(host, 'Runtime/NODE-LICENSE.txt'));
fs.copyFileSync(ffmpegLicense, path.join(host, 'Runtime/FFMPEG-LICENSE.txt'));
secret(path.join(host, 'Config/providers.json'), values);
secret(path.join(host, 'Config/team-disabled.json'), {enabled:false});
secret(path.join(host, 'Config/model.json'), {provider:'deepseek', baseUrl:'https://api.deepseek.com', model:'deepseek-chat', apiKey:values.DEEPSEEK_API_KEY});
const tts = ttsSource ? JSON.parse(fs.readFileSync(ttsSource)) : {};
secret(path.join(host, 'Config/tts.json'), {enabled:true, apiKey:values.VOLC_TTS_API_KEY, ...(tts.voices ? {voices:tts.voices} : {})});
if (neteaseSource) {
  const session = JSON.parse(fs.readFileSync(neteaseSource));
  if (!session.cookie?.MUSIC_U) throw Error('Music session is invalid.');
  secret(path.join(host, 'Config/netease-session.json'), {cookie:session.cookie});
}
const sourceRevision = run('git', ['rev-parse', 'HEAD'], {cwd:repo, encoding:'utf8'}).trim();
const manifest = {formatVersion:1, packageId:randomUUID(), sourceRevision, playerExecutable, platforms:['darwin-arm64', 'darwin-x64'], createdAt:new Date().toISOString()};
secret(path.join(host, 'manifest.json'), manifest);
const launcher = path.join(app, 'Contents/MacOS/ProjectZLauncher');
fs.writeFileSync(launcher, '#!/bin/sh\nset -eu\nbase="$(CDPATH= cd -- "$(dirname -- "$0")/../Resources/ProjectZHost" && pwd)"\nexec "$base/Runtime/node" "$base/launch.mjs" "$@"\n', {mode:0o755});
fs.chmodSync(launcher, 0o755);
run(plistTool, ['-c', 'Set :CFBundleExecutable ProjectZLauncher', plist]);
// This app needs local microphone permission for live calls.
try { run(plistTool, ['-c', 'Set :NSMicrophoneUsageDescription 用于直播连麦与语音识别。', plist]); }
catch { run(plistTool, ['-c', 'Add :NSMicrophoneUsageDescription string 用于直播连麦与语音识别。', plist]); }
run('/usr/bin/codesign', ['--force', '--deep', '--sign', '-', app]);
run('/usr/bin/codesign', ['--verify', '--deep', '--strict', app]);
console.log(JSON.stringify({packaged:true, sourceRevision, platforms:manifest.platforms, voiceConfigured:true, musicSessionIncluded:Boolean(neteaseSource)}));
