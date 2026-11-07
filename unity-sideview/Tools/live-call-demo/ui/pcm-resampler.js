// Streaming box-filter resampler: preserve phase across AudioWorklet blocks.
export class Pcm16Resampler {
  constructor(rate,frameSamples=3200){if(!Number.isFinite(rate)||rate<16000)throw Error('unsupported_sample_rate');this.ratio=rate/16000;this.remaining=this.ratio;this.sum=0;this.samples=[];this.frameSamples=frameSamples;}
  push(input){const frames=[];for(const sample of input){let remaining=1;while(remaining>1e-9){const weight=Math.min(remaining,this.remaining);this.sum+=(Number.isFinite(sample)?sample:0)*weight;remaining-=weight;this.remaining-=weight;
    if(this.remaining<1e-9){this.samples.push(Math.max(-1,Math.min(1,this.sum/this.ratio)));this.remaining=this.ratio;this.sum=0;if(this.samples.length===this.frameSamples)frames.push(this.flush());}
  }}return frames;}
  flush(){if(!this.samples.length)return null;const buffer=new ArrayBuffer(this.samples.length*2),view=new DataView(buffer);let energy=0;this.samples.forEach((x,i)=>{energy+=x*x;view.setInt16(i*2,Math.round(x*(x<0?32768:32767)),true);});const frame={pcm:buffer,rms:Math.sqrt(energy/this.samples.length),durationMs:this.samples.length/16};this.samples=[];return frame;}
}
