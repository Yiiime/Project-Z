// Isolated Unity streaming fixture. No model or TTS credentials are read.
import { createServer } from "../server.mjs";
import { SpeechService } from "../lib/speech.mjs";
const tone=Buffer.alloc(24000*2*4);
for(let i=0;i<tone.length/2;i++)tone.writeInt16LE(Math.round(Math.sin(i*2*Math.PI*220/24000)*1800),i*2);
const sleep=(ms,signal)=>new Promise((done,fail)=>{
  const abort=()=>{clearTimeout(timer);fail(Error("cancelled"));};
  const timer=setTimeout(()=>{signal.removeEventListener("abort",abort);done();},ms);
  signal.addEventListener("abort",abort,{once:true});if(signal.aborted)abort();
});
const speech=new SpeechService({config:()=>({enabled:true,apiKey:"fixture",voices:new Proxy({},{get:()=>({speaker:"fixture",resourceId:"seed-tts-2.0"})})}),
  fetch:async(_url,{body,signal})=>{
    const text=JSON.parse(body).req_params.text;
    await sleep(text.includes("延迟")?3000:250,signal);
    if(text.includes("故障"))return new Response("unavailable",{status:503});
    const encoder=new TextEncoder();
    return new Response(new ReadableStream({async start(c){
      const emit=p=>c.enqueue(encoder.encode('data: '+JSON.stringify(p)+'\n\n'));
      try{
        const words=[...text].map((word,i)=>({word,startTime:i*4/[...text].length,endTime:(i+1)*4/[...text].length}));
        for(let offset=0;offset<tone.length;offset+=24000){
          if(offset===24000)await sleep(text.includes("卡顿")?1000:180,signal);
          else if(offset>0)await sleep(180,signal);
          emit({code:0,data:tone.subarray(offset,offset+24000).toString("base64")});
          if(offset===24000)emit({code:0,sentence:{text,words}});
        }
        emit({code:20000000});c.close();
      }catch(e){try{c.error(e);}catch{}}
    }}),{headers:{"content-type":"text/event-stream"}});
  }});
const app=createServer({dbPath:":memory:",backgroundEnabled:false,speech,
  providerFactory:()=>({mode:"simulated-speech-fixture",config:{topK:6},publicConfig:()=>({configured:true})}),
  decide:c=>{
    const a=c.available_actions.find(a=>a.type==="wait");
    return {dialogue:c.environment.intent_only?"":"语音验证："+(c.interaction.input||"你好。")+"这是检查气泡随文字逐渐变长的台词，不应该提前空出两行。",
      action:{action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:"保持对话"},
      info:structuredClone(c.character_state.info)};
  }});
app.server.listen(Number(process.env.Z_TTS_FIXTURE_PORT||4433),"127.0.0.1",()=>console.log("Isolated speech fixture ready"));
for(const signal of ["SIGINT","SIGTERM"])process.on(signal,()=>{speech.cancel();app.server.close(()=>{app.store.close();process.exit(0);});});
if(process.env.Z_TTS_FIXTURE_TRACE==='1')app.server.on('request',(req,res)=>{
  let body='';req.on('data',b=>{if(body.length<200000)body+=b;});
  res.on('finish',()=>{try{
    const p=JSON.parse(body||'{}');if(!p.worldId)return;
    const w=app.engine.world(p.worldId);
    console.log(JSON.stringify({at:Date.now(),path:req.url,actor:p.actorId,kind:p.eventData?.kind,seq:p.eventData?.seq,focus:w.focus?.actor||null,epoch:w.epoch,status:res.statusCode}));
  }catch{}});
});
