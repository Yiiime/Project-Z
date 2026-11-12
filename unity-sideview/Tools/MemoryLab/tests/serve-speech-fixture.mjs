// Isolated Unity playback fixture. No model or TTS credentials are read.
import { createServer } from "../server.mjs";
import { SpeechService } from "../lib/speech.mjs";
const tone=Buffer.alloc(24000*2*4);
for(let i=0;i<tone.length/2;i++)tone.writeInt16LE(Math.round(Math.sin(i*2*Math.PI*220/24000)*1800),i*2);
const speech=new SpeechService({config:()=>({enabled:true,apiKey:"fixture",voices:new Proxy({},{get:()=>({speaker:"fixture",resourceId:"seed-tts-2.0"})})}),
  fetch:async(_url,{body,signal})=>{
    const text=JSON.parse(body).req_params.text;
    await new Promise((done,fail)=>{const timer=setTimeout(done,text.includes("延迟")?3000:800);signal.addEventListener("abort",()=>{clearTimeout(timer);fail(Error("cancelled"));},{once:true});});
    if(text.includes("故障"))return new Response("unavailable",{status:503});
    return new Response('data: '+JSON.stringify({code:0,data:tone.toString("base64")})+'\n\ndata: {"code":20000000}\n\n',{headers:{"content-type":"text/event-stream"}});
  }});
const app=createServer({dbPath:":memory:",backgroundEnabled:false,speech,
  providerFactory:()=>({mode:"simulated-speech-fixture",config:{topK:6},publicConfig:()=>({configured:true})}),
  decide:c=>{
    const a=c.available_actions.find(a=>a.type==="wait");
    return {dialogue:c.environment.intent_only?"":"语音验证："+(c.interaction.input||"你好。"),
      action:{action_id:a.id,type:a.type,target:a.target,parameters:a.parameters,description:"保持对话"},
      info:structuredClone(c.character_state.info)};
  }});
app.server.listen(Number(process.env.Z_TTS_FIXTURE_PORT||4433),"127.0.0.1",()=>console.log("Isolated speech fixture ready"));
for(const signal of ["SIGINT","SIGTERM"])process.on(signal,()=>{speech.cancel();app.server.close(()=>{app.store.close();process.exit(0);});});
