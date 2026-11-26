// Server-side provider configuration shared by the single-player and LAN launchers.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { parseEnv } from 'node:util';
import { fileURLToPath } from 'node:url';
import { readSourceConfig } from '../PortableHost/config.mjs';

function optionalText(file) {
  try { return fs.readFileSync(file, 'utf8'); }
  catch (error) { if (error.code === 'ENOENT') return ''; throw error; }
}

export function loadProfile({ configDir, stateDir }) {
  const config = path.resolve(configDir || process.env.Z_DEV_CONFIG_DIR || path.join(os.homedir(), '.config/project-z/services'));
  const supplied = parseEnv(optionalText(path.join(config, '.env.local')));
  const profile = readSourceConfig(config);
  for (const key of ['DEEPSEEK_MODEL', 'GAME_MODEL_NAME', 'ARK_API_KEY', 'ARK_MODEL', 'Z_DEV_PROVIDER',
    'FFMPEG_PATH', 'NETEASE_SESSION_FILE', 'Z_NETEASE_AUTO_RENEW', 'Z_SONG_CATALOG']) {
    if (supplied[key]) profile[key] = supplied[key];
  }
  const env = { ...profile, ...process.env };
  const mode = env.Z_DEV_PROVIDER || 'team';
  if (!['team', 'direct'].includes(mode)) throw Error('Z_DEV_PROVIDER 需要填写 team 或 direct。');
  Object.assign(env, {
    Z_MEMORY_CONFIG: path.resolve(env.Z_MEMORY_CONFIG || path.join(config, 'model.json')),
    Z_TTS_CONFIG: path.resolve(env.Z_TTS_CONFIG || path.join(config, 'tts.json')),
    Z_LIVE_CONFIG_DIR: config, Z_LIVE_VOICE_CONFIG_DIR: config,
    NETEASE_SESSION_FILE: path.resolve(config, env.NETEASE_SESSION_FILE || 'netease-session.json')
  });
  if (env.FFMPEG_PATH?.includes(path.sep)) env.FFMPEG_PATH = path.resolve(config, env.FFMPEG_PATH);
  if (env.Z_SONG_CATALOG) env.Z_SONG_CATALOG = path.resolve(config, env.Z_SONG_CATALOG);
  if (mode === 'direct') {
    fs.mkdirSync(stateDir, { recursive:true, mode:0o700 });
    env.Z_TEAM_MODEL_CONFIG = path.resolve(stateDir, 'direct-mode.json');
    // This constant file contains no credentials and can be shared between player services.
    if (optionalText(env.Z_TEAM_MODEL_CONFIG) !== '{"enabled":false}')
      fs.writeFileSync(env.Z_TEAM_MODEL_CONFIG, '{"enabled":false}', { mode:0o600 });
  } else {
    const fallback = fileURLToPath(new URL('../team-model/client.json', import.meta.url));
    env.Z_TEAM_MODEL_CONFIG = path.resolve(env.Z_TEAM_MODEL_CONFIG || (fs.existsSync(path.join(config, 'client.json')) ? path.join(config, 'client.json') : fallback));
    if (!JSON.parse(fs.readFileSync(env.Z_TEAM_MODEL_CONFIG)).enabled) throw Error('team 模式的 client.json 未启用团队模型。');
  }
  const tts = JSON.parse(optionalText(env.Z_TTS_CONFIG) || '{}');
  if (!env.VOLC_TTS_API_KEY && tts.apiKey) env.VOLC_TTS_API_KEY = tts.apiKey;
  return { env, mode };
}
