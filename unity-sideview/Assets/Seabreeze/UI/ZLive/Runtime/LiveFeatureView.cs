using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    VisualElement roomContent, stage, sidebar, controls, speech, composer, dialogLayer, dialogContent;
    Button sound, sendButton, newMessages, reopen;
    Label toast, liveSessionLabel, transcript;
    bool muted, chatCollapsed;

    static T Class<T>(T element, string classes) where T : VisualElement {
        foreach (var name in classes.Split(' ')) element.AddToClassList(name);
        return element;
    }
    static VisualElement Box(string css) => Class(new VisualElement(), css);
    static Label Text(string value, string css) => Class(new Label(value), css);
    static Button Action(string value, Action handler, string css) => Class(new Button(handler){text=value}, css);
    static Image Art(string name, string css) => Class(new Image {
        image = Resources.Load<Texture2D>("LiveGlass/" + name),
        pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit
    }, css);
    static Button Tool(string name, string description, Action handler) {
        var button = Action("",handler,"tool");
        button.tooltip = description;
        button.Add(Art(name,"tool-icon"));
        return button;
    }

    void BuildUI() {
        document=gameObject.AddComponent<UIDocument>();
        var panel=ScriptableObject.CreateInstance<PanelSettings>();
        panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution=new Vector2Int(1280,800);
        panel.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match=.65f;
        panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("LiveTheme");
        panel.sortingOrder=200;
        document.panelSettings=panel;
        var root=document.rootVisualElement;
        root.AddToClassList("room");
        root.styleSheets.Add(Resources.Load<StyleSheet>("live"));
        root.style.unityFont=Resources.Load<Font>("Fonts/NotoSansSC-Regular");
        muted=PlayerPrefs.GetInt("ZLive.Muted",0)==1;
        audioPlayer.Source.mute=muted;
        var panelRoot=root;
        roomContent=Box("room-content");
        root.Add(roomContent);
        root=roomContent;
        BuildStage(root);
        var header=Box("header");
        root.Add(header);
        header.Add(Text("Z","host-avatar"));
        var brand=Box("brand");
        header.Add(brand);
        brand.Add(Text("Z 的聊天小屋","room-title"));
        liveSessionLabel=Text("• LIVE","live-label");
        brand.Add(liveSessionLabel);

        sidebar=Box("sidebar");
        root.Add(sidebar);
        var heading=Box("row");
        sidebar.Add(heading);
        heading.Add(Text("直播聊天","panel-title"));
        heading.Add(Box("spacer"));
        heading.Add(Art("people","people"));
        heading.Add(Text("3.2万","count"));
        chat=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"chat");
        sidebar.Add(chat);
        newMessages=Action("有新消息 ↓",ScrollToLatest,"pill");
        sidebar.Add(newMessages);
        newMessages.style.display=DisplayStyle.None;
        chat.verticalScroller.valueChanged+=_=>{if(NearChatBottom())newMessages.style.display=DisplayStyle.None;};
        transcript=Text("","transcript");
        sidebar.Add(transcript);
        transcript.style.display=DisplayStyle.None;
        status=Text("","notice");
        sidebar.Add(status);
        composer=Box("composer");
        sidebar.Add(composer);
        input=Class(new TextField{maxLength=120},"chat-input");
        input.textEdition.placeholder="和 Z 聊聊…";
        ConfigureChatInput();
        composer.Add(input);
        input.RegisterCallback<FocusInEvent>(_=>composer.AddToClassList("focused"));
        input.RegisterCallback<FocusOutEvent>(_=>composer.RemoveFromClassList("focused"));
        input.RegisterValueChangedCallback(_=>RefreshSessionControls());
        sendButton=Tool("send","发送",Send);
        sendButton.AddToClassList("send");
        composer.Add(sendButton);
        controls=Box("row controls");
        sidebar.Add(controls);
        controls.Add(Box("spacer"));
        sound=Tool("volume","直播声音",ToggleSound);
        controls.Add(sound);
        controls.Add(Tool("collapse","收起聊天",()=>SetChatCollapsed(true)));
        reopen=Tool("collapse","展开聊天",()=>SetChatCollapsed(false));
        reopen.AddToClassList("reopen");
        root.Add(reopen);
        reopen.style.display=DisplayStyle.None;
        var back=Action("",ShowExitDialog,"back");
        back.name="return-game";
        back.Add(Art("back","back-icon"));
        back.Add(Text("返回游戏","back-label"));
        back.Add(Text("Esc","keycap"));
        root.Add(back);
        toast=Text("","toast");
        toast.style.display=DisplayStyle.None;
        root.Add(toast);
        BuildDialog(panelRoot);
        RefreshSessionControls();
        RefreshSound();
    }

    void BuildStage(VisualElement root) {
        stage=Box("stage");
        root.Add(stage);
        // Keep the native character and the room plate separate so audio still drives the face.
        var room=Resources.Load<Texture2D>("room");
        stage.style.backgroundImage=new StyleBackground(character.Ready?room:Resources.Load<Texture2D>("LiveGlass/stage"));
        if(character.Ready){
            characterImage=new Image{image=character.Texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            characterImage.AddToClassList("character");
            stage.Add(characterImage);
            var clip=Box("foreground-clip");
            clip.pickingMode=PickingMode.Ignore;
            var art=Box("foreground-art");
            art.style.backgroundImage=new StyleBackground(room);
            clip.Add(art);
            stage.Add(clip);
            stage.RegisterCallback<GeometryChangedEvent>(e=>{
                var size=e.newRect.size;
                art.style.width=size.x;art.style.height=size.y;art.style.top=-size.y*.795f;
                character.SetViewport(size.x,size.y,Screen.width/Mathf.Max(1,root.resolvedStyle.width));
                characterImage.image=character.Texture;
            });
            stage.RegisterCallback<PointerMoveEvent>(e=>character.TrackPointer(stage.WorldToLocal(e.position),stage.contentRect.size));
            stage.RegisterCallback<PointerLeaveEvent>(_=>character.ClearPointerGaze());
        }
        speech=Box("speech-box");
        root.Add(speech);
        speech.Add(Text("Z","speaker"));
        var lines=Class(new ScrollView{verticalScrollerVisibility=ScrollerVisibility.Hidden},"caption-scroll");
        caption=Text("","caption");
        lines.Add(caption);
        speech.Add(lines);
        caption.RegisterCallback<GeometryChangedEvent>(_=>lines.scrollOffset=new Vector2(0,lines.verticalScroller.highValue));
        speech.style.display=DisplayStyle.None;
    }

    void SetChatCollapsed(bool value) {
        chatCollapsed=value;
        sidebar.style.display=value?DisplayStyle.None:DisplayStyle.Flex;
        reopen.style.display=value?DisplayStyle.Flex:DisplayStyle.None;
        if(!value)chat.schedule.Execute(ScrollToLatest);
    }
    void RefreshSound(){
        sound.EnableInClassList("muted",muted);
        sound.tooltip=muted?"开启直播声音":"关闭直播声音";
    }
    void ToggleSound(){
        muted=!muted;
        audioPlayer.Source.mute=muted;
        PlayerPrefs.SetInt("ZLive.Muted",muted?1:0);
        RefreshSound();
        _=SyncAudioCapabilities();
    }
    IVisualElementScheduledItem dismissToast;
    void Toast(string value){
        toast.text=value;toast.style.display=DisplayStyle.Flex;
        dismissToast?.Pause();
        dismissToast=toast.schedule.Execute(()=>toast.style.display=DisplayStyle.None).StartingIn(2900);
    }
    void UpdateView(){if(sendButton!=null)RefreshSessionControls();}
    bool NearChatBottom()=>chat.scrollOffset.y>=chat.verticalScroller.highValue-65;
    void ScrollToLatest(){if(chat.childCount>0)chat.ScrollTo(chat[chat.childCount-1]);newMessages.style.display=DisplayStyle.None;}
    void AddChat(string name,string value,bool mine,bool system=false){
        if(name=="Z")return;
        bool scroll=mine||NearChatBottom();
        var row=Box(mine?"chat-row mine":"chat-row");
        if(!system){
            var avatar=Box("avatar");
            string badge=mine?null:name switch {"橘子浮标"=>"buoy","倒带一格"=>"tape","困困豆"=>"bean","听雨同学"=>"rain","烤面包边"=>"toast",_=>null};
            if(badge!=null){
                avatar.AddToClassList("viewer-"+badge);
                avatar.Add(Text(name.Substring(0,1),"viewer-initial"));
            }else{
                string portrait=mine?"self":name=="晚风来信"?"wind":name=="海盐汽水"?"salt":name=="月亮邮差"?"moon":"guest";
                avatar.Add(Art(portrait,"portrait"));
            }
            row.Add(avatar);
        }
        var content=Box("message-content");
        row.Add(content);
        content.Add(Text(mine?"我":name,"nickname"));
        content.Add(Text(value,"message-text"));
        chat.Add(row);
        while(chat.childCount>80)chat.RemoveAt(0);
        if(scroll&&!chatCollapsed)chat.schedule.Execute(()=>{if(row.parent==chat)chat.ScrollTo(row);});
        else newMessages.style.display=DisplayStyle.Flex;
    }
}
}
