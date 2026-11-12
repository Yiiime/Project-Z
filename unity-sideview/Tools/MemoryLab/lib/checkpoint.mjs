import { readFileSync } from 'node:fs';

const source = JSON.parse(readFileSync(new URL('./storage-contract.schema.json', import.meta.url)));
export function storageContract(catalog, policy) {
  const schema = structuredClone(source);
  for (const actor of catalog.actors.keys()) {
    const suffix = actor === 'z' ? 'Z' : actor === 'zhou_bobo' ? 'ZhouBobo' : actor;
    const role = policy.schema(actor);
    schema.$defs['BackendInfo' + suffix] = role.$defs.info;
    schema.$defs['BackendTurn' + suffix].allOf = role.$defs.output.allOf;
  }
  const defs=schema.$defs;
  defs.ModelContext.properties.z_story={type:"object",required:["id","count","line"],properties:{id:{type:"string"},count:{type:"integer",minimum:0},line:{type:"string"}},additionalProperties:false};
  defs.ModelContext.properties.interaction.properties.counterpart={anyOf:[{type:"null"},{type:"object",required:["actor_id","name","role"],properties:{actor_id:{type:"string"},name:{type:"string"},role:{enum:["player","npc"]}},additionalProperties:false}]};
  for(const k of ["affinity","trust"])defs.PeerRelation.properties[k]={anyOf:[{$ref:"#/$defs/Metric"},{type:"null"}]};
  defs.PeerRelation.properties.history.items={anyOf:[{$ref:"#/$defs/RelationHistory"},{type:"object",required:["reason"],properties:{reason:{type:"string"}},additionalProperties:false}]};
  for(const name of ["ActionRecord","ExpandedAction","AvailableAction"]){const type=defs[name]?.properties?.type;if(type?.enum)type.enum=[...new Set([...type.enum,"escort_player","cancel_action"])];}
  const portable=defs.PortableCheckpoint;
  portable.properties.records.properties.trace={type:"array",items:{$ref:"#/$defs/TraceRecord"}};
  portable.properties.broadcast={type:"object"};
  portable.properties.npcRuntimeArchive={type:"object",required:["encoding","data"],properties:{encoding:{const:"deflate-raw-base64"},data:{type:"string"}},additionalProperties:false};
  const runtimeRecord={type:"object",required:["id","experimentId","epoch","actor"],properties:{id:{type:"string"},experimentId:{type:"string"},epoch:{type:"integer"},actor:{$ref:"#/$defs/ActorId"}},additionalProperties:true};
  for(const kind of ["npc_run","npc_environment"])portable.properties.records.properties[kind]={type:"array",items:runtimeRecord};
  return schema;
}

// Used both at restore and by the exported team contract. Invalid diagnostic
// model output remains inspectable; only executable proposals must be valid.
export function checkpointValidator(catalog, policy) {
  const contract = storageContract(catalog, policy);
  const validate = policy.ajv.compile({ ...contract, $ref: '#/$defs/PortableCheckpoint' });
  const shape = name => policy.ajv.compile({ ...contract, $ref: '#/$defs/' + name });
  const progress = shape('TaskProgress'), participation = shape('TaskParticipation'), followup = shape('TaskFollowup');
  const fail = message => { throw Error('存档无效：' + message); };
  const json = (value, label) => { try { return JSON.parse(value); } catch { fail(label + '不是合法JSON'); } };
  return cp => {
    if (cp?.world?.actors && Object.keys(cp.world.actors).sort().join() !== [...catalog.actors.keys()].sort().join()) fail('存档人物名单不是当前八位角色');
    if (cp?.records && Object.values(cp.records).filter(Array.isArray).flat().some(r => (r?.createdHour ?? r?.hour ?? 0) > cp.world?.hour)) fail('存档包含未来记忆');
    if (!validate(cp)) fail((validate.errors[0].instancePath || '/') + ' ' + validate.errors[0].message);
    const world = cp.world, rows = Object.values(cp.records).flat(), ids = new Map();
    if (world.worldId !== cp.worldId || world.id !== 'world_' + cp.worldId) fail('世界标识不一致');
    for (const row of rows) {
      if (ids.has(row.id)) fail('记录ID重复'); ids.set(row.id, row);
      if (row.experimentId !== world.id || row.epoch !== world.epoch) fail('记录属于其他世界或时间线');
      if ((row.createdHour ?? row.hour ?? 0) > world.hour) fail('存档包含未来记忆');
    }
    for (const [id, actor] of Object.entries(world.actors)) {
      if (actor.id !== id) fail('人物ID不一致');
      for (const target of Object.keys(actor.peer_relations))
        if (target === id || !catalog.actors.has(target)) fail('人物关系目标无效');
      if (actor.pending_action) {
        const action = cp.records.action.find(a => a.id === actor.pending_action);
        if (!action || action.actor !== id || action.status !== 'pending') fail('待执行动作引用无效');
      }
    }
    for (const proposal of cp.records.proposal) {
      if (proposal.world !== cp.worldId || proposal.context.actor_id !== proposal.actor || proposal.context.session_id !== cp.worldId)
        fail('提案人物或世界引用无效');
    }
    const definitions = new Map(catalog.tasks.map(t => [t.id, t]));
    for (const [kind, table] of Object.entries(cp.tasks)) {
      const keys = new Set();
      for (const row of table) {
        if (row.session !== cp.worldId) fail('任务所属世界不一致');
        const key = kind === 'receipts' ? row.receipt : kind === 'collaboration' ? row.task + ':' + row.actor : row.task;
        if (keys.has(key)) fail('任务记录主键重复'); keys.add(key);
        const def = row.task && definitions.get(row.task);
        if (row.task && !def) fail('未知任务');
        if (kind === 'state') {
          const value = json(row.progress, '任务进度');
          if (!value || Array.isArray(value) || typeof value !== 'object') fail('任务进度必须为对象');
          for (const [step, entry] of Object.entries(value))
            if (!def.steps.some(s => s.id === step) || !progress(entry)) fail('任务步骤进度无效');
        } else if (kind === 'collaboration' && row.decision) {
          const decision = json(row.decision, '协作决定');
          if (!participation(decision) || decision.task_id !== row.task || decision.step_ids.some(id => !def.steps.some(s => s.id === id))) fail('协作决定无效');
        } else if (kind === 'followups' && row.decision) {
          const decision = json(row.decision, '任务后续');
          if (!followup(decision) || decision.task_id !== row.task) fail('任务后续无效');
        } else if (kind === 'receipts') {
          const receipt = json(row.payload, '任务回执'), task = definitions.get(receipt?.task);
          if (!task || !task.steps.some(s => s.id === receipt.step) || typeof receipt.succeeded !== 'boolean' ||
            !['player', ...catalog.actors.keys()].includes(receipt.performed_by)) fail('任务回执内容无效');
        }
      }
    }
  };
}
