import fs from 'node:fs/promises';
import path from 'node:path';
import {fail, normalize, text} from './media.mjs';

async function download(url, limit, signal) {
  const u = new URL(url);
  if (u.protocol !== 'https:' || u.username || u.password) fail('https_source_required');
  const response = await fetch(u, {redirect:'error', signal:signal || AbortSignal.timeout(45000)});
  if (!response.ok) fail('source_http_error');
  if (Number(response.headers.get('content-length')) > limit) { await response.body?.cancel(); fail('source_too_large'); }
  let size = 0; const parts = [];
  for await (const part of response.body) {
    size += part.length; if (size > limit) fail('source_too_large'); parts.push(part);
  }
  return Buffer.concat(parts);
}

// Catalog schema is our own adapter contract, not a claimed vendor API.
// Operators configure sources; song requests cannot supply arbitrary URLs/paths.
export class CatalogSource {
  constructor({id, file, url}) {
    this.id = text(id, 80); this.file = file ? path.resolve(file) : null; this.url = url || null;
    if (!!this.file === !!this.url) fail('invalid_source_config');
  }
  async search(query) {
    const raw = this.file ? await fs.readFile(this.file) : await download(this.url, 2 * 1024 * 1024);
    if (raw.length > 2 * 1024 * 1024) fail('catalog_too_large');
    const catalog = JSON.parse(raw.toString('utf8'));
    if (!Array.isArray(catalog.recordings) || catalog.recordings.length > 10000) fail('invalid_catalog');
    return catalog.recordings.filter(r => normalize(r.title) === normalize(query.title)
      && (!query.artist || normalize(r.artist) === normalize(query.artist))
      && (!query.version || normalize(r.version) === normalize(query.version))).map(r => {
      const item = {sourceId:this.id, externalId:text(r.id), title:text(r.title), artist:text(r.artist), version:text(r.version), evidence:text(r.evidence, 2000)};
      for (const kind of ['audio', 'lyrics']) if (r[kind]) {
        const ref = r[kind];
        if (!ref || typeof ref !== 'object') fail('invalid_source_ref');
        const location = this.file ? path.resolve(path.dirname(this.file), text(ref.path, 2000)) : new URL(text(ref.url, 2000), this.url).href;
        item[kind] = {...ref, location, transport:this.file ? 'file' : 'https'};
      }
      if (item.audio && (!Number.isFinite(item.audio.expectedDurationMs) || item.audio.expectedDurationMs <= 0)) fail('expected_duration_required');
      return item;
    });
  }
  async fetch(ref, kind) {
    const limit = kind === 'audio' ? 100 * 1024 * 1024 : 1024 * 1024;
    if (!ref) fail('source_resource_missing');
    if (this.file && ref.transport === 'file') {
      const stat = await fs.stat(ref.location); if (!stat.isFile() || stat.size > limit) fail('source_too_large');
      return fs.readFile(ref.location);
    }
    if (this.url && ref.transport === 'https') return download(ref.location, limit);
    fail('source_transport_mismatch');
  }
}

export const capabilities = Object.freeze({
  ownCatalogSearch:true, localFileImport:true, configuredHttpsCatalog:true,
  generalInternetMusicSearch:false, musicSourceAdapters:['netease-anonymous'],
  automaticAlignment:true, alignmentOutput:'candidate_requires_review', automaticSectionDetection:false,
  manualVersionedIndex:true, fullSongRetention:true, supportedAudio:['wav-pcm16'],
  sourceAudioDecoding:'local_ffmpeg', viviGeneration:false, viviReferenceInput:'unverified', automaticListeningReview:false
});
