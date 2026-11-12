using System;
using System.Collections;
using System.Collections.Generic;
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
        public float SpeechDuration => speechClip!=null?speechClip.length:0;
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
            var task=FetchSpeech(request,cts);
            while(!task.IsCompleted&&version==speechVersion)yield return null;
            if(version!=speechVersion)yield break;
            speechCancellation=null;
            if(task.IsCanceled||task.IsFaulted){SpeechBusy=false;SpeechStatus="语音连接中断，文字对话可继续。";yield break;}
            var reply=task.Result;
            if(!reply.ok){SpeechBusy=false;SpeechStatus=reply.error??"语音暂不可用，文字对话可继续。";yield break;}
            if(reply.runId!=RunId||reply.proposalId!=line.proposalId){SpeechBusy=false;SpeechStatus="";yield break;}
            try
            {
                speechClip=DecodeSpeech(reply.audio);
                if(speechSource==null){speechSource=gameObject.AddComponent<AudioSource>();speechSource.playOnAwake=false;speechSource.spatialBlend=0;}
                speechSource.clip=speechClip;
                if(!string.IsNullOrEmpty(reply.speechWarning))Debug.LogWarning("TTS: "+reply.speechWarning);
            }
            catch {SpeechBusy=false;SpeechStatus="语音数据无效，文字对话可继续。";yield break;}
            // Let any existing portrait preparation finish before voice and captions start.
            while(version==speechVersion&&presentationReady!=null&&!presentationReady())yield return null;
            if(version!=speechVersion)yield break;
            speechSource.Play();SpeechStatus="正在说话…";
            while(version==speechVersion&&speechSource!=null&&speechSource.isPlaying)yield return null;
            if(version==speechVersion)
            {
                SpeechBusy=false;SpeechStatus="";speechSource.clip=null;
                if(speechClip!=null){Destroy(speechClip);speechClip=null;}
                speechRoutine=null;
            }
        }
        async Task<MemoryReply> FetchSpeech(MemoryRequest input,CancellationTokenSource cts)
        {
            try
            {
                using(var request=new HttpRequestMessage(HttpMethod.Post,MemoryRuntimeConfig.Settings.serviceUrl+"/api/game/speech"))
                {
                    request.Headers.Add("X-Z-Memory","1");request.Content=new StringContent(JsonConvert.SerializeObject(input),Encoding.UTF8,"application/json");
                    using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cts.Token))
                    using(var stream=await response.Content.ReadAsStreamAsync())
                    using(var buffer=new MemoryStream())
                    {
                        if(response.Content.Headers.ContentLength>6000000)throw new InvalidDataException();
                        var bytes=new byte[16384];int count;
                        while((count=await stream.ReadAsync(bytes,0,bytes.Length,cts.Token))>0)
                        {if(buffer.Length+count>6000000)throw new InvalidDataException();buffer.Write(bytes,0,count);}
                        var reply=JsonConvert.DeserializeObject<MemoryReply>(Encoding.UTF8.GetString(buffer.ToArray()));
                        if(reply==null)throw new InvalidDataException();
                        if(!response.IsSuccessStatusCode)reply.ok=false;
                        return reply;
                    }
                }
            }
            catch {return new MemoryReply{ok=false,error=cts.IsCancellationRequested?"语音已取消或等待超时。":"语音连接失败，文字对话可继续。"};}
            finally {cts.Dispose();}
        }
        public static AudioClip DecodeSpeech(MemorySpeechAudio audio)
        {
            if(audio==null||audio.format!="pcm_s16le"||audio.sampleRate!=24000||audio.channels!=1||string.IsNullOrEmpty(audio.base64)||audio.base64.Length>5600000)throw new InvalidDataException();
            var bytes=Convert.FromBase64String(audio.base64);
            if(bytes.Length==0||bytes.Length>4*1024*1024||bytes.Length%2!=0)throw new InvalidDataException();
            var samples=new float[bytes.Length/2];
            for(int i=0;i<samples.Length;i++)samples[i]=(short)(bytes[i*2]|bytes[i*2+1]<<8)/32768f;
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
