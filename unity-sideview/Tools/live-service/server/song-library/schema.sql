PRAGMA foreign_keys=ON;
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS recordings (
  id TEXT PRIMARY KEY, identity TEXT UNIQUE NOT NULL, title TEXT NOT NULL,
  artist TEXT NOT NULL, version TEXT NOT NULL, descriptor TEXT,
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS materials (
  recording_id TEXT PRIMARY KEY REFERENCES recordings(id),
  audio TEXT, lyrics TEXT, review TEXT, active_index TEXT, updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS indexes (
  id TEXT PRIMARY KEY, recording_id TEXT NOT NULL REFERENCES recordings(id),
  revision INTEGER NOT NULL, audio_sha TEXT NOT NULL, lyrics_sha TEXT NOT NULL,
  document TEXT NOT NULL, review TEXT, created_at TEXT NOT NULL,
  UNIQUE(recording_id, revision)
);
CREATE TABLE IF NOT EXISTS assets (
  id TEXT PRIMARY KEY, recording_id TEXT NOT NULL REFERENCES recordings(id),
  segment TEXT NOT NULL, voice TEXT NOT NULL, blob TEXT NOT NULL,
  index_id TEXT REFERENCES indexes(id), review TEXT, provenance TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS jobs (
  id TEXT PRIMARY KEY, recording_id TEXT UNIQUE NOT NULL REFERENCES recordings(id),
  status TEXT NOT NULL, stage TEXT NOT NULL, attempt INTEGER NOT NULL DEFAULT 0,
  lease TEXT, lease_until INTEGER, error TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS requests (
  id TEXT PRIMARY KEY, input TEXT NOT NULL, result TEXT NOT NULL, created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS events (
  seq INTEGER PRIMARY KEY AUTOINCREMENT, recording_id TEXT, job_id TEXT,
  type TEXT NOT NULL, data TEXT NOT NULL, created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS alignment_jobs (
  id TEXT PRIMARY KEY, recording_id TEXT NOT NULL REFERENCES recordings(id),
  cache_key TEXT NOT NULL, audio_sha TEXT NOT NULL, lyrics_sha TEXT NOT NULL,
  config TEXT NOT NULL, status TEXT NOT NULL, attempt INTEGER NOT NULL DEFAULT 0,
  lease_until INTEGER, result TEXT, error TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
  UNIQUE(recording_id, cache_key)
);
