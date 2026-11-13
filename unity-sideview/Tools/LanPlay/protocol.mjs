import http from 'node:http';
import https from 'node:https';
import {createHash, timingSafeEqual} from 'node:crypto';

export const digest = value => createHash('sha256').update(value).digest('hex');
export const equal = (a, b) => {
  const left = Buffer.from(a || ''), right = Buffer.from(b || '');
  return left.length === right.length && timingSafeEqual(left, right);
};
export const json = (res, status, value) => {
  if (res.destroyed || res.headersSent) return;
  res.writeHead(status, {'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store'});
  res.end(JSON.stringify(value));
};
export async function body(req, limit = 8_000_000) {
  const chunks = []; let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > limit) throw Object.assign(Error('body_too_large'), {status: 413});
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}
const memoryActions = new Set(['open', 'sync', 'runtime/sync', 'runtime', 'focus', 'intent', 'utterance',
  'commit', 'cancel', 'checkpoint', 'close', 'state', 'speech', 'speech/stream', 'speech/cancel',
  'task/respond', 'task/invite', 'task/receipt', 'story/plan', 'live/open', 'live/close']);
export function allowed(surface, method, pathname) {
  if (surface === 'memory') return method === 'GET' && pathname === '/api/health' ||
    method === 'POST' && pathname.startsWith('/api/game/') && memoryActions.has(pathname.slice(10));
  if (surface !== 'live') return false;
  if (method === 'GET' && pathname === '/health') return true;
  if (method === 'POST' && pathname === '/v1/live/sessions') return true;
  const route = pathname.match(/^\/v1\/live\/sessions\/[a-f0-9-]{36}(?:\/(events|inputs|receipts|control|heartbeat|artifacts|audio-stream|voice|asr|dictation)(?:\/([a-z0-9-]+))?)?$/);
  if (!route) return false;
  return method === 'GET' ? !route[1] || ['events', 'artifacts', 'audio-stream'].includes(route[1]) || route[1] === 'voice' && route[2] === 'config' :
    method === 'POST' && (['inputs', 'receipts', 'control', 'heartbeat', 'asr', 'dictation'].includes(route[1]) || route[1] === 'voice' && ['start', 'joined', 'stop', 'enroll'].includes(route[2]));
}
export function headers(input) {
  return Object.fromEntries(Object.entries(input).filter(([key]) =>
    ['content-type', 'idempotency-key', 'x-z-memory', 'x-live-epoch', 'x-live-executor-lease', 'x-live-call-version'].includes(key)));
}
// pipe() preserves backpressure and streaming boundaries, including SSE and PCM.
export function proxy(req, res, target, options = {}) {
  return new Promise(resolve => {
    const transport = target.protocol === 'https:' ? https : http;
    const outgoing = transport.request(target, {method: req.method,
      headers: {...headers(req.headers), ...options.headers}, ca: options.ca,
      rejectUnauthorized: true, timeout: 180000}, upstream => {
      const responseHeaders = {};
      for (const key of ['content-type', 'content-length', 'cache-control', 'x-content-sha256'])
        if (upstream.headers[key]) responseHeaders[key] = upstream.headers[key];
      res.writeHead(upstream.statusCode, responseHeaders); res.flushHeaders();
      upstream.on('error', () => res.destroy()); upstream.pipe(res);
      upstream.once('end', resolve);
    });
    const cancel = () => {if (!res.writableEnded) outgoing.destroy(); resolve();};
    req.once('aborted', cancel); res.once('close', cancel);
    outgoing.on('timeout', () => outgoing.destroy(Error('timeout')));
    outgoing.on('error', () => {json(res, 502, {error: 'host_unavailable', message: '暂时连接不到主机，请确认主机在线后重试。'}); if (res.headersSent && !res.writableEnded) res.destroy(); resolve();});
    outgoing.once('close', () => req.off('aborted', cancel));
    if (options.bytes !== undefined) outgoing.end(options.bytes); else req.pipe(outgoing);
  });
}
