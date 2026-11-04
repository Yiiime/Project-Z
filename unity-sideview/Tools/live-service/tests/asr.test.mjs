import test from 'node:test';
import assert from 'node:assert/strict';
import {gzipSync, gunzipSync} from 'node:zlib';
import {DoubaoAsrTurn, encodeAsrPacket, decodeAsrPacket} from '../server/voice/asr.mjs';
function response(body, last = false) {
  const payload = gzipSync(Buffer.from(JSON.stringify(body)));
  const head = Buffer.from([0x11, last ? 0x93 : 0x91, 0x11, 0]);
  const meta = Buffer.alloc(8); meta.writeInt32BE(last ? -1 : 1); meta.writeUInt32BE(payload.length, 4);
  return Buffer.concat([head, meta, payload]);
}
test('ASR帧编解码支持gzip、序号和最终标志', () => {
  const packet = encodeAsrPacket({audio: {rate: 16000}});
  assert.equal(packet[1], 0x10); assert.deepEqual(JSON.parse(gunzipSync(packet.subarray(8))), {audio: {rate: 16000}});
  const decoded = decodeAsrPacket(response({result: {text: '你好'}}, true));
  assert.equal(decoded.last, true); assert.equal(decoded.body.result.text, '你好');
  assert.throws(() => decodeAsrPacket(response({}).subarray(0, 8)), /truncated/);
});
test('ASR完整一轮：partial不会当final，最后音频带结束标志，采样率不匹配拒绝', async () => {
  let callbacks, sent = [], partial = [], finals = [], errors = [];
  const turn = new DoubaoAsrTurn({apiKey: 'fixture-not-real', uid: 'g',
    connect: async options => {callbacks = options; return {send: x => sent.push(x), close() {}};},
    onPartial: x => partial.push(x), onFinal: x => finals.push(x), onError: x => errors.push(x.message)});
  await turn.open(); assert.ok(callbacks.headers['X-Api-Request-Id']);
  assert.throws(() => turn.push(Buffer.alloc(3200), 48000), /16000hz/);
  turn.push(new Int16Array(1600), 16000); turn.push(Buffer.alloc(3200));
  callbacks.onMessage(response({result: {text: '你好'}})); assert.deepEqual(partial, ['你好']); assert.equal(finals.length, 0);
  turn.finish(); assert.equal(sent.at(-1)[1], 0x22);
  callbacks.onMessage(response({result: {text: '你好呀'}}, true));
  assert.deepEqual(finals, ['你好呀']); assert.equal(errors.length, 0); assert.equal(turn.apiKey, null);
  callbacks.onMessage(response({result: {text: '旧结果'}}, true)); assert.equal(finals.length, 1);
});
test('ASR异常关闭不伪造句末，业务错误不会转成用户发言', async () => {
  let callbacks, errors = [], finals = [];
  const turn = new DoubaoAsrTurn({apiKey: 'fixture', uid: 'g', connect: async o => {callbacks = o; return {send() {}, close() {}};},
    onPartial() {}, onFinal: x => finals.push(x), onError: e => errors.push(e.message)});
  await turn.open(); callbacks.onMessage(response({code: 45000001})); callbacks.onClose();
  assert.deepEqual(errors, ['asr_business_error']); assert.deepEqual(finals, []);
});
test('ASR错误帧不泄露供应商原文', () => {
  const data = Buffer.from('private-provider-detail'), meta = Buffer.alloc(8);
  meta.writeUInt32BE(45000001); meta.writeUInt32BE(data.length, 4);
  assert.throws(() => decodeAsrPacket(Buffer.concat([Buffer.from([0x11, 0xf0, 0, 0]), meta, data])), /^Error: asr_provider_error:45000001$/);
});

test('PCM upload bridge waits for the final result and respects cancellation',async()=>{
 const {transcribePcm}=await import('../server/voice/index.mjs');
 const result=await transcribePcm(Buffer.alloc(3200),{apiKey:'fixture',uid:'turn',connect:async callbacks=>({send:bytes=>{
  if(bytes[1]===0x22){callbacks.onMessage(response({result:{text:'中间文本'}}));callbacks.onMessage(response({result:{text:'最终文本'}},true));}
 },close(){}})});
 assert.equal(result.text,'最终文本');
 const controller=new AbortController();controller.abort();
 await assert.rejects(()=>transcribePcm(Buffer.alloc(3200),{apiKey:'fixture',uid:'turn',signal:controller.signal,connect:async()=>{throw Error('must_not_connect');}}),/asr_cancelled/);
});
