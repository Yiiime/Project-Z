import { readFileSync, existsSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { fileURLToPath } from "node:url";
import { speechDelivery } from "./speech-delivery.mjs";

const ENDPOINT = "https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse";
const DEFAULT_VOICES = JSON.parse(readFileSync(new URL("../tts-voices.json", import.meta.url), "utf8"));
const CONFIG_PATH = fileURLToPath(new URL("../.local/tts.json", import.meta.url));
const MAX_AUDIO = 4 * 1024 * 1024;
const MAX_WIRE = 8 * 1024 * 1024;
const MAX_EVENT = 1024 * 1024;
const messages = {
  tts_unconfigured: "语音尚未配置，文字对话可继续。",
  tts_voice_unconfigured: "这位角色的音色尚未配置。",
  tts_configuration_error: "语音配置无效，请检查本机 TTS 设置。",
  tts_permission_denied: "语音鉴权或音色权限不足，请检查火山语音配置。",
  tts_rate_limited: "语音服务繁忙，文字对话可继续。",
  tts_failed: "这句语音生成失败，文字对话可继续。",
  tts_cancelled: "本句语音已取消。",
  tts_timeout: "语音等待超时，文字对话可继续。",
  tts_invalid_audio: "语音返回不完整，文字对话可继续。",
};
export class SpeechError extends Error {
  constructor(code) { super(messages[code] || messages.tts_failed); this.code = code; }
}
export function loadSpeechConfig(path = process.env.Z_TTS_CONFIG || CONFIG_PATH) {
  let local = {};
  try { if (existsSync(path)) local = JSON.parse(readFileSync(path, "utf8")); }
  catch { throw new SpeechError("tts_configuration_error"); }
  return { enabled: local.enabled !== false, apiKey: process.env.VOLC_TTS_API_KEY || local.apiKey || "",
    voices: { ...DEFAULT_VOICES, ...local.voices } };
}
export function speechPayload(voice, text, delivery) {
  if (typeof text !== "string" || !text.trim() || text.length > 2000)
    throw new SpeechError("tts_failed");
  if (!voice?.speaker) throw new SpeechError("tts_voice_unconfigured");
  if (!/^[A-Za-z0-9_-]{1,160}$/.test(voice.speaker) || !["seed-tts-2.0", "seed-icl-2.0"].includes(voice.resourceId) ||
      (voice.model && !["seed-tts-2.0-standard", "seed-tts-2.0-expressive"].includes(voice.model)))
    throw new SpeechError("tts_configuration_error");
  const metadata = speechDelivery(delivery);
  if (metadata.delivery && voice.resourceId === "seed-icl-2.0" && voice.model !== "seed-tts-2.0-expressive")
    throw new SpeechError("tts_configuration_error");
  const req_params = { text, speaker: voice.speaker, audio_params: { format: "pcm", sample_rate: 24000 } };
  if (voice.resourceId === "seed-icl-2.0" && voice.model) req_params.model = voice.model;
  if (metadata.delivery) req_params.additions = JSON.stringify({ context_texts: [metadata.delivery] });
  return { req_params };
}

// SSE may split UTF-8, event separators and base64 across arbitrary network chunks.
export async function readSpeechAudio(response, signal) {
  if (!response.ok) {
    throw new SpeechError([401,403].includes(response.status) ? "tts_permission_denied" : response.status === 429 ? "tts_rate_limited" : "tts_failed");
  }
  if (!response.body || !response.headers.get("content-type")?.includes("text/event-stream"))
    throw new SpeechError("tts_invalid_audio");
  const reader = response.body.getReader(), decoder = new TextDecoder();
  let pending = "", data = [], bytes = 0, wire = 0, complete = false;
  const chunks = [];
  const abort = () => { void reader.cancel().catch(() => {}); };
  signal?.addEventListener("abort", abort, { once: true });
  function event() {
    if (!data.length) return;
    let packet;
    try { packet = JSON.parse(data.join("\n")); } catch { throw new SpeechError("tts_invalid_audio"); }
    data = [];
    if (complete) throw new SpeechError("tts_invalid_audio");
    if (packet.code !== 0 && packet.code !== 20000000)
      throw new SpeechError([45000010,45000030].includes(packet.code) ? "tts_permission_denied" : "tts_failed");
    if (packet.data) {
      if (typeof packet.data !== "string" || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(packet.data))
        throw new SpeechError("tts_invalid_audio");
      const chunk = Buffer.from(packet.data, "base64"); bytes += chunk.length;
      if (bytes > MAX_AUDIO) throw new SpeechError("tts_invalid_audio");
      chunks.push(chunk);
    }
    if (packet.code === 20000000) complete = true;
  }
  try {
    while (true) {
      if (signal?.aborted) throw new SpeechError("tts_cancelled");
      const part = await reader.read();
      if (signal?.aborted) throw new SpeechError("tts_cancelled");
      if (part.done) break;
      wire += part.value.length;
      if (wire > MAX_WIRE) throw new SpeechError("tts_invalid_audio");
      pending += decoder.decode(part.value, { stream: true });
      let end;
      while ((end = pending.indexOf("\n")) >= 0) {
        const line = pending.slice(0,end).replace(/\r$/, ""); pending = pending.slice(end+1);
        if (!line) event();
        else if (line.startsWith("data:")) data.push(line.slice(5).replace(/^ /,""));
        if (data.reduce((n,s)=>n+s.length,0) > MAX_EVENT) throw new SpeechError("tts_invalid_audio");
      }
      if (pending.length > MAX_EVENT) throw new SpeechError("tts_invalid_audio");
    }
    pending += decoder.decode();
    if (pending.trim()) {
      if (!pending.startsWith("data:")) throw new SpeechError("tts_invalid_audio");
      data.push(pending.slice(5).trimStart());
    }
    event();
    if (!complete || !bytes || bytes % 2) throw new SpeechError("tts_invalid_audio");
    return { format: "pcm_s16le", sampleRate: 24000, channels: 1, base64: Buffer.concat(chunks).toString("base64") };
  } finally {
    signal?.removeEventListener("abort", abort);
    await reader.cancel().catch(()=>{}); reader.releaseLock();
  }
}

export class SpeechService {
  constructor({ config = loadSpeechConfig, fetch: request = globalThis.fetch, timeoutMs = 35000 } = {}) {
    this.config = config; this.fetch = request; this.timeoutMs = timeoutMs;
    this.attempts = new Map(); this.active = new Map();
  }
  status() {
    try { const c = this.config(); return { configured: !!(c.enabled && c.apiKey), enabled: c.enabled }; }
    catch { return { configured: false, enabled: false }; }
  }
  cancel(world, conversation, proposal) {
    for (const attempt of this.attempts.values())
      if ((!world || attempt.world === world) && (!conversation || attempt.conversation === conversation) && (!proposal || attempt.proposal === proposal)) {
        attempt.cancelled = true; attempt.controller.abort(); attempt.audio = null;
      }
  }
  async synthesize(input, signal) {
    const key = [input.worldId,input.runId,input.proposalId].join(":");
    let attempt = this.attempts.get(key);
    if (attempt) {
      if (attempt.cancelled) throw new SpeechError("tts_cancelled");
      if (attempt.promise) return attempt.promise;
      if (attempt.audio) return attempt.audio;
      throw new SpeechError(attempt.error || "tts_failed");
    }
    const config = this.config();
    if (!config.enabled || !config.apiKey) throw new SpeechError("tts_unconfigured");
    if (typeof config.apiKey !== "string" || /[\r\n]/.test(config.apiKey)) throw new SpeechError("tts_configuration_error");
    const voice = config.voices[input.actorId], payload = speechPayload(voice,input.text,input.delivery);
    // One current voice per world. A new single-chat line supersedes old audio.
    this.cancel(input.worldId);
    if (this.active.size >= 4) throw new SpeechError("tts_rate_limited");
    for (const [id,a] of this.attempts) if (!a.promise && Date.now()-a.at > 120000) this.attempts.delete(id);
    if (this.attempts.size >= 32) {
      const oldest = [...this.attempts].find(([,a])=>!a.promise);
      if (oldest) this.attempts.delete(oldest[0]);
      else throw new SpeechError("tts_rate_limited");
    }
    const controller = new AbortController();
    attempt = { proposal: input.proposalId, world: input.worldId, conversation: input.conversationId, controller, at: Date.now(), cancelled: false };
    this.attempts.set(key,attempt); this.active.set(key,attempt);
    const disconnect = () => { attempt.cancelled = true; controller.abort(); };
    signal?.addEventListener("abort",disconnect,{once:true});
    if (signal?.aborted) disconnect();
    let timedOut = false;
    const timer = setTimeout(()=>{timedOut=true;controller.abort();},this.timeoutMs);
    attempt.promise = (async()=>{
      try {
        if (controller.signal.aborted) throw new SpeechError("tts_cancelled");
        const response = await this.fetch(ENDPOINT,{method:"POST",redirect:"error",signal:controller.signal,
          headers:{"Content-Type":"application/json","Accept":"text/event-stream","X-Api-Key":config.apiKey,
            "X-Api-Resource-Id":voice.resourceId,"X-Api-Request-Id":randomUUID()},body:JSON.stringify(payload)});
        const audio = await readSpeechAudio(response,controller.signal);
        if (controller.signal.aborted) throw new SpeechError("tts_cancelled");
        attempt.audio = audio; return audio;
      } catch (e) {
        attempt.error = timedOut ? "tts_timeout" : controller.signal.aborted ? "tts_cancelled" : e instanceof SpeechError ? e.code : "tts_failed";
        throw new SpeechError(attempt.error);
      } finally {
        clearTimeout(timer);signal?.removeEventListener("abort",disconnect);this.active.delete(key);
      }
    })();
    try { return await attempt.promise; } finally { attempt.promise = null; }
  }
}
