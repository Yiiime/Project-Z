using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    Button rankButton,viewersButton,likeButton,sceneBadge,danmakuButton,sendButton,newMessages;
    Label audienceCount,replyStatus,inputCounter;
    VisualElement audienceBody,dialogLayer,dialogContent,emojiPopover,manga,mangaEyes,mangaMouth;
    readonly Dictionary<string,Label> messageReceipts=new Dictionary<string,Label>();
    int likes;bool restarting,mangaMode;
    string storyFingerprint;
    static readonly string[] LeaderNames={"晚风","纸月亮","海盐"};
    static readonly int[] LeaderMinutes={42,26,18};

    void LayoutRoom(Vector2 size) {
        if(size.x<1||body==null)return;
        // Desktop CSS uses fixed type sizes with viewport-dependent outer spacing.
        header.style.height=Mathf.Clamp(size.x*.04306f,56,72);
        footer.style.height=Mathf.Clamp(size.x*.04485f,58,75);
        body.style.paddingTop=Mathf.Clamp(size.x*.00837f,10,14);
        body.style.paddingBottom=Mathf.Clamp(size.x*.00538f,7,9);
        body.style.paddingLeft=body.style.paddingRight=Mathf.Clamp(size.x*.01196f,15,20);
        sidebar.style.width=size.x<=1100?294:Mathf.Clamp(size.x*.23565f,300,394);
        form.style.width=size.x<=1400?245:300;
        duration.style.display=size.x<=1100?DisplayStyle.None:DisplayStyle.Flex;
        document.rootVisualElement.EnableInClassList("short-room",size.y<=760);
    }
    static VisualElement Portrait(string name,bool mine=false) {
        string asset=mine?"self":name.Contains("风")?"wind":name.Contains("盐")?"salt":"moon";
        var frame=Box(mine?"avatar self-avatar":"avatar");frame.style.overflow=Overflow.Hidden;var image=new Image{image=Resources.Load<Texture2D>("Icons/"+asset),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.style.width=image.style.height=Length.Percent(100);frame.Add(image);return frame;
    }
    void BuildAudience() {
        var panel=Box("audience");sidebar.Add(panel);
        var toggle=Action("",null,"audience-summary");panel.Add(toggle);
        toggle.Add(Text("在线观众","blue-chip audience-title"));toggle.Add(Box("spacer"));audienceCount=Text("24,186 人在线  ⌃","audience-count");toggle.Add(audienceCount);
        audienceBody=Box("audience-body");panel.Add(audienceBody);
        toggle.clicked+=()=>{bool open=audienceBody.style.display!=DisplayStyle.None;audienceBody.style.display=open?DisplayStyle.None:DisplayStyle.Flex;RefreshRoomState();};
        var labels=Box("leader-labels");labels.Add(Text("排名      用户 ID",""));labels.Add(Box("spacer"));labels.Add(Text("相伴时光",""));audienceBody.Add(labels);
        AddLeaders(audienceBody);
    }
    void AddLeaders(VisualElement parent) {
        for(int i=0;i<LeaderNames.Length;i++) {int index=i;string name=LeaderNames[i];var row=Action("",()=>ShowProfile(index),"leader-row");row.Add(Portrait(name));row.Add(Text((i+1).ToString("00"),"leader-number"));row.Add(Text(name,"leader-name"));row.Add(Box("spacer"));row.Add(Text(LeaderMinutes[i]+" 分钟","leader-time"));parent.Add(row);}
    }
    void BuildDialog() {
        dialogLayer=Box("dialog-layer");dialogLayer.style.display=DisplayStyle.None;document.rootVisualElement.Add(dialogLayer);
        var paper=Box("dialog-paper");dialogLayer.Add(paper);var scroll=new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden};paper.Add(scroll);dialogContent=Box("dialog-content");scroll.Add(dialogContent);
        paper.Add(Action("×",CloseDialog,"dialog-close quiet"));dialogLayer.RegisterCallback<ClickEvent>(e=>{if(e.target==dialogLayer)CloseDialog();});
    }
    void OpenDialog(string eyebrow,string title) {dialogContent.Clear();dialogContent.Add(Text(eyebrow,"dialog-eyebrow"));dialogContent.Add(Text(title,"dialog-title"));dialogLayer.style.display=DisplayStyle.Flex;}
    public bool HandleBack(){if(dialogLayer.style.display!=DisplayStyle.None){CloseDialog();return true;}if(emojiPopover.style.display!=DisplayStyle.None){emojiPopover.style.display=DisplayStyle.None;return true;}if(expanded){ToggleExpanded();return true;}return false;}
    void CloseDialog()=>dialogLayer.style.display=DisplayStyle.None;
    void ShowAudience(){OpenDialog("MOON ISLAND FRIENDS","今晚的小岛同伴");dialogContent.Add(Text("谢谢每一位来到小屋的朋友。此刻有 "+((int?)state?["room"]?["viewers"]??24186).ToString("N0")+" 人和你一起看月亮。","dialog-description"));AddLeaders(dialogContent);dialogContent.Add(Text("观众人数与相伴时光为演示数据","demo-note"));}
    void ShowProfile(int index){OpenDialog("MOON ISLAND RESIDENT",LeaderNames[index]);dialogContent.Add(Text("在月亮小岛收集一点星光，也把温柔留给认真生活的人。","dialog-description"));dialogContent.Add(Text("Lv. 12  ·  小岛居民     "+LeaderMinutes[index]+" 分钟  ·  一起看月亮","dialog-description"));dialogContent.Add(Action("向 TA 打招呼",()=>{CloseDialog();SendText("@"+LeaderNames[index]+" 晚上好，一起听 Z 唱歌吧！");},"dialog-primary"));}
    void ShowHostProfile(){OpenDialog("MEET YOUR HOST","嗨，我是 Z。");dialogContent.Add(Text("我是 Z，住在月亮小岛。今天没有固定节目，我们随便聊聊。\n可以聊你的日常、心事，或一个突然想到的问题。","dialog-description"));dialogContent.Add(Action("和 Z 说声晚上好",()=>{CloseDialog();SendText("晚上好，Z！很高兴认识你。");},"dialog-primary"));}
    void ShowProgram(){OpenDialog("Z’S LITTLE STUDIO","今晚，在小屋玩什么？");dialogContent.Add(Text("在这里和 Z 聊聊日常，也可以告诉她你想听哪首歌。准备好以后，她会找个合适的时候唱给你听。","dialog-description"));dialogContent.Add(Action("回去和 Z 聊聊",()=>{CloseDialog();input.Focus();},"dialog-primary"));}
    static string VoiceErrorText(string code){switch(code){case "tts_provider_configuration_error":return "请检查豆包语音 Key 和音色权限";case "tts_provider_resource_not_granted":return "请在 Key 所属项目开通语音合成大模型 2.0";case "tts_provider_insufficient_balance":return "火山语音额度不足";case "tts_provider_rate_limited":return "语音请求较多，请稍候";default:return "请检查语音服务配置或稍后重试";}}
    async void RetryPendingReply(){try{await transport.Control("retry_reply");CloseDialog();}catch(Exception e){Error(e);}}
    void ShowConnection(){OpenDialog("LET’S TALK","和 Z 的连接");dialogContent.Add(Text(status.text,"dialog-description"));dialogContent.Add(Text((bool?)state?["agent"]?["enabled"]==true?"主 Agent 已连接。可以聊日常、心事，或一个突然想到的问题。":"请启动「启动直播服务.command」，在直播服务目录配置 DeepSeek Key 后重启服务，再进入直播。","dialog-description"));dialogContent.Add(Text((bool?)state?["room"]?["voiceAvailable"]==true?"Vivi 2.0 语音已配置。":"语音待配置：Vivi 2.0 使用 VOLC_TTS_API_KEY，麦克风识别使用 VOLC_ASR_API_KEY。","demo-note"));if(state?["room"]?["voiceError"]?.Type==JTokenType.String)dialogContent.Add(Text(VoiceErrorText((string)state["room"]["voiceError"]),"dialog-description"));if((string)state?["agent"]?["status"]=="unavailable")dialogContent.Add(Action("重试刚才的问题",RetryPendingReply,"dialog-primary"));dialogContent.Add(Action("重新连接",()=>{CloseDialog();_=RestartConversation();},"dialog-primary"));}
    void ShowShare(){OpenDialog("SHARE A LITTLE MOONLIGHT","把小屋分享给朋友");dialogContent.Add(Text("Z 的聊天小屋 · 月亮小岛","dialog-description"));dialogContent.Add(Action("复制邀请文字",()=>{GUIUtility.systemCopyBuffer="来月亮小岛，一起听 Z 聊聊天。";CloseDialog();Toast("邀请文字已复制");},"dialog-primary"));}
    void ShowSettings() {
        OpenDialog("MAKE YOURSELF AT HOME","让小屋更合你心意");
        var mode=new DropdownField("角色形象",new List<string>{"Z · Live2D","Z 的原画"},mangaMode?1:0);mode.RegisterValueChangedCallback(e=>SetCharacterMode(e.newValue=="Z 的原画"));dialogContent.Add(mode);
        dialogContent.Add(Text("Z 会随着聊天展现表情和动作，说话与唱歌时同步口型。","demo-note"));
        SettingSwitch("Vivi 2.0 语音",(bool?)state?["room"]?["voiceAvailable"]==true?"语音服务已配置":"语音尚未配置，文字互动照常进行",!muted,value=>{if(value==muted)ToggleSound();});
        var volumeRow=Box("settings-row");volumeRow.Add(Text("语音音量","settings-label"));volumeRow.Add(Box("spacer"));var volume=new Slider(0,100){value=audioPlayer.Source.volume*100,showInputField=true};volume.style.width=150;volume.RegisterValueChangedCallback(e=>{audioPlayer.Source.volume=e.newValue/100;PlayerPrefs.SetFloat("ZLive.Volume",audioPlayer.Source.volume);_=SyncAudioCapabilities();});volumeRow.Add(volume);dialogContent.Add(volumeRow);
        SettingSwitch("画面弹幕","右侧聊天记录始终保留",danmakuEnabled,value=>{if(value!=danmakuEnabled)ToggleDanmaku();});
        SettingSwitch("轻柔动画","减少肢体摆动和漂浮效果",character.Performance.Reduced,value=>{character.Performance.Reduced=value;PlayerPrefs.SetInt("ZLive.ReducedMotion",value?1:0);});
        dialogContent.Add(Action("好了，继续看月亮",CloseDialog,"dialog-primary"));
        dialogContent.Add(Action("重新开始对话",()=>{CloseDialog();_=RestartConversation();},"dialog-secondary"));
        dialogContent.Add(Text("模拟观众由模型生成。自由聊天的连接状态可在弹幕区查看。","demo-note"));
    }
    void SettingSwitch(string title,string note,bool initial,Action<bool> changed){var row=Box("settings-row");var label=Box("settings-text");label.Add(Text(title,"settings-label"));label.Add(Text(note,"demo-note"));row.Add(label);row.Add(Box("spacer"));bool on=initial;var toggle=Action("",null,"setting-switch");toggle.tooltip=title;toggle.EnableInClassList("on",on);toggle.Add(Box("switch-thumb"));toggle.clicked+=()=>{on=!on;toggle.EnableInClassList("on",on);changed(on);};row.Add(toggle);dialogContent.Add(row);}
    void ToggleDanmaku(){danmakuEnabled=!danmakuEnabled;danmakuLayer.style.display=danmakuEnabled?DisplayStyle.Flex:DisplayStyle.None;danmakuButton.EnableInClassList("active",danmakuEnabled);Toast(danmakuEnabled?"画面弹幕已开启":"画面弹幕已关闭，右侧记录会继续更新");}
    async Task SyncAudioCapabilities(){if(transport==null||!connected||locallyPaused)return;try{await transport.Input("client.capabilities_changed",new JObject{{"audioEnabled",!muted&&audioPlayer.Source.volume>0}});}catch(Exception e){Error(e);}}
    async Task RestartConversation(){if(restarting)return;restarting=true;try{CancelCompanion();CancelAll();if(transport!=null){try{await transport.Control("end");}catch(Exception){}transport.Dispose();transport=null;}connected=false;locallyPaused=true;PlayerPrefs.DeleteKey(SessionKey);while(events.TryDequeue(out _)){}while(connections.TryDequeue(out _)){}outcomes.Clear();messageIds.Clear();messageReceipts.Clear();chat.Clear();tarot.style.display=drawing.style.display=DisplayStyle.None;state=null;restoredTarot=restoredDrawing=null;caption.text="正在连接 Z…";await Enter();}catch(Exception e){Error(e);}finally{restarting=false;}}
    void BuildEmoji(){emojiPopover=Box("emoji-popover");emojiPopover.style.display=DisplayStyle.None;form.Add(emojiPopover);foreach(string emoji in new[]{"🌙","✨","💜","🌷","🥰","👏","🔮","☀️","🍀","晚安"}){string value=emoji;emojiPopover.Add(Action(emoji,()=>{AppendChatEmoji(value);emojiPopover.style.display=DisplayStyle.None;input.Focus();},"emoji-option"));}inputCounter=Text("","input-counter");form.Add(inputCounter);input.RegisterValueChangedCallback(e=>inputCounter.text=e.newValue.Length>90?e.newValue.Length+"/"+((bool?)state?["call"]?["active"]==true?360:120):"");}
    void ToggleEmoji()=>emojiPopover.style.display=emojiPopover.style.display==DisplayStyle.None?DisplayStyle.Flex:DisplayStyle.None;
    bool NearChatBottom()=>chat==null||chat.scrollOffset.y>=chat.verticalScroller.highValue-65;
    void ScrollToLatest(){if(chat.childCount>0)chat.ScrollTo(chat[chat.childCount-1]);newMessages.style.display=DisplayStyle.None;}
    static string ReceiptText(string value){switch(value){case "failed":return "回应暂未成功 · 点击连接状态重试";case "pending":return "已收到 · 正在准备回应";case "queued":return "待回应 · 等 Z 说完这段";case "replying":return "Z 正在回应";case "replied":return "Z 已完整回应";default:return "已送达";}}
    void RefreshRoomState(){
        if(state==null)return;var room=state["room"];int viewers=(int?)room?["viewers"]??24186;
        audienceCount.text=viewers.ToString("N0")+" 人在线  "+(audienceBody.style.display==DisplayStyle.None?"⌄":"⌃");viewersButton.text="◉  在线  "+viewers.ToString("N0");sceneBadge.text="●  "+((string)room?["stageLabel"]??"和 Z 聊聊天")+"  ⌄";
        string reply=(string)room?["replyStatus"];replyStatus.text="✦  "+(reply=="failed"?"这次回应没接上，点击连接状态可重试":reply=="pending"||reply=="queued"?"Z 正在整理你的问题，稍等一下":reply=="replying"?"Z 正在回应这条弹幕，听她说完吧":reply=="replied"?"这条已经聊完啦，继续和 Z 说说吧":reply=="seen"?"弹幕已送达，Z 会挑选话题和大家聊":"发一条弹幕，让 Z 认识你");
        if((bool?)state["call"]?["active"]==true)replyStatus.text="连麦中 · 点击右侧「下麦」结束";
        string code=(string)state["agent"]?["errorCode"],source=(string)state["agent"]?["status"];
        status.text=code=="agent_insufficient_balance"?"☾ DeepSeek 余额不足":(code=="agent_invalid_reply"||code=="agent_reply_not_approved")?"☾ 回应未成功 · 点击重试":source=="unavailable"?"☾ 主 Agent 暂时不可用":(bool?)state["agent"]?["enabled"]==true?"✦ 主 Agent 已连接":"☾ 主 Agent 未连接";
        if((bool?)state["agent"]?["enabled"]!=true&&caption.text=="正在连接 Z…")caption.text="主 Agent 未连接 · 点击弹幕区连接状态查看配置方式";
        foreach(JObject message in (JArray)(room?["messages"]??new JArray())){string id=(string)message["id"];if(id!=null&&messageReceipts.TryGetValue(id,out var label))label.text=ReceiptText((string)message["replyStatus"]);}
    }
    void SendText(string text){if(ChatInputComposing){Toast("请先完成正在输入的文字");return;}ReplaceChatDraft(text);Send();}
    async Task FinishPanel(string kind){try{var task=state?[kind=="drawing"?"recentArtwork":"tarot"] as JObject;if(task==null)return;if((string)(state?["activeTask"] as JObject)?["kind"]==kind)await transport.Input("activity.finish",new JObject{{"taskId",task["id"]}});else await PanelIntent(kind,false);}catch(Exception e){Error(e);}}
    void EditDrawing(){OpenDialog("Z’S LITTLE STUDIO","想改哪里？");var request=new TextField{maxLength=110};request.textEdition.placeholder="例如：把围巾改成蓝色，其他保持不变";dialogContent.Add(request);dialogContent.Add(Action("告诉 Z →",()=>{if(string.IsNullOrWhiteSpace(request.value))return;string value=request.value;CloseDialog();SendText("修改这幅画："+value);},"dialog-primary"));}
    void EnlargeDrawing(){OpenDialog("Z’S LITTLE STUDIO",activity.text);var large=new DrawingPresenter();large.style.height=320;large.style.minHeight=320;canvas.CopyTo(large);dialogContent.Add(large);dialogContent.Add(Text("当前画板上的草图","demo-note"));}
    void BuildManga(){manga=Box("manga");manga.style.backgroundImage=new StyleBackground(Resources.Load<Texture2D>("manga"));stage.Add(manga);mangaEyes=Box("manga-face");mangaEyes.style.backgroundImage=new StyleBackground(Resources.Load<Texture2D>("Icons/manga-eyes"));manga.Add(mangaEyes);mangaMouth=Box("manga-face");mangaMouth.style.backgroundImage=new StyleBackground(Resources.Load<Texture2D>("Icons/manga-mouth"));manga.Add(mangaMouth);SetCharacterMode(PlayerPrefs.GetInt("ZLive.Manga",0)==1);}
    void SetCharacterMode(bool value){mangaMode=value;manga.style.display=value?DisplayStyle.Flex:DisplayStyle.None;if(characterImage!=null)characterImage.style.display=value?DisplayStyle.None:DisplayStyle.Flex;foregroundClip.style.display=value?DisplayStyle.None:DisplayStyle.Flex;PlayerPrefs.SetInt("ZLive.Manga",value?1:0);}
    void UpdateManga(){if(!mangaMode)return;float t=character.Performance.TimeSeconds;mangaEyes.style.opacity=t%5.9f<.16f?1:0;mangaMouth.style.opacity=character.Performance.Phase=="speaking"&&!locallyPaused?Mathf.Clamp01(character.Performance.Mouth*3):0;}
}
}
