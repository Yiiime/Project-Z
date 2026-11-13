using System.Linq;
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
            const float deskEdge=.870f; // Rear edge of the tabletop in the room illustration.
            var clip=Box("foreground-clip");
            clip.style.top=Length.Percent(deskEdge*100);
            clip.pickingMode=PickingMode.Ignore;
            var art=Box("foreground-art");
            art.style.backgroundImage=new StyleBackground(room);
            clip.Add(art);
            stage.Add(clip);
            stage.RegisterCallback<GeometryChangedEvent>(e=>{
                var size=e.newRect.size;
                art.style.width=size.x;art.style.height=size.y;art.style.top=-size.y*deskEdge;
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
    readonly System.Collections.Generic.Dictionary<string,int> avatarSlots=new System.Collections.Generic.Dictionary<string,int>();
    int AvatarSlot(string name,string actor,bool mine){
        if(mine)return 8;
        string[] actors={"z","liyuan","liting","axing","zhengyi","xiaoxiaoqing","linxu","zhou_bobo"};
        int known=System.Array.IndexOf(actors,actor);if(known>=0)return known;
        if(avatarSlots.TryGetValue(name,out int saved))return saved;
        uint hash=2166136261;foreach(char ch in name)hash=(hash^ch)*16777619;
        // Prefer a different object, not just another colour of an NPC's object.
        int glyph=9+(int)(hash%7),slot=-1;
        for(int i=0;i<7;i++){int candidate=9+(glyph-9+i)%7;if(!avatarSlots.Values.Any(v=>v%16==candidate)){slot=candidate+16*(int)((hash/7)%8);break;}}
        if(slot<0){slot=9+(int)(hash%119);for(int i=0;i<119&&avatarSlots.ContainsValue(slot);i++)slot=9+(slot-8)%119;}
        avatarSlots[name]=slot;return slot;
    }
    void AddChat(string name,string value,bool mine,bool system=false){
        if(name=="Z")return;
        bool scroll=mine||NearChatBottom();
        var row=Box(mine?"chat-row mine":"chat-row");row.userData=mine?"player":name;
        if(!system){
            var avatar=Box("avatar");
            string actor=Seabreeze.SevenCharacterCatalog.Actors.FirstOrDefault(a=>a.displayName==name)?.id;
            var sheet=Resources.Load<Texture2D>("FirstDay/LiveAvatars");
            int slot=AvatarSlot(mine?"player":name,actor,mine);
            var icon=new Image { image=sheet, uv=new Rect((slot%16)/16f,(slot/16)/8f,1/16f,1/8f), scaleMode=ScaleMode.StretchToFill, pickingMode=PickingMode.Ignore };
            icon.style.width=Length.Percent(100);icon.style.height=Length.Percent(100);avatar.Add(icon);
            row.Add(avatar);
        }
        var content=Box("message-content");
        row.Add(content);
        content.Add(Text(mine?Seabreeze.PlayerIdentity.DisplayName:name,"nickname"));
        content.Add(Text(value,"message-text"));
        chat.Add(row);
        while(chat.childCount>80){string removed=chat[0].userData as string;chat.RemoveAt(0);if(removed!=null&&!chat.Children().Any(r=>(r.userData as string)==removed))avatarSlots.Remove(removed);}
        if(scroll&&!chatCollapsed)chat.schedule.Execute(()=>{if(row.parent==chat)chat.ScrollTo(row);});
        else newMessages.style.display=DisplayStyle.Flex;
    }
}
}
