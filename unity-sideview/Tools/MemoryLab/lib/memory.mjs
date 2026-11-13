import { uid } from "./store.mjs";
import {
  memoryState,
  memoryView,
  retrievalCandidates,
} from "./natural-memory.mjs";
import { MemoryConsolidator } from "./consolidator.mjs";
import { clone } from "./catalog.mjs";
export class MemoryBank {
  constructor(store, catalog, provider) {
    this.store = store;
    this.catalog = catalog;
    this.provider = provider;
    this.consolidator = new MemoryConsolidator(
      store,
      provider,
      catalog.registry.actors.map((a) => ({
        id: a.actor_id,
        name: a.name,
        personality: catalog.profile(a.actor_id),
      })),
    );
    this.running = new Set();
  }
  records(world, kind) {
    return this.store
      .records(world.id, kind)
      .filter(
        (r) =>
          r.epoch === world.epoch &&
          (r.createdHour ?? r.hour ?? 0) <= world.hour,
      );
  }
  remember(world, event, selected = []) {
    const people = event.visible.filter((a) => a !== "player");
    const message = {
      id: "msg_" + event.id,
      eventId: event.id,
      experimentId: world.id,
      epoch: world.epoch,
      hour: event.hour,
      participants: event.visible,
      speakerId: event.speaker,
      recipientId: event.recipient || "",
      conversationId: event.conversationId || "",
      content: event.text,
      status: "complete",
      origin: event.kind === "claim" ? "dialogue" : "world",
      disclose_to: event.disclose_to,
    };
    this.store.put("message", message);
    for (const actor of people) {
      const self = actor === event.speaker,
        kind = event.kind === "claim" ? "hearsay" : "experience";
      const name =
        event.speaker === "player"
          ? "字小节"
          : this.catalog.actors.has(event.speaker)
            ? this.catalog.actor(event.speaker).name
            : "引擎";
      this.store.put("memory", {
        id: "mem_" + event.id + "_" + actor,
        experimentId: world.id,
        epoch: world.epoch,
        actorId: actor,
        kind,
        content:
          event.kind === "claim"
            ? `${self ? "我说过" : name + "说"}：${event.text}`
            : event.text,
        gist:
          event.kind === "claim"
            ? `与${name}有关的一次交谈。`
            : event.gist ||
              "经历过一次与" +
                (event.subject ||
                  this.catalog.place(event.location || "").name ||
                  "岛上生活") +
                "有关的事情。",
        subject: event.subject || name,
        speakerId: event.speaker,
        sourceLabel:
          event.kind === "claim"
            ? "实际交谈原话，内容未核实"
            : "实际观察与回执",
        evidence: [{ messageId: message.id, quote: event.text }],
        dependencies: [],
        createdHour: event.hour,
        originalHour: event.hour,
        importance: event.importance || 1,
        emotion: 0,
        keywords: event.keywords || [],
        recallCount: 0,
        detailRecallCount: 0,
        origin:
          event.kind === "claim" ? "pending_extraction" : "world_observation",
        disclose_to: event.disclose_to,
      });
      if (event.kind === "claim")
        this.store.put("memory_job", {
          id: uid("memoryjob"),
          experimentId: world.id,
          epoch: world.epoch,
          hour: event.hour,
          actorId: actor,
          eventId: event.id,
          selected: actor === event.speaker ? selected : [],
          status: "pending",
          attempts: 0,
        });
    }
  }
  disclosure(world, m, seen = new Set()) {
    if (seen.has(m.id)) return [m.actorId];
    seen.add(m.id);
    if (m.disclose_to) return m.disclose_to;
    const messages = new Map(
      this.records(world, "message").map((e) => [e.id, e]),
    );
    const scopes = (m.evidence || []).map(
      (e) => messages.get(e.messageId)?.disclose_to || [m.actorId],
    );
    if (!scopes.length) return [m.actorId];
    return scopes.reduce((a, b) => a.filter((x) => b.includes(x)));
  }
  select(world, actor, query, limit = 10) {
    const all = this.records(world, "memory").filter(
      (m) => !(m.origin === "pending_extraction" && m.speakerId === actor),
    );
    const result = retrievalCandidates(all, world, query, actor, 40);
    const chosen = result.candidates.slice(0, limit).map((v) => {
      const m = all.find((m) => m.id === v.id);
      return {
        ...v,
        summary: v.content,
        source_event_ids: [
          ...new Set(
            (m.evidence || []).map(
              (e) => this.store.record(e.messageId)?.eventId || e.messageId,
            ),
          ),
        ],
        disclose_to: this.disclosure(world, m),
        kind:
          v.kind === "reflection"
            ? "inference"
            : v.kind === "hearsay"
              ? "claim"
              : "observation",
      };
    });
    return { selected: chosen, excluded: result.excluded };
  }
  facts(world, actor) {
    return this.records(world, "event")
      .filter((e) => e.kind === "fact" && e.visible.includes(actor))
      .map((e) => ({
        id: e.id,
        summary: e.text,
        kind: "verified",
        source_event_ids: [e.id],
        disclose_to: e.disclose_to,
      }));
  }
  history(world, actor, conversation = "", limit = 8) {
    return this.records(world, "message")
      .filter(
        (m) =>
          m.participants.includes(actor) &&
          m.hour >= world.hour - 2 &&
          (!conversation || m.conversationId === conversation),
      )
      .slice(-limit)
      .map((m) => ({
        id: m.id,
        speakerId: m.speakerId,
        text: m.content,
        source_event_ids: [m.eventId || m.id],
        kind: m.origin === "dialogue" ? "claim" : "observation",
      }));
  }
  inspect(world, actor) {
    const all = this.records(world, "memory");
    return {
      actorId: actor,
      memories: all
        .filter((m) => m.actorId === actor)
        .map((m) => ({
          ...m,
          ...memoryView(m, world),
          content: m.content,
          recallView: memoryView(m, world),
          state: memoryState(m, world, all),
          evidence: (m.evidence || []).map((e) => ({
            ...e,
            source: this.store.record(e.messageId)?.content || "",
          })),
        })),
      known_facts: this.facts(world, actor),
      pending: this.records(world, "memory_job").filter(
        (j) => j.actorId === actor && j.status !== "complete",
      ).length,
    };
  }
  async consolidate(worldId, maxJobs = 1) {
    const world = this.store.experiment(worldId);
    if (
      !world ||
      this.running.has(worldId) ||
      !this.provider?.publicConfig().configured
    )
      return [];
    this.running.add(worldId);
    const processed = [];
    try {
      for (const job of this.records(world, "memory_job")
        .filter(
          (j) => ["pending", "failed"].includes(j.status) && j.attempts < 3,
        )
        .slice(0, maxJobs)) {
        const message = this.store.record("msg_" + job.eventId);
        if (!message) {
          job.status = "failed";
          job.error = "原话来源不存在";
          this.store.put("memory_job", job);
          continue;
        }
        job.attempts++;
        job.status = "running";
        this.store.put("memory_job", job);
        const eventWorld = {
            ...this.store.experiment(worldId),
            hour: job.hour,
          },
          trace = {
            id: "trace_" + job.id,
            experimentId: worldId,
            epoch: world.epoch,
            version: world.version,
            hour: job.hour,
            actorId: job.actorId,
            observers: [job.actorId],
            evidenceMessages: [message],
            selected: job.selected || [],
            requests: {},
            responses: {},
            addedIds: [],
            status: "partial",
          };
        const controller = new AbortController(),
          timer = setTimeout(() => controller.abort(), 8000);
        try {
          await this.consolidator.extract(
            eventWorld,
            trace,
            message,
            message,
            trace.selected,
            { signal: controller.signal },
          );
          const raw = this.store.record(
            "mem_" + job.eventId + "_" + job.actorId,
          );
          if (raw) {
            raw.supersededBy = trace.addedIds[0] || "no_longterm_" + job.id;
            this.store.put("memory", raw);
          }
          job.status = "complete";
          const current = this.store.experiment(worldId);
          if (current?.epoch === world.epoch) {
            current.memoryRevision = (current.memoryRevision || 0) + 1;
            this.store.saveExperiment(current);
            const fresh = this.consolidator
              .unreflected(current, job.actorId)
              .filter((m) => m.origin !== "pending_extraction");
            if (fresh.length >= 3 || fresh.some((m) => m.importance === 3))
              this.store.put("reflection_job", {
                id: "reflect_" + job.actorId + "_" + job.eventId,
                experimentId: worldId,
                epoch: world.epoch,
                hour: current.hour,
                actorId: job.actorId,
                status: "pending",
              });
          }
          processed.push(job.id);
        } catch {
          job.status = "failed";
          job.error = "记忆整理未完成，原话已保留";
        } finally {
          clearTimeout(timer);
          if (this.store.experiment(worldId)?.epoch === job.epoch)
            this.store.put("memory_job", job);
        }
      }
    } finally {
      this.running.delete(worldId);
    }
    return processed;
  }
  async reflect(worldId, max = 1) {
    const world = this.store.experiment(worldId);
    if (
      !world ||
      this.running.has(worldId) ||
      !this.provider?.publicConfig().configured
    )
      return;
    this.running.add(worldId);
    try {
      for (const job of this.records(world, "reflection_job")
        .filter((j) => j.status === "pending")
        .slice(0, max)) {
        const trace = {
            id: "trace_" + job.id,
            experimentId: worldId,
            epoch: world.epoch,
            hour: world.hour,
            requests: {},
            responses: {},
            addedIds: [],
          },
          controller = new AbortController(),
          timer = setTimeout(() => controller.abort(), 8000);
        try {
          await this.consolidator.reflectInto(world, job.actorId, trace, {
            signal: controller.signal,
          });
          job.status = "complete";
        } catch {
          job.status = "failed";
        } finally {
          clearTimeout(timer);
          if (this.store.experiment(worldId)?.epoch === job.epoch)
            this.store.put("reflection_job", job);
        }
      }
    } finally {
      this.running.delete(worldId);
    }
  }
}
