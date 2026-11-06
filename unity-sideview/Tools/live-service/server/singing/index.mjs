// Host tools use the existing authenticated service. No review or playback ledger API.
export async function singingRequest(service,owner,route,body){
 const reply=value=>Response.json(value,{headers:{'Cache-Control':'no-store'}});
 try{
  const p=new URL(route,'http://127.0.0.1').pathname;
  if(p==='/api/singing/state'&&!body)return reply(service.overview(owner));
  const asset=p.match(/^\/api\/singing\/assets\/([a-zA-Z0-9-]+)$/);
  if(asset&&!body){const state=service.read();if(!state.assets.some(a=>a.id===asset[1]&&a.approved===true)&&!state.tasks.some(t=>t.owner===owner&&t.assetId===asset[1]&&['ready','scheduled'].includes(t.status)))return Response.json({error:'not_found'},{status:404});return new Response(await service.audio(asset[1]),{headers:{'Content-Type':'audio/wav','Cache-Control':'no-store'}});}
  if(p==='/api/singing/tasks'&&body)return reply(service.submit({...body,owner}));
  const task=p.match(/^\/api\/singing\/tasks\/([a-zA-Z0-9-]+)\/(retry|clarify|cancel|play)$/);
  if(task&&body){const [,id,action]=task;
   if(action==='retry')return reply(service.retry(id,owner,body));
   if(action==='clarify')return reply(service.clarify(id,owner,body.text));
   if(action==='cancel')return reply(service.cancel(id,owner));
   if(action==='play')return reply(await service.reserve(id,owner));
  }
  return Response.json({error:'not_found'},{status:404});
 }catch(e){return Response.json({error:/^[a-z_]+$/.test(e.message)?e.message:'singing_request_failed'},{status:409});}
}
