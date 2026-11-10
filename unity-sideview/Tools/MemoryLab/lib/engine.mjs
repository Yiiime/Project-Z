import { memoryProjection, privateOffer, narrativeChange, rememberNarration } from "./live-memory.mjs";
import { uid } from "./store.mjs";
import { clone } from "./catalog.mjs";
import { Policy, TurnError, canonical } from "./policy.mjs";
import { TaskEngine } from "./tasks.mjs";
import { MemoryBank } from "./memory.mjs";
import { ActorRuntime } from "./actor-runtime.mjs";
const encode = (v) => JSON.stringify(canonical(v));
const metrics = ["affinity", "trust", "world_suspicion"];
export class Engine {
  constructor(
    store,
    catalog,
    provider,
    { decide, graph, perceive, hear, rootBudget = 6 } = {},
  ) {
    this.store = store;
    this.catalog = catalog;
    this.provider = provider;
    this.graph = graph || catalog.graph;
    this.perceive = perceive;
    this.hear = hear;
    this.rootBudget = rootBudget;
    this.policy = new Policy(catalog);
    this.tasks = new TaskEngine(store, catalog, (w, id) => this.event(w, id));
    this.memory = new MemoryBank(store, catalog, provider);
    this.runtime = new ActorRuntime(catalog, this.policy, provider);
    this.decide = decide || ((c) => this.runtime.generate(c));
    this.inflight = new Map();
  }
  world(id) {
    const w = this.store.experiment("world_" + id);
    if (!w) throw new TurnError("未知游戏进度");
    return w;
  }
  save(w) {
    return this.store.saveExperiment(w);
  }
  actor(world, id) {
    const w = typeof world === "string" ? this.world(world) : world;
    const a = w.actors[id];
    if (!a) throw new TurnError("未知角色");
    return a;
  }
  records(w, kind) {
    return this.store.records(w.id, kind).filter((r) => r.epoch === w.epoch);
  }
  event(world, id) {
    if (!id) return null;
    const w = this.world(world),
      e = this.store.record(id);
    return e?.experimentId === w.id && e.epoch === w.epoch ? e : null;
  }
  initialActors(initial = {}) {
    const actors = {};
    for (const [actor, e] of this.catalog.actors) {
      const names = [...metrics, ...(actor === "z" ? [] : ["persuasion"])],
        info = Object.fromEntries(
          names.map((k) => [
            k,
            this.catalog.registry.initial_metric_policy[k].default,
          ]),
        );
      info.emotion = "relaxed";
      info.changes = {};
      info.memory_updates = [];
      info.knowledge_updates = [];
      if (actor !== "z")
        info.support_willingness =
          actor === "zhou_bobo" ? "unwilling" : "unassessed";
      for (const [k, v] of Object.entries(initial[actor] || {})) {
        const policy = this.catalog.registry.initial_metric_policy[k];
        if (
          !names.includes(k) ||
          !Number.isInteger(v) ||
          v < policy.min ||
          v > policy.max ||
          (actor === "zhou_bobo" && k === "persuasion" && v !== 0)
        )
          throw new TurnError("初始数值超出范围");
        info[k] = v;
      }
      actors[actor] = {
        id: actor,
        name: e.name,
        scene: e.default_location,
        version: 0,
        info,
        peer_relations: {},
        applied_event_ids: [],
        agenda: null,
        pending_action: null,
        previous_result: null,
        last_external_event: null,
        next_agenda_tick: 30,
      };
    }
    if (Object.keys(initial).some((id) => !actors[id]))
      throw new TurnError("初始角色无效");
    return actors;
  }
  create(id = uid("world"), initial = {}) {
    if (this.store.experiment("world_" + id)) throw new TurnError("进度已存在");
    const actors = this.initialActors(initial);
    const w = {
      id: "world_" + id,
      worldId: id,
      runId: uid("run"),
      active: true,
      epoch: 1,
      version: 1,
      seq: 0,
      responseRevision: 0,
      memoryRevision: 0,
      tick: 0,
      day: 1,
      period: 0,
      hour: 0,
      actors,
      initialMetrics: clone(initial),
      focus: null,
      encounters: {},
      wakes: {},
      reflected: {},
      contentRevision: this.catalog.registry.pack_version,
      createdAt: new Date().toISOString(),
    };
    this.save(w);
    this.tasks.list(id);
    return w;
  }
  addEvent(
    world,
    {
      id = uid("evt"),
      text,
      kind = "claim",
      speaker = "engine",
      visible = [],
      disclose_to = visible,
      importance = 1,
      conversationId = "",
      recipient = "",
      channel = "",
      location = "",
      gist,
      subject,
      sources = [],
      hour,
      seq,
      selected = [],
    },
  ) {
    const w = this.world(world);
    if (
      !text ||
      typeof text !== "string" ||
      !["claim", "fact", "observation", "encounter", "inference"].includes(kind)
    )
      throw new TurnError("事件无效");
    if (
      [...visible, ...disclose_to].some(
        (a) => a !== "player" && !this.catalog.actors.has(a),
      )
    )
      throw new TurnError("知情范围无效");
    const e = {
      id,
      experimentId: w.id,
      epoch: w.epoch,
      hour: hour ?? w.hour,
      seq: seq ?? w.seq + 1,
      text,
      kind,
      speaker,
      visible: [...new Set(visible)],
      disclose_to: [...new Set(disclose_to)],
      importance,
      conversationId,
      recipient,
      channel,
      location,
      gist,
      subject,
      sources,
    };
    if (e.hour > w.hour || e.seq < 1) throw new TurnError("不能提交未来事件");
    const prior = this.store.record(id);
    if (prior) {
      if (
        prior.experimentId !== w.id ||
        encode({ ...prior, selected: undefined }) !==
          encode({ ...e, selected: undefined })
      )
        throw new TurnError("同一事件重复内容冲突");
      return prior;
    }
    if (e.seq !== w.seq + 1) throw new TurnError("事件序号不连续");
    this.store.transaction(() => {
      this.store.put("event", e);
      w.seq = e.seq;
      for (const actor of e.visible.filter((a) => a !== "player")) {
        w.actors[actor].version++;
        if (
          (speaker === "player" || speaker === "engine") &&
          kind !== "encounter"
        )
          w.actors[actor].last_external_event = id;
      }
      this.save(w);
      this.memory.remember(w, e, selected);
    });
    return e;
  }
  job(
    world,
    actor,
    event,
    {
      mode = "world_event",
      conversation = "",
      channel = "",
      root = event,
      hop = 0,
      environment = {},
    } = {},
  ) {
    const w = this.world(world);
    this.actor(w, actor);
    const e = this.event(world, event);
    if (!e?.visible.includes(actor)) throw new TurnError("角色没有看到此事件");
    const j = {
      id: uid("job"),
      experimentId: w.id,
      epoch: w.epoch,
      hour: w.hour,
      world,
      actor,
      event,
      mode,
      conversation,
      channel,
      root,
      hop,
      due: w.tick,
      status: "pending",
      environment,
    };
    this.store.put("job", j);
    return j;
  }
  visible(w, a, b) {
    if (a === b) return true;
    const aa = this.actor(w, a),
      bb = this.actor(w, b);
    if (this.perceive) return Boolean(this.perceive(w, a, b));
    return aa.scene === bb.scene && !aa.scene.endsWith("_pending");
  }
  held(w, actor, except = "") {
    return (
      w.focus?.actor === actor ||
      this.records(w, "conversation").some(
        (c) =>
          c.status === "active" && c.id !== except && c.actors.includes(actor),
      )
    );
  }
  focus(world, actor, owner = "player") {
    const w = this.world(world);
    if (actor) this.actor(w, actor);
    if (!actor && w.focus && w.focus.owner !== owner) return false;
    if (w.focus?.actor === actor && w.focus?.owner === owner) return false;
    const old = w.focus?.actor;
    if (old) {
      this.runtime.cancel(world, old);
      for (const j of this.records(w, "job").filter(
        (j) =>
          j.actor === old &&
          j.mode === "player" &&
          ["pending", "running", "proposed"].includes(j.status),
      )) {
        j.status = "cancelled";
        this.store.put("job", j);
      }
      for (const j of this.records(w, "job").filter(
        (j) => j.actor === old && j.status === "paused",
      )) {
        j.status = "pending";
        j.due = w.tick;
        this.store.put("job", j);
      }
      for (const p of this.records(w, "proposal").filter(
        (p) =>
          p.status === "pending" &&
          p.actor === old &&
          p.context.interaction.mode === "player",
      )) {
        p.status = "cancelled";
        this.store.put("proposal", p);
      }
    }
    w.active = true;
    w.lastAccess = Date.now();
    w.focus = actor ? { actor, owner } : null;
    this.save(w);
    if (actor) this.cancel(world, { actor, backgroundOnly: true });
    return true;
  }
  cancel(world, { actor, conversation, backgroundOnly = false } = {}) {
    this.runtime.cancel(world, actor, conversation);
    const w = this.world(world);
    for (const kind of ["job", "proposal", "conversation"])
      for (const r of this.records(w, kind)) {
        const matches =
          (!actor || r.actor === actor || r.actors?.includes(actor)) &&
          (!conversation ||
            r.conversation === conversation ||
            r.id === conversation);
        const player = (r.mode || r.context?.interaction.mode) === "player";
        if (
          matches &&
          (!backgroundOnly || !player) &&
          ["pending", "running", "active"].includes(r.status)
        ) {
          r.status =
            backgroundOnly && kind === "job" && r.status === "pending"
              ? "paused"
              : "cancelled";
          this.store.put(kind, r);
        }
      }
  }
  player(world, actor, text, owner = "player", eventMeta = {}) {
    this.focus(world, actor, owner);
    const w = this.world(world),
      conversation = "player_" + owner + "_" + actor;
    const e = this.addEvent(world, {
      text,
      kind: "claim",
      speaker: "player",
      visible: [actor, "player"],
      conversationId: conversation,
      recipient: actor,
      location: w.actors[actor].scene,
      ...eventMeta,
    });
    return this.job(world, actor, e.id, {
      mode: "player",
      conversation,
      channel: "private_player_chat",
    });
  }
  observe(
    world,
    text,
    visible,
    {
      kind = "observation",
      disclose_to = visible,
      wake = true,
      importance = 2,
      location = "",
      gist,
      id,
      seq,
      hour,
    } = {},
  ) {
    const e = this.addEvent(world, {
      id,
      text,
      kind,
      speaker: "engine",
      visible,
      disclose_to,
      importance,
      location,
      gist,
      seq,
      hour,
    });
    if (wake)
      for (const actor of visible.filter((a) => a !== "player"))
        this.job(world, actor, e.id);
    return e;
  }
  place(world, actor, scene) {
    if (!this.catalog.map.places[scene]) throw new TurnError("未知地图地点");
    const w = this.world(world),
      a = this.actor(w, actor);
    if (a.pending_action) throw new TurnError("角色仍有待执行动作");
    a.scene = scene;
    a.version++;
    this.save(w);
    this.observe(
      world,
      "游戏确认你现在位于" + this.catalog.place(scene).name,
      [actor],
      { kind: "fact", wake: false, location: scene },
    );
  }
  advance(world, steps = 1) {
    const w = this.world(world);
    if (!Number.isInteger(steps) || steps < 1 || steps > 100)
      throw new TurnError("时间步无效");
    w.tick += steps;
    this.save(w);
    for (const a of Object.values(w.actors))
      if (
        !a.pending_action &&
        a.next_agenda_tick <= w.tick &&
        !this.records(w, "job").some(
          (j) => j.actor === a.id && ["pending", "running"].includes(j.status),
        )
      ) {
        const e = this.observe(
          world,
          "议程到点：继续或调整自己当前工作、休息和未完承诺。",
          [a.id],
          { wake: false },
        );
        this.job(world, a.id, e.id, { mode: "agenda" });
        const current = this.world(world);
        current.actors[a.id].next_agenda_tick = current.tick + 30;
        this.save(current);
      }
    this.scan(world);
  }
  day(world) {
    const w = this.world(world);
    if (w.day >= 7) throw new TurnError("当前已到Day7，可以继续对话");
    w.day++;
    w.hour += 24;
    w.version++;
    this.save(w);
    this.advance(world);
  }
  scan(world) {
    const w = this.world(world);
    if (w.host === "unity") return 0;
    const actors = Object.keys(w.actors).sort(),
      seen = new Set();
    let count = 0;
    outer: for (let i = 0; i < actors.length; i++)
      for (let j = i + 1; j < actors.length; j++) {
        const a = actors[i],
          b = actors[j];
        if (
          !this.visible(w, a, b) ||
          w.actors[a].pending_action ||
          w.actors[b].pending_action ||
          this.held(w, a) ||
          this.held(w, b)
        )
          continue;
        const key = [a, b].join(":"),
          sig = [
            w.actors[a].last_external_event,
            w.actors[b].last_external_event,
          ].join("|"),
          previous = w.encounters[key];
        seen.add(key);
        if (
          previous?.active &&
          previous.scene === w.actors[a].scene &&
          previous.signature === sig &&
          w.tick - previous.tick < 2
        )
          continue;
        if (
          w.wakes[a] === w.tick ||
          w.wakes[b] === w.tick ||
          this.records(w, "job").some(
            (x) =>
              [a, b].includes(x.actor) &&
              ["pending", "running"].includes(x.status) &&
              x.conversation.startsWith("encounter:"),
          )
        )
          continue;
        const e = this.observe(
          world,
          `你在${this.catalog.place(w.actors[a].scene).name}实际看见${this.catalog.actor(b).name}。可以继续自己的事、打招呼或谈一件愿意说的事，不要求交换秘密。`,
          [a, b],
          { kind: "encounter", wake: false, location: w.actors[a].scene },
        );
        this.job(world, a, e.id, {
          conversation: "encounter:" + key,
          environment: {
            social_opportunity: { nearby_actors: [b], kind: "co_location" },
          },
        });
        w.encounters[key] = {
          active: true,
          scene: w.actors[a].scene,
          signature: sig,
          tick: w.tick,
        };
        w.wakes[a] = w.wakes[b] = w.tick;
        if (++count >= 2) break outer;
      }
    for (const [key, value] of Object.entries(w.encounters))
      if (!seen.has(key)) value.active = false;
    const current = this.world(world);
    current.encounters = w.encounters;
    current.wakes = w.wakes;
    this.save(current);
    return count;
  }
  available(w, actor, j) {
    const a = w.actors[actor],
      actions = [
        { id: "wait_here", type: "wait", target: null, parameters: {} },
      ],
      env = j.environment || {};
    if (j.mode === "player")
      actions.push(...this.tasks.options(w.worldId, actor, j.event));
    const present =
      env.present_characters ||
      Object.keys(w.actors).filter((id) => this.visible(w, actor, id));
    if (j.mode !== "player" && w.focus?.actor !== actor)
      for (const peer of present) {
        if (
          peer === actor ||
          peer === "player" ||
          this.held(w, peer, j.conversation)
        )
          continue;
        if (
          j.environment?.social_opportunity &&
          !j.environment.social_opportunity.nearby_actors.includes(peer)
        )
          continue;
        if (!a.peer_relations[peer])
          a.peer_relations[peer] = {
            affinity: 50,
            trust: 50,
            impression_formed: false,
          };
        actions.push({
          id: "talk_" + peer,
          type: "communicate",
          target: peer,
          parameters: {
            channel: j.channel || "face_to_face",
            conversation_id: j.conversation || "conv_" + j.id + "_" + peer,
          },
        });
      }
    if (!a.pending_action) {
      const reachable = env.reachable_locations || this.graph[a.scene] || [];
      for (const target of reachable)
        actions.push({
          id: "move_" + target,
          type: "move",
          target,
          parameters: {},
        });
    }
    if (env.intent_only) {
      return [
        actions[0],
        ...(env.meeting_candidates || [])
          .filter((id) => id !== actor && !this.held(w, id))
          .map((id) => ({
            id: "meet_" + id,
            type: "schedule_meeting",
            target: id,
            parameters: {},
          })),
      ];
    }
    if (env.available_actions) return env.available_actions;
    return actions;
  }
  context(job) {
    const j = typeof job === "string" ? this.store.record(job) : job,
      w = this.world(j.world),
      a = this.actor(w, j.actor),
      event = this.event(j.world, j.event);
    if (!event?.visible.includes(j.actor))
      throw new TurnError("不可见的唤醒来源");
    const selected = this.memory.select(w, j.actor, event.text),
      actions = this.available(w, j.actor, j);
    let present =
      j.environment?.present_characters ||
      Object.keys(w.actors).filter((id) => this.visible(w, j.actor, id));
    if (j.mode === "player") present = [...new Set([...present, "player"])];
    const policy = Object.fromEntries(
      [...metrics, ...(j.actor === "z" ? [] : ["persuasion"])].map((k) => [
        k,
        {
          source:
            j.actor === "zhou_bobo" && k === "persuasion" ? "engine" : "model",
          min_delta: a.info[k] === null ? 0 : -a.info[k],
          max_delta: a.info[k] === null ? 0 : 100 - a.info[k],
        },
      ]),
    );
    const memories = [...selected.selected];
    memories.push({
      id: event.id,
      summary: event.text,
      kind: event.kind,
      source_event_ids: [event.id],
      disclose_to: event.disclose_to,
    });
    const facts = this.memory.facts(w, j.actor);
    const disclosure = Object.fromEntries(
      Object.keys(w.actors)
        .filter((id) => id !== j.actor)
        .map((id) => [
          id,
          [...facts, ...memories]
            .filter((m) => m.disclose_to?.includes(id) &&
              (j.environment?.actual_audience||[]).every(listener=>listener==="player"||m.disclose_to?.includes(listener)))
            .map((m) => m.id),
        ]),
    );
    let story;
    if (j.actor === "z" && j.mode === "player") {
      const events=this.records(w,"event");
      const projection=memoryProjection(w, events, {privateChat:true});
      const previous=events.findLast(e=>e.speaker==="z" && e.conversationId===j.conversation);
      const offer=privateOffer(projection,event.text,previous?.text||"");
      if(offer)story={id:offer.id,count:offer.count,line:offer.steps[offer.count]};
      const broadcast=projection.entries;
      // Keep actual public playback receipts visible even when a song is no longer recent conversation.
      const relevant=broadcast.filter(e=>event.text.includes(e.text.match(/《([^》]+)》/)?.[1]||"\0") ||
        /歌|唱/.test(event.text)&&/唱|播|点歌/.test(e.text));
      for(const e of (relevant.length?relevant:broadcast).slice(-3))
        if(!memories.some(m=>m.id===e.id))memories.push({id:e.id,summary:"第"+e.day+"天 "+e.speaker+"："+e.text,source_event_ids:[e.id],disclose_to:["z","player"]});
    }
    this.save(w);
    return {
      ...(story?{z_story:story}:{}),
      protocol_version: this.catalog.registry.protocol_version,
      session_id: w.worldId,
      turn_id: j.id,
      actor_id: j.actor,
      interaction: {
        mode: j.mode,
        source_actor_id:
          j.mode === "player"
            ? "player"
            : j.mode === "npc_message"
              ? event.speaker
              : "engine",
        source_event_id: event.id,
        input: event.text,
        conversation_id: j.conversation || null,
        ...(j.mode === "npc_message"
          ? { channel: j.channel, conversation_id: j.conversation }
          : {}),
      },
      plot_node: {
        id: "event_driven",
        summary: j.environment?.intent_only
          ? "尚未走近，只决定是否约见，台词保持空串，选择schedule_meeting或wait。"
          : "按已知与本局经历回应，日期不预定知情、援助或结局",
      },
      environment: {
        scene: a.scene,
        present_characters: present,
        reachable_locations:
          j.environment?.reachable_locations || this.graph[a.scene] || [],
        player_conversation_active: w.focus?.actor === j.actor,
        map_context: this.catalog.mapContext(a.scene, event.text, this.graph),
        ...j.environment,
      },
      character_state: {
        version: a.version,
        info: clone(a.info),
        known_facts: facts,
        memories,
        received_claims: memories.filter((m) => m.kind === "claim"),
        recent_conversation: this.memory.history(w, j.actor, j.conversation),
        applied_event_ids: clone(a.applied_event_ids),
        public_identity: this.catalog.identity(j.actor),
        peer_relations: clone(a.peer_relations),
        metric_policy: policy,
        peer_relation_policy: { max_abs_delta: 100 },
        disclosure_by_recipient: disclosure,
        allowed_goals: this.catalog.actor(j.actor).daily_goals,
        allowed_next_action_ids: actions.map((x) => x.id),
        agenda: a.agenda,
        cooperation_requests:
          j.mode === "player"
            ? this.tasks.requests(w.worldId, j.actor, event.id)
            : [],
        cooperation_state: this.tasks.cooperation(w.worldId, j.actor),
      },
      tasks:
        j.mode === "player"
          ? this.tasks
              .list(w.worldId, j.actor)
              .map((t) => ({
                ...t,
                collaborators: t.collaborators.filter(
                  (c) => c.actor_id === j.actor,
                ),
              }))
          : [],
      available_actions: actions,
      previous_action_result: a.previous_result,
      reply_budget: { max_dialogue_chars: 160, deadline_ms: 8000 },
    };
  }
  ready(world) {
    const w = this.world(world);
    return this.records(w, "job")
      .filter(
        (j) =>
          j.status === "pending" &&
          j.due <= w.tick &&
          !(j.mode === "player" && w.focus && w.focus.actor !== j.actor) &&
          !(j.mode !== "player" && this.held(w, j.actor, j.conversation)) &&
          !this.records(w, "proposal").some(
            (p) => p.actor === j.actor && p.status === "pending",
          ) &&
          !this.inflight.has(world + ":" + j.actor),
      )
      .sort(
        (a, b) =>
          (a.mode === "player" ? 0 : a.mode === "npc_message" ? 1 : 2) -
          (b.mode === "player" ? 0 : b.mode === "npc_message" ? 1 : 2),
      );
  }
  async run(world, max = 1, requestedJob = null) {
    const results = [];
    this.scan(world);
    for (let n = 0; n < max; n++) {
      const job = requestedJob
        ? this.ready(world).find((j) => j.id === requestedJob)
        : this.ready(world)[0];
      if (!job) break;
      const key = world + ":" + job.actor;
      this.inflight.set(key, job.id);
      job.status = "running";
      this.store.put("job", job);
      const context = this.context(job),
        w = this.world(world);
      try {
        const output = await this.decide(clone(context));
        this.policy.validate(context, output);
        const current = this.world(world);
        if (
          current.runId !== w.runId ||
          current.epoch !== w.epoch ||
          current.actors[job.actor].version !==
            context.character_state.version ||
          this.store.record(job.id).status === "cancelled"
        ) {
          job.status = "cancelled";
          results.push({ job: job.id, status: "cancelled" });
        } else {
          const proposal = {
            id: uid("proposal"),
            experimentId: w.id,
            epoch: w.epoch,
            hour: w.hour,
            runId: w.runId,
            world,
            actor: job.actor,
            status: "pending",
            context,
            output,
            job: job.id,
            conversation: job.conversation,
            root: job.root,
            hop: job.hop,
          };
          this.store.put("proposal", proposal);
          job.status = "proposed";
          results.push({
            job: job.id,
            actor: job.actor,
            status: "proposed",
            proposalId: proposal.id,
            output,
          });
        }
      } catch (e) {
        job.status = "failed";
        job.error = e instanceof TurnError ? e.message : "角色生成失败";
        results.push({
          job: job.id,
          actor: job.actor,
          status: "failed",
          error_type: e.constructor.name,
          validation_error: job.error,
        });
      } finally {
        this.store.put("job", job);
        this.inflight.delete(key);
      }
    }
    return results;
  }
  commit(world, proposalId, options = {}) {
    return this.store.transaction(() =>
      this.commitInside(world, proposalId, options),
    );
  }
  commitInside(
    world,
    proposalId,
    {
      visible,
      location,
      eventId,
      seq,
      hour,
      conversationId,
      replayDelivered = false,
    } = {},
  ) {
    const w = this.world(world),
      p = this.store.record(proposalId);
    if (!p || p.world !== world) throw new TurnError("未知提案");
    if (p.status === "committed") return p.output;
    if (
      !(
        (p.status === "pending" && p.runId === w.runId) ||
        (replayDelivered && p.status === "restored_pending")
      ) ||
      p.epoch !== w.epoch ||
      w.actors[p.actor].version !== p.context.character_state.version
    )
      throw new TurnError("提案已取消或角色状态已变");
    const mode = p.context.interaction.mode,
      output = p.output,
      action = output.action,
      info = output.info,
      actor = this.actor(w, p.actor);
    if (
      (mode !== "player" && w.focus?.actor === p.actor) ||
      (action.type === "communicate" && w.focus?.actor === action.target)
    )
      throw new TurnError("参与者正在和玩家交谈");
    if (
      action.type === "communicate" &&
      !this.visible(w, p.actor, action.target) &&
      !p.context.environment.present_characters.includes(action.target)
    )
      throw new TurnError("目标已离开交谈范围");
    const audience =
      visible ||
      (mode === "player"
        ? [p.actor, "player"]
        : action.type === "communicate"
          ? [p.actor, action.target]
          : [p.actor]);
    if (
      !audience.includes(p.actor) ||
      (action.type === "communicate" && !audience.includes(action.target)) ||
      (mode === "player" && !audience.includes("player"))
    )
      throw new TurnError("发言听众与目标不一致");
    if (action.type === "communicate") {
      for (const id of info.communication.shared_claim_ids) {
        const e = [
          ...p.context.character_state.known_facts,
          ...p.context.character_state.memories,
        ].find((x) => x.id === id);
        if (
          !e ||
          audience.some(
            (listener) =>
              listener !== "player" && !e.disclose_to.includes(listener),
          )
        )
          throw new TurnError("实际旁听者不在信息可分享范围");
      }
    }
    this.store.transaction(() => {
      for (const [metric, change] of Object.entries(info.changes))
        actor.applied_event_ids.push(...change.source_event_ids);
      for (const relation of info.peer_relation_changes || []) {
        const peer = actor.peer_relations[relation.target_actor_id];
        peer.affinity += relation.affinity_delta;
        peer.trust += relation.trust_delta;
        peer.impression_formed = true;
        actor.applied_event_ids.push(...relation.source_event_ids);
      }
      actor.applied_event_ids = [...new Set(actor.applied_event_ids)];
      actor.info = Object.fromEntries(
        Object.entries(clone(info)).filter(
          ([k]) =>
            ![
              "communication",
              "peer_relation_changes",
              "day_song_reply",
              "agenda_update",
              "social_decision",
              "task_participation",
              "task_followup",
            ].includes(k),
        ),
      );
      actor.info.changes = {};
      if (info.agenda_update) actor.agenda = clone(info.agenda_update);
      if (info.task_participation)
        this.tasks.decide(world, p.actor, info.task_participation);
      if (info.task_followup)
        this.tasks.followup(world, p.actor, info.task_followup);
      if (action.type === "offer_task")
        this.tasks.offer(
          world,
          action.parameters.task_id,
          p.actor,
          action.target,
          p.context.interaction.source_event_id,
          info.task_followup,
        );
      if (action.type === "give_task_reward")
        this.tasks.reward(
          world,
          action.parameters.task_id,
          p.actor,
          action.target,
        );
      actor.version++;
      actor.next_agenda_tick = w.tick + 30;
      this.save(w);
    });
    const conv =
      conversationId ||
      p.conversation ||
      action.parameters.conversation_id ||
      "";
    let event;
    if (output.dialogue)
      event = this.addEvent(world, {
        id: eventId,
        text: output.dialogue,
        kind: "claim",
        speaker: p.actor,
        recipient: mode === "player" ? "player" : action.target || "",
        visible: audience,
        disclose_to: audience,
        location: location || actor.scene,
        conversationId: conv,
        channel: action.parameters.channel || "private_player_chat",
        seq,
        hour,
        selected: p.context.character_state.memories,
      });
    if(event && p.actor==="z" && mode==="player" && output.info.day_song_reply){
      event.daySongReply=output.info.day_song_reply;
      event.daySongSource=p.context.interaction.source_event_id;
      this.store.put("event",event);
    }
    if(event && p.actor==="z" && mode==="player" && p.context.z_story){
      const current=this.world(world);
      const offered=p.context.z_story;
      const card=memoryProjection(current,this.records(current,"event"),{privateChat:true}).stories.find(s=>s.id===offered.id&&s.count===offered.count);
      rememberNarration(current,narrativeChange(card,output.dialogue),"private_player_chat",event.id);
      this.save(current);
    }
    const latest = this.world(world),
      a = latest.actors[p.actor];
    if (action.type === "communicate") {
      const cid = conv || "conv_" + p.id;
      let c = this.store.record(cid);
      if (!c || c.experimentId !== latest.id)
        c = {
          id: cid,
          experimentId: latest.id,
          epoch: latest.epoch,
          hour: latest.hour,
          status: "active",
          actors: [p.actor, action.target],
          lines: 0,
        };
      c.lines++;
      if (
        info.communication.intent === "end" ||
        info.communication.intent === "decline" ||
        c.lines >= this.rootBudget
      )
        c.status = "complete";
      this.store.put("conversation", c);
      if (c.status === "active" && event) {
        this.job(world, action.target, event.id, {
          mode: "npc_message",
          conversation: cid,
          channel: action.parameters.channel,
          root: p.root,
          hop: p.hop + 1,
        });
      }
      a.previous_result = {
        status: "succeeded",
        action_id: p.id,
        event_id: event?.id,
        summary: "发言实际呈现并送达。",
      };
    } else if (
      !["wait", "offer_task", "give_task_reward"].includes(action.type)
    ) {
      const id = "action_" + p.id;
      this.store.put("action", {
        id,
        experimentId: latest.id,
        epoch: latest.epoch,
        hour: latest.hour,
        actor: p.actor,
        type: action.type,
        target: action.target,
        parameters: action.parameters,
        status: "pending",
      });
      a.pending_action = id;
      a.previous_result = {
        status: "pending",
        action_id: id,
        summary: "等待实际动作回执。",
      };
    } else
      a.previous_result = {
        status: "succeeded",
        action_id: p.id,
        event_id: event?.id,
        summary:
          action.type === "wait"
            ? "保持当前状态，没有完成其他事项。"
            : "玩家任务提案或结果实际呈现。",
      };
    if (info.social_decision)
      this.store.put("social_decision", {
        id: uid("social"),
        experimentId: latest.id,
        epoch: latest.epoch,
        hour: latest.hour,
        actor: p.actor,
        payload: info.social_decision,
      });
    if (info.social_decision?.choice === "keep_to_self")
      this.cancel(world, { conversation: p.conversation });
    this.save(latest);
    p.status = "committed";
    p.eventId = event?.id;
    this.store.put("proposal", p);
    const completed = this.store.record(p.job);
    if (completed) {
      completed.status = "succeeded";
      this.store.put("job", completed);
    }
    return output;
  }
  actionReceipt(
    world,
    id,
    success,
    result = {},
    receiptId = id,
    eventMeta = {},
  ) {
    const w = this.world(world),
      action = this.store.record(id);
    if (!action || action.experimentId !== w.id || action.epoch !== w.epoch)
      throw new TurnError("未知动作");
    const payload = encode({ success, result });
    if (action.status !== "pending") {
      if (action.receiptPayload !== payload)
        throw new TurnError("动作回执冲突");
      return;
    }
    const a = w.actors[action.actor];
    if (a.pending_action !== id) throw new TurnError("动作已失效");
    if (success && action.type === "move") {
      if (
        result.scene !== action.target ||
        !(this.graph[a.scene] || []).includes(action.target)
      )
        throw new TurnError("移动回执不符合实际路线");
      a.scene = action.target;
    }
    a.pending_action = null;
    a.version++;
    this.save(w);
    action.status = success ? "succeeded" : "failed";
    action.receiptPayload = payload;
    action.receiptId = receiptId;
    this.store.put("action", action);
    const e = this.observe(
      world,
      result.summary || (success ? "动作已实际成功。" : "动作实际失败。"),
      [action.actor],
      { kind: "fact", wake: false, location: a.scene, ...eventMeta },
    );
    const latest = this.world(world);
    latest.actors[a.id].previous_result = {
      status: success ? "succeeded" : "failed",
      action_id: id,
      event_id: e.id,
      summary: e.text,
    };
    this.save(latest);
    this.job(world, a.id, e.id);
  }
  taskResponse(
    world,
    task,
    accept,
    owner = "player",
    responder = "player",
    eventMeta = {},
  ) {
    const d = this.tasks.definition(task);
    this.tasks.respond(world, task, accept, responder);
    this.focus(world, d.owner, owner);
    const e = this.addEvent(world, {
      text: `我${accept ? "接受" : "暂不接受"}这项委托：${d.title}`,
      speaker: "player",
      visible: [d.owner, "player"],
      recipient: d.owner,
      conversationId: "player_" + owner + "_" + d.owner,
      ...eventMeta,
    });
    if (!accept) this.tasks.decline(world, task, e.id);
    else if (
      [
        "map_blank",
        "map_trial",
        "map_packing",
        "dock_sort",
        "departure_prep",
        "two_friends_photo",
      ].includes(task)
    )
      this.tasks.invite(
        world,
        task,
        d.owner,
        e.id,
        "玩家已接受委托。你独立决定愿意和玩家一起做哪些部分。",
      );
    return this.job(world, d.owner, e.id, {
      mode: "player",
      conversation: e.conversationId,
    });
  }
  invite(
    world,
    task,
    actor,
    request,
    owner = "player",
    requester = "player",
    eventMeta = {},
  ) {
    if (
      requester !== "player" ||
      this.tasks.state(world, task).status !== "active"
    )
      throw new TurnError("只有玩家接受后可以邀请协作");
    const job = this.player(world, actor, request, owner, eventMeta);
    this.tasks.invite(world, task, actor, job.event, request, requester);
    return job;
  }
  taskReceipt(world, task, step, receipt, success, who, details = {}, eventMeta = {}) {
    const w = this.world(world);
    if (task === "departure_prep" && step === "notify" && success) {
      const e = this.event(world, details.message_event_id);
      if (!e?.visible.includes("z")) throw new TurnError("Z尚未实际收到消息");
    }
    const changed = this.tasks.record(
      world,
      task,
      step,
      receipt,
      success,
      who,
      details,
      Object.fromEntries(Object.values(w.actors).map((a) => [a.id, a.scene])),
    );
    if (!changed) {if(eventMeta.id)this.observe(world,"重复任务回执已确认，没有新进度。",[],{kind:"fact",wake:false,...eventMeta});return;}
    const owner = this.tasks.definition(task).owner;
    const event=this.observe(
      world,
      `游戏确认任务“${this.tasks.definition(task).title}”的步骤${step}${success ? "已完成" : "失败"}。`,
      [...new Set([owner, "player", who])],
      { kind: "fact", ...eventMeta },
    );
    // Only publish the ordinary exploration steps whose definition requires Z.
    // A substitute collaborator, a failed attempt or an invitation is not a joint outing.
    if (success && task==="map_blank" && ((step==="visit" && who==="player") || (step==="explain" && who==="z"))) {
      event.publicActivity=task+"."+step;
      this.store.put("event",event);
    }
  }
  snapshot(world) {
    const w = this.world(world);
    return {
      session: world,
      day: w.day,
      simulation_tick: w.tick,
      runId: w.runId,
      seq: w.seq,
      responseRevision: w.responseRevision,
      actors: Object.values(w.actors).map((a) => ({
        ...clone(a),
        identity: this.catalog.identity(a.id),
        talking_to_player: w.focus?.actor === a.id,
      })),
      history: Object.fromEntries(
        Object.keys(w.actors).map((id) => [
          id,
          this.records(w, "event")
            .filter((e) => e.visible.includes(id))
            .slice(-80)
            .map((e) => ({
              id: e.id,
              text: e.text,
              kind: e.kind,
              speaker: e.speaker,
            })),
        ]),
      ),
      tasks: this.tasks.list(world),
      actions: this.records(w, "action").filter((a) => a.status === "pending"),
      player_focus_actor: w.focus?.actor || null,
      social_decisions: Object.fromEntries(
        Object.keys(w.actors).map((id) => [
          id,
          this.records(w, "social_decision")
            .filter((s) => s.actor === id)
            .slice(-3)
            .map((s) => s.payload),
        ]),
      ),
      ready_jobs: this.ready(world).length,
      pending_jobs: this.records(w, "job").filter((j) => j.status === "pending")
        .length,
      memory_pending: this.records(w, "memory_job").filter(
        (j) => j.status !== "complete",
      ).length,
    };
  }
  checkpoint(world) {
    const w = this.world(world);
    const kinds = [
      "event",
      "message",
      "memory",
      "job",
      "action",
      "proposal",
      "conversation",
      "social_decision",
      "memory_job",
      "reflection_job",
    ];
    return {
      version: 2,
      worldId: world,
      contentRevision: this.catalog.registry.pack_version,
      broadcast: memoryProjection(w,this.records(w,"event")),
      world: clone(w),
      records: Object.fromEntries(kinds.map((k) => [k, this.records(w, k)])),
      tasks: this.tasks.snapshot(world),
    };
  }
  open(world, runId, checkpoint) {
    if (!runId || typeof runId !== "string")
      throw new TurnError("需要新的运行标识");
    let cp =
      typeof checkpoint === "string" && checkpoint
        ? JSON.parse(checkpoint)
        : checkpoint;
    if (
      cp &&
      (cp.version !== 2 ||
        cp.worldId !== world ||
        cp.contentRevision !== this.catalog.registry.pack_version)
    )
      throw new TurnError("旧身份版本需要明确迁移，不自动覆盖当前人设");
    let w;
    try {
      w = this.world(world);
    } catch {
      w = this.create(world);
    }
    this.cancel(world);
    const epoch = w.epoch + 1;
    if (cp) {
      const saved = cp.world;
      if (
        Object.keys(saved.actors).sort().join("|") !==
        [...this.catalog.actors.keys()].sort().join("|")
      )
        throw new TurnError("存档人物名单不是当前八位角色");
      if (
        Object.values(cp.records)
          .flat()
          .some((r) => (r.createdHour ?? r.hour ?? 0) > saved.hour)
      )
        throw new TurnError("存档包含未来记忆");
      w = { ...clone(saved), id: w.id, worldId: world };
      this.store.transaction(() => {
        this.store.clearRecords(w.id);
        this.tasks.restore(world, cp.tasks);
        for (const [kind, records] of Object.entries(cp.records)) {
          if (
            ![
              "event",
              "message",
              "memory",
              "job",
              "action",
              "proposal",
              "conversation",
              "social_decision",
              "memory_job",
              "reflection_job",
            ].includes(kind)
          )
            throw new TurnError("存档记录类型无效");
          for (const record of records) {
            const r = { ...clone(record), experimentId: w.id, epoch };
            if (kind === "proposal" && r.status === "pending")
              r.status = "restored_pending";
            if (kind === "job" && ["running", "proposed"].includes(r.status))
              r.status = "cancelled";
            if (kind === "memory_job" && r.status === "running")
              r.status = "pending";
            this.store.put(kind, r);
          }
        }
      });
    }
    if (!cp) {
      this.store.clearRecords(w.id);
      this.tasks.restore(world, {});
      w.actors = this.initialActors(w.initialMetrics || {});
      delete w.zNarrated;
      w.seq=0;w.hour=0;w.day=1;w.period=0;w.tick=0;w.reflected={};w.memoryRevision=0;
    }
    w.epoch = epoch;
    w.runId = runId;
    w.version++;
    w.active = true;
    w.focus = null;
    w.encounters = {};
    w.wakes = {};
    w.responseRevision = 0;
    this.save(w);
    return this.checkpoint(world);
  }
  async plan(world, actor, candidates, environment = {}) {
    const w = this.world(world);
    if (this.held(w, actor)) throw new TurnError("角色已在会话中");
    if (candidates.some((id) => !this.catalog.actors.has(id) || id === actor))
      throw new TurnError("交谈候选无效");
    const e = {
      id: uid("perception"),
      experimentId: w.id,
      epoch: w.epoch,
      hour: w.hour,
      kind: "encounter",
      speaker: "engine",
      text:
        "宿主确认此刻可见这些人，可决定是否走近交谈：" +
        candidates.map((id) => this.catalog.actor(id).name).join("、"),
      visible: [actor],
      disclose_to: [actor],
    };
    this.store.put("perception_context", e);
    const j = this.job(world, actor, e.id, {
      conversation: environment.conversation_id || "",
      environment: {
        ...environment,
        intent_only: true,
        meeting_candidates: candidates,
        present_characters: [actor, ...candidates],
      },
    });
    j.status = "running";
    this.store.put("job", j);
    const c = this.context(j);
    try {
      const o = await this.decide(c);
      this.policy.validate(c, o);
      if (
        this.world(world).runId !== w.runId ||
        this.world(world).actors[actor].version !== c.character_state.version ||
        this.store.record(j.id).status === "cancelled"
      )
        throw new TurnError("意向请求已取消或过期");
      if (o.dialogue) throw new TurnError("意向阶段尚未交谈，不能发台词");
      j.status = "complete";
      return {
        actorId: actor,
        targetId: o.action.type === "schedule_meeting" ? o.action.target : "",
        reason: o.action.description,
        topic: o.action.description,
      };
    } catch(error) {
      j.status = "failed";
      j.error = error instanceof TurnError ? error.message : "意向生成失败";
      throw error;
    } finally {
      this.store.put("job", j);
    }
  }
}
