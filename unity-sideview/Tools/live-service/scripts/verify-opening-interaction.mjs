// Opt-in real provider text; fictional player, isolated session, no audio verification claim.
import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';
import {normalizeAgentInput} from '../server/shared/worker/agent-director.js';
import {LiveSession} from '../server/session.mjs';
import {Providers,ArtifactStore,loadEnvironment} from '../server/providers.mjs';
if(!process.argv.includes('--allow-real-api'))throw Error('Pass --allow-real-api to use the existing provider.');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),output=process.argv.find(x=>x.startsWith('--output='))?.slice(9);
if(!output?.startsWith('/private/tmp/'))throw Error('Use isolated /private/tmp/ output.');fs.mkdirSync(output,{recursive:true});
const env=loadEnvironment(process.env.Z_LIVE_CONFIG_DIR||root);if(!env.DEEPSEEK_API_KEY)throw Error('Existing model configuration unavailable');
const provider=new Providers(env);const generate=provider.generate.bind(provider);provider.generate=async(identity,payload,options)=>{try{normalizeAgentInput(payload);}catch(error){fs.writeFileSync(path.join(output,'invalid-input.json'),JSON.stringify({payload,stack:error.stack},null,2));throw error;}return generate(identity,payload,options);};
const s=new LiveSession({root:path.join(output,'sessions'),providers:provider,artifacts:new ArtifactStore(path.join(output,'artifacts')),songRoot:path.join(root,'server/assets/songs'),capabilities:{audioEnabled:false},story:{mode:'arrival',playerName:'体验小海',worldId:crypto.randomUUID().replaceAll('-',''),day:1}});
const envelope=extra=>({protocolVersion:1,sessionId:s.id,epoch:s.epoch,executorLeaseId:s.lease,...extra});
const receipt=(c,status,extra={})=>s.receipt(envelope({commandId:c.commandId,taskId:c.taskId,resourceRevision:c.resourceRevision,payload:{...Object.fromEntries(['speechId','modality'].filter(k=>c.payload[k]!==undefined).map(k=>[k,c.payload[k]])),status,...extra}}));
const began=Date.now(),seen=new Map(),trace=[];let draw=null,offered=false;
try{
 await s.initialize();s.control(envelope({action:'resume'}));
 while(Date.now()-began<240000&&!offered){
  s.heartbeat(envelope({}));if(s.engine._state.agent.errorCode==='invalid_input')throw Error('Invalid provider input; see isolated diagnostic.');
  for(const c of s.commands.values()){
   if(['displayed','completed','interrupted','cancelled','failed'].includes(c.status))continue;
   if(c.type==='host.present'){
    if(!seen.has(c.commandId)){const duration=Math.max(3000,Array.from(c.payload.text).length*145);seen.set(c.commandId,{at:Date.now(),duration});trace.push({kind:'host',seconds:(Date.now()-began)/1000,text:c.payload.text,warmup:s.scene.warmupStatus()});receipt(c,'started',{positionMs:0,durationMs:duration});}
    const item=seen.get(c.commandId);if(Date.now()-item.at>=item.duration)receipt(c,'completed',{positionMs:item.duration});
   }else if(c.type==='virtual_audience.present'){
    if(!seen.has(c.commandId))seen.set(c.commandId,{at:Date.now()+(c.payload.delayMs||0)});
    if(Date.now()>=seen.get(c.commandId).at){trace.push({kind:'audience',seconds:(Date.now()-began)/1000,name:c.payload.name,text:c.payload.text});receipt(c,'displayed');}
   }else if(c.type==='story.present'&&c.payload.tool==='draw_guest'){
    if(!draw){draw={at:Date.now(),warmup:s.scene.warmupStatus()};if(!draw.warmup.ready)throw Error('Premature lottery');trace.push({kind:'gift_shown',seconds:(Date.now()-began)/1000,warmup:draw.warmup});}
    if(Date.now()-draw.at>3000)receipt(c,'displayed',{playerActivated:true,playerName:'体验小海'});
   }else if(c.type==='story.present'&&c.payload.tool==='offer_arrival'){
    trace.push({kind:'invitation',seconds:(Date.now()-began)/1000,title:c.payload.title,description:c.payload.description});receipt(c,'displayed');offered=true;
   }else if(['media.resume','media.pause'].includes(c.type))receipt(c,'completed');
  }
  fs.writeFileSync(path.join(output,'trace.json'),JSON.stringify(trace,null,2));await new Promise(r=>setTimeout(r,100));
 }
 if(!offered)throw Error('Opening timeout: '+(s.engine._state.agent.errorCode||s.scene.phase));
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({passed:true,scope:'Real provider text; playback and click receipts driven by verifier, not human playback or voice verification.',trace},null,2));console.log('Real-provider opening text reached player-specific invitation.');
}finally{s.close();}
