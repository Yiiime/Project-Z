// Authenticated native clients and the host agent share one persistent song service.
export function singingRequest(service, owner, route, body) {
  const reply = value => Response.json(value, {headers: {'Cache-Control': 'no-store'}});
  return (async () => {
    try {
      const url = new URL(route, 'http://127.0.0.1');
      const p = url.pathname;
      if (p === '/api/singing/state' && !body) {
        service.expirePlaybacks();
        return reply(service.overview(owner));
      }
      const asset = p.match(/^\/api\/singing\/assets\/([a-zA-Z0-9-]+)$/);
      if (asset && !body) return new Response(await service.audio(asset[1], {
        allowUnreviewed: url.searchParams.get('review') === '1',
      }), {headers: {'Content-Type': 'audio/wav', 'Cache-Control': 'no-store'}});
      if (!body) return Response.json({error: 'not_found'}, {status: 404});
      if (p === '/api/singing/tasks') return reply(service.submit({...body, owner}));
      const task = p.match(/^\/api\/singing\/tasks\/([a-zA-Z0-9-]+)\/(review|retry|clarify|cancel|play)$/);
      if (task) {
        const [, id, action] = task;
        if (action === 'review') return reply(service.review(id, owner, body));
        if (action === 'retry') return reply(service.retry(id, owner, body));
        if (action === 'clarify') return reply(service.clarify(id, owner, body.text));
        if (action === 'cancel') return reply(service.cancel(id, owner));
        if (action === 'play') return reply(service.reserve(id, owner, body));
      }
      const playback = p.match(/^\/api\/singing\/playbacks\/([a-zA-Z0-9-]+)$/);
      if (playback) return reply(service.receipt(playback[1], owner, body));
      return Response.json({error: 'not_found'}, {status: 404});
    } catch (error) {
      const code = /^[a-z_]+$/.test(error.message) ? error.message : 'singing_request_failed';
      return Response.json({error: code}, {status: 409});
    }
  })();
}
