import {TextDirector} from './text-director.js';

const VOICE = 'Vivi 2.0';
const SPEAKER = 'zh_female_vv_uranus_bigtts';
export const CALL_THINKING_TEXT='我想想啊...';
// MP3 bytes remain in one decoder, so network packet boundaries never become
// audible sentence boundaries. A prepared stream cannot play before adoption.
export class Mp3Stream {
  static supported(){return !!globalThis.MediaSource?.isTypeSupported?.('audio/mpeg')&&typeof globalThis.Audio==='function';}
  constructor(){
    this.audio=new Audio();this.audio.preload='auto';this.media=new MediaSource();this.queue=[];this.words=[];this.closed=false;this.finished=false;
    this.ready=new Promise((resolve,reject)=>{this.resolve=resolve;this.reject=reject;});this.ready.catch(()=>{});
    this.audio.onerror=()=>this.fail();
    this.audio.oncanplay=()=>this.resolve(this);
    this.media.addEventListener('sourceopen',()=>{
      if(this.closed)return;
      try{this.buffer=this.media.addSourceBuffer('audio/mpeg');this.buffer.addEventListener('updateend',()=>this.pump());this.buffer.addEventListener('error',()=>this.fail());this.pump();}catch{this.fail();}
    },{once:true});
    this.url=URL.createObjectURL(this.media);this.audio.src=this.url;
  }
  append(bytes){if(this.closed)return;this.queue.push(bytes);this.pump();}
  end(){this.finished=true;this.pump();}
  pump(){
    if(this.closed||this.error||!this.buffer||this.buffer.updating||this.media.readyState!=='open')return;
    try{if(this.buffer.buffered.length&&this.buffer.buffered.end(this.buffer.buffered.length-1)>120)return this.fail();if(this.queue.length)this.buffer.appendBuffer(this.queue.shift());else if(this.finished)this.media.endOfStream();}catch{this.fail();}
  }
  fail(){if(this.closed||this.error)return;this.error=Error('stream_failed');this.reject(this.error);this.onfailure?.();}
  caption(text,position){
    let cursor=0,index=0;
    for(const word of this.words){
      if(typeof word.word!=='string'||!word.word||!Number.isFinite(word.startTime)||!Number.isFinite(word.endTime)||word.endTime<word.startTime)continue;
      const at=text.indexOf(word.word,cursor);if(at<0)break;
      if(position<word.startTime)break;
      const from=Array.from(text.slice(0,at)).length,count=Array.from(word.word).length;
      const ratio=word.endTime>word.startTime?Math.min(1,(position-word.startTime)/(word.endTime-word.startTime)):1;
      index=from+Math.floor(count*ratio);cursor=at+word.word.length;if(ratio<1)break;
    }
    return index;
  }
  dispose(){
    if(this.closed)return;this.closed=true;this.reject(Error('cancelled'));this.onfailure=null;this.queue=[];
    this.audio.onended=null;this.audio.onerror=null;this.audio.oncanplay=null;this.audio.pause();this.audio.removeAttribute('src');this.audio.load();
    try{this.node?.disconnect();}catch{}URL.revokeObjectURL(this.url);
  }
}
const notices = {
  unavailable: 'Vivi 2.0 尚未配置，继续文字播放。',
  unsupported: '当前浏览器无法播放语音，继续文字播放。',
  timeout: '语音等待超时，已继续文字播放。',
  failed: '本句语音暂时不可用，已继续文字播放。',
  long: '这句话较长，使用完整文字展示。',
};

/** One caption/animation interface for real Vivi audio and text-only fallback. */
export class SpeechDirector extends EventTarget {
  constructor({fetchImpl=globalThis.fetch?.bind(globalThis),
    createAudioContext=()=>new (globalThis.AudioContext||globalThis.webkitAudioContext)(),
    requestFrame=callback=>globalThis.requestAnimationFrame(callback),
    cancelFrame=id=>globalThis.cancelAnimationFrame(id), timeoutMs=25000}={}) {
    super();
    Object.assign(this,{fetch:fetchImpl,createAudioContext,requestFrame,cancelFrame,timeoutMs});
    this.fallback=new TextDirector();this.current=null;this.context=null;this.gain=null;this.analyser=null;
    this.enabled=false;this.available=false;this.configured=false;this.paused=false;this.destroyed=false;
    this.volume=.85;this.sequence=0;this.enableSequence=0;this.frame=null;this.configController=null;
    this.retained=new Map();this.retainedBytes=0;this.retentionTimer=null;
    this.retention={maxEntries:4,maxBytes:8*1024*1024,maxAgeMs:120000};
    this.warm=null;
    this.config={enabled:false,voice:VOICE,speaker:SPEAKER,format:'mp3',maxTextLength:500};
    this.status={mode:'muted',message:'声音已静音'};
    this.fallback.addEventListener('text',({detail})=>{
      const current=this.current;if(!current||current.mode!=='text'||detail.id!==current.id)return;
      const text=current.prefix+detail.text;current.index=Array.from(text).length;
      this.emit('text',{id:current.id,text});
    });
    this.fallback.addEventListener('activity',({detail})=>{
      const current=this.current;if(current?.mode==='text'&&detail.id===current.id)this.emit('activity',detail);
    });
    this.fallback.addEventListener('complete',({detail})=>{
      const current=this.current;if(current?.mode==='text'&&detail.id===current.id)this.complete(current);
    });
  }
  performance(current,phase,extra={}){if(this.valid(current))this.emit('performance',{id:current.id,phase,...extra});}
  emit(type,detail){this.dispatchEvent(new CustomEvent(type,{detail}));}
  report(mode,message='',reason=''){
    this.status={mode,message,reason,enabled:this.enabled,available:this.available,volume:this.volume,voice:VOICE};
    this.emit('status',this.status);
  }
  async configure(){
    this.configController?.abort();const controller=new AbortController();this.configController=controller;
    const timer=setTimeout(()=>controller.abort(),6000);
    try{
      if(!this.fetch||globalThis.location?.protocol==='file:')throw new Error('unavailable');
      const response=await this.fetch('/api/tts/config',{signal:controller.signal,cache:'no-store'});
      if(!response.ok)throw new Error('unavailable');
      const data=await response.json();
      if(this.destroyed||this.configController!==controller)return {enabled:false};
      this.available=data.enabled===true&&data.provider==='Volcengine'&&data.voice===VOICE&&data.speaker===SPEAKER&&data.format==='mp3';
      this.config={...this.config,...data,maxTextLength:Math.min(500,Number(data.maxTextLength)||500)};
    }catch{if(this.configController===controller)this.available=false;}
    finally{clearTimeout(timer);if(this.configController===controller)this.configController=null;}
    if(this.destroyed)return {enabled:false};
    this.configured=true;
    if(!this.available&&this.enabled)this.setEnabled(false);
    this.report(this.enabled?'ready':'muted',this.available?'声音已静音':notices.unavailable,this.available?'':'unavailable');
    return {...this.config,enabled:this.available};
  }
  audioContext(){
    if(this.context)return this.context;
    const context=this.createAudioContext();this.context=context;
    this.gain=context.createGain();this.gain.gain.value=this.volume;
    this.analyser=context.createAnalyser();this.analyser.fftSize=256;
    this.analyser.connect(this.gain);this.gain.connect(context.destination);
    this.samples=new Float32Array(this.analyser.fftSize);
    return context;
  }
  async setEnabled(value){
    const sequence=++this.enableSequence;
    if(!value){
      void this.stopCallThinking();
      this.enabled=false;this.dropWarmSpeech();this.clearRetained();const current=this.current;
      if(current&&!current.done){this.abort(current);this.stop(current);this.startText(current,current.index);}
      this.report('muted','声音已静音');return false;
    }
    if(this.enabled)return true;
    try{
      // Called directly from a button gesture, before any network await.
      await this.audioContext().resume();
    }catch{this.report('muted',notices.unsupported,'unsupported');return false;}
    if(!this.configured||!this.available)await this.configure();
    if(sequence!==this.enableSequence||this.destroyed)return false;
    if(!this.available){this.report('muted',notices.unavailable,'unavailable');return false;}
    this.enabled=true;this.report('ready',`${VOICE} 已开启`);
    // Enabling affects future speeches; the current caption keeps its timer,
    // progress and identity, without replacing it or creating a paid request.
    return true;
  }
  setVolume(value){
    this.volume=Math.max(0,Math.min(1,Number(value)||0));
    if(!this.volume)void this.stopCallThinking();
    if(!this.volume)this.dropWarmSpeech();
    if(this.gain)this.gain.gain.value=this.volume;
    this.report(this.status.mode,this.status.message,this.status.reason);
  }
  speak(detail){
    this.pruneRetained();
    const key=typeof detail.utteranceKey==='string'?detail.utteranceKey:null;
    const retained=detail.resume===true&&key?this.retained.get(key):null;
    if(retained)this.removeRetained(key);
    this.clearCurrent(true);if(this.destroyed)return;
    const text=String(detail.text||'');
    const current={id:detail.id??detail.speechId,detail:{...detail,text},text,chars:Array.from(text),index:0,
      token:++this.sequence,mode:'waiting',done:false,source:null,buffer:null,offset:0,controller:null,timer:null};
    this.current=current;
    const saved=retained?.text===text?retained:null;
    if(saved)current.index=saved.index;
    this.emit('text',{id:current.id,text:current.chars.slice(0,current.index).join('')});this.emit('activity',{id:current.id,speaking:false});
    if(detail.source==='call-yield'){
      const audio=this.callYieldAudio;
      if(audio&&this.enabled&&this.available){current.buffer=audio.buffer;current.timeline=this.timeline(current,audio.words,audio.buffer.duration);current.mode='audio';if(!this.paused)void this.play(current);}
      else this.startText(current,0); // Never play a late acknowledgement over the guest's next turn.
      return;
    }
    if(detail.resume===true){
      // Only explicit engine continuations can reuse an interrupted recording.
      // A cancelled/expired generation resumes as text, never a second paid POST.
      if(saved?.buffer&&this.enabled&&this.available){
        Object.assign(current,{mode:'audio',buffer:saved.buffer,offset:saved.offset,timeline:saved.timeline});
        if(!this.paused)this.play(current);
      }else this.startText(current,current.index);
      return;
    }
    if(!this.enabled||!this.available){this.startText(current,0);return;}
    if(text.length>this.config.maxTextLength){this.useText(current,'long');return;}
    const warm=this.warm;
    if(warm&&warm.key===detail.preparedAudioKey&&warm.text===text&&warm.mood===detail.mood){
      clearTimeout(warm.timer);this.warm=null;
      void this.useWarmAudio(current,warm);return;
    }
    if(!this.paused)this.requestAudio(current);
  }
  // One unpublished recording, exact candidate identity. Warming never speaks,
  // updates captions, or authorizes publication. The engine owns that decision.
  warmSpeech(key,text,mood,{streaming=false}={}){
    if(this.destroyed||this.paused||!this.enabled||!this.available||!this.volume||!key||!text||text.length>this.config.maxTextLength)return null;
    if(this.warm?.key===key&&this.warm.text===text&&this.warm.mood===mood)return key;
    this.dropWarmSpeech();
    const slot={key,text,mood,controller:new AbortController(),startedAt:performance.now()};this.warm=slot;
    slot.timer=setTimeout(()=>this.dropWarmSpeech(key),30000);
    this.emit('preparation',{key,phase:'started'});
    if(streaming&&Mp3Stream.supported()){
      slot.stream=new Mp3Stream();
      slot.promise=slot.stream.ready.then(()=>{this.emit('preparation',{key,phase:'ready',durationMs:Math.round(performance.now()-slot.startedAt)});return {stream:slot.stream};}).catch(()=>null);
      slot.network=this.fillStream(slot,text,mood);
      return key;
    }
    slot.promise=this.prepareAudio(text,mood,{signal:slot.controller.signal}).then(audio=>{
      const bytes=(audio.buffer.length||Math.ceil(audio.buffer.duration*(audio.buffer.sampleRate||24000)))*(audio.buffer.numberOfChannels||1)*4;
      if(slot.controller.signal.aborted||bytes>8*1024*1024)return null;
      this.emit('preparation',{key,phase:'ready',durationMs:Math.round(performance.now()-slot.startedAt)});return audio;
    }).catch(()=>{if(!slot.controller.signal.aborted)this.emit('preparation',{key,phase:'failed',durationMs:Math.round(performance.now()-slot.startedAt)});return null;});
    return key;
  }
  prepareCallYield(){
    if(this.callYieldAudio)return Promise.resolve(this.callYieldAudio);
    if(this.callYieldPreparation)return this.callYieldPreparation.promise;
    if(!this.enabled||!this.available)return Promise.resolve(null);
    const work={controller:new AbortController()};this.callYieldPreparation=work;
    work.promise=this.prepareAudio('好，你先说。','warm',{signal:work.controller.signal}).then(audio=>{
      if(this.callYieldPreparation===work&&!work.controller.signal.aborted&&audio.buffer.duration<=5)this.callYieldAudio=audio;
    }).catch(()=>{}).finally(()=>{if(this.callYieldPreparation===work)this.callYieldPreparation=null;});
    return work.promise;
  }
  // Fixed, user-requested waiting feedback. Separate from generated content and
  // its single warm slot; it cannot approve a reply or mark a player answered.
  prepareCallThinking(){
    if(this.thinkingAudio)return Promise.resolve(this.thinkingAudio);
    if(this.thinkingPreparation)return this.thinkingPreparation.promise;
    if(this.destroyed||!this.enabled||!this.available)return Promise.resolve(null);
    const work={controller:new AbortController()};this.thinkingPreparation=work;
    work.promise=this.prepareAudio(CALL_THINKING_TEXT,'warm',{signal:work.controller.signal}).then(audio=>{
      if(!this.destroyed&&!work.controller.signal.aborted&&audio.buffer.duration<=4)this.thinkingAudio=audio;
      return this.thinkingAudio||null;
    }).catch(()=>null).finally(()=>{if(this.thinkingPreparation===work)this.thinkingPreparation=null;});
    return work.promise;
  }
  playCallThinking(){
    if(this.thinkingCue||!this.thinkingAudio||this.destroyed||this.paused||!this.enabled||!this.volume||this.current&&!this.current.done)return false;
    const context=this.audioContext();if(context.state!=='running')return false;
    const source=context.createBufferSource(),gain=context.createGain(),cue={id:'call-thinking-'+crypto.randomUUID(),source,gain,frame:null,timer:null,stopping:false};
    source.buffer=this.thinkingAudio.buffer;source.connect(gain);gain.connect(this.analyser);this.thinkingCue=cue;
    cue.finished=new Promise(resolve=>cue.resolve=resolve);
    cue.finish=()=>{
      if(this.thinkingCue!==cue)return;this.thinkingCue=null;clearTimeout(cue.timer);if(cue.frame!==null)this.cancelFrame(cue.frame);
      source.onended=null;try{source.stop();}catch{}try{source.disconnect();gain.disconnect();}catch{}
      this.emit('call-feedback',{id:cue.id,phase:'ended',text:CALL_THINKING_TEXT});cue.resolve();
    };
    const animate=()=>{if(this.thinkingCue!==cue)return;this.analyser.getFloatTimeDomainData(this.samples);const level=Math.sqrt(this.samples.reduce((sum,n)=>sum+n*n,0)/this.samples.length);this.emit('call-feedback',{id:cue.id,phase:'activity',speaking:level>.008,level});cue.frame=this.requestFrame(animate);};
    source.onended=cue.finish;
    try{source.start();this.emit('call-feedback',{id:cue.id,phase:'speaking',text:CALL_THINKING_TEXT});animate();return true;}catch{cue.finish();return false;}
  }
  stopCallThinking(fadeMs=0){
    const cue=this.thinkingCue;if(!cue)return Promise.resolve();
    if(!fadeMs){cue.finish();return cue.finished;}
    if(!cue.stopping){cue.stopping=true;const ms=Math.min(180,Math.max(0,fadeMs)),gain=cue.gain.gain,time=this.context.currentTime;gain.cancelScheduledValues?.(time);gain.setValueAtTime?.(gain.value,time);gain.linearRampToValueAtTime?.(0,time+ms/1000);cue.timer=setTimeout(cue.finish,ms);}
    return cue.finished;
  }
  fadeOut(ms=180){
    if(this.fadeTask)return this.fadeTask.promise;
    if((!this.current?.source&&!this.current?.stream)||!this.gain||!this.context)return Promise.resolve();
    const gain=this.gain.gain,time=this.context.currentTime,duration=Math.max(0,Math.min(250,ms));
    gain.cancelScheduledValues?.(time);gain.setValueAtTime?.(gain.value,time);gain.linearRampToValueAtTime?.(0,time+duration/1000);
    const task={};task.promise=new Promise(resolve=>{task.resolve=resolve;task.timer=setTimeout(()=>{if(this.fadeTask===task)this.fadeTask=null;resolve();},duration);});this.fadeTask=task;return task.promise;
  }
  resetFade(){
    const task=this.fadeTask;this.fadeTask=null;if(task){clearTimeout(task.timer);task.resolve();}
    if(this.gain){this.gain.gain.cancelScheduledValues?.(this.context.currentTime);this.gain.gain.value=this.volume;}
  }
  dropWarmSpeech(key){
    const slot=this.warm;if(!slot||key&&slot.key!==key)return;
    this.warm=null;clearTimeout(slot.timer);slot.controller.abort();slot.stream?.dispose();
    this.emit('preparation',{key:slot.key,phase:'cancelled'});
  }
  async useWarmAudio(current,slot){
    current.mode='loading';current.controller=slot.controller;
    if(slot.stream){current.stream=slot.stream;slot.stream.onfailure=()=>{if(this.valid(current))this.useText(current,'failed');};}
    this.performance(current,'thinking');this.report('loading','正在准备 Z 的声音');
    const audio=await slot.promise;
    if(!this.valid(current)||current.mode!=='loading'||slot.controller.signal.aborted||!this.enabled)return;
    if(!slot.stream)current.controller=null;
    if(!audio){this.useText(current,'failed');return;} // No second paid synthesis.
    if(audio.stream){if(audio.stream.error){this.useText(current,'failed');return;}current.mode='audio';if(!this.paused)await this.playStream(current);return;}
    current.buffer=audio.buffer;current.timeline=this.timeline(current,audio.words,audio.buffer.duration);current.mode='audio';
    if(!this.paused)await this.play(current);
  }
  valid(current){return this.current===current&&!current.done&&!this.destroyed;}
  abort(current){current.controller?.abort();current.controller=null;clearTimeout(current.timer);current.timer=null;}
  stop(current){
    current.playSequence=(current.playSequence||0)+1;
    if(this.frame!==null){this.cancelFrame(this.frame);this.frame=null;}
    current.stream?.audio.pause();
    if(current.source){
      current.offset=this.position(current);const source=current.source;current.source=null;
      source.onended=null;try{source.stop();}catch{}try{source.disconnect();}catch{}
    }
    this.emit('activity',{id:current.id,speaking:false,level:0});
  }
  startText(current,index){
    if(!this.valid(current))return;
    void this.stopCallThinking(80);
    current.stream?.dispose();current.stream=null;
    current.mode='text';current.buffer=null;current.offset=0;current.prefix=current.chars.slice(0,index).join('');current.index=index;
    if(!this.paused)this.performance(current,'speaking',{resumed:current.performanceStarted===true,mode:'text'});
    current.performanceStarted=true;
    this.fallback.speak({...current.detail,text:current.chars.slice(index).join('')});
    if(this.paused)this.fallback.pause();else this.fallback.resume();
  }
  useText(current,reason){
    if(!this.valid(current))return;
    this.abort(current);this.stop(current);this.startText(current,current.index);
    this.report('fallback',notices[reason]||notices.failed,reason);
  }
  async requestAudio(current){
    if(!this.valid(current)||this.paused||!this.enabled)return;
    this.performance(current,'thinking');
    current.mode='loading';const controller=new AbortController();current.controller=controller;
    let timedOut=false;current.timer=setTimeout(()=>{
      timedOut=true;controller.abort();if(this.valid(current)&&current.mode==='loading')this.useText(current,'timeout');
    },this.timeoutMs);
    this.report('loading','正在准备 Z 的声音');
    const requestId=globalThis.crypto?.randomUUID?.()||'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g,c=>{
      const n=Math.random()*16|0;return(c==='x'?n:(n&3)|8).toString(16);
    });
    try{
      const response=await this.fetch('/api/tts',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/x-ndjson'},
        signal:controller.signal,body:JSON.stringify({requestId,text:current.text,mood:({neutral:'warm',shy:'warm',sad:'warm',surprised:'happy'})[current.detail.mood]||(['happy','warm','serious','thinking'].includes(current.detail.mood)?current.detail.mood:'warm')})});
      if(!this.valid(current)||current.mode!=='loading'||controller.signal.aborted)return;
      if(!response.ok){
        const error=await response.json().catch(()=>({}));
        if(response.status===503&&error.error==='not_configured')throw new Error('unavailable');
        throw new Error('failed');
      }
      if(!response.headers.get('Content-Type')?.includes('application/x-ndjson')||!response.body)throw new Error('failed');
      const audio=await this.readAudio(response.body,current,requestId,controller.signal);
      if(!this.valid(current)||current.mode!=='loading'||controller.signal.aborted)return;
      const buffer=await this.audioContext().decodeAudioData(audio.bytes.buffer);
      if(!this.valid(current)||current.mode!=='loading'||controller.signal.aborted)return;
      if(!Number.isFinite(buffer.duration)||buffer.duration<=0||buffer.duration>120)throw new Error('failed');
      clearTimeout(current.timer);current.timer=null;current.controller=null;current.buffer=buffer;
      current.timeline=this.timeline(current,audio.words,buffer.duration);current.mode='audio';await this.play(current);
    }catch(error){
      if(this.valid(current)&&current.mode==='loading')this.useText(current,timedOut?'timeout':error.message==='unavailable'?'unavailable':'failed');
    }finally{clearTimeout(current.timer);current.timer=null;if(current.controller===controller)current.controller=null;}
  }
  async readAudio(body,current,requestId,signal,sink=null){
    const reader=body.getReader(),decoder=new TextDecoder(),chunks=[],words=[];
    let text='',bytes=0,total=0,index=0,started=false,done=false;
    const record=line=>{
      if(!line.trim())return;
      if(line.length>1500000)throw new Error('failed');
      const item=JSON.parse(line);
      if(item.requestId!==requestId)throw new Error('failed');
      if(item.type==='error')throw new Error('failed');
      if(item.type==='start'){
        if(started||item.requestId!==requestId||item.format!=='mp3'||item.source!=='volcengine'||item.speaker!==SPEAKER)throw new Error('failed');
        started=true;
      }else if(item.type==='audio'){
        if(!started||done||item.index!==index++||typeof item.data!=='string'||!item.data||!/^[A-Za-z0-9+/]*={0,2}$/.test(item.data))throw new Error('failed');
        const binary=atob(item.data),chunk=Uint8Array.from(binary,char=>char.charCodeAt(0));
        bytes+=chunk.length;if(bytes>8000000)throw new Error('failed');if(sink)sink.append(chunk);else chunks.push(chunk);
      }else if(item.type==='sentence'){
        if(!started||!Array.isArray(item.sentence?.words))return;
        words.push(...item.sentence.words);if(words.length>2000)throw new Error('failed');if(sink)sink.words=words;
      }else if(item.type==='done'){
        if(!started||!bytes)throw new Error('failed');done=true;
      }else throw new Error('failed');
    };
    try{
      while(!done){
        const next=await reader.read();
        if((current&&!this.valid(current))||signal.aborted)throw new Error('cancelled');
        if(next.done){text+=decoder.decode();if(text.trim())record(text);break;}
        total+=next.value.byteLength;if(total>12000000)throw new Error('failed');
        text+=decoder.decode(next.value,{stream:true});
        if(text.length>1500000&&!text.includes('\n'))throw new Error('failed');
        let newline;
        while((newline=text.indexOf('\n'))>=0){const line=text.slice(0,newline);text=text.slice(newline+1);record(line);if(done)break;}
      }
      if(!done)throw new Error('failed');
      if(sink){sink.end();return {words};}
      const joined=new Uint8Array(bytes);let offset=0;for(const chunk of chunks){joined.set(chunk,offset);offset+=chunk.length;}
      return {bytes:joined,words};
    }finally{try{await reader.cancel();}catch{}}
  }
  async fillStream(slot,text,mood){
    const requestId=crypto.randomUUID(),timer=setTimeout(()=>{slot.stream.fail();slot.controller.abort();},this.timeoutMs);
    try{
      const response=await this.fetch('/api/tts',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/x-ndjson'},signal:slot.controller.signal,
        body:JSON.stringify({requestId,text,mood:({neutral:'warm',shy:'warm',sad:'warm',surprised:'happy'})[mood]||mood})});
      if(!response.ok||!response.headers.get('Content-Type')?.includes('application/x-ndjson')||!response.body)throw Error('tts_failed');
      await this.readAudio(response.body,null,requestId,slot.controller.signal,slot.stream);
      const normalize=value=>value.normalize('NFKC').replace(/[^\p{L}\p{N}]/gu,'');
      if(normalize(slot.stream.words.map(w=>w.word||'').join(''))!==normalize(text))throw Error('transcript_unverified');
      this.emit('preparation',{key:slot.key,phase:'received',durationMs:Math.round(performance.now()-slot.startedAt)});
    }catch{slot.stream.fail();slot.controller.abort();}finally{clearTimeout(timer);}
  }
  async playStream(current){
    const stream=current.stream,sequence=(current.playSequence||0)+1;current.playSequence=sequence;
    try{
      await this.audioContext().resume();
      await this.stopCallThinking(80);
      if(!this.valid(current)||this.paused||!this.enabled||current.playSequence!==sequence)return;
      if(!stream.node){stream.node=this.context.createMediaElementSource(stream.audio);stream.node.connect(this.analyser);}
      stream.audio.onended=()=>{if(this.valid(current)&&!this.paused&&stream.finished)this.complete(current);};
      await stream.audio.play();
      if(!this.valid(current)||this.paused||current.playSequence!==sequence){stream.audio.pause();return;}
      this.performance(current,'speaking',{resumed:current.performanceStarted===true,mode:'audio',streaming:true});current.performanceStarted=true;
      this.report('playing',`${VOICE} 正在说话`);this.animate(current);
    }catch{if(this.valid(current))this.useText(current,'failed');}
  }
  // Prepare an isolated performance segment without replacing chat, speaking,
  // or using text fallback. Caller owns cancellation and the resulting audio.
  async prepareAudio(text,mood,{signal}={}){
    if(!this.enabled||!this.available||!text||text.length>this.config.maxTextLength)throw Error('voice_unavailable');
    const controller=new AbortController(),abort=()=>controller.abort();
    signal?.addEventListener('abort',abort,{once:true});
    const timer=setTimeout(abort,this.timeoutMs),requestId=crypto.randomUUID();
    try{
      if(signal?.aborted)throw Error('cancelled');
      const response=await this.fetch('/api/tts',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/x-ndjson'},signal:controller.signal,
        body:JSON.stringify({requestId,text,mood:({neutral:'warm',shy:'warm',sad:'warm',surprised:'happy'})[mood]||mood})});
      if(!response.ok||!response.headers.get('Content-Type')?.includes('application/x-ndjson'))throw Error('tts_failed');
      const audio=await this.readAudio(response.body,null,requestId,controller.signal);
      const buffer=await this.audioContext().decodeAudioData(audio.bytes.buffer);
      if(controller.signal.aborted||signal?.aborted)throw Error('cancelled');
      if(!Number.isFinite(buffer.duration)||buffer.duration<=0||buffer.duration>120)throw Error('audio_invalid');
      // Provider subtitle evidence checks text coverage; it is not an independent
      // ASR, an acting-quality score, or proof of a particular emotional effect.
      const normalized=v=>v.normalize('NFKC').replace(/[^\p{L}\p{N}]/gu,'');
      if(normalized(audio.words.map(w=>w.word||'').join(''))!==normalized(text))throw Error('transcript_unverified');
      return {buffer,words:audio.words,requestId};
    }finally{clearTimeout(timer);signal?.removeEventListener('abort',abort);controller.abort();}
  }
  timeline(current,words,duration){
    const points=[{time:0,index:0}];let cursor=0,covered=0,lastStart=0,lastEnd=0;
    for(const word of words){
      const start=Number(word.startTime),end=Number(word.endTime),value=word.word;
      if(typeof value!=='string'||!value||!Number.isFinite(start)||!Number.isFinite(end)||start<lastEnd||end<start||end>duration+.25)return null;
      if(word.confidence!==undefined){
        if(!Number.isFinite(word.confidence)||word.confidence<0||word.confidence>1)return null;
        if(word.confidence<.35)continue;
      }
      const at=current.text.indexOf(value,cursor);if(at<0)return null;
      const before=Array.from(current.text.slice(0,at)).length,after=before+Array.from(value).length;
      points.push({time:Math.min(start,duration),index:before},{time:Math.min(end,duration),index:after});
      cursor=at+value.length;covered+=Array.from(value).length;lastStart=start;lastEnd=end;
    }
    if(!words.length||covered<current.chars.length*.55||lastStart>duration)return null;
    points.push({time:duration,index:current.chars.length});return points;
  }
  position(current){return Math.min(current.buffer?.duration||0,current.offset+(current.source?Math.max(0,this.context.currentTime-current.startedAt):0));}
  captionIndex(current,position){
    if(!current.timeline)return Math.floor(current.chars.length*position/current.buffer.duration);
    const points=current.timeline;let before=points[0],after=points.at(-1);
    for(let index=1;index<points.length;index++){after=points[index];if(after.time>=position)break;before=after;}
    const ratio=after.time>before.time?Math.max(0,Math.min(1,(position-before.time)/(after.time-before.time))):1;
    return Math.floor(before.index+(after.index-before.index)*ratio);
  }
  async play(current){
    if(!this.valid(current)||this.paused||!this.enabled)return;
    const playSequence=(current.playSequence||0)+1;current.playSequence=playSequence;
    try{
      await this.audioContext().resume();await this.stopCallThinking(80);if(!this.valid(current)||this.paused||!this.enabled||current.playSequence!==playSequence)return;
      const source=this.context.createBufferSource();source.buffer=current.buffer;source.connect(this.analyser);
      current.source=source;current.startedAt=this.context.currentTime;
      source.onended=()=>{if(this.valid(current)&&current.source===source&&!this.paused){
        current.offset=current.buffer.duration;current.source=null;source.onended=null;try{source.disconnect();}catch{}this.complete(current);
      }};
      source.start(0,current.offset);this.performance(current,'speaking',{resumed:current.performanceStarted===true||current.detail.resume===true,mode:'audio'});current.performanceStarted=true;this.report('playing',`${VOICE} 正在说话`);this.animate(current);
    }catch{if(this.valid(current))this.useText(current,'failed');}
  }
  animate(current){
    if(!this.valid(current)||(!current.source&&!current.stream)||this.paused)return;
    const index=Math.min(current.chars.length,Math.max(current.index,current.stream?current.stream.caption(current.text,current.stream.audio.currentTime):this.captionIndex(current,this.position(current))));
    if(index!==current.index){current.index=index;this.emit('text',{id:current.id,text:current.chars.slice(0,index).join('')});}
    this.analyser.getFloatTimeDomainData(this.samples);
    const level=Math.sqrt(this.samples.reduce((sum,n)=>sum+n*n,0)/this.samples.length);
    this.emit('activity',{id:current.id,speaking:level>.008,level});
    this.frame=this.requestFrame(()=>{this.frame=null;this.animate(current);});
  }
  complete(current){
    if(!this.valid(current))return;
    this.performance(current,'listening');
    this.stop(current);this.abort(current);current.stream?.dispose();current.stream=null;current.done=true;current.index=current.chars.length;current.buffer=null;
    this.emit('text',{id:current.id,text:current.text});this.emit('activity',{id:current.id,speaking:false,level:0});
    this.report(this.enabled?'ready':'muted',this.enabled?`${VOICE} 已开启`:'声音已静音');this.emit('complete',{id:current.id});
  }
  finishNow(){
    const current=this.current;if(!current||current.done)return;
    this.abort(current);this.stop(current);if(current.mode!=='text')this.startText(current,current.index);this.fallback.finishNow();
  }
  pause(){
    void this.stopCallThinking();
    this.dropWarmSpeech();
    if(this.paused)return;this.paused=true;const current=this.current;
    if(current&&!current.done){
      if(current.mode==='audio')this.stop(current);
      else if(current.mode==='loading'){this.abort(current);this.startText(current,current.index);}
    }
    this.fallback.pause();this.context?.suspend().catch(()=>{});
    this.report('paused','播放已暂停');
  }
  resume(){
    if(!this.paused)return;this.paused=false;const current=this.current;
    if(current&&!current.done){
      if(current.mode==='audio'&&current.stream)void this.playStream(current);
      else if(current.mode==='audio'&&current.buffer)this.play(current);
      else if(current.mode==='waiting'&&this.enabled)this.requestAudio(current);
      else {this.performance(current,'speaking',{resumed:true,mode:'text'});this.fallback.resume();}
    }
  }
  removeRetained(key){const saved=this.retained.get(key);if(saved)this.retainedBytes-=saved.bytes;this.retained.delete(key);}
  clearRetained(){clearTimeout(this.retentionTimer);this.retentionTimer=null;this.retained.clear();this.retainedBytes=0;}
  pruneRetained(){for(const [key,saved]of this.retained)if(saved.expiresAt<=performance.now())this.removeRetained(key);}
  scheduleRetainedExpiry(){
    clearTimeout(this.retentionTimer);this.retentionTimer=null;if(!this.retained.size)return;
    const next=Math.min(...[...this.retained.values()].map(saved=>saved.expiresAt));
    this.retentionTimer=setTimeout(()=>{this.retentionTimer=null;this.pruneRetained();this.scheduleRetainedExpiry();},Math.max(0,next-performance.now()));
  }
  retain(current){
    const key=current.detail.utteranceKey;
    if(current.done||typeof key!=='string'||!key||current.detail.scope==='reply')return;
    this.pruneRetained();this.removeRetained(key);
    let buffer=current.mode==='audio'?current.buffer:null;
    let bytes=buffer?(buffer.length||Math.ceil(buffer.duration*(buffer.sampleRate||24000)))*(buffer.numberOfChannels||1)*4:0;
    if(bytes>this.retention.maxBytes){buffer=null;bytes=0;}
    while(this.retained.size&&(this.retained.size>=this.retention.maxEntries||this.retainedBytes+bytes>this.retention.maxBytes))
      this.removeRetained(this.retained.keys().next().value);
    this.retained.set(key,{text:current.text,index:current.index,buffer,bytes,offset:current.offset,timeline:current.timeline,
      expiresAt:performance.now()+this.retention.maxAgeMs});this.retainedBytes+=bytes;this.scheduleRetainedExpiry();
  }
  clearCurrent(preserve=false){
    const current=this.current;this.current=null;this.sequence++;
    if(current){this.abort(current);this.stop(current);current.stream?.dispose();current.stream=null;if(preserve)this.retain(current);current.buffer=null;}
    this.resetFade();
    this.fallback.cancel();
  }
  cancel({preserveRetained=false}={}){void this.stopCallThinking(80);this.dropWarmSpeech();this.clearCurrent();if(!preserveRetained)this.clearRetained();}
  destroy(){
    this.thinkingPreparation?.controller.abort();this.thinkingPreparation=null;this.thinkingAudio=null;void this.stopCallThinking();
    this.callYieldPreparation?.controller.abort();this.callYieldPreparation=null;this.callYieldAudio=null;
    this.destroyed=true;this.enableSequence++;this.configController?.abort();this.cancel();this.fallback.destroy();
    this.context?.close().catch(()=>{});
  }
}
