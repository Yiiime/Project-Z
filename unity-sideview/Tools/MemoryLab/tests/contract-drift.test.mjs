import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import Ajv from 'ajv/dist/2020.js';
import {execFileSync} from 'node:child_process';
import {Catalog} from '../lib/catalog.mjs';
import {Store} from '../lib/store.mjs';
import {Engine} from '../lib/engine.mjs';
import {LiveRoom} from '../lib/live-room.mjs';
import {createLab} from '../lib/lab.mjs';

const schema=JSON.parse(fs.readFileSync(new URL('../../../../docs/Project-Z-NPC全链路.schema.json',import.meta.url)));
const ajv=new Ajv({strict:false,allErrors:true});ajv.addSchema(schema);
const valid=(name,value)=>{const check=ajv.compile({$ref:schema.$id+'#/$defs/'+name});assert(check(value),name+': '+JSON.stringify(check.errors));};
test('exported source-derived role/live contracts and source fingerprints have no drift',()=>{
 execFileSync(process.execPath,[new URL('../scripts/export-contract.mjs',import.meta.url).pathname,'--check'],{stdio:'pipe'});
});
test('actual role contexts, committed relation history, live schemas and lab snapshots satisfy the shared contract',async t=>{
 const catalog=new Catalog(),store=new Store(),e=new Engine(store,catalog,{publicConfig:()=>({configured:false})});t.after(()=>store.close());
 const lab=createLab(e),created=await lab('new',{actorId:'linxu'}),scope={worldId:created.worldId,runId:created.runId,actorId:'linxu'};valid('LabResponse',created);
 e.store.event(e.world(scope.worldId),'reflection','整理了近期经历',{actorId:'linxu'});
 assert.equal(e.snapshot(scope.worldId).history.linxu.length,0);
 e.decide=c=>({dialogue:'我会重新考虑。',action:{option_id:'wait_here'},info:{emotion:'serious',changes:{},support_willingness:'considering',peer_relation_changes:[{target_actor_id:'zhou_bobo',affinity_delta:-2,trust_delta:-5,reason:'听到了一条新的传闻，需要核实',source_event_ids:[c.interaction.source_event_id]}]}});
 const result=await lab('turn',{...scope,text:'听说周伯伯不愿意让Z走。'}),proposal=result.results[0];assert.equal(proposal.status,'proposed');
 const committed=await lab('commit',{...scope,proposalId:proposal.proposalId,dialogue:proposal.output.dialogue});valid('LabResponse',committed);valid('PortableCheckpoint',e.checkpoint(scope.worldId));
 for(const actor of catalog.actors.keys()){
  const suffix=actor==='z'?'Z':actor==='zhou_bobo'?'ZhouBobo':actor;
  const c=e.context(e.player(scope.worldId,actor,'你如何看待大家的关系？'));valid('ModelContext',c);
  const turn={dialogue:'我先想想。',action:{option_id:'wait_here'},info:{emotion:'curious',changes:{},...(actor==='z'?{}:{support_willingness:actor==='zhou_bobo'?'unwilling':'unassessed'})}};
  valid('PlayerTurn'+suffix,turn);valid('BackendTurn'+suffix,e.policy.validate(c,turn));
  const self=structuredClone(turn);self.info.peer_relation_changes=[{target_actor_id:actor,affinity_delta:1,trust_delta:1,reason:'自我关系应被拒绝',source_event_ids:[c.interaction.source_event_id]}];
  assert.equal(ajv.compile({$ref:schema.$id+'#/$defs/PlayerTurn'+suffix})(self),false);
 }
 e.focus(scope.worldId,null);const live=new LiveRoom(e),binding=live.open(scope);
 for(const actor of catalog.actors.keys())valid('LivePrepareResponse',{ok:true,...live.prepare({...binding,actorId:actor})});
});
