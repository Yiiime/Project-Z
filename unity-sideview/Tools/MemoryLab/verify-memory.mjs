// Explicit real-model extraction and reflection probe; isolated from user saves.
import { writeFileSync } from "node:fs";
import assert from "node:assert/strict";
import { createServer } from "./server.mjs";
const output=process.argv[2];
if(!output?.startsWith("/private/tmp/"))throw Error("Use temporary output");
const app=createServer({dbPath:":memory:",backgroundEnabled:false});
const {engine:e,store}=app;
e.create("memory_probe");
const checks=[];
for(const text of ["我最喜欢海边的橘色落日，下次想和你一起看。","我昨天说想出海，但现在风浪太大，我决定先留在岛上。","我答应明天带一本画册过来；今天还没有带到。"]){
  const j=e.player("memory_probe","z",text);const [r]=await e.run("memory_probe",1,j.id);
  checks.push({kind:"dialogue",status:r?.status,dialogue:r?.output?.dialogue,error:r?.validation_error});
  if(r?.status==="proposed")e.commit("memory_probe",r.proposalId);
}
await e.memory.consolidate(e.world("memory_probe").id,12);
await e.memory.reflect(e.world("memory_probe").id,2);
const before=e.checkpoint("memory_probe"), events=before.records.event;
const messages=before.records.message;
const extracted=before.records.memory.filter(m=>m.origin!=="pending_extraction");
checks.push({kind:"longterm-extraction",count:extracted.length,jobs:before.records.memory_job.map(j=>({status:j.status,error:j.error}))});
assert(extracted.length>0,"Real memory extraction produced no memories");
for(const m of extracted){
  assert(m.actorId==="z","Private player conversation reached another actor");
  for(const ev of m.evidence||[]) {const source=messages.find(x=>x.id===ev.messageId);assert(source?.content.includes(ev.quote),"Untraceable memory quote");}
}
const wish=messages.find(m=>m.speakerId==="player"&&m.content.includes("我最喜欢海边的橘色落日"));
const wishPromoted=extracted.some(m=>m.kind==="commitment"&&m.evidence?.some(ev=>ev.messageId===wish?.id));
checks.push({kind:"wish-is-not-promise",passed:!wishPromoted});
e.open("memory_probe","reload",before);
const restored=e.checkpoint("memory_probe");
assert.equal(restored.world.epoch,before.world.epoch+1);
const content=rows=>rows.map(({epoch,...m})=>m);
assert.deepEqual(content(restored.records.memory),content(before.records.memory));
checks.push({kind:"memory-save-reload",passed:true});
writeFileSync(output,JSON.stringify({model:app.provider.config.model,checks,events,memories:extracted,reflections:store.records(e.world("memory_probe").id,"reflection_job")},null,2));
console.log(JSON.stringify(checks));
await new Promise(resolve=>app.server.close(resolve));store.close();
