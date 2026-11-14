using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZLive {
public sealed partial class LiveAudioPlayer {
    CancellationTokenSource thinking;
    public void CancelThinking(){thinking?.Cancel();}
    // Local waiting feedback never produces dialogue, memory or completion receipts.
    public async Task Thinking(JObject meta,CancellationToken cancel) {
        if(thinking!=null||Paused||!await voiceGate.WaitAsync(0,cancel))return;
        var own=thinking=CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try{var clip=await Decode(meta,own.Token);own.Token.ThrowIfCancellationRequested();await WaitReady(own.Token);Source.clip=clip;Source.Play();Character?.BeginSpeaking();
            float end=Time.unscaledTime+clip.length+.5f;while(Source.isPlaying&&Time.unscaledTime<end){own.Token.ThrowIfCancellationRequested();await Frame();}
        }catch(OperationCanceledException){float volume=Source.volume,began=Time.unscaledTime;while(Source.isPlaying&&Time.unscaledTime-began<.1f){Source.volume=volume*(1-(Time.unscaledTime-began)/.1f);await Frame();}Source.volume=volume;}
        catch(Exception){}finally{Source.Stop();Character?.EndCue();if(thinking==own)thinking=null;own.Dispose();voiceGate.Release();}
    }
    float? fadedVolume;string playingSpeechId;
    public async Task FadeOut(string speechId,int milliseconds,CancellationToken cancel) {
        if(!Source.isPlaying||playingSpeechId!=speechId)return;var clip=Source.clip;int generation=stopGeneration;
        float volume=Source.volume;fadedVolume??=volume;float began=Time.unscaledTime,seconds=Mathf.Clamp(milliseconds,0,200)/1000f;
        while(Source.clip==clip&&generation==stopGeneration&&Time.unscaledTime-began<seconds){cancel.ThrowIfCancellationRequested();Source.volume=volume*(1-(Time.unscaledTime-began)/seconds);await Frame();}
        if(Source.clip==clip&&generation==stopGeneration)Source.volume=0;
    }
    void RestoreFade(){if(fadedVolume.HasValue){Source.volume=fadedVolume.Value;fadedVolume=null;}}
    async Task HostPcm(JObject command,CancellationToken cancel) {
        var p=(JObject)command["payload"];string text=(string)p["text"];
        var events=new ConcurrentQueue<JObject>();using(var own=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
            var download=Transport.ReadPcm((string)p["audioStreamId"],events.Enqueue,own.Token);
            var clip=AudioClip.Create("Z Vivi stream",24000*90,1,24000,false);var samples=new List<float>();var words=new JArray();
            bool started=false,finished=false,buffering=false;int index=0,lastSample=0,generation=outputGeneration,recoveries=0,stoppedAt=stopGeneration;
            float lastReport=-1000,lastData=Time.unscaledTime,stalled=0;bool header=false;
            try {
                while(true) {
                    cancel.ThrowIfCancellationRequested();if(stoppedAt!=stopGeneration)throw new OperationCanceledException();
                    while(events.TryDequeue(out var e)) {
                        lastData=Time.unscaledTime;
                        switch((string)e["type"]) {
                            case "start":if(header||(int?)e["sampleRate"]!=24000)throw new Exception("invalid_audio_stream");header=true;break;
                            case "audio":
                                if(!header||(int?)e["index"]!=index++)throw new Exception("invalid_audio_stream");
                                var bytes=Convert.FromBase64String((string)e["data"]);if(bytes.Length%2!=0||bytes.Length==0||samples.Count+bytes.Length/2>clip.samples)throw new Exception("invalid_audio_stream");
                                var part=new float[bytes.Length/2];for(int i=0;i<part.Length;i++)part[i]=(short)(bytes[i*2]|bytes[i*2+1]<<8)/32768f;
                                if(!clip.SetData(part,samples.Count))throw new Exception("audio_unreadable");samples.AddRange(part);break;
                            case "words":foreach(var word in (JArray)e["words"])words.Add(word.DeepClone());break;
                            case "done":if(!header||samples.Count==0||Math.Abs((double)e["durationMs"]-samples.Count/24d)>1)throw new Exception("invalid_audio_stream");finished=true;break;
                            default:throw new Exception("tts_stream_failed");
                        }
                    }
                    if(download.IsFaulted)await download;if(download.IsCanceled)throw new OperationCanceledException();
                    if(!started&&(samples.Count>=2400||finished)) {
                        // Waiting behind the intro is intentional; queued PCM remains buffered.
                        await WaitReady(cancel);lastData=Time.unscaledTime;RestoreFade();Source.clip=clip;Source.Play();Character?.BeginSpeaking();started=true;
                        await Receipt(command,new JObject{{"status","started"},{"durationMs",0},{"positionMs",0}});
                    }
                    if(started&&!Paused) {
                        int sample=Math.Min(Source.timeSamples,samples.Count);float elapsed=Time.unscaledDeltaTime;
                        bool advanced=sample>lastSample;stalled=advanced?0:stalled+elapsed;lastSample=Math.Max(lastSample,sample);
                        if(outputGeneration!=generation||!buffering&&stalled>.75f) {
                            if(recoveries++>0)throw new Exception("audio_output_unavailable");Source.Stop();var old=clip;
                            clip=AudioClip.Create("Z Vivi recovered stream",24000*90,1,24000,false);clip.SetData(samples.ToArray(),0);Destroy(old);Source.clip=clip;Source.timeSamples=lastSample;Source.Play();generation=outputGeneration;stalled=0;
                        }
                        if(!finished&&sample>=samples.Count-240){Source.Pause();Source.timeSamples=lastSample;buffering=true;}
                        if(buffering&&(samples.Count-lastSample>=2400||finished)){Source.UnPause();buffering=false;stalled=0;}
                        float position=lastSample/24f;string shown=words.Count>0?PrefixAt(text,words,lastSample/24000f,samples.Count/24000f):"";Caption?.Invoke(shown);
                        if(position-lastReport>=300){lastReport=position;await Receipt(command,new JObject{{"status","progress"},{"positionMs",Mathf.RoundToInt(position)},{"text",shown}});}
                        if(finished&&lastSample>=samples.Count){await download;Source.Stop();Caption?.Invoke(text);await Receipt(command,new JObject{{"status","completed"},{"positionMs",Mathf.RoundToInt(samples.Count/24f)},{"durationMs",Mathf.RoundToInt(samples.Count/24f)}});break;}
                    }
                    if(Time.unscaledTime-lastData>20&&!finished)throw new Exception("tts_stream_failed");
                    await Frame();
                }
            }finally{own.Cancel();try{await download;}catch{}Source.Stop();Destroy(clip);}
        }
    }
}
}
