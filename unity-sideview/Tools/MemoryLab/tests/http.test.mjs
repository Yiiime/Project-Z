import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync,rmSync,readFileSync,statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import http from 'node:http';
import { createMemoryServer } from '../server.mjs';
import { ModelProvider,DEFAULT_CONFIG } from '../lib/provider.mjs';
import { FixtureProvider } from './fixtures.mjs';
async function listen(s){await new Promise(r=>s.listen(0,'127.0.0.1',r));return 'http://127.0.0.1:'+s.address().port;}
async function close(s){s.closeAllConnections?.();await new Promise(r=>s.close(r));}
async function call(url,path,data,headers={}){const r=await fetch(url+path,data?{method:'POST',headers:{'content-type':'application/json','x-z-memory':'1',...headers},body:JSON.stringify(data)}:{});return {status:r.status,data:await r.json()};}
test('HTTP game endpoints persist independently, reject wrong origins and recover from invalid replies',async t=>{
 const dir=mkdtempSync(join(tmpdir(),'z-memory-http-'));const provider=new FixtureProvider();
 const app=createMemoryServer({dbPath:join(dir,'test.sqlite'),configPath:join(dir,'model.json'),providerFactory:()=>provider});const url=await listen(app.server);
 t.after(async()=>{await close(app.server);app.store.close();rmSync(dir,{recursive:true,force:true});});
 assert.equal((await call(url,'/api/health')).data.mode,'simulated');
 assert.equal((await call(url,'/api/game/open',{worldId:'world',runId:'run',day:1,period:0},{origin:'https://unrelated.example'})).status,403);
 const input={worldId:'world',runId:'run',day:1,period:0};assert.equal((await call(url,'/api/game/open',input)).status,200);
 const original=provider.call.bind(provider);provider.call=async()=>({text:'invalid json'});
 const broken=await call(url,'/api/game/utterance',{...input,requestId:'bad_reply',actorId:'c01',recipientId:'c02',conversationId:'convo',scene:'Street'});
 assert.equal(broken.status,500);assert.equal(app.engine.requests.size,0);
 provider.call=original;
 const good=await call(url,'/api/game/utterance',{...input,requestId:'good_reply',actorId:'c01',recipientId:'c02',conversationId:'convo',scene:'Street'});
 assert.equal(good.status,200);assert.ok(good.data.proposalId);
 assert.equal(JSON.parse(good.data.checkpoint).messages.length,0);
});
test('configuration is local-only, protected on disk, and never returned in status or provider errors',async t=>{
 const dir=mkdtempSync(join(tmpdir(),'z-memory-config-'));let responseCode=200;
 const upstream=http.createServer((req,res)=>{req.resume();req.on('end',()=>{res.writeHead(responseCode,{'content-type':'application/json'});res.end(responseCode===200?JSON.stringify({choices:[{message:{content:'OK'},finish_reason:'stop'}]}):JSON.stringify({error:'TEST_PRIVATE_CREDENTIAL'}));});});
 const endpoint=await listen(upstream);
 const configPath=join(dir,'private/model.json');const app=createMemoryServer({dbPath:join(dir,'test.sqlite'),configPath,providerFactory:s=>{const p=new ModelProvider(s,{allowTestEndpoint:true});p.keys.clear();p.config={...DEFAULT_CONFIG};return p;}});
 const url=await listen(app.server);t.after(async()=>{await close(app.server);await close(upstream);app.store.close();rmSync(dir,{recursive:true,force:true});});
 assert.equal((await call(url,'/api/health')).data.configured,false);
 const input={provider:'ark',baseUrl:endpoint,model:'test-model',apiKey:'TEST_PRIVATE_CREDENTIAL',apiType:'chat'};
 const saved=await call(url,'/api/config',input);assert.equal(saved.status,200);assert.ok(!JSON.stringify(saved.data).includes(input.apiKey));
 assert.equal(statSync(configPath).mode&0o777,0o600);assert.equal(JSON.parse(readFileSync(configPath,'utf8')).apiKey,input.apiKey);
 assert.equal((await call(url,'/api/config/test',{})).status,200);
 responseCode=429;const limited=await call(url,'/api/config/test',{});assert.equal(limited.status,502);assert.ok(!JSON.stringify(limited.data).includes(input.apiKey));
 assert.ok(!JSON.stringify(app.store.settings()).includes(input.apiKey));
});
