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
    Label callStatus,songStatus;
    Button callButton,microphoneButton;
    Toggle automaticVoice;
    TextField songRequest;
    string songFingerprint;
    float nextSongPoll,nextVoiceTurn;
    bool pollingSongs,callChanging,micStarting,recording,transcribing,voiceDetected;
    AudioClip microphoneClip;
    string microphoneDevice;
    float recordingStarted,lastVoiceAt;
    int microphoneVersion;
    CancellationTokenSource microphoneCancel,previewCancel;
    readonly HashSet<string> songWrites=new HashSet<string>();

    void BuildCompanionUI() {
        callBar=Box("call-bar");sidebar.Insert(0,callBar);
        callStatus=Text("文字聊天 · 可进入连麦席","call-status");callBar.Add(callStatus);
        var actions=Box("companion-actions");callBar.Add(actions);
        callButton=Action("连麦",ToggleCall);actions.Add(callButton);
        microphoneButton=Action("说话",ToggleMicrophone);actions.Add(microphoneButton);
        actions.Add(Action("邀请弹幕",()=>_=CallAction("call.audience")));
        automaticVoice=new Toggle("自动轮流说话");automaticVoice.RegisterValueChangedCallback(e=>{if(!e.newValue){CancelMicrophone();_=CallAction("call.input",new JObject{{"state","idle"}});}});callBar.Add(automaticVoice);
        storyChoices=Box("story-choices");callBar.Add(storyChoices);
        callBar.Add(Action("点歌与试听",ToggleSongs));
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
        if(transport==null||callChanging||locallyPaused)return;
        callChanging=true;CancelMicrophone();
        try {
            bool active=(bool?)state?["call"]?["active"]==true;
            await transport.Input(active?"call.end":"call.join",new JObject());
            ApplyState(await transport.Request(transport.Route()));
        }catch(Exception e){Error(e);}finally{callChanging=false;}
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
        callButton.text=active?"挂断":"连麦";
        callButton.SetEnabled(transport!=null&&!locallyPaused&&(bool?)state?["agent"]?["enabled"]==true);
        microphoneButton.SetEnabled(active&&voice&&!locallyPaused&&!transcribing);
        automaticVoice.SetEnabled(active&&voice&&!locallyPaused);
        microphoneButton.text=recording?"结束说话":transcribing?"识别中":"说话";
        callStatus.text=!active?"文字聊天 · 可进入连麦席":recording?"正在听你说 · 最长 20 秒":transcribing?"正在识别这一句":voice?"已连麦 · 可说话或发文字":"已连麦 · 使用文字（语音服务待配置）";
        input.maxLength=active?360:120;
        storyChoices.Clear();var story=call?["story"];
        if(active&&(string)story?["status"]=="offered") {
            var step=call["storyDefinition"]?["steps"]?[(int)story["step"]];
            if(step==null)return;
            storyChoices.Add(Text((string)step["question"],"song-note"));
            foreach(JObject choice in (JArray)step["choices"]) {
                string id=(string)choice["id"];int revision=(int)story["revision"];
                storyChoices.Add(Action((string)choice["label"],()=>_=CallAction("call.story",new JObject{{"choice",id},{"revision",revision}})));
            }
        }
        if(!active&&(recording||transcribing||micStarting))CancelMicrophone();
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
        }else if(automaticVoice.value&&!transcribing&&!micStarting&&!locallyPaused&&connected&&Application.isFocused&&
            (bool?)state?["call"]?["active"]==true&&(bool?)state?["call"]?["voiceAvailable"]==true&&
            (string)state?["call"]?["input"]=="idle"&&state?["voiceOwner"]?.Type==JTokenType.Null&&
            (bool?)state?["performance"]?["waiting"]!=true&&Time.unscaledTime>=nextVoiceTurn) {
            nextVoiceTurn=Time.unscaledTime+2;_=BeginMicrophone();
        }
    }

    void ToggleMicrophone(){if(recording)FinishMicrophone();else if(!micStarting&&!transcribing)_=BeginMicrophone();}

    async Task BeginMicrophone() {
        if(transport==null||locallyPaused||(bool?)state?["call"]?["active"]!=true)return;
        CancelMicrophone();micStarting=true;
        var turn=microphoneCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=turn.Token;
        try {
            var permission=Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while(!permission.isDone){await NextFrame();token.ThrowIfCancellationRequested();}
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone))throw new LiveProtocolException("microphone_permission_required");
            if(Microphone.devices.Length==0)throw new LiveProtocolException("microphone_unavailable");
            await transport.Input("call.input",new JObject{{"state","listening"}});token.ThrowIfCancellationRequested();
            var snapshot=await transport.Request(transport.Route());token.ThrowIfCancellationRequested();
            if((bool?)snapshot["call"]?["active"]!=true||(string)snapshot["call"]?["input"]!="listening")throw new OperationCanceledException();
            ApplyState(snapshot);microphoneVersion=(int)snapshot["call"]["version"];
            microphoneDevice=Microphone.devices[0];microphoneClip=Microphone.Start(microphoneDevice,false,21,16000);
            if(microphoneClip==null)throw new LiveProtocolException("microphone_unavailable");
            recording=true;voiceDetected=false;recordingStarted=lastVoiceAt=Time.unscaledTime;UpdateCallView();
        }catch(OperationCanceledException){}catch(Exception e){automaticVoice.SetValueWithoutNotify(false);Error(e);await ResetCallInput(turn);}
        finally{if(microphoneCancel==turn)micStarting=false;}
    }

    async void FinishMicrophone() {
        if(!recording||microphoneCancel==null)return;
        var turn=microphoneCancel;var token=turn.Token;var clip=microphoneClip;int frames=Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);recording=false;microphoneClip=null;transcribing=true;UpdateCallView();
        try {
            if(frames<clip.frequency/10||!voiceDetected){await ResetCallInput(turn);nextVoiceTurn=Time.unscaledTime+2;return;}
            var samples=new float[frames*clip.channels];clip.GetData(samples,0);
            int count=Math.Min(320000,(int)((long)frames*16000/clip.frequency));var pcm=new byte[count*2];
            for(int i=0;i<count;i++) {
                double position=(double)i*clip.frequency/16000;int left=(int)position,right=Math.Min(left+1,frames-1);float fraction=(float)(position-left),value=0;
                for(int channel=0;channel<clip.channels;channel++)value+=Mathf.Lerp(samples[left*clip.channels+channel],samples[right*clip.channels+channel],fraction)/clip.channels;
                short sample=(short)Mathf.RoundToInt(Mathf.Clamp(value,-1,1)*32767);pcm[i*2]=(byte)(sample&255);pcm[i*2+1]=(byte)((sample>>8)&255);
            }
            var result=await transport.Transcribe(pcm,microphoneVersion,token);token.ThrowIfCancellationRequested();
            if((int?)result["callVersion"]!=microphoneVersion)throw new OperationCanceledException();
            string text=(string)result["text"];
            if(automaticVoice.value)await transport.Input("viewer.message",new JObject{{"text",text}});
            else {await transport.Input("call.input",new JObject{{"state","reviewing"}});input.value=text;input.Focus();Toast("识别完成，确认文字后发送");}
            ApplyState(await transport.Request(transport.Route()));nextVoiceTurn=Time.unscaledTime+2;
        }catch(OperationCanceledException){}catch(Exception e){automaticVoice.SetValueWithoutNotify(false);Error(e);await ResetCallInput(turn);}
        finally{if(clip!=null)Destroy(clip);if(microphoneCancel==turn){transcribing=false;microphoneCancel=null;turn.Dispose();UpdateCallView();}}
    }

    async Task ResetCallInput(CancellationTokenSource turn) {
        if(microphoneCancel!=turn||turn.IsCancellationRequested||disposed||leaving)return;
        try{await transport.Input("call.input",new JObject{{"state","idle"}});ApplyState(await transport.Request(transport.Route()));}catch(Exception){}
    }
    void CancelMicrophone() {
        microphoneCancel?.Cancel();microphoneCancel?.Dispose();microphoneCancel=null;
        if(recording)Microphone.End(microphoneDevice);
        if(microphoneClip!=null)Destroy(microphoneClip);
        microphoneClip=null;recording=micStarting=transcribing=false;
    }
    void StopPreview(){previewCancel?.Cancel();previewCancel?.Dispose();previewCancel=null;}
    void CancelCompanion(){automaticVoice?.SetValueWithoutNotify(false);CancelMicrophone();StopPreview();}

    void ToggleSongs() {
        bool show=songPanel.style.display==DisplayStyle.None;songPanel.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        if(show){nextSongPoll=0;}else StopPreview();
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
            card.Add(Action("试听（暂停直播）",()=>_=PreviewSong((string)task["assetId"])));
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
        if(phase=="ready"||phase=="completed")card.Add(Text("返回直播并继续，Z 会根据对话安排演唱。","song-note"));
        if(phase!="playing"&&phase!="completed"&&phase!="cancelled"&&phase!="rejected")card.Add(Action("取消任务",()=>_=SongWrite(id,route+"cancel",new JObject())));
    }
    async Task PreviewSong(string assetId) {
        if(transport==null)return;StopPreview();CancelMicrophone();CancelAll();
        var preview=previewCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var token=preview.Token;
        try {
            locallyPaused=true;audioPlayer.Pause(true);await transport.Control("pause");token.ThrowIfCancellationRequested();
            var meta=await transport.Request(transport.Route("/song-preview/"+assetId));token.ThrowIfCancellationRequested();
            songStatus.text=muted?"正在试听 · 点击直播间声音按钮可解除静音":"正在试听";
            await audioPlayer.Preview(meta,token);songStatus.text="试听结束 · 完成审核后可返回直播并继续";
        }catch(OperationCanceledException){}catch(Exception e){if(!disposed)Error(e);}
        finally{if(previewCancel==preview){previewCancel=null;preview.Dispose();}}
    }
}
}
