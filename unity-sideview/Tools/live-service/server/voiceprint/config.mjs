import fs from 'node:fs/promises';
import path from 'node:path';

export const VOICEPRINT_KEYS = Object.freeze([
  'VOLC_RTC_APP_ID', 'VOLC_RTC_APP_KEY', 'VOLC_ACCESSKEY', 'VOLC_SECRETKEY',
  'VOLC_ASR_API_KEY', 'VOLC_ASR_APP_ID', 'VOLC_ASR_ACCESS_KEY', 'VOLC_VOICEPRINT_ID',
]);

// Intentionally does not read arbitrary env variables or expose their values.
function valueFrom(source, name) {
  const match = source.match(new RegExp('^[ \\t]*(?:export[ \\t]+)?' + name + '[ \\t]*=[ \\t]*([^\\r\\n]*)', 'm'));
  if (!match) return '';
  const value = match[1].trim();
  if (/^["']/.test(value)) return value.match(/^(["'])(.*?)\1\s*(?:#.*)?$/)?.[2]?.trim() || '';
  return value.replace(/\s+#.*$/, '').trim();
}

export async function loadVoiceprintConfig(root, environment = process.env) {
  const sources = [];
  for (const file of ['火山声纹配置.env', '火山识别配置.env', '.env.local']) {
    try { sources.push(await fs.readFile(path.join(root, file), 'utf8')); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  const values = {};
  for (const name of [...VOICEPRINT_KEYS, 'Z_CALL_VOICE_MODE', 'Z_CALL_ASR_SOURCE']) {
    values[name] = environment[name]?.trim() || sources.map(source => valueFrom(source, name)).find(Boolean) || '';
  }
  const mode = values.Z_CALL_VOICE_MODE || 'disabled';
  const asrSource = values.Z_CALL_ASR_SOURCE || 'api-key';
  let voiceprintId = values.VOLC_VOICEPRINT_ID;
  if (!voiceprintId) {
    try {
      const profile = JSON.parse(await fs.readFile(path.join(root, 'data/voiceprint/profile.json'), 'utf8'));
      if (profile.appId === values.VOLC_RTC_APP_ID && profile.version === 2 && typeof profile.id === 'string') voiceprintId = profile.id;
    } catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
  }
  const required = ['VOLC_RTC_APP_ID', 'VOLC_RTC_APP_KEY', 'VOLC_ACCESSKEY', 'VOLC_SECRETKEY',
    ...(asrSource === 'legacy' ? ['VOLC_ASR_APP_ID', 'VOLC_ASR_ACCESS_KEY'] : ['VOLC_ASR_API_KEY'])];
  return {
    mode, validMode: ['disabled', 'voiceprint'].includes(mode) && ['api-key', 'legacy'].includes(asrSource), asrSource,
    rtcAppId: values.VOLC_RTC_APP_ID, rtcAppKey: values.VOLC_RTC_APP_KEY,
    accessKey: values.VOLC_ACCESSKEY, secretKey: values.VOLC_SECRETKEY,
    asrApiKey: values.VOLC_ASR_API_KEY, asrAppId: values.VOLC_ASR_APP_ID, asrAccessToken: values.VOLC_ASR_ACCESS_KEY,
    voiceprintId,
    missing: [...required.filter(name => !values[name]), ...(!voiceprintId ? ['VOLC_VOICEPRINT_ID'] : [])],
  };
}

export function publicVoiceprintConfig(config) {
  return {
    provider: 'volc-mmpg', requested: config.mode === 'voiceprint',
    configured: config.validMode && config.missing.length === 0,
    credentialsConfigured: config.validMode && config.missing.every(name => name === 'VOLC_VOICEPRINT_ID'),
    needsEnrollment: !config.voiceprintId, asrSource: config.asrSource,
    // Actual activity belongs to each live connection, never inferred from credentials.
    validated: false, active: false,
    missing: [...config.missing], invalidMode: !config.validMode,
  };
}
