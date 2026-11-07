import assert from 'node:assert/strict';
import {gzipSync,gunzipSync} from 'node:zlib';
import {Pcm16Resampler} from '../ui/pcm-resampler.js';
import {DoubaoRecognition} from '../ui/doubao-recognition.js';
import {encodePacket,decodePacket,recognitionResults} from '../server/asr/protocol.mjs';
import {loadAsrConfig,publicAsrConfig} from '../server/asr/config.mjs';
import fs from 'node:fs/promises';import os from 'node:os';import path from 'node:path';

const frame=(body,{last=false,compression=true}={})=>{const payload=Buffer.from(JSON.stringify(body)),data=compression?gzipSync(payload):payload,head=Buffer.alloc(12);head.set([0x11,last?0x93:0x91,compression?0x11:0x10,0]);head.writeInt32BE(last?-2:1,4);head.writeUInt32BE(data.length,8);return Buffer.concat([head,data]);};
const packet=encodePacket(Buffer.from([1,2,3,4]),{audio:true,last:true});assert.equal(packet[1],0x22);assert.deepEqual(gunzipSync(packet.subarray(8)),Buffer.from([1,2,3,4]));
const decoded=decodePacket(frame({result:{utterances:[{text:'今天',definite:true},{text:'想聊',definite:false}]}}));assert.deepEqual(recognitionResults(decoded.body).map(x=>x.final),[true,false]);
assert(decodePacket(frame({result:{text:'今天想聊'}},{last:true})).last);assert.throws(()=>decodePacket(Buffer.from([1,2])));assert.throws(()=>decodePacket(Buffer.concat([frame({}),Buffer.from([0])])));
for(const rate of [44100,48000]){const r=new Pcm16Resampler(rate);let frames=[];for(let offset=0;offset<rate;offset+=128)frames.push(...r.push(new Float32Array(Math.min(128,rate-offset)).fill(.5)));const last=r.flush();if(last)frames.push(last);assert.equal(frames.reduce((n,f)=>n+f.pcm.byteLength/2,0),16000);assert.equal(new DataView(frames[0].pcm).getInt16(0,true),16384);assert.equal(frames[0].rms,.5);}
const temp=await fs.mkdtemp(path.join(os.tmpdir(),'z-asr-config-'));try{await fs.writeFile(path.join(temp,'火山识别配置.env'),'VOLC_ASR_API_KEY=fixture-secret\n');const config=await loadAsrConfig(temp,{});assert(config.enabled);await fs.writeFile(path.join(temp,'火山识别配置.env'),'VOLC_ASR_API_KEY=\nVOLC_ASR_RESOURCE_ID=volc.seedasr.sauc.duration\nVOLC_ASR_APP_ID=\nVOLC_ASR_ACCESS_KEY=\n');assert.equal((await loadAsrConfig(temp,{})).enabled,false,'empty key cannot consume the next environment line');assert(!JSON.stringify(publicAsrConfig(config)).includes('fixture-secret'));assert.equal((await loadAsrConfig(temp,{VOLC_ASR_RESOURCE_ID:'invalid'})).enabled,false);}finally{await fs.rm(temp,{recursive:true,force:true});}
console.log('PASS ASR binary protocol, definite segments, streaming resampling and secret-free configuration');

const flush=async()=>{for(let i=0;i<20;i++)await Promise.resolve();};
let stopped=0,closed=0,mediaCalls=0,options,contexts=[];
const stream={getTracks:()=>[{stop(){stopped++;}}]};
class Context{constructor(){contexts.push(this);this.audioWorklet={addModule:async()=>{}};this.destination={};}resume(){return Promise.resolve();}close(){closed++;return Promise.resolve();}createMediaStreamSource(){return {connect(){},disconnect(){}};}createGain(){return {gain:{value:1},connect(){},disconnect(){}};}}
class Worklet{constructor(){this.port={postMessage:({type})=>{if(type==='flush')this.port.onmessage?.({data:{type:'flushed'}});}};}connect(){}disconnect(){}}
class Socket{static all=[];constructor(){Socket.all.push(this);this.bufferedAmount=0;this.sent=[];}send(x){this.sent.push(x);}close(){this.closed=true;}}
const create=(extra={})=>new DoubaoRecognition({fetchImpl:async()=>Response.json({enabled:true,provider:'doubao'}),mediaDevices:{async getUserMedia(o){mediaCalls++;options=o;return stream;}},Context,Worklet,WebSocketClass:Socket,location:{href:'http://127.0.0.1:4174/'},...extra});
let r=create(),events=[],ended=0;r.onresult=e=>events.push(e);r.onend=()=>ended++;r.start();await flush();let socket=Socket.all.at(-1);const emit=data=>socket.onmessage?.({data:JSON.stringify({sessionId:'test-session',...data})});
emit({type:'ready'});assert.equal(r.state,'listening');assert.equal(mediaCalls,1);assert(options.audio.echoCancellation&&options.audio.noiseSuppression);
emit({type:'result',revision:1,results:[{text:'今天',final:false}]});emit({type:'result',revision:1,results:[{text:'重复包',final:false}]});assert.equal(events.length,1);
r.push({pcm:new ArrayBuffer(6400),rms:.02,durationMs:200});r.stop();assert.equal(stopped,1);assert.equal(JSON.parse(socket.sent.at(-1)).type,'finish');
emit({type:'result',revision:2,results:[{text:'今天想聊天。',final:true}]});emit({type:'end'});assert.equal(events.length,2);assert(events.at(-1).results[0].isFinal);assert.equal(ended,1);assert(socket.closed);assert.equal(closed,1);
let permission;const priorMedia=mediaCalls;r=create({fetchImpl:async()=>Response.json({enabled:false,provider:'doubao'})});r.onerror=e=>permission=e.error;r.start();await flush();assert.equal(permission,'asr_not_configured');assert.equal(mediaCalls,priorMedia,'no recording before configuration');assert.equal(r.state,'closed');
let resolveCapture;r=create({mediaDevices:{getUserMedia:()=>new Promise(resolve=>resolveCapture=resolve)}});r.start();await flush();r.abort();const before=stopped;resolveCapture(stream);await flush();assert.equal(stopped,before+1,'late permission grant releases microphone');
r=create();r.start();await flush();socket=Socket.all.at(-1);emit({type:'ready'});let failure;r.onerror=e=>failure=e.error;emit({type:'error',code:'asr_permission'});assert.equal(failure,'asr_permission');assert.equal(r.state,'closed');
console.log('PASS ASR capture uses one processed stream, flushes final audio, deduplicates revisions, reports missing permissions and closes late microphone grants');
