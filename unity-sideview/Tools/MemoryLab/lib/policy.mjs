import Ajv2020 from "ajv/dist/2020.js";
import { clone } from "./catalog.mjs";
export class TurnError extends Error {}
const equal = (a, b) =>
  JSON.stringify(canonical(a)) === JSON.stringify(canonical(b));
export function canonical(v) {
  if (Array.isArray(v)) return v.map(canonical);
  if (v && typeof v === "object")
    return Object.fromEntries(
      Object.keys(v)
        .sort()
        .map((k) => [k, canonical(v[k])]),
    );
  return v;
}
export class Policy {
  constructor(catalog) {
    this.catalog = catalog;
    this.ajv = new Ajv2020({
      strict: false,
      allErrors: true,
      allowUnionTypes: true,
    });
    this.validators = new Map();
  }
  schema(actor, kind = "output") {
    const defs = clone(this.catalog.contract.$defs),
      p = defs.info.properties,
      ids = [...this.catalog.actors.keys()];
    if (actor === "z") {
      p.day_song_reply = {enum:["accept","discuss","decline"]};
      delete p.persuasion;
      delete p.support_willingness;
      delete p.changes.properties.persuasion;
    } else {
      defs.info.required.push("persuasion", "support_willingness");
      defs.info.allOf.push({
        if: {
          properties: { persuasion: { type: "null" } },
          required: ["persuasion"],
        },
        then: {
          required: ["missing_state"],
          properties: { missing_state: { contains: { const: "persuasion" } } },
        },
      });
    }
    if (actor === "zhou_bobo") {
      p.persuasion.maximum = 0;
      p.support_willingness.enum = ["unwilling"];
    }
    p.missing_state.items.enum = [
      "affinity",
      "trust",
      "world_suspicion",
      ...(actor === "z" ? [] : ["persuasion"]),
    ];
    p.peer_relation_changes.items.properties.target_actor_id.enum = ids.filter(
      (x) => x !== actor,
    );
    defs.input.properties.actor_id = { const: actor };
    defs.input.properties.interaction.allOf[0].then.properties.source_actor_id.enum =
      ids.filter((x) => x !== actor);
    defs.output.allOf[0].then.properties.action.properties.target.enum =
      ids.filter((x) => x !== actor);
    return {
      $schema: "https://json-schema.org/draft/2020-12/schema",
      $ref: "#/$defs/" + kind,
      $defs: defs,
    };
  }
  validator(actor, kind) {
    const key = actor + kind;
    if (!this.validators.has(key))
      this.validators.set(key, this.ajv.compile(this.schema(actor, kind)));
    return this.validators.get(key);
  }
  validateInput(c) {
    const v = this.validator(c.actor_id, "input");
    if (!v(c)) throw new TurnError("输入格式错误：" + v.errors[0].instancePath);
    const ids = c.available_actions.map((a) => a.id);
    if (
      new Set(ids).size !== ids.length ||
      !c.available_actions.some((a) => a.type === "wait")
    )
      throw new TurnError("合法动作目录无效");
  }
  sources(c) {
    const ids = new Set([c.interaction.source_event_id]);
    for (const bucket of ["known_facts", "memories", "received_claims"])
      for (const m of c.character_state[bucket] || []) {
        if (m.id) ids.add(m.id);
        for (const id of m.source_event_ids || []) ids.add(id);
      }
    if (c.previous_action_result?.event_id)
      ids.add(c.previous_action_result.event_id);
    return ids;
  }
  validateSources(ids, c, scoring = false) {
    const available = this.sources(c);
    if (
      !Array.isArray(ids) ||
      !ids.length ||
      ids.some((id) => !available.has(id))
    )
      throw new TurnError("引用了本角色未获得的来源");
    if (
      scoring &&
      ids.some((id) => c.character_state.applied_event_ids.includes(id))
    )
      throw new TurnError("不能对同一事件重复计分");
  }
  validate(c, o) {
    this.validateInput(c);
    const v = this.validator(c.actor_id, "output");
    if (!v(o)) throw new TurnError("输出格式错误：" + v.errors[0].instancePath);
    if (o.dialogue.length > (c.reply_budget?.max_dialogue_chars || 160))
      throw new TurnError("台词超出本轮字数预算");
    const action = o.action,
      info = o.info,
      mode = c.interaction.mode;
    const selected = c.available_actions.find(
      (a) =>
        a.id === action.action_id &&
        a.type === action.type &&
        a.target === action.target &&
        equal(a.parameters, action.parameters),
    );
    if (!selected) throw new TurnError("动作未匹配本轮合法选项");
    for (const box of [c.environment, c.plot_node])
      if (
        box?.available_actions &&
        !box.available_actions.some(
          (a) =>
            a.id === action.action_id &&
            a.type === action.type &&
            a.target === action.target &&
            equal(a.parameters, action.parameters),
        )
      )
        throw new TurnError("动作超出宿主约束");
    if (
      (mode === "player" || c.environment.player_conversation_active) &&
      action.type === "communicate"
    )
      throw new TurnError("玩家对话不能投递为NPC消息");
    if (mode === "npc_message" && o.dialogue && action.type !== "communicate")
      throw new TurnError("NPC发言必须有明确通信动作");
    if (
      c.actor_id === "zhou_bobo" &&
      selected.effect_class === "escape_assistance"
    )
      throw new TurnError("周伯伯不能提供逃离援助");
    if (["offer_task", "give_task_reward"].includes(action.type)) {
      const task = (c.tasks || []).find(
        (t) => t.id === action.parameters.task_id,
      );
      if (
        mode !== "player" ||
        action.target !== "player" ||
        !o.dialogue ||
        !task ||
        task.owner !== c.actor_id ||
        task.assignee !== "player"
      )
        throw new TurnError("正式任务只能由所有者向玩家发布");
      if (action.type === "offer_task") {
        if (!task.enabled || !["available", "declined"].includes(task.status))
          throw new TurnError("任务当前不能提出");
        if (
          task.status === "declined" &&
          (!info.task_followup ||
            info.task_followup.task_id !== task.id ||
            info.task_followup.choice !== "reoffer" ||
            task.decline_event_id === c.interaction.source_event_id)
        )
          throw new TurnError("重提需要新的玩家交流与明确后续判断");
      } else if (task.status !== "completed" || task.reward_claimed)
        throw new TurnError("任务结果尚不能领取");
    }
    const social = info.social_decision;
    if (c.environment.social_opportunity && !social)
      throw new TurnError("相遇需要社会决定");
    if (social) {
      if (mode === "player") throw new TurnError("玩家回合不做NPC社交判断");
      this.validateSources(social.source_event_ids, c);
      if (social.choice === "engage" && action.type !== "communicate")
        throw new TurnError("主动交流需要通信动作");
      if (
        social.choice === "keep_to_self" &&
        (o.dialogue || action.type === "communicate")
      )
        throw new TurnError("保持独处不能同时发言");
    }
    const requests = c.character_state.cooperation_requests || [],
      p = info.task_participation;
    if (requests.length && !p) throw new TurnError("必须独立回应协作邀请");
    if (p) {
      const invitation =
        requests.find((x) => x.task_id === p.task_id) ||
        (c.character_state.cooperation_state || []).find(
          (x) => x.task_id === p.task_id,
        );
      if (
        !invitation ||
        (requests.length && mode !== "player") ||
        (mode === "player" && !o.dialogue)
      )
        throw new TurnError("协作需要实际玩家邀请和回应");
      this.validateSources(p.source_event_ids, c);
      if (
        !p.source_event_ids.includes(c.interaction.source_event_id) ||
        p.step_ids.some((id) => !invitation.allowed_step_ids.includes(id))
      )
        throw new TurnError("协作来源或范围无效");
      if (p.choice !== "willing" && p.step_ids.length)
        throw new TurnError("拒绝或改期不能承诺步骤");
      if (
        c.actor_id === "zhou_bobo" &&
        invitation.effect_class === "escape_assistance" &&
        p.choice === "willing"
      )
        throw new TurnError("周伯伯不能参与逃离任务");
    }
    if (info.task_followup) {
      const f = info.task_followup,
        t = (c.tasks || []).find((t) => t.id === f.task_id);
      if (
        mode !== "player" ||
        !t ||
        t.owner !== c.actor_id ||
        t.status !== "declined"
      )
        throw new TurnError("只能回应本人的被拒绝任务");
      this.validateSources(f.source_event_ids, c);
      if (
        !f.source_event_ids.includes(c.interaction.source_event_id) ||
        (f.choice === "reoffer" &&
          (action.type !== "offer_task" || action.parameters.task_id !== t.id))
      )
        throw new TurnError("后续判断与实际动作不一致");
    }
    const previous = c.character_state.info,
      metrics = [
        "affinity",
        "trust",
        "world_suspicion",
        ...(c.actor_id === "z" ? [] : ["persuasion"]),
      ],
      changed = [];
    for (const k of metrics) {
      const before = previous[k],
        after = info[k],
        delta = after - before,
        entry = info.changes[k];
      if (before === null || after === null) {
        if (before !== after || entry) throw new TurnError("不能猜测缺失数值");
        continue;
      }
      const policy = c.character_state.metric_policy?.[k] || {};
      if (
        ((policy.source === "engine" ||
          (c.actor_id === "zhou_bobo" && k === "persuasion")) &&
          delta) ||
        delta < (policy.min_delta ?? -before) ||
        delta > (policy.max_delta ?? 100 - before)
      )
        throw new TurnError("数值变化违反宿主限制");
      if (delta) {
        if (!entry || entry.delta !== delta)
          throw new TurnError("数值快照和变化量不一致");
        this.validateSources(entry.source_event_ids, c, true);
        changed.push(k);
      } else if (entry) throw new TurnError("未变化的数值不能填写变化项");
    }
    if (!equal(Object.keys(info.changes).sort(), changed.sort()))
      throw new TurnError("包含额外数值变化");
    if (
      !equal(
        (info.missing_state || []).sort(),
        metrics.filter((k) => info[k] === null).sort(),
      )
    )
      throw new TurnError("缺失状态不一致");
    if (action.type === "communicate") {
      const comm = info.communication;
      this.validateSources(comm.source_event_ids, c);
      const allowed =
        c.character_state.disclosure_by_recipient?.[action.target] || [];
      if (
        comm.shared_claim_ids.some(
          (id) => !allowed.includes(id) || !this.sources(c).has(id),
        )
      )
        throw new TurnError("不允许向该对象转述这些内容");
      for(const id of comm.shared_claim_ids) {
        const claim=[...c.character_state.known_facts,...c.character_state.memories].find(m=>m.id===id);
        if((c.environment.actual_audience||[]).some(listener=>listener!=="player"&&!claim?.disclose_to?.includes(listener)))
          throw new TurnError("旁听者无权听取该信息");
      }
      const channel = action.parameters.channel;
      if (
        ["face_to_face", "whisper"].includes(channel) &&
        !c.environment.present_characters.includes(action.target)
      )
        throw new TurnError("目标不在实际可交流区域");
      if (
        !["face_to_face", "whisper"].includes(channel) &&
        !c.environment.allowed_communication_channels?.includes(channel)
      )
        throw new TurnError("宿主未开启远程通信");
    } else if (info.communication)
      throw new TurnError("没有通信动作不能添加通信信息");
    for (const p of info.peer_relation_changes || []) {
      const peer = c.character_state.peer_relations[p.target_actor_id];
      if (
        mode !== "npc_message" ||
        p.target_actor_id !== c.interaction.source_actor_id ||
        !peer ||
        peer.affinity == null ||
        peer.trust == null
      )
        throw new TurnError("NPC关系只能更新实际来信者");
      this.validateSources(p.source_event_ids, c, true);
      if (!p.source_event_ids.includes(c.interaction.source_event_id))
        throw new TurnError("NPC关系须引用实际来信");
      for (const k of ["affinity", "trust"])
        if (
          Math.abs(p[k + "_delta"]) >
            (c.character_state.peer_relation_policy?.max_abs_delta ?? 100) ||
          peer[k] + p[k + "_delta"] < 0 ||
          peer[k] + p[k + "_delta"] > 100
        )
          throw new TurnError("NPC关系超出范围");
    }
    if (info.agenda_update) {
      const a = info.agenda_update;
      if (
        !(c.character_state.allowed_goals || []).some(
          (g) => (g.id || g) === a.goal_id,
        ) ||
        (a.next_action_id &&
          !c.character_state.allowed_next_action_ids.includes(a.next_action_id))
      )
        throw new TurnError("议程不在当前合法目录");
      this.validateSources(a.source_event_ids, c);
    }
    return o;
  }
  outputContract(c) {
    const s = this.schema(c.actor_id),
      i = s.$defs.info,
      p = i.properties;
    p.agenda_update.properties.goal_id.enum = (c.character_state.allowed_goals || []).map(g=>g.id||g);
    p.agenda_update.properties.next_action_id.enum = [null, ...(c.character_state.allowed_next_action_ids || [])];
    if (c.environment.intent_only) {
      s.$defs.output.properties.dialogue = { const: "" };
      p.social_decision = false;
      p.changes.const = {};
    }
    if (c.interaction.mode === "player") p.social_decision = false;
    else if (
      c.environment.social_opportunity ||
      c.interaction.mode === "npc_message"
    )
      i.required.push("social_decision");
    if (c.character_state.cooperation_requests?.length)
      i.required.push("task_participation");
    else if (!c.character_state.cooperation_state?.length)
      p.task_participation = false;
    const declined = (c.tasks || []).filter((t) => t.status === "declined");
    if (!declined.length) p.task_followup = false;
    else if (
      declined.some((t) => t.decline_event_id === c.interaction.source_event_id)
    )
      i.required.push("task_followup");
    for (const k of ["affinity", "trust", "world_suspicion", "persuasion"])
      if (p[k])
        i.allOf.push({
          if: {
            properties: { [k]: { const: c.character_state.info[k] } },
            required: [k],
          },
          then: { properties: { changes: { properties: { [k]: false } } } },
          else: { properties: { changes: { required: [k] } } },
        });
    return {
      $ref: "#/$defs/output",
      $defs: { output: s.$defs.output, info: i },
    };
  }
}
