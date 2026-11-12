import {deliveryText} from '../utterance.js';
/**
 * Server-only Vivi 2.0 adapter, using the official HTTP SSE TTS endpoint.
 * https://docs.volcengine.com/docs/DoubaoVoice/HTTPChunkedSSEUnidirectionalStreaming-V3?lang=zh
 * All deduplication, concurrency and rate limits are local to one Worker isolate.
 */
import {Z_VOICE_PROFILE} from '../ui/talent-config.js';
const TTS_ENDPOINT = 'https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse';
const TTS_SPEAKER = Z_VOICE_PROFILE.speaker;
const TTS_RESOURCE = Z_VOICE_PROFILE.speakingModel;
const TTS_MAX_TEXT = 500;
const TTS_TIMEOUT = 20000;
const TTS_MAX_BODY = 8192;
const TTS_MAX_WIRE = 4 * 1024 * 1024;
const TTS_MAX_EVENT = 512 * 1024;
const TTS_MAX_AUDIO = 1024 * 1024;
const TTS_MAX_WORDS = 2048;
const TTS_MAX_ATTEMPTS = 1024;
const TTS_ATTEMPT_TTL = 5 * 60 * 1000;
const TTS_WINDOW = 60000;
const TTS_PER_WINDOW = 30;
const TTS_ACTIVE_PER_USER = 2;
const TTS_ACTIVE_TOTAL = 6;
const TTS_UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const TTS_MOODS = new Set(['happy', 'warm', 'serious', 'thinking']);
const ttsAttempts = new Map();
const ttsQuotas = new Map();
let ttsActive = 0;
const ttsEncoder = new TextEncoder();

class TtsError extends Error {
  constructor(status, code, message, retryAfterMs = 0) { super(message); Object.assign(this, { status, code, retryAfterMs }); }
}
const ttsUnavailable = () => new TtsError(502, 'tts_provider_unavailable', 'Vivi 的声音暂时连接不上，文字会继续显示。');
const ttsInvalid = () => new TtsError(502, 'tts_invalid_response', '这次语音没有完整生成，文字会继续显示。');
const ttsTimeout = () => new TtsError(504, 'tts_provider_timeout', '这次声音准备得有点久，文字会继续显示。');
const ttsHeaders = extra => ({ 'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff', 'Cross-Origin-Resource-Policy': 'same-origin', ...extra });
function ttsRecord(error) {
  const safe = error instanceof TtsError ? error : ttsUnavailable();
  return { error: safe.code, message: safe.message, ...(safe.retryAfterMs ? { retryAfterMs: safe.retryAfterMs } : {}) };
}
function ttsJson(value, status = 200, extra = {}) {
  return new Response(JSON.stringify(value), { status, headers: ttsHeaders({ 'Content-Type': 'application/json; charset=utf-8', ...extra }) });
}
function ttsDiagnostic(context, classification, status = 0) {
  // Never include credentials, caller text/identity, provider bodies or errors.
  try { context.diagnostics?.({ operation: 'tts', classification, status: Number(status) || 0 }); } catch {}
}
const ttsObject = value => value !== null && typeof value === 'object' && !Array.isArray(value);
function ttsPlain(value, limit, required = false) {
  if (typeof value !== 'string' || value.length > limit || /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(value) || (required && !value.trim())) throw new TtsError(400, 'tts_invalid_input', '语音文字格式不正确。');
  return value.trim();
}
function ttsIdentity(request) {
  const id = request.headers.get('oai-authenticated-user-id')?.trim();
  if (!id || id.length > 256 || /[\u0000-\u001f\u007f]/.test(id)) throw new TtsError(401, 'authentication_required', '请先登录后开启 Z 的声音。');
  return id;
}
async function ttsReadInput(request, url) {
  if (request.headers.get('Origin') !== url.origin) throw new TtsError(403, 'invalid_origin', '请从当前直播页面开启声音。');
  if ((request.headers.get('Content-Type') || '').split(';')[0].trim().toLowerCase() !== 'application/json') throw new TtsError(415, 'json_required', '语音请求需要使用 JSON。');
  const length = request.headers.get('Content-Length');
  if (length && (!/^\d+$/.test(length) || Number(length) > TTS_MAX_BODY)) throw new TtsError(413, 'tts_body_too_large', '语音文字过长。');
  const reader = request.body?.getReader();
  if (!reader) throw new TtsError(400, 'tts_invalid_input', '语音请求内容无效。');
  const decoder = new TextDecoder('utf-8', { fatal: true }); let size = 0, text = '';
  try {
    for (;;) {
      const part = await reader.read(); if (part.done) break;
      size += part.value.byteLength;
      if (size > TTS_MAX_BODY) { void reader.cancel().catch(() => {}); throw new TtsError(413, 'tts_body_too_large', '语音文字过长。'); }
      text += decoder.decode(part.value, { stream: true });
    }
    text += decoder.decode();
    const input = JSON.parse(text);
    if (!ttsObject(input) || Object.keys(input).some(key => !['requestId', 'text', 'mood', 'delivery'].includes(key)) || typeof input.requestId !== 'string' || !TTS_UUID.test(input.requestId)) throw new TtsError(400, 'tts_invalid_input', '语音请求编号或字段无效。');
    const mood = input.mood ?? 'happy';
    if (!TTS_MOODS.has(mood)) throw new TtsError(400, 'tts_invalid_input', '语音语气无效。');
    return { requestId: input.requestId.toLowerCase(), text: ttsPlain(input.text, TTS_MAX_TEXT, true), mood, delivery:input.delivery===undefined?'':deliveryText(input.delivery) };
  } catch (error) {
    if (error instanceof TtsError) throw error;
    throw new TtsError(400, 'tts_invalid_input', '语音请求内容无效。');
  } finally { reader.releaseLock(); }
}
async function ttsHash(value, context) {
  return [...new Uint8Array(await context.crypto.subtle.digest('SHA-256', ttsEncoder.encode(value)))].map(b => b.toString(16).padStart(2, '0')).join('');
}
function ttsPrune(now) {
  for (const [id, entry] of ttsAttempts) if (entry.expiresAt <= now) ttsAttempts.delete(id);
  for (const [id, quota] of ttsQuotas) if (!quota.active && quota.startedAt + TTS_WINDOW <= now) ttsQuotas.delete(id);
}
function ttsReserve(id, now) {
  let quota = ttsQuotas.get(id);
  if (!quota && ttsQuotas.size >= TTS_MAX_ATTEMPTS) throw new TtsError(429, 'tts_service_busy', '声音服务现在有点忙，请稍后再试。', 5000);
  if (!quota) { quota = { startedAt: now, count: 0, active: 0 }; ttsQuotas.set(id, quota); }
  if (now - quota.startedAt >= TTS_WINDOW) { quota.startedAt = now; quota.count = 0; }
  if (quota.active >= TTS_ACTIVE_PER_USER || ttsActive >= TTS_ACTIVE_TOTAL) throw new TtsError(429, 'tts_in_progress', 'Z 正在准备声音，稍等一下。', 1500);
  if (quota.count >= TTS_PER_WINDOW) throw new TtsError(429, 'tts_rate_limited', '声音请求有一点密集，请稍后再试。', Math.max(1000, quota.startedAt + TTS_WINDOW - now));
  quota.active++; quota.count++; ttsActive++;
  let released = false;
  return () => { if (!released) { released = true; quota.active = Math.max(0, quota.active - 1); ttsActive = Math.max(0, ttsActive - 1); } };
}
function ttsPayload(value, userHash, format) {
  const styles = {
    happy: '用自然、清亮、有活力的年轻女主播语气说话，轻松俏皮，不要夸张卖萌。',
    warm: '用自然温柔的年轻女主播语气说话，亲切放松，不要刻意拖长字音。',
    serious: '用年轻女主播认真而清楚的语气说话，语速稍放稳，保持明亮自然。',
    thinking: '用自然好奇的年轻女主播语气说话，像随口向熟人询问，不要播报腔。',
  };
  return { user: { uid: `z_${userHash}` }, req_params: {
    text: value.text, speaker: TTS_SPEAKER,
    audio_params: { format: format || 'mp3', sample_rate: 24000, enable_subtitle: true, speech_rate: value.delivery ? 0 : value.mood === 'happy' ? 3 : value.mood === 'serious' ? -3 : 0 },
    additions: JSON.stringify({ context_texts: [value.delivery || styles[value.mood]] }),
  } };
}
function ttsUpstreamError(status) {
  if (status === 401 || status === 403) return new TtsError(502, 'tts_provider_configuration_error', '火山语音的密钥或音色权限需要检查。');
  if (status === 402) return new TtsError(503, 'tts_provider_insufficient_balance', '火山语音额度不足，文字会继续显示。');
  if (status === 429) return new TtsError(429, 'tts_provider_rate_limited', 'Vivi 现在有点忙，文字会继续显示。', 5000);
  if (status === 400 || status === 422) return new TtsError(502, 'tts_provider_request_rejected', '当前语音配置暂不受支持。');
  return ttsUnavailable();
}
function ttsSentence(raw, limits) {
  if (!ttsObject(raw) || typeof raw.text !== 'string' || raw.text.length > 2000 || !Array.isArray(raw.words) || raw.words.length > TTS_MAX_WORDS) throw ttsInvalid();
  limits.words += raw.words.length;
  if (limits.words > TTS_MAX_WORDS) throw ttsInvalid();
  return { text: raw.text, words: raw.words.map(item => {
    if (!ttsObject(item) || typeof item.word !== 'string' || !item.word || item.word.length > 128 || !Number.isFinite(item.startTime) || !Number.isFinite(item.endTime) || item.startTime < 0 || item.endTime < item.startTime || item.endTime > 180) throw ttsInvalid();
    const word = { word: item.word, startTime: item.startTime, endTime: item.endTime };
    if (Number.isFinite(item.confidence) && item.confidence >= 0 && item.confidence <= 1) word.confidence = item.confidence;
    return word;
  }) };
}
function ttsAudioSize(data) {
  if (typeof data !== 'string' || !data || data.length % 4 || !/^[A-Za-z0-9+/]+={0,2}$/.test(data)) throw ttsInvalid();
  return data.length / 4 * 3 - (data.endsWith('==') ? 2 : data.endsWith('=') ? 1 : 0);
}

function ttsStream(request, value, userHash, context, release) {
  const abort = new AbortController();
  let sink, reader, timer, wakePull, finished = false;
  const limits = { wire: 0, audio: 0, words: 0, index: 0 };
  function emitNow(type, data = {}) { if (!finished) sink.enqueue(ttsEncoder.encode(JSON.stringify({ type, requestId: value.requestId, ...data }) + '\n')); }
  async function emit(type, data = {}) {
    // Keep at most one bounded provider event beyond the 64KiB response queue.
    // A slow reader holds its concurrency slot and is still subject to timeout.
    while (!finished && sink.desiredSize <= 0) await new Promise(resolve => { wakePull = resolve; });
    if (!finished) emitNow(type, data);
  }
  function finish(error, canceled = false) {
    if (finished) return;
    if (error && !canceled) emitNow('error', ttsRecord(error));
    finished = true; clearTimeout(timer); request.signal.removeEventListener('abort', onAbort);
    wakePull?.(); wakePull = null;
    abort.abort(); if (reader) void reader.cancel().catch(() => {}).finally(() => { try { reader.releaseLock(); } catch {} });
    release(); try { sink.close(); } catch {}
  }
  const onAbort = () => finish(null, true);
  async function frame(data) {
    let packet; try { packet = JSON.parse(data); } catch { throw ttsInvalid(); }
    if (!ttsObject(packet) || !Number.isInteger(packet.code)) throw ttsInvalid();
    if (packet.code === 20000000) {
      if (!limits.audio) throw ttsInvalid();
      const textWords = packet.usage?.text_words;
      await emit('done', Number.isSafeInteger(textWords) && textWords >= 0 && textWords <= 50000 ? { usage: { textWords } } : {});
      finish(); return;
    }
    if (packet.code !== 0) {
      ttsDiagnostic(context, 'provider_error_code', packet.code);
      if (packet.code === 45000000 || packet.code === 45000001 || packet.code === 40402003) throw new TtsError(502, 'tts_provider_request_rejected', '火山语音的音色授权、额度或请求配置需要检查。');
      throw ttsUnavailable();
    }
    if (packet.data !== null && packet.data !== undefined) {
      limits.audio += ttsAudioSize(packet.data);
      if (limits.audio > (context.format==='pcm'?4320000:TTS_MAX_AUDIO)) throw new TtsError(502, 'tts_audio_too_large', '这段语音过长，文字会继续显示。');
      await emit('audio', { index: limits.index++, data: packet.data });
    }
    if (packet.sentence !== undefined && packet.sentence !== null) await emit('sentence', { sentence: ttsSentence(packet.sentence, limits) });
  }
  async function run() {
    let response;
    try {
      response = await context.fetch(TTS_ENDPOINT, { method: 'POST', redirect: 'manual', signal: abort.signal,
        headers: { 'Content-Type': 'application/json', 'X-Api-Key': context.secret, 'X-Api-Resource-Id': TTS_RESOURCE, 'X-Api-Request-Id': value.requestId, 'X-Control-Require-Usage-Tokens-Return': '*' },
        body: JSON.stringify(ttsPayload(value, userHash, context.format)) });
    } catch (error) {
      if (finished) return;
      ttsDiagnostic(context, /illegal invocation|incompatible receiver/i.test(String(error?.message || '')) ? 'fetch_receiver_error' : 'transport_error');
      throw ttsUnavailable();
    }
    if (finished) { void response.body?.cancel().catch(() => {}); return; }
    if (!response.ok) { ttsDiagnostic(context, response.status >= 300 && response.status < 400 ? 'redirect_error' : 'http_error', response.status); void response.body?.cancel().catch(() => {}); throw ttsUpstreamError(response.status); }
    if (!response.body || !(response.headers.get('Content-Type') || '').toLowerCase().includes('text/event-stream')) throw ttsInvalid();
    reader = response.body.getReader();
    const decoder = new TextDecoder('utf-8', { fatal: true });
    let pending = '', eventData = [], eventBytes = 0;
    const line = async text => {
      if (text.endsWith('\r')) text = text.slice(0, -1);
      if (!text) {
        if (eventData.length) await frame(eventData.join('\n'));
        eventData = []; eventBytes = 0; return;
      }
      eventBytes += text.length;
      if (eventBytes > TTS_MAX_EVENT) throw ttsInvalid();
      if (text.startsWith('data:')) eventData.push(text.slice(5).replace(/^ /, ''));
    };
    for (;;) {
      const part = await reader.read(); if (finished) return;
      if (part.done) break;
      limits.wire += part.value.byteLength;
      if (limits.wire > TTS_MAX_WIRE) throw ttsInvalid();
      pending += decoder.decode(part.value, { stream: true });
      let end;
      while (!finished && (end = pending.indexOf('\n')) !== -1) { await line(pending.slice(0, end)); pending = pending.slice(end + 1); }
      if (finished) return;
      if (pending.length > TTS_MAX_EVENT) throw ttsInvalid();
    }
    pending += decoder.decode();
    if (pending) await line(pending); if (eventData.length) await line('');
    if (!finished) throw ttsInvalid(); // EOF is not the provider's success code.
  }
  return new ReadableStream({
    start(controller) {
      sink = controller;
      emitNow('start', { format: context.format, sampleRate: 24000, source: 'volcengine', speaker: TTS_SPEAKER });
      timer = setTimeout(() => { ttsDiagnostic(context, 'timeout'); finish(ttsTimeout()); }, context.timeoutMs);
      request.signal.addEventListener('abort', onAbort, { once: true });
      if (request.signal.aborted) { onAbort(); return; }
      void run().catch(error => { if (!finished) { if (!(error instanceof TtsError)) ttsDiagnostic(context, 'invalid_response'); finish(error instanceof TtsError ? error : ttsInvalid()); } });
    },
    pull() { wakePull?.(); wakePull = null; },
    cancel() { finish(null, true); },
  }, { highWaterMark: 64 * 1024, size: chunk => chunk.byteLength });
}

/** Web APIs only; fetch/crypto/time are injectable for offline regression tests. */
export async function handleTts(request, env = {}, options = {}) {
  const context = { format: options.audioFormat === 'pcm' ? 'pcm' : 'mp3', fetch: options.fetch || globalThis.fetch?.bind(globalThis), crypto: options.crypto || globalThis.crypto,
    now: options.now || Date.now, timeoutMs: Math.min(TTS_TIMEOUT, Math.max(1, Number(options.timeoutMs) || TTS_TIMEOUT)), diagnostics: options.diagnostics,
    secret: typeof env.VOLC_TTS_API_KEY === 'string' ? env.VOLC_TTS_API_KEY.trim() : '' };
  try {
    const url = new URL(request.url);
    if (url.pathname === '/api/tts/config' && request.method === 'GET') return ttsJson({ enabled: Boolean(context.secret) && !/[\r\n]/.test(context.secret), provider: 'Volcengine', voice: 'Vivi 2.0', speaker: TTS_SPEAKER, format: 'mp3', maxTextLength: TTS_MAX_TEXT });
    if (url.pathname !== '/api/tts') return ttsJson({ error: 'not_found', message: '接口不存在。' }, 404);
    if (request.method !== 'POST') return ttsJson({ error: 'method_not_allowed', message: '请求方式无效。' }, 405, { Allow: 'POST' });
    const user = ttsIdentity(request); const value = await ttsReadInput(request, url);
    if (!context.secret || /[\r\n]/.test(context.secret)) throw new TtsError(503, 'tts_not_configured', 'Vivi 语音尚未配置，文字会继续显示。');
    if (!context.fetch || !context.crypto?.subtle) throw new TtsError(503, 'tts_runtime_unavailable', '当前环境暂不支持语音，文字会继续显示。');
    const userHash = await ttsHash(user, context), now = context.now(); ttsPrune(now);
    const id = `${userHash}:${value.requestId}`;
    if (ttsAttempts.has(id)) throw new TtsError(409, 'tts_duplicate_request', '这条声音请求已经提交，请勿重复发送。');
    if (ttsAttempts.size >= TTS_MAX_ATTEMPTS) throw new TtsError(429, 'tts_service_busy', '声音服务现在有点忙，请稍后再试。', 5000);
    const release = ttsReserve(userHash, now);
    ttsAttempts.set(id, { expiresAt: now + TTS_ATTEMPT_TTL });
    return new Response(ttsStream(request, value, userHash, context, release), { headers: ttsHeaders({ 'Content-Type': 'application/x-ndjson; charset=utf-8', 'X-Accel-Buffering': 'no' }) });
  } catch (error) {
    const safe = error instanceof TtsError ? error : ttsUnavailable();
    return ttsJson(ttsRecord(safe), safe.status, safe.retryAfterMs ? { 'Retry-After': String(Math.ceil(safe.retryAfterMs / 1000)) } : {});
  }
}
