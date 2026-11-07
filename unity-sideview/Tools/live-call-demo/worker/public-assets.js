// PUBLIC_ASSETS is an explicit build-time allowlist, never a filesystem proxy.
export function servePublicAsset(request,pathname){
  const asset=PUBLIC_ASSETS[pathname];
  if(!Object.hasOwn(PUBLIC_ASSETS,pathname)||!asset)return new Response('Not found',{status:404});
  if(!['GET','HEAD'].includes(request.method))return new Response('Method not allowed',{status:405,headers:{Allow:'GET, HEAD'}});
  const headers={'Content-Type':asset.type,'Cache-Control':'private, no-cache',ETag:asset.etag,'X-Content-Type-Options':'nosniff','Cross-Origin-Resource-Policy':'same-origin'};
  if(request.headers.get('If-None-Match')===asset.etag)return new Response(null,{status:304,headers});
  headers['Content-Length']=String(asset.size);
  const body=request.method==='HEAD'?null:Uint8Array.from(atob(asset.body),char=>char.charCodeAt(0));
  return new Response(body,{headers});
}
