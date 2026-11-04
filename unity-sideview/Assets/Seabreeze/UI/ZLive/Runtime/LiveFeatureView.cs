using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace ZLive {
// Presentation only: activity choices, content and transitions remain owned by the server agent.
public sealed partial class LiveFeatureController {
    VisualElement stage,header,sidebar,footer,settings,pausedOverlay,danmakuLayer,foregroundClip,foregroundArt;
    Label moodLabel,duration,toast;Button drawingChip,tarotChip,sound;bool muted=true,danmakuEnabled=true,expanded;float viewStarted;string restoredTarot,restoredDrawing;
    readonly List<Label> flying=new List<Label>();
    static T Class<T>(T view,string cls) where T:VisualElement {foreach(string name in cls.Split(' '))view.AddToClassList(name);return view;}
    static Label Text(string value,string cls)=>Class(new Label(value),cls);
    static Button Action(string value,System.Action callback,string cls="small-button")=>Class(new Button(callback){text=value},cls);
    static VisualElement Box(string cls)=>Class(new VisualElement(),cls);
    static Button Icon(string id,string title,System.Action action){var button=Action("",action,"icon-button");button.tooltip=title;button.Add(Class(new Image{image=Resources.Load<Texture2D>("Icons/"+id),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore},"control-icon"));return button;}
    static void SetIcon(Button button,string id,string title){button.text="";button.tooltip=title;var image=button.Q<Image>();if(image!=null)image.image=Resources.Load<Texture2D>("Icons/"+id);}

    void BuildUI(){
        document=gameObject.AddComponent<UIDocument>();var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1280,720);panel.match=0;panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("LiveTheme");panel.sortingOrder=200;document.panelSettings=panel;
        var root=document.rootVisualElement;root.AddToClassList("room");root.styleSheets.Add(Resources.Load<StyleSheet>("live"));root.style.unityFont=Resources.Load<Font>("Fonts/NotoSansSC-Regular");viewStarted=Time.unscaledTime;
        // Silent during development; this controls local monitoring, not playback completion evidence.
        muted=PlayerPrefs.GetInt("ZLive.Muted",1)==1;
#if UNITY_EDITOR
        muted=true; // Every editor test starts silently; the local sound button can opt in.
#endif
        audioPlayer.Source.mute=muted;
        header=Box("header");root.Add(header);header.Add(Action("←  返回",()=>ExitRequested?.Invoke(),"quiet"));header.Add(Text("Z","avatar host-avatar"));
        var brand=Box("brand");brand.Add(Text("Z 的聊天小屋","room-title"));brand.Add(Text("●  LIVE  ·  自主对话","live-label"));header.Add(brand);
        var follow=Action("＋ 关注",null,"follow");bool following=PlayerPrefs.GetInt("ZLive.Follow",0)==1;follow.text=following?"已关注":"＋ 关注";follow.clicked+=()=>{following=!following;follow.text=following?"已关注":"＋ 关注";PlayerPrefs.SetInt("ZLive.Follow",following?1:0);};header.Add(follow);header.Add(Box("spacer"));
        duration=Text("直播时长  00:00:00","stat");header.Add(duration);
        var body=Box("body");root.Add(body);stage=Box("stage");body.Add(stage);var room=Resources.Load<Texture2D>("room");stage.style.backgroundImage=new StyleBackground(room);
        if(character.Ready){characterImage=new Image{image=character.Texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};characterImage.AddToClassList("character");stage.Add(characterImage);}else{characterStatus=Text("角色资源待接入","character-status");stage.Add(characterStatus);}
        foregroundClip=Box("foreground-clip");foregroundClip.pickingMode=PickingMode.Ignore;foregroundArt=Box("foreground-art");foregroundArt.pickingMode=PickingMode.Ignore;foregroundArt.style.backgroundImage=new StyleBackground(room);foregroundClip.Add(foregroundArt);stage.Add(foregroundClip);
        stage.RegisterCallback<GeometryChangedEvent>(e=>{float w=e.newRect.width,h=e.newRect.height;if(tarot!=null)tarot.style.maxHeight=Mathf.Max(120,h-218);if(drawing!=null)drawing.style.maxHeight=Mathf.Max(120,h-218);foregroundArt.style.width=w;foregroundArt.style.height=h;foregroundArt.style.top=-h*.795f;if(characterImage!=null){character.SetViewport(w,h,Screen.width/Mathf.Max(1,root.resolvedStyle.width));characterImage.image=character.Texture;}});
        stage.RegisterCallback<PointerMoveEvent>(e=>TrackPointer(e.position),TrickleDown.TrickleDown);
        stage.RegisterCallback<PointerLeaveEvent>(e=>{if(e.target==stage)character.ClearPointerGaze();});
        var sceneTop=Box("scene-top");sceneTop.Add(Text("●  和 Z 聊聊天  ⌄","scene-badge"));sceneTop.Add(Box("spacer"));sceneTop.Add(Text("高清  ●","quality"));stage.Add(sceneTop);
        danmakuLayer=Box("danmaku-layer");danmakuLayer.pickingMode=PickingMode.Ignore;stage.Add(danmakuLayer);
        var speech=Box("speech-box");var speechHead=Box("speech-heading");speechHead.Add(Text("Z","speaker-z"));moodLabel=Text("等 Z 开口","mood-label");speechHead.Add(moodLabel);speechHead.Add(Box("spacer"));speechHead.Add(Text("▂ ▅ ▃ ▆","voice-bars"));speech.Add(speechHead);var captionScroll=Class(new ScrollView(),"caption-scroll");caption=Text("今晚，随便聊聊。","caption");captionScroll.verticalScrollerVisibility=ScrollerVisibility.Hidden;captionScroll.Add(caption);speech.Add(captionScroll);stage.Add(speech);
        tarot=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"activity-panel");tarot.style.display=DisplayStyle.None;stage.Add(tarot);
        drawing=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"activity-panel drawing-panel");drawing.style.display=DisplayStyle.None;stage.Add(drawing);var drawHead=Box("play-heading");drawHead.Add(Text("A LITTLE DRAWING BY Z","eyebrow"));drawHead.Add(Box("spacer"));drawHead.Add(Action("收起 ↘",()=>_=PanelIntent("drawing",false),"quiet"));drawing.Add(drawHead);activity=Text("Z 的小画室","panel-title");drawing.Add(activity);canvas=new DrawingPresenter();drawing.Add(canvas);drawing.Add(Text("01  轮廓       02  特征       03  完成","drawing-steps"));var drawActions=Box("play-actions");drawActions.Add(Action("修改这幅画",()=>{input.value="这幅画我想修改：";input.Focus();}));drawActions.Add(Action("收好画作 →",()=>_=PanelIntent("drawing",false)));drawing.Add(drawActions);
        var dock=Box("play-dock");tarotChip=Action("☾ 本轮塔罗",()=>_=PanelIntent("tarot",true));drawingChip=Action("✎ 看看画板",()=>_=PanelIntent("drawing",true));dock.Add(tarotChip);dock.Add(drawingChip);stage.Add(dock);
        var bottom=Box("scene-bottom");bottom.Add(Action("返回探索",()=>ExitRequested?.Invoke()));bottom.Add(Text("☾  月亮小岛 · Z 的小屋","location"));bottom.Add(Box("spacer"));sound=Icon(muted?"muted":"sound",muted?"本机已静音 · 点击开启声音":"关闭本机声音",ToggleSound);bottom.Add(sound);pause=Icon("pause","暂停直播",TogglePause);bottom.Add(pause);var dm=Action("弹",null,"icon-button");dm.clicked+=()=>{danmakuEnabled=!danmakuEnabled;dm.text=danmakuEnabled?"弹":"隐";danmakuLayer.style.display=danmakuEnabled?DisplayStyle.Flex:DisplayStyle.None;};bottom.Add(dm);bottom.Add(Icon("fullscreen","全屏直播",ToggleExpanded));bottom.Add(Icon("settings","直播设置",()=>settings.style.display=settings.resolvedStyle.display==DisplayStyle.None?DisplayStyle.Flex:DisplayStyle.None));stage.Add(bottom);
        settings=Box("settings");settings.style.display=DisplayStyle.None;settings.Add(Text("直播设置","panel-title"));settings.Add(Action("停止当前才艺",StopActivity));settings.Add(Action("返回探索",()=>ExitRequested?.Invoke()));settings.Add(Action("关闭",()=>settings.style.display=DisplayStyle.None));stage.Add(settings);
        pausedOverlay=Box("pause-overlay");pausedOverlay.Add(Text("Ⅱ","pause-mark"));pausedOverlay.Add(Text("把这一刻留久一点","pause-title"));pausedOverlay.Add(Action("继续直播",TogglePause));stage.Add(pausedOverlay);
        sidebar=Box("sidebar");body.Add(sidebar);
        var ch=Box("section-title chat-heading");ch.Add(Text("月光弹幕","blue-chip"));ch.Add(Box("spacer"));status=Text("正在连接…","agent-status");ch.Add(status);sidebar.Add(ch);chat=Class(new ScrollView(),"chat");chat.verticalScrollerVisibility=ScrollerVisibility.Hidden;sidebar.Add(chat);chat.Add(Text("☾  欢迎来到 Z 的聊天小屋\n     让月亮替你保管一点烦恼。","chat-welcome"));sidebar.Add(Text("✦  发一条弹幕，让 Z 认识你","chat-tip"));
        footer=Box("footer");root.Add(footer);var form=Box("chat-form");form.Add(Action("☺",()=>{input.value+=" ☾";input.Focus();},"emoji"));input=Class(new TextField{maxLength=120},"chat-input");input.textEdition.placeholder="和 Z 聊聊……";input.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter){Send();e.StopPropagation();}});var inputBox=input.Q<VisualElement>(className:"unity-base-text-field__input");if(inputBox!=null){inputBox.style.backgroundColor=Color.clear;inputBox.style.borderTopWidth=inputBox.style.borderBottomWidth=inputBox.style.borderLeftWidth=inputBox.style.borderRightWidth=0;}form.Add(input);form.Add(Action("➤",Send,"send"));footer.Add(form);footer.Add(Box("spacer"));int likes=PlayerPrefs.GetInt("ZLive.Likes",1286);var like=Action("♡  "+likes,null,"like");like.clicked+=()=>{likes++;like.text="♡  "+likes;PlayerPrefs.SetInt("ZLive.Likes",likes);};footer.Add(like);footer.Add(Action("↗",()=>{GUIUtility.systemCopyBuffer="Z 的聊天小屋 · 月亮小岛";Toast("已复制直播间名称");},"share"));toast=Text("","toast");toast.style.display=DisplayStyle.None;root.Add(toast);UpdatePanelLayout();
    }
    void TrackPointer(Vector2 panelPosition){if(stage==null)return;var local=stage.WorldToLocal(panelPosition);character.TrackPointer(local,stage.contentRect.size);}
    void ToggleSound(){muted=!muted;audioPlayer.Source.mute=muted;SetIcon(sound,muted?"muted":"sound",muted?"本机已静音 · 点击开启声音":"关闭本机声音");PlayerPrefs.SetInt("ZLive.Muted",muted?1:0);Toast(muted?"本机已静音":"本机声音已开启");}
    void ToggleExpanded(){expanded=!expanded;header.style.display=sidebar.style.display=footer.style.display=expanded?DisplayStyle.None:DisplayStyle.Flex;}
    void Toast(string text){toast.text=text;toast.style.display=DisplayStyle.Flex;toast.schedule.Execute(()=>toast.style.display=DisplayStyle.None).ExecuteLater(2400);}
    void UpdateView(){if(document==null||pausedOverlay==null)return;SetIcon(pause,locallyPaused?"play":"pause",locallyPaused?"继续直播":"暂停直播");pausedOverlay.style.display=locallyPaused?DisplayStyle.Flex:DisplayStyle.None;duration.text="直播时长  "+TimeSpan.FromSeconds(Time.unscaledTime-viewStarted).ToString(@"hh\:mm\:ss");status.tooltip=status.text;
        if(!locallyPaused)for(int i=flying.Count-1;i>=0;i--){var item=flying[i];float x=item.style.left.value.value-Time.unscaledDeltaTime*65;item.style.left=x;if(x < -item.resolvedStyle.width-30){item.RemoveFromHierarchy();flying.RemoveAt(i);}}
    }
    void ShowMood(JObject payload){string mood=(string)(payload["mood"]??payload["emotion"]);moodLabel.text=mood=="happy"?"笑着和你说":mood=="warm"?"温柔地说":mood=="serious"?"认真地说":mood=="sad"?"轻声说":mood=="curious"?"好奇地看着你":"和你聊着";}
    void UpdatePanelLayout(){if(drawingChip==null)return;drawingChip.style.display=state?["recentArtwork"] is JObject?DisplayStyle.Flex:DisplayStyle.None;tarotChip.style.display=state?["tarot"] is JObject?DisplayStyle.Flex:DisplayStyle.None;}
    void AddChat(string name,string text,bool mine){if(name=="Z")return;var row=Box(mine?"chat-row chat-mine":"chat-row");row.Add(Text(mine?"你":name.StartsWith("晚")?"风":name.StartsWith("月")?"月":"盐","avatar"));var content=Box("chat-content");content.Add(Text(name,"chat-name"));content.Add(Text(text,"chat-text"));row.Add(content);chat.Add(row);while(chat.childCount>70)chat.RemoveAt(0);chat.schedule.Execute(()=>chat.ScrollTo(row));
        if(danmakuEnabled&&connected&&flying.Count<8){var item=Text(text,"danmaku");item.pickingMode=PickingMode.Ignore;item.style.left=Mathf.Max(400,stage.resolvedStyle.width);item.style.top=55+(flying.Count%4)*30;danmakuLayer.Add(item);flying.Add(item);}
    }
    void RestorePanels(JObject snapshot){
        var t=snapshot["tarot"] as JObject;bool open=(bool?)snapshot["panels"]?["tarot"]==true;
        if(!open){tarot.style.display=DisplayStyle.None;restoredTarot=null;}
        else if(t!=null){string phase=(string)t["phase"],key=(string)t["id"]+":"+(string)t["round"]+":"+phase;if(key!=restoredTarot&&(phase=="awaiting_selection"||phase=="interpreting"||phase=="completed")){restoredTarot=key;var payload=(JObject)t.DeepClone();payload["view"]=phase=="awaiting_selection"?"backs":"revealed";ShowTarot(new JObject{{"taskId",t["id"]},{"resourceRevision",t["revision"]},{"payload",payload}});}}
        var art=snapshot["recentArtwork"] as JObject;bool drawingOpen=(bool?)snapshot["panels"]?["drawing"]==true;
        if(!drawingOpen){drawing.style.display=DisplayStyle.None;restoredDrawing=null;}
        else if(art!=null&&art["artifactId"]?.Type==JTokenType.String){string key=(string)art["artifactId"]+":"+(string)art["stage"];if(key!=restoredDrawing){restoredDrawing=key;_=RestoreDrawing(art,key);}}
    }
    async System.Threading.Tasks.Task RestoreDrawing(JObject art,string key){try{var bytes=await transport.Artifact((string)art["artifactId"],life.Token);if(disposed||key!=restoredDrawing)return;canvas.Present(JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes)),(int)art["stage"],(string)art["artifactId"]);drawing.style.display=DisplayStyle.Flex;}catch(OperationCanceledException){}catch(Exception e){Error(e);}}
    static string CardResource(string title){switch(title){case "星星":return "star";case "太阳":return "sun";case "魔术师":return "magician";case "节制":return "temperance";case "月亮":return "moon";case "世界":return "world";default:return "star";}}
    void ShowTarot(JObject command){var p=(JObject)command["payload"];bool backs=(string)p["view"]=="backs",viewer=(string)p["origin"]=="viewer";tarot.Clear();tarot.style.display=DisplayStyle.Flex;UpdatePanelLayout();var head=Box("play-heading");head.Add(Text("A MOMENT WITH THE CARDS","eyebrow"));head.Add(Box("spacer"));head.Add(Action("收起 ↘",()=>_=PanelIntent("tarot",false),"quiet"));tarot.Add(head);tarot.Add(Text(backs?(viewer?"这次，由你选牌":"这次，Z 为自己选牌"):(viewer?"你的牌面":"Z 的牌面"),"panel-title"));tarot.Add(Text((string)p["question"],"play-question"));tarot.Add(Text(backs?(viewer?"凭直觉，点开一张牌":"等 Z 选出心里的那张牌")+" · 第 "+(string)p["round"]+" 轮":"牌已翻开，听 Z 慢慢解读。","play-status"));var cards=Box("play-cards");tarot.Add(cards);
        if(backs){int index=0;foreach(JObject candidate in (JArray)p["candidates"]){string candidateId=(string)candidate["id"];var button=Action("",async()=>{try{foreach(var child in cards.Children())child.SetEnabled(false);await transport.Input("tarot.selection",new JObject{{"taskId",command["taskId"]},{"resourceRevision",command["resourceRevision"]},{"round",p["round"]},{"candidateId",candidateId}});}catch(Exception e){Error(e);foreach(var child in cards.Children())child.SetEnabled(viewer);}},"tarot-card");button.Add(Class(new Image{image=Resources.Load<Texture2D>("Cards/back"+index++),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore},"card-art"));button.Add(Text((string)candidate["label"],"card-label"));button.SetEnabled(viewer);cards.Add(button);}}
        else foreach(JObject card in (JArray)p["cards"]){var face=Box("tarot-card");face.Add(Class(new Image{image=Resources.Load<Texture2D>("Cards/"+CardResource((string)card["title"])),scaleMode=ScaleMode.ScaleToFit},"card-art"));face.Add(Text((string)card["title"]+" · 正位","card-label"));face.Add(Text((string)card["position"],"card-position"));cards.Add(face);}
        tarot.Add(Text(viewer?"不用想得太用力，留一点位置给直觉。":"这一次，让我们听听 Z 的心事。","play-note"));var actions=Box("play-actions");actions.Add(Action("先暂停",TogglePause));actions.Add(Action("补充我的情况",()=>input.Focus()));actions.Add(Action("结束本轮",StopActivity));tarot.Add(actions);
    }
}
}
