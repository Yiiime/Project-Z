import {randomUUID} from 'node:crypto';
import WebSocket,{WebSocketServer} from 'ws';
import {ASR_ENDPOINT,encodePacket,decodePacket,recognitionResults} from './protocol.mjs';

// Same-origin, loopback-only bridge. Audio and transcripts remain ephemeral.
export function attachAsrBridge(server,{port,loadConfig,WebSocketClass=WebSocket,endpoint=ASR_ENDPOINT}){
  const wss=new WebSocketServer({noServer:true,maxPayload:16000,perMessageDeflate:false});const sessions=new Set();let reservations=0;
  server.on('upgrade',async(req,socket,head)=>{
    if(req.url!=='/api/asr/stream')return;
    const host=req.headers.host,validHost=[`127.0.0.1:${port}`,`localhost:${port}`].includes(host);
    const reject=status=>{if(!socket.destroyed)socket.end(`HTTP/1.1 ${status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);};
    if(!validHost||req.headers.origin!==`http://${host}`||req.url!=='/api/asr/stream')return reject('403 Forbidden');
    if(sessions.size+reservations>=2)return reject('429 Too Many Requests');
    reservations++;
    try{const config=await loadConfig();if(!config.enabled)return reject('503 Service Unavailable');if(socket.destroyed)return;
      wss.handleUpgrade(req,socket,head,client=>connect(client,config));
    }catch{reject('503 Service Unavailable');}finally{reservations--;}
  });
  function connect(client,config){
    const id=randomUUID();let provider,closed=false,ready=false,finishing=false,sequence=0,bytes=0,pending=null,queued=[],endTimer;
    sessions.add(client);
    const emit=data=>{if(client.readyState===1)client.send(JSON.stringify({sessionId:id,...data}));};
    const close=()=>{if(closed)return;closed=true;clearTimeout(lifetime);clearTimeout(initial);clearTimeout(endTimer);clearInterval(heartbeat);queued=[];pending=null;sessions.delete(client);if(provider&&provider.readyState!==3)provider.terminate();if(client.readyState<2)client.close();};
    const fail=(code,status)=>{emit({type:'error',code,...(status?{status}:{})});close();};
    const lifetime=setTimeout(()=>fail('asr_session_timeout'),90000),initial=setTimeout(()=>fail('asr_connect_timeout'),10000);
    let alive=true;const heartbeat=setInterval(()=>{if(!alive)return close();alive=false;client.ping();},10000);client.on('pong',()=>{alive=true;});
    client.on('error',close);client.on('close',close);
    const send=packet=>{if(provider.readyState!==1||provider.bufferedAmount>128000)return fail('asr_backpressure');provider.send(packet);};
    const finish=()=>{if(!ready)return;if(!pending){emit({type:'end'});return close();}send(encodePacket(pending,{audio:true,last:true}));pending=null;endTimer=setTimeout(()=>fail('asr_final_timeout'),6000);};
    const audio=buffer=>{if(pending)send(encodePacket(pending,{audio:true}));pending=buffer;};
    client.on('message',(data,binary)=>{
      if(closed)return;
      if(binary){if(finishing||data.length<2||data.length%2||data.length>12800)return fail('asr_invalid_audio');bytes+=data.length;if(bytes>85*32000)return fail('asr_audio_limit');
        if(ready)audio(Buffer.from(data));else{if(queued.length>=10)return fail('asr_not_ready');queued.push(Buffer.from(data));}return;}
      let value;try{value=JSON.parse(data.toString());}catch{return fail('asr_invalid_control');}
      if(value.type!=='finish'||Object.keys(value).length!==1||finishing)return fail('asr_invalid_control');finishing=true;if(ready)finish();
    });
    const headers={'X-Api-Resource-Id':config.resource,'X-Api-Request-Id':id,'X-Api-Connect-Id':randomUUID(),'X-Api-Sequence':'-1',
      ...(config.apiKey?{'X-Api-Key':config.apiKey}:{'X-Api-App-Key':config.appId,'X-Api-Access-Key':config.accessKey})};
    provider=new WebSocketClass(endpoint,{headers,maxPayload:2*1024*1024,handshakeTimeout:8000,perMessageDeflate:false});
    provider.on('unexpected-response',(req,res)=>{res.resume();fail(res.statusCode===401||res.statusCode===403?'asr_permission':res.statusCode===429?'asr_quota':'asr_provider_unavailable',res.statusCode);});
    provider.on('error',()=>fail('asr_provider_unavailable'));
    provider.on('close',()=>{if(!closed)fail('asr_disconnected');});
    provider.on('open',()=>{if(closed)return;send(encodePacket({user:{uid:'local-demo'},audio:{format:'pcm',codec:'raw',rate:16000,bits:16,channel:1},request:{model_name:'bigmodel',enable_itn:true,enable_punc:true,enable_nonstream:true,show_utterances:true,result_type:'full',end_window_size:800}}));});
    provider.on('message',(data,binary)=>{
      if(closed)return;
      try{if(!binary)throw Error('asr_invalid_packet');const packet=decodePacket(data);
        if(!ready){ready=true;clearTimeout(initial);emit({type:'ready',provider:'doubao',model:'streaming-asr-2.0'});for(const chunk of queued)audio(chunk);queued=[];if(finishing)finish();}
        const results=recognitionResults(packet.body,{last:packet.last});if(results.length)emit({type:'result',revision:++sequence,results});
        if(packet.last){emit({type:'end'});close();}
      }catch(e){fail(e.providerCode?'asr_provider_rejected':'asr_invalid_packet',e.providerCode);}
    });
  }
  return {close(){for(const client of sessions)client.terminate();wss.close();}};
}
