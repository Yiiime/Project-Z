import { SpeechService, SpeechError } from "./lib/speech.mjs";
import {FirstDayAgent} from './lib/first-day.mjs';
import {dayConversation} from "./lib/daytime.mjs";
import {localSongSubmit,DaySongDispatch} from "./lib/day-song-dispatch.mjs";
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
import { TeamModelProvider } from "./lib/team-provider.mjs";
import { loadTeamClient } from "../team-model/client.mjs";
import { Engine } from "./lib/engine.mjs";
import { LiveRoom } from "./lib/live-room.mjs";
import { createLab } from "./lib/lab.mjs";
import { createUtteranceHandler } from "./lib/utterance.mjs";
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
import {memoryProjection, narrativeChange, rememberNarration} from "./lib/live-memory.mjs";
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
  teamClient,
  speech = new SpeechService(),
  songSubmit,
  decide,
  backgroundEnabled = true,
  configPath = process.env.Z_MEMORY_CONFIG ||
    join(APP_ROOT, ".local/model.json"),
} = {}) {
  const team = providerFactory ? null : (teamClient === undefined ? loadTeamClient() : teamClient);
  const catalog = new Catalog(root),
    store = new Store(dbPath),
    provider = providerFactory
      ? providerFactory(store)
      : team ? new TeamModelProvider(store, team) : new ModelProvider(store);
  if (!providerFactory && !team && process.env.GAME_MODEL_API_KEY)
    provider.set({
      provider: "deepseek",
      baseUrl: "https://api.deepseek.com",
      model: process.env.GAME_MODEL_NAME || "deepseek-chat",
      apiKey: process.env.GAME_MODEL_API_KEY,
    });
  if (
    !providerFactory && !team &&
    !process.env.GAME_MODEL_API_KEY &&
    existsSync(configPath)
  ) {
    const config = JSON.parse(readFileSync(configPath, "utf8"));
    if (config.apiKey) provider.set(config);
  }
  let configurationRevision = 1;
  const engine = new Engine(store, catalog, provider, { decide }),
    firstDay = new FirstDayAgent(engine, provider, catalog),
    token = randomBytes(24).toString("hex");
  const daySongs=songSubmit ? new DaySongDispatch(songSubmit) : null;
  const liveRoom=new LiveRoom(engine), lab=createLab(engine), utterance=createUtteranceHandler(engine,firstDay);
  let background = false;
  const status = () => ({
    ok: true,
    app: "moonlife-runtime",
    protocol: 2,
    runtimeRevision: "integrated-npc-runtime-v1",
    configurationRevision,
    mode: provider.mode || "real",
    version: catalog.registry.pack_version,
    ...provider.publicConfig(),
    speech: speech.status(),
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
  const referenceCheckpoint=world=>{
    const cp=engine.checkpoint(world),ref=store.freezeCheckpoint(cp);
    return {version:3,ref,worldId:world,contentRevision:cp.contentRevision,
      broadcast:{...cp.broadcast,entries:[]},world:{day:cp.world.day,period:cp.world.period,hour:cp.world.hour,actors:Object.fromEntries(Object.entries(cp.world.actors).map(([id,a])=>[id,{runtime_input_seq:a.runtime_input_seq||0}]))},
      tasks:{state:cp.tasks.state,collaboration:cp.tasks.collaboration,definitions:cp.tasks.definitions},
      records:{action:cp.records.action.filter(a=>a.status==="pending"),event:cp.records.event.filter(e=>e.id.startsWith("story_"+world+"_")).map(e=>({id:e.id}))}};
  };
  const gameResponse = (world, extra = {}, portable = false) => {
    const w = engine.world(world);
    w.responseRevision++;
    w.lastAccess = Date.now();
    engine.save(w);
    const checkpoint=engine.checkpoint(world);
    daySongs?.update(w.worldId,w.day,checkpoint.broadcast.songs);
    return {
      ok: true,
      runId: w.runId,
      seq: w.seq,
      responseRevision: w.responseRevision,
      checkpoint: JSON.stringify(w.syncMode==="reference-v1"&&!portable?referenceCheckpoint(world):checkpoint),
      ...(w.syncMode==="reference-v1"&&extra.result?{...extra,result:{dialogue:extra.result.dialogue,expression:extra.result.expression||extra.expression}}:extra),
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
        if(["/lab","/lab.js","/lab.css"].includes(url.pathname))return respond(res,readFileSync(join(APP_ROOT,"public",url.pathname==="/lab"?"lab.html":url.pathname.slice(1)),"utf8").replace("__TOKEN__",token),200,url.pathname==="/lab"?"text/html":url.pathname.endsWith(".js")?"text/javascript":"text/css");
        if (url.pathname === "/runtimes")
          return respond(res, readFileSync(join(APP_ROOT, "public/runtimes.html"), "utf8").replace("__TOKEN__", token), 200, "text/html");
        if (url.pathname === "/api/debug/runtimes") {
          if (req.headers["x-test-token"] !== token && req.headers["x-z-memory"] !== "1")
            throw new TurnError("需要本机观察室标识");
          const world = url.searchParams.get("worldId"), actor = url.searchParams.get("actorId");
          return respond(res, {
            ok: true,
            worlds: store.experiments().filter(w => w.worldId).map(w => ({ worldId: w.worldId, day: w.day })),
            actors: catalog.registry.actors.map(a => ({ id: a.actor_id, name: a.name })),
            ...(world && actor ? engine.inspectRuntime(world, actor, {
              after: Number(url.searchParams.get("after") || 0), limit: Number(url.searchParams.get("limit") || 80),
            }) : {}),
          });
        }
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
      if (url.pathname.startsWith("/api/team/")) {
        if (!team) throw new TurnError("团队模型尚未启用");
        try {
          if (url.pathname === "/api/team/login") return respond(res, await team.startLogin());
          if (url.pathname === "/api/team/poll") return respond(res, await team.pollLogin());
          if (url.pathname === "/api/team/logout") { await team.logout(); return respond(res, { ok: true }); }
        } catch (error) { throw new TurnError(error.message); }
        throw new TurnError("接口不存在");
      }
      if (url.pathname === "/api/config") {
        if (team) throw new TurnError("当前由团队统一提供模型，请使用 GitHub 登录");
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
      if(url.pathname.startsWith("/api/lab/"))return respond(res,await lab(url.pathname.slice("/api/lab/".length),p));
      if (url.pathname.startsWith("/api/game/")) {
        const world = p.worldId;
        if (!world || !/^[a-zA-Z0-9_-]{1,100}$/.test(world))
          throw new TurnError("世界标识无效");
        let result;
        if (url.pathname === "/api/game/open") {
          let inputCheckpoint = p.checkpoint
            ? typeof p.checkpoint === "string"
              ? JSON.parse(p.checkpoint)
              : p.checkpoint
            : null;
          if(inputCheckpoint?.version===3){if(inputCheckpoint.worldId!==world)throw new TurnError("存档所属世界不一致");inputCheckpoint=store.thawCheckpoint(inputCheckpoint.ref);}
          if (
            inputCheckpoint?.world?.hour >
            logicalHour(p.day, p.period) + 0.001
          )
            throw new TurnError("当前游戏不能加载未来记忆");
          speech.cancel(world);
          engine.open(world, p.runId, inputCheckpoint);
          const w = engine.world(world);
          const target = logicalHour(p.day, p.period);
          if (w.hour > target + 0.001)
            throw new TurnError("当前游戏不能加载未来记忆");
          w.host = "unity";
          w.syncMode=p.syncMode||"legacy-full";w.weather=p.weather||w.weather||"未提供";
          w.day = p.day;
          w.period = p.period;
          w.hour = target;
          engine.save(w);
          if(p.details?.firstDay)firstDay.update(world,p.details.firstDay);
          if(daySongs){
            // A checkpoint can be rolled back, or reopened after an offline evening.
            daySongs.resetWorld(w.worldId);
            const events=engine.records(w,"event");
            const pastDays=new Set(events.filter(e=>e.daySongBridge).map(e=>Math.floor(e.hour/24)+1).filter(day=>day<w.day));
            for(const day of pastDays)daySongs.update(w.worldId,day,dayConversation(events,day).songs);
          }
          result = gameResponse(world);
        } else {
          const w = checkRun(p);
          if(p.details?.firstDay)w.firstDay=firstDay.update(world,p.details.firstDay);
          if(url.pathname === '/api/game/story/plan') {
            result=gameResponse(world,{storyPlan:await firstDay.plan(world,p.runId,p.requestId)});
          } else if (["/api/game/speech","/api/game/speech/stream"].includes(url.pathname)) {
            // Synthesize the committed server-side proposal, never caller-supplied text.
            const proposal = store.record(p.proposalId);
            const currentSpeech = () => {
              const current = checkRun(p);
              const event = proposal?.eventId ? engine.event(world, proposal.eventId) : null;
              const latest = engine.records(current, "event").filter(e =>
                e.conversationId === p.conversationId && e.channel === "private_player_chat").at(-1);
              return proposal?.world === world && proposal.runId === p.runId &&
                proposal.epoch === current.epoch && proposal.hour === current.hour && proposal.status === "committed" &&
                proposal.actor === p.actorId && current.focus?.actor === p.actorId &&
                proposal.context.interaction.mode === "player" && proposal.conversation === p.conversationId &&
                event?.channel === "private_player_chat" && event.recipient === "player" &&
                latest?.id === event.id && proposal.output.dialogue;
            };
            if (!currentSpeech()) throw new TurnError("语音不属于当前已展示的单聊台词");
            const controller = new AbortController();
            const closed = () => { if (!res.writableEnded) controller.abort(); };
            res.once("close", closed);
            const streaming=url.pathname.endsWith("/stream"),started=performance.now();
            let firstAudioMs=null,firstSubtitleMs=null;
            const write=packet=>{
              if(res.writableLength>8*1024*1024){controller.abort();res.destroy();}
              if(controller.signal.aborted||res.destroyed)throw new SpeechError("tts_cancelled");
              res.write(JSON.stringify(packet)+"\n");
            };
            if(streaming){
              res.writeHead(200,{"Content-Type":"application/x-ndjson; charset=utf-8","Cache-Control":"no-store","X-Accel-Buffering":"no"});
              res.flushHeaders();
              write({type:"start",runId:p.runId,proposalId:p.proposalId,speechWarning:proposal.speech_warning||""});
            }
            try {
              const audio = await speech.synthesize({worldId:world,runId:p.runId,proposalId:p.proposalId,
                actorId:p.actorId,conversationId:p.conversationId,text:proposal.output.dialogue,delivery:proposal.delivery},controller.signal,streaming?packet=>{
                  if(!currentSpeech())throw new SpeechError("tts_cancelled");
                  if(packet.type==="audio"&&firstAudioMs===null)firstAudioMs=Math.round(performance.now()-started);
                  if(packet.type==="subtitle"&&firstSubtitleMs===null)firstSubtitleMs=Math.round(performance.now()-started);
                  write(packet);
                }:undefined);
              if (!currentSpeech()) throw new SpeechError("tts_cancelled");
              if(streaming){
                write({type:"done",totalSamples:Buffer.byteLength(audio.base64,"base64")/2,
                  firstAudioMs,firstSubtitleMs,completeMs:Math.round(performance.now()-started)});
                res.end();return;
              }
              return respond(res,{ok:true,runId:p.runId,proposalId:p.proposalId,audio,
                speechWarning:proposal.speech_warning || ""});
            } catch(error){
              if(!streaming)throw error;
              if(!res.destroyed&&!controller.signal.aborted){
                const safe=error instanceof SpeechError?error:new SpeechError("tts_cancelled");
                write({type:"error",error:safe.message,speechError:safe.code});res.end();
              }
              return;
            } finally { res.removeListener("close",closed); }
          } else if (url.pathname === "/api/game/speech/cancel") {
            speech.cancel(world,p.conversationId,p.proposalId);
            return respond(res,{ok:true});
          } else if(url.pathname==="/api/game/live/open")result=gameResponse(world,{binding:liveRoom.open(p)});
          else if(url.pathname==="/api/game/live/close"){liveRoom.close(p);result=gameResponse(world);}
          else if(url.pathname.startsWith("/api/game/live/")){const op=url.pathname.split("/").at(-1);if(!["prepare","propose","observe","projection"].includes(op))throw new TurnError("直播接口不存在");result={ok:true,...liveRoom[op](p)};}
          else if(w.liveRoom && !["/api/game/checkpoint","/api/game/state","/api/game/runtime","/api/game/runtime/sync"].includes(url.pathname))throw new TurnError("直播期间暂停岛上行为，请先同步退出直播");
          else if (url.pathname === "/api/game/runtime/sync") {
            const event = p.runtimeEvent;
            if (!event || event.actorId !== p.actorId) throw new TurnError("NPC运行记录身份不匹配");
            let runtimeSeq;
            store.transaction(() => {
              const when = logicalHour(event.day, event.period), now = logicalHour(p.day, p.period);
              if (when > now + 0.001 || now < w.hour - 0.001) throw new TurnError("NPC运行记录时间不属于当前进度");
              w.hour = now; w.day = p.day; w.period = p.period; engine.save(w);
              runtimeSeq = engine.updateEnvironment(world, event.actorId, event.snapshot, {
                id: event.id, hostSeq: event.seq, day: event.day, period: event.period,
              });
            });
            result = gameResponse(world, { runtimeActorId: event.actorId, runtimeSeq });
          } else if (url.pathname === "/api/game/runtime") {
            result = { ok: true, ...engine.inspectRuntime(world, p.actorId, { after: p.after ?? 0, limit: p.limit ?? 80 }) };
          } else if (url.pathname === "/api/game/sync") {
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
              const replayProposal =
                event.kind === "utterance" && event.speakerId !== "player"
                  ? store.record(event.proposalId)
                  : null;
              return respond(
                res,
                gameResponse(
                  world,
                  replayProposal
                    ? { expression: replayProposal.portrait_expression ?? null }
                    : {},
                ),
              );
            }
            let committedExpression = null;
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
              if (event.kind === "clock" || (event.kind === "utterance" && event.speakerId === "player")) speech.cancel(world);
              if (event.kind === "clock") {
                engine.observe(world, "游戏日期时段已经推进。", [], {
                  kind: "fact",
                  wake: false,
                  id: event.id,
                  seq: event.seq,
                  hour: when,
                });
              } else if (event.kind === "live_memory") {
                if(event.channel!=="livestream" || !["player","z","engine"].includes(event.speakerId) ||
                   typeof event.text!=="string" || !event.text.length || event.text.length>360)
                  throw new TurnError("直播记忆回执无效");
                const actual=engine.addEvent(world,{id:event.id,seq:event.seq,hour:when,text:event.text,
                  kind:event.speakerId==="engine"?"fact":"claim",speaker:event.speakerId,channel:"livestream",
                  location:"live_room",subject:event.accepted===true && event.speakerId==="z"?"player_and_z":undefined,visible:["z","player"],conversationId:event.conversationId});
                if(event.speakerId==="z" && event.stepId){
                  const current=engine.world(world);
                  const card=memoryProjection(current,engine.records(current,"event")).stories.find(s=>s.id===event.stepId);
                  const change=narrativeChange(card,event.text);
                  if(change && change.step===Number(event.receiptId)){
                    rememberNarration(current,change,"livestream",actual.id);
                    engine.save(current);
                  }
                }
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
                    location: event.locationId || event.location || "",
                    seq: event.seq,
                    hour: when,
                  });
                } else {
                  const proposal = store.record(event.proposalId);
                  if (
                    !proposal ||
                    proposal.actor !== event.speakerId ||
                    proposal.output.dialogue !== event.text ||
                    proposal.conversation!==event.conversationId ||
                    (proposal.context.interaction.channel||"private_player_chat")!==(event.channel||"private_player_chat") ||
                    (proposal.context.interaction.mode === "player" &&
                      event.recipientId !== "player") ||
                    (proposal.output.action.type === "communicate" &&
                      proposal.output.action.target !== event.recipientId)
                  )
                    throw new TurnError("实际发言与批准的提案不一致");
                  committedExpression = proposal.portrait_expression ?? null;
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
                  { scene: event.locationId, summary: event.text, ...(event.reasonCode?{reason_code:event.reasonCode}:{}) },
                  event.id,
                  { id: event.id, seq: event.seq, hour: when },
                );
              } else if(event.kind==="action_transition") {
                engine.actionTransition(world,event.actionId,{scene:event.locationId,place:event.location,x:event.positionX,arrival_id:event.arrivalId},{id:event.id,seq:event.seq,hour:when});
              } else if(event.kind==="action_progress") {
                engine.actionProgress(world,event.actionId,event.routeIndex,event.locationId,{id:event.id,seq:event.seq,hour:when});
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
                  conversationId:event.conversationId,
                });
              } else
                throw new TurnError("不支持该事件类型；剧情必须由引擎显式投递");
              let stored = engine.event(world, event.id);
              if(!stored&&["utterance","action_result"].includes(event.kind))stored=engine.acknowledgeReceipt(world,event,when);
              if(!stored)throw new TurnError("事件没有产生可确认记录");
              if(stored && event.kind==='story_event' && event.verified && event.observers?.includes('z') &&
                 (['player_introduced','fireworks_story','arrival_accepted'].some(key=>event.id===`story_${world}_${key}`) ||
                  event.id.startsWith(`story_${world}_welcome_complete_`))) {
                stored.publicActivity='firstday'; store.put('event',stored);
              }
              if (stored) {
                if(event.kind==='story_event' && event.id===`story_${world}_encounter_${event.conversationId}` &&
                   event.conversationId && w.firstDay && event.observers?.length===2 && event.observers.includes('player') && event.observers.some(id=>catalog.actors.has(id))) {
                  stored.subject='player_encounter';stored.recipient=event.observers.find(id=>id!=='player');
                }
                if(event.kind==="utterance" && stored.speaker==="z" && stored.channel==="private_player_chat")stored.daySongBridge=true;
                stored.game_payload_hash = digest;
                store.put("event", stored);
              }
            });
            result = gameResponse(
              world,
              event.kind === "utterance" && event.speakerId !== "player"
                ? { expression: committedExpression }
                : {},
            );
          } else if (url.pathname === "/api/game/focus") {
            speech.cancel(world);
            engine.focus(world, p.actorId || null, "unity_" + p.runId);
            result = gameResponse(world);
          } else if (url.pathname === "/api/game/intent") {
            if (p.runtimeSnapshot) {
              if (p.runtimeSeq !== (engine.actor(world, p.actorId).runtime_input_seq || 0)) throw new TurnError("NPC环境尚未同步或已过期");
              engine.updateEnvironment(world, p.actorId, p.runtimeSnapshot);
            } else if (p.locationId) {
              const current = engine.world(world);
              if (!current.actors[p.actorId]?.pending_action) {
                catalog.place(p.locationId); current.actors[p.actorId].scene = p.locationId; engine.save(current);
              }
            }
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
            result=gameResponse(world,await utterance(p));
            // Prepare sound for the proposed harbor greeting without publishing a
            // dialogue receipt. Playback still requires the normal committed-line check.
            if (w.firstDay?.phase === 'Harbor' && p.actorId === 'z' && p.channel === 'private_player_chat') {
              const proposal=store.record(result.proposalId);
              if(proposal?.world===world && proposal.runId===p.runId && proposal.output?.dialogue)
                void speech.synthesize({worldId:world,runId:p.runId,proposalId:proposal.id,
                  actorId:p.actorId,conversationId:p.conversationId,text:proposal.output.dialogue,delivery:proposal.delivery}).catch(()=>{});
            }
          } else if (url.pathname === "/api/game/commit") {
            const proposal = store.record(p.proposalId);
            if(proposal?.output.dialogue)throw new TurnError("有台词的提案须使用实际发言回执");
            engine.commit(world, p.proposalId, {
              visible: p.observers || [p.actorId],
              location: p.locationId,
            });
            result = gameResponse(world, {
              expression: proposal?.portrait_expression ?? null,
            });
          } else if (url.pathname === "/api/game/cancel") {
            speech.cancel(world,p.conversationId);
            engine.cancel(world, {
              actor: p.actorId,
              conversation: p.conversationId,
            });
            result = { ok: true };
          } else if (url.pathname === "/api/game/checkpoint")
            result = gameResponse(world,{},p.portable===true);
          else if (url.pathname === "/api/game/close") {
            speech.cancel(world);
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
            error instanceof TurnError || error instanceof SpeechError
              ? error.message
              : "请求处理失败，未返回内部信息",
          ok: false,
          ...(error instanceof SpeechError ? {speechError:error.code} : {}),
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
    speech.cancel();
    daySongs?.close();
    clearInterval(timer);
    for (const w of store.experiments().filter((w) => w.worldId))
      engine.runtime.cancel(w.worldId);
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
    songSubmit:localSongSubmit(process.env.Z_LIVE_CONNECTION_FILE || resolve(APP_ROOT,"../../UserSettings/ZLive/local-connection.json")),
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
