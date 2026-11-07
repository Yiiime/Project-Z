using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    VisualElement callBar,storyChoices;
    Label callStatus,sidebarCallStatus;
    Button callButton,sidebarCallButton;
    bool automaticVoice=true;string voiceNotice="";
    float nextVoiceTurn;
    bool callChanging,micStarting,recording,transcribing,voiceDetected,requestingMicrophonePermission,dictating;
    AudioClip microphoneClip;
    string microphoneDevice;
    float recordingStarted,lastVoiceAt;
    int microphoneVersion;
    CancellationTokenSource microphoneCancel;
    void BuildCompanionUI() {
        callButton=Icon("mic","语音输入",ToggleDictation);callButton.AddToClassList("call-toggle");form.Insert(form.IndexOf(sendButton),callButton);
        callStatus=Text("","call-status");callStatus.style.display=DisplayStyle.None;form.Add(callStatus);
        var controls=Box("companion-controls");sidebar.Insert(0,controls);
        var actions=Box("companion-actions");controls.Add(actions);
        sidebarCallButton=Action("上麦连聊",ToggleCall,"small-button companion-button");actions.Add(sidebarCallButton);
        sidebarCallStatus=Text("点击上麦，和 Z 轮流说话","companion-status");controls.Add(sidebarCallStatus);
        callBar=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"call-story-card");sidebar.Insert(1,callBar);
        storyChoices=Box("story-choices");callBar.Add(storyChoices);
        input.RegisterCallback<FocusInEvent>(evt=>{if((bool?)state?["call"]?["active"]==true){voiceNotice="";_=CallAction("call.input",new JObject{{"state","reviewing"}});}});
        input.RegisterCallback<FocusOutEvent>(evt=>{if((bool?)state?["call"]?["active"]==true&&string.IsNullOrWhiteSpace(input.value))_=CallAction("call.input",new JObject{{"state","idle"}});});
        UpdateCallView();
    }

    async void ToggleCall() {
        if(transport==null||callChanging)return;
        callChanging=true;CancelMicrophone();
        try {
            bool active=(bool?)state?["call"]?["active"]==true;
            if(!active&&(bool?)state?["call"]?["voiceAvailable"]!=true){voiceNotice="语音识别尚未配置，可直接打字";Toast(voiceNotice);return;}
            if(!active)await EnsureMicrophonePermission(life.Token);
            if(locallyPaused){await transport.Control("resume");locallyPaused=false;audioPlayer.Pause(false);}
            if(!active&&muted)ToggleSound();
            voiceNotice=active?"已下麦":"";automaticVoice=true;
            await transport.Input(active?"call.end":"call.join",new JObject());
            ApplyState(await transport.Request(transport.Route()));
            if(!active)await BeginMicrophone();
        }catch(OperationCanceledException){}catch(Exception e){voiceNotice=FriendlyError(e);Toast(voiceNotice);Error(e);}finally{callChanging=false;UpdateCallView();}
    }

    async Task CallAction(string action,JObject payload=null) {
        if(transport==null||locallyPaused)return;
        CancelMicrophone();
        try{await transport.Input(action,payload??new JObject());ApplyState(await transport.Request(transport.Route()));}
        catch(Exception e){Error(e);}
    }

    void UpdateCallView() {
        if(callButton==null)return;
        var call=state?["call"];
        bool active=(bool?)call?["active"]==true,voice=(bool?)call?["voiceAvailable"]==true;
        callButton.tooltip=active?"请先下麦再使用语音输入":dictating?(recording?"结束录音并转文字":"取消语音输入"):"语音输入 · 识别后确认发送";
        callButton.EnableInClassList("connected",dictating);
        bool enabled=!callChanging&&transport!=null&&(active||(bool?)state?["agent"]?["enabled"]==true);
        callButton.SetEnabled(transport!=null&&!active&&!callChanging);sidebarCallButton.SetEnabled(enabled&&!dictating);
        sidebarCallButton.text=callChanging?"连接中…":active?"下麦":"上麦连聊";
        sidebarCallButton.EnableInClassList("connected",active);
        callStatus.text=recording?(dictating?"正在录音 · 再点麦克风结束":"正在听你说…"):transcribing?"正在识别这一句":voiceNotice.Length>0?voiceNotice:active?(state?["voiceOwner"]?.Type!=JTokenType.Null?"Z 在说话…":(bool?)call?["busy"]==true?"Z 正在回应…":(string)call?["input"]=="reviewing"?"确认输入后发送":"已上麦"):"";
        callStatus.style.display=active||dictating||voiceNotice.Length>0?DisplayStyle.Flex:DisplayStyle.None;
        sidebarCallStatus.text=callStatus.text.Length>0?callStatus.text:voice?"点击上麦，和 Z 轮流说话":"语音识别待配置，可先打字聊天";
        UpdateChatInputLimit();input.textEdition.placeholder=active?"已上麦，也可以打字…":"和 Z 聊聊……";
        var story=call?["story"] as JObject;bool offered=active&&(string)story?["status"]=="offered";
        callBar.style.display=offered?DisplayStyle.Flex:DisplayStyle.None;
        string fingerprint=offered?story.ToString():"";
        if(fingerprint!=storyFingerprint){storyFingerprint=fingerprint;storyChoices.Clear();if(offered){
            var step=call["storyDefinition"]?["steps"]?[(int)story["step"]];
            if(step==null)return;
            storyChoices.Add(Text("今晚的小片段","eyebrow"));storyChoices.Add(Text((string)step["question"],"song-note"));
            foreach(JObject choice in (JArray)step["choices"]) {
                string id=(string)choice["id"];int revision=(int)story["revision"];
                storyChoices.Add(Action((string)choice["label"],()=>_=CallAction("call.story",new JObject{{"choice",id},{"revision",revision}})));
            }
        }}
        if(!active&&!dictating&&(recording||transcribing||micStarting))CancelMicrophone();
    }

    void UpdateCompanion() {
        if(transport==null)return;
        if(recording) {
            int position=Microphone.GetPosition(microphoneDevice);
            if(position>=256) {
                var samples=new float[256*microphoneClip.channels];
                microphoneClip.GetData(samples,position-256);float energy=0;
                foreach(float sample in samples)energy+=sample*sample;
                if(energy/samples.Length>.0003f){voiceDetected=true;lastVoiceAt=Time.unscaledTime;}
            }
            float elapsed=Time.unscaledTime-recordingStarted;
            if(elapsed>=19.5f||voiceDetected&&Time.unscaledTime-lastVoiceAt>.85f||!voiceDetected&&elapsed>8)FinishMicrophone();
        }else if(automaticVoice&&!dictating&&!callChanging&&!transcribing&&!micStarting&&!locallyPaused&&connected&&Application.isFocused&&
            (bool?)state?["call"]?["active"]==true&&(bool?)state?["call"]?["voiceAvailable"]==true&&
            (string)state?["call"]?["input"]=="idle"&&state?["voiceOwner"]?.Type==JTokenType.Null&&
            (bool?)state?["call"]?["busy"]!=true&&Time.unscaledTime>=nextVoiceTurn) {
            nextVoiceTurn=Time.unscaledTime+.6f;_=BeginMicrophone();
        }
    }


    async void ToggleDictation() {
        if(transport==null||callChanging||(bool?)state?["call"]?["active"]==true)return;
        if(dictating){if(recording)FinishMicrophone();else{CancelMicrophone();UpdateCallView();}return;}
        CancelMicrophone();dictating=true;micStarting=true;voiceNotice="";
        var turn=microphoneCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=turn.Token;
        try {
            await EnsureMicrophonePermission(token);
            var snapshot=await transport.Request(transport.Route());token.ThrowIfCancellationRequested();
            if((bool?)snapshot["call"]?["active"]==true)throw new OperationCanceledException();
            microphoneVersion=(int)snapshot["call"]["version"];
            StartMicrophone();
        }catch(OperationCanceledException){if(microphoneCancel==turn)CancelMicrophone();}catch(Exception e){voiceNotice=FriendlyError(e);Toast(voiceNotice);CancelMicrophone();}
        finally{if(microphoneCancel==turn)micStarting=false;UpdateCallView();}
    }

    void StartMicrophone() {
        if(Microphone.devices.Length==0)throw new LiveProtocolException("microphone_unavailable");
        microphoneDevice=Microphone.devices[0];microphoneClip=Microphone.Start(microphoneDevice,false,21,16000);
        if(microphoneClip==null)throw new LiveProtocolException("microphone_unavailable");
        recording=true;voiceDetected=false;recordingStarted=lastVoiceAt=Time.unscaledTime;UpdateCallView();
    }

    async Task EnsureMicrophonePermission(CancellationToken token) {
        if(Application.HasUserAuthorization(UserAuthorization.Microphone))return;
        requestingMicrophonePermission=true;
        try {
            var permission=Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while(!permission.isDone){await NextFrame();token.ThrowIfCancellationRequested();}
            token.ThrowIfCancellationRequested();
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone))throw new LiveProtocolException("microphone_permission_required");
        }finally{requestingMicrophonePermission=false;}
    }

    async Task EndFailedVoiceTurn(CancellationTokenSource turn,Exception error) {
        automaticVoice=false;voiceNotice=FriendlyError(error);Toast(voiceNotice);
        await ResetCallInput(turn);
        if(disposed||leaving||transport==null)return;
        try{await transport.Input("call.end",new JObject());ApplyState(await transport.Request(transport.Route()));}
        catch(Exception e){Error(e);}
    }

    async Task BeginMicrophone() {
        if(transport==null||locallyPaused||(bool?)state?["call"]?["active"]!=true)return;
        CancelMicrophone();micStarting=true;
        var turn=microphoneCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=turn.Token;
        try {
            await EnsureMicrophonePermission(token);
            if(Microphone.devices.Length==0)throw new LiveProtocolException("microphone_unavailable");
            await transport.Input("call.input",new JObject{{"state","listening"}});token.ThrowIfCancellationRequested();
            var snapshot=await transport.Request(transport.Route());token.ThrowIfCancellationRequested();
            if((bool?)snapshot["call"]?["active"]!=true||(string)snapshot["call"]?["input"]!="listening")throw new OperationCanceledException();
            ApplyState(snapshot);microphoneVersion=(int)snapshot["call"]["version"];
            StartMicrophone();
        }catch(OperationCanceledException){}catch(Exception e){await EndFailedVoiceTurn(turn,e);}
        finally{if(microphoneCancel==turn)micStarting=false;}
    }

    async void FinishMicrophone() {
        if(!recording||microphoneCancel==null)return;
        var turn=microphoneCancel;var token=turn.Token;bool draft=dictating;var clip=microphoneClip;int frames=Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);recording=false;microphoneClip=null;transcribing=true;UpdateCallView();
        try {
            if(frames<clip.frequency/10||!voiceDetected){if(draft){voiceNotice="没有听清，请再试一次";Toast(voiceNotice);}else await ResetCallInput(turn);nextVoiceTurn=Time.unscaledTime+2;return;}
            var samples=new float[frames*clip.channels];clip.GetData(samples,0);
            int count=Math.Min(320000,(int)((long)frames*16000/clip.frequency));var pcm=new byte[count*2];
            for(int i=0;i<count;i++) {
                double position=(double)i*clip.frequency/16000;int left=(int)position,right=Math.Min(left+1,frames-1);float fraction=(float)(position-left),value=0;
                for(int channel=0;channel<clip.channels;channel++)value+=Mathf.Lerp(samples[left*clip.channels+channel],samples[right*clip.channels+channel],fraction)/clip.channels;
                short sample=(short)Mathf.RoundToInt(Mathf.Clamp(value,-1,1)*32767);pcm[i*2]=(byte)(sample&255);pcm[i*2+1]=(byte)((sample>>8)&255);
            }
            var result=await transport.Transcribe(pcm,microphoneVersion,token,draft);token.ThrowIfCancellationRequested();
            if((int?)result["callVersion"]!=microphoneVersion)throw new OperationCanceledException();
            string text=(string)result["text"];
            if(draft){SetVoiceDraft(text,120);voiceNotice=text?.Length>120?"识别完成，请编辑至 120 字以内再发送":"识别完成，可编辑后发送";input.Focus();Toast(voiceNotice);return;}
            if(automaticVoice&&!string.IsNullOrWhiteSpace(text)&&text.Length<=360)await transport.Input("viewer.message",new JObject{{"text",text}});
            else {await transport.Input("call.input",new JObject{{"state","reviewing"}});SetVoiceDraft(text,360);voiceNotice=text?.Length>360?"识别完成，请编辑至 360 字以内再发送":"识别内容已放进输入框，确认后发送";input.Focus();Toast(voiceNotice);}
            ApplyState(await transport.Request(transport.Route()));nextVoiceTurn=Time.unscaledTime+2;
        }catch(OperationCanceledException){}catch(Exception e){if(draft){voiceNotice=FriendlyError(e);Toast(voiceNotice);}else await EndFailedVoiceTurn(turn,e);}
        finally{if(clip!=null)Destroy(clip);if(microphoneCancel==turn){transcribing=dictating=false;microphoneCancel=null;turn.Dispose();UpdateCallView();}}
    }

    void SetVoiceDraft(string text,int limit) {
        ReplaceChatDraft(text??"",limit);
    }

    async Task ResetCallInput(CancellationTokenSource turn) {
        if(microphoneCancel!=turn||turn.IsCancellationRequested||disposed||leaving)return;
        try{await transport.Input("call.input",new JObject{{"state","idle"}});ApplyState(await transport.Request(transport.Route()));}catch(Exception){}
    }
    void CancelMicrophone() {
        microphoneCancel?.Cancel();microphoneCancel?.Dispose();microphoneCancel=null;
        if(recording)Microphone.End(microphoneDevice);
        if(microphoneClip!=null)Destroy(microphoneClip);
        microphoneClip=null;recording=micStarting=transcribing=dictating=false;
    }
    void CancelCompanion(){automaticVoice=false;CancelMicrophone();}
}
}
