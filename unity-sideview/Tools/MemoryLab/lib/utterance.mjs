import { createHash } from 'node:crypto';
import { canonical, TurnError } from './policy.mjs';

// Durable request identity lives on the job, so response recovery also works
// after a service restart. The in-process queue only coalesces concurrent calls.
export function createUtteranceHandler(engine, firstDay = null) {
  const running = new Map();
  return async function utterance(p) {
    const key = `${p.worldId}:${p.runId}:${p.actorId}`;
    const previous = running.get(key);
    const task = (async () => {
      if (previous) await previous.catch(() => {});
      const world = p.worldId, actor = p.actorId, recipient = p.recipientId;
      let w = engine.world(world);
      if (!w.active || w.runId !== p.runId) throw new TurnError('请求属于已取消的时间线');
      engine.catalog.actor(actor);
      if (recipient !== 'player') engine.catalog.actor(recipient);
      const isPlayer = recipient === 'player';
      if (isPlayer && w.focus?.actor !== actor) throw new TurnError('该NPC未取得玩家对话占用');
      if (isPlayer) await firstDay?.prepareTask(world, actor, p.conversationId);
      w = engine.world(world);
      if (!w.active || w.runId !== p.runId || isPlayer && w.focus?.actor !== actor) throw new TurnError('请求属于已取消的时间线');
      const source = engine.utteranceSource(world, actor, recipient, p.conversationId, p.sourceEventId);
      const channel = p.channel || (isPlayer ? 'private_player_chat' : 'face_to_face');
      const environment = {
        ...(isPlayer && w.firstDay && !['Complete','DailyReady'].includes(w.firstDay.phase) ? {first_day:w.firstDay} : {}),
        sceneId: p.scene, day: p.day, period: p.period, weather: p.weather || w.weather || '未提供',
        actual_audience: isPlayer ? [actor, 'player'] : [...new Set([actor, recipient, ...(p.observers || [])])],
        ...(!isPlayer ? { social_opportunity: { nearby_actors: [recipient], kind: 'co_location' } } : {}),
        recipient_actor_id:recipient,companion_routes:isPlayer?(p.companionRoutes||[]):[],
        present_characters: [actor, recipient], reachable_locations: p.reachableLocations || [],
        ...(Array.isArray(p.walkableLocations) ? { walkable_locations: p.walkableLocations } : {}),
        available_actions: p.availableActions?.length ? p.availableActions : undefined,
      };
      const inputHash = createHash('sha256').update(JSON.stringify(canonical({ actor, recipient,
        source: source.id, conversation: p.conversationId, channel, location: p.locationId || null, environment, runtimeRevision:p.runtimeSeq??null }))).digest('hex');
      const requestId = p.requestId || `source:${actor}:${source.id}`;
      if (typeof requestId !== 'string' || requestId.length > 240) throw new TurnError('请求标识无效');
      const jobs = engine.records(w, 'job');
      let job = jobs.find(j => j.request_ids?.includes(requestId));
      if (job && job.input_hash !== inputHash) throw new TurnError('同一请求标识的内容不同');
      // A reconnect can mint a new requestId for the same synced player line.
      job ||= jobs.find(j => j.input_hash === inputHash && ['pending', 'running', 'proposed', 'succeeded'].includes(j.status));
      if (job) {
        if (!job.request_ids.includes(requestId)) { job.request_ids.push(requestId); engine.record('job', job); }
        const proposal = engine.store.record(job.proposal_id || '') || engine.records(w, 'proposal').find(x => x.job === job.id);
        if (!proposal && ['pending', 'running'].includes(job.status) && !engine.inflight.has(world + ':' + actor)) {
          // The process can stop after persisting the job but before persisting
          // its proposal. Resume that job; no reply or Info was committed.
          job.status = 'pending'; engine.record('job', job);
        } else {
          if (!proposal || !['pending', 'committed'].includes(proposal.status) || proposal.runId !== w.runId)
            throw new TurnError(job.error || '这次回复已取消，请重新发起对话');
          return response(job, proposal, requestId);
        }
      }
      if (isPlayer && engine.records(w, 'event').some(e => e.conversationId === p.conversationId &&
        e.speaker === 'player' && e.recipient === actor && e.seq > source.seq)) {
        if (job) { job.status = 'cancelled'; engine.record('job', job); }
        throw new TurnError('输入已经被较新的对话替代');
      }
      // A newer delivered player input explicitly supersedes an unacknowledged
      // reply in this conversation; its old numeric changes are never applied.
      if (isPlayer) engine.supersedePlayerReply(world, actor, p.conversationId, source);
      if(p.runtimeSnapshot){
        if(p.runtimeSeq!==(engine.actor(world,actor).runtime_input_seq||0))throw new TurnError("NPC环境尚未同步或已过期");
        engine.updateEnvironment(world,actor,p.runtimeSnapshot);
      }else if(p.locationId&&!w.actors[actor].pending_action){w.actors[actor].scene=p.locationId;engine.save(w);}
      job ||= engine.job(world, actor, source.id, { mode: isPlayer ? 'player' : source.speaker === recipient ? 'npc_message' : 'world_event',
        conversation: p.conversationId, channel, environment });
      job.request_ids ||= [requestId]; job.input_hash = inputHash; engine.record('job', job);
      const result = (await engine.run(world, 1, job.id))[0];
      if (!result || result.status !== 'proposed') throw new TurnError(result?.validation_error || '该角色正在处理另一回合');
      return response(job, engine.store.record(result.proposalId), requestId);
    })();
    running.set(key, task);
    try { return await task; } finally { if (running.get(key) === task) running.delete(key); }
  };
  function response(job, proposal, requestId) {
    return { requestId, actorId: job.actor, proposalId: proposal.id, text: proposal.output.dialogue,
      result: proposal.output, endConversation: proposal.output.info.communication?.intent === 'end' || proposal.output.info.social_decision?.choice === 'keep_to_self',
      recalledMemoryIds: proposal.context.character_state.memories.map(m => m.id), expression:proposal.portrait_expression??null,delivery:proposal.delivery||"" };
  }
}
