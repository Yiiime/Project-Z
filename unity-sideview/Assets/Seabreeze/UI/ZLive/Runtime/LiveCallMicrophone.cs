using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZLive {
// PCM for the existing /asr endpoint. Samples live only for the current spoken turn.
internal sealed class CallOutputReference {
    // Keep half a second of speaker output at 4 kHz. The audio thread only writes.
    readonly float[] history=new float[2048];
    readonly float[] input=new float[80];
    public volatile bool Enabled;
    int written,phase,frames;
    float sum;
    public void Write(float[] data,int channels,int rate){
        if(!Enabled||rate<=0||channels<=0)return;
        int index=System.Threading.Volatile.Read(ref written);
        for(int i=0;i<data.Length;i+=channels){
            sum+=data[i];frames++;phase+=4000;
            if(phase<rate)continue;
            phase-=rate;history[index++ & 2047]=sum/frames;sum=0;frames=0;
        }
        System.Threading.Volatile.Write(ref written,index);
    }
    public bool Matches(float[] block,int channels,int size){
        int end=System.Threading.Volatile.Read(ref written);
        if(end<input.Length)return false;
        double energy=0;
        for(int i=0;i<input.Length;i++){
            float sum=0;int start=i*size/input.Length,stop=(i+1)*size/input.Length;
            for(int k=start;k<stop;k++)sum+=block[k*channels];
            input[i]=sum/Math.Max(1,stop-start);energy+=input[i]*input[i];
        }
        if(energy<1e-8)return false;
        // Find delayed playback, allowing for device latency and polarity changes.
        int first=Math.Max(0,end-history.Length+128);
        for(int at=end-input.Length;at>=first;at--){
            double dot=0,refEnergy=0;
            for(int i=0;i<input.Length;i++){
                float v=history[(at+i)&2047];dot+=v*input[i];refEnergy+=v*v;
            }
            if(refEnergy>1e-6 && dot*dot>.34*refEnergy*energy)return true;
        }
        return false;
    }
}
internal class CallMicrophone : IDisposable {
    readonly string device;
    protected AudioClip clip;
    protected virtual int Position => Microphone.GetPosition(device);
    int cursor;
    public CallMicrophone() {
        if(Microphone.devices.Length==0)throw new LiveProtocolException("microphone_unavailable");
        device=Microphone.devices[0];
        clip=Microphone.Start(device,true,2,16000);
        if(clip==null)throw new LiveProtocolException("microphone_unavailable");
    }
    protected CallMicrophone(AudioClip input){clip=input;}
    public virtual void Dispose(){
        if(clip==null)return;
        if(device!=null)Microphone.End(device);
        UnityEngine.Object.Destroy(clip);clip=null;
    }
    public async Task<byte[]> Listen(Func<Task> frame,CallOutputReference output,Func<bool> checkEcho,Func<Task> onSpeech,CancellationToken token){
        var samples=new List<float>();
        int quiet=0,voiced=0;
        int rate=clip.frequency,channels=clip.channels;
        int size=Math.Max(1,rate/50); // 20 ms blocks
        var block=new float[size*channels];
        bool started=false;
        cursor=Math.Max(0,Position);
        while(true){
            await frame();token.ThrowIfCancellationRequested();
            int position=Position;
            if(position<0)throw new LiveProtocolException("microphone_unavailable");

            int available=(position-cursor+clip.samples)%clip.samples;
            while(available>=size){
                if(!clip.GetData(block,cursor))throw new LiveProtocolException("microphone_unavailable");
                cursor=(cursor+size)%clip.samples;available-=size;
                float energy=0;
                for(int i=0;i<size;i++){
                    float value=0;
                    for(int c=0;c<channels;c++)value+=block[i*channels+c];
                    value/=channels;energy+=value*value;samples.Add(value);
                }
                bool loud=energy/size>.014f*.014f;
                bool echo=loud && checkEcho() && output.Matches(block,channels,size);
                if(echo)samples.RemoveRange(samples.Count-size,size);
                if(loud&&!echo){voiced++;quiet=0;}else{voiced=0;quiet+=size;}
                if(!started && voiced>=4){
                    started=true;
                    await onSpeech();
                    token.ThrowIfCancellationRequested();
                }
                if(!started && samples.Count>size*8)samples.RemoveRange(0,size);
                if(started&&(quiet>=rate*.55f || samples.Count>=rate*19)){
                    int count=(int)((long)samples.Count*16000/rate);
                    var pcm=new byte[count*2];
                    for(int i=0;i<count;i++){
                        short sample=(short)Mathf.RoundToInt(Mathf.Clamp(samples[(int)((long)i*rate/16000)],-1f,1f)*32767);
                        pcm[i*2]=(byte)sample;pcm[i*2+1]=(byte)(sample>>8);
                    }
                    return pcm;
                }
            }
        }
    }
}

public sealed partial class LiveFeatureController {
    CancellationTokenSource localTurnCancel;
    float echoUntil;
    bool MayHearPlayback(){
        if(audioPlayer.Source.isPlaying)echoUntil=Time.unscaledTime+.35f;
        return Time.unscaledTime<echoUntil;
    }
    async Task ListenToCall(CancellationTokenSource owner,string callId){
        var token=owner.Token;
        try{
            while(!token.IsCancellationRequested && rtcCallId==callId){
                if(micMuted||capture==null){await NextFrame();continue;}
                using(var turn=CancellationTokenSource.CreateLinkedTokenSource(token)){
                    localTurnCancel=turn;
                    bool listening=false;
                    try{
                        var pcm=await capture.Listen(NextFrame,echoReference,MayHearPlayback,async()=>{
                            await transport.Input("call.input",new JObject{{"state","listening"}});
                            listening=true;
                        },turn.Token);
                        turn.Token.ThrowIfCancellationRequested();
                        var snapshot=await transport.Request(transport.Route());turn.Token.ThrowIfCancellationRequested();
                        if((string)snapshot["call"]?["id"]!=callId)return;
                        int version=(int)snapshot["call"]["version"];
                        var result=await transport.Transcribe(pcm,version,turn.Token);
                        turn.Token.ThrowIfCancellationRequested();
                        string text=((string)result["text"])?.Trim();
                        if((int?)result["callVersion"]!=version || string.IsNullOrEmpty(text))continue;
                        if(text.Length>360){
                            await transport.Input("call.input",new JObject{{"state","reviewing"}});
                            turn.Token.ThrowIfCancellationRequested();SetVoiceDraft(text,360);input.Focus();Toast("请编辑这一句后发送");
                        }else{
                            await transport.Input("viewer.message",new JObject{{"text",text}});
                            turn.Token.ThrowIfCancellationRequested();ShowPlayerSpeech(text,true);
                        }
                        listening=false;
                    }catch(OperationCanceledException) when(turn.IsCancellationRequested){}
                    catch(LiveProtocolException e) when(e.Message=="stale_voice_turn") {listening=false;}
                    finally{
                        localTurnCancel=null;
                        if(listening&&!token.IsCancellationRequested&&rtcCallId==callId)
                            await transport.Input("call.input",new JObject{{"state","idle"}});
                    }
                }
            }
        }catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception e){if(!token.IsCancellationRequested && rtcCallId==callId){Toast(FriendlyError(e));LeaveCall();}}
    }
}
}
