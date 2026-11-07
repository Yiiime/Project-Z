// Explicit local smoke test: one synthetic Vivi sentence, no microphone capture.
import fs from 'node:fs/promises';import os from 'node:os';import path from 'node:path';import {execFileSync} from 'node:child_process';import WebSocket from 'ws';
const origin='http://127.0.0.1:'+String(process.env.Z_PREVIEW_PORT||4174),text='今天开了好多会，我想和你聊一会儿。';
const ws=new WebSocket(origin.replace('http:','ws:')+'/api/asr/stream',{origin,handshakeTimeout:10000});let readyResolve,readyReject,doneResolve,doneReject;let settled=false,finalText='',partials=0;
const ready=new Promise((a,b)=>{readyResolve=a;readyReject=b;}),done=new Promise((a,b)=>{doneResolve=a;doneReject=b;});done.catch(()=>{});
const fail=code=>{readyReject(Error(code));doneReject(Error(code));};
const timer=setTimeout(()=>fail('smoke_timeout'),45000);
ws.on('message',bytes=>{const event=JSON.parse(bytes);if(event.type==='ready')readyResolve();if(event.type==='result'){partials++;finalText=event.results.map(r=>r.text).join('');}if(event.type==='error')fail(event.code+':'+(event.status||''));if(event.type==='end'){settled=true;doneResolve();}});ws.on('error',()=>fail('socket_error'));ws.on('close',()=>{if(!settled)fail('closed_before_end');});ws.on('unexpected-response',(req,res)=>{res.resume();fail('http_'+res.statusCode);});
let dir;
try{
 await ready;
 const response=await fetch(origin+'/api/tts',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json'},body:JSON.stringify({requestId:crypto.randomUUID(),text,mood:'warm'})});if(!response.ok)throw Error('tts_http_'+response.status);
 const chunks=[];for(const line of (await response.text()).trim().split('\n')){const event=JSON.parse(line);if(event.type==='audio')chunks.push(Buffer.from(event.data,'base64'));if(event.type==='error')throw Error('tts_failed');}if(!chunks.length)throw Error('no_audio');
 dir=await fs.mkdtemp(path.join(os.tmpdir(),'z-asr-live-'));const mp3=path.join(dir,'input.mp3'),wav=path.join(dir,'input.wav');await fs.writeFile(mp3,Buffer.concat(chunks));execFileSync('/usr/bin/afconvert',['-f','WAVE','-d','LEI16@16000','-c','1',mp3,wav],{stdio:'pipe'});
 const audio=await fs.readFile(wav);let pcm;for(let pos=12;pos+8<=audio.length;){const tag=audio.toString('ascii',pos,pos+4),size=audio.readUInt32LE(pos+4);if(tag==='data'){pcm=audio.subarray(pos+8,pos+8+size);break;}pos+=8+size+(size%2);}if(!pcm?.length)throw Error('no_pcm');
 const samples=Buffer.concat([pcm,Buffer.alloc(32000)]);
 for(let at=0;at<samples.length;at+=6400){if(ws.readyState!==1)throw Error('stream_closed');ws.send(samples.subarray(at,at+6400));await new Promise(r=>setTimeout(r,200));}
 ws.send(JSON.stringify({type:'finish'}));await done;
 console.log(JSON.stringify({status:'ok',provider:'doubao',expected:text,recognized:finalText,resultPackets:partials,via:'localhost WebSocket proxy',audio:'synthetic Vivi test sentence'}));
}catch(e){console.log(JSON.stringify({status:'failed',code:e.message}));process.exitCode=1;}finally{clearTimeout(timer);ws.terminate();if(dir)await fs.rm(dir,{recursive:true,force:true});}
