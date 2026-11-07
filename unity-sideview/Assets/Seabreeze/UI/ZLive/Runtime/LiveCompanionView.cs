using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    VisualElement callBar,storyChoices,normalSpeech,callDialogue,hostRow,playerRow;
    Label hostLine,playerLine;Button callButton;float thinkingAfter;bool thinkingOffered;
    bool callChanging,requestingMicrophonePermission,rtcReady;string voiceNotice="",rtcCallId;
    CancellationTokenSource microphoneCancel,enrollmentCancel;AudioClip enrollmentClip;
    void BuildCompanionUI() {
        callButton=Icon("mic","点击上麦",ToggleCall);callButton.AddToClassList("call-toggle");form.Insert(form.IndexOf(sendButton),callButton);
        var speech=document.rootVisualElement.Q(className:"speech-box");
        normalSpeech=Box("normal-speech");foreach(var child in speech.Children().ToArray()){child.RemoveFromHierarchy();normalSpeech.Add(child);}speech.Add(normalSpeech);
        callDialogue=Box("call-dialogue");speech.Add(callDialogue);
        hostRow=Bubble("Z",false,out hostLine);playerRow=Bubble("你",true,out playerLine);
        callDialogue.Add(hostRow);callDialogue.Add(playerRow);
        callBar=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"call-story-card");sidebar.Insert(0,callBar);
        storyChoices=Box("story-choices");callBar.Add(storyChoices);UpdateCallView();
    }
    VisualElement Bubble(string name,bool mine,out Label line) {
        var row=Box(mine?"call-bubble call-player":"call-bubble");row.Add(Text(name,mine?"speaker-z speaker-player":"speaker-z"));
        var scroll=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"call-line-scroll");line=Text("","caption");scroll.Add(line);row.Add(scroll);return row;
    }
    void SetHostCaption(string text){if(!string.IsNullOrEmpty(text))thinkingAfter=0;caption.text=text;if(hostLine!=null){hostLine.text=text;hostRow.style.display=DisplayStyle.Flex;}}
    void SetVoiceDraft(string text,int limit){ReplaceChatDraft(text??"",limit);}
    void ShowPlayerSpeech(string text,bool final) {
        if(playerLine==null||string.IsNullOrWhiteSpace(text))return;
        audioPlayer.CancelThinking();thinkingAfter=final?Time.unscaledTime+.9f:0;thinkingOffered=false;playerLine.text=text;playerRow.style.display=DisplayStyle.Flex;
        callDialogue.Insert(0,playerRow);hostLine.text=final?"…":"正在听你说…";hostRow.style.display=DisplayStyle.Flex;
    }
    async void ToggleCall() {
        if(transport==null||enrollmentCancel!=null||SessionClosing||SessionInputBlocked)return;
        if(callChanging){microphoneCancel?.Cancel();return;}
        if((bool?)state?["call"]?["active"]==true){CancelMicrophone();try{await transport.Input("call.end",new JObject());ApplyState(await transport.Request(transport.Route()));}catch(Exception e){Error(e);}return;}
        callChanging=true;var own=microphoneCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=own.Token;
        try {
            NativeRtc.EnsureAvailable();var config=await transport.Request(transport.Route("/voice/config"));token.ThrowIfCancellationRequested();
            if((bool?)config["configured"]!=true||(bool?)config["requested"]!=true)throw new LiveProtocolException((bool?)config["needsEnrollment"]==true?"voiceprint_enrollment_required":"voiceprint_not_configured");
            await EnsureMicrophonePermission(token);
            if(locallyPaused){await transport.Control("resume");locallyPaused=false;audioPlayer.Pause(false);}
            if(muted)ToggleSound();
            playerLine.text=hostLine.text="";playerRow.style.display=hostRow.style.display=DisplayStyle.None;
            await transport.Input("call.join",new JObject());token.ThrowIfCancellationRequested();
            ApplyState(await transport.Request(transport.Route()));token.ThrowIfCancellationRequested();
            var rtc=await transport.Voice("start");token.ThrowIfCancellationRequested();rtcCallId=(string)rtc["callId"];
            NativeRtc.Start((string)rtc["appId"],(string)rtc["roomId"],(string)rtc["userId"],(string)rtc["token"]);
            double deadline=Time.realtimeSinceStartupAsDouble+18;
            while(NativeRtc.State!=2){token.ThrowIfCancellationRequested();if(NativeRtc.State<0||Time.realtimeSinceStartupAsDouble>deadline)throw new LiveProtocolException("rtc_join_failed");await NextFrame();}
            await transport.Voice("joined",rtcCallId);token.ThrowIfCancellationRequested();
            while(!rtcReady){token.ThrowIfCancellationRequested();if(Time.realtimeSinceStartupAsDouble>deadline)throw new LiveProtocolException("voiceprint_connect_timeout");await NextFrame();}
        }catch(OperationCanceledException){if(microphoneCancel==own)await EndCancelledCall();}
        catch(Exception e){Toast(FriendlyError(e));if(microphoneCancel==own)await EndCancelledCall();}
        finally{if(microphoneCancel==own)callChanging=false;UpdateCallView();}
    }
    async Task EndCancelledCall(){NativeRtc.Stop();try{await transport.Input("call.end",new JObject());ApplyState(await transport.Request(transport.Route()));}catch{}CancelMicrophone();}

    void ReceiveCallVoice(JObject e) {
        if(rtcCallId==null||(string)e["callId"]!=rtcCallId||SessionClosing||disposed)return;
        switch((string)e["type"]){
            case "ready":rtcReady=true;break;
            case "transcript":if((bool?)e["speakerVerified"]==true)ShowPlayerSpeech((string)e["text"],(bool?)e["final"]==true);break;
            case "review":SetVoiceDraft((string)e["text"],360);input.Focus();Toast("请编辑这一句后发送");break;
            case "discard":hostLine.text=caption.text;break;
            case "error":Toast(FriendlyError(new LiveProtocolException((string)e["code"])));CancelMicrophone();break;
        }
    }
    async Task CallAction(string action,JObject payload=null){if(transport==null||locallyPaused||SessionClosing||SessionInputBlocked)return;try{await transport.Input(action,payload??new JObject());ApplyState(await transport.Request(transport.Route()));}catch(Exception e){Error(e);}}
    void UpdateCallView() {
        if(callButton==null)return;var call=state?["call"];bool active=(bool?)call?["active"]==true;
        callButton.tooltip=active||callChanging?"点击下麦":"点击上麦";callButton.EnableInClassList("connected",active||callChanging);callButton.SetEnabled(transport!=null&&enrollmentCancel==null&&!SessionClosing&&!SessionInputBlocked);
        document.rootVisualElement.EnableInClassList("call-active",active);
        normalSpeech.style.display=active?DisplayStyle.None:DisplayStyle.Flex;callDialogue.style.display=active?DisplayStyle.Flex:DisplayStyle.None;
        UpdateChatInputLimit();input.textEdition.placeholder=active?"已上麦，也可以打字…":"和 Z 聊聊……";
        if(rtcCallId!=null&&(!active||(string)call?["id"]!=rtcCallId))CancelMicrophone();
        var story=call?["story"] as JObject;bool offered=active&&(string)story?["status"]=="offered";callBar.style.display=offered?DisplayStyle.Flex:DisplayStyle.None;
        string fingerprint=offered?story.ToString():"";if(fingerprint==storyFingerprint)return;storyFingerprint=fingerprint;storyChoices.Clear();if(!offered)return;
        var step=call["storyDefinition"]?["steps"]?[(int)story["step"]];if(step==null)return;
        storyChoices.Add(Text("今晚的小片段","eyebrow"));storyChoices.Add(Text((string)step["question"],"song-note"));
        foreach(JObject choice in (JArray)step["choices"]){string id=(string)choice["id"];int revision=(int)story["revision"];storyChoices.Add(Action((string)choice["label"],()=>_=CallAction("call.story",new JObject{{"choice",id},{"revision",revision}})));}
    }
    void UpdateCompanion(){
        if(thinkingAfter>0&&!thinkingOffered&&Time.unscaledTime>=thinkingAfter&&(bool?)state?["call"]?["active"]==true&&(bool?)state?["performance"]?["waiting"]==true&&state?["call"]?["thinkingAudio"] is JObject meta){thinkingOffered=true;_=audioPlayer.Thinking(meta,life.Token);}
if(transport==null||SessionClosing||SessionInputBlocked){CancelCompanion();return;}if(rtcReady&&NativeRtc.State<0){Toast("连麦连接中断，请重新上麦");CancelMicrophone();_=CallAction("call.end");}}
    async Task EnsureMicrophonePermission(CancellationToken token) {
        if(Application.HasUserAuthorization(UserAuthorization.Microphone))return;requestingMicrophonePermission=true;
        try{var permission=Application.RequestUserAuthorization(UserAuthorization.Microphone);while(!permission.isDone){await NextFrame();token.ThrowIfCancellationRequested();}token.ThrowIfCancellationRequested();if(!Application.HasUserAuthorization(UserAuthorization.Microphone))throw new LiveProtocolException("microphone_permission_required");}
        finally{requestingMicrophonePermission=false;}
    }
    void CancelMicrophone(){thinkingAfter=0;audioPlayer?.CancelThinking();rtcReady=callChanging=false;rtcCallId=null;microphoneCancel?.Cancel();microphoneCancel?.Dispose();microphoneCancel=null;NativeRtc.Stop();}
    void CancelCompanion(){CancelMicrophone();enrollmentCancel?.Cancel();}
    async void ShowVoiceSettings() {
        OpenDialog("YOUR VOICE","连麦声音设置");
        var note=Text("正在读取声音设置…","dialog-description");dialogContent.Add(note);
        try{var config=await transport.Request(transport.Route("/voice/config"));if(note.panel==null)return;
            if((bool?)config["needsEnrollment"]!=true){note.text="已记住你的声音。点击直播麦克风即可连麦。";return;}
            if((bool?)config["credentialsConfigured"]!=true){note.text="声音服务尚未配置，请在本地服务配置中补齐 RTC 与语音凭证。";return;}
            note.text="先在相对安静的地方录入约 25 秒声音，之后用于区分你和周围的人。录音只用于声纹登记，不保存原录音。";
            var start=Action("开始录入声音",null,"dialog-primary");dialogContent.Add(start);start.clicked+=()=>EnrollVoice(note,start);
        }catch(Exception e){note.text=FriendlyError(e);}
    }
    async void EnrollVoice(Label note,Button start) {
        if(enrollmentCancel!=null)return;if(callChanging||(bool?)state?["call"]?["active"]==true){note.text="请先下麦，再录入声音。";return;}
        var own=enrollmentCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=own.Token;start.SetEnabled(false);UpdateCallView();
        EventCallback<DetachFromPanelEvent> detached=_=>own.Cancel();note.RegisterCallback(detached);
        try{await EnsureMicrophonePermission(token);if(Microphone.devices.Length==0)throw new LiveProtocolException("microphone_unavailable");
            string device=Microphone.devices[0];enrollmentClip=Microphone.Start(device,false,26,16000);if(enrollmentClip==null)throw new LiveProtocolException("microphone_unavailable");
            double began=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-began<25){token.ThrowIfCancellationRequested();if(note.panel==null)throw new OperationCanceledException();note.text="请用平常的声音聊聊今天：今天我来到了月亮小岛，想和 Z 聊一会儿。工作里有忙碌，也有值得开心的小事……（还剩 "+Mathf.CeilToInt(25-(float)(Time.realtimeSinceStartupAsDouble-began))+" 秒）";await NextFrame();}
            int frames=Microphone.GetPosition(device);Microphone.End(device);var samples=new float[frames*enrollmentClip.channels];enrollmentClip.GetData(samples,0);
            int count=(int)((long)frames*16000/enrollmentClip.frequency),voiced=0;var pcm=new short[count];
            for(int i=0;i<count;i++){float sample=0;int at=Math.Min(frames-1,(int)((long)i*enrollmentClip.frequency/16000));for(int c=0;c<enrollmentClip.channels;c++)sample+=samples[at*enrollmentClip.channels+c]/enrollmentClip.channels;pcm[i]=(short)Mathf.RoundToInt(Mathf.Clamp(sample,-1,1)*32767);if(Mathf.Abs(sample)>.008f)voiced++;}
            if(voiced<16000*2)throw new LiveProtocolException("voiceprint_recording_too_quiet");
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);foreach(short sample in pcm)writer.Write(sample);note.text="正在保存声音设置…";await transport.EnrollVoice(memory.ToArray(),token);}
            note.text="已记住你的声音，可以回直播间上麦了。";
        }catch(OperationCanceledException){}catch(Exception e){note.text=FriendlyError(e);}finally{if(enrollmentClip!=null){Microphone.End(null);Destroy(enrollmentClip);enrollmentClip=null;}note.UnregisterCallback(detached);enrollmentCancel=null;own.Dispose();start.SetEnabled(true);UpdateCallView();}
    }
}
}
