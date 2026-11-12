import { uid } from "./store.mjs";
import { TurnError } from "./policy.mjs";
import { ensureRelations } from "./relationships.mjs";

function diagnosticCopy(value) {
  if (Array.isArray(value)) return value.map(diagnosticCopy);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value)
    .filter(([key]) => !/^(api[-_]?key|authorization|cookie|password|secret|roomToken|token)$/i.test(key))
    .map(([key, child]) => [key, diagnosticCopy(child)]));
  return value;
}

// Standalone diagnostic host. It shares Engine/Policy/Memory with Unity,
// but never loads a Unity save or claims a physical action was completed.
export function createLab(engine) {
  const worlds = p => {
    const w = engine.world(p.worldId);
    if (!w.lab || !w.worldId.startsWith("lab_") || w.runId !== p.runId)
      throw new TurnError("这不是当前独立测试存档");
    if(w.host!=="unity"){w.host="unity";engine.save(w);}
    return w;
  };
  const inspect = (world, actorId = "z") => {
    const w = engine.world(world);
    for (const a of Object.values(w.actors)) ensureRelations(a, engine.catalog);
    engine.save(w);
    const records = Object.fromEntries(["event", "message", "memory", "job", "proposal", "action", "conversation", "social_decision", "memory_job", "reflection_job", "trace"].map(k => [k, engine.records(w,k)]));
    return diagnosticCopy({ ok:true, worldId:world, runId:w.runId, contentVersion:engine.catalog.registry.pack_version,
      exportVersion:1,
      backend:{world:w,taskCheckpoint:engine.tasks.snapshot(world)},
      exportNotice:"后台诊断快照，含完整任务底表与记录；访问凭据已移除，不用作可恢复存档。",
      state:engine.snapshot(world), records,
      actor:{ id:actorId, profile:engine.catalog.profile(actorId), memory:engine.memory.inspect(w,actorId) },
      initialRelationships:engine.catalog.registry.initial_relationships,
      observerView:true,
    });
  };
  return async (operation,p) => {
    if (operation === "new") {
      const world=uid("lab"), runId=uid("run");
      engine.open(world,runId,null,{initialSeed:world});
      const w=engine.world(world);w.lab=true;w.host="unity";engine.save(w);
      engine.focus(world,p.actorId || "z","lab");
      return inspect(world,p.actorId);
    }
    const w=worlds(p), actor=p.actorId || "z";
    engine.catalog.actor(actor);
    let job=null, results=[];
    if (operation === "inspect") return inspect(w.worldId,actor);
    if (operation === "focus") engine.focus(w.worldId,p.focusActorId || null,"lab");
    else if (operation === "turn") {
      if (typeof p.text !== "string" || !p.text.trim() || p.text.length>4000) throw new TurnError("请输入不超过4000字的对话");
      job=engine.player(w.worldId,actor,p.text.trim(),"lab");
    } else if (operation === "place") {
      if (engine.held(w,actor)) throw new TurnError("请先结束该角色的交谈");
      engine.place(w.worldId,actor,p.locationId);
    } else if (operation === "encounter") {
      engine.catalog.actor(p.targetId);
      if (actor===p.targetId || engine.held(w,actor) || engine.held(w,p.targetId) ||
          w.actors[actor].pending_action || w.actors[p.targetId].pending_action ||
          w.actors[actor].scene!==w.actors[p.targetId].scene)
        throw new TurnError("双方须在同地点、空闲且没有待执行行动；先结束玩家交谈");
      const conversation=uid("lab_encounter");
      const e=engine.observe(w.worldId,`${engine.catalog.actor(actor).name}和${engine.catalog.actor(p.targetId).name}在${engine.catalog.place(w.actors[actor].scene).name}同场，可以打招呼，也可以各做各的。`,[actor,p.targetId],{kind:"encounter",wake:false,conversationId:conversation});
      job=engine.job(w.worldId,actor,e.id,{conversation,environment:{present_characters:[actor,p.targetId],actual_audience:[actor,p.targetId],social_opportunity:{kind:"co_location",nearby_actors:[p.targetId]}}});
    } else if (operation === "continue") {
      job=engine.ready(w.worldId).find(j=>j.mode==="npc_message" && j.conversation===p.conversationId);
      if (!job) throw new TurnError("当前NPC交流已结束，或参与者正在与玩家交谈");
    } else if (operation === "event") {
      if (typeof p.text!=="string" || !p.text.trim() || p.text.length>4000 || !Array.isArray(p.visible) || !p.visible.length) throw new TurnError("请填写消息和知情角色");
      engine.observe(w.worldId,p.text,p.visible,{kind:p.verified===true?"fact":"observation",wake:false,disclose_to:p.visible});
    } else if (operation === "task/respond") {
      if(typeof p.accepted!=="boolean") throw new TurnError("需要明确接受或拒绝");
      job=engine.taskResponse(w.worldId,p.taskId,p.accepted,"lab");
    } else if (operation === "task/invite") {
      job=engine.invite(w.worldId,p.taskId,actor,p.text || "愿意和我一起做这件事吗？","lab");
    } else if (operation === "commit") {
      const proposal=engine.store.record(p.proposalId);
      if(!proposal || proposal.world!==w.worldId || proposal.runId!==w.runId) throw new TurnError("提案不属于当前测试");
      // The page acknowledges only the exact line that it rendered.
      if (p.dialogue!==proposal.output.dialogue) throw new TurnError("实际显示的台词与提案不一致");
      engine.commit(w.worldId,p.proposalId);
    } else if (operation === "cancel") engine.cancel(w.worldId,{actor});
    else throw new TurnError("测试接口不存在");
    if(job) results=await engine.run(w.worldId,1,job.id);
    return {...inspect(w.worldId,job?.actor || actor),results};
  };
}
