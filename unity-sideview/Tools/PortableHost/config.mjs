import fs from 'node:fs';
import path from 'node:path';

// Deliberately excludes host paths, personal saves and unrelated credentials.
export const CONFIG_KEYS = Object.freeze([
  'DEEPSEEK_API_KEY', 'VOLC_TTS_API_KEY', 'VOLC_ASR_API_KEY',
  'VOLC_RTC_APP_ID', 'VOLC_RTC_APP_KEY', 'VOLC_ACCESSKEY', 'VOLC_SECRETKEY',
  'VOLC_ASR_APP_ID', 'VOLC_ASR_ACCESS_KEY', 'VOLC_VOICEPRINT_ID',
  'Z_CALL_VOICE_MODE', 'Z_CALL_ASR_SOURCE',
]);
export function selectConfig(input) {
  return Object.fromEntries(CONFIG_KEYS.flatMap(key => {
    const value = input[key];
    if (value === undefined || value === '') return [];
    if (typeof value !== 'string' || /[\r\n\0]/.test(value)) throw Error('Invalid configuration field: ' + key);
    return [[key, value]];
  }));
}
export function readSourceConfig(directory) {
  const values = {};
  for (const file of ['.env.local', 'DeepSeek配置.env', '火山语音配置.env', '火山识别配置.env', '火山声纹配置.env', '.env']) {
    const source = path.join(directory, file);
    if (!fs.existsSync(source)) continue;
    for (const line of fs.readFileSync(source, 'utf8').split(/\r?\n/)) {
      const match = line.match(/^\s*(?:export\s+)?([A-Z_]+)\s*=\s*(.*)$/);
      if (!match || !CONFIG_KEYS.includes(match[1]) || values[match[1]]) continue;
      const raw = match[2].trim(), quoted = raw.match(/^(['"])(.*?)\1\s*(?:#.*)?$/);
      values[match[1]] = quoted ? quoted[2] : /^['"]/.test(raw) ? '' : raw.replace(/\s+#.*$/, '').trim();
    }
  }
  return selectConfig(values);
}
export function hostEnvironment(config, locations, inherited = process.env) {
  const { tools, runtime, session, host, memoryPort, livePort } = locations;
  const safe = Object.fromEntries(['HOME', 'USER', 'LOGNAME', 'LANG', 'LC_ALL', 'TMPDIR'].flatMap(k => inherited[k] ? [[k, inherited[k]]] : []));
  return {
    ...safe, ...selectConfig(config),
    PATH: path.join(host, 'Runtime') + ':/usr/bin:/bin:/usr/sbin:/sbin',
    GAME_MODEL_API_KEY: config.DEEPSEEK_API_KEY,
    GAME_MODEL_NAME: 'deepseek-chat',
    Z_TEAM_MODEL_CONFIG: path.join(host, 'Config/team-disabled.json'),
    Z_MEMORY_PORT: String(memoryPort), Z_UNITY_PORT: String(livePort),
    Z_MEMORY_CONFIG: path.join(runtime, 'config/model.json'),
    Z_TTS_CONFIG: path.join(runtime, 'config/tts.json'),
    Z_MEMORY_DB: path.join(runtime, 'memory.sqlite'),
    Z_MEMORY_SAVE_ROOT: path.join(runtime, 'saves'),
    Z_LIVE_CONFIG_DIR: path.join(host, 'Config'),
    Z_LIVE_VOICE_CONFIG_DIR: path.join(host, 'Config'),
    Z_LIVE_RUNTIME_DIR: path.join(runtime, 'live'),
    Z_LIVE_CONNECTION_FILE: path.join(session, 'connection.json'),
    Z_LIVE_CHARACTER_SERVICE_URL: `http://127.0.0.1:${memoryPort}`,
    NETEASE_SESSION_FILE: path.join(runtime, 'netease/session.json'),
    Z_NETEASE_AUTO_RENEW: '1',
    FFMPEG_PATH: path.join(host, 'Runtime', `ffmpeg-${process.arch}`),
  };
}
