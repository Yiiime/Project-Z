// Diagnostic service for an isolated Unity run. Raw model responses stay in /private/tmp.
import { appendFileSync } from "node:fs";
import { createServer } from "./server.mjs";
const root=process.argv[2],port=Number(process.argv[3]);
if(!root?.startsWith("/private/tmp/")||!port)throw Error("Use a temporary directory and a dedicated port");
const app=createServer({dbPath:root+"/memory.sqlite"});
const call=app.provider.call.bind(app.provider);
app.provider.call=async(body,options)=>{
  const response=await call(body,options);
  try {const c=JSON.parse(body.messages?.at(-1)?.content);if(c.turn_id)appendFileSync(root+"/model.jsonl",JSON.stringify({turn:c.turn_id,actor:c.actor_id,mode:c.interaction.mode,output:response.text})+"\n");}catch{}
  return response;
};
app.server.listen(port,"127.0.0.1",()=>console.log("Verification service listening on "+port));
for(const signal of ["SIGINT","SIGTERM"])process.on(signal,()=>app.server.close(()=>{app.store.close();process.exit(0);}));
