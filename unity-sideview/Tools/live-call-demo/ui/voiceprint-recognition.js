import {DoubaoRecognition} from './doubao-recognition.js';

export function enrollmentWav(frames) {
  const size = frames.reduce((n, frame) => n + frame.byteLength, 0);
  const bytes = new Uint8Array(44 + size), view = new DataView(bytes.buffer);
  const text = (at, value) => [...value].forEach((c, i) => { bytes[at + i] = c.charCodeAt(0); });
  text(0, 'RIFF'); view.setUint32(4, 36 + size, true); text(8, 'WAVE'); text(12, 'fmt ');
  view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
  view.setUint32(24, 16000, true); view.setUint32(28, 32000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
  text(36, 'data'); view.setUint32(40, size, true);
  let offset = 44; for (const frame of frames) { bytes.set(new Uint8Array(frame), offset); offset += frame.byteLength; }
  return bytes;
}

export class VoiceprintRecognition {
  constructor({ fetchImpl = globalThis.fetch?.bind(globalThis), WebSocketClass = globalThis.WebSocket,
    loadSdk = () => import('/api/voiceprint/rtc.js'), mediaDevices = globalThis.navigator?.mediaDevices,
    location = globalThis.location, Context = globalThis.AudioContext || globalThis.webkitAudioContext, Worklet = globalThis.AudioWorkletNode } = {}) {
    Object.assign(this, { fetch: fetchImpl, WebSocketClass, loadSdk, mediaDevices, location, Context, Worklet });
    this.persistent = true; this.closed = false; this.controller = new AbortController();
  }
  start(config) { void this.open(config).catch(error => { if (!this.closed) this.fail(error.name === 'NotAllowedError' ? 'not-allowed' : /^voiceprint_/.test(error.message) ? error.message : 'voiceprint_connection_failed'); }); }
  async open(config) {
    this.onsetup?.({ phase: 'connecting', text: '正在连接…' });
    if (!config.credentialsConfigured) throw Error('voiceprint_not_configured');
    // Enrollment is an explicit Settings action, never part of joining a call.
    if (config.needsEnrollment) throw Error('voiceprint_setup_required');
    if (this.closed) return;
    this.onsetup?.({ phase: 'connecting', text: '正在连接…' });
    this.connectTimer = setTimeout(() => this.fail('voiceprint_connect_timeout'), 22000);
    const sdk = await this.loadSdk(); if (this.closed) return;
    this.sdk = sdk.default; await this.connect();
  }
  async enroll() {
    const stream = await this.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: false, autoGainControl: false }, video: false });
    if (this.closed) { stream.getTracks().forEach(t => t.stop()); return; }
    this.enrollStream = stream; this.deviceId = stream.getAudioTracks()[0]?.getSettings?.().deviceId;
    const context = this.context = new this.Context(); await context.resume();
    await context.audioWorklet.addModule('/api/asr/worklet.js'); if (this.closed) return;
    const source = this.source = context.createMediaStreamSource(stream), worklet = this.worklet = new this.Worklet(context, 'z-asr-capture'), sink = this.sink = context.createGain();
    sink.gain.value = 0; const frames = []; let duration = 0, voiced = 0, lastSecond = -1;
    this.onsetup?.({ phase: 'enrolling', remaining: 25 });
    await new Promise((resolve, reject) => {
      this.enrollReject = reject;
      this.enrollTimeout = setTimeout(() => reject(Error('voiceprint_enrollment_timeout')), 35000);
      worklet.port.onmessage = ({ data }) => {
        if (this.closed || !(data?.pcm instanceof ArrayBuffer)) return;
        frames.push(data.pcm); duration += data.durationMs; if (data.rms > .008) voiced += data.durationMs;
        const second = Math.max(0, Math.ceil((25000 - duration) / 1000));
        if (second !== lastSecond) { lastSecond = second; this.onsetup?.({ phase: 'enrolling', remaining: second }); }
        if (duration >= 25000) { worklet.port.onmessage = null; resolve(); }
      };
      source.connect(worklet); worklet.connect(sink); sink.connect(context.destination);
    });
    this.releaseEnrollment(); if (this.closed) return;
    // This energy check detects obvious silence only; provider validates actual voice quality.
    if (voiced < 12000) throw Error('voiceprint_enrollment_too_quiet');
    this.onsetup?.({ phase: 'registering' });
    const response = await this.fetch('/api/voiceprint/enroll', { method: 'POST', headers: { 'Content-Type': 'audio/wav' }, body: enrollmentWav(frames), signal: this.controller.signal });
    frames.length = 0;
    const result = await response.json(); if (!response.ok || !result.enrolled) throw Error('voiceprint_registration_failed');
  }
  async connect() {
    const url = new URL('/api/voiceprint/stream', this.location.href); url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = this.socket = new this.WebSocketClass(url.href);
    socket.onerror = () => this.fail('voiceprint_connection_failed');
    socket.onclose = () => { if (!this.closed) this.fail('voiceprint_disconnected'); };
    socket.onmessage = ({ data }) => { if (this.closed) return; try { void this.receive(JSON.parse(data)).catch(() => this.fail('voiceprint_invalid_response')); } catch { this.fail('voiceprint_invalid_response'); } };
  }
  async receive(event) {
    if (event.type === 'error') return this.fail(event.code);
    if (event.type === 'rtc') {
      if (this.engine) throw Error('duplicate_rtc');
      const engine = this.engine = this.sdk.createEngine(event.appId);
      engine.on('onError', () => { if (!this.closed) this.fail('voiceprint_rtc_error'); });
      await engine.setAudioCaptureConfig({ channelCount: 1, echoCancellation: true, noiseSuppression: false, autoGainControl: false });
      if (this.closed) return;
      await engine.joinRoom(event.token, event.roomId, { userId: event.userId }, { isAutoPublish: true, isAutoSubscribeAudio: false, isAutoSubscribeVideo: false });
      if (this.closed) { await engine.leaveRoom().catch(() => {}); return; }
      this.socket.send(JSON.stringify({ type: 'joined' })); return;
    }
    if (event.type === 'ready') {
      if (!this.engine || this.ready) throw Error('invalid_ready');
      const engine = this.engine;
      try { await engine.startAudioCapture(this.deviceId); }
      catch (error) { if (this.closed) return; throw error; }
      if (this.closed) { await engine.stopAudioCapture().catch(() => {}); return; }
      this.ready = true; clearTimeout(this.connectTimer); this.onstart?.(); return;
    }
    if (!this.ready) return;
    if (event.type === 'transcript') {
      if (event.speakerVerified !== true || typeof event.text !== 'string' || typeof event.final !== 'boolean' || typeof event.conversationId !== 'string') throw Error('invalid_transcript');
      const row = [{ transcript: event.text, confidence: 1 }]; row.isFinal = event.final;
      this.onresult?.({ results: [row], resultIndex: 0, provider: 'volc-mmpg', speakerVerified: true, conversationId: event.conversationId, turnFinal: event.final });
    } else if (event.type === 'discard') this.ondiscard?.(event);
  }
  releaseEnrollment() {
    clearTimeout(this.enrollTimeout); this.enrollReject = null;
    if (this.worklet) this.worklet.port.onmessage = null;
    this.worklet?.disconnect(); this.source?.disconnect(); this.sink?.disconnect();
    this.enrollStream?.getTracks().forEach(track => track.stop()); this.enrollStream = null;
    void this.context?.close().catch(() => {}); this.context = null;
  }
  stop() { this.abort(); this.onend?.(); }
  abort() {
    if (this.closed) return; this.closed = true; this.controller.abort(); clearTimeout(this.connectTimer);
    this.enrollReject?.(Error('voiceprint_canceled')); this.releaseEnrollment();
    if (this.socket) { this.socket.onmessage = null; this.socket.onclose = null; this.socket.onerror = null; this.socket.close(); }
    const engine = this.engine; this.engine = null;
    if (engine) void (async () => { try { await engine.stopAudioCapture(); } catch {} try { await engine.leaveRoom(); } catch {} try { this.sdk.destroyEngine(engine); } catch {} })();
  }
  fail(error) { if (this.closed) return; this.abort(); this.onerror?.({ error }); }
}

// Preserve the existing recognition interface and explicit legacy configuration.
export class CallRecognition {
  static isSupported() { return DoubaoRecognition.isSupported(); }
  constructor({ fetchImpl = globalThis.fetch?.bind(globalThis), Legacy = DoubaoRecognition, Protected = VoiceprintRecognition } = {}) {
    Object.assign(this, { fetch: fetchImpl, Legacy, Protected }); this.closed = false; this.persistent = false; this.controller = new AbortController();
  }
  start() {
    void (async () => {
      try {
        const response = await this.fetch('/api/voiceprint/config', { cache: 'no-store', signal: this.controller.signal });
        if (!response.ok) throw Error('voiceprint_config_failed');
        const config = await response.json(); if (this.closed) return;
        if(config.invalidMode)throw Error('voiceprint_config_failed');
        this.persistent = config.requested;
        this.delegate = config.requested ? new this.Protected() : new this.Legacy();
        for (const name of ['onstart','onend','onerror','onresult','onacoustic','onsetup','ondiscard']) this.delegate[name] = value => { if (!this.closed) this[name]?.(value); };
        this.delegate.start(config);
      } catch (error) { if (!this.closed) this.onerror?.({ error: error.message === 'voiceprint_config_failed' ? error.message : 'voiceprint_connection_failed' }); }
    })();
  }
  stop() { this.delegate?.stop(); }
  abort() { if (this.closed) return; this.closed = true; this.controller.abort(); this.delegate?.abort(); }
}
