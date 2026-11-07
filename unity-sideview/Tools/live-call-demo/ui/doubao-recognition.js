// Web Speech-shaped adapter; all recognition is performed by Doubao through the local bridge.
export class DoubaoRecognition {
  static isSupported(){return !!(globalThis.navigator?.mediaDevices?.getUserMedia&&(globalThis.AudioContext||globalThis.webkitAudioContext)&&globalThis.AudioWorkletNode&&globalThis.WebSocket);}
  constructor({fetchImpl=globalThis.fetch?.bind(globalThis),WebSocketClass=globalThis.WebSocket,mediaDevices=globalThis.navigator?.mediaDevices,Context=globalThis.AudioContext||globalThis.webkitAudioContext,Worklet=globalThis.AudioWorkletNode,location=globalThis.location}={}){
    Object.assign(this,{fetch:fetchImpl,WebSocketClass,mediaDevices,Context,Worklet,location});this.state='new';this.revision=0;this.floor=.003;this.hotMs=0;this.prebuffer=[];
  }
  start(){if(this.state!=='new')throw Error('asr_already_started');this.state='starting';this.controller=new AbortController();
    try{this.context=new this.Context();this.resuming=this.context.resume();this.resuming.catch(()=>{});}catch{this.fail('audio-capture');return;}
    void this.open();
  }
  async open(){
    this.timer=setTimeout(()=>this.fail('asr_connect_timeout'),12000);
    try{
      const response=await this.fetch('/api/asr/config',{cache:'no-store',signal:this.controller.signal});if(!response.ok)throw Error('asr_not_configured');const config=await response.json();if(!config.enabled||config.provider!=='doubao')throw Error('asr_not_configured');
      if(this.state!=='starting')return;
      const stream=await this.mediaDevices.getUserMedia({audio:{channelCount:1,echoCancellation:true,noiseSuppression:true,autoGainControl:false},video:false});
      if(this.state!=='starting'){stream.getTracks().forEach(t=>t.stop());return;}this.stream=stream;
      await this.resuming;if(this.state!=='starting')return;
      await this.context.audioWorklet.addModule('/api/asr/worklet.js');if(this.state!=='starting')return;
      this.capture();
      const url=new URL('/api/asr/stream',this.location.href);url.protocol=url.protocol==='https:'?'wss:':'ws:';
      const socket=this.socket=new this.WebSocketClass(url.href);socket.binaryType='arraybuffer';
      socket.onerror=()=>this.fail('asr_connection_failed');socket.onclose=()=>{if(this.state!=='closed')this.fail('asr_disconnected');};
      socket.onmessage=event=>{if(this.state==='closed')return;try{this.receive(JSON.parse(event.data));}catch{this.fail('asr_invalid_response');}};
    }catch(e){if(this.state==='closed')return;this.fail(e.name==='NotAllowedError'?'not-allowed':e.name==='NotFoundError'?'audio-capture':e.message==='asr_not_configured'?e.message:'asr_connection_failed');}
  }
  receive(message){
    if(!message||typeof message.sessionId!=='string')throw Error('invalid_session');
    if(this.sessionId&&this.sessionId!==message.sessionId)throw Error('stale_session');this.sessionId=message.sessionId;
    if(message.type==='error'){this.fail(message.code);return;}
    if(message.type==='ready'){
      if(this.state!=='starting')return;clearTimeout(this.timer);this.state='listening';
      for(const frame of this.prebuffer)this.push(frame);this.prebuffer=[];this.onstart?.();return;
    }
    if(message.type==='result'){
      if(!['listening','finishing'].includes(this.state)||!Number.isInteger(message.revision)||message.revision<=this.revision)return;
      if(!Array.isArray(message.results)||message.results.length>128)throw Error('invalid_results');this.revision=message.revision;
      const results=message.results.map(r=>{if(typeof r.text!=='string'||typeof r.final!=='boolean'||r.text.length>4000)throw Error('invalid_result');const row=[{transcript:r.text,confidence:1}];row.isFinal=r.final;return row;});
      // Confidence=1 is a compatibility sentinel for provider-definite text, not a measured score.
      this.onresult?.({resultIndex:0,results,provider:'doubao'});return;
    }
    if(message.type==='end'){const end=this.onend;this.cleanup();end?.();return;}
    throw Error('unknown_event');
  }
  capture(){this.source=this.context.createMediaStreamSource(this.stream);this.worklet=new this.Worklet(this.context,'z-asr-capture');this.sink=this.context.createGain();this.sink.gain.value=0;this.worklet.port.onmessage=({data})=>{if(data?.type==='flushed')this.finishSending();else this.push(data);};this.source.connect(this.worklet);this.worklet.connect(this.sink);this.sink.connect(this.context.destination);}
  push({pcm,rms,durationMs}){
    if(this.state==='starting'){if(this.prebuffer.length>=10)return this.fail('asr_connect_timeout');this.prebuffer.push({pcm,rms,durationMs});return;}
    if(!['listening','finishing'].includes(this.state))return;
    if(!(pcm instanceof ArrayBuffer)||!pcm.byteLength||pcm.byteLength>12800)return this.fail('asr_invalid_audio');
    if(this.socket.bufferedAmount>128000)return this.fail('asr_backpressure');this.socket.send(pcm);
    if(rms<this.floor*1.7)this.floor=.98*this.floor+.02*rms;
    const above=rms>Math.max(.008,this.floor*2.8);this.hotMs=above?this.hotMs+durationMs:0;
    this.onacoustic?.({available:true,rms,noiseFloor:this.floor,voiceLikely:this.hotMs>=250,noisy:false});
  }
  stop(){if(this.state==='closed'||this.state==='finishing')return;if(this.state!=='listening'){const end=this.onend;this.cleanup();end?.();return;}
    this.state='finishing';clearTimeout(this.timer);this.flushTimer=setTimeout(()=>this.finishSending(),500);this.worklet.port.postMessage({type:'flush'});
  }
  finishSending(){if(this.state!=='finishing'||this.finishSent)return;this.finishSent=true;clearTimeout(this.flushTimer);this.releaseAudio();this.timer=setTimeout(()=>this.fail('asr_final_timeout'),7500);try{this.socket.send(JSON.stringify({type:'finish'}));}catch{this.fail('asr_disconnected');}}
  releaseAudio(){if(this.worklet)this.worklet.port.onmessage=null;this.worklet?.disconnect();this.source?.disconnect();this.sink?.disconnect();this.stream?.getTracks().forEach(t=>t.stop());this.stream=null;const context=this.context;this.context=null;void context?.close().catch(()=>{});}
  cleanup(){if(this.state==='closed')return;this.state='closed';clearTimeout(this.timer);clearTimeout(this.flushTimer);this.controller?.abort();this.prebuffer=[];this.releaseAudio();if(this.socket){this.socket.onmessage=null;this.socket.onclose=null;this.socket.onerror=null;try{this.socket.close();}catch{}}}
  abort(){this.cleanup();}
  fail(error){if(this.state==='closed')return;const notify=this.onerror;this.cleanup();notify?.({error});}
}
