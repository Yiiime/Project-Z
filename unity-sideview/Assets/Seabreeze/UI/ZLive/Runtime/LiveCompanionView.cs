using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    VisualElement callBar,storyChoices,songPanel;
    ScrollView songList;
    Label callStatus,sidebarCallStatus,songStatus;
    Button callButton,sidebarCallButton;
    bool automaticVoice=true;string voiceNotice="";
    TextField songRequest;
    string songFingerprint;
    float nextSongPoll,nextVoiceTurn;
    bool pollingSongs,callChanging,micStarting,recording,transcribing,voiceDetected,requestingMicrophonePermission,dictating;
    AudioClip microphoneClip;
    string microphoneDevice;
    float recordingStarted,lastVoiceAt;
    int microphoneVersion;
    CancellationTokenSource microphoneCancel;
    bool previewStarting,previewActive;
    readonly HashSet<string> songWrites=new HashSet<string>();

    void BuildCompanionUI() {
        callButton=Icon("mic","语音输入",ToggleDictation);callButton.AddToClassList("call-toggle");form.Insert(form.IndexOf(sendButton),callButton);
        callStatus=Text("","call-status");callStatus.style.display=DisplayStyle.None;form.Add(callStatus);
        var controls=Box("companion-controls");sidebar.Insert(0,controls);
        var actions=Box("companion-actions");controls.Add(actions);
        sidebarCallButton=Action("上麦连聊",ToggleCall,"small-button companion-button");actions.Add(sidebarCallButton);
        actions.Add(Action("点歌与试听",ToggleSongs,"small-button companion-button"));
        sidebarCallStatus=Text("点击上麦，和 Z 轮流说话","companion-status");controls.Add(sidebarCallStatus);
        callBar=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"call-story-card");sidebar.Insert(1,callBar);
        storyChoices=Box("story-choices");callBar.Add(storyChoices);
        input.RegisterCallback<FocusInEvent>(evt=>{if((bool?)state?["call"]?["active"]==true){voiceNotice="";_=CallAction("call.input",new JObject{{"state","reviewing"}});}});
        input.RegisterCallback<FocusOutEvent>(evt=>{if((bool?)state?["call"]?["active"]==true&&string.IsNullOrWhiteSpace(input.value))_=CallAction("call.input",new JObject{{"state","idle"}});});
        songPanel=Box("song-panel");songPanel.style.display=DisplayStyle.None;document.rootVisualElement.Add(songPanel);
        var heading=Box("companion-actions");heading.Add(Text("点歌与试听","panel-title"));heading.Add(Box("spacer"));heading.Add(Action("关闭",ToggleSongs));songPanel.Add(heading);
        songPanel.Add(Text("明确歌曲、歌手与片段；新歌生成后需试听审核。","song-note"));
        songRequest=new TextField{multiline=true,maxLength=600};songRequest.AddToClassList("song-request");songRequest.textEdition.placeholder="例如：颜人中《晚安》的第一段副歌";songPanel.Add(songRequest);
        songPanel.Add(Action("提交点歌",()=>_=SubmitSong()));
        var libraryTitle=new TextField("曲库歌名"){maxLength=120};songPanel.Add(libraryTitle);
        var libraryArtist=new TextField("歌手（可选）"){maxLength=120};songPanel.Add(libraryArtist);
        var libraryResult=Text("查询第一段副歌的资料与已掌握状态。","song-note");songPanel.Add(libraryResult);
        songPanel.Add(Action("查询曲库",async()=>{
            if(transport==null)return;
            try {
                var result=await transport.Request("/api/song-library/lookup",new JObject{{"title",libraryTitle.value},{"artist",libraryArtist.value}});
                var lines=new List<string>();
                foreach(JObject item in (JArray)result["states"])lines.Add((string)item["recording"]["title"]+" · "+(string)item["recording"]["version"]+"："+(string)item["label"]);
                libraryResult.text=lines.Count>0?string.Join("\n",lines):"当前曲库暂无匹配资料；可提交点歌准备唱段。";
            }catch(Exception e){libraryResult.text=FriendlyError(e);}
        }));
        songStatus=Text("","song-note");songPanel.Add(songStatus);
        songPanel.Add(Action("停止试听",StopPreview));
        songList=new ScrollView();songList.style.flexGrow=1;songPanel.Add(songList);
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
        input.maxLength=Math.Max(active?360:120,input.value?.Length??0);input.textEdition.placeholder=active?"已上麦，也可以打字…":"和 Z 聊聊……";
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
        if(songPanel.resolvedStyle.display!=DisplayStyle.None&&!pollingSongs&&Time.unscaledTime>=nextSongPoll) {
            nextSongPoll=Time.unscaledTime+3;_=PollSongs();
        }
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
        }else if(automaticVoice&&!previewStarting&&!dictating&&!callChanging&&!transcribing&&!micStarting&&!locallyPaused&&connected&&Application.isFocused&&
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
            if(draft){input.maxLength=Math.Max(120,text?.Length??0);input.value=text;voiceNotice=text?.Length>120?"识别完成，请编辑至 120 字以内再发送":"识别完成，可编辑后发送";input.Focus();Toast(voiceNotice);return;}
            if(automaticVoice&&!string.IsNullOrWhiteSpace(text)&&text.Length<=360)await transport.Input("viewer.message",new JObject{{"text",text}});
            else {await transport.Input("call.input",new JObject{{"state","reviewing"}});input.value=text;voiceNotice="识别内容已放进输入框，确认后发送";input.Focus();Toast(voiceNotice);}
            ApplyState(await transport.Request(transport.Route()));nextVoiceTurn=Time.unscaledTime+2;
        }catch(OperationCanceledException){}catch(Exception e){if(draft){voiceNotice=FriendlyError(e);Toast(voiceNotice);}else await EndFailedVoiceTurn(turn,e);}
        finally{if(clip!=null)Destroy(clip);if(microphoneCancel==turn){transcribing=dictating=false;microphoneCancel=null;turn.Dispose();UpdateCallView();}}
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
    async void StopPreview(){
        var preview=state?["songPreview"] as JObject;if(transport==null||preview==null)return;
        try{await transport.Input("song.preview.stop",new JObject{{"taskId",preview["id"]}});songStatus.text="已停止试听";previewActive=false;}
        catch(Exception e){Error(e);}
    }
    void CancelCompanion(){automaticVoice=false;CancelMicrophone();}
    void UpdateSongPlayback(){
        var preview=state?["songPreview"] as JObject;
        if(preview!=null){previewActive=true;songStatus.text=(string)preview["phase"]=="preparing"?"正在准备试唱…":(string)preview["phase"]=="paused"?"试听已暂停":"Z 正在试唱 · "+(string)preview["title"];}
        else if(previewActive){previewActive=false;songStatus.text="试听结束 · 可以填写审核结论";}
    }

    void ToggleSongs() {
        bool show=songPanel.style.display==DisplayStyle.None;songPanel.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        if(show){nextSongPoll=0;}
    }
    async Task SubmitSong() {
        if(transport==null||string.IsNullOrWhiteSpace(songRequest.value))return;
        string text=songRequest.value.Trim();
        await SongWrite("submit","/api/singing/tasks",new JObject{{"text",text},{"sourceId",Guid.NewGuid().ToString()},{"sessionId",transport.SessionId}});
    }
    async Task SongWrite(string id,string route,JObject body) {
        if(transport==null||!songWrites.Add(id))return;
        try{await transport.Request(route,body);songStatus.text="已保存";songFingerprint=null;await PollSongs();}
        catch(Exception e){songStatus.text=FriendlyError(e);}
        finally{songWrites.Remove(id);}
    }
    async Task PollSongs() {
        if(pollingSongs||disposed||leaving||transport==null)return;pollingSongs=true;
        try {
            var result=await transport.Request("/api/singing/state");if(disposed||leaving)return;
            var tasks=(JArray)result["tasks"];string fingerprint=tasks.ToString();if(fingerprint==songFingerprint)return;
            songFingerprint=fingerprint;songList.Clear();
            if(tasks.Count==0)songList.Add(Text("还没有点歌任务。","song-note"));
            for(int i=tasks.Count-1;i>=0;i--)ShowSong((JObject)tasks[i]);
        }catch(Exception e){if(!disposed)songStatus.text=FriendlyError(e);}
        finally{pollingSongs=false;}
    }
    void ShowSong(JObject task) {
        string id=(string)task["id"],phase=(string)task["status"],route="/api/singing/tasks/"+id+"/";
        var card=Box("song-card");songList.Add(card);card.Add(Text((string)task["request"],"song-title"));
        var names=new Dictionary<string,string>{{"queued","排队中"},{"parsing","确认歌曲"},{"generating","生成歌声"},{"checking","检查音频"},{"awaiting_review","等待试听审核"},{"needs_input","需要补充"},{"ready","已审核，可安排演唱"},{"playing","正在直播播放"},{"completed","已播完"},{"failed","本次未完成"},{"cancelled","已取消"},{"rejected","无法生成"}};
        card.Add(Text((names.TryGetValue(phase,out var label)?label:phase)+"  "+(string)task["reason"],"song-note"));
        if(phase=="needs_input") {
            card.Add(Text((string)task["question"],"song-note"));var clarification=new TextField{maxLength=600};card.Add(clarification);
            card.Add(Action("补充说明",()=>_=SongWrite(id,route+"clarify",new JObject{{"text",clarification.value}})));
        }
        if(task["assetId"]?.Type==JTokenType.String&&(phase=="awaiting_review"||phase=="ready"||phase=="completed"))
            card.Add(Action("听 Z 试唱",()=>_=PreviewSong((string)task["assetId"])));
        if(phase=="awaiting_review") {
            var checks=new Dictionary<string,Toggle>();string[] keys={"song","segment","voice","aCappella","ending"},labels={"歌曲正确","片段正确","音色符合 Z","无伴奏","结尾完整"};
            for(int i=0;i<keys.Length;i++){var check=new Toggle(labels[i]);checks[keys[i]]=check;card.Add(check);}
            var note=new TextField{maxLength=600};note.textEdition.placeholder="填写试听结论";card.Add(note);
            card.Add(Action("审核通过",()=>{var values=new JObject();foreach(var pair in checks)values[pair.Key]=pair.Value.value;_=SongWrite(id,route+"review",new JObject{{"approved",true},{"checks",values},{"note",note.value}});}));
            card.Add(Action("审核不通过",()=>_=SongWrite(id,route+"review",new JObject{{"approved",false},{"note",note.value}})));
        }
        if(phase=="failed") {
            var acknowledgement=new Toggle("已核对上次生成结果，允许再次生成");card.Add(acknowledgement);
            card.Add(Action("重试",()=>_=SongWrite(id,route+"retry",new JObject{{"acknowledgeUnknown",acknowledgement.value}})));
        }
        if(phase=="ready"||phase=="completed")card.Add(Text("Z 会根据对话安排演唱，也可以点击「听 Z 试唱」。","song-note"));
        if(phase!="playing"&&phase!="completed"&&phase!="cancelled"&&phase!="rejected")card.Add(Action("取消任务",()=>_=SongWrite(id,route+"cancel",new JObject())));
    }
    async Task PreviewSong(string assetId) {
        if(transport==null||previewStarting)return;previewStarting=true;CancelMicrophone();
        try {
            if(locallyPaused){await transport.Control("resume");locallyPaused=false;audioPlayer.Pause(false);}
            if(muted)ToggleSound();
            if(audioPlayer.Source.volume<=0)audioPlayer.Source.volume=.85f;
            await SyncAudioCapabilities();
            await transport.Input("song.preview",new JObject{{"assetId",assetId}});
            ApplyState(await transport.Request(transport.Route()));songPanel.style.display=DisplayStyle.None;
            Toast("听 Z 试唱 · 可在点歌面板停止或填写审核结论");
        }catch(OperationCanceledException){}catch(Exception e){if(!disposed){songStatus.text=FriendlyError(e);Error(e);}}
        finally{previewStarting=false;}
    }
}
}
