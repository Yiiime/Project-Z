// PAGE, handleChat and handleTts are joined by the build. Secrets stay server-side.
function replyJson(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' } });
}
export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const visitor = await demoVisitorSession(request, env);
    request = visitor.request;
    const visitorHeaders = visitor.setCookie ? {'Set-Cookie':visitor.setCookie} : {};
    const singing = await handleCloudSinging(request, env, ctx);
    if(singing.response){if(visitor.setCookie)singing.response.headers.set('Set-Cookie',visitor.setCookie);return singing.response;}
    if(singing.seedAudio)return servePublicAsset(request,'/audio/songs/wanan-yanrenzhong-vivi2-chorus.wav');

    if ((request.method === 'GET' || request.method === 'HEAD') && url.pathname === '/performance') return new Response(request.method === 'HEAD' ? null : PERFORMANCE_PAGE, { headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store', ...visitorHeaders } });
    if(url.pathname.startsWith('/live2d/')||url.pathname.startsWith('/audio/songs/'))return servePublicAsset(request,url.pathname);
    if(url.pathname==='/api/performance'||url.pathname.startsWith('/api/performance/')){
      try{return await handlePerformance(request,env);}catch{return replyJson({error:'performance_unavailable',message:'动作分析暂时不可用，请稍后重试。'},503);}
    }
    if (url.pathname === '/api/agent' || url.pathname.startsWith('/api/agent/')) {
      try { return await handleAgent(request, env, singing.context ? {singingContext:()=>singing.context} : {}); }
      catch { return replyJson({ error: 'agent_unavailable', message: '主 Agent 暂时不可用，保留当前互动。' }, 503); }
    }
    if (url.pathname === '/api/chat' || url.pathname.startsWith('/api/chat/')) {
      try { return await handleChat(request, env); }
      catch { return replyJson({ error: 'chat_unavailable', message: '文字聊天暂时无法使用，请稍后再试。' }, 503); }
    }
    if (url.pathname === '/api/tts' || url.pathname.startsWith('/api/tts/')) {
      try { return await handleTts(request, env); }
      catch { return replyJson({ error: 'tts_unavailable', message: '语音暂时连接不上，先用文字陪你。' }, 503); }
    }
    if (request.method === 'GET' && url.pathname === '/api/health') {
      return replyJson({ status: 'ok', mode: 'text-and-voice', provider: 'DeepSeek', model: 'deepseek-flash', voice: 'Vivi 2.0', chatConfigured: typeof env.DEEPSEEK_API_KEY === 'string' && Boolean(env.DEEPSEEK_API_KEY.trim()) && !/[\r\n]/.test(env.DEEPSEEK_API_KEY.trim()), voiceConfigured: typeof env.VOLC_TTS_API_KEY === 'string' && Boolean(env.VOLC_TTS_API_KEY.trim()) && !/[\r\n]/.test(env.VOLC_TTS_API_KEY.trim()) });
    }
    if ((request.method === 'GET' || request.method === 'HEAD') && ['/', '/index.html'].includes(url.pathname)) {
      return new Response(request.method === 'HEAD' ? null : PAGE, { headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'same-origin', ...visitorHeaders } });
    }
    if (request.method === 'GET' && url.pathname === '/favicon.ico') return new Response(null, { status: 204 });
    return new Response('Not found', { status: 404 });
  },
};
