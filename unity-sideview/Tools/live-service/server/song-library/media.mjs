import fs from 'node:fs';
import path from 'node:path';
import {createHash, randomUUID} from 'node:crypto';

export const hash = bytes => createHash('sha256').update(bytes).digest('hex');
export const canonical = value => JSON.stringify(value, Object.keys(value).sort());
export function fail(code) { throw new Error(code); }
export function text(value, max = 240) {
  if (typeof value !== 'string' || !value.trim() || value.length > max || /[\u0000-\u001f]/.test(value)) fail('invalid_text');
  return value.trim();
}
export const normalize = value => String(value || '').normalize('NFKC').replace(/[\s《》]/g, '').toLowerCase();

// The canonical stored audio is PCM16 WAV. Source adapters decode compressed
// originals before this integrity check; the original is retained separately.
export function inspectWav(bytes) {
  if (bytes.length < 44 || bytes.toString('ascii', 0, 4) !== 'RIFF' || bytes.toString('ascii', 8, 12) !== 'WAVE') fail('audio_format_unsupported');
  if (bytes.readUInt32LE(4) + 8 !== bytes.length) fail('audio_truncated');
  let fmt, data;
  for (let pos = 12; pos + 8 <= bytes.length;) {
    const size = bytes.readUInt32LE(pos + 4), end = pos + 8 + size;
    if (end > bytes.length) fail('audio_truncated');
    const kind = bytes.toString('ascii', pos, pos + 4);
    if (kind === 'fmt ') fmt = bytes.subarray(pos + 8, end);
    if (kind === 'data') data = bytes.subarray(pos + 8, end);
    pos = end + (size % 2);
  }
  if (!fmt || fmt.length < 16 || !data?.length || fmt.readUInt16LE(0) !== 1 || fmt.readUInt16LE(14) !== 16) fail('audio_format_unsupported');
  const channels = fmt.readUInt16LE(2), sampleRate = fmt.readUInt32LE(4);
  if (![1, 2].includes(channels) || sampleRate < 8000 || sampleRate > 96000 || data.length % (channels * 2)) fail('audio_format_unsupported');
  if (fmt.readUInt16LE(12) !== channels * 2 || fmt.readUInt32LE(8) !== sampleRate * channels * 2) fail('audio_format_unsupported');
  let energy = 0;
  for (let p = 0; p < data.length; p += 2) energy += (data.readInt16LE(p) / 32768) ** 2;
  if (Math.sqrt(energy / (data.length / 2)) < 0.0001) fail('audio_silent');
  return {format: 'wav-pcm16', durationMs: Math.round(data.length / (channels * sampleRate * 2) * 1000), channels, sampleRate};
}

export class BlobStore {
  constructor(directory) { this.directory = path.resolve(directory); fs.mkdirSync(this.directory, {recursive:true}); }
  put(bytes) {
    if (!Buffer.isBuffer(bytes) || !bytes.length || bytes.length > 100 * 1024 * 1024) fail('file_size_invalid');
    const sha256 = hash(bytes), target = path.join(this.directory, sha256);
    if (!fs.existsSync(target) || hash(fs.readFileSync(target)) !== sha256) {
      const temporary = target + '.' + randomUUID() + '.tmp';
      fs.writeFileSync(temporary, bytes, {mode:0o600}); fs.renameSync(temporary, target);
    }
    return {sha256, size:bytes.length};
  }
  read(ref) {
    if (!ref || !/^[a-f0-9]{64}$/.test(ref.sha256 || '')) fail('invalid_blob_ref');
    const file = path.join(this.directory, ref.sha256);
    if (!fs.existsSync(file)) fail('blob_missing');
    const bytes = fs.readFileSync(file);
    if (bytes.length !== ref.size || hash(bytes) !== ref.sha256) fail('blob_corrupt');
    return bytes;
  }
  available(ref) { try { this.read(ref); return true; } catch { return false; } }
}

export function parseLyrics(bytes) {
  if (bytes.length > 1024 * 1024) fail('lyrics_too_large');
  const raw = new TextDecoder('utf-8', {fatal:true}).decode(bytes).replace(/^\uFEFF/, '');
  const rows = raw.split(/\r?\n/).filter(line => line.trim());
  const lines = rows.filter(line => !/^\[(ar|ti|al|by|offset):/i.test(line)).map((line, i) => ({
    id:`line_${i + 1}`, text:line.replace(/\[\d+:\d+(?:\.\d+)?\]/g, '').trim()
  })).filter(line => line.text);
  if (!lines.length || lines.some(line => line.text.length > 1000)) fail('invalid_lyrics');
  // LRC timestamps are preserved in the raw file, never silently promoted to
  // validated musical phrase boundaries.
  return {encoding:'utf-8', lines, timing:'unverified'};
}
