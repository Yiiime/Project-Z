import {gzipSync, gunzipSync} from 'node:zlib';
import {randomUUID} from 'node:crypto';
import {check} from './protocol.mjs';

// 自行实现的豆包 ASR v3 二进制帧；不复制供应商 SDK。仅16kHz/单声道/PCM16LE。
export function encodeAsrPacket(payload, {audio = false, last = false} = {}) {
  const body = gzipSync(audio ? Buffer.from(payload) : Buffer.from(JSON.stringify(payload)));
  const head = Buffer.from([0x11, (audio ? 0x20 : 0x10) | (last ? 2 : 0), audio ? 0x01 : 0x11, 0]);
  const size = Buffer.alloc(4); size.writeUInt32BE(body.length); return Buffer.concat([head, size, body]);
}
export function decodeAsrPacket(input) {
  const b = Buffer.from(input); check(b.length >= 8 && b.length <= 2 * 1024 * 1024, 'invalid_asr_frame');
  const version = b[0] >> 4, headerSize = (b[0] & 15) * 4, type = b[1] >> 4, flags = b[1] & 15;
  const serial = b[2] >> 4, compression = b[2] & 15;
  check(version === 1 && headerSize >= 4 && headerSize <= b.length - 4 && [9, 15].includes(type)
    && [0, 1].includes(compression) && [0, 1].includes(serial), 'unsupported_asr_frame');
  let offset = headerSize, sequence = null, errorCode = null;
  if (type === 15 || (flags & 1)) {
    check(b.length >= offset + 8, 'truncated_asr_frame');
    if (type === 15) errorCode = b.readUInt32BE(offset); else sequence = b.readInt32BE(offset);
    offset += 4;
  }
  const size = b.readUInt32BE(offset); offset += 4;
  check(offset + size === b.length, 'invalid_asr_payload_size');
  const bytes = compression ? gunzipSync(b.subarray(offset), {maxOutputLength: 2 * 1024 * 1024}) : b.subarray(offset);
  const body = serial === 1 ? JSON.parse(bytes.toString('utf8')) : bytes.toString('utf8');
  if (errorCode !== null) throw new Error('asr_provider_error:' + errorCode); // 不把供应商原始内容或密钥写入日志。
  return {body, sequence, last: Boolean(flags & 2)};
}

// ws npm 包的可选桥接；由宿主传 WebSocket 构造器，本包核心无需安装第三方依赖。
export function createWsConnector(WebSocketClass) {
  return ({url, headers, signal, onMessage, onClose, onError}) => new Promise((resolve, reject) => {
    const socket = new WebSocketClass(url, {headers, maxPayload: 2 * 1024 * 1024, handshakeTimeout: 8000});
    let opened = false;
    const abort = () => {socket.terminate(); if (!opened) reject(new Error('asr_aborted'));};
    if (signal.aborted) {abort(); return;}
    signal.addEventListener('abort', abort, {once: true});
    socket.once('open', () => {
      opened = true;
      resolve({send: buffer => {
        check(socket.readyState === 1 && socket.bufferedAmount < 128000, 'asr_backpressure'); socket.send(buffer);
      }, close: () => socket.close()});
    });
    socket.on('message', (data, binary) => { if (binary) onMessage(data); else onError(new Error('unexpected_asr_text_frame')); });
    socket.on('error', () => {const error = new Error('asr_socket_error'); if (!opened) reject(error); else onError(error);});
    socket.on('close', () => {
      signal.removeEventListener('abort', abort);
      if (!opened) reject(new Error('asr_closed_before_open')); else onClose();
    });
  });
}

// 一次实例处理一轮语音。finish 由按键释放或宿主VAD/语义判停触发；不把中间分句自动当整轮结束。
export class DoubaoAsrTurn {
  constructor({apiKey, connect, uid, onPartial, onFinal, onError, maxDurationMs = 60000}) {
    check(apiKey && typeof connect === 'function' && uid && typeof onPartial === 'function'
      && typeof onFinal === 'function' && typeof onError === 'function', 'missing_asr_dependencies');
    check(Number.isFinite(maxDurationMs) && maxDurationMs > 0 && maxDurationMs <= 120000, 'invalid_asr_duration');
    this.apiKey = apiKey; this.connect = connect; this.uid = uid; this.onPartial = onPartial;
    this.onFinal = onFinal; this.onError = onError; this.maxDurationMs = maxDurationMs;
    this.controller = new AbortController(); this.socket = null; this.pending = null; this.closed = false; this.finishing = false; this.bytes = 0;
  }
  async open() {
    check(!this.opening && !this.closed, 'asr_already_opened'); this.opening = true;
    this.timer = setTimeout(() => this.fail(new Error('asr_turn_timeout')), this.maxDurationMs + 10000);
    try {
      this.socket = await this.connect({url: 'wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async',
        headers: {'X-Api-Key': this.apiKey, 'X-Api-Resource-Id': 'volc.seedasr.sauc.duration',
          'X-Api-Request-Id': randomUUID(), 'X-Api-Connect-Id': randomUUID(), 'X-Api-Sequence': '-1'},
        signal: this.controller.signal, onMessage: bytes => this.receive(bytes),
        onClose: () => {if (!this.closed) this.fail(new Error('asr_incomplete'));}, onError: e => this.fail(e)});
      if (this.closed) {this.socket.close(); return;}
      this.socket.send(encodeAsrPacket({user: {uid: this.uid}, audio: {format: 'pcm', codec: 'raw', rate: 16000, bits: 16, channel: 1},
        request: {model_name: 'bigmodel', enable_itn: true, enable_punc: true, show_utterances: true}}));
    } catch (error) {this.fail(error); throw error;}
  }
  push(pcm16, sampleRate = 16000) {
    check(this.socket && !this.closed && !this.finishing, 'asr_not_writable');
    check(sampleRate === 16000, 'asr_requires_16000hz');
    const chunk = ArrayBuffer.isView(pcm16) ? Buffer.from(new Uint8Array(pcm16.buffer, pcm16.byteOffset, pcm16.byteLength)) : Buffer.from(pcm16);
    check(chunk.length > 0 && chunk.length % 2 === 0 && chunk.length <= 64000, 'invalid_pcm');
    this.bytes += chunk.length; check(this.bytes <= this.maxDurationMs * 32, 'asr_audio_too_long');
    // 保留最后一块，以真实音频负包结尾；不会发送伪造的静音来补结束事件。
    if (this.pending) this.socket.send(encodeAsrPacket(this.pending, {audio: true}));
    this.pending = chunk;
  }
  finish() {
    check(this.socket && this.pending && !this.closed && !this.finishing, 'asr_nothing_to_finish');
    this.finishing = true; this.socket.send(encodeAsrPacket(this.pending, {audio: true, last: true})); this.pending = null;
  }
  receive(bytes) {
    if (this.closed) return;
    try {
      const packet = decodeAsrPacket(bytes);
      if (packet.body.code !== undefined) check([0, 20000000].includes(packet.body.code), 'asr_business_error');
      const result = packet.body.result;
      const value = Array.isArray(result) ? result[0]?.text : result?.text;
      if (packet.last) {
        check(this.finishing, 'unexpected_asr_final');
        const finalText = typeof value === 'string' ? value.trim() : '';
        this.close(); this.onFinal(finalText);
      } else if (typeof value === 'string' && value.trim()) this.onPartial(value.trim());
    } catch (error) {this.fail(error);}
  }
  fail(error) { if (this.closed) return; this.close(); this.onError(error); }
  close() {
    if (this.closed) return; this.closed = true; clearTimeout(this.timer); this.pending = null;
    this.socket?.close(); this.controller.abort(); this.apiKey = null;
  }
}
