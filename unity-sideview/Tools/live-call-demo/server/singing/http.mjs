import fs from 'node:fs/promises';
import path from 'node:path';
const json=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'}});
export async function singingHttp(request,service,{owner='local-preview-owner'}={}){
  const url=new URL(request.url),p=url.pathname;
  if(p!=='/singing'&&!p.startsWith('/api/singing/'))return null;
  try{
    // Local single-owner demo only. Do not expose this adapter on a public host.
    if(!['127.0.0.1','localhost'].includes(url.hostname))return json({error:'local_only'},403);
    if(request.headers.get('Sec-Fetch-Site')==='cross-site')return json({error:'invalid_origin'},403);
    if(request.method==='POST'&&(request.headers.get('Origin')!==url.origin||!request.headers.get('Content-Type')?.startsWith('application/json')))return json({error:'invalid_origin_or_content_type'},403);
    if(p==='/singing'&&request.method==='GET')return new Response(await fs.readFile(path.join(service.root,'server/singing/manage.html'),'utf8'),{headers:{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store','Content-Security-Policy':"default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; media-src 'self'; connect-src 'self'; frame-ancestors 'none'"}});
    if(p==='/api/singing/state'&&request.method==='GET'){service.expirePlaybacks();return json(service.overview(owner));}
    const audio=p.match(/^\/api\/singing\/assets\/([a-zA-Z0-9-]+)$/);
    if(audio&&request.method==='GET')return new Response(await service.audio(audio[1],{allowUnreviewed:url.searchParams.get('review')==='1'}),{headers:{'Content-Type':'audio/wav','Cache-Control':'private, no-store','Cross-Origin-Resource-Policy':'same-origin'}});
    if(request.method!=='POST')return json({error:'not_found'},404);
    const raw=await request.text();if(raw.length>8192)return json({error:'body_too_large'},413);const body=JSON.parse(raw);
    if(p==='/api/singing/tasks')return json(service.submit({...body,owner}),202);
    const route=p.match(/^\/api\/singing\/tasks\/([a-zA-Z0-9-]+)\/(review|retry|clarify|cancel|play)$/);
    if(route){const id=route[1];switch(route[2]){
      case 'review':return json(service.review(id,owner,body));
      case 'retry':return json(service.retry(id,owner,body));
      case 'clarify':return json(service.clarify(id,owner,body.text));
      case 'cancel':return json(service.cancel(id,owner));
      case 'play':return json(service.reserve(id,owner,body));
    }}
    const receipt=p.match(/^\/api\/singing\/playbacks\/([a-zA-Z0-9-]+)$/);
    if(receipt)return json(service.receipt(receipt[1],owner,body));
    return json({error:'not_found'},404);
  }catch(error){const code=/^[a-z_]+$/.test(error.message)?error.message:'singing_request_failed';return json({error:code},409);}
}
