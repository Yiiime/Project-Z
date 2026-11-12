import { clone } from "./catalog.mjs";

// Directed beliefs belong to their observer. Filling an absent edge never
// changes an existing save's values, nor invents a shared biographical event.
export function ensureRelations(actor, catalog) {
  actor.peer_relations ||= {};
  for (const target of catalog.actors.keys()) {
    if (target === actor.id || actor.peer_relations[target]) continue;
    const baseline = catalog.registry.initial_relationships?.[actor.id]?.[target];
    actor.peer_relations[target] = baseline
      ? { ...clone(baseline), impression_formed: true, history: [] }
      : { affinity: 50, trust: 50, source: "neutral_unspecified", impression_formed: false, history: [] };
  }
  return actor.peer_relations;
}

export function applyRelationChange(peer, change, source, turnId) {
  const before = { affinity: peer.affinity, trust: peer.trust };
  peer.affinity += change.affinity_delta;
  peer.trust += change.trust_delta;
  peer.impression_formed = true;
  peer.history = [...(peer.history || []), {
    turn_id: turnId,
    reason: change.reason,
    source_event_ids: [...change.source_event_ids],
    source_kind: source?.kind || "claim",
    source_actor_id: source?.speaker || "engine",
    before,
    after: { affinity: peer.affinity, trust: peer.trust },
  }].slice(-12);
}
