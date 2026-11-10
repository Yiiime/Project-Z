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
    Button callButton, leaveCall;
    Label callLabel;
    Image callIcon;
    float thinkingAfter;
    bool thinkingOffered, callChanging, requestingMicrophonePermission, rtcReady, micMuted;
    string rtcCallId;
    CancellationTokenSource microphoneCancel,enrollmentCancel;
    AudioClip enrollmentClip;
    void BuildCompanionUI() {
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
        caption.text=text??"";
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
        if(transport==null||enrollmentCancel!=null||leavingCall||SessionClosing||SessionInputBlocked)return;
        if(callChanging){microphoneCancel?.Cancel();return;}
        if(rtcReady){
            try{NativeRtc.SetMuted(!micMuted);micMuted=!micMuted;UpdateCallView();}
            catch(Exception e){Toast(FriendlyError(e));}
            return;
        }
        callChanging=true;var own=microphoneCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=own.Token;UpdateCallView();
        try {
            NativeRtc.EnsureAvailable();var config=await transport.Request(transport.Route("/voice/config"));token.ThrowIfCancellationRequested();
            if((bool?)config["needsEnrollment"]==true&&(bool?)config["credentialsConfigured"]==true){ShowVoiceSettings();return;}
            if((bool?)config["configured"]!=true||(bool?)config["requested"]!=true)throw new LiveProtocolException((bool?)config["needsEnrollment"]==true?"voiceprint_enrollment_required":"voiceprint_not_configured");
            await EnsureMicrophonePermission(token);
            if(locallyPaused){await transport.Control("resume");locallyPaused=false;audioPlayer.Pause(false);}

            transcript.text="";transcript.style.display=DisplayStyle.None;
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
        finally{
            if(microphoneCancel==own){
                callChanging=false;
                if(!rtcReady){microphoneCancel=null;own.Dispose();}
            }
            if(!disposed)UpdateCallView();
        }
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
        callLabel.text=rtcReady?(micMuted?"已静音":"连麦中"):callChanging?"等待接通…":"连麦";
        string icon=micMuted?"mic-off":"mic";
        if(callIcon.name!=icon){callIcon.name=icon;callIcon.image=Resources.Load<Texture2D>("LiveGlass/"+icon);}
        callButton.tooltip=rtcReady?(micMuted?"恢复麦克风":"静音麦克风"):callChanging?"取消连麦":"申请连麦";
        callButton.SetEnabled(connected&&!leavingCall&&enrollmentCancel==null&&!SessionClosing&&!SessionInputBlocked);
        leaveCall.style.display=rtcReady?DisplayStyle.Flex:DisplayStyle.None;
        if(!rtcReady)transcript.style.display=DisplayStyle.None;
        UpdateChatInputLimit();
        input.textEdition.placeholder=rtcReady?"已上麦，也可以打字…":"和 Z 聊聊…";
    }
    void UpdateCompanion(){
        if(thinkingAfter>0&&!thinkingOffered&&Time.unscaledTime>=thinkingAfter&&(bool?)state?["call"]?["active"]==true&&(bool?)state?["performance"]?["waiting"]==true&&state?["call"]?["thinkingAudio"] is JObject meta){thinkingOffered=true;_=audioPlayer.Thinking(meta,life.Token);}
if(transport==null||SessionClosing||SessionInputBlocked){CancelCompanion();return;}if(rtcReady&&NativeRtc.State<0){Toast("连麦连接中断，请重新上麦");CancelMicrophone();_=CallAction("call.end");}}
    async Task EnsureMicrophonePermission(CancellationToken token) {
        if(Application.HasUserAuthorization(UserAuthorization.Microphone))return;requestingMicrophonePermission=true;
        try{var permission=Application.RequestUserAuthorization(UserAuthorization.Microphone);while(!permission.isDone){await NextFrame();token.ThrowIfCancellationRequested();}token.ThrowIfCancellationRequested();if(!Application.HasUserAuthorization(UserAuthorization.Microphone))throw new LiveProtocolException("microphone_permission_required");}
        finally{requestingMicrophonePermission=false;}
    }
    void CancelMicrophone(){thinkingAfter=0;audioPlayer?.CancelThinking();micMuted=rtcReady=callChanging=false;rtcCallId=null;microphoneCancel?.Cancel();microphoneCancel?.Dispose();microphoneCancel=null;NativeRtc.Stop();}
    void CancelCompanion(){CancelMicrophone();enrollmentCancel?.Cancel();}
    async void ShowVoiceSettings() {
        OpenDialog("登记你的声音");
        dialogContent.Add(Action("取消",CloseDialog,"secondary"));
        var note=Text("正在读取声音设置…","dialog-copy");dialogContent.Add(note);
        try{var config=await transport.Request(transport.Route("/voice/config"));if(note.panel==null)return;
            if((bool?)config["needsEnrollment"]!=true){note.text="已记住你的声音。点击直播麦克风即可连麦。";return;}
            if((bool?)config["credentialsConfigured"]!=true){note.text="声音服务尚未配置，请在本地服务配置中补齐 RTC 与语音凭证。";return;}
            note.text="先在相对安静的地方录入约 5 秒有效人声，之后用于区分你和周围的人。录音只用于声纹登记，不保存原录音。";
            var start=Action("开始录入声音",null,"primary");dialogContent.Add(start);start.clicked+=()=>EnrollVoice(note,start);
        }catch(Exception e){note.text=FriendlyError(e);}
    }
    async void EnrollVoice(Label note,Button start) {
        if(enrollmentCancel!=null)return;if(callChanging||(bool?)state?["call"]?["active"]==true){note.text="请先下麦，再录入声音。";return;}
        var own=enrollmentCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=own.Token;start.SetEnabled(false);UpdateCallView();
        EventCallback<DetachFromPanelEvent> detached=_=>own.Cancel();note.RegisterCallback(detached);
        string device=null;
        try{await EnsureMicrophonePermission(token);if(Microphone.devices.Length==0)throw new LiveProtocolException("microphone_unavailable");
            device=Microphone.devices[0];enrollmentClip=Microphone.Start(device,false,10,16000);if(enrollmentClip==null)throw new LiveProtocolException("microphone_unavailable");
            // Count non-silent 20 ms windows; pauses between words do not use the voice budget.
            int rate=enrollmentClip.frequency, channels=enrollmentClip.channels;
            int window=Math.Max(1,rate/50), read=0, voicedWindows=0;
            float[] block=new float[window*channels];
            double deadline=Time.realtimeSinceStartupAsDouble+9;
            while(voicedWindows<250&&Time.realtimeSinceStartupAsDouble<deadline){
                token.ThrowIfCancellationRequested();
                int available=Microphone.GetPosition(device);
                while(read+window<=available&&voicedWindows<250){
                    enrollmentClip.GetData(block,read);
                    double power=0;foreach(float sample in block)power+=sample*sample;
                    if(power/block.Length>0.000064)voicedWindows++;
                    read+=window;
                }
                note.text="请自然地说：“今天我来到了月亮小岛，想听 Z 唱一首喜欢的歌。”\n还需约 "+Mathf.CeilToInt((250-voicedWindows)/50f)+" 秒人声";
                await NextFrame();
            }
            token.ThrowIfCancellationRequested();
            Microphone.End(device);
            if(voicedWindows<250)throw new LiveProtocolException("voiceprint_recording_too_quiet");
            var samples=new float[read*channels];enrollmentClip.GetData(samples,0);
            int count=(int)((long)read*16000/rate);var pcm=new short[count];
            for(int i=0;i<count;i++){
                int frame=(int)((long)i*rate/16000);float sample=0;
                for(int c=0;c<channels;c++)sample+=samples[frame*channels+c]/channels;
                pcm[i]=(short)Mathf.RoundToInt(Mathf.Clamp(sample,-1,1)*32767);
            }
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);foreach(short sample in pcm)writer.Write(sample);note.text="正在保存声音设置…";await transport.EnrollVoice(memory.ToArray(),token);}
            CloseDialog();Toast("声音已登记");document.rootVisualElement.schedule.Execute(()=>{if(!disposed&&!leaving&&!SessionClosing)ToggleCall();});
        }catch(OperationCanceledException){}catch(Exception e){note.text=FriendlyError(e);}finally{if(enrollmentClip!=null){if(device!=null&&Microphone.IsRecording(device))Microphone.End(device);Destroy(enrollmentClip);enrollmentClip=null;}note.UnregisterCallback(detached);enrollmentCancel=null;own.Dispose();start.SetEnabled(true);UpdateCallView();}
    }
}
}
