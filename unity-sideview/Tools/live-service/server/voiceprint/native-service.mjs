import fs from 'node:fs/promises';
import path from 'node:path';
import {randomUUID, createHash} from 'node:crypto';
import {loadVoiceprintConfig, publicVoiceprintConfig} from './config.mjs';
import {connectMmpg, rtcApi} from './volc-client.mjs';
import {buildVoiceprintSession} from './session.mjs';
import {issueRtcToken} from './rtc-token.mjs';
import {VoiceprintTurnGate} from './turn-gate.mjs';
import {validateEnrollmentWav} from './http.mjs';

// Authentication and executor-lease checks happen in HTTP before these methods.
// RTC tokens live only in this ephemeral map, never session events or saves.
export class NativeVoiceService {
 constructor({configRoot, profileRoot, connect=connectMmpg, api=rtcApi, load=loadVoiceprintConfig, makeToken=issueRtcToken}) {
  Object.assign(this,{configRoot,profileRoot,connect,api,load,makeToken});this.links=new Map();this.enrolling=new Set();
 }
 profile(owner){return path.join(this.profileRoot,createHash('sha256').update(owner).digest('hex')+'.json');}
 async config(owner){
  const config=await this.load(this.configRoot);
  // Shared host credentials never imply another player's voice identity.
  if(owner!=='local-owner')config.voiceprintId='';
  try{const p=JSON.parse(await fs.readFile(this.profile(owner),'utf8'));if(p.version===2&&p.appId===config.rtcAppId)config.voiceprintId=p.id;}catch(e){if(e.code!=='ENOENT'&&!(e instanceof SyntaxError))throw e;}
  config.missing=config.missing.filter(k=>k!=='VOLC_VOICEPRINT_ID');if(!config.voiceprintId)config.missing.push('VOLC_VOICEPRINT_ID');return config;
 }
 async publicConfig(owner){return publicVoiceprintConfig(await this.config(owner));}
 async enroll(owner,audio,signal){
  if(this.enrolling.has(owner))throw Error('voiceprint_enrollment_busy');this.enrolling.add(owner);
  try{
   const config=await this.config(owner);if(!config.validMode||config.missing.some(k=>k!=='VOLC_VOICEPRINT_ID'))throw Error('voiceprint_not_configured');
   if(config.voiceprintId)return {enrolled:true};validateEnrollmentWav(audio);
   const {status,data}=await this.api(config,'RegisterVoicePrint',{AudioFormat:0,Audio:audio.toString('base64'),AudioName:'z_'+randomUUID(),Version:2},{signal});
   if(status!==200||data?.ResponseMetadata?.Error||typeof data?.Result!=='string'||!data.Result)throw Error('voiceprint_registration_failed');
   await fs.mkdir(this.profileRoot,{recursive:true,mode:0o700});const file=this.profile(owner),tmp=file+'.'+randomUUID()+'.tmp';
   await fs.writeFile(tmp,JSON.stringify({appId:config.rtcAppId,id:data.Result,version:2}),{mode:0o600});await fs.rename(tmp,file);return {enrolled:true};
  }finally{this.enrolling.delete(owner);}
 }
 current(s,callId){return !s.closed&&s.phase==='live'&&s.sessionControl.normal&&s.engine._state.call.active&&s.engine._state.call.id===callId&&Date.now()<s.leaseUntil;}
 async start(s){
  const callId=s.engine._state.call.id;if(!this.current(s,callId))throw Error('stale_voice_turn');
  const old=this.links.get(s.id);if(old?.callId===callId)return old.result;
  this.stop(s);const link={callId,result:null,close(){}};this.links.set(s.id,link);
  link.result=this.open(s,link);try{return await link.result;}catch(e){if(this.links.get(s.id)===link)this.links.delete(s.id);throw e;}
 }
 async open(s,link){
  const config=await this.config(s.owner);
  if(this.links.get(s.id)!==link||!this.current(s,link.callId))throw Error('stale_voice_turn');
  if(config.mode!=='voiceprint'||!config.validMode||config.missing.length)throw Error('voiceprint_not_configured');
  const suffix=randomUUID().replaceAll('-',''),roomId='z_'+suffix,userId='p_'+suffix,agentId='a_'+suffix;
  const gate=new VoiceprintTurnGate({holdMs:300});let provider,closed=false,providerReady=false,joined=false;
  const current=()=>!closed&&this.links.get(s.id)===link&&this.current(s,link.callId);
  const emit=p=>{if(current())s.event('call.voice',{...p,callId:link.callId});};
  const close=()=>{if(closed)return;closed=true;gate.reset();clearInterval(flush);clearTimeout(initial);clearTimeout(lifetime);
   if(this.links.get(s.id)===link)this.links.delete(s.id);
   if(provider?.readyState===1){provider.send(JSON.stringify({event_id:randomUUID(),type:'session.close'}));provider.close();setTimeout(()=>provider.terminate(),1000).unref();}else provider?.terminate();
  };
  const fail=code=>{emit({type:'error',code});close();if(s.engine._state.call.id===link.callId)s.engine.endCall();};
  const forward=events=>{if(!current())return close();for(const e of events){
   if(e.type==='player-speaking'){void s.engine.yieldCallFloor();emit(e);}
   else if(e.type==='transcript'){
    emit(e);
    if(e.final){if(e.text.length<=360)s.input({protocolVersion:1,sessionId:s.id,epoch:s.epoch,type:'viewer.message',payload:{text:e.text}});else{s.engine.setCallInput('reviewing');emit({type:'review',text:e.text});}}
   }else if(e.type==='discard'){s.engine.setCallInput('idle');emit(e);}
  }};
  const flush=setInterval(()=>{try{forward(gate.flush());}catch{fail('voiceprint_connection_failed');}},25);
  const initial=setTimeout(()=>fail('voiceprint_connect_timeout'),20000),lifetime=setTimeout(()=>fail('voiceprint_session_expired'),29*60*1000);
  const ready=()=>{if(current()&&joined&&providerReady){clearTimeout(initial);emit({type:'ready',protection:'voiceprint'});}};
  link.close=close;link.join=()=>{joined=true;ready();};
  try{
   provider=this.connect(config);
   provider.on('open',()=>{if(current())provider.send(JSON.stringify(buildVoiceprintSession(config,{roomId,userId,agentId})));else close();});
   provider.on('unexpected-response',(_req,res)=>{res.resume();fail('voiceprint_permission');});
   provider.on('error',()=>fail('voiceprint_connection_failed'));provider.on('close',()=>{if(!closed)fail('voiceprint_disconnected');});
   provider.on('message',data=>{if(!current())return close();try{const e=JSON.parse(data);if(e.type==='session.created'){providerReady=true;return ready();}
    if(e.type==='error')return fail('voiceprint_provider_rejected');if(e.type==='user.audio.transcription.started'&&e.sv_active!==true)return fail('voiceprint_verification_inactive');forward(gate.receive(e));
   }catch{fail('voiceprint_invalid_response');}});
   return {callId:link.callId,appId:config.rtcAppId,roomId,userId,token:this.makeToken({appId:config.rtcAppId,appKey:config.rtcAppKey,roomId,userId})};
  }catch{close();throw Error('voiceprint_connection_failed');}
 }
 joined(s,callId){const link=this.links.get(s.id);if(!link||link.callId!==callId||!this.current(s,callId))throw Error('stale_voice_turn');link.join?.();return {accepted:true};}
 stop(s){const link=this.links.get(s.id);this.links.delete(s.id);link?.close();return {accepted:true};}
 close(){for(const link of this.links.values())link.close();this.links.clear();}
}
