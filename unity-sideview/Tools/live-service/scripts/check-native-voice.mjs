import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {loadVoiceprintConfig,publicVoiceprintConfig} from '../server/voiceprint/config.mjs';
import {connectMmpg,rtcApi,safeProviderCode} from '../server/voiceprint/volc-client.mjs';
import {buildVoiceprintSession} from '../server/voiceprint/session.mjs';
const root=process.env.Z_LIVE_VOICE_CONFIG_DIR||process.env.Z_LIVE_CONFIG_DIR||fileURLToPath(new URL('../',import.meta.url));
const config=await loadVoiceprintConfig(path.resolve(root));const report={configuration:publicVoiceprintConfig(config),microphoneUsed:false};
if(process.argv.includes('--live')&&report.configuration.configured){
 try{const {status,data}=await rtcApi(config,'ListVoicePrint',{});report.rtc={ok:status===200&&!data?.ResponseMetadata?.Error,httpStatus:status,...(data?.ResponseMetadata?.Error?{code:safeProviderCode(data.ResponseMetadata.Error.Code)}:{})};}catch{report.rtc={ok:false,code:'connection_failed'};}
 report.mmpg=await new Promise(resolve=>{
  let socket,finished=false;const end=result=>{if(finished)return;finished=true;clearTimeout(timer);if(socket?.readyState===1){socket.send(JSON.stringify({event_id:randomUUID(),type:'session.close'}));socket.close();setTimeout(()=>socket.terminate(),500).unref();}else socket?.terminate();resolve(result);};
  const timer=setTimeout(()=>end({ok:false,code:'timeout'}),20000);
  try{socket=connectMmpg(config);const suffix=randomUUID().replaceAll('-','');socket.on('open',()=>socket.send(JSON.stringify(buildVoiceprintSession(config,{roomId:'qa_'+suffix,userId:'p_'+suffix,agentId:'a_'+suffix}))));
   socket.on('unexpected-response',(_req,res)=>{res.resume();end({ok:false,httpStatus:res.statusCode});});socket.on('error',()=>end({ok:false,code:'connection_failed'}));socket.on('close',()=>end({ok:false,code:'closed'}));
   socket.on('message',bytes=>{try{const e=JSON.parse(bytes);if(e.type==='session.created')end({ok:true,stage:'configured_session_created_no_audio'});else if(e.type==='error')end({ok:false,code:'provider_rejected'});}catch{end({ok:false,code:'invalid_response'});}});
  }catch{end({ok:false,code:'connection_failed'});}
 });
}
console.log(JSON.stringify(report,null,2));if(!report.configuration.configured||report.rtc?.ok===false||report.mmpg?.ok===false)process.exitCode=1;
