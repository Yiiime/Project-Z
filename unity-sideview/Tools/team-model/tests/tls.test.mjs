import test from "node:test";
import assert from "node:assert/strict";
import https from "node:https";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { secureRequest } from "../client.mjs";

test("pinned HTTPS accepts the expected certificate and rejects a different server certificate", async t => {
  const root=mkdtempSync(join(tmpdir(),"z-team-tls-"));t.after(()=>rmSync(root,{recursive:true,force:true}));
  for(const name of ["trusted","other"]){
    const result=spawnSync("openssl",["req","-x509","-newkey","rsa:2048","-nodes","-days","1","-subj","/CN=127.0.0.1","-addext","subjectAltName=IP:127.0.0.1","-keyout",join(root,name+".key"),"-out",join(root,name+".pem")],{stdio:"pipe"});
    assert.equal(result.status,0,"OpenSSL certificate creation failed");
  }
  let requests=0;
  const server=https.createServer({key:readFileSync(join(root,"trusted.key")),cert:readFileSync(join(root,"trusted.pem"))},(req,res)=>{requests++;res.writeHead(200,{"Content-Type":"application/json"});res.end('{"ok":true}');});
  t.after(()=>{server.closeAllConnections();return new Promise(resolve=>server.close(resolve));});
  await new Promise(resolve=>server.listen(0,"127.0.0.1",resolve));
  const url=`https://127.0.0.1:${server.address().port}/auth/status`;
  const valid=await secureRequest(url,{certificate:readFileSync(join(root,"trusted.pem")),token:"fixture-service-token",signal:AbortSignal.timeout(5000)});
  assert.equal((await valid.json()).ok,true);
  await assert.rejects(secureRequest(url,{certificate:readFileSync(join(root,"other.pem")),token:"fixture-service-token",signal:AbortSignal.timeout(5000)}));
  assert.equal(requests,1,"The untrusted connection must not receive the token");
});
