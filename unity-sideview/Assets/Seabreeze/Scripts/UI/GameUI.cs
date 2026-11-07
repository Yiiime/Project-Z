using System;
using System.Collections.Generic;
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
        bool IsTransitioning => WorldDirector.Instance != null && WorldDirector.Instance.IsTransitioning;
        Canvas canvas;
        NativeLiveHost nativeLive;
        RectTransform canvasRect, hud, bottomStrip, overlayRoot, dialogueRoot, promptRoot;
        Transform overlayContent;
        IslandMapView mapView;
        TextMeshProUGUI dateText, timeText, weatherText, placeText, ambienceText, objectiveText;
        TextMeshProUGUI speakerText, dialogueText, nextText, promptText, toastText;
        RectTransform choiceRoot;
        Button nextButton;
        CanvasGroup toastGroup;
        GameSession session;
        bool initialized, hidden, interactionVisible;
        string overlayMode = "", observedEnding = "", pendingEnding = "";
        Vector3 interactionPosition;
        Vector2 mapCanvasSize;
        float visibleCharacters, toastUntil;
        int dialogueFrame = -1;
        readonly List<Button> choiceButtons = new List<Button>();
        static readonly Vector2 TL = new Vector2(0,1), TC = new Vector2(.5f,1), C = new Vector2(.5f,.5f), BL = Vector2.zero;

        // Exposed for runtime verification and accessibility tooling; the map remains
        // an ordinary overlay owned by this UI, with no travel/navigation side effects.
        public IslandMapView CurrentMapView => mapView;

        public void Initialize()
        {
            if(initialized)return;
            session = GameSession.Instance;
            if(session == null) { Debug.LogError("GameUI requires GameSession.Initialize first."); return; }
            initialized = true; Instance = this;
            UIPrimitives.Font = Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            if(UIPrimitives.Font == null) UIPrimitives.Font = TMP_Settings.defaultFontAsset;
            var cg = new GameObject("Seabreeze Interface",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            cg.transform.SetParent(transform,false);
            canvas = cg.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=100;
            canvasRect=(RectTransform)cg.transform;
            var scaler=cg.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            if(EventSystem.current==null)
            { var es=new GameObject("UI Event System",typeof(EventSystem),typeof(InputSystemUIInputModule)); es.transform.SetParent(transform,false); }
            BuildHUD(); BuildDialogue(); BuildInteraction(); BuildToast();
            overlayRoot=Stretch("Overlay",canvasRect,Vector2.zero,Vector2.zero);overlayRoot.gameObject.SetActive(false);
            session.Changed+=OnStateChanged;
            session.Dialogue.Changed+=OnDialogueChanged;
            session.Dialogue.Closed+=OnDialogueClosed;
            session.Story.Toast+=ShowToast;
            observedEnding=session.State.ending ?? "";
            RefreshHUD(); OnDialogueChanged();
        }
        void OnDestroy()
        {
            if(nativeLive!=null)nativeLive.Close();
            if(Instance==this){Instance=null;Time.timeScale=1f;}
            if(session!=null)
            {
                session.Changed-=OnStateChanged;
                if(session.Dialogue!=null){session.Dialogue.Changed-=OnDialogueChanged;session.Dialogue.Closed-=OnDialogueClosed;}
                if(session.Story!=null)session.Story.Toast-=ShowToast;
            }
        }
        void BuildHUD()
        {
            hud=Stretch("Exploration HUD",canvasRect,Vector2.zero,Vector2.zero);
            Panel("Environment information glass",hud,TL,TL,new Vector2(0,-97),new Vector2(460,132),new Color(.025f,.065f,.14f,.56f));
            Panel("Date backing",hud,TL,TL,new Vector2(0,-52),new Vector2(310,45),new Color(.035f,.085f,.18f,.55f));
            dateText=Label("Day",hud,TL,TL,new Vector2(60,-57),new Vector2(270,40),"第 1 天",26,Paper);
            timeText=Label("Time period",hud,TL,TL,new Vector2(60,-104),new Vector2(195,65),"上午",46,Paper);
            var weather=Rect("Weather icon",hud,TL,TL,new Vector2(250,-124),new Vector2(25,25));var wi=weather.gameObject.AddComponent<LineIcon>();wi.symbol=LineIcon.Symbol.Sun;wi.color=Paper;wi.raycastTarget=false;
            weatherText=Label("Weather",hud,TL,TL,new Vector2(286,-122),new Vector2(350,40),"晴",22,Muted);
            var pin=Rect("Place pin",hud,TL,TL,new Vector2(59,-181),new Vector2(22,25));var pi=pin.gameObject.AddComponent<LineIcon>();pi.symbol=LineIcon.Symbol.Pin;pi.color=Paper;pi.raycastTarget=false;
            placeText=Label("Place",hud,TL,TL,new Vector2(92,-181),new Vector2(350,40),"海边小街",24,Paper);
            RightShortcut("地图", "M", LineIcon.Symbol.Map, 105, ()=>ToggleOverlay("map"));
            RightShortcut("手机", "Tab", LineIcon.Symbol.Phone, -9, ()=>ToggleOverlay("phone"));
            RightShortcut("日志", "J", LineIcon.Symbol.Journal, -123, ()=>ToggleOverlay("journal"));
            bottomStrip=Rect("Exploration footer",hud,BL,BL,Vector2.zero,new Vector2(1920,140));
            Panel("Bottom atmosphere",bottomStrip,BL,BL,Vector2.zero,new Vector2(5000,140),new Color(.025f,.065f,.13f,.65f));
            Line(bottomStrip,BL,BL,new Vector2(60,133),new Vector2(1800,1),new Color(.65f,.77f,.86f,.13f));
            ambienceText=Label("Ambience",bottomStrip,BL,BL,new Vector2(60,83),new Vector2(1400,32),"海风，和远处的浪。",21,Muted,TextAlignmentOptions.BottomLeft);
            Label("Controls",bottomStrip,BL,BL,new Vector2(60,35),new Vector2(1450,32),"A / D  移动     空格  跳跃     E  交互     M  地图     J  日志     Esc  暂停",20,Paper,TextAlignmentOptions.BottomLeft);
            objectiveText=Label("Current thought",hud,new Vector2(1,1),new Vector2(1,1),new Vector2(-60,-56),new Vector2(650,90),"",21,Muted,TextAlignmentOptions.TopRight);
            var sound=CircleButton("Sound",hud,new Vector2(1,0),new Vector2(1,0),new Vector2(-54,30),LineIcon.Symbol.Sound,()=>{AudioListener.volume=AudioListener.volume>.01f?0:1;ShowToast(AudioListener.volume<.01f?"声音已关闭":"海风回来了");});
        }
        void RightShortcut(string label,string key,LineIcon.Symbol symbol,float y,Action action)
        {
            var button=CircleButton(label,hud,new Vector2(1,.5f),C,new Vector2(-77,y),symbol,action);
            Label("Key",button.transform,new Vector2(.5f,0),TC,new Vector2(0,-9),new Vector2(92,28),key,17,Muted,TextAlignmentOptions.Top);
        }
        Button CircleButton(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,LineIcon.Symbol symbol,Action action)
        {
            var p=Panel(name,parent,anchor,pivot,pos,new Vector2(66,66),new Color(.05f,.10f,.19f,.68f),true);
            p.sprite = UIPrimitives.CircleSprite;
            var b=p.gameObject.AddComponent<Button>();b.targetGraphic=p;b.navigation=new Navigation{mode=Navigation.Mode.None};b.onClick.AddListener(()=>{if(!IsTransitioning)action();});
            var icon=Stretch("Icon",p.transform,Vector2.zero,Vector2.zero).gameObject.AddComponent<LineIcon>();icon.symbol=symbol;icon.ring=true;icon.color=new Color(.87f,.91f,.97f,.9f);icon.raycastTarget=false;
            return b;
        }
        void BuildDialogue()
        {
            dialogueRoot=Rect("Conversation",canvasRect,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,24),new Vector2(1120,220));
            Panel("Conversation glass",dialogueRoot,Vector2.zero,Vector2.zero,Vector2.zero,new Vector2(1120,220),new Color(.035f,.075f,.15f,.92f),true);
            Line(dialogueRoot,TL,TL,Vector2.zero,new Vector2(1120,2),new Color(.74f,.82f,.91f,.5f));
            speakerText=Label("Speaker",dialogueRoot,TL,TL,new Vector2(32,-18),new Vector2(740,34),"",23,Accent);
            dialogueText=Label("Dialogue",dialogueRoot,TL,TL,new Vector2(32,-64),new Vector2(1056,120),"",25,Paper);
            dialogueText.lineSpacing=8;
            choiceRoot=Rect("Choices",dialogueRoot,TL,TL,new Vector2(32,-172),new Vector2(1056,144));
            nextButton=Button("Continue",dialogueRoot,new Vector2(1,1),new Vector2(1,1),new Vector2(-24,-14),new Vector2(228,38),"E / 空格  继续",ContinueDialogue,false,18);
            nextText=nextButton.GetComponentInChildren<TextMeshProUGUI>();
            dialogueRoot.gameObject.SetActive(false);
        }
        void BuildInteraction()
        {
            promptRoot=Rect("Interaction prompt",canvasRect,C,C,Vector2.zero,new Vector2(250,50));
            Panel("Prompt glass",promptRoot,C,C,Vector2.zero,new Vector2(250,50),new Color(.045f,.08f,.15f,.88f));
            Line(promptRoot,BL,BL,Vector2.zero,new Vector2(250,1),new Color(.85f,.9f,1,.4f));
            promptText=Label("Prompt",promptRoot,C,C,Vector2.zero,new Vector2(235,44),"E  交谈",22,Paper,TextAlignmentOptions.Midline);
            promptRoot.gameObject.SetActive(false);
        }
        void BuildToast()
        {
            var rt=Rect("Quiet notification",canvasRect,TC,TC,new Vector2(0,-51),new Vector2(670,54));
            var p=Panel("Toast glass",rt,C,C,Vector2.zero,new Vector2(670,54),new Color(.04f,.09f,.16f,.88f));
            toastText=Label("Toast text",rt,C,C,Vector2.zero,new Vector2(630,46),"",21,Paper,TextAlignmentOptions.Midline);
            toastGroup=rt.gameObject.AddComponent<CanvasGroup>();toastGroup.alpha=0;toastGroup.blocksRaycasts=false;
        }
        void OnStateChanged()
        {
            RefreshHUD();
            string ending=session.State.ending??"";
            if(ending!=observedEnding){observedEnding=ending;if(ending.Length>0)pendingEnding=ending;}
        }
        void RefreshHUD()
        {
            if(!initialized)return;
            dateText.text="第  "+session.Clock.Day+"  天";
            dateText.fontSize=26;
            timeText.text=PeriodName(session.Clock.Period);
            string clock=session.Clock.TimeLabel;
            int clockStart=clock.IndexOfAny("0123456789".ToCharArray());
            if(clockStart>=0)clock=clock.Substring(clockStart);
            string weather=session.Clock.WeatherLabel.Split('·')[0].Trim();
            weatherText.text=clock+"  ·  "+weather;
            placeText.text=PlaceName(session.CurrentPlace);
            objectiveText.text=session.Story.Objective;
            ambienceText.text=session.CurrentPlace==PlaceId.Cafe?"杯碟轻响。烘焙的香气，藏在海风够不到的地方。":session.CurrentPlace==PlaceId.Lighthouse?"灯塔静静转身。海浪替那些没说完的话，停顿了一下。":"海风，和远处的浪。街角有人把今天的第一杯咖啡放在窗边。";
        }
        public static string PlaceName(PlaceId id)=>id==PlaceId.Cafe?"潮汐咖啡馆":id==PlaceId.Lighthouse?"灯塔海堤":"海边小街";
        public static string PeriodName(TimePeriod period)=>period==TimePeriod.Morning?"上午":period==TimePeriod.Afternoon?"午后":period==TimePeriod.Evening?"傍晚":"夜晚";
        void OnDialogueChanged()
        {
            if(!initialized)return;
            bool open=session.Dialogue.IsOpen;
            dialogueRoot.gameObject.SetActive(open&&!HasOverlay);
            bottomStrip.gameObject.SetActive(!open);
            if(!open)return;
            dialogueFrame=Time.frameCount;
            speakerText.text=session.Dialogue.Speaker;
            dialogueText.text=session.Dialogue.Text;
            dialogueText.maxVisibleCharacters=0;visibleCharacters=0;
            dialogueText.ForceMeshUpdate(true);
            Clear(choiceRoot);choiceButtons.Clear();
            int count=session.Dialogue.Choices.Count;
            // Fit the spoken line near the screen edge. Only longer passages and choices grow upward.
            float textHeight=Mathf.Max(62,dialogueText.GetPreferredValues(session.Dialogue.Text,1056,0).y+8);
            dialogueText.rectTransform.sizeDelta=new Vector2(1056,textHeight);
            float choicesTop=64+textHeight+12;
            choiceRoot.anchoredPosition=new Vector2(32,-choicesTop);
            float height=count>0?choicesTop+count*48+18:Mathf.Max(182,64+textHeight+24);
            dialogueRoot.sizeDelta=new Vector2(1120,height);
            ((RectTransform)dialogueRoot.GetChild(0)).sizeDelta=new Vector2(1120,height);
            for(int i=0;i<count;i++)
            {
                int index=i;
                var button=Button("Choice "+(i+1),choiceRoot,TL,TL,new Vector2(0,-i*48),new Vector2(1056,43),(i+1)+"  ·  "+session.Dialogue.Choices[i].text,()=>Choose(index),false,22);
                button.gameObject.SetActive(false);choiceButtons.Add(button);
            }
            nextButton.gameObject.SetActive(count==0);
            nextText.text="E / 空格  显示全文";
            HideInteraction();
        }
        void OnDialogueClosed()
        {
            if(dialogueRoot!=null)dialogueRoot.gameObject.SetActive(false);
            if(bottomStrip!=null)bottomStrip.gameObject.SetActive(true);
        }
        bool FullyRevealed=>dialogueText.maxVisibleCharacters>=dialogueText.textInfo.characterCount;
        void RevealAll()
        { dialogueText.maxVisibleCharacters=int.MaxValue;visibleCharacters=int.MaxValue;foreach(var b in choiceButtons)b.gameObject.SetActive(true);nextText.text="E / 空格  继续"; }
        void ContinueDialogue()
        {
            if(!session.Dialogue.IsOpen||HasOverlay||IsTransitioning||Time.frameCount<=dialogueFrame)return;
            if(!FullyRevealed){RevealAll();return;}
            if(session.Dialogue.Choices.Count==0)session.Dialogue.Advance();
        }
        void Choose(int index)
        {if(session.Dialogue.IsOpen&&!HasOverlay&&!IsTransitioning&&Time.frameCount>dialogueFrame&&FullyRevealed)session.Dialogue.Choose(index);}
        public void ShowInteraction(string text,Vector3 worldPosition)
        {
            if(!initialized)return;
            interactionVisible=true;interactionPosition=worldPosition;
            string caption=text.StartsWith("E")?text:"E  "+text;
            if(promptText.text!=caption)
            {
                promptText.text=caption;
                float width=Mathf.Clamp(promptText.GetPreferredValues(caption).x+38,250,510);
                promptRoot.sizeDelta=new Vector2(width,50);
                ((RectTransform)promptRoot.GetChild(0)).sizeDelta=new Vector2(width,50);
                ((RectTransform)promptRoot.GetChild(1)).sizeDelta=new Vector2(width,1);
                promptText.rectTransform.sizeDelta=new Vector2(width-24,44);
            }
        }
        public void HideInteraction(){interactionVisible=false;if(promptRoot!=null)promptRoot.gameObject.SetActive(false);}
        public void ShowToast(string text)
        {if(!initialized)return;toastText.text=text;toastUntil=Time.unscaledTime+3.8f;toastGroup.alpha=1;toastGroup.transform.SetAsLastSibling();}

        void Update()
        {
            if(!initialized||session==null)return;
            if(session.Memory!=null&&session.Memory.PlayerChatOpen){HideInteraction();return;}
            if(overlayMode=="live")
            {
                if(Application.isFocused&&GameInput.Pressed(Key.Escape)&&!(nativeLive?.HandleBack()??false))CloseOverlay();
                return;
            }
            if(IsTransitioning)
            {
                HideInteraction();
                if(toastGroup!=null)toastGroup.alpha=Mathf.MoveTowards(toastGroup.alpha,Time.unscaledTime<toastUntil?1:0,Time.unscaledDeltaTime*2);
                return;
            }
            if(session.Dialogue.IsOpen&&!HasOverlay)
            {
                if(dialogueText.maxVisibleCharacters!=int.MaxValue)
                {
                    visibleCharacters+=Time.unscaledDeltaTime*43;
                    dialogueText.maxVisibleCharacters=Mathf.FloorToInt(visibleCharacters);
                    if(FullyRevealed)RevealAll();
                }
                if(Time.frameCount>dialogueFrame)
                {
                    if(GameInput.Pressed(Key.E)||GameInput.Pressed(Key.Space)||GameInput.Pressed(Key.Enter))ContinueDialogue();
                    if(GameInput.Pressed(Key.Digit1)&&choiceButtons.Count>0)Choose(0);
                    if(GameInput.Pressed(Key.Digit2)&&choiceButtons.Count>1)Choose(1);
                    if(GameInput.Pressed(Key.Digit3)&&choiceButtons.Count>2)Choose(2);
                }
            }
            if(GameInput.Pressed(Key.Escape)) {if(HasOverlay)CloseOverlay();else OpenOverlay("pause");}
            else if(!session.Dialogue.IsOpen)
            {
                if(GameInput.Pressed(Key.M))ToggleOverlay("map");
                if(GameInput.Pressed(Key.Tab))ToggleOverlay("phone");
                if(GameInput.Pressed(Key.J))ToggleOverlay("journal");
                if(GameInput.Pressed(Key.H)){hidden=!hidden;hud.gameObject.SetActive(!hidden);}
            }
            if(!string.IsNullOrEmpty(pendingEnding)&&!session.Dialogue.IsOpen&&!HasOverlay)
            {pendingEnding="";OpenOverlay("ending");}
            if(toastGroup!=null)toastGroup.alpha=Mathf.MoveTowards(toastGroup.alpha,Time.unscaledTime<toastUntil?1:0,Time.unscaledDeltaTime*2);
        }
        void LateUpdate()
        {
            if(!initialized)return;
            ResizeMapOverlayIfNeeded();
            bool visible=interactionVisible&&!HasOverlay&&!session.Dialogue.IsOpen&&!hidden&&!IsTransitioning;
            promptRoot.gameObject.SetActive(visible);
            if(!visible||Camera.main==null)return;
            Vector3 screen=Camera.main.WorldToScreenPoint(interactionPosition);
            if(screen.z<0){promptRoot.gameObject.SetActive(false);return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out Vector2 local);
            float halfWidth=canvasRect.rect.width*.5f,halfHeight=canvasRect.rect.height*.5f;
            promptRoot.anchoredPosition=new Vector2(Mathf.Clamp(local.x,-halfWidth+promptRoot.sizeDelta.x*.5f+12,halfWidth-promptRoot.sizeDelta.x*.5f-12),Mathf.Clamp(local.y,-halfHeight+190,halfHeight-90));
        }
        void ToggleOverlay(string mode)
        {if(session.Dialogue.IsOpen&&mode!="pause")return;if(overlayMode==mode)CloseOverlay();else OpenOverlay(mode);}
        void OpenOverlay(string mode)
        {
            if(!initialized||IsTransitioning)return;
            overlayMode=mode;Time.timeScale=0f;session.SetOverlay(true);HideInteraction();dialogueRoot.gameObject.SetActive(false);
            mapView=null;
            Clear(overlayRoot);overlayRoot.gameObject.SetActive(true);
            var scrim=Stretch("Backdrop",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();scrim.color=new Color(.015f,.035f,.08f,.76f);scrim.raycastTarget=true;
            if(mode=="live"){canvas.enabled=false;nativeLive=NativeLiveHost.Open(transform,CloseOverlay);return;}
            Vector2 size;
            if(mode=="map")size=MapPanelSize();
            else
                size=mode=="pause"||mode=="restart"?new Vector2(760,730):mode=="ending"?new Vector2(1160,790):new Vector2(1280,790);
            var panel=Panel("Panel",overlayRoot,C,C,Vector2.zero,size,Ink,true);
            overlayContent=panel.transform;
            Line(panel.transform,TL,TL,Vector2.zero,new Vector2(size.x,2),new Color(.66f,.76f,.88f,.55f));
            CircleButton("Close",panel.transform,new Vector2(1,1),new Vector2(1,1),new Vector2(-24,-24),LineIcon.Symbol.Close,CloseOverlay);
            if(mode=="map")BuildMap();else if(mode=="phone")BuildPhone();else if(mode=="journal")BuildJournal();else if(mode=="pause")BuildPause();else if(mode=="restart")BuildRestart();else if(mode=="ending")BuildEnding();
        }
        public void CloseOverlay()
        {
            if(!initialized)return;
            if(nativeLive!=null){nativeLive.Close();nativeLive=null;}
            canvas.enabled=true;
            mapView=null;
            overlayMode="";Time.timeScale=1f;overlayRoot.gameObject.SetActive(false);session.SetOverlay(false);
            dialogueRoot.gameObject.SetActive(session.Dialogue.IsOpen);dialogueFrame=Time.frameCount;
            RefreshHUD();
        }
        void Header(string eyebrow,string title,string subtitle)
        {
            float width=((RectTransform)overlayContent).sizeDelta.x;
            Label("Eyebrow",overlayContent,TL,TL,new Vector2(46,-36),new Vector2(width-150,28),eyebrow,17,Accent);
            Label("Title",overlayContent,TL,TL,new Vector2(44,-78),new Vector2(width-150,60),title,38,Paper);
            Label("Subtitle",overlayContent,TL,TL,new Vector2(46,-145),new Vector2(width-92,70),subtitle,21,Muted);
        }
        void BuildMap()
        {
            var panelRect=(RectTransform)overlayContent;
            float width=panelRect.sizeDelta.x;
            float height=panelRect.sizeDelta.y;
            Label("Map eyebrow",overlayContent,TL,TL,new Vector2(42,-26),new Vector2(width-84,28),"ISLAND MAP  /  位置图",17,Accent);
            Label("Map title",overlayContent,TL,TL,new Vector2(40,-62),new Vector2(width-80,58),"岛屿地图",38,Paper);
            Label("Map subtitle",overlayContent,TL,TL,new Vector2(42,-119),new Vector2(width-84,36),"把光标移到地点，查看简短介绍。",19,Muted);

            float viewportWidth=Mathf.Max(1,width-72);
            float viewportHeight=Mathf.Max(1,height-238);
            var viewport=Rect("Island map viewport",overlayContent,C,C,new Vector2(0,-45),new Vector2(viewportWidth,viewportHeight));
            mapView=viewport.gameObject.AddComponent<IslandMapView>();
            mapView.Initialize(viewport,panelRect);
            mapCanvasSize=canvasRect.rect.size;

            Label("Map shortcut",overlayContent,BL,BL,new Vector2(42,22),new Vector2(width-84,30),"M / Esc  收起地图     ·     移动光标查看地点",18,Muted,TextAlignmentOptions.BottomLeft);
        }

        Vector2 MapPanelSize()
        {
            Vector2 available=canvasRect.rect.size-new Vector2(32,32);
            return new Vector2(Mathf.Min(1660,Mathf.Max(1,available.x)),Mathf.Min(1020,Mathf.Max(1,available.y)));
        }

        void ResizeMapOverlayIfNeeded()
        {
            if(overlayMode!="map"||overlayContent==null||canvasRect==null)return;
            Vector2 currentCanvasSize=canvasRect.rect.size;
            if((currentCanvasSize-mapCanvasSize).sqrMagnitude<.25f)return;
            mapCanvasSize=currentCanvasSize;
            var panel=(RectTransform)overlayContent;
            Vector2 size=MapPanelSize();
            panel.sizeDelta=size;
            var hairline=panel.Find("Hairline") as RectTransform;
            if(hairline!=null)hairline.sizeDelta=new Vector2(size.x,2);
            SetMapChildWidth(panel,"Map eyebrow",size.x-84,28);
            SetMapChildWidth(panel,"Map title",size.x-80,58);
            SetMapChildWidth(panel,"Map subtitle",size.x-84,36);
            SetMapChildWidth(panel,"Map shortcut",size.x-84,30);
            var viewport=panel.Find("Island map viewport") as RectTransform;
            if(viewport!=null)viewport.sizeDelta=new Vector2(Mathf.Max(1,size.x-72),Mathf.Max(1,size.y-238));
        }

        static void SetMapChildWidth(Transform panel,string childName,float width,float height)
        {
            var child=panel.Find(childName) as RectTransform;
            if(child!=null)child.sizeDelta=new Vector2(width,height);
        }

        void BuildPhone()
        {
            Header("PHONE  /  A LITTLE CONNECTION","海风里收到的消息","几句话，也可以把两个地方变得近一点。" );
            Button("Open Z Live",overlayContent,TL,TL,new Vector2(46,-220),new Vector2(1188,60),"Z 的直播 · 进入聊天小屋",()=>OpenOverlay("live"),false,24);
            var scroll=MakeScroll(new Vector2(46,-300),new Vector2(1188,380));
            float y=0;
            foreach(string message in session.Story.PhoneMessages)
            {
                y=TextEntry(scroll,y,message,22,new Color(.12f,.19f,.29f,.95f));
            }
            if(y==0)y=TextEntry(scroll,0,"暂时没有新消息。\n先去街上走走吧，故事会从遇见的人那里开始。",24,new Color(.12f,.19f,.29f,.95f));
            SetContentHeight(scroll,y);
            Label("Phone hint",overlayContent,BL,BL,new Vector2(46,28),new Vector2(1100,35),"Tab / Esc  收起手机     ·     消息随今天的经历更新",18,Muted);
        }
        void BuildJournal()
        {
            Header("JOURNAL  /  THINGS TO REMEMBER","今天，值得留下的事",session.Story.Objective);
            var scroll=MakeScroll(new Vector2(46,-230),new Vector2(1188,454));
            float y=0;
            if(session.State.items.Count>0)y=TextEntry(scroll,y,"口袋里  /  "+string.Join("  ·  ",session.State.items),21,new Color(.17f,.20f,.26f,.85f));
            for(int i=session.State.journal.Count-1;i>=0;i--)y=TextEntry(scroll,y,session.State.journal[i],23,new Color(.10f,.17f,.25f,.90f));
            if(y==0)y=TextEntry(scroll,0,"新的一天。\n此刻还没有什么需要记下，但海边总有一些小事。",24,new Color(.1f,.17f,.25f,.9f));
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
            var track=Panel("Volume slider",overlayContent,TL,TL,new Vector2(48,-540),new Vector2(660,7),new Color(.24f,.32f,.42f,1),true);
            var slider=track.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;slider.value=AudioListener.volume;
            var handle=Panel("Handle",track.transform,new Vector2(0,.5f),C,Vector2.zero,new Vector2(18,24),Accent,true);
            slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.direction=Slider.Direction.LeftToRight;slider.navigation=new Navigation{mode=Navigation.Mode.None};
            slider.onValueChanged.AddListener(value=>{AudioListener.volume=value;volumeLabel.text=Mathf.RoundToInt(value*100)+"%";});
            Label("Pause help",overlayContent,BL,BL,new Vector2(46,51),new Vector2(670,56),"Esc  返回游戏\n移动 A / D  ·  跳跃 空格  ·  交互 E",18,Muted);
        }
        void BuildRestart()
        {
            Header("NEW DAY  /  START AGAIN","把今天，重新翻开","这会替换当前存档。已经做出的选择和今天的日志会清空。" );
            Label("Restart details",overlayContent,TL,TL,new Vector2(46,-260),new Vector2(660,110),"如果只是想再逛一会儿，可以返回游戏。\n如果想听听另一种回答，就从海边的早晨开始。",25,Paper);
            Button("Cancel restart",overlayContent,TL,TL,new Vector2(46,-430),new Vector2(668,64),"保留今天，继续散步",()=>OpenOverlay("pause"),true,24);
            Button("Confirm restart",overlayContent,TL,TL,new Vector2(46,-515),new Vector2(668,64),"确认重新开始",()=>{CloseOverlay();observedEnding="";pendingEnding="";session.NewGame();RefreshHUD();OnDialogueChanged();},false,24);
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
