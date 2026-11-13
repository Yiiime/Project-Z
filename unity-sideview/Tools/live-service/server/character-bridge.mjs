// Server-only adapter. Binding credentials are local game-session capabilities,
// never model API keys. An attached room cannot fall back to another persona.
export class CharacterBridge {
  constructor(binding, { fetchImpl = fetch, pending = [], publications = [], onChanged = () => {} } = {}) {
    const url = new URL(binding.memoryUrl);
    if (url.protocol !== "http:" || !["127.0.0.1", "localhost"].includes(url.hostname) || url.username || url.password || url.pathname !== "/") throw Error("invalid_character_service");
    for (const k of ["worldId", "runId", "roomId", "roomToken"]) if (typeof binding[k] !== "string" || !/^[a-zA-Z0-9_-]{1,100}$/.test(binding[k])) throw Error("invalid_character_binding");
    this.binding = { ...binding, memoryUrl: url.origin }; this.fetch = fetchImpl; this.pending = pending; this.onChanged = onChanged; this.publications = new Map(publications); this.sending = null;
  }
  bindPublication(eventId, proposalId, actorId, text) {
    if (!proposalId) return;
    this.publications.set(eventId, { id: proposalId, actorId, text });
    if (this.publications.size > 64) this.publications.delete(this.publications.keys().next().value);
    this.onChanged();
  }
  async post(operation, data = {}) {
    const { memoryUrl, ...scope } = this.binding;
    const response = await this.fetch(memoryUrl + "/api/game/live/" + operation, { method: "POST", headers: { "Content-Type": "application/json", "X-Z-Memory": "1" }, body: JSON.stringify({ ...scope, ...data }), signal: AbortSignal.timeout(8000) });
    const result = await response.json(); if (!response.ok || !result.ok) throw Object.assign(Error("character_sync_failed"),{reason:result.error}); return result;
  }
  record(event, receipt = null) {
    if (!event?.id || !event.text) return;
    if(receipt?.speaker==="engine"){
      const id=event.id+":engine_receipt";
      if(!this.pending.some(p=>p.event.id===id))this.pending.push({event:{id,actorId:"engine",text:receipt.text,stage:"message",verified:true}});
      this.onChanged();void this.flush().catch(()=>{});return;
    }
    if(!["player.message","npc.message","speech.progress","speech.completed","speech.interrupted"].includes(event.type))return;
    const actorId = event.speakerId === "Z" || event.type.startsWith("speech.") ? "z" : event.speakerRole === "player" ? "player" : event.speakerId;
    if (!actorId) return;
    const bound = this.publications.get(event.id);
    const proposal = event.type !== "speech.progress" && bound?.actorId === actorId && bound.text === event.text ? bound : null;
    const id = event.id + ":" + event.type + ":" + event.text.length;
    if (!this.pending.some(p => p.event.id === id)) this.pending.push({ event: { id, actorId, text: event.text, publicationId:event.speechId ? "speech-"+event.speechId : event.id, stage:event.type.split(".")[1],...(receipt?.change?{change:receipt.change}:{}),...(receipt?.shared?{shared:true}:{}) }, ...(proposal ? { proposalId: proposal.id } : {}) });
    if (event.type !== "speech.progress") this.publications.delete(event.id);
    this.onChanged();
    void this.flush().catch(() => {}); // Durable queue is retried before the next turn/exit.
  }
  async flush() {
    if (this.sending) return this.sending;
    this.sending = (async () => { while (this.pending.length) { await this.post("observe", this.pending[0]); this.pending.shift(); this.onChanged(); } })();
    try { await this.sending; } finally { this.sending = null; }
  }
  async prepare(value) {
    await this.flush();
    const actorId = value.kind === "host" ? "z" : value.context.audience?.eligibleNpcIds?.[0];
    if (!actorId) throw Error("missing_live_actor");
    const prepared=await this.post("prepare", { actorId });prepared.publicEventIds=value.context.publicEvents.map(e=>e.id);return prepared;
  }
  wrap(envelope, prepared, kind) {
    const host = kind === "host";
    envelope.system += "\n【统一人物接口，替代上文最终JSON包装】你就是下面人物资料中的当前角色；不要扮演其他人。只输出dialogue/action/info三键。dialogue是纯台词。" +
      (host ? "action保存上文主持协议中除speech和memory外的字段（action、mood、gesture、transition、handledIds、wakeAfterMs、continuation、agendaUpdates、tool等）；speech放到dialogue，省略memory。" : "action为{type:send_danmaku或wait,intent:reaction/atmosphere/advance,evidence_ids:[本次公开依据ID]}。发言时 action 同时带 actorId、delivery、expression，遵循上文逐角色表达协议。不发弹幕时dialogue为空、type为wait；你仍在房间里感知消息，不能退出或替其他人发言。") +
      "info遵守下方契约，只给本人状态变化；不重复数值快照，不创建世界事实、任务完成或记忆。所有岛民必须在线，但可以选择暂不说话。输出前考虑实际公开听众，不把私聊秘密直接讲给全场。人物与当前时间地点以character_context为准，旧开场设定不覆盖本局经历。\n人物Info契约：" + JSON.stringify(prepared.infoSchema) +
      "\n可选字段无变化时完全省略，不填false、null占位。不知道今天做过什么时，说平时做什么或现在想做什么；没有执行记录不能把职业日常编成今天已经完成的经历。台词里的称呼不会改变引擎日期和天气。action.evidence_ids只可使用这些公开消息ID："+JSON.stringify(prepared.publicEventIds)+"；不能用character_context中的记忆ID代替。Info的changes来源则必须用character_context.interaction.source_event_id。";
    envelope.system = prepared.prompt + "\n\n" + envelope.system;
    if (prepared.turnSchema) envelope.system += "\n本轮完整直播输出Schema（后台按同一份校验）：" + JSON.stringify(prepared.turnSchema);
    envelope.input.character_context = prepared.context;
    if (envelope.input.world) envelope.input.world = { day: prepared.context.environment.day, time: prepared.context.environment.period, scene: "live_room" };
    return envelope;
  }
  decode(content, prepared, kind) {
    const turn = JSON.parse(content);
    if (Object.keys(turn).sort().join() !== "action,dialogue,info" || typeof turn.dialogue !== "string" || !turn.info || !turn.action) throw Error("invalid_character_turn");
    let native;
    if (kind === "host") native = { ...turn.action, speech: turn.dialogue, memory: "" };
    else if (turn.action.type === "wait" && turn.dialogue === "") native = { status: "skip" };
    else if (turn.action.type === "send_danmaku") native = { status: "ok", npc_id: prepared.actorId, intent: turn.action.intent, text: turn.dialogue, evidence_ids: turn.action.evidence_ids,actorId:turn.action.actorId,delivery:turn.action.delivery,expression:turn.action.expression };
    else throw Error("invalid_character_action");
    return { turn, native: JSON.stringify(native) };
  }
  async proposed(prepared, turn, parsed, kind) {
    const result = await this.post("propose", { contextId: prepared.contextId, turn });
    if (kind === "host") parsed.characterProposalId = result.proposalId;
    else if (parsed.items?.[0]) parsed.items[0].characterProposalId = result.proposalId;
  }
}
