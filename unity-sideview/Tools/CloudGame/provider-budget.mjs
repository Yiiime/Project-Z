// Applied inside each isolated cloud worker, immediately before a billable request.
import path from 'node:path';
import {DatabaseSync} from 'node:sqlite';
import {GameBudget} from './budget.mjs';

const modelUrls = new Set(['https://api.deepseek.com/chat/completions','https://api.deepseek.com/v1/chat/completions']);
const speechUrl = 'https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse';
const maxCompletion = 4096;

function denied(status, reason) {
  return Response.json({error:{code:reason,message:reason}},{status});
}

export function createCloudProviderFetch() {
  const principal = process.env.Z_CLOUD_PLAYER_ID;
  const hostRoot = process.env.Z_LAN_HOST_ROOT || '';
  if (!/^[a-f0-9]{32}$/.test(principal || '') || !path.isAbsolute(hostRoot))
    throw Error('invalid_cloud_provider_identity');
  const db = new DatabaseSync(path.join(hostRoot,'provider-usage.sqlite'));
  db.exec('PRAGMA busy_timeout=3000; PRAGMA journal_mode=WAL;');
  const budget = new GameBudget(db);
  return async (url, options = {}) => {
    if (options.method !== 'POST' || typeof options.body !== 'string' || Buffer.byteLength(options.body) > 512_000)
      return denied(400,'invalid_provider_request');
    let value;
    try { value = JSON.parse(options.body); } catch { return denied(400,'invalid_provider_json'); }
    const endpoint = String(url);
    let units, payload = options.body;
    if (modelUrls.has(endpoint)) {
      if (!Array.isArray(value?.messages) || !value.messages.length || (value.n !== undefined && value.n !== 1))
        return denied(400,'invalid_model_request');
      const requested = value.max_tokens ?? 2048;
      if (!Number.isSafeInteger(requested) || requested < 1 || value.max_completion_tokens !== undefined)
        return denied(400,'invalid_model_limit');
      value.max_tokens = Math.min(requested,maxCompletion);
      payload = JSON.stringify(value);
      // Charge UTF-8 bytes plus chat framing and the full allowed completion up front.
      // This deliberately overcounts most text and covers retry, background and stream calls.
      units = {providerModelCalls:1,providerTokenUnits:Buffer.byteLength(payload) + 16 * value.messages.length + 128 + value.max_tokens};
    } else if (endpoint === speechUrl) {
      const text = value?.req_params?.text;
      if (typeof text !== 'string' || !text.length) return denied(400,'invalid_speech_request');
      units = {providerTtsChars:text.length};
    } else return denied(403,'provider_not_enabled');
    try {
      budget.charge(principal,units);
    } catch (error) {
      return denied(error.status === 429 ? 429 : 503, error.status === 429 ? 'provider_daily_limit' : 'provider_budget_unavailable');
    }
    return fetch(endpoint,{...options,body:payload,redirect:'error'});
  };
}
