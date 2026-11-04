import fs from 'node:fs';
import path from 'node:path';
import {uid,hash} from './providers.mjs';
import {TALENT_CATALOG,playableSongs,validatePerformance} from './shared/ui/talent-config.js';
const terminal=p=>['completed','interrupted','failed'].includes(p);
export class RemoteTalent extends EventTarget {
 constructor(session,speech){super();this.session=session;this.speech=speech;this.current=null;this.paused=false;this.catalog=structuredClone(TALENT_CATALOG);}
 emit(type,detail){this.dispatchEvent(new CustomEvent(type,{detail}));}
 snapshot(){if(!this.current)return null;const {id,kind,title,phase,playbackId=null,artifactId=null,positionMs=0,durationMs=0,reason='',lineIndex=-1}=this.current;return {id,kind,title,phase,playbackId,artifactId,positionMs,durationMs,reason,lineIndex,revision:0};}
 changed(){this.emit('state',this.snapshot());}
 preview(assetId){
  if(this.current&&!terminal(this.current.phase))throw Error('talent_busy');
  if(this.paused||!this.speech.enabled||!this.speech.volume)throw Error('audio_disabled');
  const service=this.session.providers.singing,data=service?.read();
  const asset=data?.assets.find(a=>a.id===assetId);
  if(!asset||!data.tasks.some(t=>t.owner===this.session.owner&&t.assetId===assetId&&['awaiting_review','ready','completed'].includes(t.status)))throw Error('asset_unavailable');
  if(this.current)this.finish();
  const j=this.current={id:uid(),kind:'singing',title:`试听 · ${asset.plan.title} · ${asset.plan.segment.description}`,song:{id:asset.id,src:'/api/singing/assets/'+asset.id,sha256:asset.sha256},preview:true,phase:'preparing',controller:new AbortController(),positionMs:0};
  this.session.cancelHost('song_preview');
  const e=this.session.engine;e.epoch++;e.pending?.abort();e.pending=null;e._cancelPreparation('song_preview');e._cancelTopic('song_preview');e._queue=[];e._continuations=[];
  e.callVersion++;if(e._state.call.active)e._state.call.input='idle';
  this.changed();void this.prepare(j);return true;
 }
 start(kind,args,{inbox=[]}={}){
  if(this.current||this.paused||!this.speech.enabled||!this.speech.volume)return false;let song;
  try{if(kind==='voice-performance'){if(!this.speech.available)return false;validatePerformance(args,inbox);}else if(kind==='singing'){song=playableSongs(this.catalog).find(s=>s.id===args.songId);if(!song)return false;}else return false;}catch{return false;}
  const j=this.current={id:uid(),kind,title:song?`${song.title} · ${song.section}`:args.title,args,song,phase:'preparing',controller:new AbortController(),positionMs:0};this.changed();void this.prepare(j);return true;
 }
 async prepare(j){try{
  const clips=[];if(j.kind==='singing'){
   const bytes=j.song.src.startsWith('/api/singing/assets/')?await this.session.providers.singing.audio(j.song.id,{allowUnreviewed:j.preview===true}):fs.readFileSync(path.join(this.session.songRoot,path.basename(j.song.src)));if(hash(bytes)!==j.song.sha256)throw Error('song_hash_mismatch');
   const meta=this.session.artifacts.put(bytes,'audio/wav',{sourceSha256:j.song.sha256});this.session.artifactIds.add(meta.artifactId);clips.push({...meta,text:j.title,mood:'warm',gesture:'none',words:[],pauseAfterMs:0});
  }else for(const line of j.args.lines){const audio=await this.speech.prepareAudio(line.text,line.mood,{signal:j.controller.signal});clips.push({...audio,...line});}
  if(this.current!==j||j.controller.signal.aborted)return;
  j.clips=clips;const m=this.session.artifacts.put({clips},'application/json');j.artifactId=m.artifactId;this.session.artifactIds.add(m.artifactId);
  this.session.command('media.prepare',{kind:j.kind,artifactId:j.artifactId,clips,voiceVersion:TALENT_CATALOG.voiceVersion},j.id,0);
 }catch{if(this.current===j&&!j.controller.signal.aborted){j.phase='failed';j.reason='prepare_failed';this.changed();if(j.preview)this.finish();}}}
 play(){const j=this.current;if(!j||j.phase!=='ready'||this.paused)return false;j.phase='starting';j.playbackId=uid();j.positionMs=0;j.lineIndex=-1;this.changed();this.session.command('media.play',{artifactId:j.artifactId,playbackId:j.playbackId},j.id,0);return true;}
 receipt(command,p){const j=this.current;if(!j||j.id!==command.taskId)throw Error('state_conflict');
  if(command.type==='media.prepare'&&p.status==='ready'){if(j.phase!=='preparing'||!Number.isFinite(p.durationMs)||p.durationMs<=0||p.durationMs>180000)throw Error('invalid_duration');j.durationMs=p.durationMs;j.phase='ready';}
  else if(command.type==='media.play'){
   if(j.playbackId!==p.playbackId||terminal(j.phase))throw Error('state_conflict');
   if(p.status==='started'){if(j.phase!=='starting')throw Error('state_conflict');j.phase='playing';}
   else if(p.status==='progress'){j.positionMs=p.positionMs;if(Number.isInteger(p.lineIndex)&&p.lineIndex!==j.lineIndex){if(p.lineIndex!==j.lineIndex+1||!j.clips[p.lineIndex])throw Error('invalid_segment');j.lineIndex=p.lineIndex;this.emit('cue',{taskId:j.id,text:j.clips[p.lineIndex].text});}}
   else if(p.status==='completed'){if(j.phase!=='playing'||p.positionMs<j.durationMs-250)throw Error('not_finished');j.phase='completed';j.positionMs=j.durationMs;}
  }
  if(['failed','interrupted','skipped'].includes(p.status)){j.controller?.abort();j.phase=p.status==='failed'?'failed':'interrupted';j.reason=p.reason||p.status;}
  this.changed();if(j.preview){if(j.phase==='ready'&&!this.paused)this.play();else if(terminal(j.phase))this.finish();}return true;
 }
 cancel(reason='cancelled'){const j=this.current;if(!j||terminal(j.phase))return false;j.controller?.abort();j.phase='interrupted';j.reason=reason;this.session.cancelTask(j.id,reason);this.changed();if(j.preview)this.finish();return true;}
 finish(cancel=false){const j=this.current;if(!j||!terminal(j.phase)&&!cancel)return false;if(cancel){this.cancel();if(this.current!==j)return true;}this.current=null;this.changed();this.session.command('activity.release',{kind:j.kind},j.id,0);return true;}
 replay(){const j=this.current;if(!j||j.phase!=='completed'||!j.clips||this.paused)return false;j.phase='ready';this.changed();return true;}
 pause(){this.paused=true;const j=this.current;if(j&&['starting','playing'].includes(j.phase)){j.phase='paused';this.changed();}}
 resume(){this.paused=false;const j=this.current;if(j?.phase==='paused'){j.phase='playing';this.changed();}else if(j?.preview&&j.phase==='ready')this.play();}
 reset(){this.cancel('session_reset');this.current=null;this.changed();}
 destroy(){this.reset();}
}
