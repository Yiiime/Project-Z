import { DatabaseSync } from "node:sqlite";
import { randomUUID, createHash } from "node:crypto";
import { mkdirSync } from "node:fs";
import { dirname } from "node:path";

export const uid = (prefix) =>
  `${prefix}_${randomUUID().replaceAll("-", "").slice(0, 16)}`;

export class Store {
  constructor(path = ":memory:") {
    if (path !== ":memory:")
      mkdirSync(dirname(path), { recursive: true, mode: 0o700 });
    this.db = new DatabaseSync(path);
    this.db.exec(`PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;
      CREATE TABLE IF NOT EXISTS experiments (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS settings (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS team_sessions (token_hash TEXT PRIMARY KEY, payload TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS game_archives (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS snapshot_blobs (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS records (
        id TEXT PRIMARY KEY, experiment_id TEXT NOT NULL REFERENCES experiments(id),
        kind TEXT NOT NULL, seq INTEGER NOT NULL, payload TEXT NOT NULL);
      CREATE INDEX IF NOT EXISTS records_scope ON records(experiment_id, kind, seq);
      CREATE INDEX IF NOT EXISTS records_actor ON records(experiment_id, kind,
        json_extract(payload, '$.actor'), json_extract(payload, '$.epoch'), seq);`);
  }
  transaction(fn) {
    if (this.transactionDepth) return fn();
    this.transactionDepth = 1;
    this.db.exec("BEGIN IMMEDIATE");
    try {
      const value = fn();
      this.db.exec("COMMIT");
      return value;
    } catch (error) {
      this.db.exec("ROLLBACK");
      throw error;
    } finally {
      this.transactionDepth = 0;
    }
  }
  experiments() {
    return this.db
      .prepare("SELECT payload FROM experiments ORDER BY rowid DESC")
      .all()
      .map((r) => JSON.parse(r.payload));
  }
  experiment(id) {
    const r = this.db
      .prepare("SELECT payload FROM experiments WHERE id=?")
      .get(id);
    return r && JSON.parse(r.payload);
  }
  saveExperiment(exp) {
    this.db
      .prepare(
        "INSERT INTO experiments VALUES (?, ?) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
      )
      .run(exp.id, JSON.stringify(exp));
    return exp;
  }
  createExperiment(name, ownerId) {
    const count = this.experiments().filter(
      (e) => e.ownerId === ownerId,
    ).length;
    const exp = {
      id: uid("exp"),
      name: name || `实验 ${String(count + 1).padStart(2, "0")}`,
      hour: 0,
      epoch: 1,
      version: 1,
      createdAt: new Date().toISOString(),
      ...(ownerId ? { ownerId } : {}),
    };
    this.saveExperiment(exp);
    this.event(exp, "created", "实验已创建", {});
    return exp;
  }
  records(expId, kind) {
    return this.db
      .prepare(
        "SELECT payload FROM records WHERE experiment_id=? AND kind=? ORDER BY seq",
      )
      .all(expId, kind)
      .map((r) => JSON.parse(r.payload));
  }
  record(id) {
    const r = this.db.prepare("SELECT payload FROM records WHERE id=?").get(id);
    return r && JSON.parse(r.payload);
  }
  actorRecords(expId, kind, actor, epoch, { after = 0, limit = 200, descending = false } = {}) {
    return this.db.prepare(`SELECT payload FROM records WHERE experiment_id=? AND kind=?
      AND json_extract(payload, '$.actor')=? AND json_extract(payload, '$.epoch')=?
      AND COALESCE(json_extract(payload, '$.runtime_seq'), 0)>?
      ORDER BY seq ${descending ? 'DESC' : 'ASC'} LIMIT ?`)
      .all(expId, kind, actor, epoch, after, limit).map(r => JSON.parse(r.payload));
  }
  put(kind, record) {
    const existing = this.db
      .prepare("SELECT seq FROM records WHERE id=?")
      .get(record.id);
    const seq =
      existing?.seq ??
      this.db.prepare("SELECT COALESCE(MAX(seq),0)+1 AS n FROM records").get()
        .n;
    this.db
      .prepare(
        "INSERT INTO records VALUES (?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
      )
      .run(record.id, record.experimentId, kind, seq, JSON.stringify(record));
    return record;
  }
  event(exp, type, text, details = {}) {
    return this.put("event", {
      id: uid("evt"),
      experimentId: exp.id,
      epoch: exp.epoch,
      hour: exp.hour,
      type,
      text,
      details,
      at: new Date().toISOString(),
    });
  }
  settings() {
    const r = this.db
      .prepare("SELECT payload FROM settings WHERE id=?")
      .get("provider");
    return r ? JSON.parse(r.payload) : {};
  }
  saveSettings(value) {
    this.db
      .prepare(
        "INSERT INTO settings VALUES (?,?) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
      )
      .run("provider", JSON.stringify(value));
  }
  clearRecords(expId) {
    this.db.prepare("DELETE FROM records WHERE experiment_id=?").run(expId);
  }
  archiveSnapshot(id, snapshot) {
    this.db
      .prepare("INSERT OR IGNORE INTO game_archives VALUES (?, ?)")
      .run(id, JSON.stringify(snapshot));
  }
  freezeCheckpoint(checkpoint) {
    return this.transaction(() => {
      const put = value => {
        const payload=JSON.stringify(value), id=createHash("sha256").update(payload).digest("hex");
        this.db.prepare("INSERT OR IGNORE INTO snapshot_blobs VALUES (?,?)").run(id,payload);return id;
      };
      const manifest={...checkpoint,records:Object.fromEntries(Object.entries(checkpoint.records).map(([kind,rows])=>[kind,rows.map(put)]))};
      const ref=put(manifest);return ref;
    });
  }
  thawCheckpoint(ref) {
    if(typeof ref!=="string"||!/^[a-f0-9]{64}$/.test(ref))throw Error("存档引用无效");
    const get=id=>{const row=this.db.prepare("SELECT payload FROM snapshot_blobs WHERE id=?").get(id);if(!row)throw Error("缺少角色存档数据库，请同时恢复后端备份或导入完整快照");return JSON.parse(row.payload);};
    const manifest=get(ref);
    return {...manifest,records:Object.fromEntries(Object.entries(manifest.records).map(([kind,ids])=>[kind,ids.map(get)]))};
  }
  teamSession(tokenHash) {
    const r = this.db
      .prepare("SELECT payload FROM team_sessions WHERE token_hash=?")
      .get(tokenHash);
    return r && JSON.parse(r.payload);
  }
  saveTeamSession(tokenHash, session) {
    this.db
      .prepare("INSERT INTO team_sessions VALUES (?, ?)")
      .run(tokenHash, JSON.stringify(session));
  }
  close() {
    this.db.close();
  }
}
