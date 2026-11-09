import { Signer } from '@volcengine/openapi';
import WebSocket from 'ws';

export const MMPG_ENDPOINT = 'wss://interactai.volcvideo.com/api/v2/mmpg';
export function signedHeaders(config, url, { method = 'GET', body = '', date } = {}) {
  if (!config.accessKey || !config.secretKey) throw new Error('rtc_credentials_missing');
  const target = new URL(url);
  const request = { region: 'cn-north-1', method, pathname: target.pathname,
    params: Object.fromEntries(target.searchParams), headers: { Host: target.host }, body };
  if (body) request.headers['Content-Type'] = 'application/json';
  new Signer(request, 'rtc').addAuthorization({ accessKeyId: config.accessKey, secretKey: config.secretKey }, date);
  return request.headers;
}

export async function rtcApi(config, action, payload, { fetchImpl = fetch, signal } = {}) {
  if (!['ListVoicePrint', 'RegisterVoicePrint'].includes(action)) throw new Error('rtc_action_not_allowed');
  const url = `https://rtc.volcengineapi.com/?Action=${action}&Version=2024-12-01`;
  const body = JSON.stringify({ ...payload, AppId: config.rtcAppId });
  const response = await fetchImpl(url, { method: 'POST', headers: signedHeaders(config, url, { method: 'POST', body }), body, signal: signal || AbortSignal.timeout(20000) });
  const data = await response.json();
  return { status: response.status, data };
}

export function connectMmpg(config, { WebSocketClass = WebSocket } = {}) {
  return new WebSocketClass(MMPG_ENDPOINT, { headers: signedHeaders(config, MMPG_ENDPOINT),
    handshakeTimeout: 12000, maxPayload: 2 * 1024 * 1024, perMessageDeflate: false });
}

// Provider error messages may echo credentials. Never log raw bodies, headers or SDK errors.
export function safeProviderCode(value) {
  return typeof value === 'string' && /^[A-Za-z][A-Za-z0-9_.-]{0,80}$/.test(value) ? value : 'provider_error';
}
