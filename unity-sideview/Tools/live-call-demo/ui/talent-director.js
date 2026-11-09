import {TALENT_CATALOG, playableSongs, validatePerformance, liveSkillEnabled} from './talent-config.js';

const terminal = phase => ['completed','interrupted','failed'].includes(phase);
const uid = () => crypto.randomUUID();
export class TalentDirector extends EventTarget {
  constructor({speech,catalog=TALENT_CATALOG,fetchImpl=globalThis.fetch?.bind(globalThis),requestFrame=callback=>requestAnimationFrame(callback),cancelFrame=id=>cancelAnimationFrame(id)}={}) {
    super();Object.assign(this,{speech,catalog,fetch:fetchImpl,requestFrame,cancelFrame});
    this.current=null;this.paused=false;this.destroyed=false;this.frame=null;
  }
  emit(type,detail){this.dispatchEvent(new CustomEvent(type,{detail}));}
  valid(job){return this.current===job&&!this.destroyed&&!terminal(job.phase);}
  snapshot(){
    const j=this.current;if(!j)return null;
    return {id:j.id,kind:j.kind,title:j.title,phase:j.phase,playbackId:j.playbackId,artifactId:j.artifactId,revision:0,
      positionMs:Math.round(this.position(j)*1000),durationMs:Math.round((j.buffer?.duration||j.duration||0)*1000),reason:j.reason||'',lineIndex:j.cueIndex};
  }
  changed(){this.emit('state',this.snapshot());}
  start(kind,args,{inbox=[]}={}){
    if(!liveSkillEnabled(kind))return false;
    if(this.current||this.destroyed||this.paused||!this.speech.enabled||this.speech.volume===0)return false;
    let song;
    try{
      if(kind==='voice-performance'){if(!this.speech.available)return false;validatePerformance(args,inbox);}
      else if(kind==='singing'){song=playableSongs(this.catalog).find(s=>s.id===args.songId);if(!song)return false;}
      else return false;
    }catch{return false;}
    const j={id:uid(),kind,title:song?`${song.title} · ${song.section}`:args.title,phase:'preparing',args:structuredClone(args),song,
      voiceVersion:this.catalog.voiceVersion,voiceIdentity:this.catalog.voiceIdentity,controller:new AbortController(),buffer:null,source:null,offset:0,cues:[],cueIndex:-1,playbackId:null,artifactId:null};
    this.current=j;this.changed();void this.prepare(j);return true;
  }
  checkAudio(buffer,max=180){
    if(!Number.isFinite(buffer.duration)||buffer.duration<=0||buffer.duration>max||!Number.isInteger(buffer.numberOfChannels)||buffer.numberOfChannels<1||buffer.numberOfChannels>2)throw Error('audio_invalid');
    let energy=0,count=0;
    for(let c=0;c<buffer.numberOfChannels;c++){
      const samples=buffer.getChannelData(c);
      for(let i=0;i<samples.length;i+=8){const v=samples[i];if(!Number.isFinite(v))throw Error('audio_invalid');energy+=v*v;count++;}
    }
    if(!count||Math.sqrt(energy/count)<.0001)throw Error('audio_silent');
  }
  async prepare(j){
    const timeout=setTimeout(()=>{if(this.valid(j)&&j.phase==='preparing')this.fail(j,'prepare_timeout');},120000);
    try{
      const context=this.speech.audioContext();
      if(j.kind==='voice-performance'){
        const pieces=[];let frames=0;
        for(const line of j.args.lines){
          const piece=await this.speech.prepareAudio(line.text,line.mood,{signal:j.controller.signal});
          if(!this.valid(j)||j.controller.signal.aborted)return;
          this.checkAudio(piece.buffer,90);
          if(piece.buffer.sampleRate!==context.sampleRate)throw Error('audio_invalid');
          const pause=Math.round(line.pauseAfterMs*context.sampleRate/1000);
          j.cues.push({at:frames/context.sampleRate,end:(frames+piece.buffer.length)/context.sampleRate,...line});
          pieces.push({buffer:piece.buffer,start:frames});frames+=piece.buffer.length+pause;
          if(frames/context.sampleRate>180)throw Error('audio_too_long');
        }
        const channels=Math.max(...pieces.map(p=>p.buffer.numberOfChannels));
        const merged=context.createBuffer(channels,frames,context.sampleRate);
        for(const piece of pieces)for(let c=0;c<channels;c++)merged.getChannelData(c).set(piece.buffer.getChannelData(Math.min(c,piece.buffer.numberOfChannels-1)),piece.start);
        j.buffer=merged;
      }else{
        // Only a reviewed local asset or server-resolved library ID, never an agent URL.
        const response=await this.fetch(j.song.src,{signal:j.controller.signal,redirect:'error',cache:'no-cache'});
        if(!response.ok||!/^audio\/(mpeg|wav|x-wav)(;|$)/i.test(response.headers.get('Content-Type')||''))throw Error('song_unavailable');
        const reader=response.body?.getReader();if(!reader)throw Error('song_unavailable');
        const chunks=[];let size=0;
        try{for(;;){const part=await reader.read();if(part.done)break;size+=part.value.length;if(size>12*1024*1024)throw Error('audio_too_large');chunks.push(part.value);}}
        finally{await reader.cancel().catch(()=>{});}
        const bytes=new Uint8Array(size);let at=0;for(const part of chunks){bytes.set(part,at);at+=part.length;}
        if(j.song.sha256){const digest=await crypto.subtle.digest('SHA-256',bytes);const hash=Array.from(new Uint8Array(digest),b=>b.toString(16).padStart(2,'0')).join('');if(hash!==j.song.sha256)throw Error('asset_integrity_failed');}
        const buffer=await context.decodeAudioData(bytes.buffer);if(!this.valid(j))return;j.buffer=buffer;
        j.cues=[{at:0,end:buffer.duration,text:`${j.song.title} · ${j.song.section}`,mood:'warm',gesture:'none'}];
      }
      if(!this.valid(j))return;
      this.checkAudio(j.buffer);
      j.artifactId=uid();j.duration=j.buffer.duration;j.phase='ready';this.changed();
    }catch(error){if(this.valid(j))this.fail(j,error.message||'prepare_failed');}
    finally{clearTimeout(timeout);}
  }
  voiceValid(j){return this.speech.enabled&&this.speech.volume>0&&j.voiceVersion===this.catalog.voiceVersion&&j.voiceIdentity===this.catalog.voiceIdentity;}
  async play(){
    const j=this.current;if(!j||j.phase!=='ready'||this.paused)return false;
    if(!this.voiceValid(j)){this.fail(j,'voice_unavailable');return false;}
    j.phase='starting';j.playbackId=uid();this.changed();await this.playBuffer(j);return true;
  }
  async playBuffer(j){
    const sequence=(j.playSequence||0)+1;j.playSequence=sequence;
    try{
      await this.speech.audioContext().resume();
      if(!this.valid(j)||this.paused||j.playSequence!==sequence||!['starting','playing'].includes(j.phase))return;
      if(!this.voiceValid(j))throw Error('voice_unavailable');
      const source=this.speech.context.createBufferSource();source.buffer=j.buffer;source.connect(this.speech.analyser);
      j.source=source;j.startedAt=this.speech.context.currentTime;
      source.onended=()=>{
        if(!this.valid(j)||j.source!==source||this.paused)return;
        j.offset=j.duration;j.source=null;source.onended=null;source.disconnect();
        this.stopFrame();this.emit('activity',{speaking:false,level:0});j.phase='completed';this.changed();
      };
      source.start(0,j.offset);j.phase='playing';this.changed();this.animate(j);
    }catch(error){if(this.valid(j))this.fail(j,error.message||'playback_failed');}
  }
  position(j=this.current){return j?Math.min(j.duration||0,j.offset+(j.source?Math.max(0,this.speech.context.currentTime-j.startedAt):0)):0;}
  animate(j){
    if(!this.valid(j)||!j.source||this.paused)return;
    const at=this.position(j);let index=0;while(index+1<j.cues.length&&j.cues[index+1].at<=at)index++;
    if(index!==j.cueIndex){j.cueIndex=index;this.emit('cue',{...j.cues[index],id:`${j.playbackId}:${index}`,taskId:j.id});}
    this.speech.analyser.getFloatTimeDomainData(this.speech.samples);
    const level=Math.sqrt(this.speech.samples.reduce((sum,n)=>sum+n*n,0)/this.speech.samples.length);
    this.emit('activity',{speaking:level>.008,level});
    this.frame=this.requestFrame(()=>{this.frame=null;this.animate(j);});
  }
  stopFrame(){if(this.frame!==null)this.cancelFrame(this.frame);this.frame=null;}
  stopSource(j){
    j.playSequence=(j.playSequence||0)+1;
    j.offset=this.position(j);this.stopFrame();
    if(j.source){const source=j.source;j.source=null;source.onended=null;try{source.stop();source.disconnect();}catch{}}
    this.emit('activity',{speaking:false,level:0});
  }
  fail(j,reason){if(!this.valid(j))return;this.stopSource(j);j.controller.abort();j.phase='failed';j.reason=/^[a-z_]{1,80}$/.test(reason)?reason:'audio_failed';j.buffer=null;this.changed();}
  cancel(reason='cancelled'){
    const j=this.current;if(!j||terminal(j.phase))return false;
    this.stopSource(j);j.controller.abort();j.phase='interrupted';j.reason=reason;j.buffer=null;this.changed();return true;
  }
  finish(cancel=false){const j=this.current;if(!j)return false;if(!terminal(j.phase)&&!cancel)return false;if(cancel)this.cancel();this.current=null;this.changed();return true;}
  replay(){const j=this.current;if(!j||j.phase!=='completed'||!j.buffer||this.paused||!this.voiceValid(j))return false;j.phase='ready';j.offset=0;j.cueIndex=-1;j.playbackId=null;this.changed();return true;}
  pause(){if(this.paused)return;this.paused=true;const j=this.current;if(j&&['playing','starting'].includes(j.phase)){this.stopSource(j);j.phase='paused';this.changed();}}
  resume(){if(!this.paused)return;this.paused=false;const j=this.current;if(j?.phase==='paused'){j.phase='starting';this.changed();void this.playBuffer(j);}}
  reset(){this.cancel('session_reset');this.current=null;this.changed();}
  destroy(){this.reset();this.destroyed=true;}
}
