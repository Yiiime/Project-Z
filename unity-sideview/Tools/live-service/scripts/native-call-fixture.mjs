// Explicit local-only QA server: generated sine PCM, no provider or user data.
import http from 'node:http';
const id='00000000-0000-4000-8000-000000000001';
const server=http.createServer(async(req,res)=>{
 if(req.headers.authorization!=='Bearer call-qa'){res.writeHead(401);return res.end();}
 if(req.url==='/v1/live/sessions'){res.setHeader('Content-Type','application/json');return res.end(JSON.stringify({sessionId:id,executorLeaseId:id,epoch:1,lastServerSeq:0}));}
 if(req.url?.includes('/audio-stream/')){
  res.writeHead(200,{'Content-Type':'application/x-ndjson'});const send=e=>{if(!res.destroyed)res.write(JSON.stringify(e)+'\n');};
  const pcm=(offset,count)=>{const b=Buffer.alloc(count*2);for(let i=0;i<count;i++)b.writeInt16LE(Math.round(Math.sin((offset+i)/24000*440*2*Math.PI)*3000),i*2);return b.toString('base64');};
  send({type:'start',sampleRate:24000});send({type:'audio',index:0,data:pcm(0,14400)});send({type:'words',words:[{word:'今天',startTime:0,endTime:.4}]});
  const timer=setTimeout(()=>{send({type:'audio',index:1,data:pcm(14400,24000)});send({type:'words',words:[{word:'慢慢聊',startTime:.6,endTime:1.55}]});send({type:'done',durationMs:1600});res.end();},1200);
  res.once('close',()=>clearTimeout(timer));return;
 }
 res.writeHead(404);res.end();
});
server.listen(0,'127.0.0.1',()=>console.log('CALL_QA_URL=http://127.0.0.1:'+server.address().port));
for(const sig of ['SIGTERM','SIGINT'])process.once(sig,()=>{server.closeAllConnections();server.close();});
