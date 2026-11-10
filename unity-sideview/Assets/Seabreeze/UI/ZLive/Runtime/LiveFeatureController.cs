using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace ZLive {
public sealed partial class LiveFeatureController:MonoBehaviour {
    LiveTransport transport;LiveAudioPlayer audioPlayer;CharacterPresenter character;UIDocument document;Image characterImage;
    Label status,caption;ScrollView chat;TextField input;JObject state;
    readonly ConcurrentQueue<JObject> events=new ConcurrentQueue<JObject>();readonly ConcurrentQueue<string> connections=new ConcurrentQueue<string>();
    readonly Dictionary<string,CancellationTokenSource> running=new Dictionary<string,CancellationTokenSource>();readonly Dictionary<string,JObject> outcomes=new Dictionary<string,JObject>();readonly HashSet<string> messageIds=new HashSet<string>();
    readonly CancellationTokenSource life=new CancellationTokenSource();readonly HashSet<TaskCompletionSource<bool>> frameWaiters=new HashSet<TaskCompletionSource<bool>>();float heartbeatAt;bool connected,locallyPaused=true,disposed,leaving;string pendingBase,pendingToken,pendingPin;string SessionKey {get {using(var sha=System.Security.Cryptography.SHA256.Create())return "ZLive.Session."+BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pendingBase+"|"+pendingToken))).Replace("-","");}}
    public Action ExitRequested;
    public Action<string> SessionEnded;
    bool sessionEndNotified;
    public void Configure(string serviceUrl,string gameSessionToken){pendingBase=serviceUrl;pendingToken=gameSessionToken;}
    async void Start(){try{character=gameObject.AddComponent<CharacterPresenter>();character.Initialize();audioPlayer=gameObject.AddComponent<LiveAudioPlayer>();audioPlayer.Character=character;character.Audio=audioPlayer.Source;audioPlayer.Frame=NextFrame;audioPlayer.Receipt=Report;audioPlayer.Caption=SetHostCaption;gameObject.AddComponent<NativeRtcAudioReference>();BuildUI();BuildCompanionUI();
#if !UNITY_EDITOR
        // A local playtest launcher can select its own connection without modifying the signed app.
        var connectionFile=Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE");
        if(string.IsNullOrEmpty(pendingBase)&&!string.IsNullOrWhiteSpace(connectionFile))
            ReadConnection(connectionFile);
        if(string.IsNullOrEmpty(pendingBase))ReadConnection(Path.Combine(Application.dataPath,"Resources","zlive-connection.json"));
#endif
        if(string.IsNullOrEmpty(pendingBase)) {
            string file=Path.Combine(Application.persistentDataPath,"ZLive","local-connection.json");
            if(File.Exists(file)){ReadConnection(file);}
        }
#if UNITY_EDITOR
        if(string.IsNullOrEmpty(pendingBase)){string file=Path.GetFullPath(Path.Combine(Application.dataPath,"../UserSettings/ZLive/local-connection.json"));if(File.Exists(file)){ReadConnection(file);}}
#endif
        if(string.IsNullOrEmpty(pendingBase)){status.text="尚未连接 · 请先启动直播服务，再重新进入";return;}await Enter();
    }catch(Exception e){Error(e);}}
    void ReadConnection(string file){if(!File.Exists(file))return;var c=JObject.Parse(File.ReadAllText(file));pendingBase=(string)c["baseUrl"];pendingToken=(string)c["token"];pendingPin=(string)c["certificateSha256"];}
    public async Task Enter(){transport=new LiveTransport(pendingBase,pendingToken,pendingPin);transport.Event+=e=>events.Enqueue(e);transport.Connection+=s=>connections.Enqueue(s);audioPlayer.Transport=transport;
        state=await transport.Enter(PlayerPrefs.GetString(SessionKey,""),!muted&&audioPlayer.Source.volume>0,character.Ready);if(leaving||disposed){await transport.Control("end");return;}PlayerPrefs.SetString(SessionKey,transport.SessionId);PlayerPrefs.Save();ApplyState(state);await transport.Control("resume");
        // A new Unity process cannot prove an old audio instance completed. Never replay it silently.
        foreach(JObject c in (JArray)state["pendingCommands"]){if((string)c["type"]=="host.present"||(string)c["type"]=="media.play"||(string)c["type"]=="audience.present")await Report(c,new JObject{{"status","interrupted"},{"reason","client_restarted"}});else Queue(c);}
        connected=true;_=Observe(transport.Subscribe());locallyPaused=false;audioPlayer.Pause(false);await SyncAudioCapabilities();UpdateCallView();
    }
    async Task Observe(Task task){try{await task;}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception e){if(!disposed)Error(e);}}
    void Update(){if(disposed||leaving)return;UpdateView();UpdateCompanion();while(connections.TryDequeue(out var c)){if(c=="disconnected"){connected=false;locallyPaused=true;CancelCompanion();audioPlayer.Pause(true);status.text="连接中断 · 已暂停播放";}else{connected=true;if(locallyPaused&&Application.isFocused)_=ResumeRoom();}}
        if(transport==null)return;int budget=30;while(budget-->0&&events.TryDequeue(out var e)){if((int)e["epoch"]!=transport.Epoch){CancelAll();status.text="会话版本已更新 · 请重新进入直播";connected=false;continue;}long seq=(long)e["serverSeq"];if(seq<=transport.Cursor)continue;if(seq!=transport.Cursor+1){locallyPaused=true;audioPlayer.Pause(true);status.text="同步缺口 · 请重新进入直播";continue;}Queue(e);transport.Cursor=seq;}
        if(transport!=null&&connected&&Time.unscaledTime>=heartbeatAt){heartbeatAt=Time.unscaledTime+10;_=Observe(transport.Heartbeat());}
        NotifySessionEnded();
    }
    void NotifySessionEnded(){
        if(sessionEndNotified||leaving||disposed||SessionPhase!="ended"||SessionEnded==null||string.IsNullOrEmpty(transport?.SessionId))return;
        sessionEndNotified=true;
        SessionEnded.Invoke(transport.SessionId);
        PlayerPrefs.DeleteKey(SessionKey);PlayerPrefs.Save();
    }
    void Queue(JObject e){if((string)e["type"]=="call.voice"){ReceiveCallVoice((JObject)e["payload"]);return;}if((string)e["type"]=="session.state"){ApplyState((JObject)e["payload"]);return;}if(e["commandId"]==null)return;string id=(string)e["commandId"];if(outcomes.TryGetValue(id,out var outcome)){_=Observe(transport.Receipt(e,outcome));return;}if(running.ContainsKey(id))return;
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);running[id]=cancel;_=Execute(e,cancel);
    }
    async Task Execute(JObject c,CancellationTokenSource cancel){string id=(string)c["commandId"],type=(string)c["type"];var p=(JObject)c["payload"];try{
        if((int)c["protocolVersion"]!=1)throw new Exception("unsupported_protocol");if(DateTimeOffset.TryParse((string)c["expiresAt"],out var expiry)&&expiry<DateTimeOffset.UtcNow)throw new Exception("command_expired");await Report(c,new JObject{{"status","received"}});cancel.Token.ThrowIfCancellationRequested();
        switch(type){
        case "host.fade":await audioPlayer.FadeOut((string)p["speechId"],(int?)p["durationMs"]??180,cancel.Token);await Report(c,new JObject{{"status","completed"}});break;
        case "host.present":if(p["fallbackReason"]?.Type==JTokenType.String)Toast("语音暂不可用 · "+VoiceErrorText((string)p["fallbackReason"]));await audioPlayer.Host(c,cancel.Token);break;
        case "audience.present":{string mid=(string)p["messageId"];if(!messageIds.Contains(mid)){AddChat(NpcName((string)p["npcId"]),(string)p["text"],false);messageIds.Add(mid);messageIds.Add("message:"+mid);}if(chatCollapsed){await Report(c,new JObject{{"status","interrupted"},{"reason","chat_collapsed"}});break;}await VisibleFrame(chat,cancel.Token);await Report(c,new JObject{{"status","displayed"}});break;}
        case "media.prepare":await audioPlayer.Prepare(c,cancel.Token);break;
        case "media.play":await audioPlayer.Play(c,cancel.Token);break;
        case "media.pause":locallyPaused=true;audioPlayer.Pause(true);await Report(c,new JObject{{"status","completed"}});break;
        case "media.resume":locallyPaused=false;audioPlayer.Pause(false);await Report(c,new JObject{{"status","completed"}});break;
        case "execution.cancel":if((bool?)p["all"]==true)CancelAll(id);else if(running.TryGetValue((string)p["targetCommandId"]??"",out var old))old.Cancel();await Report(c,new JObject{{"status","completed"}});break;
        case "activity.release":await Report(c,new JObject{{"status","completed"}});break;
        case "session.ended":CancelAll(id);status.text="直播已结束";stopRequested=false;PlayerPrefs.DeleteKey(SessionKey);await Report(c,new JObject{{"status","completed"}});break;
        default:throw new Exception("unsupported_command");}
    }catch(OperationCanceledException){await TryFailure(c,"interrupted","execution_cancelled");}catch(Exception e){await TryFailure(c,"failed",e is LiveProtocolException||e.Message=="audio_output_unavailable"?e.Message:"execution_failed");Error(e);}finally{running.Remove(id);cancel.Dispose();}}
    async Task TryFailure(JObject c,string statusValue,string reason){try{await Report(c,new JObject{{"status",statusValue},{"reason",reason}});}catch(LiveProtocolException e)when(e.Message=="state_conflict"||e.Message=="stale_epoch"){}catch(Exception){status.text="执行回执待恢复";}}
    async Task Report(JObject c,JObject evidence){var p=(JObject)c["payload"];string result=(string)evidence["status"];var body=new JObject();foreach(string key in new[]{"artifactId","playbackId","speechId","stage","round","view","modality"})if(p[key]!=null)body[key]=p[key].DeepClone();foreach(var item in evidence.Properties())body[item.Name]=item.Value.DeepClone();
        if(result=="completed"||result=="displayed"||result=="ready"||result=="failed"||result=="interrupted")outcomes[(string)c["commandId"]]=(JObject)body.DeepClone();await transport.Receipt(c,body);if((result=="completed"||result=="displayed")&&connected&&status.text.StartsWith("暂未完成"))status.text="已连接 · 和 Z 聊聊天";
    }
    async Task VisibleFrame(VisualElement element,CancellationToken cancel){await NextFrame();await NextFrame();cancel.ThrowIfCancellationRequested();if(!(Application.isFocused||(Application.isBatchMode&&document.panelSettings.targetTexture!=null))||!element.visible||element.resolvedStyle.display==DisplayStyle.None||element.worldBound.width<=0||element.worldBound.height<=0)throw new Exception("not_visible");}
    Task NextFrame(){var tcs=new TaskCompletionSource<bool>();if(disposed){tcs.SetCanceled();return tcs.Task;}frameWaiters.Add(tcs);StartCoroutine(EndFrame(tcs));return tcs.Task;}
    IEnumerator EndFrame(TaskCompletionSource<bool> tcs){yield return null;frameWaiters.Remove(tcs);if(disposed||leaving)tcs.TrySetCanceled();else tcs.TrySetResult(true);}
    void ApplyState(JObject snapshot){state=snapshot;UpdateCallView();character.SetWaiting((bool?)snapshot["performance"]?["waiting"]??false);
        foreach(JObject e in (JArray)(snapshot["publicEvents"]??new JArray())){string id=(string)e["id"],type=(string)e["type"];if(messageIds.Contains(id)||!(type=="player.message"||type=="npc.message"||type=="speech.completed"))continue;messageIds.Add(id);if(snapshot["room"]?["messages"]==null)AddChat(type=="speech.completed"?"Z":type=="player.message"?"你":NpcName((string)e["speakerId"]),(string)e["text"],type=="player.message");}
        foreach(JObject message in (JArray)(snapshot["room"]?["messages"]??new JArray())){string id="message:"+(string)message["id"];if(messageIds.Add(id)&&(string)message["type"]!="z")AddChat((string)message["name"]??"小屋通知",(string)message["text"],(bool?)message["mine"]==true,(string)message["type"]=="system");}
        if((string)snapshot["phase"]=="paused"){locallyPaused=true;audioPlayer.Pause(true);}
    }
    static string NpcName(string id)=>id=="wind"?"晚风来信":id=="moon"?"月亮邮差":id=="salt"?"海盐汽水":"小岛来客";
    async void Send() {
        if(sendingInput||SessionInputBlocked)return;
        if(ChatInputComposing){Toast("请先选好汉字，再发送");return;}
        if(transport==null){Toast("请先启动直播服务");return;}
        string draft=input.value??"",text=draft.Trim();
        if(text.Length==0){Toast("先写一句想对 Z 说的话吧");return;}
        int limit=(bool?)state?["call"]?["active"]==true?360:120;
        if(text.Length>limit){Toast($"请将内容编辑至 {limit} 字以内再发送");return;}
        // Clear before awaiting the network, while no IME composition is active.
        // An acknowledgement must never erase the next draft or its preview cursor.
        ReplaceChatDraft("");int clearedRevision=inputRevision;
        sendingInput=true;sendButton.SetEnabled(false);bool accepted=false;
        character.NoticeMessage();
        try {
            if(locallyPaused){await transport.Control("resume");locallyPaused=false;audioPlayer.Pause(false);await SyncAudioCapabilities();}
            await transport.Input("viewer.message",new JObject{{"text",text}});accepted=true;if((bool?)state?["call"]?["active"]==true)ShowPlayerSpeech(text,true);

            ApplyState(await transport.Request(transport.Route()));
        }catch(Exception e){
            if(!accepted)RestoreFailedDraft(draft,clearedRevision);
            Error(e);Toast(FriendlyError(e));
        }finally{sendingInput=false;if(!disposed)RefreshSessionControls();}
    }
    public async Task Leave(){leaving=true;CancelCompanion();CancelAll();PlayerPrefs.DeleteKey(SessionKey);if(transport!=null)await transport.Control("end");}
    public void StopPlayback(){leaving=true;CancelCompanion();locallyPaused=true;CancelAll();audioPlayer?.Pause(true);}
    bool resuming;
    async Task ResumeRoom(){
        if(resuming||!connected||!locallyPaused||SessionClosing||SessionPhase=="ended")return;
        resuming=true;
        try{await transport.Control("resume");if(!disposed&&!leaving){locallyPaused=false;audioPlayer.Pause(false);status.text="";await SyncAudioCapabilities();}}
        catch(Exception e){if(!disposed)Error(e);}finally{resuming=false;}
    }
    void OnApplicationFocus(bool focus){
        if(SessionClosing||stopRequested||requestingMicrophonePermission||transport==null||disposed||leaving)return;
        if(focus){_=ResumeRoom();return;}
        CancelCompanion();locallyPaused=true;audioPlayer.Pause(true);_=Observe(transport.Control("pause"));
    }
    void CancelAll(string except=null){foreach(var entry in new List<KeyValuePair<string,CancellationTokenSource>>(running))if(entry.Key!=except)entry.Value.Cancel();audioPlayer?.Stop();}
    static string FriendlyError(Exception error) {
        switch(error.Message) {
            case "microphone_permission_required":return "请在系统设置中允许使用麦克风";
            case "rtc_plugin_missing":case "rtc_plugin_invalid":return "原生连麦组件尚未安装，请运行 Tools/native-rtc/build-macos.sh";
            case "rtc_platform_unsupported":return "当前原生连麦暂支持 macOS";
            case "rtc_join_failed":return "未能连接语音房间，请检查网络后重试";
            case "voiceprint_enrollment_required":return "请先登记你的声音";
            case "rtc_mute_failed":return "麦克风状态切换失败，可下麦后重新连麦";
            case "voiceprint_invalid_speech":case "voiceprint_invalid_duration":return "清晰人声不足，请再说一句完整的话";
            case "voiceprint_not_configured":return "连麦声音服务尚未配置，可继续打字聊天";
            case "voiceprint_recording_too_quiet":return "录音中的人声不足，请靠近麦克风重试";
            case "voiceprint_permission":case "voiceprint_provider_rejected":return "声音服务权限暂不可用，请检查服务配置";
            case "voiceprint_connect_timeout":case "voiceprint_connection_failed":case "voiceprint_disconnected":return "连麦连接中断，请重新上麦";
            case "voiceprint_verification_inactive":return "本轮声音保护未生效，请重新上麦";
            case "voiceprint_session_expired":return "本轮连麦已结束，请重新上麦";
            case "microphone_unavailable":return "没有可用麦克风，可继续发文字";
            case "asr_not_configured":return "语音识别服务尚未配置，可继续发文字";
            case "asr_busy":return "上一句还在识别，请稍候";
            case "stale_voice_turn":case "asr_cancelled":return "这一轮语音已结束，请重新说话";
            case "asr_unavailable":case "asr_text_unavailable":return "没有识别出完整内容，请重试或发文字";
            case "review_incomplete":return "请填写试听结论，并逐项确认审核内容";
            case "confirm_unknown_attempt":return "请先核对上次生成结果，再勾选允许重试";
            case "retry_not_allowed":return "当前任务无法重试，可重新提交点歌";
            case "task_limit":return "点歌任务已满，请先处理现有任务";
            case "talent_busy":return "Z 正在表演，请先结束当前表演";
            case "asset_unavailable":return "当前唱段暂时无法播放";
            case "audio_disabled":return "请开启声音后再唱歌";
            case "audio_output_unavailable":return "声音设备未就绪，请检查耳机连接后重试";
            case "audio_interrupted":case "audio_timeout":return "声音播放中断，请重试";
            case "session_paused":return "请先继续直播";
            case "state_conflict":case "stale_review":return "状态已更新，请稍后重试";
            case "unauthorized":return "直播连接已失效，请重新启动直播服务";
            case "invalid_request":case "invalid_input":return "请检查输入内容是否完整";
            default:return "请检查直播服务连接，或稍后重试";
        }
    }
    void Error(Exception e){if(status!=null)status.text="暂未完成 · "+FriendlyError(e);Debug.LogWarning("ZLive: "+e.GetType().Name+" "+e.Message);}
    void OnApplicationPause(bool paused){if(paused)OnApplicationFocus(false);}
    void OnDestroy(){disposed=true;ReleaseInputKeyboard();CancelCompanion();foreach(var waiter in frameWaiters)waiter.TrySetCanceled();frameWaiters.Clear();if(document!=null&&document.panelSettings!=null)Destroy(document.panelSettings);life.Cancel();CancelAll();transport?.Dispose();life.Dispose();}
}
}
