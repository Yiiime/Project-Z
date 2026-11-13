import { randomBytes, createHash } from "node:crypto";
import { clone } from "./catalog.mjs";
import { TurnError, canonical } from "./policy.mjs";
import { readFileSync } from "node:fs";
import {portraitExpressionKeys} from "./portrait-expression.mjs";
import {memoryProjection,narrativeChange,rememberNarration} from "./live-memory.mjs";
const liveContract = JSON.parse(readFileSync(new URL("./live-contract.schema.json", import.meta.url)));
const hash = x => createHash("sha256").update(JSON.stringify(canonical(x))).digest("hex");

// The room is a transport surface of this world, never another character store.
export class LiveRoom {
  constructor(engine) {
    this.engine = engine; this.store = engine.store;
    this.validEvent = engine.policy.ajv.compile({ ...clone(liveContract), $ref: "#/$defs/LivePublishedEvent" });
  }
  turnSchema(actor, infoSchema) {
    const contract=clone(liveContract);
    for(const name of ["LiveHostAction","LiveAudienceAction"]){for(const shape of contract.$defs[name].oneOf||[contract.$defs[name]])Object.assign(shape.properties,{actorId:{const:actor},delivery:{type:"string",minLength:1,maxLength:160},expression:{enum:portraitExpressionKeys(actor)}});}
    return { ...contract, type: "object", additionalProperties: false,
      required: ["dialogue", "action", "info"], properties: {
        dialogue: { type: "string", maxLength: actor === "z" ? 360 : 36 },
        action: { $ref: "#/$defs/" + (actor === "z" ? "LiveHostAction" : "LiveAudienceAction") },
        info: clone(infoSchema),
      }, allOf: [{ if: { properties: { action: { properties: { [actor === "z" ? "action" : "type"]: { const: "wait" } }, required: [actor === "z" ? "action" : "type"] } } }, then: { properties: { dialogue: { const: "" } } } }],
    };
  }
  check(p) {
    const w = this.engine.world(p.worldId), room = w.liveRoom;
    if (!w.active || w.runId !== p.runId || !room || room.id !== p.roomId || room.token !== p.roomToken)
      throw new TurnError("直播不属于当前运行进度");
    return { w, room };
  }
  open(p) {
    let w = this.engine.world(p.worldId);
    if (w.runId !== p.runId || !w.active || w.focus) throw new TurnError("结束当前交谈后再进入直播");
    if (w.liveRoom) return this.binding(w);
    this.engine.cancel(p.worldId);
    w = this.engine.world(p.worldId);
    w.liveRoom = { id: "room_" + randomBytes(12).toString("hex"), token: randomBytes(24).toString("hex"), actors: [...this.engine.catalog.actors.keys()].filter(id=>w.firstDay?.phase!=="EveningLive"||id!=="liyuan") };
    this.engine.save(w);
    this.engine.addEvent(p.worldId, { id: w.liveRoom.id + "_opened", text: "Z的直播开始，玩家和本场在场岛民进入直播间。岛民通过弹幕交流；没有任何岛外秘密因此自动公开。", kind: "fact", speaker: "engine", visible: [...w.liveRoom.actors, "player"], channel: "livestream", conversationId: w.liveRoom.id });
    return this.binding(this.engine.world(p.worldId));
  }
  binding(w) { return { worldId: w.worldId, runId: w.runId, roomId: w.liveRoom.id, roomToken: w.liveRoom.token, actors: w.liveRoom.actors }; }
  projection(p) { const {w}=this.check(p);return memoryProjection(w,this.engine.records(w,"event")); }
  close(p) {
    const w = this.engine.world(p.worldId);
    if (w.runId !== p.runId) throw new TurnError("直播已不属于当前存档");
    if (!w.liveRoom) return;
    if (p.roomId !== w.liveRoom.id) throw new TurnError("直播房间不匹配");
    if (p.roomToken !== w.liveRoom.token) throw new TurnError("直播凭据不匹配");
    for (const event of Object.values(w.liveRoom.partial || {}))
      this.observe({ ...p, event: { ...event, stage: "interrupted" } });
    this.engine.cancel(p.worldId, { conversation: w.liveRoom.id });
    const current = this.engine.world(p.worldId); current.liveRoom = null; this.engine.save(current);
  }
  prepare(p) {
    const { w, room } = this.check(p), actor = p.actorId;
    if (!room.actors.includes(actor)) throw new TurnError("当前角色不在直播间");
    const latest = this.engine.records(w, "event").filter(e => e.conversationId === room.id && e.visible.includes(actor)).at(-1);
    const job = this.engine.job(p.worldId, actor, latest.id, { mode: "livestream", conversation: room.id, channel: "livestream", environment: {
      live_room: { id: room.id, mandatory_participants: room.actors, role: actor === "z" ? "host" : "audience" },
      present_characters: [...room.actors, "player"], actual_audience: [...room.actors, "player"], reachable_locations: [],
      available_actions: [{ id: "wait_here", type: "wait", target: null, parameters: {} }],
    }});
    // A published stream is known to the room. Only this actor's private memory
    // is loaded; private memory does not become shared by room membership.
    const context = this.engine.context(job);
    context.environment.live_room.heard_in_progress = Object.values(room.partial || {});
    context.reply_budget.max_dialogue_chars = actor === "z" ? 360 : 36;
    const schema = this.engine.policy.modelContract(context);
    for (const k of ["communication", "social_decision", "agenda_update", "task_participation", "task_followup"]) delete schema.$defs.info.properties[k];
    job.status = "live_prepared"; job.context = context; job.infoSchema = schema.$defs.info; this.engine.record("job", job);
    this.engine.runs.append(w, actor, "decision.requested", job.id, { context });
    return { contextId: job.id, actorId: actor, prompt: this.engine.catalog.prompt(actor), context, infoSchema: schema.$defs.info, turnSchema: this.turnSchema(actor, schema.$defs.info), contentVersion: this.engine.catalog.registry.pack_version };
  }
  propose(p) {
    const { w, room } = this.check(p), job = this.store.record(p.contextId);
    if (!job || job.experimentId !== w.id || job.epoch !== w.epoch || job.conversation !== room.id || job.status !== "live_prepared") throw new TurnError("直播角色回合失效");
    const context = job.context, raw = p.turn;
    if (!this.engine.policy.ajv.compile(this.turnSchema(job.actor, job.infoSchema))(raw))
      throw new TurnError("直播三部分输出或动作字段不完整");
    if (!raw || Object.keys(raw).sort().join() !== "action,dialogue,info" || typeof raw.dialogue !== "string") throw new TurnError("直播只接收三部分角色输出");
    const validInfo = this.engine.policy.ajv.compile(job.infoSchema);
    if (!validInfo(raw.info)) throw new TurnError("直播Info格式不符");
    for (const k of ["communication", "social_decision", "agenda_update", "task_participation", "task_followup"]) if (Object.hasOwn(raw.info, k)) throw new TurnError("直播不能执行岛上动作");
    if (!raw.action || (job.actor === "z" ? !["speak", "wait", "tool"].includes(raw.action.action) : !["send_danmaku", "wait"].includes(raw.action.type))) throw new TurnError("直播行动无效");
    const output = { dialogue: raw.dialogue, action: { action_id: "wait_here", type: "wait", target: null, parameters: {}, description: "直播呈现适配" }, info: this.engine.policy.expandInfo(context, raw.info) };
    this.engine.policy.validate(context, output);
    this.engine.runs.append(w, job.actor, "decision.completed", job.id, { output: raw });
    const proposal = { id: "proposal_" + job.id, experimentId: w.id, epoch: w.epoch, hour: w.hour, runId: w.runId, world: p.worldId, actor: job.actor, status: "pending", context, output, liveTurn: clone(raw), job: job.id, conversation: room.id, root: job.event, hop: 0 };
    this.engine.record("proposal", proposal); job.status = "proposed"; this.engine.record("job", job);
    // A deliberate wait has no external delivery to confirm.
    if (!output.dialogue) this.commit(proposal, [...room.actors, "player"]);
    return { proposalId: proposal.id };
  }
  commit(proposal, audience, eventId) {
    const current = this.engine.world(proposal.world).actors[proposal.actor];
    if (hash(current.info) !== hash(proposal.context.character_state.info) || hash(current.peer_relations) !== hash(proposal.context.character_state.peer_relations)) throw new TurnError("直播决定期间本人状态已改变");
    // Mere reception of another public message changes the observation version,
    // not the held decision's numeric base. Never overwrite newer state values.
    proposal.context.character_state.version = current.version;
    this.engine.record("proposal", proposal);
    return this.engine.commit(proposal.world, proposal.id, { visible: audience, eventId, conversationId: proposal.conversation });
  }
  observe(p) {
    // Validation, proposal commit, observation and publication terminal are one
    // transaction. A rejected completion must preserve the previous prefix.
    let failedProposal = null;
    try {
      return this.store.transaction(() => this.observeInside(p, (proposal, error) => {
        failedProposal = { id: proposal.id, reason: error.message };
      }));
    } catch (error) {
      if (!failedProposal) throw error;
      // A nested commit shares this transaction. Let it fully roll back before
      // recording the actually heard speech without the rejected proposal's Info.
      return this.store.transaction(() => {
        this.check(p);
        const proposal = this.store.record(failedProposal.id);
        proposal.status = 'cancelled'; proposal.cancelReason = failedProposal.reason;
        this.engine.record('proposal', proposal);
        return this.observeInside({ ...p, proposalId: null });
      });
    }
  }
  observeInside(p, onCommitFailure = () => {}) {
    const { w, room } = this.check(p); let event = p.event;
    if (!this.validEvent(event)) throw new TurnError("直播感知事件字段或stage无效");
    if (!event.id || event.id.length > 200 || !event.text || event.text.length > 2000 || ![...room.actors, "player", "engine"].includes(event.actorId)) throw new TurnError("弹幕感知事件无效");
    const key = event.publicationId || event.id, stage = event.stage || "message";
    room.partial ||= {}; room.publications ||= {};
    const terminal = room.publications[key], old = room.partial[key];
    if (terminal) {
      if (terminal.actorId !== event.actorId) throw new TurnError("同一播放的角色不同");
      if (stage === "progress") {
        if (!terminal.text.startsWith(event.text)) throw new TurnError("结束后的进度与已播内容冲突");
        return { ignored: true, eventId: terminal.eventId };
      }
      if (stage === "interrupted") return { duplicate: true, eventId: terminal.eventId };
      if (terminal.stage !== stage || terminal.text !== event.text) throw new TurnError("已结束播放的内容冲突");
      return { duplicate: true, eventId: terminal.eventId };
    }
    if (old && old.actorId !== event.actorId) throw new TurnError("同一播放的角色不同");
    if (stage === "progress") {
      if (event.actorId !== "z" || old && !event.text.startsWith(old.text)) throw new TurnError("播出台词不能倒退");
      room.partial[key] = { ...event, id: key }; this.engine.save(w); return { partial: true };
    }
    if (stage === "interrupted") {
      if (!old) return { ignored: true };
      event = { ...old, id: key + ":interrupted", stage: "interrupted" };
    } else if (old && (stage !== "completed" || !event.text.startsWith(old.text))) {
      throw new TurnError("完成台词必须包含实际已播放的完整前缀");
    }
    const eventId = "live_" + hash([w.worldId, room.id, event.id]).slice(0,40), prior = this.engine.event(p.worldId, eventId);
    if (prior && (prior.text !== event.text || prior.speaker !== event.actorId)) throw new TurnError("弹幕事件内容冲突");
    const audience = [...room.actors, "player"], proposal = p.proposalId ? this.store.record(p.proposalId) : null;
    let committed = false;
    if (!prior && proposal?.status === "pending" && proposal.world === p.worldId && proposal.conversation === room.id && proposal.actor === event.actorId && proposal.output.dialogue === event.text) {
      // Only a complete, actually published line can apply the proposed Info.
      if (stage !== "interrupted") {
        try { this.commit(proposal, audience, eventId); committed = true; }
        catch (error) { onCommitFailure(proposal, error); throw error; }
      }
    }
    if (!prior && !committed) this.engine.addEvent(p.worldId, { id: eventId, text: event.text, speaker: event.actorId, kind: event.actorId === "engine" ? (event.verified===true?"fact":"observation") : "claim",
      ...(event.actorId==="z"&&event.shared===true?{subject:"player_and_z"}:{}), visible: audience, channel: "livestream", conversationId: room.id });
    const current = this.engine.world(p.worldId);
    if(event.change&&event.actorId==="z"){
      const card=memoryProjection(current,this.engine.records(current,"event")).stories.find(s=>s.id===event.change.id);
      const change=narrativeChange(card,event.text);
      if(change&&change.step===event.change.step)rememberNarration(current,change,"livestream",eventId);
    }
    delete current.liveRoom.partial?.[key];
    current.liveRoom.publications ||= {};
    current.liveRoom.publications[key] = { actorId: event.actorId, text: event.text, stage, eventId };
    this.engine.save(current);
    return prior ? { duplicate: true, eventId } : { committed, eventId };
  }
}
