using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    [DefaultExecutionOrder(80)]
    public sealed class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }
        public bool HasOverlay => !string.IsNullOrEmpty(overlayMode);
        public bool IsOverlayTransitioning => overlayBusy;
        bool IsTransitioning => WorldDirector.Instance != null && WorldDirector.Instance.IsTransitioning;
        bool ConversationActive => session != null && session.Conversation != null &&
            (session.Conversation.IsActive || session.Conversation.IsTransitioning);
        Canvas canvas;
        RectTransform canvasRect, hud, bottomStrip, overlayRoot;
        CanvasGroup hudGroup, overlayGroup, toastGroup;
        Transform overlayContent;
        TextMeshProUGUI dateText, timeText, weatherText, placeText, ambienceText, objectiveText, toastText;
        LineIcon weatherIcon;
        InteractionPromptUI interactionPrompt;
        DialogueUIController dialogueUI;
        GameSession session;
        Coroutine overlayRoutine;
        bool initialized, hidden, overlayBusy;
        string overlayMode = "", observedEnding = "", pendingEnding = "";
        float toastUntil, previousVolume = 1f;
        static readonly Vector2 TL = new Vector2(0,1), TC = new Vector2(.5f,1), C = new Vector2(.5f,.5f), BL = Vector2.zero;

        public void Initialize()
        {
            if(initialized)return;
            session=GameSession.Instance;
            if(session==null){Debug.LogError("GameUI requires GameSession.Initialize first.");return;}
            initialized=true;Instance=this;
            UIPrimitives.Font=Style.Font!=null?Style.Font:Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            if(UIPrimitives.Font==null)UIPrimitives.Font=TMP_Settings.defaultFontAsset;
            var cg=new GameObject("Seabreeze Interface",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            cg.transform.SetParent(transform,false);
            canvas=cg.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            canvasRect=(RectTransform)cg.transform;
            var scaler=cg.GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=Style.ReferenceResolution;scaler.matchWidthOrHeight=.5f;
            if(EventSystem.current==null)
            {
                var es=new GameObject("UI Event System",typeof(EventSystem),typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform,false);
            }
            BuildHUD();
            interactionPrompt=gameObject.AddComponent<InteractionPromptUI>();
            interactionPrompt.Initialize(canvasRect);
            BuildToast();
            overlayRoot=Stretch("Overlay",canvasRect,Vector2.zero,Vector2.zero);
            overlayGroup=overlayRoot.gameObject.AddComponent<CanvasGroup>();
            overlayGroup.alpha=0;overlayGroup.blocksRaycasts=false;overlayGroup.interactable=false;
            overlayRoot.gameObject.SetActive(false);
            dialogueUI=gameObject.AddComponent<DialogueUIController>();
            dialogueUI.Initialize(session,canvasRect);
            session.Changed+=OnStateChanged;
            session.Story.Toast+=ShowToast;
            if(session.Conversation!=null)session.Conversation.Changed+=OnConversationChanged;
            observedEnding=session.State.ending??"";
            RefreshHUD();OnConversationChanged();
        }
        void OnDestroy()
        {
            if(Instance==this){Instance=null;Time.timeScale=1f;}
            if(session==null)return;
            session.Changed-=OnStateChanged;
            if(session.Story!=null)session.Story.Toast-=ShowToast;
            if(session.Conversation!=null)session.Conversation.Changed-=OnConversationChanged;
        }
        void BuildHUD()
        {
            hud=Stretch("Exploration HUD",canvasRect,Vector2.zero,Vector2.zero);
            hudGroup=hud.gameObject.AddComponent<CanvasGroup>();
            var information=Panel("Environment glass",hud,TL,TL,new Vector2(42,-38),new Vector2(360,166),Style.HudFill);
            ((RoundedPanel)information).CornerRadius=18;
            dateText=Label("Day",information.transform,TL,TL,new Vector2(22,-16),new Vector2(290,28),"第 1 天",20,Muted);
            timeText=Label("Time period",information.transform,TL,TL,new Vector2(20,-52),new Vector2(150,57),"上午",40,Paper);
            var weather=Rect("Weather icon",information.transform,TL,TL,new Vector2(179,-69),new Vector2(21,21));
            weatherIcon=weather.gameObject.AddComponent<LineIcon>();weatherIcon.symbol=LineIcon.Symbol.Sun;weatherIcon.color=Cool;weatherIcon.raycastTarget=false;
            weatherText=Label("Weather",information.transform,TL,TL,new Vector2(211,-67),new Vector2(145,34),"09:20 · 晴",18,Muted);
            var pin=Rect("Place pin",information.transform,TL,TL,new Vector2(21,-123),new Vector2(17,20));
            var pi=pin.gameObject.AddComponent<LineIcon>();pi.symbol=LineIcon.Symbol.Pin;pi.color=Cool;pi.raycastTarget=false;
            placeText=Label("Place",information.transform,TL,TL,new Vector2(48,-121),new Vector2(290,30),"海边小街",21,Paper);
            RightShortcut("地图","M",LineIcon.Symbol.Map,110,()=>ToggleOverlay("map"));
            RightShortcut("手机","Tab",LineIcon.Symbol.Phone,0,()=>ToggleOverlay("phone"));
            RightShortcut("日志","J",LineIcon.Symbol.Journal,-110,()=>ToggleOverlay("journal"));
            bottomStrip=Rect("Exploration footer",hud,BL,BL,new Vector2(42,28),new Vector2(1140,86));
            var footer=Panel("Atmosphere glass",bottomStrip,BL,BL,Vector2.zero,new Vector2(1140,86),new Color(.035f,.08f,.15f,.38f));
            ((RoundedPanel)footer).SetBorder(new Color(.67f,.81f,.88f,.16f),1);
            ambienceText=Label("Ambience",bottomStrip,TL,TL,new Vector2(20,-13),new Vector2(1098,27),"海风，和远处的浪。",19,Paper);
            Label("Controls",bottomStrip,TL,TL,new Vector2(20,-49),new Vector2(1098,27),"A / D  移动     空格  跳跃     E  交互     M  地图     J  日志     Esc  暂停",17,Muted);
            objectiveText=Label("Current thought",hud,new Vector2(1,1),new Vector2(1,1),new Vector2(-54,-52),new Vector2(610,76),"",19,Paper,TextAlignmentOptions.TopRight);
            CircleButton("Sound",hud,new Vector2(1,0),new Vector2(1,0),new Vector2(-48,30),LineIcon.Symbol.Sound,ToggleSound);
        }
        void ToggleSound()
        {
            if(AudioListener.volume>.01f){previousVolume=AudioListener.volume;AudioListener.volume=0;}
            else AudioListener.volume=Mathf.Max(.05f,previousVolume);
            ShowToast(AudioListener.volume<.01f?"声音已关闭":"海风回来了");
        }
        void RightShortcut(string label,string key,LineIcon.Symbol symbol,float y,Action action)
        {
            var button=CircleButton(label,hud,new Vector2(1,.5f),C,new Vector2(-73,y),symbol,action);
            Label("Key",button.transform,new Vector2(.5f,0),TC,new Vector2(0,-7),new Vector2(92,26),key,16,Muted,TextAlignmentOptions.Top);
        }
        Button CircleButton(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,LineIcon.Symbol symbol,Action action)
        {
            var panel=Panel(name,parent,anchor,pivot,pos,new Vector2(62,62),Style.HudFill,true);
            ((RoundedPanel)panel).CornerRadius=31;
            ((RoundedPanel)panel).SetBorder(Style.PanelBorder,1);
            var button=panel.gameObject.AddComponent<Button>();button.targetGraphic=panel;
            button.navigation=new Navigation{mode=Navigation.Mode.None};
            button.onClick.AddListener(()=>{if(!IsTransitioning&&!overlayBusy&&!ConversationActive)action();});
            var icon=Rect("Icon",panel.transform,C,C,Vector2.zero,new Vector2(30,30)).gameObject.AddComponent<LineIcon>();
            icon.symbol=symbol;icon.ring=false;icon.color=Paper;icon.raycastTarget=false;
            UIHoverFeedback.Attach(button);
            return button;
        }
        void BuildToast()
        {
            var rt=Rect("Quiet notification",canvasRect,TC,TC,new Vector2(0,-36),new Vector2(650,48));
            var panel=Panel("Toast glass",rt,C,C,Vector2.zero,new Vector2(650,48),Style.HudFill);
            ((RoundedPanel)panel).CornerRadius=24;
            toastText=Label("Toast text",rt,C,C,Vector2.zero,new Vector2(610,40),"",19,Paper,TextAlignmentOptions.Midline);
            toastGroup=rt.gameObject.AddComponent<CanvasGroup>();toastGroup.alpha=0;toastGroup.blocksRaycasts=false;
        }
        void OnStateChanged()
        {
            RefreshHUD();
            string ending=session.State.ending??"";
            if(ending!=observedEnding){observedEnding=ending;if(ending.Length>0)pendingEnding=ending;}
        }
        void OnConversationChanged()
        {
            if(!initialized)return;
            if(ConversationActive)
            {
                interactionPrompt.Hide(true);
                if(HasOverlay)CloseOverlayImmediately();
            }
        }
        void RefreshHUD()
        {
            if(!initialized)return;
            dateText.text="第 "+session.Clock.Day+" 天";
            timeText.text=PeriodName(session.Clock.Period);
            string clock=session.Clock.TimeLabel;
            int clockStart=clock.IndexOfAny("0123456789".ToCharArray());
            if(clockStart>=0)clock=clock.Substring(clockStart);
            string weather=session.Clock.WeatherLabel.Split('·')[0].Trim();
            weatherText.text=clock+" · "+weather;
            weatherIcon.symbol=session.Clock.Period==TimePeriod.Night?LineIcon.Symbol.Moon:LineIcon.Symbol.Sun;
            weatherIcon.SetVerticesDirty();
            placeText.text=PlaceName(session.CurrentPlace);
            objectiveText.text=session.Story.Objective;
            ambienceText.text=session.CurrentPlace==PlaceId.Cafe?"杯碟轻响。烘焙的香气，藏在海风够不到的地方。":session.CurrentPlace==PlaceId.Lighthouse?"灯塔静静转身。海浪替那些没说完的话，停顿了一下。":"海风，和远处的浪。街角有人把今天的第一杯咖啡放在窗边。";
        }
        public static string PlaceName(PlaceId id)=>id==PlaceId.Cafe?"潮汐咖啡馆":id==PlaceId.Lighthouse?"灯塔海堤":"海边小街";
        public static string PeriodName(TimePeriod period)=>period==TimePeriod.Morning?"上午":period==TimePeriod.Afternoon?"午后":period==TimePeriod.Evening?"傍晚":"夜晚";
        public void ShowInteraction(string text,Vector3 worldPosition)
        {
            if(!initialized)return;
            bool npc=false;
            if(WorldDirector.Instance!=null&&WorldDirector.Instance.Player!=null)
            {
                var interaction=WorldDirector.Instance.Player.GetComponent<PlayerInteraction>();
                npc=interaction!=null&&interaction.Current!=null&&interaction.Current.npc;
            }
            interactionPrompt.Show(text,worldPosition,npc);
        }
        public void HideInteraction(){if(interactionPrompt!=null)interactionPrompt.Hide();}
        public void ShowToast(string text)
        {
            if(!initialized)return;
            toastText.text=text;toastUntil=Time.unscaledTime+3.8f;
            toastGroup.transform.SetAsLastSibling();
        }
        void Update()
        {
            if(!initialized||session==null)return;
            bool conversation=ConversationActive;
            float hudTarget=!hidden&&!conversation?1:0;
            hudGroup.alpha=Mathf.MoveTowards(hudGroup.alpha,hudTarget,Time.unscaledDeltaTime/Style.FadeInDuration);
            hudGroup.interactable=!hidden&&!conversation&&!HasOverlay&&!IsTransitioning;
            hudGroup.blocksRaycasts=hudGroup.interactable;
            interactionPrompt.SetSuppressed(hidden||conversation||HasOverlay||IsTransitioning);
            toastGroup.alpha=Mathf.MoveTowards(toastGroup.alpha,Time.unscaledTime<toastUntil?1:0,Time.unscaledDeltaTime/Style.FadeInDuration);
            if(IsTransitioning||conversation||overlayBusy)return;
            // Conversation input, including Escape, belongs exclusively to DialogueUIController.
            if(GameInput.Pressed(Key.Escape)){if(HasOverlay)CloseOverlay();else OpenOverlay("pause");}
            else if(GameInput.Pressed(Key.M))ToggleOverlay("map");
            else if(GameInput.Pressed(Key.Tab))ToggleOverlay("phone");
            else if(GameInput.Pressed(Key.J))ToggleOverlay("journal");
            else if(GameInput.Pressed(Key.H))hidden=!hidden;
            if(!string.IsNullOrEmpty(pendingEnding)&&!HasOverlay&&!ConversationActive)
            {pendingEnding="";OpenOverlay("ending");}
        }
        void ToggleOverlay(string mode)
        {
            if(ConversationActive||overlayBusy||IsTransitioning)return;
            if(overlayMode==mode)CloseOverlay();else OpenOverlay(mode);
        }
        void OpenOverlay(string mode)
        {
            if(!initialized||IsTransitioning||ConversationActive||overlayBusy)return;
            overlayBusy=true;session.SetOverlay(true);Time.timeScale=0f;
            interactionPrompt.Hide(true);
            overlayRoutine=StartCoroutine(ShowOverlayRoutine(mode));
        }
        IEnumerator ShowOverlayRoutine(string mode)
        {
            overlayRoot.gameObject.SetActive(true);overlayGroup.blocksRaycasts=true;overlayGroup.interactable=false;
            if(HasOverlay)yield return FadeOverlay(0,Style.FadeOutDuration);
            overlayMode=mode;
            BuildOverlay(mode);
            yield return FadeOverlay(1,Style.FadeInDuration);
            overlayBusy=false;overlayRoutine=null;overlayGroup.interactable=true;
        }
        void BuildOverlay(string mode)
        {
            Clear(overlayRoot);
            var scrim=Stretch("Backdrop",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();
            scrim.color=Style.Scrim;scrim.raycastTarget=true;
            Vector2 size=mode=="pause"||mode=="restart"?new Vector2(760,730):mode=="ending"?new Vector2(1160,790):new Vector2(1280,790);
            var panel=Panel("Panel",overlayRoot,C,C,Vector2.zero,size,Ink,true);
            ((RoundedPanel)panel).CornerRadius=24;
            overlayContent=panel.transform;
            CircleButton("Close",panel.transform,new Vector2(1,1),new Vector2(1,1),new Vector2(-22,-22),LineIcon.Symbol.Close,CloseOverlay);
            if(mode=="map")BuildMap();else if(mode=="phone")BuildPhone();else if(mode=="journal")BuildJournal();
            else if(mode=="pause")BuildPause();else if(mode=="restart")BuildRestart();else if(mode=="ending")BuildEnding();
        }
        IEnumerator FadeOverlay(float target,float duration)
        {
            float start=overlayGroup.alpha;
            for(float elapsed=0;elapsed<duration;elapsed+=Time.unscaledDeltaTime)
            {
                float t=Mathf.Clamp01(elapsed/Mathf.Max(.01f,duration));float eased=t*t*(3-2*t);
                overlayGroup.alpha=Mathf.Lerp(start,target,eased);
                if(overlayContent!=null)overlayContent.localScale=Vector3.one*Mathf.Lerp(.992f,1f,overlayGroup.alpha);
                yield return null;
            }
            overlayGroup.alpha=target;
            if(overlayContent!=null)overlayContent.localScale=Vector3.one*Mathf.Lerp(.992f,1f,target);
        }
        public void CloseOverlay(){CloseOverlayThen(null);}
        public void CloseOverlayImmediately()
        {
            if(!initialized)return;
            if(overlayRoutine!=null)StopCoroutine(overlayRoutine);
            overlayRoutine=null;FinishCloseOverlay();
        }
        void CloseOverlayThen(Action continuation)
        {
            if(!initialized||overlayBusy)return;
            if(!HasOverlay){continuation?.Invoke();return;}
            overlayBusy=true;overlayGroup.interactable=false;
            overlayRoutine=StartCoroutine(CloseOverlayRoutine(continuation));
        }
        IEnumerator CloseOverlayRoutine(Action continuation)
        {
            yield return FadeOverlay(0,Style.FadeOutDuration);
            FinishCloseOverlay();
            continuation?.Invoke();
        }
        void FinishCloseOverlay()
        {
            overlayMode="";overlayBusy=false;overlayRoutine=null;
            overlayGroup.alpha=0;overlayGroup.blocksRaycasts=false;overlayGroup.interactable=false;
            overlayRoot.gameObject.SetActive(false);
            Time.timeScale=1f;
            session.SetOverlay(IsTransitioning);
            RefreshHUD();
        }
        void Header(string eyebrow,string title,string subtitle)
        {
            float width=((RectTransform)overlayContent).sizeDelta.x;
            Label("Eyebrow",overlayContent,TL,TL,new Vector2(46,-36),new Vector2(width-150,28),eyebrow,17,Accent);
            Label("Title",overlayContent,TL,TL,new Vector2(44,-78),new Vector2(width-150,60),title,Style.TitleSize,Paper);
            Label("Subtitle",overlayContent,TL,TL,new Vector2(46,-145),new Vector2(width-92,70),subtitle,20,Muted);
        }
        void BuildMap()
        {
            Header("TIDELINE  /  LOCAL MAP","在小镇，慢一点也没关系", "当前："+PlaceName(session.CurrentPlace)+"   ·   "+PeriodName(session.Clock.Period)+"   ·   选择地点前往，也可以从街道的出入口步行进入。" );
            var area=Rect("Map drawing",overlayContent,TL,TL,new Vector2(46,-235),new Vector2(1188,335));
            Panel("Sea",area,BL,BL,new Vector2(0,0),new Vector2(1188,127),new Color(.12f,.28f,.36f,.24f));
            for(int i=0;i<4;i++)Line(area,BL,BL,new Vector2(50+i*140,27+i*20),new Vector2(430,1),new Color(.45f,.68f,.74f,.23f));
            Label("Sea label",area,BL,BL,new Vector2(935,48),new Vector2(200,35),"海，向东展开",18,new Color(.52f,.73f,.81f,1));
            Line(area,BL,BL,new Vector2(165,199),new Vector2(850,2),new Color(.67f,.75f,.82f,.45f));
            MapNode(area,PlaceId.Cafe,180,"咖啡还热着","室内 · 阿遥的咖啡与旧相册");
            MapNode(area,PlaceId.Street,580,"沿着海走一走","街道 · 明信片、旧邮差与海风");
            MapNode(area,PlaceId.Lighthouse,1010,"等一盏灯亮起","海堤 · 观景台与蓝色信箱");
            Label("Map note",overlayContent,TL,TL,new Vector2(46,-611),new Vector2(1180,90),"长椅与咖啡可以让时间向前。\n有人会在傍晚换个地方；错过一段时间，也可以在第二天再来。",22,Muted);
            Label("Map shortcut",overlayContent,new Vector2(0,0),BL,new Vector2(46,27),new Vector2(1000,30),"M / Esc  收起地图",18,Muted);
        }
        void MapNode(Transform parent,PlaceId place,float x,string thought,string description)
        {
            var rt=Rect(place.ToString(),parent,BL,C,new Vector2(x,200),new Vector2(62,62));
            var icon=rt.gameObject.AddComponent<LineIcon>();icon.symbol=LineIcon.Symbol.Pin;icon.ring=true;icon.color=session.CurrentPlace==place?Accent:Paper;icon.raycastTarget=false;
            Label("Place",parent,BL,C,new Vector2(x,266),new Vector2(270,40),PlaceName(place),26,session.CurrentPlace==place?Accent:Paper,TextAlignmentOptions.Midline);
            Label("Thought",parent,BL,C,new Vector2(x,150),new Vector2(310,32),thought,19,Muted,TextAlignmentOptions.Midline);
            var button=Button("Travel",parent,BL,C,new Vector2(x,102),new Vector2(240,40),session.CurrentPlace==place?"在这里继续探索":"前往  "+PlaceName(place),()=>CloseOverlayThen(()=>{if(session.CurrentPlace!=place)session.Travel(place);}),session.CurrentPlace==place,18);
            button.GetComponentInChildren<TextMeshProUGUI>().alignment=TextAlignmentOptions.Midline;
        }
        void BuildPhone()
        {
            Header("PHONE  /  A LITTLE CONNECTION","海风里收到的消息","几句话，也可以把两个地方变得近一点。" );
            var scroll=MakeScroll(new Vector2(46,-225),new Vector2(1188,455));
            float y=0;
            foreach(string message in session.Story.PhoneMessages)
            {
                y=TextEntry(scroll,y,message,22,Style.CardFill);
            }
            if(y==0)y=TextEntry(scroll,0,"暂时没有新消息。\n先去街上走走吧，故事会从遇见的人那里开始。",24,Style.CardFill);
            SetContentHeight(scroll,y);
            Label("Phone hint",overlayContent,BL,BL,new Vector2(46,28),new Vector2(1100,35),"Tab / Esc  收起手机     ·     消息随今天的经历更新",18,Muted);
        }
        void BuildJournal()
        {
            Header("JOURNAL  /  THINGS TO REMEMBER","今天，值得留下的事",session.Story.Objective);
            var scroll=MakeScroll(new Vector2(46,-230),new Vector2(1188,454));
            float y=0;
            if(session.State.items.Count>0)y=TextEntry(scroll,y,"口袋里  /  "+string.Join("  ·  ",session.State.items),21,Style.CardFill);
            for(int i=session.State.journal.Count-1;i>=0;i--)y=TextEntry(scroll,y,session.State.journal[i],23,Style.CardFill);
            if(y==0)y=TextEntry(scroll,0,"新的一天。\n此刻还没有什么需要记下，但海边总有一些小事。",24,Style.CardFill);
            SetContentHeight(scroll,y);
            Label("Journal hint",overlayContent,BL,BL,new Vector2(46,28),new Vector2(880,35),"J / Esc  收起日志     ·     滚动阅读     ·     进度会自动保存",18,Muted);
            if(!string.IsNullOrEmpty(session.State.ending))Button("Recall ending",overlayContent,new Vector2(1,0),new Vector2(1,0),new Vector2(-40,25),new Vector2(230,42),"回看这一天",()=>OpenOverlay("ending"),true,20);
        }
        RectTransform MakeScroll(Vector2 position,Vector2 size)
        {
            var view=Rect("Scroll view",overlayContent,TL,TL,position,size);
            var image=view.gameObject.AddComponent<Image>();image.color=new Color(.02f,.05f,.1f,.01f);image.raycastTarget=true;
            var mask=view.gameObject.AddComponent<RectMask2D>();
            var scroll=view.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;scroll.inertia=true;scroll.viewport=view;
            var content=Rect("Content",view,TL,TL,Vector2.zero,new Vector2(size.x-12,size.y));scroll.content=content;
            return content;
        }
        float TextEntry(RectTransform content,float y,string value,float size,Color background)
        {
            float width=content.sizeDelta.x;
            var text=Label("Entry",content,TL,TL,new Vector2(24,-y-21),new Vector2(width-48,200),value,size,Paper);
            float height=Mathf.Max(78,text.GetPreferredValues(value,width-48,0).y+47);
            text.rectTransform.sizeDelta=new Vector2(width-48,height-36);
            var bg=Panel("Entry background",content,TL,TL,new Vector2(0,-y),new Vector2(width,height),background);bg.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
            return y+height+14;
        }
        void SetContentHeight(RectTransform content,float height)=>content.sizeDelta=new Vector2(content.sizeDelta.x,Mathf.Max(height,455));
        void BuildPause()
        {
            Header("PAUSE  /  TAKE YOUR TIME","让这一刻，停一停","所有选择和相遇，都会被记住。" );
            Button("Resume",overlayContent,TL,TL,new Vector2(46,-230),new Vector2(668,64),"继续散步",CloseOverlay,true,25);
            Button("Save",overlayContent,TL,TL,new Vector2(46,-308),new Vector2(668,56),"保存此刻",()=>{if(session.Save())ShowToast("已保存。下次会从这里继续。");},false,23);
            Button("Restart",overlayContent,TL,TL,new Vector2(46,-378),new Vector2(668,56),"重新开始这一天",()=>OpenOverlay("restart"),false,23);
            Label("Volume",overlayContent,TL,TL,new Vector2(46,-477),new Vector2(260,35),"环境声音",22,Muted);
            var volumeLabel=Label("Volume value",overlayContent,TL,TL,new Vector2(610,-477),new Vector2(100,35),Mathf.RoundToInt(AudioListener.volume*100)+"%",22,Paper,TextAlignmentOptions.TopRight);
            var sliderRect=Rect("Volume slider",overlayContent,TL,TL,new Vector2(48,-524),new Vector2(660,38));
            var hit=sliderRect.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var track=Panel("Track",sliderRect,new Vector2(.5f,.5f),C,Vector2.zero,new Vector2(660,3),new Color(.48f,.67f,.76f,.40f));
            var slider=sliderRect.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;slider.value=AudioListener.volume;
            var handle=Panel("Handle",sliderRect,new Vector2(0,.5f),C,Vector2.zero,new Vector2(18,18),Cool,true);
            ((RoundedPanel)handle).CornerRadius=9;
            slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.direction=Slider.Direction.LeftToRight;slider.navigation=new Navigation{mode=Navigation.Mode.None};
            slider.onValueChanged.AddListener(value=>{AudioListener.volume=value;volumeLabel.text=Mathf.RoundToInt(value*100)+"%";});
            Label("Pause help",overlayContent,BL,BL,new Vector2(46,51),new Vector2(670,56),"Esc  返回游戏\n移动 A / D  ·  跳跃 空格  ·  交互 E",18,Muted);
        }
        void BuildRestart()
        {
            Header("NEW DAY  /  START AGAIN","把今天，重新翻开","这会替换当前存档。已经做出的选择和今天的日志会清空。" );
            Label("Restart details",overlayContent,TL,TL,new Vector2(46,-260),new Vector2(660,110),"如果只是想再逛一会儿，可以返回游戏。\n如果想听听另一种回答，就从海边的早晨开始。",25,Paper);
            Button("Cancel restart",overlayContent,TL,TL,new Vector2(46,-430),new Vector2(668,64),"保留今天，继续散步",()=>OpenOverlay("pause"),true,24);
            Button("Confirm restart",overlayContent,TL,TL,new Vector2(46,-515),new Vector2(668,64),"确认重新开始",()=>CloseOverlayThen(()=>{observedEnding="";pendingEnding="";hidden=false;session.NewGame();RefreshHUD();}),false,24);
        }
        string ResolveEndingMemory()
        {
            // Read the completed ending's authored effect, not whichever observation was added last.
            if(session.Database!=null && session.Database.dialogues!=null)
            {
                foreach(var node in session.Database.dialogues)
                {
                    if(node==null)continue;
                    string entry=FindEndingEntry(node.effects);
                    if(!string.IsNullOrEmpty(entry))return entry;
                    if(node.choices==null)continue;
                    foreach(var choice in node.choices)
                    {
                        if(choice==null)continue;
                        entry=FindEndingEntry(choice.effects);
                        if(!string.IsNullOrEmpty(entry))return entry;
                    }
                }
            }
            return "海风还会回来。今天遇见的人，也会记得你的回答。";
        }
        string FindEndingEntry(StoryEffect[] effects)
        {
            if(effects==null || string.IsNullOrEmpty(session.State.ending))return null;
            foreach(var effect in effects)
            {
                if(effect==null || effect.ending!=session.State.ending || string.IsNullOrEmpty(effect.journal))continue;
                // Keep the saved day and period prefix when this memory exists in the journal.
                foreach(string entry in session.State.journal)
                    if(entry!=null && entry.EndsWith(effect.journal,StringComparison.Ordinal))return entry;
                return effect.journal;
            }
            return null;
        }
        void BuildEnding()
        {
            Header("A DAY BY THE SEA  /  FIN","海风捎来的信",session.State.ending??"今天的故事，暂时停在这里。");
            Line(overlayContent,TL,TL,new Vector2(46,-215),new Vector2(1068,1),new Color(.76f,.8f,.86f,.3f));
            string note=ResolveEndingMemory();
            Label("Ending memory",overlayContent,TL,TL,new Vector2(66,-260),new Vector2(1028,230),note,27,Paper);
            Label("Ending reflection",overlayContent,TL,TL,new Vector2(66,-508),new Vector2(1030,80),"有些故事没有很大的结尾。\n只是天色暗下来时，想起谁，就知道该往哪里走。",24,Muted);
            Button("Continue after ending",overlayContent,BL,BL,new Vector2(46,75),new Vector2(510,64),"再沿着海走一会儿",CloseOverlay,true,24);
            Button("Read journal",overlayContent,BL,BL,new Vector2(578,75),new Vector2(536,64),"翻开今天的日志",()=>OpenOverlay("journal"),false,24);
            Label("Completion status",overlayContent,BL,BL,new Vector2(46,24),new Vector2(1000,31),"这一日的故事已完成  ·  已自动保存  ·  随时可以继续探索",18,Muted);
        }
    }
}
