import WebSocket from 'ws';
import {DoubaoAsrTurn, createWsConnector} from './asr.mjs';

export async function transcribePcm(bytes, {apiKey, uid, signal, connect = createWsConnector(WebSocket)} = {}) {
  if (!apiKey) throw Error('asr_not_configured');
  if (!Buffer.isBuffer(bytes) || bytes.length < 3200 || bytes.length > 640000 || bytes.length % 2) throw Error('invalid_audio');
  let finish, fail;
  const result = new Promise((resolve, reject) => { finish = resolve; fail = reject; });
  const turn = new DoubaoAsrTurn({apiKey, uid, connect, maxDurationMs: 20000,
    onPartial() {}, onFinal: finish, onError: () => fail(Error('asr_unavailable'))});
  const abort = () => {turn.close();fail(Error('asr_cancelled'));};
  signal?.addEventListener('abort', abort, {once: true});
  result.catch(() => {});
  try {
    if (signal?.aborted) throw Error('asr_cancelled');
    await turn.open();
    for (let offset = 0; offset < bytes.length; offset += 16000) {
      if (signal?.aborted) throw Error('asr_cancelled');
      turn.push(bytes.subarray(offset, offset + 16000));
      // Bounded queue: allow the WebSocket sender to drain between audio chunks.
      await new Promise(resolve => setTimeout(resolve, 12));
    }
    turn.finish();
    const text = await result;
    if (!text || text.length > 360) throw Error('asr_text_unavailable');
    return {text};
  } finally {signal?.removeEventListener('abort', abort);turn.close();}
}
