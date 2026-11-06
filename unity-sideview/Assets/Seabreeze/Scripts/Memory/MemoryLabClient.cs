using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace Seabreeze
{
    public sealed class MemoryLabClient : MonoBehaviour
    {
        GameSession session;
        HttpClient http;
        readonly Dictionary<string,CancellationTokenSource> requests=new Dictionary<string,CancellationTokenSource>();
        int generation;
        int acknowledgedRevision;
        bool opened,opening,syncing,checking;
        float nextHealth,nextOpen,retryModelAt;
        public string RunId {get;private set;}="";
        public string LastError {get;private set;}="";
        public string Mode {get;private set;}="real";
        public string Model {get;private set;}="";
        public bool Reachable {get;private set;}
        public bool Configured {get;private set;}
        public int ConfigurationRevision {get;private set;}
        public event Action ConnectionChanged;
        public bool Synchronized => opened&&!opening&&!syncing&&session?.State?.memory!=null&&session.State.memory.outbox.Count==0;
        public bool CanGenerate => Synchronized&&Reachable&&Configured&&Time.unscaledTime>=retryModelAt;
        public string Status => !Reachable?"记忆服务未连接":!Configured?"尚未配置模型":!Synchronized?"正在同步记忆":Time.unscaledTime<retryModelAt?"模型暂时不可用，稍后重试":Mode=="simulated"?"模拟模型验收":"模型已连接";
        public void Initialize(GameSession owner)
        {
            session=owner;
            http=new HttpClient(new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
            BeginWorld();
        }
        public void BeginWorld()
        {
            generation++;foreach(var c in requests.Values)c.Cancel();requests.Clear();
            StopAllCoroutines();opened=opening=syncing=checking=false;
            RunId=Guid.NewGuid().ToString("N");nextHealth=nextOpen=retryModelAt=0;LastError="";acknowledgedRevision=0;
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
                Reachable=reply.app=="project-z-seven-memory";Configured=reply.configured;Mode=reply.mode??"real";Model=reply.model??"";
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
            state.checkpoint=reply.checkpoint;state.acknowledgedSeq=Math.Max(state.acknowledgedSeq,reply.seq);
            state.outbox.RemoveAll(e=>e.seq<=state.acknowledgedSeq);
            session.Save();
        }
        public void Generate(string endpoint,MemoryRequest request,Action<MemoryReply> complete,Action<string> failed)
        {
            if(!CanGenerate){failed("模型尚未就绪，请稍候。");return;}
            var state=session.State.memory;
            StartCoroutine(Send(endpoint,request,35,r=>{Accept(r,state);complete(r);},e=>{LastError=e;retryModelAt=Time.unscaledTime+15;failed(e);}));
        }
        public void CancelConversation(string conversationId)
        {
            if(!opened)return;var request=RequestBase();request.conversationId=conversationId;
            StartCoroutine(Send("/api/game/cancel",request,3,_=>{},_=>{}));
        }
        IEnumerator Send(string endpoint,MemoryRequest input,int seconds,Action<MemoryReply> complete,Action<string> failed)
        {
            int version=generation;string key=Guid.NewGuid().ToString("N");
            var cts=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));requests[key]=cts;
            using(var request=new HttpRequestMessage(input==null?HttpMethod.Get:HttpMethod.Post,MemoryRuntimeConfig.Settings.serviceUrl+endpoint))
            {
                request.Headers.Add("X-Z-Memory","1");
                if(input!=null)request.Content=new StringContent(JsonUtility.ToJson(input),Encoding.UTF8,"application/json");
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
                        if(!read.IsFaulted&&!read.IsCanceled&&read.Result.Length<=8000000)try{reply=JsonUtility.FromJson<MemoryReply>(read.Result);}catch{ }
                        if(reply==null)failed("记忆服务返回格式无效。");
                        else if(!response.IsSuccessStatusCode||!reply.ok)failed(string.IsNullOrEmpty(reply.error)?"记忆服务暂时不可用。":reply.error);
                        else complete(reply);
                    }
                }
            }
            requests.Remove(key);cts.Dispose();
        }
        void OnDestroy(){generation++;foreach(var c in requests.Values)c.Cancel();requests.Clear();http?.Dispose();}
    }
}
