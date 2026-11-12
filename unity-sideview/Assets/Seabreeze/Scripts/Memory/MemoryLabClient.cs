using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
namespace Seabreeze
{
    public sealed class MemoryLabClient : MonoBehaviour
    {
        GameSession session;
        HttpClient http;
        readonly Dictionary<string,CancellationTokenSource> requests=new Dictionary<string,CancellationTokenSource>();
        int generation;
        int speechVersion;
        CancellationTokenSource speechCancellation;
        Coroutine speechRoutine;
        AudioSource speechSource;
        AudioClip speechClip;
        string speechConversation="",speechProposal="";
        public string SpeechStatus {get;private set;}="";
        public bool SpeechPlaying => speechSource!=null&&speechSource.isPlaying;
        public bool SpeechBusy {get;private set;}
        readonly float[] speechSamples=new float[512];
        public float SpeechAmplitude
        {
            get
            {
                if(!SpeechPlaying)return 0;
                speechSource.GetOutputData(speechSamples,0);
                float energy=0;foreach(float sample in speechSamples)energy+=sample*sample;
                return Mathf.Clamp01(Mathf.Sqrt(energy/speechSamples.Length)*8);
            }
        }
        const int SpeechRate=24000,MaxSpeechSamples=2*1024*1024,SpeechPrebuffer=7200;
        int receivedSpeechSamples,subtitleTextCursor;bool speechStreamComplete;
        string spokenText="";float captionRate=5;
        readonly Dictionary<string,float> actorCaptionRates=new Dictionary<string,float>();
        struct SpeechCue { public int start,end;public float from,to; }
        readonly List<SpeechCue> speechCues=new List<SpeechCue>();
        public bool SpeechStreamComplete => speechStreamComplete;
        public float SpeechFirstAudioMs {get;private set;}=-1;
        public float SpeechPlaybackStartMs {get;private set;}=-1;
        public float SpeechCompleteMs {get;private set;}=-1;
        public float SpeechDuration => speechStreamComplete?(float)receivedSpeechSamples/SpeechRate:0;
        public int SpeechCaptionEnd
        {
            get
            {
                float position=SpeechPosition,ahead=Mathf.Min(position*1.04f+.12f,position+.35f);
                foreach(var cue in speechCues)
                {
                    if(ahead<cue.from)return cue.start;
                    if(ahead<cue.to)return Mathf.FloorToInt(Mathf.Lerp(cue.start,cue.end,Mathf.InverseLerp(cue.from,cue.to,ahead)));
                }
                float estimated;
                if(speechCues.Count>0){var last=speechCues[speechCues.Count-1];estimated=last.end+Mathf.Max(0,ahead-last.to)*captionRate;}
                else if(SpeechDuration>0)estimated=spokenText.Length*ahead/SpeechDuration;
                else estimated=ahead*captionRate;
                return Mathf.Clamp(Mathf.FloorToInt(estimated),0,Mathf.Max(0,spokenText.Length-(speechStreamComplete?0:1)));
            }
        }
        public float SpeechPosition => speechSource!=null&&speechClip!=null?(float)speechSource.timeSamples/speechClip.frequency:0;

        int acknowledgedRevision;
        bool opened,opening,syncing,checking,focusing;int focusVersion;
        float nextHealth,nextOpen,retryModelAt;
        public string RunId {get;private set;}="";
        public string LastError {get;private set;}="";
        public string BackgroundError {get;private set;}="";
        public string Mode {get;private set;}="real";
        public string Model {get;private set;}="";
        public bool Reachable {get;private set;}
        public bool Configured {get;private set;}
        public int ConfigurationRevision {get;private set;}
        public event Action ConnectionChanged;
        public bool Synchronized => opened&&!opening&&!syncing&&session?.State?.memory!=null&&session.State.memory.outbox.Count==0;
        // Accept input during journal sync. PlayerReply waits until that sync completes.
        public bool CanQueuePlayerInput => opened&&!opening&&!focusing&&Reachable&&Configured&&Time.unscaledTime>=retryModelAt;
        public bool CanGenerate => Synchronized&&CanQueuePlayerInput;
        public string Status => !Reachable?"记忆服务未连接":!Configured?"尚未配置模型":!Synchronized?"正在同步记忆":Time.unscaledTime<retryModelAt?"模型暂时不可用，稍后重试":Mode=="simulated"?"模拟模型验收":"模型已连接";
        public void Initialize(GameSession owner)
        {
            session=owner;
            http=new HttpClient(new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
            BeginWorld();
        }
        public void BeginWorld()
        {
            StopSpeech(false);
            generation++;focusVersion++;focusing=false;foreach(var c in requests.Values)c.Cancel();requests.Clear();
            StopAllCoroutines();opened=opening=syncing=checking=false;
            RunId=Guid.NewGuid().ToString("N");nextHealth=nextOpen=retryModelAt=0;LastError="";BackgroundError="";acknowledgedRevision=0;
        }
        public MemoryRequest RequestBase()
        {
            return new MemoryRequest { worldId=session.State.memory.worldId,runId=RunId,requestId=Guid.NewGuid().ToString("N"),day=session.State.day,period=session.State.period };
        }
        void Update()
        {
            if(session==null||session.State?.memory==null||http==null)return;
            if(!checking&&Time.unscaledTime>=nextHealth){checking=true;nextHealth=Time.unscaledTime+3;StartCoroutine(Health());}
            if(Reachable&&!opened&&!opening&&Time.unscaledTime>=nextOpen){opening=true;nextOpen=Time.unscaledTime+5;StartCoroutine(Open());}
            if(opened&&!syncing&&session.State.memory.outbox.Count>0){syncing=true;StartCoroutine(Sync());}
        }
        IEnumerator Health()
        {
            yield return Send("/api/health",null,3,(reply)=>{
                bool changed=!Reachable||Configured!=reply.configured||ConfigurationRevision!=reply.configurationRevision;
                Reachable=reply.app=="moonlife-runtime";Configured=reply.configured;Mode=reply.mode??"real";Model=reply.model??"";
                if(ConfigurationRevision!=reply.configurationRevision){retryModelAt=0;LastError="";}
                ConfigurationRevision=reply.configurationRevision;
                if(changed)ConnectionChanged?.Invoke();
            },error=>{bool was=Reachable;Reachable=false;LastError=error;if(was){opened=false;ConnectionChanged?.Invoke();}});
            checking=false;
        }
        IEnumerator Open()
        {
            var state=session.State.memory;var request=RequestBase();request.checkpoint=state.checkpoint;
            yield return Send("/api/game/open",request,8,r=>{if(session.State.memory!=state)return;opened=true;Accept(r,state);LastError="";},e=>LastError=e);
            opening=false;
        }
        IEnumerator Sync()
        {
            var state=session.State.memory;
            var request=RequestBase();request.eventData=state.outbox[0];
            yield return Send("/api/game/sync",request,40,r=>{if(session.State.memory!=state)return;Accept(r,state);LastError=r.warning??"";},e=>{LastError=e;opened=false;nextOpen=Time.unscaledTime+3;});
            syncing=false;
        }
        void Accept(MemoryReply reply,MemorySaveState state)
        {
            if(reply.runId!=RunId||session.State.memory!=state||string.IsNullOrEmpty(reply.checkpoint)||reply.seq<state.acknowledgedSeq||reply.responseRevision<acknowledgedRevision)return;
            acknowledgedRevision=reply.responseRevision;
            state.checkpoint=reply.checkpoint;state.acknowledgedSeq=Math.Max(state.acknowledgedSeq,reply.seq);state.nextSeq=Math.Max(state.nextSeq,reply.seq);
            state.outbox.RemoveAll(e=>e.seq<=state.acknowledgedSeq);
            session.Save();
        }
        public void Generate(string endpoint,MemoryRequest request,Action<MemoryReply> complete,Action<string> failed)
        {
            bool playerRequest=IsPlayerRequest(request);
            if(playerRequest)LastError="";
            if(!CanGenerate){if(playerRequest)LastError="模型尚未就绪，请稍候。";failed("模型尚未就绪，请稍候。");return;}
            var state=session.State.memory;
            StartCoroutine(Send(endpoint,request,35,r=>{Accept(r,state);if(playerRequest)LastError="";else BackgroundError="";complete(r);},e=>
            {
                if(playerRequest)LastError=e;else BackgroundError=e;
                // A malformed/invalid turn belongs to that actor and can be
                // retried. Only a player transport/model outage should disable
                // the normal input entrance; an autonomous NPC must not lock it.
                if(playerRequest&&!IsRecoverableTurnError(e))retryModelAt=Time.unscaledTime+15;
                failed(e);
            }));
        }

        static bool IsPlayerRequest(MemoryRequest request)
        {
            return request!=null&&string.Equals(request.channel,"private_player_chat",StringComparison.Ordinal);
        }

        static bool IsRecoverableTurnError(string error)
        {
            if(string.IsNullOrEmpty(error))return false;
            return error.StartsWith("输出格式错误：",StringComparison.Ordinal)||
                error.StartsWith("输入格式错误：",StringComparison.Ordinal)||
                error.StartsWith("模型未返回 JSON 对象",StringComparison.Ordinal)||
                error.StartsWith("模型输出未完成",StringComparison.Ordinal)||
                error.StartsWith("本轮取消或超过等待预算",StringComparison.Ordinal)||
                error.StartsWith("台词超出本轮字数预算",StringComparison.Ordinal);
        }
        public void CancelConversation(string conversationId) {if(speechConversation==conversationId)StopSpeech();StartCoroutine(CancelAfterDelivered(conversationId));}
        IEnumerator CancelAfterDelivered(string conversation)
        {
            while(session.State.memory.outbox.Any(e=>e.conversationId==conversation))yield return null;
            if(!opened)yield break;var request=RequestBase();request.conversationId=conversation;
            yield return Send("/api/game/cancel",request,3,_=>{},_=>{});
        }
        public void Focus(string actor){StopSpeech();int version=++focusVersion;focusing=true;StartCoroutine(FocusAfterDelivered(actor,version));}
        IEnumerator FocusAfterDelivered(string actor,int version)
        {
            while(version==focusVersion&&(!opened||session.State.memory.outbox.Count>0))yield return null;
            if(version!=focusVersion)yield break;var request=RequestBase();request.actorId=actor;
            yield return Send("/api/game/focus",request,5,r=>Accept(r,session.State.memory),e=>LastError=e);
            if(version==focusVersion)focusing=false;
        }
        public void CommitDecision(MemoryReply reply,string actor,string[] observers,string location)
        {var request=RequestBase();request.proposalId=reply.proposalId;request.actorId=actor;request.observers=observers;request.locationId=location;StartCoroutine(Send("/api/game/commit",request,5,r=>Accept(r,session.State.memory),e=>LastError=e));}
        public void RespondTask(string task,bool accepted,Action<MemoryReply> completed)
        {session.Memory.Append(new MemoryEvent{kind="task_response",taskId=task,accepted=accepted,responderId="player",conversationId=session.Memory.Conversations.ActiveId});StartCoroutine(AfterSynchronized(completed));}
        public void InviteTask(string task,string actor,string text,Action<MemoryReply> completed)
        {session.Memory.Append(new MemoryEvent{kind="task_invite",taskId=task,actorId=actor,text=text,conversationId=session.Memory.Conversations.ActiveId});StartCoroutine(AfterSynchronized(completed));}
        IEnumerator AfterSynchronized(Action<MemoryReply> completed){while(!CanGenerate)yield return null;completed(new MemoryReply{ok=true});}
        public void ConfirmTaskStep(string task,string step,string performer,Newtonsoft.Json.Linq.JObject evidence)
        {session.Memory.Append(new MemoryEvent{kind="task_receipt",taskId=task,stepId=step,performerId=performer,receiptId=Guid.NewGuid().ToString("N"),succeeded=true,participants=evidence?["participants"]?.ToObject<string[]>()??Array.Empty<string>(),voluntaryConsents=evidence?["voluntary_consents"]?.ToObject<string[]>()??Array.Empty<string>(),messageEventId=(string)evidence?["message_event_id"]});}
        // Only the coordinator's private player reply path starts speech.
        public void Speak(MemoryReply line,string conversation,string actor,Func<bool> presentationReady=null)
        {
            StopSpeech();
            if(line==null||string.IsNullOrEmpty(line.proposalId)||AudioListener.volume<=.01f)return;
            spokenText=line.result?.dialogue??line.text??"";
            captionRate=actorCaptionRates.TryGetValue(actor,out float rate)?rate:5;
            speechConversation=conversation;speechProposal=line.proposalId;SpeechBusy=true;SpeechStatus="正在准备语音…";
            int version=speechVersion;
            speechRoutine=StartCoroutine(SpeakAfterDelivered(line,conversation,actor,version,presentationReady));
        }
        public void StopSpeech(bool notify=true)
        {
            string conversation=speechConversation,proposal=speechProposal;
            speechVersion++;
            try{speechCancellation?.Cancel();}catch(ObjectDisposedException){}speechCancellation=null;
            if(speechRoutine!=null){StopCoroutine(speechRoutine);speechRoutine=null;}
            if(speechSource!=null){speechSource.Stop();speechSource.clip=null;}
            if(speechClip!=null){Destroy(speechClip);speechClip=null;}
            speechConversation=speechProposal="";SpeechBusy=false;SpeechStatus="";
            receivedSpeechSamples=subtitleTextCursor=0;speechStreamComplete=false;speechCues.Clear();
            SpeechFirstAudioMs=SpeechPlaybackStartMs=SpeechCompleteMs=-1;
            if(notify&&opened&&!string.IsNullOrEmpty(proposal))
            {
                var request=RequestBase();request.conversationId=conversation;request.proposalId=proposal;
                StartCoroutine(Send("/api/game/speech/cancel",request,3,_=>{},_=>{}));
            }
        }
        IEnumerator SpeakAfterDelivered(MemoryReply line,string conversation,string actor,int version,Func<bool> presentationReady)
        {
            // CommitLine appends to the journal. Voice must wait for its receipt.
            float deadline=Time.realtimeSinceStartup+45;
            while(!Synchronized&&version==speechVersion&&Time.realtimeSinceStartup<deadline)yield return null;
            if(version!=speechVersion)yield break;
            if(!Synchronized){SpeechBusy=false;SpeechStatus="语音等待同步超时，继续显示文字。";yield break;}
            var request=RequestBase();request.conversationId=conversation;request.proposalId=line.proposalId;request.actorId=actor;
            var cts=new CancellationTokenSource(TimeSpan.FromSeconds(40));speechCancellation=cts;
            var packets=new ConcurrentQueue<MemorySpeechPacket>();
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var task=FetchSpeechStream(request,packets,cts);
            bool started=false,paused=false;string failure=null;
            while(version==speechVersion)
            {
                while(packets.TryDequeue(out var packet))
                {
                    try
                    {
                        if(packet.type=="audio")
                        {
                            var samples=DecodeSpeechSamples(packet.audio);
                            if(samples.Length==0||receivedSpeechSamples+samples.Length>MaxSpeechSamples)throw new InvalidDataException();
                            if(speechClip==null)
                            {
                                speechClip=AudioClip.Create("NPC streamed dialogue",MaxSpeechSamples,1,SpeechRate,false);
                                if(speechSource==null){speechSource=gameObject.AddComponent<AudioSource>();speechSource.playOnAwake=false;speechSource.spatialBlend=0;}
                                speechSource.clip=speechClip;
                            }
                            if(!speechClip.SetData(samples,receivedSpeechSamples))throw new InvalidDataException();
                            receivedSpeechSamples+=samples.Length;
                            if(SpeechFirstAudioMs<0)SpeechFirstAudioMs=(float)clock.Elapsed.TotalMilliseconds;
                        }
                        else if(packet.type=="subtitle")
                        {
                            foreach(var word in packet.words??Array.Empty<MemorySpeechWord>())
                            {
                                if(string.IsNullOrEmpty(word.word)||float.IsNaN(word.startTime)||float.IsNaN(word.endTime)||word.startTime<0||word.endTime<word.startTime||word.endTime>180)continue;
                                int index=spokenText.IndexOf(word.word,subtitleTextCursor,StringComparison.Ordinal);
                                if(index<0)continue;
                                speechCues.Add(new SpeechCue{start=index,end=index+word.word.Length,from=word.startTime,to=word.endTime});
                                subtitleTextCursor=index+word.word.Length;
                            }
                        }
                        else if(packet.type=="done")
                        {
                            if(receivedSpeechSamples==0||packet.totalSamples!=receivedSpeechSamples)throw new InvalidDataException();
                            speechStreamComplete=true;SpeechCompleteMs=(float)clock.Elapsed.TotalMilliseconds;
                            actorCaptionRates[actor]=Mathf.Clamp(spokenText.Length/SpeechDuration,3,8);
                        }
                        else if(packet.type=="start"&&!string.IsNullOrEmpty(packet.speechWarning))Debug.LogWarning("TTS: "+packet.speechWarning);
                    }
                    catch{failure="语音数据无效，文字对话可继续。";break;}
                }
                if(failure!=null)break;
                if(task.IsCompleted)
                {
                    failure=task.IsFaulted||task.IsCanceled?"语音连接中断，文字对话可继续。":task.Result;
                    if(failure!=null)break;
                    if(!speechStreamComplete){failure="语音返回不完整，文字对话可继续。";break;}
                }
                if(!started&&receivedSpeechSamples>0&&(receivedSpeechSamples>=SpeechPrebuffer||speechStreamComplete)&&
                    (presentationReady==null||presentationReady()))
                {
                    speechSource.Play();started=true;SpeechPlaybackStartMs=(float)clock.Elapsed.TotalMilliseconds;SpeechStatus="正在说话…";
                }
                if(started)
                {
                    int remaining=receivedSpeechSamples-speechSource.timeSamples;
                    if(speechStreamComplete&&(remaining<=0||(!paused&&!speechSource.isPlaying)))break;
                    // Keep the sample clock and captions frozen during a network underrun.
                    if(!paused&&!speechStreamComplete&&remaining<SpeechRate/10){speechSource.Pause();speechSource.timeSamples=Mathf.Min(speechSource.timeSamples,receivedSpeechSamples);paused=true;SpeechStatus="语音缓冲中…";}
                    if(paused&&(remaining>=SpeechPrebuffer||speechStreamComplete))
                    {speechSource.timeSamples=Mathf.Min(speechSource.timeSamples,Mathf.Max(0,receivedSpeechSamples-1));speechSource.UnPause();paused=false;SpeechStatus="正在说话…";}
                }
                yield return null;
            }
            if(version!=speechVersion)yield break;
            try{cts.Cancel();}catch(ObjectDisposedException){}
            speechCancellation=null;SpeechBusy=false;SpeechStatus=failure??"";
            if(speechSource!=null){speechSource.Stop();speechSource.clip=null;}
            if(speechClip!=null){Destroy(speechClip);speechClip=null;}
            speechRoutine=null;
            Debug.Log($"TTS timing: actor={actor} firstAudioMs={SpeechFirstAudioMs:F0} playbackStartMs={SpeechPlaybackStartMs:F0} completeMs={SpeechCompleteMs:F0}");
        }
        async Task<string> FetchSpeechStream(MemoryRequest input,ConcurrentQueue<MemorySpeechPacket> packets,CancellationTokenSource cts)
        {
            try
            {
                // Each localhost audio response owns its connection. Unity/Mono may leave a
                // cancelled streaming connection unusable; never reuse it for another turn.
                // The Node-to-provider HTTPS pool remains reusable across turns.
                using(var speechHttp=new HttpClient(new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan })
                using(var request=new HttpRequestMessage(HttpMethod.Post,MemoryRuntimeConfig.Settings.serviceUrl+"/api/game/speech/stream"))
                {
                    request.Headers.Add("X-Z-Memory","1");request.Content=new StringContent(JsonConvert.SerializeObject(input),Encoding.UTF8,"application/json");
                    using(var response=await speechHttp.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cts.Token))
                    using(var stream=await response.Content.ReadAsStreamAsync())
                    using(var cancellation=cts.Token.Register(()=>stream.Dispose()))
                    using(var reader=new StreamReader(stream,Encoding.UTF8))
                    {
                        if(!response.IsSuccessStatusCode||response.Content.Headers.ContentType?.MediaType!="application/x-ndjson")return "语音请求失败，文字对话可继续。";
                        bool began=false,done=false;int wire=0,records=0;
                        string text;
                        while((text=await reader.ReadLineAsync())!=null)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            wire+=text.Length;
                            if(text.Length>1000000||wire>8000000||++records>4098||done)throw new InvalidDataException();
                            var packet=JsonConvert.DeserializeObject<MemorySpeechPacket>(text);
                            if(packet==null)throw new InvalidDataException();
                            if(!began)
                            {
                                if(packet.type!="start"||packet.runId!=input.runId||packet.proposalId!=input.proposalId)throw new InvalidDataException();
                                began=true;
                            }
                            else if(packet.type=="error")return "语音生成失败，文字对话可继续。";
                            else if(packet.type=="done")done=true;
                            else if(packet.type!="audio"&&packet.type!="subtitle")throw new InvalidDataException();
                            packets.Enqueue(packet);
                        }
                        return done?null:"语音返回不完整，文字对话可继续。";
                    }
                }
            }
            catch(Exception error){Debug.LogWarning("TTS stream transport: "+error.GetType().Name);return cts.IsCancellationRequested?"语音已取消或等待超时。":"语音连接失败，文字对话可继续。";}
            finally{cts.Dispose();}
        }
        static float[] DecodeSpeechSamples(MemorySpeechAudio audio)
        {
            if(audio==null||audio.format!="pcm_s16le"||audio.sampleRate!=24000||audio.channels!=1||string.IsNullOrEmpty(audio.base64)||audio.base64.Length>5600000)throw new InvalidDataException();
            var bytes=Convert.FromBase64String(audio.base64);
            if(bytes.Length==0||bytes.Length>4*1024*1024||bytes.Length%2!=0)throw new InvalidDataException();
            var samples=new float[bytes.Length/2];
            for(int i=0;i<samples.Length;i++)samples[i]=(short)(bytes[i*2]|bytes[i*2+1]<<8)/32768f;
            return samples;
        }
        public static AudioClip DecodeSpeech(MemorySpeechAudio audio)
        {
            var samples=DecodeSpeechSamples(audio);
            var clip=AudioClip.Create("NPC dialogue",samples.Length,1,24000,false);
            if(!clip.SetData(samples,0)){Destroy(clip);throw new InvalidDataException();}
            return clip;
        }
        IEnumerator Send(string endpoint,MemoryRequest input,int seconds,Action<MemoryReply> complete,Action<string> failed)
        {
            int version=generation;string key=Guid.NewGuid().ToString("N");
            var cts=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));requests[key]=cts;
            using(var request=new HttpRequestMessage(input==null?HttpMethod.Get:HttpMethod.Post,MemoryRuntimeConfig.Settings.serviceUrl+endpoint))
            {
                request.Headers.Add("X-Z-Memory","1");
                if(input!=null)request.Content=new StringContent(JsonConvert.SerializeObject(input),Encoding.UTF8,"application/json");
                Task<HttpResponseMessage> task=null;
                try{task=http.SendAsync(request,cts.Token);}catch{ }
                if(task==null){requests.Remove(key);cts.Dispose();if(version==generation)failed("无法连接本机记忆服务。");yield break;}
                while(!task.IsCompleted&&version==generation)yield return null;
                if(version!=generation){cts.Cancel();requests.Remove(key);cts.Dispose();yield break;}
                if(task.IsCanceled||task.IsFaulted){requests.Remove(key);cts.Dispose();failed("连接中断或等待超时，请稍后再试。");yield break;}
                using(var response=task.Result)
                {
                    var read=response.Content.ReadAsStringAsync();while(!read.IsCompleted&&version==generation)yield return null;
                    if(version==generation)
                    {
                        MemoryReply reply=null;
                        if(!read.IsFaulted&&!read.IsCanceled&&read.Result.Length<=8000000)try{reply=JsonConvert.DeserializeObject<MemoryReply>(read.Result);}catch{ }
                        if(reply==null)failed("记忆服务返回格式无效。");
                        else if(!response.IsSuccessStatusCode||!reply.ok)failed(string.IsNullOrEmpty(reply.error)?"记忆服务暂时不可用。":reply.error);
                        else complete(reply);
                    }
                }
            }
            requests.Remove(key);cts.Dispose();
        }
        void OnDestroy(){StopSpeech(false);generation++;foreach(var c in requests.Values)c.Cancel();requests.Clear();http?.Dispose();}
    }
}
