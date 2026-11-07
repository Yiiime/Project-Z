import { fileURLToPath } from 'node:url';
import fs from 'node:fs/promises';
import path from 'node:path';
import { loadVoiceprintConfig } from '../server/voiceprint/config.mjs';
import { rtcApi, connectMmpg, safeProviderCode } from '../server/voiceprint/volc-client.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const config = await loadVoiceprintConfig(root);
const report = { at: new Date().toISOString(), microphoneUsed: false };
try {
  const { status, data } = await rtcApi(config, 'ListVoicePrint', {});
  const error = data?.ResponseMetadata?.Error;
  report.rtc = { httpStatus: status, ok: status === 200 && !error,
    ...(error ? { code: safeProviderCode(error.Code) } : { registeredVoiceprints: data?.Result?.TotalCount ?? null }) };
} catch (error) { report.rtc = { ok: false, code: safeProviderCode(error.cause?.code || error.code || error.name) }; }
report.mmpg = await new Promise(resolve => {
  let socket, finished = false;
  const finish = result => { if (finished) return; finished = true; clearTimeout(timer); socket?.terminate(); resolve(result); };
  const timer = setTimeout(() => finish({ ok: false, code: 'timeout' }), 14000);
  try {
    socket = connectMmpg(config);
    socket.on('open', () => finish({ ok: true, stage: 'authenticated_websocket_only' }));
    socket.on('unexpected-response', (req, res) => { res.resume(); finish({ ok: false, httpStatus: res.statusCode }); });
    socket.on('error', error => finish({ ok: false, code: safeProviderCode(error.code || error.name) }));
    socket.on('close', () => finish({ ok: false, code: 'closed' }));
  } catch (error) { finish({ ok: false, code: safeProviderCode(error.message) }); }
});
await fs.mkdir(path.join(root, 'outputs'), { recursive: true });
await fs.writeFile(path.join(root, 'outputs/voiceprint-permission-validation.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
if (!report.rtc.ok || !report.mmpg.ok) process.exitCode = 1;
