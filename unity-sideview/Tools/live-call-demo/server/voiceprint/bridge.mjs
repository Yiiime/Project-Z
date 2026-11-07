import { randomUUID } from 'node:crypto';
import { WebSocketServer } from 'ws';
import { connectMmpg } from './volc-client.mjs';
import { buildVoiceprintSession } from './session.mjs';
import { issueRtcToken } from './rtc-token.mjs';
import { VoiceprintTurnGate } from './turn-gate.mjs';

export function attachVoiceprintBridge(server, { port, loadConfig, connect = connectMmpg, makeToken = issueRtcToken }) {
  const wss = new WebSocketServer({ noServer: true, maxPayload: 2048, perMessageDeflate: false });
  const sessions = new Set(); let pending = 0;
  server.on('upgrade', async (req, socket, head) => {
    if (req.url !== '/api/voiceprint/stream') return;
    const host = req.headers.host;
    const reject = status => socket.end(`HTTP/1.1 ${status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
    if (![`127.0.0.1:${port}`, `localhost:${port}`].includes(host) || req.headers.origin !== `http://${host}`) return reject('403 Forbidden');
    if (sessions.size + pending >= 1) return reject('429 Too Many Requests');
    pending++;
    try {
      const config = await loadConfig();
      if (config.mode !== 'voiceprint' || !config.validMode || config.missing.length) return reject('503 Service Unavailable');
      if (!socket.destroyed) wss.handleUpgrade(req, socket, head, client => begin(client, config));
    } catch { if (!socket.destroyed) reject('503 Service Unavailable'); }
    finally { pending--; }
  });
  function begin(client, config) {
    const suffix = randomUUID().replaceAll('-', ''), roomId = 'z_call_' + suffix, userId = 'p_' + suffix, agentId = 'z_' + suffix;
    let provider, closed = false, providerReady = false, joined = false, alive = true;
    const gate = new VoiceprintTurnGate({ holdMs: 300 });
    const emit = event => { if (client.readyState === 1 && client.bufferedAmount < 128000) client.send(JSON.stringify(event)); else close(); };
    const close = () => {
      if (closed) return; closed = true; gate.reset(); sessions.delete(close);
      clearTimeout(initial); clearTimeout(lifetime); clearInterval(heartbeat); clearInterval(flush);
      if (provider?.readyState === 1) { provider.send(JSON.stringify({ event_id: randomUUID(), type: 'session.close' })); provider.close(); setTimeout(() => provider.terminate(), 1000).unref(); }
      else if (provider && provider.readyState !== 3) provider.terminate();
      if (client.readyState < 2) client.close();
    };
    const fail = code => { if (!closed) emit({ type: 'error', code }); close(); };
    const initial = setTimeout(() => fail('voiceprint_connect_timeout'), 20000);
    const lifetime = setTimeout(() => fail('voiceprint_session_expired'), 30 * 60 * 1000);
    const heartbeat = setInterval(() => { if (!alive) return close(); alive = false; client.ping(); }, 10000);
    const forward = events => { for (const event of events) emit(event); };
    const flush = setInterval(() => forward(gate.flush()), 25);
    const ready = () => { if (joined && providerReady) { clearTimeout(initial); emit({ type: 'ready', provider: 'volc-mmpg', protection: 'voiceprint' }); } };
    sessions.add(close);
    client.on('pong', () => { alive = true; }); client.on('close', close); client.on('error', close);
    client.on('message', (data, binary) => {
      try {
        if (binary) return fail('voiceprint_invalid_control');
        const event = JSON.parse(data);
        if (event.type === 'joined' && !joined && Object.keys(event).length === 1) { joined = true; ready(); }
        else if (event.type === 'close' && Object.keys(event).length === 1) close();
        else fail('voiceprint_invalid_control');
      } catch { fail('voiceprint_invalid_control'); }
    });
    try {
      emit({ type: 'rtc', appId: config.rtcAppId, roomId, userId, token: makeToken({ appId: config.rtcAppId, appKey: config.rtcAppKey, roomId, userId }) });
      provider = connect(config);
      provider.on('open', () => { if (!closed) provider.send(JSON.stringify(buildVoiceprintSession(config, { roomId, userId, agentId }))); });
      provider.on('unexpected-response', (req, res) => { res.resume(); fail('voiceprint_permission'); });
      provider.on('error', () => fail('voiceprint_connection_failed'));
      provider.on('close', () => { if (!closed) fail('voiceprint_disconnected'); });
      provider.on('message', data => {
        if (closed) return;
        try {
          const event = JSON.parse(data);
          if (event.type === 'session.created') { providerReady = true; ready(); return; }
          if (event.type === 'error') return fail('voiceprint_provider_rejected');
          if (event.type === 'user.audio.transcription.started' && event.sv_active !== true) return fail('voiceprint_verification_inactive');
          forward(gate.receive(event));
        } catch { fail('voiceprint_invalid_response'); }
      });
    } catch { fail('voiceprint_connection_failed'); }
  }
  return { close() { for (const end of [...sessions]) end(); wss.close(); } };
}
