import assert from 'node:assert/strict';
import worker from '../dist/server/index.js';
const origin='https://demo.test',env={Z_PUBLIC_DEMO:'true',DEEPSEEK_API_KEY:'fixture-only',VOLC_TTS_API_KEY:'fixture-only'};
const page=()=>new Request(origin+'/');
const post=(cookie,extra={})=>new Request(origin+'/api/tts',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json',...(cookie?{Cookie:cookie}:{}),...extra},body:JSON.stringify({requestId:crypto.randomUUID(),text:'你好。',mood:'warm'})});
let calls=0;const originalFetch=globalThis.fetch;
globalThis.fetch=async()=>{calls++;return new Response('',{status:503});};
try{
 const first=await worker.fetch(page(),env),set=first.headers.get('Set-Cookie');
 assert.equal(first.status,200);assert(set.includes('HttpOnly'));assert(set.includes('Secure'));assert(set.includes('SameSite=Lax'));assert(!set.includes(env.DEEPSEEK_API_KEY));
 const cookie=set.split(';')[0],second=(await worker.fetch(page(),env)).headers.get('Set-Cookie').split(';')[0];assert.notEqual(cookie,second);
 const again=await worker.fetch(new Request(origin+'/',{headers:{Cookie:cookie}}),env);assert.equal(again.headers.get('Set-Cookie').split('=')[1].split('.')[0],cookie.split('=')[1].split('.')[0], 'renewal retains the visitor identity');
 for(const badCookie of [undefined,cookie.slice(0,-1)+(cookie.endsWith('0')?'1':'0')]){const r=await worker.fetch(post(badCookie),env);assert.equal(r.status,401);}
 assert.equal(calls,0);
 const accepted=await worker.fetch(post(cookie),env);assert.equal(accepted.status,200);await accepted.text();assert.equal(calls,1);
 const crossOrigin=await worker.fetch(post(cookie,{Origin:'https://other.test'}),env);assert.equal(crossOrigin.status,403);assert.equal(calls,1);
 const privateEnv={...env,Z_PUBLIC_DEMO:'false'};assert.equal((await worker.fetch(page(),privateEnv)).headers.get('Set-Cookie'),null);assert.equal((await worker.fetch(post(cookie),privateEnv)).status,401);
 const loggedIn=await worker.fetch(new Request(origin+'/',{headers:{'oai-authenticated-user-id':'real-user'}}),env);assert.equal(loggedIn.headers.get('Set-Cookie'),null);
 const differentSite=await worker.fetch(new Request('https://different.test/api/tts',post(cookie)),env);assert.equal(differentSite.status,401);
 const rotated=await worker.fetch(post(cookie),{...env,DEEPSEEK_API_KEY:'rotated-fixture'});assert.equal(rotated.status,401);
 console.log('PASS anonymous session creation, independent visitors, reuse, tamper/no-cookie rejection, provider reachability, same-origin enforcement, private mode, authenticated identity, cross-site isolation and key rotation.');
}finally{globalThis.fetch=originalFetch;}
