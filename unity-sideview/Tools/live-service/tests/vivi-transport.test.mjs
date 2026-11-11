import test from 'node:test';
import assert from 'node:assert/strict';
import {once} from 'node:events';
import {WebSocketServer} from 'ws';
import {connectVivi} from '../scripts/vivi-transport.mjs';
test('a 127-byte transcription followed by fragmented audio survives a ping and a large audio message',async t=>{
 const server=new WebSocketServer({port:0,host:'127.0.0.1'});await once(server,'listening');
 const socket=connectVivi('ws://127.0.0.1:'+server.address().port,'fixture');
 t.after(()=>{socket.terminate();for(const c of server.clients)c.terminate();server.close();});
 const got=[];socket.on('message',(data,binary)=>{assert(!binary);got.push(data.length);});
 const done=once(socket,'close');
 server.on('connection',c=>{c.send('x'.repeat(127));c.send('b'.repeat(64),{fin:false});c.ping();c.send('c'.repeat(64),{fin:true});
  c.send('a'.repeat(5*1024*1024));c.close(1000);});
 await done;assert.deepEqual(got,[127,128,5*1024*1024]);
});
