import test from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync} from 'node:fs';
import {Catalog} from '../../MemoryLab/lib/catalog.mjs';
import {NPCS,AUDIENCE_NAMES} from '../server/shared/audience-roster.js';
import {ISLAND_PERSONAS} from '../server/shared/worker/island-roster.js';
import {Z_PERSONA} from '../server/shared/worker/z-persona.js';
import {buildAgentRequest,normalizeAgentInput,adaptAudienceReply,parseAgentReply} from '../server/shared/worker/agent-director.js';
const catalog=new Catalog();
const event={id:'public',type:'speech.completed',text:'今天聊聊大家在岛上的工作。',speakerRole:'host',speakerId:'Z'};
const request=(ids,recent=[])=>normalizeAgentInput({requestId:crypto.randomUUID(),kind:'audience',eventId:event.id,context:{phase:'chat',publicEvents:[event],audience:{eligibleNpcIds:ids,recentMessages:recent}}});
test('generated host and seven viewers have one source and no private client biographies',()=>{
 execFileSync(process.execPath,[new URL('../scripts/build-character-content.mjs',import.meta.url).pathname,'--check']);
 const ids=[...catalog.actors.keys()].filter(id=>id!=='z');assert.deepEqual(NPCS.map(n=>n.npc_id),ids);
 for(const n of NPCS){assert.equal(AUDIENCE_NAMES[n.npc_id],catalog.actor(n.npc_id).name);assert.equal(ISLAND_PERSONAS[n.npc_id].profile,catalog.profile(n.npc_id));}
 assert(Z_PERSONA.includes(catalog.profile('z')));
 const client=readFileSync(new URL('../server/shared/audience-roster.js',import.meta.url),'utf8');
 assert(!client.match(/YLY|Luna|夺权|巡警演员|ISLAND_PERSONAS/));
});
test('legacy batch generation receives only one private persona, and cycles all seven actors',()=>{
 const ids=NPCS.map(n=>n.npc_id),recent=[],seen=[];
 for(let i=0;i<7;i++){
  const v=request(ids,recent),p=buildAgentRequest(v).input,n=p.npcs[0];
  assert.equal(p.npcs.length,1);assert.deepEqual(p.allowed_candidate_npc_ids,[n.npc_id]);assert.equal(p.slots.length,1);
  for(const other of ids.filter(id=>id!==n.npc_id))assert(!JSON.stringify(p).includes(JSON.stringify(catalog.profile(other)).slice(1,-1)));
  seen.push(n.npc_id);recent.push({npcId:n.npc_id,text:'本轮已经发出'});
 }
 assert.deepEqual(new Set(seen),new Set(ids));
});
test('every island actor can generate an allowed live candidate, but cannot impersonate another',()=>{
 for(const n of NPCS){
  const v=request([n.npc_id]),p=buildAgentRequest(v).input;
  assert.equal(p.npcs[0].profile,catalog.profile(n.npc_id));
  const reply={status:'ok',npc_id:n.npc_id,intent:'reaction',text:'今天的事我也想听听。',evidence_ids:['public']};
  assert.equal(parseAgentReply(adaptAudienceReply(JSON.stringify(reply),v),v).items[0].npc_id,n.npc_id);
  const other=NPCS.find(x=>x.npc_id!==n.npc_id).npc_id;
  assert.throws(()=>parseAgentReply(adaptAudienceReply(JSON.stringify({...reply,npc_id:other}),v),v));
 }
});
test('audience review has only the candidate private profile; Z gets no YLY takeover dossier',()=>{
 const v=normalizeAgentInput({requestId:crypto.randomUUID(),kind:'review',eventId:'public',context:{revision:1,publicEvents:[event],candidate:{id:'c',role:'audience',speakerId:'zhou_bobo',text:'今天聊点什么？',intent:'reaction',evidenceIds:['public']}}});
 const p=buildAgentRequest(v).input;assert.equal(p.viewer_profile.profile,catalog.profile('zhou_bobo'));
 assert(!JSON.stringify(p).includes(JSON.stringify(catalog.profile('liyuan')).slice(1,-1)));
 assert(!Z_PERSONA.includes(catalog.profile('zhou_bobo')));
});
