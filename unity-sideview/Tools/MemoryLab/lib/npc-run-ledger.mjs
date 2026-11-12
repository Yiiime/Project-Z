import { uid } from './store.mjs';
import { clone } from './catalog.mjs';

// Operational history is separate from character memory. Only the actor owning
// a record, or an actual recipient of an event, gets a journal entry.
export class NpcRunLedger {
  constructor(store) { this.store = store; }

  append(world, actor, type, sourceId, details = {}) {
    if (!world.actors?.[actor]) throw new Error('Unknown runtime actor');
    const previous = this.store.actorRecords(world.id, 'npc_run', actor, world.epoch, { descending: true, limit: 1 })[0];
    return this.store.put('npc_run', {
      id: uid('npc_run'), experimentId: world.id, epoch: world.epoch,
      actor, runtime_seq: (previous?.runtime_seq || 0) + 1,
      hour: world.hour, day: world.day, period: world.period,
      runId: world.runId, type, source_id: sourceId || null,
      at: new Date().toISOString(), details: clone(details),
    });
  }

  source(world, kind, record, previous) {
    if (record.epoch !== world.epoch || JSON.stringify(previous) === JSON.stringify(record)) return;
    let actors = [];
    if (kind === 'event' || kind === 'perception_context') actors = record.visible || [];
    else if (['job', 'proposal', 'action', 'social_decision'].includes(kind)) actors = [record.actor];
    else if (kind === 'conversation') actors = record.participants || record.actors || [];
    for (const actor of new Set(actors.filter(a => Object.hasOwn(world.actors, a)))) {
      this.append(world, actor, kind + '.' + (record.status || 'received'), record.id, { record });
    }
  }

  state(world, previousWorld) {
    for (const [actor, state] of Object.entries(world.actors || {})) {
      const previous = previousWorld?.epoch === world.epoch ? previousWorld.actors?.[actor] : null;
      const focus = world.focus?.actor === actor;
      const previousFocus = previousWorld?.focus?.actor === actor;
      if (previous && JSON.stringify(previous) === JSON.stringify(state) && focus === previousFocus) continue;
      this.append(world, actor, previous ? 'state.changed' : 'runtime.opened', null, {
        state, player_conversation_active: focus,
      });
    }
  }

  inspect(world, actor, { after = 0, limit = 80 } = {}) {
    if (!Number.isInteger(after) || after < 0 || !Number.isInteger(limit) || limit < 1 || limit > 200)
      throw new Error('Runtime cursor/limit is invalid');
    const entries = this.store.actorRecords(world.id, 'npc_run', actor, world.epoch, { after, limit: limit + 1 });
    return {
      entries: entries.slice(0, limit), has_more: entries.length > limit,
      next_cursor: entries.length ? entries[Math.min(entries.length, limit) - 1].runtime_seq : after,
    };
  }
}
