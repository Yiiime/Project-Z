import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { createServer } from "../server.mjs";

test("team login enables real provider protocol without loading or rewriting the local API Key", async t => {
  const root=mkdtempSync(join(tmpdir(),"z-team-memory-"));
  const configPath=join(root,"model.json");
  const prior=JSON.stringify({provider:"deepseek",baseUrl:"https://api.deepseek.com",model:"old-model",apiType:"chat",apiKey:"fixture-local-key"});
  writeFileSync(configPath,prior);
  let logged=false,calls=0;
  const team={
    config:{profiles:{npc:{provider:"ark",baseUrl:"https://ark-cn-beijing.bytedance.net/api/v3",model:"ep-test",apiType:"responses"}}},
    session:()=>logged?{user:{id:17,login:"member"}}:null,
    publicStatus:()=>({enabled:true,connected:logged,user:logged?{id:17,login:"member"}:null}),
    startLogin:async()=>({userCode:"ABCD-EFGH",verificationUri:"https://github.com/login/device",interval:5}),
    pollLogin:async()=>{logged=true;return {connected:true,pending:false};},
    logout:async()=>{logged=false;},
    modelFetch:()=>async(url,options)=>{
      calls++;assert.equal(url,"https://ark-cn-beijing.bytedance.net/api/v3/responses");
      assert.equal(JSON.parse(options.body).model,"ep-test");
      assert.equal(options.headers.Authorization,"Bearer team-managed");
      return Response.json({status:"completed",output:[{type:"message",content:[{type:"output_text",text:"OK"}]}]});
    }
  };
  const app=createServer({dbPath:join(root,"memory.sqlite"),configPath,teamClient:team,backgroundEnabled:false});
  t.after(async()=>{app.server.closeAllConnections();await new Promise(resolve=>app.server.close(resolve));app.store.close();rmSync(root,{recursive:true,force:true});});
  await new Promise(resolve=>app.server.listen(0,"127.0.0.1",resolve));
  const base=`http://127.0.0.1:${app.server.address().port}`;
  const post=(route,body={})=>fetch(base+route,{method:"POST",headers:{"Content-Type":"application/json","X-Z-Memory":"1"},body:JSON.stringify(body)});
  const before=await (await fetch(base+"/api/config")).json();assert.equal(before.team.enabled,true);assert.equal(before.configured,false);
  assert.equal((await post("/api/config",{apiKey:"override"})).status,400);
  assert.equal((await (await post("/api/team/login")).json()).userCode,"ABCD-EFGH");
  await (await post("/api/team/poll")).json();
  const connected=await (await fetch(base+"/api/config")).json();assert.equal(connected.configured,true);assert.equal(connected.team.connected,true);
  assert.doesNotMatch(JSON.stringify(connected),/fixture-local-key|team-managed/);
  assert.equal((await post("/api/config/test")).status,200);assert.equal(calls,1);
  assert.equal(readFileSync(configPath,"utf8"),prior);
  await post("/api/team/logout");
  assert.equal((await (await fetch(base+"/api/health")).json()).configured,false);
});
