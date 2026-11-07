import { once } from "node:events";
import { writeFileSync } from "node:fs";
import { createServer } from "./server.mjs";
const output=process.argv[2];if(!output?.startsWith("/private/tmp/"))throw Error("Use temporary output");
const app=createServer({dbPath:":memory:",backgroundEnabled:true});
app.server.listen(0,"127.0.0.1");await once(app.server,"listening");
const base="http://127.0.0.1:"+app.server.address().port;
const post=(path,data)=>fetch(base+"/api/game/"+path,{method:"POST",headers:{"Content-Type":"application/json","X-Z-Memory":"1"},body:JSON.stringify(data)});
const rows=[];
async function sample(concurrency,batch,i){
  const prefix=`b${concurrency}_${batch}_${i}`,common={worldId:prefix,runId:prefix,day:1,period:0};
  for(const [path,data] of [["open",{}],["focus",{actorId:"z"}],["sync",{eventData:{id:prefix+"evt",seq:1,kind:"utterance",day:1,period:0,speakerId:"player",recipientId:"z",conversationId:prefix,text:"我是第一次来小岛，想找咖啡店，应该找谁？",locationId:"z_home",channel:"private_player_chat",observers:["z","player"]}}]]){
    const response=await post(path,{...common,...data});if(!response.ok)throw Error(`Setup failed: ${response.status}`);await response.arrayBuffer();
  }
  const start=performance.now();
  const response=await post("utterance",{...common,actorId:"z",recipientId:"player",conversationId:prefix,requestId:prefix,scene:"Street",locationId:"z_home"});
  const headersMs=performance.now()-start,data=await response.json(),completeMs=performance.now()-start;
  rows.push({concurrency,headersMs:Math.round(headersMs),completeMs:Math.round(completeMs),code:response.status,dialogue:data.result?.dialogue,error:data.error});
  writeFileSync(output,JSON.stringify({model:app.provider.config.model,streaming:false,note:"Native endpoint sends one validated JSON response. First displayable dialogue is available only after completeMs; this is not upstream token latency.",rows},null,2));
}
try{for(const concurrency of [1,4])for(let b=0;b<(concurrency===1?10:5);b++)await Promise.all(Array.from({length:concurrency},(_,i)=>sample(concurrency,b,i)));console.log(JSON.stringify({samples:rows.length,failures:rows.filter(r=>r.code!==200)}));}
finally{await new Promise(r=>app.server.close(r));app.store.close();}
