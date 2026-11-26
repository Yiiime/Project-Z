// Admission limits for CloudGame only. The private workers retain their provider-side policies.
// Each entry limits one UTC day per member / across the whole game.
const limits = {
  generationRequests: [600, 2400],
  providerModelCalls: [400, 1800],
  providerTokenUnits: [2400000, 10000000],
  providerTtsChars: [40000, 150000],
  speechRequests: [400, 1600],
  asrSeconds: [1800, 7200],
  liveSeconds: [5400, 21600],
  rooms: [12, 48],
};

function quotaError(message) {
  return Object.assign(new Error(message), {status: 429});
}

export class GameBudget {
  constructor(db) {
    this.db = db;
    db.exec(`
      CREATE TABLE IF NOT EXISTS cloud_usage (
        day TEXT NOT NULL, kind TEXT NOT NULL, member TEXT NOT NULL, units INTEGER NOT NULL,
        PRIMARY KEY(day, kind, member)
      );
      CREATE TABLE IF NOT EXISTS cloud_live_leases (
        member TEXT NOT NULL, room TEXT NOT NULL, end_second INTEGER NOT NULL,
        PRIMARY KEY(member, room)
      );
    `);
    this.used = db.prepare('SELECT COALESCE(SUM(units),0) AS total, COALESCE(SUM(CASE WHEN member=? THEN units ELSE 0 END),0) AS own FROM cloud_usage WHERE day=? AND kind=?');
    this.add = db.prepare('INSERT INTO cloud_usage VALUES(?,?,?,?) ON CONFLICT(day,kind,member) DO UPDATE SET units=units+excluded.units');
    this.lease = db.prepare('SELECT end_second FROM cloud_live_leases WHERE member=? AND room=?');
    this.setLease = db.prepare('INSERT INTO cloud_live_leases VALUES(?,?,?) ON CONFLICT(member,room) DO UPDATE SET end_second=excluded.end_second');
    this.expireLeases = db.prepare('DELETE FROM cloud_live_leases WHERE end_second < ?');
    this.members = new Map();
    this.inFlight = 0;
    this.window = 0;
    this.requests = 0;
  }

  enter(member) {
    const minute = Math.floor(Date.now() / 60000);
    if (this.window !== minute) {
      this.window = minute;
      this.requests = 0;
      for (const [id, item] of this.members) {
        if (!item.active) this.members.delete(id);
        else item.count = 0;
      }
    }
    const item = this.members.get(member) || {active: 0, count: 0};
    if (item.active >= 16 || this.inFlight >= 48 || item.count >= 360 || this.requests >= 1200) {
      throw quotaError('game_busy');
    }
    this.members.set(member, item);
    item.active++;
    item.count++;
    this.inFlight++;
    this.requests++;
    return () => {
      item.active--;
      this.inFlight--;
    };
  }

  // Only authenticated, allowlisted game operations call this method. Retries are charged
  // on admission, including provider failures. Capacity to stop and disconnect is independent.
  charge(member, counts, roomToRenew = null) {
    if (!roomToRenew && !Object.keys(counts).length) return;
    const date = new Date();
    const today = date.toISOString().slice(0, 10);
    const second = Math.floor(date.getTime() / 1000);
    this.db.exec('BEGIN IMMEDIATE');
    try {
      if (roomToRenew) {
        // The existing LiveSession worker pauses 30 seconds after its final heartbeat.
        // Reserve only the extension; frequent heartbeats must not consume 30s each.
        this.expireLeases.run(second);
        const oldEnd = this.lease.get(member, roomToRenew)?.end_second || second;
        counts.liveSeconds = Math.max(0, second + 30 - Math.max(oldEnd, second));
        this.setLease.run(member, roomToRenew, second + 30);
      }
      for (const [kind, amount] of Object.entries(counts)) {
        if (!Object.hasOwn(limits, kind) || !Number.isSafeInteger(amount) || amount < 0) {
          throw new Error('invalid_quota_charge');
        }
        if (!amount) continue;
        const used = this.used.get(member, today, kind);
        const [perMember, allMembers] = limits[kind];
        if (used.own + amount > perMember || used.total + amount > allMembers) {
          throw quotaError('daily_' + kind + '_limit');
        }
        this.add.run(today, kind, member, amount);
      }
      this.db.exec('COMMIT');
    } catch (error) {
      this.db.exec('ROLLBACK');
      throw error;
    }
  }
}
