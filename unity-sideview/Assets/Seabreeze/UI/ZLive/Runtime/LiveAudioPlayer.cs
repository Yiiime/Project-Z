using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
namespace ZLive {
public sealed partial class LiveAudioPlayer:MonoBehaviour {
    public AudioSource Source{get;private set;}public CharacterPresenter Character;public bool Paused{get;private set;}
    public bool PresentationHeld;
    public Action<string> Caption;public Func<Task> Frame;public Func<JObject,JObject,Task> Receipt;public LiveTransport Transport;
    readonly Dictionary<string,AudioClip> cache=new Dictionary<string,AudioClip>();readonly Dictionary<string,JArray> manifests=new Dictionary<string,JArray>();
    readonly HashSet<string> titleManifests=new HashSet<string>();
    readonly SemaphoreSlim voiceGate=new SemaphoreSlim(1,1);
    int outputGeneration,stopGeneration;
    readonly HashSet<string> invalidatedClips=new HashSet<string>();
    void OnEnable(){AudioSettings.OnAudioConfigurationChanged+=OutputChanged;}
    void OnDisable(){AudioSettings.OnAudioConfigurationChanged-=OutputChanged;}
    void OutputChanged(bool deviceChanged){outputGeneration++;foreach(var id in cache.Keys)invalidatedClips.Add(id);Debug.Log("ZLive audio: output configuration changed; device="+deviceChanged);}
    void Awake(){Source=gameObject.AddComponent<AudioSource>();Source.playOnAwake=false;Source.ignoreListenerPause=true;Source.loop=false;Source.spatialBlend=0;Source.volume=.85f;}
    public void Pause(bool paused){if(Paused==paused)return;Paused=paused;if(paused)Source.Pause();else Source.UnPause();if(Character!=null)Character.Paused=paused;}
    public void Stop(){CancelThinking();stopGeneration++;Source.Stop();RestoreFade();Character?.EndCue();}
    public async Task<AudioClip> Decode(JObject meta,CancellationToken cancel){string id=(string)meta["artifactId"];if(cache.TryGetValue(id,out var clip)&&!invalidatedClips.Contains(id))return clip;
        if(clip!=null){Destroy(clip);cache.Remove(id);}var bytes=await Transport.Artifact(id,cancel);cancel.ThrowIfCancellationRequested();string mime=(string)meta["contentType"],ext=mime=="audio/wav"?".wav":mime=="audio/mpeg"?".mp3":throw new Exception("unsupported_audio");
        string dir=Path.Combine(Application.temporaryCachePath,"z-live-audio");Directory.CreateDirectory(dir);string file=Path.Combine(dir,id+ext);File.WriteAllBytes(file,bytes);
        try{using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri,ext==".wav"?AudioType.WAV:AudioType.MPEG))using(cancel.Register(()=>request.Abort())){var op=request.SendWebRequest();while(!op.isDone){cancel.ThrowIfCancellationRequested();await Frame();}if(request.result!=UnityWebRequest.Result.Success)throw new Exception("decode_failed");clip=DownloadHandlerAudioClip.GetContent(request);if(clip==null||clip.length<=0||clip.length>180||clip.channels<1||clip.channels>2)throw new Exception("invalid_audio");
            float energy=0;var samples=new float[Math.Min(4096,clip.samples*clip.channels)];for(int i=0;i<8;i++){if(!clip.GetData(samples,Mathf.Max(0,(clip.samples-samples.Length/clip.channels)*i/8)))throw new Exception("audio_unreadable");foreach(float v in samples){if(float.IsNaN(v)||float.IsInfinity(v))throw new Exception("invalid_audio");energy+=v*v;}}if(energy<.000001f)throw new Exception("audio_silent");cache[id]=clip;invalidatedClips.Remove(id);return clip;}}
        finally{if(File.Exists(file))File.Delete(file);}
    }
    public async Task Prepare(JObject command,CancellationToken cancel){var p=(JObject)command["payload"];var clips=(JArray)p["clips"];if(clips==null||clips.Count<1||clips.Count>6)throw new Exception("invalid_manifest");float duration=0;foreach(JObject meta in clips){var clip=await Decode(meta,cancel);duration+=clip.length*1000+((int?)meta["pauseAfterMs"]??0);}if(duration>180000)throw new Exception("audio_too_long");manifests[(string)p["artifactId"]]=(JArray)clips.DeepClone();if((string)p["kind"]=="singing")titleManifests.Add((string)p["artifactId"]);await Receipt(command,new JObject{{"status","ready"},{"durationMs",Mathf.RoundToInt(duration)}});}
    public async Task Host(JObject command,CancellationToken cancel){var p=(JObject)command["payload"];if(p["actorId"]!=null&&(string)p["actorId"]!="z")throw new Exception("host_actor_mismatch");CancelThinking();await voiceGate.WaitAsync(cancel);try{
        playingSpeechId=(string)p["speechId"];string text=(string)p["text"];Character?.Cue((string)p["mood"],(string)p["gesture"],(string)p["transition"],(float?)p["gestureHoldMs"]??0,(bool?)p["carryArms"]??false,(string)p["expression"]);bool audio=(string)p["modality"]=="audio";RestoreFade();if((string)p["modality"]=="pcm-stream"){await HostPcm(command,cancel);return;}
        if(audio){var clip=await Decode(new JObject{{"artifactId",p["audioArtifactId"]},{"contentType","audio/mpeg"}},cancel);await WaitReady(cancel);Source.clip=clip;Source.Play();Character?.BeginSpeaking();await Frame();cancel.ThrowIfCancellationRequested();await Receipt(command,new JObject{{"status","started"},{"durationMs",Mathf.RoundToInt(clip.length*1000)},{"positionMs",0}});float durationMs=clip.length*1000;await PlayClip(command,clip,text,(JArray)p["words"],0,-1,cancel);await Receipt(command,new JObject{{"status","completed"},{"positionMs",Mathf.RoundToInt(durationMs)}});
        }else{float duration=Mathf.Clamp(text.Length*80,1200,22000),position=0,last=-1000;await WaitReady(cancel);Character?.BeginSpeaking(true);await Receipt(command,new JObject{{"status","started"},{"durationMs",Mathf.RoundToInt(duration)},{"positionMs",0}});
            while(position<duration){await Frame();cancel.ThrowIfCancellationRequested();if(Paused)continue;position=Mathf.Min(duration,position+Time.unscaledDeltaTime*1000);string shown=SafePrefix(text,Mathf.FloorToInt(text.Length*position/duration));Caption?.Invoke(shown);if(position-last>=600){last=position;await Receipt(command,new JObject{{"status","progress"},{"positionMs",Mathf.RoundToInt(position)},{"text",shown}});}}
            Caption?.Invoke(text);await Frame();await Receipt(command,new JObject{{"status","completed"},{"positionMs",Mathf.RoundToInt(duration)}});
        }
    }finally{Caption?.Invoke("");playingSpeechId=null;Source.Stop();RestoreFade();Character?.EndCue();voiceGate.Release();}}
    public async Task Play(JObject command,CancellationToken cancel){var p=(JObject)command["payload"];if(!manifests.TryGetValue((string)p["artifactId"],out var clips))throw new Exception("media_not_prepared");await voiceGate.WaitAsync(cancel);try{float total=0;foreach(JObject meta in clips)total+=(await Decode(meta,cancel)).length*1000+((int?)meta["pauseAfterMs"]??0);float offset=0;await WaitReady(cancel);
        for(int i=0;i<clips.Count;i++){var meta=(JObject)clips[i];var clip=await Decode(meta,cancel);float clipDurationMs=clip.length*1000;Character?.Cue((string)meta["mood"],(string)meta["gesture"],"smooth");Caption?.Invoke((string)meta["text"]);Source.clip=clip;Source.Play();Character?.BeginSpeaking();await Frame();cancel.ThrowIfCancellationRequested();if(i==0)await Receipt(command,new JObject{{"status","started"},{"durationMs",Mathf.RoundToInt(total)},{"positionMs",0}});
            await Receipt(command,new JObject{{"status","progress"},{"positionMs",Mathf.RoundToInt(offset)},{"lineIndex",i}});await PlayClip(command,clip,(string)meta["text"],meta["words"] as JArray,offset,i,cancel,titleManifests.Contains((string)p["artifactId"]));offset+=clipDurationMs;float pause=(int?)meta["pauseAfterMs"]??0,waited=0;while(waited<pause){await Frame();cancel.ThrowIfCancellationRequested();if(!Paused)waited+=Time.unscaledDeltaTime*1000;}offset+=pause;
        }await Receipt(command,new JObject{{"status","completed"},{"positionMs",Mathf.RoundToInt(total)}});
    }finally{Caption?.Invoke("");playingSpeechId=null;Source.Stop();RestoreFade();Character?.EndCue();voiceGate.Release();}}
    async Task WaitReady(CancellationToken cancel){while(Paused || PresentationHeld){cancel.ThrowIfCancellationRequested();await Frame();}cancel.ThrowIfCancellationRequested();}
    async Task PlayClip(JObject command,AudioClip clip,string text,JArray words,float offset,int line,CancellationToken cancel,bool titleOnly=false) {
        float last=-1000,seconds=0,stalled=0,activeTime=0;
        int generation=outputGeneration,stoppedAt=stopGeneration,recoveries=0,previousSample=Source.timeSamples;
        int sampleCount=clip.samples,channels=clip.channels,frequency=clip.frequency;float duration=clip.length;seconds=previousSample/(float)frequency;
        // Device resets can invalidate dynamically decoded clips. Keep this clip's PCM
        // until playback ends so a reconnect can resume at the last confirmed sample.
        var pcm=new float[clip.samples*clip.channels];
        if(!clip.GetData(pcm,0))throw new Exception("audio_unreadable");
        while(true) {
            await Frame();cancel.ThrowIfCancellationRequested();if(stoppedAt!=stopGeneration)throw new OperationCanceledException();if(Paused)continue;
            float delta=Time.unscaledDeltaTime;activeTime+=delta;
            int sample=Source.timeSamples;
            bool changed=generation!=outputGeneration;
            bool advanced=sample>previousSample;
            if(advanced){seconds=sample/(float)frequency;previousSample=sample;stalled=0;}else stalled+=delta;
            // A stopped source is completion only after it actually reached the end.
            if(!changed&&!Source.isPlaying&&seconds>0&&seconds>=duration-.15f)break;
            if(changed||stalled>.75f) {
                if(recoveries++>=1)throw new Exception("audio_output_unavailable");
                Source.Stop();
                if(!changed&&!AudioSettings.Reset(AudioSettings.GetConfiguration()))throw new Exception("audio_output_unavailable");
                cancel.ThrowIfCancellationRequested();
                var replacement=AudioClip.Create("Z recovered audio",sampleCount,channels,frequency,false);
                if(!replacement.SetData(pcm,0)){Destroy(replacement);throw new Exception("audio_unreadable");}
                foreach(string id in new List<string>(cache.Keys))if(cache[id]==clip){cache[id]=replacement;invalidatedClips.Remove(id);}
                Destroy(clip);clip=replacement;
                Source.clip=clip;Source.timeSamples=Mathf.Clamp(previousSample,0,sampleCount-1);
                Source.Play();if(Paused)Source.Pause();
                generation=outputGeneration;stalled=0;
                Debug.Log("ZLive audio: recovered output at sample "+previousSample);
                continue;
            }
            float ms=Mathf.Min(duration*1000,seconds*1000);
            string shown=titleOnly?text:PrefixAt(text,words,seconds,duration);Caption?.Invoke(shown);
            if(ms-last>=600){last=ms;var evidence=new JObject{{"status","progress"},{"positionMs",Mathf.RoundToInt(offset+ms)}};if(line<0)evidence["text"]=shown;await Receipt(command,evidence);}
            if(activeTime>duration+5)throw new Exception("audio_timeout");
        }
        Caption?.Invoke(text);
    }
    static string SafePrefix(string text,int count){count=Mathf.Clamp(count,0,text.Length);if(count>0&&count<text.Length&&char.IsHighSurrogate(text[count-1]))count--;return text.Substring(0,count);}
    static string PrefixAt(string text,JArray words,float seconds,float duration){int cursor=0;if(words!=null&&words.Count>0){foreach(JObject w in words){string word=(string)w["word"];if(string.IsNullOrEmpty(word)||(float?)w["endTime"]>seconds)break;int at=text.IndexOf(word,cursor,StringComparison.Ordinal);if(at<0)break;cursor=at+word.Length;}return SafePrefix(text,cursor);}return SafePrefix(text,Mathf.FloorToInt(text.Length*seconds/duration));}
    void OnDestroy(){foreach(var clip in cache.Values)if(clip!=null)Destroy(clip);cache.Clear();}
}
}
