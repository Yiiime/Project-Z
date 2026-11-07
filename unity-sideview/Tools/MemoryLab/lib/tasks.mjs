import { clone } from "./catalog.mjs";
import { TurnError, canonical } from "./policy.mjs";
const encode = (x) => JSON.stringify(canonical(x));
export class TaskEngine {
  constructor(store, catalog, event) {
    this.db = store.db;
    this.catalog = catalog;
    this.event = event;
    this.definitions = new Map(catalog.tasks.map((t) => [t.id, t]));
    if (catalog.tasks.some((t) => t.assignee !== "player"))
      throw new TurnError("任务只能给玩家");
    this.db.exec(
      `CREATE TABLE IF NOT EXISTS task_state(session TEXT,task TEXT,status TEXT,enabled INTEGER,progress TEXT,reward_claimed INTEGER DEFAULT 0,PRIMARY KEY(session,task));CREATE TABLE IF NOT EXISTS task_receipts(session TEXT,receipt TEXT,payload TEXT,PRIMARY KEY(session,receipt));CREATE TABLE IF NOT EXISTS task_followups(session TEXT,task TEXT,decline_event TEXT,decision TEXT,PRIMARY KEY(session,task));CREATE TABLE IF NOT EXISTS task_collaboration(session TEXT,task TEXT,actor TEXT,status TEXT,invite_event TEXT,request TEXT,decision TEXT,PRIMARY KEY(session,task,actor));`,
    );
  }
  definition(task) {
    const d = this.definitions.get(task);
    if (!d) throw new TurnError("未知委托");
    return d;
  }
  state(world, task) {
    const d = this.definition(task);
    this.db
      .prepare("INSERT OR IGNORE INTO task_state VALUES(?,?,?,?,?,0)")
      .run(world, task, "available", Number(d.enabled_on_start), "{}");
    return this.db
      .prepare("SELECT * FROM task_state WHERE session=? AND task=?")
      .get(world, task);
  }
  collaborators(world, task) {
    return this.db
      .prepare("SELECT * FROM task_collaboration WHERE session=? AND task=?")
      .all(world, task)
      .map((r) => ({
        actor_id: r.actor,
        status: r.status,
        decision: r.decision ? JSON.parse(r.decision) : null,
      }));
  }
  list(world, owner) {
    return [...this.definitions.values()]
      .filter((d) => !owner || d.owner === owner)
      .map((d) => {
        const row = this.state(world, d.id),
          f = this.db
            .prepare("SELECT * FROM task_followups WHERE session=? AND task=?")
            .get(world, d.id);
        return {
          ...clone(d),
          status: row.status,
          enabled: Boolean(row.enabled),
          progress: JSON.parse(row.progress),
          reward_claimed: Boolean(row.reward_claimed),
          decline_event_id: f?.decline_event || null,
          followup: f?.decision ? JSON.parse(f.decision) : null,
          collaborators: this.collaborators(world, d.id),
        };
      });
  }
  canReoffer(world, task, id) {
    const f = this.db
        .prepare("SELECT * FROM task_followups WHERE session=? AND task=?")
        .get(world, task),
      event = this.event(world, id);
    return Boolean(
      f &&
        id &&
        f.decline_event !== id &&
        event?.speaker === "player" &&
        event.visible.includes(this.definition(task).owner),
    );
  }
  options(world, actor, id) {
    return this.list(world, actor).flatMap((t) => [
      ...(t.enabled &&
      (t.status === "available" ||
        (t.status === "declined" && this.canReoffer(world, t.id, id)))
        ? [
            {
              id: "offer_" + t.id,
              type: "offer_task",
              target: "player",
              parameters: { task_id: t.id },
            },
          ]
        : []),
      ...(t.status === "completed" && !t.reward_claimed
        ? [
            {
              id: "reward_" + t.id,
              type: "give_task_reward",
              target: "player",
              parameters: { task_id: t.id },
            },
          ]
        : []),
    ]);
  }
  offer(world, task, actor, recipient = "player", trigger, followup) {
    const d = this.definition(task),
      s = this.state(world, task);
    if (
      recipient !== "player" ||
      d.owner !== actor ||
      !s.enabled ||
      !["available", "declined"].includes(s.status)
    )
      throw new TurnError("当前不能发布此玩家任务");
    if (
      s.status === "declined" &&
      (!followup ||
        followup.choice !== "reoffer" ||
        !this.canReoffer(world, task, trigger))
    )
      throw new TurnError("重提需要新的玩家交流和明确判断");
    this.db
      .prepare("UPDATE task_state SET status=? WHERE session=? AND task=?")
      .run("offered", world, task);
  }
  respond(world, task, accept, responder = "player") {
    if (
      responder !== "player" ||
      typeof accept !== "boolean" ||
      this.state(world, task).status !== "offered"
    )
      throw new TurnError("只有玩家能回应已发布的任务");
    this.db
      .prepare("UPDATE task_state SET status=? WHERE session=? AND task=?")
      .run(accept ? "active" : "declined", world, task);
  }
  enable(world, task) {
    this.state(world, task);
    this.db
      .prepare("UPDATE task_state SET enabled=1 WHERE session=? AND task=?")
      .run(world, task);
  }
  decline(world, task, event) {
    this.db
      .prepare(
        "INSERT INTO task_followups VALUES(?,?,?,NULL) ON CONFLICT(session,task) DO UPDATE SET decline_event=excluded.decline_event,decision=NULL",
      )
      .run(world, task, event);
  }
  followup(world, actor, decision) {
    if (
      this.definition(decision.task_id).owner !== actor ||
      this.state(world, decision.task_id).status !== "declined"
    )
      throw new TurnError("只能判断自己的被拒任务");
    this.db
      .prepare(
        "UPDATE task_followups SET decision=? WHERE session=? AND task=?",
      )
      .run(JSON.stringify(decision), world, decision.task_id);
  }
  allowed(task, actor) {
    return this.definition(task)
      .steps.filter(
        (s) =>
          (s.performed_by.length === 1 && s.performed_by[0] === "player") ||
          s.performed_by.includes(actor),
      )
      .map((s) => s.id);
  }
  invite(world, task, actor, event, request, requester = "player") {
    if (requester !== "player" || this.state(world, task).status !== "active")
      throw new TurnError("玩家先接受任务才能邀请协作");
    this.catalog.actor(actor);
    this.db
      .prepare(
        "INSERT INTO task_collaboration VALUES(?,?,?,'invited',?,?,NULL) ON CONFLICT(session,task,actor) DO UPDATE SET status='invited',invite_event=excluded.invite_event,request=excluded.request,decision=NULL",
      )
      .run(world, task, actor, event, request);
  }
  requests(world, actor, event) {
    return this.db
      .prepare(
        "SELECT * FROM task_collaboration WHERE session=? AND actor=? AND status='invited' AND invite_event=?",
      )
      .all(world, actor, event)
      .filter((r) => this.state(world, r.task).status === "active")
      .map((r) => {
        const d = this.definition(r.task);
        return {
          task_id: d.id,
          title: d.title,
          request: r.request,
          source_event_id: r.invite_event,
          effect_class: d.effect_class,
          allowed_step_ids: this.allowed(d.id, actor),
          step_labels: Object.fromEntries(
            d.steps
              .filter((s) => this.allowed(d.id, actor).includes(s.id))
              .map((s) => [s.id, s.label]),
          ),
        };
      });
  }
  cooperation(world, actor) {
    return this.db
      .prepare("SELECT * FROM task_collaboration WHERE session=? AND actor=?")
      .all(world, actor)
      .filter((r) => this.state(world, r.task).status === "active")
      .map((r) => {
        const d = this.definition(r.task);
        return {
          task_id: d.id,
          title: d.title,
          status: r.status,
          source_event_id: r.invite_event,
          effect_class: d.effect_class,
          allowed_step_ids: this.allowed(d.id, actor),
          decision: r.decision ? JSON.parse(r.decision) : null,
        };
      });
  }
  decide(world, actor, p) {
    const row = this.db
      .prepare(
        "SELECT * FROM task_collaboration WHERE session=? AND task=? AND actor=?",
      )
      .get(world, p.task_id, actor);
    if (
      !row ||
      this.state(world, p.task_id).status !== "active" ||
      p.step_ids.some((s) => !this.allowed(p.task_id, actor).includes(s)) ||
      (p.choice !== "willing" && p.step_ids.length) ||
      (actor === "zhou_bobo" &&
        this.definition(p.task_id).effect_class === "escape_assistance" &&
        p.choice === "willing")
    )
      throw new TurnError("协作决定超出真实邀请或角色范围");
    this.db
      .prepare(
        "UPDATE task_collaboration SET status=?,decision=? WHERE session=? AND task=? AND actor=?",
      )
      .run(p.choice, JSON.stringify(p), world, p.task_id, actor);
  }
  record(world, task, stepId, receipt, success, who, details, positions) {
    const d = this.definition(task),
      state = this.state(world, task),
      step = d.steps.find((s) => s.id === stepId);
    if (
      !step ||
      typeof success !== "boolean" ||
      !details ||
      typeof details !== "object" ||
      !receipt
    )
      throw new TurnError("回执不完整");
    if (who !== "player") {
      const p = this.collaborators(world, task).find((p) => p.actor_id === who);
      if (p?.status !== "willing" || !p.decision.step_ids.includes(stepId))
        throw new TurnError("NPC尚未同意协作此步骤");
    }
    if (
      !step.performed_by.includes(who) &&
      !(
        step.performed_by.length === 1 &&
        step.performed_by[0] === "player" &&
        who !== "player"
      )
    )
      throw new TurnError("不能代替其他NPC的专属职责");
    const payload = encode({
        task,
        step: stepId,
        succeeded: success,
        performed_by: who,
        details,
      }),
      prior = this.db
        .prepare(
          "SELECT payload FROM task_receipts WHERE session=? AND receipt=?",
        )
        .get(world, receipt);
    if (prior) {
      if (prior.payload !== payload) throw new TurnError("重复回执内容冲突");
      return false;
    }
    if (state.status !== "active")
      throw new TurnError("玩家尚未接受任务或任务已完成");
    let progress = JSON.parse(state.progress);
    const same = (a, b) => encode([...a].sort()) === encode([...b].sort());
    if (progress[stepId]?.succeeded) {
      if (
        task === "two_friends_photo" &&
        stepId === "invite" &&
        success &&
        !same(
          details.participants || [],
          progress.invite.details.participants || [],
        )
      )
        progress = {};
      else throw new TurnError("不能重复完成同一步骤");
    }
    if (success && step.depends_on.some((id) => !progress[id]?.succeeded))
      throw new TurnError("前置步骤尚未完成");
    if (task === "two_friends_photo" && success) {
      const peers = details.participants || [];
      if (
        peers.length !== 2 ||
        new Set(peers).size !== 2 ||
        peers.some((p) => !positions[p] || ["z", "zhou_bobo"].includes(p))
      )
        throw new TurnError("需两位不同的既有熟人");
      for (const k of ["invite", "consent", "arrive"])
        if (progress[k] && !same(progress[k].details.participants || [], peers))
          throw new TurnError("各步需同一对参与者");
      if (
        stepId === "consent" &&
        (!same(details.voluntary_consents || [], peers) ||
          peers.some((id) => {
            const p = this.collaborators(world, task).find(
              (p) => p.actor_id === id,
            );
            return (
              p?.status !== "willing" ||
              !p.decision.step_ids.includes("consent")
            );
          }))
      )
        throw new TurnError("需每位NPC实际自主同意");
      if (
        (["arrive", "photo"].includes(stepId) &&
          peers.some((id) => positions[id] !== "photo_studio")) ||
        (stepId === "photo" && positions.zhou_bobo !== "photo_studio")
      )
        throw new TurnError("拍摄参与者尚未到场");
    }
    progress[stepId] = {
      succeeded: success,
      performed_by: who,
      details,
      receipt_id: receipt,
    };
    this.db
      .prepare("INSERT INTO task_receipts VALUES(?,?,?)")
      .run(world, receipt, payload);
    this.db
      .prepare(
        "UPDATE task_state SET progress=?,status=? WHERE session=? AND task=?",
      )
      .run(
        JSON.stringify(progress),
        d.steps.every((s) => progress[s.id]?.succeeded)
          ? "completed"
          : "active",
        world,
        task,
      );
    return true;
  }
  reward(world, task, actor, recipient = "player") {
    const d = this.definition(task),
      s = this.state(world, task);
    if (
      recipient !== "player" ||
      d.owner !== actor ||
      s.status !== "completed" ||
      s.reward_claimed
    )
      throw new TurnError("不能领取该任务结果");
    this.db
      .prepare(
        "UPDATE task_state SET reward_claimed=1 WHERE session=? AND task=?",
      )
      .run(world, task);
    return d.reward;
  }
  snapshot(world) {
    return {
      state: this.db
        .prepare("SELECT * FROM task_state WHERE session=?")
        .all(world),
      receipts: this.db
        .prepare("SELECT * FROM task_receipts WHERE session=?")
        .all(world),
      followups: this.db
        .prepare("SELECT * FROM task_followups WHERE session=?")
        .all(world),
      collaboration: this.db
        .prepare("SELECT * FROM task_collaboration WHERE session=?")
        .all(world),
    };
  }
  restore(world, data) {
    for (const [key, table] of Object.entries({
      state: "task_state",
      receipts: "task_receipts",
      followups: "task_followups",
      collaboration: "task_collaboration",
    })) {
      this.db.prepare("DELETE FROM " + table + " WHERE session=?").run(world);
      for (const row of data[key] || []) {
        const values = { ...row, session: world },
          keys = Object.keys(values);
        this.db
          .prepare(
            `INSERT INTO ${table} (${keys.join(",")}) VALUES(${keys.map(() => "?").join(",")})`,
          )
          .run(...keys.map((k) => values[k]));
      }
    }
  }
}
