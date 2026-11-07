import assert from 'node:assert/strict';import http from 'node:http';import {EventEmitter} from 'node:events';import {once} from 'node:events';import {gzipSync,gunzipSync} from 'node:zlib';import WebSocket from 'ws';
import {attachAsrBridge} from '../server/asr/bridge.mjs';
const serverFrame=(body,last=false)=>{const data=gzipSync(Buffer.from(JSON.stringify(body))),header=Buffer.alloc(12);header.set([0x11,last?0x93:0x91,0x11,0]);header.writeInt32BE(last?-2:1,4);header.writeUInt32BE(data.length,8);return Buffer.concat([header,data]);};
class Provider extends EventEmitter{
 static instances=[];constructor(url,options){super();this.readyState=1;this.bufferedAmount=0;this.sent=[];this.options=options;Provider.instances.push(this);queueMicrotask(()=>this.emit('open'));}
 send(packet){this.sent.push(packet);if(packet[1]>>4===1){assert.equal(JSON.parse(gunzipSync(packet.subarray(8))).request.enable_nonstream,true);queueMicrotask(()=>this.emit('message',serverFrame({}),true));}
 else if(packet[1]&2)queueMicrotask(()=>this.emit('message',serverFrame({result:{utterances:[{text:'今天想聊聊天',definite:true}]}},true),true));}
 terminate(){this.readyState=3;this.emit('close');}
}
const server=http.createServer();server.listen(0,'127.0.0.1');await once(server,'listening');const port=server.address().port;
const bridge=attachAsrBridge(server,{port,loadConfig:async()=>({enabled:true,apiKey:'server-only-secret',resource:'volc.seedasr.sauc.duration'}),WebSocketClass:Provider});
const clients=[];try{
 const wrong=new WebSocket(`ws://127.0.0.1:${port}/api/asr/stream`,{origin:'https://untrusted.test'});clients.push(wrong);wrong.on('error',()=>{});const [,res]=await once(wrong,'unexpected-response');assert.equal(res.statusCode,403);res.resume();wrong.terminate();
 const ws=new WebSocket(`ws://127.0.0.1:${port}/api/asr/stream`,{origin:`http://127.0.0.1:${port}`});clients.push(ws);const messages=[];ws.on('message',b=>messages.push(JSON.parse(b)));await once(ws,'message');assert.equal(messages[0].type,'ready');
 ws.send(Buffer.alloc(6400,1));ws.send(Buffer.alloc(6400,2));ws.send(JSON.stringify({type:'finish'}));await once(ws,'close');
 assert(messages.some(m=>m.type==='result'&&m.results[0].text==='今天想聊聊天'));assert.equal(messages.at(-1).type,'end');assert(!JSON.stringify(messages).includes('server-only-secret'));
 const upstream=Provider.instances.at(-1);assert.equal(upstream.options.headers['X-Api-Key'],'server-only-secret');assert.equal(upstream.sent.length,3);assert.equal(upstream.sent.at(-1)[1],0x22);assert.equal(upstream.readyState,3);
 console.log('PASS ASR loopback bridge: rejects cross-origin, streams PCM, sends real final audio, delivers final result before close and never leaks credentials');
}finally{for(const client of clients)client.terminate();bridge.close();server.close();await once(server,'close');}
