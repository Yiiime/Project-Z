using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace ZLive {
// Presentation only: activity choices, content and transitions remain owned by the server agent.
public sealed partial class LiveFeatureController {
    VisualElement stage,header,sidebar,footer,settings,pausedOverlay,danmakuLayer,foregroundClip,foregroundArt,body,form;
    Label moodLabel,duration,toast,drawingSteps;Button drawingChip,tarotChip,sound;bool muted,danmakuEnabled=true,expanded;float viewStarted;string restoredTarot,restoredDrawing;
    readonly List<Label> flying=new List<Label>();
    static T Class<T>(T view,string cls) where T:VisualElement {foreach(string name in cls.Split(' '))view.AddToClassList(name);return view;}
    static Label Text(string value,string cls)=>Class(new Label(value),cls);
    static Button Action(string value,System.Action callback,string cls="small-button")=>Class(new Button(callback){text=value},cls);
    static VisualElement Box(string cls)=>Class(new VisualElement(),cls);
    static Button Icon(string id,string title,System.Action action){var button=Action("",action,"icon-button");button.tooltip=title;button.Add(Class(new Image{image=Resources.Load<Texture2D>("Icons/"+id),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore},"control-icon"));return button;}
    static void SetIcon(Button button,string id,string title){button.text="";button.tooltip=title;var image=button.Q<Image>();if(image!=null)image.image=Resources.Load<Texture2D>("Icons/"+id);}

    void BuildUI(){
        document=gameObject.AddComponent<UIDocument>();var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1280,720);panel.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight;panel.match=.5f;panel.scale=1;panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("LiveTheme");panel.sortingOrder=200;document.panelSettings=panel;
        var root=document.rootVisualElement;root.AddToClassList("room");root.styleSheets.Add(Resources.Load<StyleSheet>("live"));root.style.unityFont=Resources.Load<Font>("Fonts/NotoSansSC-Regular");viewStarted=Time.unscaledTime;
        muted=PlayerPrefs.GetInt("ZLive.Muted",0)==1;
        audioPlayer.Source.volume=PlayerPrefs.GetFloat("ZLive.Volume",.85f);
        character.Performance.Reduced=PlayerPrefs.GetInt("ZLive.ReducedMotion",0)==1;
        audioPlayer.Source.mute=muted;
        header=Box("header");root.Add(header);header.Add(Action("←  返回游戏",()=>ExitRequested?.Invoke(),"quiet back"));header.Add(Action("Z",ShowHostProfile,"avatar host-avatar"));
        var brand=Box("brand");brand.Add(Text("Z 的聊天小屋","room-title"));brand.Add(Text("●  LIVE  ·  自主对话","live-label"));header.Add(brand);
        var follow=Action("＋ 关注",null,"follow");bool following=PlayerPrefs.GetInt("ZLive.Follow",0)==1;follow.text=following?"已关注":"＋ 关注";follow.clicked+=()=>{following=!following;follow.text=following?"已关注":"＋ 关注";PlayerPrefs.SetInt("ZLive.Follow",following?1:0);};header.Add(follow);header.Add(Box("spacer"));
        rankButton=Action("♛ 热度排名  #14",ShowAudience,"quiet rank");header.Add(rankButton);viewersButton=Action("◉  在线  24,186",ShowAudience,"quiet viewers");header.Add(viewersButton);
        duration=Text("直播时长  00:00:00","stat");header.Add(duration);
        body=Box("body");root.Add(body);stage=Box("stage");body.Add(stage);var room=Resources.Load<Texture2D>("room");stage.style.backgroundImage=new StyleBackground(room);
        if(character.Ready){characterImage=new Image{image=character.Texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};characterImage.AddToClassList("character");stage.Add(characterImage);}else{characterStatus=Text("角色资源待接入","character-status");stage.Add(characterStatus);}
        foregroundClip=Box("foreground-clip");foregroundClip.pickingMode=PickingMode.Ignore;foregroundArt=Box("foreground-art");foregroundArt.pickingMode=PickingMode.Ignore;foregroundArt.style.backgroundImage=new StyleBackground(room);foregroundClip.Add(foregroundArt);stage.Add(foregroundClip);BuildManga();
        var hit=Action("",()=>SendText("晚上好，Z！"),"character-hit");hit.tooltip="和 Z 打招呼";stage.Add(hit);
        stage.RegisterCallback<GeometryChangedEvent>(e=>{float w=e.newRect.width,h=e.newRect.height;if(tarot!=null)tarot.style.maxHeight=Mathf.Max(100,h-211);if(drawing!=null)drawing.style.maxHeight=Mathf.Max(100,h-211);foregroundArt.style.width=w;foregroundArt.style.height=h;foregroundArt.style.top=-h*.795f;if(characterImage!=null){character.SetViewport(w,h,Screen.width/Mathf.Max(1,root.resolvedStyle.width));characterImage.image=character.Texture;}});
        stage.RegisterCallback<PointerMoveEvent>(e=>TrackPointer(e.position),TrickleDown.TrickleDown);
        stage.RegisterCallback<PointerLeaveEvent>(e=>{if(e.target==stage)character.ClearPointerGaze();});
        var sceneTop=Box("scene-top");sceneBadge=Action("●  和 Z 聊聊天  ⌄",ShowProgram,"scene-badge");sceneTop.Add(sceneBadge);sceneTop.Add(Box("spacer"));sceneTop.Add(Text("高清  ●","quality"));stage.Add(sceneTop);
        danmakuLayer=Box("danmaku-layer");danmakuLayer.pickingMode=PickingMode.Ignore;stage.Add(danmakuLayer);
        var speech=Box("speech-box");var speechHead=Box("speech-heading");speechHead.Add(Text("Z","speaker-z"));moodLabel=Text("等 Z 开口","mood-label");speechHead.Add(moodLabel);speechHead.Add(Box("spacer"));speechHead.Add(Text("▂ ▅ ▃ ▆","voice-bars"));speech.Add(speechHead);var captionScroll=Class(new ScrollView(),"caption-scroll");caption=Text("正在连接 Z…","caption");captionScroll.verticalScrollerVisibility=ScrollerVisibility.Hidden;captionScroll.Add(caption);speech.Add(captionScroll);stage.Add(speech);
        tarot=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"activity-panel");tarot.style.display=DisplayStyle.None;stage.Add(tarot);
        drawing=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"activity-panel drawing-panel");drawing.style.display=DisplayStyle.None;stage.Add(drawing);var drawHead=Box("play-heading");drawHead.Add(Text("A LITTLE DRAWING BY Z","eyebrow"));drawHead.Add(Box("spacer"));drawHead.Add(Action("收起 ↘",()=>_=PanelIntent("drawing",false),"quiet"));drawing.Add(drawHead);activity=Text("Z 的小画室","panel-title");drawing.Add(activity);canvas=new DrawingPresenter();drawing.Add(canvas);drawingSteps=Text("","drawing-steps");drawing.Add(drawingSteps);var drawActions=Box("play-actions");drawActions.Add(Action("放大看看",EnlargeDrawing));drawActions.Add(Action("修改这幅画",EditDrawing));drawActions.Add(Action("收好画作 →",()=>_=FinishPanel("drawing")));drawActions.Add(Action("结束本次",StopActivity));drawing.Add(drawActions);
        var dock=Box("play-dock");tarotChip=Action("☾ 本轮塔罗",()=>_=PanelIntent("tarot",true));drawingChip=Action("✎ 看看画板",()=>_=PanelIntent("drawing",true));dock.Add(tarotChip);dock.Add(drawingChip);stage.Add(dock);
        var bottom=Box("scene-bottom");bottom.Add(Text("☾  月亮小岛 · Z 的小屋","location"));bottom.Add(Box("spacer"));sound=Icon(muted?"muted":"sound",muted?"本机已静音 · 点击开启声音":"关闭本机声音",ToggleSound);bottom.Add(sound);pause=Icon("pause","暂停直播",TogglePause);bottom.Add(pause);danmakuButton=Action("弹",ToggleDanmaku,"icon-button");bottom.Add(danmakuButton);bottom.Add(Icon("fullscreen","全屏直播",ToggleExpanded));bottom.Add(Icon("settings","直播设置",ShowSettings));stage.Add(bottom);
        pausedOverlay=Box("pause-overlay");pausedOverlay.Add(Text("Ⅱ","pause-mark"));pausedOverlay.Add(Text("把这一刻留久一点","pause-title"));pausedOverlay.Add(Action("继续直播",TogglePause));stage.Add(pausedOverlay);
        sidebar=Box("sidebar");body.Add(sidebar);BuildAudience();
        var ch=Box("section-title chat-heading");ch.Add(Text("月光弹幕","blue-chip"));ch.Add(Box("spacer"));status=Text("正在连接…","agent-status");status.RegisterCallback<ClickEvent>(_=>ShowConnection());ch.Add(status);sidebar.Add(ch);chat=Class(new ScrollView(),"chat");chat.verticalScrollerVisibility=ScrollerVisibility.Hidden;sidebar.Add(chat);replyStatus=Text("✦  发一条弹幕，让 Z 认识你","chat-tip");sidebar.Add(replyStatus);newMessages=Action("有新弹幕 ↓",ScrollToLatest,"new-messages");newMessages.style.display=DisplayStyle.None;sidebar.Add(newMessages);chat.verticalScroller.valueChanged+=_=>{if(NearChatBottom())newMessages.style.display=DisplayStyle.None;};
        footer=Box("footer");root.Add(footer);form=Box("chat-form");form.Add(Action("☺",ToggleEmoji,"emoji"));input=Class(new TextField{maxLength=120},"chat-input");input.textEdition.placeholder="和 Z 聊聊……";ConfigureChatInput();var inputBox=input.Q<VisualElement>(className:"unity-base-text-field__input");if(inputBox!=null){inputBox.style.backgroundColor=Color.clear;inputBox.style.borderTopWidth=inputBox.style.borderBottomWidth=inputBox.style.borderLeftWidth=inputBox.style.borderRightWidth=0;}form.Add(input);sendButton=Action("➤",Send,"send");sendButton.tooltip="发送弹幕";form.Add(sendButton);footer.Add(form);footer.Add(Box("spacer"));likes=2380;likeButton=Action("♡  2,380",()=>{likes++;likeButton.text="♡  "+likes.ToString("N0");rankButton.text="♛ 热度排名  #"+Mathf.Max(1,14-(likes-2380)/1000);Toast("给 Z 留下一点星光");},"like");footer.Add(likeButton);footer.Add(Icon("share","分享直播间",ShowShare));toast=Text("","toast");toast.style.display=DisplayStyle.None;root.Add(toast);BuildDialog();BuildEmoji();root.RegisterCallback<GeometryChangedEvent>(e=>LayoutRoom(e.newRect.size));UpdatePanelLayout();BuildSessionControls();
    }
    void TrackPointer(Vector2 panelPosition){if(stage==null)return;var local=stage.WorldToLocal(panelPosition);character.TrackPointer(local,stage.contentRect.size);}
    void ToggleSound(){muted=!muted;audioPlayer.Source.mute=muted;SetIcon(sound,muted?"muted":"sound",muted?"本机已静音 · 点击开启声音":"关闭本机声音");PlayerPrefs.SetInt("ZLive.Muted",muted?1:0);_=SyncAudioCapabilities();Toast(muted?"语音已关闭":"语音已开启");}
    void ToggleExpanded(){expanded=!expanded;document.rootVisualElement.EnableInClassList("expanded",expanded);Toast(expanded?"已展开直播 · 再点全屏按钮恢复":"已恢复直播布局");}
    void Toast(string text){toast.text=text;toast.style.display=DisplayStyle.Flex;toast.schedule.Execute(()=>toast.style.display=DisplayStyle.None).ExecuteLater(2400);}
    void UpdateView(){if(document==null||pausedOverlay==null)return;RefreshSessionControls();SetIcon(pause,locallyPaused?"play":"pause",locallyPaused?"继续直播":"暂停直播");pausedOverlay.style.display=locallyPaused&&SessionPhase!="ended"&&!SessionClosing?DisplayStyle.Flex:DisplayStyle.None;duration.text="直播时长  "+TimeSpan.FromSeconds((viewEnded??Time.unscaledTime)-viewStarted).ToString(@"hh\:mm\:ss");status.tooltip=status.text;UpdateManga();
        if(!locallyPaused)for(int i=flying.Count-1;i>=0;i--){var item=flying[i];float x=item.style.left.value.value-Time.unscaledDeltaTime*65;item.style.left=x;if(x < -item.resolvedStyle.width-30){item.RemoveFromHierarchy();flying.RemoveAt(i);}}
    }
    void ShowMood(JObject payload){string mood=(string)(payload["mood"]??payload["emotion"]);moodLabel.text=mood=="happy"?"笑着和你说":mood=="warm"?"温柔地说":mood=="serious"?"认真地说":mood=="sad"?"轻声说":mood=="curious"?"好奇地看着你":"和你聊着";}
    void UpdatePanelLayout(){if(drawingChip==null)return;drawingChip.style.display=state?["recentArtwork"] is JObject&&drawing.style.display==DisplayStyle.None?DisplayStyle.Flex:DisplayStyle.None;tarotChip.style.display=state?["tarot"] is JObject&&tarot.style.display==DisplayStyle.None?DisplayStyle.Flex:DisplayStyle.None;}
    void AddChat(string name,string text,bool mine,string id=null,string receipt=null,bool system=false){if(name=="Z")return;bool scroll=mine||NearChatBottom();var row=Box(mine?"chat-row chat-mine":"chat-row");if(system)row.Add(Text("✦","system-mark"));else row.Add(Portrait(name,mine));var content=Box("chat-content");content.Add(Text(name+(mine?"   你":""),"chat-name"));content.Add(Text(text,"chat-text"));if(mine){var label=Text(ReceiptText(receipt),"message-receipt");content.Add(label);if(id!=null)messageReceipts[id]=label;}row.Add(content);chat.Add(row);while(chat.childCount>80){var old=chat[0];foreach(var key in new List<string>(messageReceipts.Keys))if(old.Contains(messageReceipts[key]))messageReceipts.Remove(key);chat.RemoveAt(0);}if(scroll)chat.schedule.Execute(()=>chat.ScrollTo(row));else newMessages.style.display=DisplayStyle.Flex;
        if(!system&&danmakuEnabled&&connected&&flying.Count<10){var item=Text(text,mine?"danmaku danmaku-mine":"danmaku");item.pickingMode=PickingMode.Ignore;item.style.left=Mathf.Max(400,stage.resolvedStyle.width);item.style.top=55+(flying.Count%5)*28;danmakuLayer.Add(item);flying.Add(item);}
    }
    void RestorePanels(JObject snapshot){
        var t=snapshot["tarot"] as JObject;bool open=(bool?)snapshot["panels"]?["tarot"]==true;
        if(!open){tarot.style.display=DisplayStyle.None;restoredTarot=null;}
        else if(t!=null){string phase=(string)t["phase"],key=(string)t["id"]+":"+(string)t["round"]+":"+phase+":"+(bool?)t["paused"]+":"+locallyPaused;if(key!=restoredTarot&&(phase=="candidates_ready"||phase=="awaiting_selection"||phase=="selection_locked"||phase=="revealing"||phase=="interpreting"||phase=="completed"||phase=="cancelled")){restoredTarot=key;var payload=(JObject)t.DeepClone();payload["view"]=(t["cards"] as JArray)?.Count>0?"revealed":"backs";ShowTarot(new JObject{{"taskId",t["id"]},{"resourceRevision",t["revision"]},{"payload",payload}});}}
        var art=snapshot["recentArtwork"] as JObject;bool drawingOpen=(bool?)snapshot["panels"]?["drawing"]==true;
        if(!drawingOpen){drawing.style.display=DisplayStyle.None;restoredDrawing=null;}
        else if(art!=null&&art["artifactId"]?.Type==JTokenType.String){string key=(string)art["artifactId"]+":"+(string)art["stage"];if(key!=restoredDrawing){restoredDrawing=key;_=RestoreDrawing(art,key);}}
    }
    async System.Threading.Tasks.Task RestoreDrawing(JObject art,string key){try{var bytes=await transport.Artifact((string)art["artifactId"],life.Token);if(disposed||key!=restoredDrawing)return;canvas.Present(JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes)),(int)art["stage"],(string)art["artifactId"]);drawing.style.display=DisplayStyle.Flex;}catch(OperationCanceledException){}catch(Exception e){Error(e);}}
    static string CardResource(string title){switch(title){case "星星":return "star";case "太阳":return "sun";case "魔术师":return "magician";case "节制":return "temperance";case "月亮":return "moon";case "世界":return "world";default:return "star";}}
    void ShowTarot(JObject command){
        var p=(JObject)command["payload"];var record=state?["tarot"] as JObject;bool current=(string)record?["id"]==(string)command["taskId"];
        bool backs=(string)p["view"]=="backs",viewer=(string)p["origin"]=="viewer",three=current&&(string)record["spread"]=="three",selectionPaused=current&&(bool?)record["paused"]==true;
        string phase=current?(string)record["phase"]:backs?"candidates_ready":"revealing";
        tarot.Clear();tarot.style.display=DisplayStyle.Flex;UpdatePanelLayout();
        var head=Box("play-heading");head.Add(Text("A MOMENT WITH THE CARDS","eyebrow"));head.Add(Box("spacer"));head.Add(Action("收起 ↘",()=>_=PanelIntent("tarot",false),"quiet"));tarot.Add(head);
        tarot.Add(Text((current?(string)record["owner"]:viewer?"你的":"Z")+"的问题 · "+(three?"三张牌阵":"单张牌阵"),"panel-title"));tarot.Add(Text((string)p["question"],"play-question"));
        tarot.Add(Text(selectionPaused?"本轮暂停 · 选择会保留":phase=="completed"?"解读完成 · 可以接着聊":phase=="selection_locked"?"已选定 · 不可改选":backs?(viewer?"轮到你选牌":"Z 正在选牌 · 你可以旁观"):"Z 正在解读","play-status"));
        var slots=Box("tarot-slots");tarot.Add(slots);int selected=(p["slots"] as JArray)?.Count??0;string[] names=three?new[]{"当前处境","留意因素","可尝试行动"}:new[]{"本次关注的角度"};for(int i=0;i<names.Length;i++)slots.Add(Text((i<selected?"✓ ":"○ ")+names[i],"tarot-slot"));
        var cards=Box("play-cards");tarot.Add(cards);
        if(backs){int index=0;foreach(JObject candidate in (JArray)p["candidates"]){string candidateId=(string)candidate["id"];var button=Action("",async()=>{try{foreach(var child in cards.Children())child.SetEnabled(false);await transport.Input("tarot.selection",new JObject{{"taskId",command["taskId"]},{"resourceRevision",command["resourceRevision"]},{"round",p["round"]},{"candidateId",candidateId}});}catch(Exception e){Error(e);ApplyState(await transport.Request(transport.Route()));}},"tarot-card");button.Add(Class(new Image{image=Resources.Load<Texture2D>("Cards/back0"),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore},"card-art"));button.Add(Text("ABC"[index].ToString(),"card-label"));button.Add(Text(index==1?"中间":(string)candidate["label"]+"边","card-position"));index++;button.SetEnabled(viewer&&phase=="awaiting_selection"&&!selectionPaused&&!locallyPaused);cards.Add(button);}}
        else foreach(JObject card in (JArray)p["cards"]){var face=Action("",()=>{OpenDialog("Z’S LITTLE STUDIO",(string)card["position"]+" · "+(string)card["title"]);dialogContent.Add(Text((string)card["meaning"],"dialog-description"));dialogContent.Add(Text((string)card["advice"],"dialog-description"));},"tarot-card");face.Add(Class(new Image{image=Resources.Load<Texture2D>("Cards/"+CardResource((string)card["title"])),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore},"card-art"));face.Add(Text((string)card["title"]+" · 正位","card-label"));face.Add(Text((string)card["position"],"card-position"));cards.Add(face);}
        tarot.Add(Text(backs?(three?"每轮三选一，选齐三张一起翻开":"只翻选中的一张，其余牌不揭示"):"点牌可复看 · 补充背景沿用原牌","play-note"));
        var actions=Box("play-actions");tarot.Add(actions);
        if(viewer&&backs&&(phase=="awaiting_selection"||phase=="candidates_ready"))actions.Add(Action(selectionPaused?"继续选牌":"先暂停",async()=>{try{await transport.Input("tarot.pause",new JObject{{"taskId",command["taskId"]}});ApplyState(await transport.Request(transport.Route()));}catch(Exception e){Error(e);}}));
        if(!backs)actions.Add(Action(viewer?"补充我的情况":"聊聊 Z 的问题",()=>input.Focus()));
        if(phase=="completed"||phase=="cancelled")actions.Add(Action("收好本轮 →",()=>_=FinishPanel("tarot")));
        else actions.Add(Action("结束本轮",StopActivity));
    }
}
}
