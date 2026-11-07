// Anonymous demo visitors have a signed, site-local quota identity, not an
// account. Keep Sites' authenticated identity when one is supplied by dispatch.
const DEMO_COOKIE = 'z_demo_visitor';
const DEMO_TTL = 30 * 24 * 60 * 60;
const demoEncoder = new TextEncoder();
async function demoVisitorKey(env) {
  const secret = env.DEEPSEEK_API_KEY || env.VOLC_TTS_API_KEY;
  if (typeof secret !== 'string' || !secret.trim()) return null;
  return crypto.subtle.importKey('raw', demoEncoder.encode('z-demo-visitor-v1:' + secret.trim()), {name:'HMAC',hash:'SHA-256'}, false, ['sign','verify']);
}
async function demoVisitorSession(request, env) {
  if (env.Z_PUBLIC_DEMO !== 'true' || request.headers.get('oai-authenticated-user-id')?.trim()) return {request};
  const url = new URL(request.url);
  const page = ['GET','HEAD'].includes(request.method) && ['/','/index.html','/performance','/singing'].includes(url.pathname);
  if (!page && !url.pathname.startsWith('/api/')) return {request};
  const key = await demoVisitorKey(env);
  if (!key) return {request};
  const cookie = (request.headers.get('Cookie') || '').split(';').map(s=>s.trim()).find(s=>s.startsWith(DEMO_COOKIE+'='))?.slice(DEMO_COOKIE.length+1);
  let id;
  const match = cookie?.match(/^([0-9a-f-]{36})\.(\d{10})\.([0-9a-f]{64})$/);
  const now = Math.floor(Date.now()/1000);
  if (match && Number(match[2]) > now && Number(match[2]) <= now+DEMO_TTL) {
    const bytes = Uint8Array.from(match[3].match(/../g), hex=>parseInt(hex,16));
    if (await crypto.subtle.verify('HMAC',key,bytes,demoEncoder.encode(url.origin+'|'+match[1]+'.'+match[2]))) id=match[1];
  }
  let setCookie;
  if (page) {
    id=id||crypto.randomUUID();
    const value=id+'.'+(now+DEMO_TTL);
    const signature=[...new Uint8Array(await crypto.subtle.sign('HMAC',key,demoEncoder.encode(url.origin+'|'+value)))].map(b=>b.toString(16).padStart(2,'0')).join('');
    setCookie=`${DEMO_COOKIE}=${value}.${signature}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${DEMO_TTL}${url.protocol==='https:'?'; Secure':''}`;
  }
  if (!id) return {request};
  // Internal adapter compatibility only. This header is never returned to the
  // browser or forwarded to a provider, and grants no account/data permissions.
  const headers=new Headers(request.headers);
  headers.set('oai-authenticated-user-id','demo-visitor:'+id);
  return {request:new Request(request,{headers}),setCookie};
}
