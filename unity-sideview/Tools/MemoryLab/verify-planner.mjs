import { writeFileSync } from "node:fs";
import { createServer } from "./server.mjs";
const path=process.argv[2];if(!path?.startsWith("/private/tmp/"))throw Error("Use temporary output");
const app=createServer({dbPath:":memory:",backgroundEnabled:false}),e=app.engine;
e.create("planner");const w=e.world("planner");w.host="unity";e.save(w);
const rows=[], call=app.provider.call.bind(app.provider);let raw;
app.provider.call=async(...args)=>{const r=await call(...args);raw=r.text;return r;};
for(let round=0;round<3;round++)for(const actor of ["z","linxu","zhou_bobo","xiaoxiaoqing","zhengyi"]){
  raw=null;
  try{const result=await e.plan("planner",actor,["z","linxu","zhou_bobo","xiaoxiaoqing","zhengyi"].filter(x=>x!==actor),{sceneId:"Street",locationId:"main_street",reachable_locations:[]});rows.push({actor,round,status:"valid",result});}
  catch(error){rows.push({actor,round,status:"failed",error:error.message,raw});}
  writeFileSync(path,JSON.stringify(rows,null,2));
}
console.log(JSON.stringify({calls:rows.length,failures:rows.filter(r=>r.status==="failed")}));
await new Promise(resolve=>app.server.close(resolve));app.store.close();
