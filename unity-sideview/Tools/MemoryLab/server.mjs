import http from "node:http";
import {
  readFileSync,
  writeFileSync,
  mkdirSync,
  chmodSync,
  existsSync,
} from "node:fs";
import { resolve, join, dirname } from "node:path";
import { tmpdir } from "node:os";
import { randomBytes, createHash } from "node:crypto";
import { pathToFileURL, fileURLToPath } from "node:url";
const APP_ROOT = dirname(fileURLToPath(import.meta.url));
import { Store, uid } from "./lib/store.mjs";
import { Catalog, PROJECT } from "./lib/catalog.mjs";
import { ModelProvider } from "./lib/provider.mjs";
import { Engine } from "./lib/engine.mjs";
import { TurnError, canonical } from "./lib/policy.mjs";
export function environmentFromFile(path) {
  if (!path) return;
  for (const line of readFileSync(path, "utf8").split("\n")) {
    const found = line.match(
      /^(?:export\s+)?(GAME_MODEL_API_KEY|GAME_MODEL_NAME)=(.*)$/,
    );
    if (found && !process.env[found[1]])
      process.env[found[1]] = found[2].trim().replace(/^(['"])(.*)\1$/, "$2");
  }
}
const logicalHour = (day, period) => {
  if (
    !Number.isInteger(day) ||
    day < 1 ||
    !Number.isInteger(period) ||
    period < 0 ||
    period > 3
  )
    throw new TurnError("游戏日期无效");
  return (
    (day - 1) * 24 +
    [9 + 20 / 60, 14 + 10 / 60, 18 + 15 / 60, 20 + 40 / 60][period]
  );
};
export function createServer({
  dbPath = join(APP_ROOT, ".data/unified.sqlite"),
  root = PROJECT,
  providerFactory,
  decide,
  backgroundEnabled = true,
  configPath = process.env.Z_MEMORY_CONFIG ||
    join(APP_ROOT, ".local/model.json"),
} = {}) {
  const catalog = new Catalog(root),
    store = new Store(dbPath),
    provider = providerFactory
      ? providerFactory(store)
      : new ModelProvider(store);
  if (!providerFactory && process.env.GAME_MODEL_API_KEY)
    provider.set({
      provider: "deepseek",
      baseUrl: "https://api.deepseek.com",
      model: process.env.GAME_MODEL_NAME || "deepseek-chat",
      apiKey: process.env.GAME_MODEL_API_KEY,
    });
  if (
    !providerFactory &&
    !process.env.GAME_MODEL_API_KEY &&
    existsSync(configPath)
  ) {
    const config = JSON.parse(readFileSync(configPath, "utf8"));
    if (config.apiKey) provider.set(config);
  }
  let configurationRevision = 1;
  const engine = new Engine(store, catalog, provider, { decide }),
    token = randomBytes(24).toString("hex");
  let background = false;
  const status = () => ({
    ok: true,
    app: "moonlife-runtime",
    protocol: 2,
    configurationRevision,
    mode: provider.mode || "real",
    version: catalog.registry.pack_version,
    ...provider.publicConfig(),
  });
  const respond = (res, data, code = 200, type = "application/json") => {
    const body = typeof data === "string" ? data : JSON.stringify(data);
    res.writeHead(code, {
      "Content-Type": type + "; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    });
    res.end(body);
  };
  const wrapper = (world, results = [], started = performance.now()) => {
    const snapshot = engine.snapshot(world);
    return {
      session: snapshot.session,
      day: snapshot.day,
      simulation_tick: snapshot.simulation_tick,
      player_focus_actor: snapshot.player_focus_actor,
      ready_jobs: snapshot.ready_jobs,
      pending_jobs: snapshot.pending_jobs,
      history: snapshot.history,
      actors: snapshot.actors.map((a) => ({
        id: a.id,
        name: a.name,
        scene: a.scene,
        identity: a.identity,
        talking_to_player: a.talking_to_player,
      })),
      tasks: snapshot.tasks.map(({followup, collaborators, ...task}) => ({
        ...task,
        collaborators: (collaborators || []).map((helper) => ({
          actor_id: helper.actor_id,
          status: helper.status,
          step_ids: helper.decision?.step_ids || [],
        })),
      })),
      results: results.map(({output, ...result}) => ({
        ...result,
        ...(output ? {output: {dialogue: output.dialogue}} : {}),
      })),
      errors: results
        .filter((r) => r.status === "failed")
        .map((r) => ({
          job: r.job,
          message: r.validation_error || "本轮生成失败，未提交状态",
        })),
      elapsed_ms: Math.round(performance.now() - started),
    };
  };
  const gameResponse = (world, extra = {}) => {
    const w = engine.world(world);
    w.responseRevision++;
    w.lastAccess = Date.now();
    engine.save(w);
    return {
      ok: true,
      runId: w.runId,
      seq: w.seq,
      responseRevision: w.responseRevision,
      checkpoint: JSON.stringify(engine.checkpoint(world)),
      ...extra,
    };
  };
  const checkRun = (input) => {
    const w = engine.world(input.worldId);
    if (w.runId !== input.runId || !w.active)
      throw new TurnError("请求属于已取消的时间线");
    return w;
  };
  const server = http.createServer(async (req, res) => {
    try {
      const host = req.headers.host || "";
      if (!/^(127\.0\.0\.1|localhost)(:\d+)?$/.test(host))
        throw new TurnError("仅允许本机体验");
      const url = new URL(req.url, "http://" + host);
      if (req.headers.origin && req.headers.origin !== "http://" + host)
        throw new TurnError("请求来源不匹配");
      if (req.method === "GET") {
        if (url.pathname === "/")
          return respond(
            res,
            readFileSync(
              join(APP_ROOT, "public/experience.html"),
              "utf8",
            ).replace("__TOKEN__", token),
            200,
            "text/html",
          );
        if (url.pathname === "/api/config")
          return respond(res, {
            ...status(),
            actors: catalog.registry.actors.map((a) => ({
              id: a.actor_id,
              name: a.name,
            })),
            scenes: Object.keys(catalog.map.places),
            places: Object.fromEntries(
              Object.entries(catalog.map.places).map(([k, v]) => [k, v.name]),
            ),
            initial_policy: catalog.registry.initial_metric_policy,
          });
        if (url.pathname === "/api/health") return respond(res, status());
        if (
          ["/settings", "/settings.js", "/settings.css"].includes(url.pathname)
        )
          return respond(
            res,
            readFileSync(
              join(
                APP_ROOT,
                "public",
                url.pathname === "/settings"
                  ? "settings.html"
                  : url.pathname.slice(1),
              ),
              "utf8",
            ),
            200,
            url.pathname === "/settings"
              ? "text/html"
              : url.pathname.endsWith(".js")
                ? "text/javascript"
                : "text/css",
          );
        if (url.pathname === "/memories")
          return respond(
            res,
            readFileSync(
              join(APP_ROOT, "public/memories.html"),
              "utf8",
            ).replace("__TOKEN__", token),
            200,
            "text/html",
          );
        if (url.pathname === "/api/debug/memories") {
          if (
            req.headers["x-test-token"] !== token &&
            req.headers["x-z-memory"] !== "1"
          )
            throw new TurnError("需要本机观察室标识");
          const world =
            url.searchParams.get("worldId") ||
            store.experiments().find((w) => w.worldId)?.worldId;
          const actor = url.searchParams.get("actorId") || "z";
          const w = world ? engine.world(world) : null;
          return respond(res, {
            ok: true,
            worlds: store
              .experiments()
              .filter((w) => w.worldId)
              .map((w) => ({
                worldId: w.worldId,
                day: w.day,
                period: w.period,
              })),
            actors: catalog.registry.actors.map((a) => ({
              id: a.actor_id,
              name: a.name,
            })),
            ...(w
              ? engine.memory.inspect(w, actor)
              : { memories: [], known_facts: [], pending: 0 }),
          });
        }
        return respond(res, { error: "接口不存在" }, 404);
      }
      if (
        req.method !== "POST" ||
        (req.headers["x-test-token"] !== token &&
          req.headers["x-z-memory"] !== "1")
      )
        throw new TurnError("请求标识无效");
      let raw = "";
      for await (const chunk of req) {
        raw += chunk;
        if (Buffer.byteLength(raw) > 8_000_000) throw new TurnError("请求过大");
      }
      const p = JSON.parse(raw || "{}"),
        started = performance.now();
      if (url.pathname === "/api/config") {
        for (const w of store.experiments().filter((w) => w.worldId))
          engine.cancel(w.worldId);
        provider.set(p);
        mkdirSync(dirname(configPath), { recursive: true, mode: 0o700 });
        writeFileSync(
          configPath,
          JSON.stringify({ ...provider.config, apiKey: provider.key }),
          { mode: 0o600 },
        );
        chmodSync(configPath, 0o600);
        configurationRevision++;
        return respond(res, status());
      }
      if (url.pathname === "/api/config/test") {
        await provider.call(
          provider.request([{ role: "user", content: "请只回复OK" }], {
            maxTokens: 20,
          }),
          { signal: AbortSignal.timeout(8000) },
        );
        return respond(res, { ...status(), message: "模型连接成功" });
      }
      if (url.pathname.startsWith("/api/game/")) {
        const world = p.worldId;
        if (!world || !/^[a-zA-Z0-9_-]{1,100}$/.test(world))
          throw new TurnError("世界标识无效");
        let result;
        if (url.pathname === "/api/game/open") {
          const inputCheckpoint = p.checkpoint
            ? typeof p.checkpoint === "string"
              ? JSON.parse(p.checkpoint)
              : p.checkpoint
            : null;
          if (
            inputCheckpoint?.world?.hour >
            logicalHour(p.day, p.period) + 0.001
          )
            throw new TurnError("当前游戏不能加载未来记忆");
          engine.open(world, p.runId, inputCheckpoint);
          const w = engine.world(world);
          const target = logicalHour(p.day, p.period);
          if (w.hour > target + 0.001)
            throw new TurnError("当前游戏不能加载未来记忆");
          w.host = "unity";
          w.day = p.day;
          w.period = p.period;
          w.hour = target;
          engine.save(w);
          result = gameResponse(world);
        } else {
          const w = checkRun(p);
          if (url.pathname === "/api/game/sync") {
            const event = p.eventData;
            if (!event?.id || !Number.isInteger(event.seq))
              throw new TurnError("事件缺少稳定标识和序号");
            const digest = createHash("sha256")
              .update(JSON.stringify(canonical(event)))
              .digest("hex");
            const old = engine.event(world, event.id);
            if (old) {
              if (old.game_payload_hash !== digest)
                throw new TurnError("重复事件内容不同");
              return respond(res, gameResponse(world));
            }
            store.transaction(() => {
              const when = logicalHour(event.day, event.period),
                now = logicalHour(p.day, p.period);
              if (when > now + 0.001 || now < w.hour - 0.001)
                throw new TurnError("事件时间不属于当前进度");
              w.hour = now;
              w.day = p.day;
              w.period = p.period;
              engine.save(w);
              const location =
                event.locationId || event.location || "main_street";
              if (event.kind === "clock") {
                engine.observe(world, "游戏日期时段已经推进。", [], {
                  kind: "fact",
                  wake: false,
                  id: event.id,
                  seq: event.seq,
                  hour: when,
                });
              } else if (event.kind === "utterance") {
                const audience =
                  event.channel === "private_player_chat" ||
                  (event.speakerId === "player" && !event.channel)
                    ? [event.speakerId, event.recipientId]
                    : [
                        ...new Set([
                          event.speakerId,
                          event.recipientId,
                          ...(event.observers || []),
                        ]),
                      ];
                if (event.speakerId === "player") {
                  engine.addEvent(world, {
                    id: event.id,
                    text: event.text,
                    speaker: "player",
                    visible: audience,
                    recipient: event.recipientId,
                    conversationId: event.conversationId,
                    channel: event.channel || "private_player_chat",
                    location,
                    seq: event.seq,
                    hour: when,
                  });
                } else {
                  const proposal = store.record(event.proposalId);
                  if (
                    !proposal ||
                    proposal.actor !== event.speakerId ||
                    proposal.output.dialogue !== event.text ||
                    (proposal.context.interaction.mode === "player" &&
                      event.recipientId !== "player") ||
                    (proposal.output.action.type === "communicate" &&
                      proposal.output.action.target !== event.recipientId)
                  )
                    throw new TurnError("实际发言与批准的提案不一致");
                  engine.commit(world, event.proposalId, {
                    visible: audience,
                    location,
                    eventId: event.id,
                    seq: event.seq,
                    hour: when,
                    conversationId: event.conversationId,
                    replayDelivered: proposal.status === "restored_pending",
                  });
                }
              } else if (event.kind === "task_response") {
                engine.taskResponse(
                  world,
                  event.taskId,
                  event.accepted,
                  "unity_" + p.runId,
                  event.responderId || "player",
                  {
                    id: event.id,
                    seq: event.seq,
                    hour: when,
                    conversationId: event.conversationId,
                  },
                );
              } else if (event.kind === "task_invite") {
                engine.invite(
                  world,
                  event.taskId,
                  event.actorId,
                  event.text,
                  "unity_" + p.runId,
                  "player",
                  {
                    id: event.id,
                    seq: event.seq,
                    hour: when,
                    conversationId: event.conversationId,
                  },
                );
              } else if (event.kind === "task_receipt") {
                engine.taskReceipt(world,event.taskId,event.stepId,event.receiptId,event.succeeded,event.performerId,{participants:event.participants||[],voluntary_consents:event.voluntaryConsents||[],message_event_id:event.messageEventId},{id:event.id,seq:event.seq,hour:when});
              } else if (event.kind === "action_result") {
                engine.actionReceipt(
                  world,
                  event.actionId,
                  event.succeeded,
                  { scene: event.locationId, summary: event.text },
                  event.id,
                  { id: event.id, seq: event.seq, hour: when },
                );
              } else if (["observation", "story_event"].includes(event.kind)) {
                engine.observe(world, event.text, event.observers || [], {
                  kind: event.verified ? "fact" : "observation",
                  disclose_to: event.discloseTo || event.observers || [],
                  wake: false,
                  id: event.id,
                  seq: event.seq,
                  hour: when,
                  location,
                  gist: event.gist,
                });
              } else
                throw new TurnError("不支持该事件类型；剧情必须由引擎显式投递");
              const stored = engine.event(world, event.id);
              if (stored) {
                stored.game_payload_hash = digest;
                store.put("event", stored);
              }
            });
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/focus") {
            engine.focus(world, p.actorId || null, "unity_" + p.runId);
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/intent") {
            result = gameResponse(
              world,
              await engine.plan(world, p.actorId, p.candidates || [], {
                reachable_locations: [],
                conversation_id: p.conversationId,
                sceneId: p.scene,
                locationId: p.locationId,
              }),
            );
          } else if (url.pathname === "/api/game/utterance") {
            const actor = p.actorId,
              recipient = p.recipientId;
            catalog.actor(actor);
            if (recipient !== "player") catalog.actor(recipient);
            const isPlayer = recipient === "player";
            if (isPlayer && w.focus?.actor !== actor)
              throw new TurnError("该NPC未取得玩家对话占用");
            const history = engine.memory.history(
                w,
                actor,
                p.conversationId,
                8,
              ),
              last = history.at(-1);
            const fallback =
              last || engine.memory.history(w, actor, "", 8).at(-1);
            if (!fallback) throw new TurnError("先提交实际交谈或会面事件");
            const current = engine.world(world);
            if (p.locationId) {
              current.actors[actor].scene = p.locationId;
              engine.save(current);
            }
            const j = engine.job(world, actor, fallback.source_event_ids[0], {
              mode: isPlayer
                ? "player"
                : engine.event(world, fallback.source_event_ids[0])?.speaker ===
                    recipient
                  ? "npc_message"
                  : "world_event",
              conversation: p.conversationId,
              channel: p.channel || "face_to_face",
              environment: {
                sceneId: p.scene,
                ...(!isPlayer
                  ? {
                      social_opportunity: {
                        nearby_actors: [recipient],
                        kind: "co_location",
                      },
                    }
                  : {}),
                present_characters: [actor, recipient],
                reachable_locations: p.reachableLocations || [],
                available_actions: p.availableActions?.length
                  ? p.availableActions
                  : undefined,
              },
            });
            const results = await engine.run(world, 1, j.id),
              r = results[0];
            if (!r || r.status !== "proposed")
              throw new TurnError(
                r?.validation_error || "该角色正在处理另一回合",
              );
            result = gameResponse(world, {
              requestId: p.requestId,
              actorId: actor,
              proposalId: r.proposalId,
              text: r.output.dialogue,
              result: r.output,
              endConversation:
                r.output.info.communication?.intent === "end" ||
                r.output.info.social_decision?.choice === "keep_to_self",
              recalledMemoryIds: store
                .record(r.proposalId)
                .context.character_state.memories.map((m) => m.id),
            });
          } else if (url.pathname === "/api/game/commit") {
            engine.commit(world, p.proposalId, {
              visible: p.observers || [p.actorId],
              location: p.locationId,
            });
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/cancel") {
            engine.cancel(world, {
              actor: p.actorId,
              conversation: p.conversationId,
            });
            result = { ok: true };
          } else if (url.pathname === "/api/game/checkpoint")
            result = gameResponse(world);
          else if (url.pathname === "/api/game/close") {
            engine.cancel(world);
            const current = engine.world(world);
            current.active = false;
            engine.save(current);
            result = { ok: true };
          } else if (url.pathname === "/api/game/task/respond") {
            engine.taskResponse(
              world,
              p.taskId,
              p.accepted,
              "unity_" + p.runId,
              p.responderId || "player",
            );
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/task/invite") {
            engine.invite(
              world,
              p.taskId,
              p.actorId,
              p.text,
              "unity_" + p.runId,
            );
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/task/receipt") {
            engine.taskReceipt(
              world,
              p.taskId,
              p.stepId,
              p.receiptId,
              p.succeeded,
              p.performerId,
              p.details || {},
            );
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/state")
            result = gameResponse(world, { state: engine.snapshot(world) });
          else throw new TurnError("接口不存在");
        }
        return respond(res, result);
      }
      let world = p.session,
        results = [];
      if (url.pathname === "/api/new") {
        world = uid("session");
        engine.create(world, p.initial_metrics || {});
        engine.focus(world, p.actor || "z", p.client || "player");
      } else {
        engine.world(world);
        if (url.pathname === "/api/state")
          engine.focus(world, p.actor || "z", p.client || "player");
        else if (url.pathname === "/api/focus")
          engine.focus(world, p.actor, p.client || "player");
        else if (url.pathname === "/api/turn") {
          engine.player(world, p.actor, p.text, p.client || "player");
          results = await engine.run(world, 1);
        } else if (url.pathname === "/api/event") {
          engine.observe(world, p.text, p.visible, {
            kind: p.verified ? "fact" : "observation",
            disclose_to: p.disclose_to || p.visible,
          });
          results = await engine.run(world, 1);
        } else if (url.pathname === "/api/background") {
          if (p.advance !== false) engine.advance(world);
          results = await engine.run(world, Math.min(p.limit || 1, 3));
        } else if (url.pathname === "/api/day") {
          engine.day(world);
          results = await engine.run(world, 1);
        } else if (url.pathname === "/api/place")
          engine.place(world, p.actor, p.scene);
        else if (url.pathname === "/api/proposal/commit")
          engine.commit(world, p.proposalId);
        else if (url.pathname === "/api/receipt") {
          const action = store.record(p.action);
          engine.actionReceipt(world, p.action, p.success, {
            scene: action.target,
            summary: "测试主持人确认动作" + (p.success ? "成功" : "失败"),
          });
          results = await engine.run(world, 1);
        } else if (url.pathname === "/api/task/respond") {
          engine.taskResponse(
            world,
            p.task,
            p.accept,
            p.client || "player",
            p.responder || "player",
          );
          results = await engine.run(world, 1);
        } else if (url.pathname === "/api/task/invite") {
          engine.invite(
            world,
            p.task,
            p.helper,
            p.request,
            p.client || "player",
            p.requester || "player",
          );
          results = await engine.run(world, 1);
        } else if (url.pathname === "/api/task/enable")
          engine.tasks.enable(world, p.task);
        else if (url.pathname === "/api/task/receipt")
          engine.taskReceipt(
            world,
            p.task,
            p.step,
            p.receipt,
            p.success,
            p.performed_by,
            p.details || {},
          );
        else throw new TurnError("接口不存在");
      }
      return respond(res, wrapper(world, results, started));
    } catch (error) {
      respond(
        res,
        {
          error:
            error instanceof TurnError
              ? error.message
              : "请求处理失败，未返回内部信息",
          ok: false,
        },
        400,
      );
    }
  });
  const timer = setInterval(async () => {
    if (
      !backgroundEnabled ||
      background ||
      engine.runtime.active.size ||
      engine.inflight.size
    )
      return;
    background = true;
    try {
      for (const w of store
        .experiments()
        .filter(
          (w) =>
            w.active &&
            w.worldId &&
            (!w.lastAccess || Date.now() - w.lastAccess < 900000),
        )) {
        await engine.memory.consolidate(w.id, 1);
        if (!engine.runtime.active.size) await engine.memory.reflect(w.id, 1);
      }
    } catch {
    } finally {
      background = false;
    }
  }, 1500);
  timer.unref();
  server.on("close", () => {
    clearInterval(timer);
    for (const w of store.experiments().filter((w) => w.worldId))
      engine.cancel(w.worldId);
  });
  return { server, engine, store, catalog, provider, token, status };
}
if (import.meta.url === pathToFileURL(resolve(process.argv[1] || "")).href) {
  const flag = (name) => {
    const i = process.argv.indexOf(name);
    return i < 0 ? undefined : process.argv[i + 1];
  };
  environmentFromFile(flag("--env-file"));
  const port = Number(flag("--port") || process.env.Z_MEMORY_PORT || 4330);
  const app = createServer({
    dbPath:
      flag("--db") ||
      process.env.Z_MEMORY_DB ||
      join(APP_ROOT, ".data/unified.sqlite"),
  });
  app.server.listen(port, "127.0.0.1", () =>
    console.log("Unified NPC experience: http://127.0.0.1:" + port),
  );
  for (const signal of ["SIGINT", "SIGTERM"])
    process.on(signal, () =>
      app.server.close(() => {
        app.store.close();
        process.exit(0);
      }),
    );
}
