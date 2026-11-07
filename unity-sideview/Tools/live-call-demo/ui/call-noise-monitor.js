// Local acoustic feedback only. Not speaker identification, neural VAD or an ASR denoiser.
export class BrowserNoiseMonitor {
  constructor({onChange=()=>{}, sampleIntervalMs=80}={}) {this.onChange=onChange;this.intervalMs=sampleIntervalMs;this.sequence=0;this.state={available:false,rms:0,noiseFloor:0,voiceLikely:false,noisy:false};}
  async start() {
    if(!globalThis.navigator?.mediaDevices?.getUserMedia)return false;
    const Context=globalThis.AudioContext||globalThis.webkitAudioContext;if(!Context)return false;
    const sequence=++this.sequence;
    try {
      const stream=await navigator.mediaDevices.getUserMedia({audio:{channelCount:1,echoCancellation:true,noiseSuppression:true,autoGainControl:false},video:false});
      if(sequence!==this.sequence){stream.getTracks().forEach(track=>track.stop());return false;}
      this.stream=stream;this.context=new Context();await this.context.resume();
      if(sequence!==this.sequence){await this.stop();return false;}
      this.source=this.context.createMediaStreamSource(stream);this.analyser=this.context.createAnalyser();this.analyser.fftSize=1024;this.source.connect(this.analyser);
      this.samples=new Float32Array(1024);this.floor=.003;this.warmup=0;this.hotFrames=0;this.highNoiseFrames=0;
      this.timer=setInterval(()=>this.sample(),this.intervalMs);return true;
    } catch {await this.stop();this.onChange({...this.state,available:false});return false;}
  }
  sample() {
    if(!this.analyser)return;
    this.analyser.getFloatTimeDomainData(this.samples);
    const rms=Math.sqrt(this.samples.reduce((sum,value)=>sum+value*value,0)/this.samples.length);
    if(!Number.isFinite(rms))return;
    // Adapt only near the floor: sustained foreground must not become the noise reference.
    if(this.warmup++<12)this.floor=this.floor*.85+rms*.15;
    else if(rms<this.floor*1.7)this.floor=this.floor*.98+rms*.02;
    const above=rms>Math.max(.008,this.floor*2.8);
    this.hotFrames=above?this.hotFrames+1:0;
    this.highNoiseFrames=this.floor>.018?this.highNoiseFrames+1:Math.max(0,this.highNoiseFrames-1);
    this.state={available:true,rms,noiseFloor:this.floor,voiceLikely:this.hotFrames>=3,noisy:this.highNoiseFrames>=12};
    this.onChange({...this.state});
  }
  async stop() {
    this.sequence++;clearInterval(this.timer);this.timer=null;
    this.stream?.getTracks().forEach(track=>track.stop());this.stream=null;
    this.source?.disconnect();this.source=null;this.analyser=null;
    const context=this.context;this.context=null;try{await context?.close();}catch{}
    this.state={available:false,rms:0,noiseFloor:0,voiceLikely:false,noisy:false};
  }
}
