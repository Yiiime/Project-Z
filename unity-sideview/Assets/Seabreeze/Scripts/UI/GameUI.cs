using System;
using System.Collections;
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
    public sealed partial class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }
        public bool HasOverlay => !string.IsNullOrEmpty(overlayMode);
        bool IsTransitioning => WorldDirector.Instance != null && WorldDirector.Instance.IsTransitioning;
        Canvas canvas;
        NativeLiveHost nativeLive;
        RectTransform canvasRect, hud, bottomStrip, overlayRoot, dialogueRoot;
        Transform overlayContent;
        TextMeshProUGUI dateText, timeText, weatherText, placeText, ambienceText, objectiveText;
        TextMeshProUGUI speakerText, dialogueText, nextText, toastText;
        RectTransform choiceRoot;
        Button nextButton;
        CanvasGroup toastGroup, overlayGroup;
        InteractionPromptUI interactionPrompt;
        Coroutine overlayRoutine;
        bool overlayBusy;
        public bool IsOverlayTransitioning => overlayBusy;
        GameSession session;
        bool initialized, hidden;
        string overlayMode = "", observedEnding = "", pendingEnding = "";
        float visibleCharacters, toastUntil;
        int dialogueFrame = -1;
        readonly List<Button> choiceButtons = new List<Button>();
        static readonly Vector2 TL = new Vector2(0,1), TC = new Vector2(.5f,1), C = new Vector2(.5f,.5f), BL = Vector2.zero;

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
            overlayRoot=Stretch("Overlay",canvasRect,Vector2.zero,Vector2.zero);overlayGroup=overlayRoot.gameObject.AddComponent<CanvasGroup>();overlayRoot.gameObject.SetActive(false);
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
            dateText=Label("Day",hud,TL,TL,new Vector2(44,-52),new Vector2(270,36),"",26,Paper);
            timeText=Label("Time period",hud,TL,TL,new Vector2(44,-96),new Vector2(98,60),"",44,Paper);
            FinalKitAssets.Icon("sunset",hud,TL,TL,new Vector2(152,-116),new Vector2(24,24));
            weatherText=Label("Weather",hud,TL,TL,new Vector2(188,-113),new Vector2(150,32),"",18,Paper);
            FinalKitAssets.Icon("pin",hud,TL,TL,new Vector2(44,-169),new Vector2(18,20));
            placeText=Label("Place",hud,TL,TL,new Vector2(72,-165),new Vector2(420,32),"",20,Paper);
            bottomStrip=Stretch("Exploration footer",hud,Vector2.zero,Vector2.zero);
            FinalKitAssets.Icon("waveform",bottomStrip,BL,BL,new Vector2(44,28),new Vector2(20,24));
            ambienceText=Label("Ambience",bottomStrip,BL,BL,new Vector2(76,26),new Vector2(1120,30),"",18,new Color(.79f,.78f,.86f),TextAlignmentOptions.MidlineLeft);
            objectiveText=Label("Current thought",hud,TL,TL,Vector2.zero,Vector2.zero,"",18,Muted);
            objectiveText.gameObject.SetActive(false);
            var phone=KitCircle("Phone",hud,new Vector2(1,0),new Vector2(1,0),new Vector2(-34,54),64,"phone",()=>ToggleOverlay("phone"));
            Label("Key",phone.transform,new Vector2(.5f,0),TC,new Vector2(0,-6),new Vector2(92,24),"Tab",14,Muted,TextAlignmentOptions.Top);
            phoneBadge=MakeBadge(phone.transform,new Vector2(-2,2));
            foreach(var label in new[]{dateText,timeText,weatherText,placeText,ambienceText})
            {label.outlineColor=new Color(.10f,.14f,.22f,.6f);label.outlineWidth=.08f;}
        }

        void RightShortcut(string label,string key,LineIcon.Symbol symbol,float y,Action action)
        {
            var button=CircleButton(label,hud,new Vector2(1,.5f),C,new Vector2(-77,y),symbol,action);
            Label("Key",button.transform,new Vector2(.5f,0),TC,new Vector2(0,-9),new Vector2(92,28),key,17,Muted,TextAlignmentOptions.Top);
        }
        Button CircleButton(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,LineIcon.Symbol symbol,Action action)
        {
            var p=Panel(name,parent,anchor,pivot,pos,new Vector2(66,66),new Color(.05f,.10f,.19f,.68f),true);
            ((RoundedPanel)p).CornerRadius=33;
            var b=p.gameObject.AddComponent<Button>();b.targetGraphic=p;b.navigation=new Navigation{mode=Navigation.Mode.None};b.onClick.AddListener(()=>{if(!IsTransitioning&&!overlayBusy&&!(session.Memory?.PlayerChatOpen??false))action();});
            UIHoverFeedback.Attach(b);
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
            interactionPrompt=gameObject.AddComponent<InteractionPromptUI>();
            interactionPrompt.Initialize(canvasRect);
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
            weatherText.text=weather;
            RefreshPhoneBadge();
            placeText.text=PlaceName(session.CurrentPlace);
            objectiveText.text=session.Story.Objective;
            ambienceText.text=session.CurrentPlace==PlaceId.Cafe?"杯碟轻响。烘焙的香气，藏在海风够不到的地方。":session.CurrentPlace==PlaceId.Lighthouse?"灯塔静静转身。海浪替那些没说完的话，停顿了一下。":"海风，和远处的浪";
        }
        static IslandMapCatalog placeCatalog;
        public static string PlaceName(PlaceId id)
        {
            if(placeCatalog==null)placeCatalog=Resources.Load<IslandMapCatalog>("Content/IslandMap");
            if(placeCatalog!=null)foreach(var location in placeCatalog.locations)if(location.place==id)return location.displayName;
            return id==PlaceId.Cafe?"潮汐咖啡馆":id==PlaceId.Lighthouse?"灯塔海堤":"海边小街";
        }
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
            if(initialized)interactionPrompt.Show(text,worldPosition);
        }
        public void HideInteraction(){interactionPrompt?.Hide();}
        public void ShowToast(string text)
        {if(!initialized)return;toastText.text=text;toastUntil=Time.unscaledTime+3.8f;toastGroup.alpha=1;toastGroup.transform.SetAsLastSibling();}

        void Update()
        {
            if(!initialized||session==null)return;
            bool playerChat=session.Memory!=null&&session.Memory.PlayerChatOpen;
            hud.gameObject.SetActive(!hidden&&!playerChat);
            if(playerChat){HideInteraction();return;}
            if(overlayMode=="live")
            {
                if(Application.isFocused&&GameInput.Pressed(Key.Escape)&&!(nativeLive?.HandleBack()??false))CloseOverlay();
                return;
            }
            if(IsTransitioning||overlayBusy)
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
            if(GameInput.Pressed(Key.Escape)) {if(IsPhonePage(overlayMode)&&overlayMode!="phone")OpenOverlay("phone");else if(HasOverlay)CloseOverlay();else OpenOverlay("pause");}
            else if(!session.Dialogue.IsOpen)
            {
                if(GameInput.Pressed(Key.M))ToggleOverlay("map");
                if(GameInput.Pressed(Key.Tab)){if(IsPhonePage(overlayMode))CloseOverlay();else ToggleOverlay("phone");}
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
            interactionPrompt.SetSuppressed(HasOverlay||overlayBusy||session.Dialogue.IsOpen||hidden||IsTransitioning||(session.Memory?.PlayerChatOpen??false));
        }
        void ToggleOverlay(string mode)
        {
            if(overlayBusy||(session.Memory?.PlayerChatOpen??false))return;
            if(session.Dialogue.IsOpen&&mode!="pause")return;
            if(overlayMode==mode)CloseOverlay();else OpenOverlay(mode);
        }
        void OpenOverlay(string mode)
        {
            if(!initialized||IsTransitioning||overlayBusy||(session.Memory?.PlayerChatOpen??false))return;
            overlayBusy=true;Time.timeScale=0f;session.SetOverlay(true);HideInteraction();dialogueRoot.gameObject.SetActive(false);
            overlayRoutine=StartCoroutine(ShowOverlayRoutine(mode));
        }
        IEnumerator ShowOverlayRoutine(string mode)
        {
            if(HasOverlay)yield return FadeOverlay(0,Style.FadeOutDuration);
            overlayMode=mode;Clear(overlayRoot);overlayRoot.gameObject.SetActive(true);overlayGroup.alpha=0;
            var scrim=Stretch("Backdrop",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();scrim.color=IsPhonePage(mode)?Color.clear:Style.Scrim;scrim.raycastTarget=true;
            if(IsPhonePage(mode))
            {
                var dismiss=scrim.gameObject.AddComponent<Button>();dismiss.transition=Selectable.Transition.None;
                dismiss.navigation=new Navigation{mode=Navigation.Mode.None};dismiss.onClick.AddListener(CloseOverlay);
            }
            if(mode=="live")
            {
                canvas.enabled=false;nativeLive=NativeLiveHost.Open(transform,CloseOverlay);overlayBusy=false;overlayRoutine=null;yield break;
            }
            if(IsPhonePage(mode))
            {
                BuildPhoneShell(mode);
                yield return overlayContent.gameObject.AddComponent<PhoneSceneGlass>().Capture((RectTransform)overlayContent);
                yield return FadeOverlay(1,Style.FadeInDuration);overlayBusy=false;overlayRoutine=null;yield break;
            }
            Vector2 size=mode=="pause"||mode=="restart"?new Vector2(760,730):mode=="ending"?new Vector2(1160,790):new Vector2(1280,790);
            var panel=Panel("Panel",overlayRoot,C,C,Vector2.zero,size,Ink,true);overlayContent=panel.transform;
            CircleButton("Close",panel.transform,new Vector2(1,1),new Vector2(1,1),new Vector2(-24,-24),LineIcon.Symbol.Close,CloseOverlay);
            if(mode=="map")BuildMap();else if(mode=="journal")BuildJournal();else if(mode=="pause")BuildPause();else if(mode=="restart")BuildRestart();else if(mode=="ending")BuildEnding();
            yield return FadeOverlay(1,Style.FadeInDuration);overlayBusy=false;overlayRoutine=null;
        }
        IEnumerator FadeOverlay(float target,float duration)
        {
            overlayGroup.interactable=false;overlayGroup.blocksRaycasts=false;
            float start=overlayGroup.alpha;
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/Mathf.Max(.01f,duration))
            {overlayGroup.alpha=Mathf.Lerp(start,target,t*t*(3-2*t));yield return null;}
            overlayGroup.alpha=target;overlayGroup.interactable=target>0;overlayGroup.blocksRaycasts=target>0;
        }
        public void CloseOverlay(){CloseOverlayThen(null);}
        public void CloseOverlayImmediately()
        {
            if(!initialized)return;
            if(overlayRoutine!=null)StopCoroutine(overlayRoutine);
            overlayRoutine=null;FinishCloseOverlay();
        }
        void CloseOverlayThen(Action next)
        {
            if(!initialized||overlayBusy)return;
            if(nativeLive!=null){FinishCloseOverlay();next?.Invoke();return;}
            if(!HasOverlay){next?.Invoke();return;}
            overlayBusy=true;overlayRoutine=StartCoroutine(CloseOverlayRoutine(next));
        }
        IEnumerator CloseOverlayRoutine(Action next)
        {
            yield return FadeOverlay(0,Style.FadeOutDuration);FinishCloseOverlay();next?.Invoke();
        }
        void FinishCloseOverlay()
        {
            if(nativeLive!=null){nativeLive.Close();nativeLive=null;}
            canvas.enabled=true;overlayBusy=false;overlayRoutine=null;
            overlayMode="";Time.timeScale=1f;overlayRoot.gameObject.SetActive(false);session.SetOverlay(IsTransitioning);
            dialogueRoot.gameObject.SetActive(session.Dialogue.IsOpen);dialogueFrame=Time.frameCount;RefreshHUD();
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
            Header("TIDELINE  /  LOCAL MAP","在小镇，慢一点也没关系", "当前："+PlaceName(session.CurrentPlace)+"   ·   "+PeriodName(session.Clock.Period)+"   ·   选择地点前往，也可以从街道的出入口步行进入。" );
            var area=Rect("Map drawing",overlayContent,TL,TL,new Vector2(46,-235),new Vector2(1188,335));
            Panel("Sea",area,BL,BL,new Vector2(0,0),new Vector2(1188,127),new Color(.08f,.20f,.30f,.6f));
            for(int i=0;i<4;i++)Line(area,BL,BL,new Vector2(50+i*140,27+i*20),new Vector2(430,1),new Color(.25f,.52f,.63f,.30f));
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
            Button("Model settings",overlayContent,TL,TL,new Vector2(46,-444),new Vector2(310,32),"对话服务设置",()=>Application.OpenURL(MemoryRuntimeConfig.Settings.serviceUrl+"/settings"),false,18);
            Button("Load save",overlayContent,TL,TL,new Vector2(380,-444),new Vector2(330,32),"读取上次存档",()=>CloseOverlayThen(()=>session.LoadSavedGame()),false,18);
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
            Button("Confirm restart",overlayContent,TL,TL,new Vector2(46,-515),new Vector2(668,64),"确认重新开始",()=>CloseOverlayThen(()=>{observedEnding="";pendingEnding="";session.NewGame();RefreshHUD();OnDialogueChanged();}),false,24);
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
