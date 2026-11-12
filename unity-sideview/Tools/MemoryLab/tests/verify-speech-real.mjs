// Opt-in real TTS probe. Each configured island actor synthesizes one short line.
// No LLM calls, player saves or automatic retries. Outputs must stay outside the repo.
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve, join } from "node:path";
import { SpeechService } from "../lib/speech.mjs";
import { Catalog } from "../lib/catalog.mjs";
const out=resolve(process.env.Z_TTS_PROBE_OUTPUT || "");
if(!out.startsWith("/private/tmp/"))throw Error("Set Z_TTS_PROBE_OUTPUT to an isolated /private/tmp directory");
mkdirSync(out,{recursive:true});
const speech=new SpeechService(),catalog=new Catalog(),report=[];
for(const [id,actor] of catalog.actors) {
  if(process.env.Z_TTS_PROBE_ACTOR && id!==process.env.Z_TTS_PROBE_ACTOR)continue;
  const start=Date.now();
  try {
    const audio=await speech.synthesize({worldId:"probe-"+id,runId:"isolated",proposalId:"line-"+id,
      conversationId:"probe",actorId:id,text:"你好，我是"+actor.name+"。",delivery:""});
    const pcm=Buffer.from(audio.base64,"base64"),header=Buffer.alloc(44);
    header.write("RIFF");header.writeUInt32LE(36+pcm.length,4);header.write("WAVEfmt ",8);header.writeUInt32LE(16,16);
    header.writeUInt16LE(1,20);header.writeUInt16LE(1,22);header.writeUInt32LE(24000,24);header.writeUInt32LE(48000,28);
    header.writeUInt16LE(2,32);header.writeUInt16LE(16,34);header.write("data",36);header.writeUInt32LE(pcm.length,40);
    let peak=0;for(let n=0;n<pcm.length;n+=2)peak=Math.max(peak,Math.abs(pcm.readInt16LE(n)));
    if(!peak)throw Error("silent_audio");
    writeFileSync(join(out,id+".wav"),Buffer.concat([header,pcm]));
    report.push({actor:id,ok:true,milliseconds:Date.now()-start,seconds:pcm.length/48000,peak});
    console.log(id+": audio received ("+(Date.now()-start)+" ms)");
  } catch(e) {report.push({actor:id,ok:false,code:e.code||"probe_failed",milliseconds:Date.now()-start});console.log(id+": "+(e.code||"probe_failed"));}
}
writeFileSync(join(out,"report.json"),JSON.stringify(report,null,2));
if(report.some(r=>!r.ok))process.exitCode=1;
