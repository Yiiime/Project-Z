import {Pcm16Resampler} from './pcm.js';
class ZAsrCapture extends AudioWorkletProcessor {
  constructor(){super();this.resampler=new Pcm16Resampler(sampleRate);this.stopped=false;this.port.onmessage=({data})=>{if(data?.type==='flush'){this.stopped=true;const frame=this.resampler.flush();if(frame)this.port.postMessage(frame,[frame.pcm]);this.port.postMessage({type:'flushed'});}};}
  process(inputs,outputs){if(this.stopped)return true;const channels=inputs[0];if(channels?.length){const mono=new Float32Array(channels[0].length);for(const channel of channels)for(let i=0;i<mono.length;i++)mono[i]+=channel[i]/channels.length;
    for(const frame of this.resampler.push(mono))this.port.postMessage(frame,[frame.pcm]);}
    for(const channel of outputs[0]||[])channel.fill(0);return true;
  }
}
registerProcessor('z-asr-capture',ZAsrCapture);
