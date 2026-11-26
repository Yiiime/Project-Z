using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    Button callButton, leaveCall;
    Label callLabel;
    Image callIcon;
    float thinkingAfter;
    bool thinkingOffered, callChanging, requestingMicrophonePermission, rtcReady, micMuted;
    string rtcCallId;
    CancellationTokenSource microphoneCancel;
    const string CallInvitation = "诶？有人连麦了，快来和我唠唠嗑";
    AudioSource invitationPlayer;
    bool inviting, localVoice;
    string lastHostCaption;
    CallMicrophone capture;
    CallOutputReference echoReference;
    internal Func<CallMicrophone> OpenCallMicrophone = () => new CallMicrophone();
    internal Func<CancellationToken,Task> AuthorizeMicrophone;
    void BuildCompanionUI() {
        echoReference=GetComponent<NativeRtcAudioReference>().Output;
        AuthorizeMicrophone=EnsureMicrophonePermission;
        var invitationObject=new GameObject("Call invitation audio");
        invitationObject.transform.SetParent(transform,false);
        invitationPlayer=invitationObject.AddComponent<AudioSource>();
        invitationPlayer.playOnAwake=false;
        invitationPlayer.ignoreListenerPause=true;
        invitationPlayer.clip=Resources.Load<AudioClip>("LiveGlass/call-invitation");
        callButton=Action("",ToggleCall,"mic-control");
        callIcon=Art("mic","tool-icon");
        callButton.Add(callIcon);
        callLabel=Text("连麦","mic-label");
        callButton.Add(callLabel);
        controls.Insert(0,callButton);
        leaveCall=Action("下麦",LeaveCall,"leave-mic");
        controls.Insert(1,leaveCall);
        UpdateCallView();
    }
    void SetHostCaption(string text){
        lastHostCaption=text;
        if(inviting)return;
        DisplayCaption(text);
    }
    void DisplayCaption(string text){
        string visible=text??"";
        if(SceneMode=="arrival" && caption.resolvedStyle.width>0)
        {
            // Progressive captions retain the most recent complete two-line page.
            // Measure actual font/layout rather than assuming CJK and Latin share widths.
            float width=caption.resolvedStyle.width, size=caption.resolvedStyle.fontSize;
            int start=0;
            while(start<visible.Length && caption.MeasureTextSize(visible.Substring(start),width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined).y>size*2.65f)
            { start++; if(start<visible.Length&&char.IsLowSurrogate(visible[start]))start++; }
            visible=visible.Substring(start);
        }
        caption.text=visible;
        speech.style.display=string.IsNullOrEmpty(text)?DisplayStyle.None:DisplayStyle.Flex;
        if(!string.IsNullOrEmpty(text))thinkingAfter=0;
    }
    void SetVoiceDraft(string text,int limit){ReplaceChatDraft(text??"",limit);}
    void ShowPlayerSpeech(string text,bool final){
        if(string.IsNullOrWhiteSpace(text))return;
        audioPlayer.CancelThinking();thinkingAfter=final?Time.unscaledTime+.9f:0;
        thinkingOffered=false;transcript.text="你："+text;
        transcript.style.display=DisplayStyle.Flex;
    }
    bool leavingCall;
    async void LeaveCall(){
        if(leavingCall)return;
        leavingCall=true;
        CancelMicrophone();
        UpdateCallView();
        try{await CallAction("call.end");}finally{leavingCall=false;UpdateCallView();}
    }
    async void ToggleCall() {
        if(transport==null||!connected||callChanging||leavingCall||SessionClosing||SessionInputBlocked)return;
        if(rtcReady){
            try{
                if(localVoice) {
                    capture?.Dispose();capture=null;
                    if(micMuted)capture=OpenCallMicrophone();
                    localTurnCancel?.Cancel();
                } else NativeRtc.SetMuted(!micMuted);
                micMuted=!micMuted;UpdateCallView();
            }catch(Exception e){Toast(FriendlyError(e));}
            return;
        }
        callChanging=true;
        var own=microphoneCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);
        var token=own.Token;
        UpdateCallView();
        bool joinSent=false;
        try {
            BeginInvitation();
            await Task.Delay(3000,token);
            EndInvitation();
            token.ThrowIfCancellationRequested();
            JObject config=null;
            try{config=await transport.Request(transport.Route("/voice/config"));}
            catch(LiveProtocolException e) when(e.Message=="voiceprint_not_configured"){}
            token.ThrowIfCancellationRequested();
            localVoice=(bool?)config?["configured"]!=true || (bool?)config?["requested"]!=true;
            if(localVoice && (bool?)state?["call"]?["voiceAvailable"]!=true)throw new LiveProtocolException("asr_not_configured");
            if(!localVoice)NativeRtc.EnsureAvailable();
            await AuthorizeMicrophone(token);
            token.ThrowIfCancellationRequested();
            if(localVoice)capture=OpenCallMicrophone();
            if(locallyPaused){await transport.Control("resume");locallyPaused=false;audioPlayer.Pause(false);}

            transcript.text="";transcript.style.display=DisplayStyle.None;
            joinSent=true;
            await transport.Input("call.join",new JObject());token.ThrowIfCancellationRequested();
            ApplyState(await transport.Request(transport.Route()));token.ThrowIfCancellationRequested();
            if(localVoice){
                if((bool?)state?["call"]?["active"]!=true)throw new LiveProtocolException("call_join_failed");
                rtcCallId=(string)state["call"]["id"];
                rtcReady=true;echoReference.Enabled=true;
                _=ListenToCall(own,rtcCallId);
                return;
            }
            var rtc=await transport.Voice("start");token.ThrowIfCancellationRequested();rtcCallId=(string)rtc["callId"];
            NativeRtc.Start((string)rtc["appId"],(string)rtc["roomId"],(string)rtc["userId"],(string)rtc["token"]);
            double deadline=Time.realtimeSinceStartupAsDouble+18;
            while(NativeRtc.State!=2){token.ThrowIfCancellationRequested();if(NativeRtc.State<0||Time.realtimeSinceStartupAsDouble>deadline)throw new LiveProtocolException("rtc_join_failed");await NextFrame();}
            await transport.Voice("joined",rtcCallId);token.ThrowIfCancellationRequested();
            while(!rtcReady){token.ThrowIfCancellationRequested();if(Time.realtimeSinceStartupAsDouble>deadline)throw new LiveProtocolException("voiceprint_connect_timeout");await NextFrame();}
        }catch(OperationCanceledException){if(microphoneCancel==own){if(joinSent)await EndCancelledCall();else CancelMicrophone();}}
        catch(Exception e){Toast(FriendlyError(e));if(microphoneCancel==own){if(joinSent)await EndCancelledCall();else CancelMicrophone();}}
        finally{
            if(microphoneCancel==own){
                callChanging=false;
                if(!rtcReady){microphoneCancel=null;own.Dispose();}
            }
            if(!disposed)UpdateCallView();
        }
    }
    void BeginInvitation(){
        inviting=true;
        audioPlayer.Pause(true);
        DisplayCaption(CallInvitation);
        if(invitationPlayer.clip!=null&&!muted){
            invitationPlayer.volume=audioPlayer.Source.volume;
            invitationPlayer.pitch=Mathf.Max(1f,invitationPlayer.clip.length/2.95f);
            invitationPlayer.Play();
        }
    }
    void EndInvitation(){
        if(!inviting)return;
        inviting=false;
        invitationPlayer.Stop();
        if(!locallyPaused&&!leaving&&!disposed)audioPlayer.Pause(false);
        DisplayCaption(lastHostCaption);
    }
    async Task EndCancelledCall(){NativeRtc.Stop();try{await transport.Input("call.end",new JObject());ApplyState(await transport.Request(transport.Route()));}catch{}CancelMicrophone();}

    void ReceiveCallVoice(JObject e) {
        if(rtcCallId==null||(string)e["callId"]!=rtcCallId||SessionClosing||disposed)return;
        switch((string)e["type"]){
            case "ready":rtcReady=true;break;
            case "transcript":if((bool?)e["speakerVerified"]==true)ShowPlayerSpeech((string)e["text"],(bool?)e["final"]==true);break;
            case "review":SetVoiceDraft((string)e["text"],360);input.Focus();Toast("请编辑这一句后发送");break;
            case "discard":transcript.style.display=DisplayStyle.None;break;
            case "error":Toast(FriendlyError(new LiveProtocolException((string)e["code"])));LeaveCall();break;
        }
    }
    async Task CallAction(string action,JObject payload=null){if(transport==null||locallyPaused||SessionClosing||SessionInputBlocked)return;try{await transport.Input(action,payload??new JObject());ApplyState(await transport.Request(transport.Route()));}catch(Exception e){Error(e);}}
    void UpdateCallView() {
        if(callButton==null)return;
        var call=state?["call"];
        if(rtcCallId!=null&&((bool?)call?["active"]!=true||(string)call?["id"]!=rtcCallId))CancelMicrophone();
        callLabel.text=rtcReady?(micMuted?"已静音":"连麦中"):callChanging?"请求中…":"连麦";
        string icon=micMuted?"mic-off":"mic";
        if(callIcon.name!=icon){callIcon.name=icon;callIcon.image=Resources.Load<Texture2D>("LiveGlass/"+icon);}
        callButton.tooltip=rtcReady?(micMuted?"恢复麦克风":"静音麦克风"):callChanging?"正在请求连麦":"申请连麦";
        callButton.SetEnabled(connected&&!leavingCall&&!callChanging&&!SessionClosing&&!SessionInputBlocked);
        leaveCall.style.display=rtcReady?DisplayStyle.Flex:DisplayStyle.None;
        if(!rtcReady)transcript.style.display=DisplayStyle.None;
        UpdateChatInputLimit();
        input.textEdition.placeholder=rtcReady?"已上麦，也可以打字…":"和 Z 聊聊…";
    }
    void UpdateCompanion(){
        if(thinkingAfter>0&&!thinkingOffered&&Time.unscaledTime>=thinkingAfter&&(bool?)state?["call"]?["active"]==true&&(bool?)state?["performance"]?["waiting"]==true&&state?["call"]?["thinkingAudio"] is JObject meta){thinkingOffered=true;_=audioPlayer.Thinking(meta,life.Token);}
if(transport==null||!connected||SessionClosing||SessionInputBlocked){CancelCompanion();return;}if(rtcReady&&!localVoice&&NativeRtc.State<0){Toast("连麦连接中断，请重新上麦");CancelMicrophone();_=CallAction("call.end");}}
    async Task EnsureMicrophonePermission(CancellationToken token) {
        if(Application.HasUserAuthorization(UserAuthorization.Microphone))return;requestingMicrophonePermission=true;
        try{var permission=Application.RequestUserAuthorization(UserAuthorization.Microphone);while(!permission.isDone){await NextFrame();token.ThrowIfCancellationRequested();}token.ThrowIfCancellationRequested();if(!Application.HasUserAuthorization(UserAuthorization.Microphone))throw new LiveProtocolException("microphone_permission_required");}
        finally{requestingMicrophonePermission=false;}
    }
    void CancelMicrophone(){
        EndInvitation();
        thinkingAfter=0;audioPlayer?.CancelThinking();
        if(echoReference!=null)echoReference.Enabled=false;
        capture?.Dispose();capture=null;
        localVoice=micMuted=rtcReady=callChanging=false;rtcCallId=null;
        microphoneCancel?.Cancel();microphoneCancel?.Dispose();microphoneCancel=null;
        NativeRtc.Stop();
    }
    void CancelCompanion(){CancelMicrophone();}
}
}
