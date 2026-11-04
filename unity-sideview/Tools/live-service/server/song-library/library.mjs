import fs from 'node:fs';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import {BlobStore, inspectWav, parseLyrics, hash, canonical, fail, normalize, text} from './media.mjs';
import {capabilities} from './sources.mjs';

const now = () => new Date().toISOString();
const decode = value => value ? JSON.parse(value) : null;
const safeError = e => /^[a-z_]{2,80}$/.test(e?.message || '') ? e.message : 'source_unavailable';
export const Z_VOICE = Object.freeze({identity:'z-vivi', revision:1, speaker:'zh_female_vv_uranus_bigtts', singingModel:'1.2.6.1', mode:'single_voice_a_cappella'});
function voiceKey(v) {
  return canonical({identity:text(v.identity), revision:text(String(v.revision)), speaker:text(v.speaker), singingModel:text(v.singingModel), mode:text(v.mode)});
}
export function selector(input = {kind:'chorus', ordinal:1}) {
  const kind = input?.kind;
  if (!['first_line','verse','chorus','bridge','full','time_range','lyric_start'].includes(kind)) fail('invalid_segment');
  if (kind === 'time_range') {
    if (![input.startMs,input.endMs].every(Number.isInteger) || input.startMs < 0 || input.endMs <= input.startMs) fail('invalid_range');
    return {kind, startMs:input.startMs, endMs:input.endMs};
  }
  if (kind === 'lyric_start') return {kind, text:text(input.text, 300), ...(input.occurrence !== undefined ? {occurrence:positiveInt(input.occurrence)} : {})};
  return {kind, ordinal:['first_line','full'].includes(kind) ? 1 : positiveInt(input.ordinal ?? 1)};
}
function positiveInt(n) { if (!Number.isInteger(n) || n < 1 || n > 10000) fail('invalid_ordinal'); return n; }

export class SongLibrary {
  constructor({directory, sources = []}) {
    this.directory = path.resolve(directory); fs.mkdirSync(this.directory, {recursive:true});
    this.blobs = new BlobStore(path.join(this.directory, 'blobs'));
    this.db = new DatabaseSync(path.join(this.directory, 'library.sqlite'));
    this.db.exec('PRAGMA busy_timeout=5000;');
    this.db.exec(fs.readFileSync(new URL('./schema.sql', import.meta.url), 'utf8'));
    this.sources = new Map(sources.map(s => [s.id,s]));
    this.capabilities = capabilities;
  }
  close() { this.db.close(); }
  tx(fn) { this.db.exec('BEGIN IMMEDIATE'); try { const r = fn(); this.db.exec('COMMIT'); return r; } catch(e) { this.db.exec('ROLLBACK'); throw e; } }
  event(recordingId, type, data = {}, jobId = null) {
    this.db.prepare('INSERT INTO events(recording_id,job_id,type,data,created_at) VALUES(?,?,?,?,?)').run(recordingId,jobId,type,JSON.stringify(data),now());
  }
  events(recordingId) { return this.db.prepare('SELECT * FROM events WHERE recording_id=? ORDER BY seq').all(recordingId).map(e => ({...e,data:decode(e.data)})); }
  recording(id) { const r = this.db.prepare('SELECT * FROM recordings WHERE id=?').get(id); if (!r) fail('recording_not_found'); return {...r, descriptor:decode(r.descriptor)}; }
  register({title,artist,version,descriptor = null}) {
    title = text(title); artist = text(artist); version = text(version);
    const identity = JSON.stringify([title,artist,version].map(normalize));
    return this.tx(() => {
      let row = this.db.prepare('SELECT id FROM recordings WHERE identity=?').get(identity);
      if (!row) {
        row = {id:randomUUID()};
        this.db.prepare('INSERT INTO recordings VALUES(?,?,?,?,?,?,?)').run(row.id,identity,title,artist,version,null,now());
        this.db.prepare('INSERT INTO materials(recording_id,updated_at) VALUES(?,?)').run(row.id,now());
        this.event(row.id,'recording_registered',{title,artist,version});
      }
      if (descriptor) this._attach(row.id, descriptor);
      return this.recording(row.id);
    });
  }
  _attach(id, d) {
    const r = this.recording(id);
    if (![this.sources.has(d.sourceId),normalize(d.title) === normalize(r.title),normalize(d.artist) === normalize(r.artist),normalize(d.version) === normalize(r.version)].every(Boolean)) fail('source_version_mismatch');
    text(d.externalId); text(d.evidence,2000);
    if (r.descriptor && (r.descriptor.sourceId !== d.sourceId || r.descriptor.externalId !== d.externalId)) fail('recording_source_conflict');
    // References remain stable until an explicit source rebind, with an event.
    if (r.descriptor && JSON.stringify(r.descriptor) === JSON.stringify(d)) return;
    this.db.prepare('UPDATE recordings SET descriptor=? WHERE id=?').run(JSON.stringify(d),id);
    this.event(id,'source_bound',{sourceId:d.sourceId,externalId:d.externalId,evidence:d.evidence});
  }
  async lookup(query, {refreshSources = false} = {}) {
    const q = {title:text(query.title), artist:query.artist ? text(query.artist) : '', version:query.version ? text(query.version) : ''};
    let matches = this.db.prepare('SELECT * FROM recordings').all().filter(r => normalize(r.title) === normalize(q.title) && (!q.artist || normalize(r.artist) === normalize(q.artist)) && (!q.version || normalize(r.version) === normalize(q.version)));
    const sourceErrors = []; let searched = 0;
    const previous = this.db.prepare('SELECT result FROM requests WHERE input=? ORDER BY created_at DESC,rowid DESC LIMIT 1').get(JSON.stringify(q));
    if (!matches.length || (this.sources.size && matches.every(r=>!r.descriptor)) || refreshSources || decode(previous?.result)?.status === 'lookup_incomplete') {
      for (const source of this.sources.values()) {
        searched++;
        try { for (const candidate of await source.search(q)) {
          const r = this.register({...candidate,descriptor:candidate});
          if (!matches.some(m => m.id === r.id)) matches.push(r);
        } } catch(e) { sourceErrors.push({sourceId:source.id, code:safeError(e)}); }
      }
    }
    // An incomplete search cannot establish uniqueness across configured sources.
    const status = sourceErrors.length ? 'lookup_incomplete' : matches.length > 1 ? 'ambiguous' : matches.length ? 'found' : !searched ? 'source_not_configured' : 'not_found_in_configured_sources';
    const result = {status, candidates:matches.map(({id,title,artist,version}) => ({id,title,artist,version})), sourceErrors, searchedSources:searched};
    this.db.prepare('INSERT INTO requests VALUES(?,?,?,?)').run(randomUUID(),JSON.stringify(q),JSON.stringify(result),now());
    return result;
  }
  material(id) {
    this.recording(id);
    const r = this.db.prepare('SELECT * FROM materials WHERE recording_id=?').get(id);
    const audio = decode(r.audio), lyrics = decode(r.lyrics), review = decode(r.review);
    const audioAvailable = this.blobs.available(audio), lyricsAvailable = this.blobs.available(lyrics);
    const verified = audioAvailable && lyricsAvailable && review?.approved === true && review?.audioSha === audio.sha256 && review?.lyricsSha === lyrics.sha256;
    const index = r.active_index ? this.index(r.active_index) : null;
    const validIndex = verified && index?.review?.approved && index.audio_sha === audio.sha256 && index.lyrics_sha === lyrics.sha256;
    return {recordingId:id, audio, lyrics, review, audioAvailable, lyricsAvailable,
      status:verified ? 'ready' : audioAvailable && lyricsAvailable ? review?.approved === false ? 'review_rejected' : 'awaiting_review' : audioAvailable || lyricsAvailable ? 'partial' : 'missing',
      fullSongRetained:Boolean(audioAvailable && review?.approved === true && review?.audioSha === audio.sha256),
      indexStatus:validIndex ? index.review.fullCoverage ? 'complete' : 'partial' : 'missing',
      activeIndexId:validIndex ? index.id : null};
  }
  job(id) { return this.db.prepare('SELECT * FROM jobs WHERE recording_id=?').get(id) || null; }
  invalidateResource(id, kind, reason) {
    if (!['audio','lyrics'].includes(kind)) fail('invalid_resource_kind');
    reason=text(reason,2000);
    return this.tx(() => {
      const m=this.material(id);
      if (this.job(id)?.status==='running') fail('job_running');
      this.db.prepare(`UPDATE materials SET ${kind}=NULL,review=NULL,active_index=NULL,updated_at=? WHERE recording_id=?`).run(now(),id);
      this.event(id,'resource_invalidated',{kind,reason,previous:m[kind]});
      return this.material(id);
    });
  }
  enqueue(id) {
    this.recording(id);
    if (this.material(id).status === 'ready') return {status:'already_retained', recordingId:id};
    return this.tx(() => {
      let j = this.job(id);
      if (j && ['queued','running'].includes(j.status)) return j;
      if (!j) {
        const jobId = randomUUID();
        this.db.prepare('INSERT INTO jobs(id,recording_id,status,stage,created_at,updated_at) VALUES(?,?,?,?,?,?)').run(jobId,id,'queued','pending',now(),now());
      } else this.db.prepare("UPDATE jobs SET status='queued',stage='pending',error=NULL,updated_at=? WHERE id=?").run(now(),j.id);
      j = this.job(id); this.event(id,'material_queued',{},j.id); return j;
    });
  }
  recover() {
    this.tx(() => {
      for (const j of this.db.prepare("SELECT * FROM jobs WHERE status='running' AND lease_until<?").all(Date.now())) {
        this.db.prepare("UPDATE jobs SET status='failed',error='interrupted',lease=NULL,updated_at=? WHERE id=?").run(now(),j.id);
        this.event(j.recording_id,'material_failed',{stage:j.stage,code:'interrupted'},j.id);
      }
    });
  }
  async runNext({recordingId} = {}) {
    this.recover();
    const job = this.tx(() => {
      const j = recordingId ? this.db.prepare("SELECT * FROM jobs WHERE status='queued' AND recording_id=?").get(recordingId)
        : this.db.prepare("SELECT * FROM jobs WHERE status='queued' ORDER BY created_at,id LIMIT 1").get();
      if (!j) return null;
      const lease = randomUUID();
      this.db.prepare("UPDATE jobs SET status='running',attempt=attempt+1,lease=?,lease_until=?,updated_at=? WHERE id=?").run(lease,Date.now()+300000,now(),j.id);
      this.event(j.recording_id,'material_started',{attempt:j.attempt+1},j.id); return {...j,lease};
    });
    if (!job) return null;
    const guarded = fn => this.tx(() => {
      const current = this.job(job.recording_id);
      if (current.status !== 'running' || current.lease !== job.lease || current.lease_until < Date.now()) fail('job_lease_lost');
      return fn();
    });
    let stage = 'source';
    try {
      const r = this.recording(job.recording_id), d = r.descriptor, source = d && this.sources.get(d.sourceId);
      for (const kind of ['audio','lyrics']) {
        stage = kind; const material = this.material(r.id);
        if (material[kind+'Available']) continue;
        if (!source) fail('source_not_configured');
        if (!d[kind]) fail(kind === 'audio' ? 'audio_source_missing' : 'lyrics_source_missing');
        guarded(() => this.db.prepare('UPDATE jobs SET stage=?,updated_at=? WHERE id=?').run(stage,now(),job.id));
        const fetched = await source.fetch(d[kind],kind);
        const bytes = Buffer.isBuffer(fetched) ? fetched : fetched.bytes;
        if (!Buffer.isBuffer(bytes)) fail('invalid_source_bytes');
        if (d[kind].sha256 && hash(bytes) !== d[kind].sha256) fail('source_checksum_mismatch');
        let details;
        if (kind === 'audio') {
          details = inspectWav(bytes);
          if (Math.abs(details.durationMs-d.audio.expectedDurationMs) > 500) fail('recording_duration_mismatch');
        } else details = parseLyrics(bytes);
        const original = fetched.original ? this.blobs.put(fetched.original) : null;
        const blob = {...this.blobs.put(bytes),...details,source:{sourceId:d.sourceId,externalId:d.externalId,evidence:d.evidence},
          ...(original ? {original,acquisition:fetched.metadata||{}} : {}),acquiredAt:now()};
        guarded(() => {
          this.db.prepare(`UPDATE materials SET ${kind}=?,review=NULL,active_index=NULL,updated_at=? WHERE recording_id=?`).run(JSON.stringify(blob),now(),r.id);
          this.event(r.id,'resource_saved',{kind,sha256:blob.sha256,size:blob.size},job.id);
        });
      }
      guarded(() => {
        this.db.prepare("UPDATE jobs SET status='awaiting_review',stage='material_review',lease=NULL,updated_at=? WHERE id=?").run(now(),job.id);
        this.event(job.recording_id,'material_awaiting_review',{},job.id);
      });
    } catch(e) {
      if (e.message === 'job_lease_lost') return {status:'stale_result_discarded'};
      guarded(() => {
        this.db.prepare("UPDATE jobs SET status='failed',error=?,lease=NULL,updated_at=? WHERE id=?").run(safeError(e),now(),job.id);
        this.event(job.recording_id,'material_failed',{stage,code:safeError(e)},job.id);
      });
    }
    return this.job(job.recording_id);
  }
  reviewMaterial(id, {approved = true,reviewer,note,checks,audioSha,lyricsSha}) {
    reviewer = text(reviewer); note = text(note,2000);
    if (typeof approved !== 'boolean' || approved && !['recordingVersion','fullAudio','matchingLyrics'].every(k => checks?.[k] === true)) fail('material_review_incomplete');
    const m = this.material(id);
    if (!m.audioAvailable || !m.lyricsAvailable || audioSha !== m.audio.sha256 || lyricsSha !== m.lyrics.sha256) fail('stale_material_review');
    const review = {approved,reviewer,note,checks,audioSha,lyricsSha,at:now()};
    this.tx(() => {
      this.db.prepare('UPDATE materials SET review=?,updated_at=? WHERE recording_id=?').run(JSON.stringify(review),now(),id);
      this.db.prepare('UPDATE jobs SET status=?,stage=?,error=?,updated_at=? WHERE recording_id=? AND status IN (\'awaiting_review\',\'completed\',\'failed\')').run(approved?'completed':'failed',approved?'retained':'material_review',approved?null:'material_review_rejected',now(),id);
      this.event(id,'material_reviewed',review);
    });
    return this.material(id);
  }
  index(id) {
    const row = this.db.prepare('SELECT * FROM indexes WHERE id=?').get(id); if (!row) fail('index_not_found');
    return {...row,document:decode(row.document),review:decode(row.review)};
  }
  saveIndex(id, {audioSha,lyricsSha,phrases,sections,method,evidence}) {
    const m = this.material(id);
    if (!m.audioAvailable || !m.lyricsAvailable || audioSha !== m.audio.sha256 || lyricsSha !== m.lyrics.sha256) fail('stale_index_material');
    text(method); text(evidence,2000);
    if (!Array.isArray(phrases) || !phrases.length || phrases.length > 2000 || !Array.isArray(sections) || sections.length > 200) fail('invalid_index');
    const lineIds = new Set(m.lyrics.lines.map(l => l.id)), ids = new Set(), ordinals = new Set();
    let previousEnd = 0, previousOrdinal = 0;
    const bounds = p => {
      if (![p.startMs,p.endMs].every(Number.isInteger) || p.startMs < 0 || p.endMs <= p.startMs || p.endMs > m.audio.durationMs) fail('invalid_index_bounds');
    };
    const safePhrases = phrases.map(p => {
      text(p.id); bounds(p); const ordinal = positiveInt(p.ordinal);
      if (ids.has(p.id) || ordinals.has('phrase:'+ordinal) || ordinal <= previousOrdinal || p.startMs < previousEnd || !Array.isArray(p.lineIds) || !p.lineIds.length || new Set(p.lineIds).size !== p.lineIds.length || p.lineIds.some(l => !lineIds.has(l))) fail('invalid_phrase');
      previousEnd = p.endMs; previousOrdinal=ordinal; ids.add(p.id); ordinals.add('phrase:'+ordinal);
      return {id:p.id,ordinal,startMs:p.startMs,endMs:p.endMs,lineIds:p.lineIds};
    });
    const safeSections = sections.map(s => {
      text(s.id); bounds(s); const ordinal = positiveInt(s.ordinal);
      if (!['verse','chorus','bridge'].includes(s.kind) || ids.has(s.id) || ordinals.has(s.kind+':'+ordinal) || !Array.isArray(s.phraseIds) || !s.phraseIds.length || new Set(s.phraseIds).size !== s.phraseIds.length) fail('invalid_section');
      const matched = s.phraseIds.map(pid => safePhrases.find(p => p.id === pid));
      if (matched.some(p => !p || p.startMs < s.startMs || p.endMs > s.endMs) || matched.some((p,i) => i && p.startMs < matched[i-1].startMs)) fail('invalid_section_phrases');
      ids.add(s.id); ordinals.add(s.kind+':'+ordinal);
      return {id:s.id,kind:s.kind,ordinal,startMs:s.startMs,endMs:s.endMs,phraseIds:s.phraseIds};
    });
    return this.tx(() => {
      const revision = this.db.prepare('SELECT COALESCE(MAX(revision),0)+1 AS next FROM indexes WHERE recording_id=?').get(id).next;
      const indexId = randomUUID();
      this.db.prepare('INSERT INTO indexes VALUES(?,?,?,?,?,?,?,?)').run(indexId,id,revision,audioSha,lyricsSha,JSON.stringify({phrases:safePhrases,sections:safeSections,method,evidence}),null,now());
      this.event(id,'index_candidate_saved',{indexId,revision}); return this.index(indexId);
    });
  }
  reviewIndex(indexId, {approved = true,reviewer,note,checks,fullCoverage = false}) {
    reviewer = text(reviewer); note = text(note,2000);
    if (typeof approved !== 'boolean' || typeof fullCoverage !== 'boolean' || approved && !['recordingVersion','phraseBoundaries','sectionLabels','tailsPreserved'].every(k => checks?.[k] === true)) fail('index_review_incomplete');
    const index = this.index(indexId), m = this.material(index.recording_id);
    if (m.status !== 'ready' || index.audio_sha !== m.audio.sha256 || index.lyrics_sha !== m.lyrics.sha256) fail('stale_index_material');
    if (index.review) fail('index_already_reviewed');
    if (approved && fullCoverage) {
      const covered = new Set(index.document.phrases.flatMap(p => p.lineIds));
      if (m.lyrics.lines.some(l => !covered.has(l.id))) fail('index_coverage_incomplete');
    }
    const review = {approved,reviewer,note,checks,fullCoverage:approved && fullCoverage,at:now()};
    this.tx(() => {
      this.db.prepare('UPDATE indexes SET review=? WHERE id=?').run(JSON.stringify(review),indexId);
      if (approved) this.db.prepare('UPDATE materials SET active_index=?,updated_at=? WHERE recording_id=?').run(indexId,now(),index.recording_id);
      this.event(index.recording_id,'index_reviewed',{indexId,...review});
    });
    return this.index(indexId);
  }
  resolve(id, input) {
    const request = selector(input), m = this.material(id);
    if (!m.activeIndexId) return {status:'segment_unresolved',reason:m.status === 'ready' ? 'index_not_verified' : 'material_not_ready',request};
    const idx = this.index(m.activeIndexId), doc = idx.document;
    let range;
    if (request.kind === 'first_line') range = doc.phrases.find(p => p.ordinal === 1);
    else if (['verse','chorus','bridge'].includes(request.kind)) range = doc.sections.find(s => s.kind === request.kind && s.ordinal === request.ordinal);
    else if (request.kind === 'full') {
      if (!idx.review.fullCoverage) return {status:'segment_unresolved',reason:'full_index_not_verified',request};
      range = {startMs:0,endMs:m.audio.durationMs};
    } else if (request.kind === 'time_range') {
      if (request.endMs > m.audio.durationMs) fail('invalid_range');
      // Explicit user times are exact coordinates, not a musical-boundary claim.
      range = request;
    } else {
      const matches = doc.phrases.filter(p => normalize(p.lineIds.map(l => m.lyrics.lines.find(x => x.id === l).text).join('')).includes(normalize(request.text)));
      if (matches.length > 1 && !request.occurrence) return {status:'ambiguous',reason:'repeated_lyric',candidates:matches.map(p => p.id),request};
      const phrase = matches[(request.occurrence || 1)-1];
      const section = phrase && doc.sections.filter(s => s.phraseIds.includes(phrase.id));
      if (section?.length > 1) return {status:'ambiguous',reason:'overlapping_sections',request};
      if (phrase && section?.length === 1) range = {startMs:phrase.startMs,endMs:section[0].endMs};
    }
    if (!range) return {status:'segment_unresolved',reason:'requested_scope_not_indexed',request};
    return {status:'resolved',recordingId:id,indexId:idx.id,indexRevision:idx.revision,request,startMs:range.startMs,endMs:range.endMs,
      audioSha:m.audio.sha256,lyricsSha:m.lyrics.sha256,boundaryBasis:request.kind === 'time_range' ? 'explicit_user_time' : 'human_reviewed_index',
      generationSupported:false};
  }
  addAsset(id, {file, segment, voice, indexId = null, provenance}) {
    this.recording(id); const range = selector(segment); voiceKey(voice); text(provenance,2000);
    if (indexId) {
      const resolved = this.resolve(id,range);
      if (resolved.status !== 'resolved' || resolved.indexId !== indexId) fail('asset_index_mismatch');
    }
    const stat = fs.statSync(file); if (stat.size > 100 * 1024 * 1024) fail('file_size_invalid');
    const bytes = fs.readFileSync(file), blob = {...this.blobs.put(bytes),...inspectWav(bytes)};
    const assetId = randomUUID();
    this.tx(() => {
      this.db.prepare('INSERT INTO assets VALUES(?,?,?,?,?,?,?,?,?)').run(assetId,id,JSON.stringify(range),voiceKey(voice),JSON.stringify(blob),indexId,null,provenance,now());
      this.event(id,'asset_candidate_saved',{assetId,segment:range,sha256:blob.sha256,indexId});
    });
    return this.asset(assetId);
  }
  asset(id) {
    const a = this.db.prepare('SELECT * FROM assets WHERE id=?').get(id); if (!a) fail('asset_not_found');
    return {...a,segment:decode(a.segment),voice:decode(a.voice),blob:decode(a.blob),review:decode(a.review)};
  }
  importLegacyReview(id, review) {
    const a = this.asset(id);
    if (review?.approved !== true || review.sha256 !== a.blob.sha256 || !review.approvalSource || !review.scope) fail('legacy_review_mismatch');
    this.blobs.read(a.blob);
    const preserved = {...review,source:'existing_human_overall_review',importedAt:now()};
    this.tx(() => {
      this.db.prepare('UPDATE assets SET review=? WHERE id=?').run(JSON.stringify(preserved),id);
      this.event(a.recording_id,'legacy_review_imported',{assetId:id,sha256:a.blob.sha256,source:review.approvalSource});
    });
    return this.asset(id);
  }
  reviewAsset(id, {approved,reviewer,note,checks}) {
    reviewer = text(reviewer); note = text(note,2000);
    const a = this.asset(id);
    if (typeof approved !== 'boolean' || approved && !['song','segment','voice','aCappella','ending'].every(k => checks?.[k] === true)) fail('asset_review_incomplete');
    this.blobs.read(a.blob);
    const review = {approved,reviewer,note,checks,at:now()};
    this.tx(() => {
      this.db.prepare('UPDATE assets SET review=? WHERE id=?').run(JSON.stringify(review),id);
      this.event(a.recording_id,'asset_reviewed',{assetId:id,...review});
    });
    return this.asset(id);
  }
  state(id, input, voice = Z_VOICE) {
    const r = this.recording(id), request = selector(input), material = this.material(id), key = voiceKey(voice);
    const all = this.db.prepare('SELECT id FROM assets WHERE recording_id=? AND voice=?').all(id,key).map(a => this.asset(a.id));
    const approved = all.filter(a => a.review?.approved === true && this.blobs.available(a.blob));
    const asset = approved.find(a => canonical(a.segment) === canonical(request) && (!a.index_id || a.index_id === material.activeIndexId));
    const status = asset ? 'segment_ready' : approved.length ? 'song_partially_mastered' : 'song_unmastered';
    return {recording:{id:r.id,title:r.title,artist:r.artist,version:r.version},request,status,
      label:({segment_ready:'所点唱段已有素材',song_partially_mastered:'这首歌已有部分唱段',song_unmastered:'这首歌尚无已掌握唱段'})[status],
      assetId:asset?.id || null,masteredSegments:approved.map(a => ({assetId:a.id,segment:a.segment,indexId:a.index_id})),
      pendingReview:all.filter(a => !a.review).map(a => a.id),material,locator:this.resolve(id,request),job:this.job(id),
      next:asset ? 'use_reviewed_asset' : material.status !== 'ready' ? 'prepare_or_review_material' : this.resolve(id,request).status !== 'resolved' ? 'prepare_or_review_index' : 'generation_adapter_required',
      capabilities};
  }
  // Local integration boundary: contains references to our retained resources,
  // not invented Vivi fields or a claim that Vivi accepts reference conditioning.
  generationInput(id, request) {
    const locator = this.resolve(id,request);
    if (locator.status !== 'resolved') fail('segment_unresolved');
    const {title,artist,version}=this.recording(id);
    return {recording:{id,title,artist,version},locator,referenceAudio:this.material(id).audio,
      lyrics:this.material(id).lyrics,providerAdapter:'not_connected',readyToGenerate:false};
  }
}
