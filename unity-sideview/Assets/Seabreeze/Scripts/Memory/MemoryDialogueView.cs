using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;
namespace Seabreeze
{
    // PR32 presentation, driven by the existing model conversation and memory events.
    public sealed partial class MemoryDialogueView : MonoBehaviour
    {
        MemoryGameController memory; GameSession session;
        RectTransform root,speechPanel,playerPanel,portrait,column,content;
        TextMeshProUGUI speechTitle,speechText,playerTitle,locationText,historyText,playerState,emptyPrompt,inputHelp;
        TMP_InputField input; Button send; ScrollRect historyScroll;
        CanvasGroup playerGroup; Live2DCharacterView character; Coroutine transition;
        sealed class ChatBubble
        {
            public RectTransform Row,Panel;
            public TextMeshProUGUI Text;
            public Image Surface;
            public bool IsPlayer;
        }
        readonly List<ChatBubble> history=new List<ChatBubble>();
        DialogueSceneBackdrop backdrop;
        float previousColumnWidth;int lastLayoutCharacters=-1;
        static readonly Color BubbleInk=new Color(.145f,.149f,.208f,1);
        const float ExpressionHoldSeconds=1.2f;
        const float LiyuanSmilePreparationSeconds=.25f;
        const float LiyuanBawlPreparationSeconds=2f;
        float charactersPerSecond;
        bool followingSpeech,speechStarted;string portraitSpeechToken;
        public bool ReadyForSpeech => PlayerInputReady&&!linePreparationActive;
        public void FollowSpeech()
        {
            followingSpeech=true;speechStarted=false;
            portraitSpeechToken=Guid.NewGuid().ToString("N");
            character?.SetState(portraitEmotion,false);
        }
        float ExpressionClock => selectedActor == "xiaoxiaoqing" ? Time.realtimeSinceStartup : Time.unscaledTime;
        Coroutine linePreparation;
        bool linePreparationActive;
        string selectedActor,lastSpeaker,portraitEmotion; bool entering,typing; float revealed,speechUntil,expressionEndsAt;

        void EndFollowingSpeech()
        {
            if(portraitSpeechToken!=null)character?.EndSpeechDrive(portraitSpeechToken,0);
            portraitSpeechToken=null;followingSpeech=false;
        }
        public bool Closing {get;private set;}
        public bool PlayerInputReady => !entering&&!Closing&&playerPanel!=null&&playerPanel.gameObject.activeSelf;
        internal TMP_InputField DialogueInput => input;
        internal Button DialogueSend => send;
        internal TextMeshProUGUI DialogueHistory => historyText;
        public string VisibleSpeech => speechText!=null&&speechPanel.gameObject.activeSelf?speechTitle.text+"："+speechText.text:"";
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w)
        {
            memory=m;session=s;UIPrimitives.Font=Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            var canvas=new GameObject("岛民对话界面",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform,false);canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=150;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=Style.ReferenceResolution;scaler.matchWidthOrHeight=.5f;
            root=canvas.GetComponent<RectTransform>();

            speechPanel=Panel("附近的交谈",root,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,30),new Vector2(1060,200),Style.PanelFill).rectTransform;
            speechTitle=Label("说话者",speechPanel,Vector2.up,Vector2.up,new Vector2(26,-16),new Vector2(1008,35),"",25,Accent);
            speechText=Label("台词",speechPanel,Vector2.up,Vector2.up,new Vector2(26,-60),new Vector2(1008,122),"",25,Paper);speechTitle.richText=speechText.richText=false;
            speechText.enableAutoSizing=true;speechText.fontSizeMin=18;speechText.fontSizeMax=25;speechPanel.gameObject.SetActive(false);
            BuildPlayerView();
            session.Clock.Changed+=RefreshDialoguePalette;
        }
        static RectTransform Region(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var r=Stretch(name,parent,Vector2.zero,Vector2.zero);r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        void BuildPlayerView()
        {
            playerPanel=Stretch("玩家交谈",root,Vector2.zero,Vector2.zero);playerGroup=playerPanel.gameObject.AddComponent<CanvasGroup>();
            backdrop=gameObject.AddComponent<DialogueSceneBackdrop>();backdrop.Initialize(playerPanel);
            var shade=Stretch("Scene dim",playerPanel,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();shade.color=new Color(.08f,.12f,.22f,.19f);shade.raycastTarget=true;
            portrait=Region("Live2D portrait viewport",playerPanel,new Vector2(.015f,.025f),new Vector2(.445f,.84f));portrait.gameObject.AddComponent<RectMask2D>();
            character=portrait.gameObject.AddComponent<Live2DCharacterView>();character.Initialize(portrait);
            column=Region("Conversation column",playerPanel,new Vector2(.455f,.12f),new Vector2(.95f,.87f));
            playerTitle=Label("交谈对象",column,Vector2.up,Vector2.up,Vector2.zero,new Vector2(690,58),"",44,Paper);
            locationText=Label("地点时段",column,Vector2.up,Vector2.up,new Vector2(0,-64),new Vector2(690,36),"",18,Paper);
            foreach(var label in new[]{playerTitle,locationText}){label.richText=false;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.sizeDelta=new Vector2(-160,label.rectTransform.sizeDelta.y);}
            var viewport=Stretch("Conversation scroll viewport",column,new Vector2(-5,138),new Vector2(5,-156));viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            historyScroll=viewport.gameObject.AddComponent<ScrollRect>();historyScroll.horizontal=false;historyScroll.movementType=ScrollRect.MovementType.Clamped;historyScroll.scrollSensitivity=34;historyScroll.viewport=viewport;
            content=Region("Conversation content",viewport,Vector2.up,Vector2.one);content.pivot=new Vector2(.5f,1);historyScroll.content=content;
            emptyPrompt=Label("Conversation invitation",content,Vector2.up,Vector2.up,new Vector2(12,-26),new Vector2(630,64),"想说什么，就从这里开始吧。",23,new Color(.95f,.95f,1,.8f));
            replySurface=Panel("回复",column,Vector2.zero,Vector2.zero,Vector2.zero,new Vector2(0,72),new Color(.94f,.95f,1,.64f),true);replySurface.rectTransform.anchorMax=Vector2.right;
            var dock=replySurface;
            ((RoundedPanel)dock).CornerRadius=36;((RoundedPanel)dock).SetBorder(new Color(1,1,1,.28f),1);
            input=dock.gameObject.AddComponent<TMP_InputField>();input.characterLimit=300;input.lineType=TMP_InputField.LineType.SingleLine;
            var area=Stretch("Text Area",dock.transform,new Vector2(26,18),new Vector2(-128,-18));area.gameObject.AddComponent<RectMask2D>();
            var entered=Label("Text",area,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,"",23,BubbleInk);Fill(entered.rectTransform);entered.textWrappingMode=TextWrappingModes.NoWrap;
            input.textViewport=area;input.textComponent=entered;input.richText=false;input.caretColor=BubbleInk;input.customCaretColor=true;
            var hint=Label("Placeholder",area,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,"写下想说的话…",23,new Color(.2f,.23f,.30f,.63f));Fill(hint.rectTransform);hint.richText=false;input.placeholder=hint;
            input.onSubmit.AddListener(_=>Send());
            sendSurface=Panel("发送",dock.transform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-12,0),new Vector2(98,48),new Color(1,1,1,.28f),true);
            var sendPanel=sendSurface;
            ((RoundedPanel)sendPanel).CornerRadius=24;((RoundedPanel)sendPanel).SetBorder(new Color(1,1,1,.4f),1);
            send=sendPanel.gameObject.AddComponent<Button>();send.targetGraphic=sendPanel;send.navigation=new Navigation{mode=Navigation.Mode.None};send.onClick.AddListener(Send);
            var sendColors=send.colors;sendColors.highlightedColor=new Color(.86f,.86f,1,1);sendColors.pressedColor=new Color(.7f,.75f,.9f,1);send.colors=sendColors;
            sendCaption=Label("Send caption",sendPanel.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(90,35),"发送 →",21,BubbleInk,TextAlignmentOptions.Center);
            playerState=Label("连接与等待状态",column,Vector2.zero,Vector2.zero,new Vector2(5,88),new Vector2(730,32),"",18,Paper);playerState.richText=false;playerState.rectTransform.anchorMax=Vector2.right;playerState.rectTransform.sizeDelta=new Vector2(0,32);
            inputHelp=Label("输入提示",column,Vector2.zero,Vector2.zero,new Vector2(5,-36),new Vector2(730,28),"Enter 发送    ·    点击气泡显示全文    ·    Esc 返回",16,new Color(.9f,.92f,1,.7f));inputHelp.rectTransform.anchorMax=Vector2.right;inputHelp.rectTransform.sizeDelta=new Vector2(0,28);
            DialogueControl("声音",-76,"sound",()=>{AudioListener.volume=AudioListener.volume>.01f?0:1;if(AudioListener.volume<=.01f)memory.Client.StopSpeech();});
            DialogueControl("结束交谈",-8,"close",CloseConversationView);
            BuildDaylightPresentation();
            playerGroup.alpha=0;playerPanel.gameObject.SetActive(false);
        }
        void DialogueControl(string name,float x,string symbol,Action action)
        {
            var p=Panel(name,column,Vector2.one,Vector2.one,new Vector2(x,-12),new Vector2(49,49),new Color(.92f,.94f,1,.45f),true);
            headerControls.Add(p);
            ((RoundedPanel)p).CornerRadius=25;((RoundedPanel)p).SetBorder(new Color(1,1,1,.28f));
            var button=p.gameObject.AddComponent<Button>();button.targetGraphic=p;button.navigation=new Navigation{mode=Navigation.Mode.None};button.onClick.AddListener(()=>{if(PlayerInputReady)action();});
            FinalKitAssets.Icon(symbol,p.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(21,21));
        }
        void ClearHistory()
        {
            foreach(var bubble in history)Destroy(bubble.Row.gameObject);history.Clear();historyText=null;
            if(emptyPrompt!=null)emptyPrompt.gameObject.SetActive(true);
        }
        void AddBubble(string message,bool isPlayer)
        {
            emptyPrompt.gameObject.SetActive(false);
            var row=Rect(isPlayer?"Player message":"NPC message",content,Vector2.up,Vector2.up,Vector2.zero,Vector2.zero);row.anchorMax=Vector2.one;
            var anchor=isPlayer?Vector2.one:Vector2.up;
            var panel=FinalKitAssets.Bubble(isPlayer?"bubble-player":"bubble-npc",row,anchor,anchor,Vector2.zero,new Vector2(334,86));panel.raycastTarget=true;
            var text=Label("Message",panel.transform,Vector2.up,Vector2.up,new Vector2(34,-22),new Vector2(260,36),message,24,BubbleInk);text.richText=false;text.lineSpacing=5;text.maxVisibleCharacters=isPlayer?int.MaxValue:0;
            var reveal=panel.gameObject.AddComponent<Button>();reveal.targetGraphic=panel;reveal.transition=Selectable.Transition.None;reveal.navigation=new Navigation{mode=Navigation.Mode.None};reveal.onClick.AddListener(FinishTyping);
            var bubble=new ChatBubble{Row=row,Panel=panel.rectTransform,Text=text,Surface=panel,IsPlayer=isPlayer};
            ApplyBubblePalette(bubble);history.Add(bubble);historyText=text;
            if(history.Count>12){Destroy(history[0].Row.gameObject);history.RemoveAt(0);}
            RefreshHistoryLayout();
        }
        static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static bool IsComposing(){var system=EventSystem.current?.currentInputModule?.input;return !string.IsNullOrEmpty(system!=null?system.compositionString:Input.compositionString);}
        void Send()
        {
            if(IsComposing()){StartCoroutine(ResumeInput());return;}
            if(!PlayerInputReady||memory.Conversations==null||!memory.Client.CanGenerate||memory.Conversations.Sending||string.IsNullOrWhiteSpace(input.text))return;
            FinishTyping();ResetPortraitIdle();string value=input.text;input.text="";memory.Conversations.SendPlayer(value);input.ActivateInputField();
        }
        IEnumerator ResumeInput(){yield return null;if(PlayerInputReady&&memory.PlayerChatOpen)input.ActivateInputField();}
        public void OpenPlayer(string actorId)
        {
            if(transition!=null)StopCoroutine(transition);ResetPortraitIdle();Closing=false;entering=true;selectedActor=actorId;ClearTaskCards();ClearHistory();typing=false;portraitEmotion=null;expressionEndsAt=0;
            playerTitle.text=SevenCharacterCatalog.Name(actorId);
            RefreshDialoguePalette();

            locationText.text=GameUI.PlaceName(session.CurrentPlace)+"  ·  "+GameUI.PeriodName(session.Clock.Period);
            playerPanel.gameObject.SetActive(true);speechPanel.gameObject.SetActive(false);playerGroup.alpha=0;playerGroup.interactable=false;input.text="";
            ApplyPortrait(actorId);
            if(!ScriptedOpen)RefreshTasks();RefreshHistoryLayout();transition=StartCoroutine(AnimatePlayer(true));
        }
        void ApplyPortrait(string actorId)
        {
            var definition=SevenCharacterCatalog.Find(actorId);
            // No audio timing exists when muted/offline; keep a calm readable fallback.
            charactersPerSecond = Mathf.Min(6,definition != null && definition.dialogueCharactersPerSecond > 0
                ? definition.dialogueCharactersPerSecond : Style.TypewriterSpeed);
            bool final=actorId=="zhou_bobo"&&memory.ZhouboPortraitFinal;
            string path=final?definition?.previewPortrait:definition?.portrait;
            var profile=!string.IsNullOrEmpty(path)?Resources.Load<Live2DModelProfile>(path):null;
            portrait.gameObject.SetActive(profile!=null);
            if(profile!=null)character.Show(profile);else character.Hide();
        }
        IEnumerator AnimatePlayer(bool opening)
        {
            if(opening)yield return backdrop.Capture();
            float start=playerGroup.alpha,end=opening?1:0,duration=opening?Style.DialogueEnterDuration:Style.DialogueExitDuration;
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/Mathf.Max(.01f,duration)){SetPose(Mathf.Lerp(start,end,t*t*(3-2*t)));yield return null;}
            SetPose(end);transition=null;entering=false;Closing=false;
            if(opening){playerGroup.interactable=true;if(!ScriptedOpen)input.ActivateInputField();}
            else{playerPanel.gameObject.SetActive(false);character.Hide();backdrop.Release();}
        }
        void SetPose(float alpha){playerGroup.alpha=alpha;portrait.anchoredPosition=Vector2.left*(1-alpha)*34;portrait.localScale=Vector3.one*Mathf.Lerp(.975f,1,alpha);column.anchoredPosition=Vector2.right*(1-alpha)*18;}
        public void HidePlayer()
        {
            if(playerPanel==null||!playerPanel.gameObject.activeSelf)return;
            if(transition!=null)StopCoroutine(transition);Closing=true;entering=false;playerGroup.interactable=false;input.DeactivateInputField();FinishTyping();ResetPortraitIdle();memory.ResetZhouboPortraitSelection();transition=StartCoroutine(AnimatePlayer(false));
        }
        void RefreshHistoryLayout()
        {
            Canvas.ForceUpdateCanvases();float width=Mathf.Max(160,historyScroll.viewport.rect.width-16),y=16;
            previousColumnWidth=column.rect.width;
            foreach(var bubble in history)
            {
                bubble.Text.ForceMeshUpdate(true);
                int count=Mathf.Min(bubble.Text.maxVisibleCharacters,bubble.Text.textInfo.characterCount);
                bool visible=bubble.IsPlayer||count>0;
                bubble.Row.gameObject.SetActive(visible);
                if(!visible){bubble.Row.sizeDelta=new Vector2(-16,0);continue;}
                string shown=bubble.Text.text;
                if(count<bubble.Text.textInfo.characterCount){var last=bubble.Text.textInfo.characterInfo[count-1];shown=shown.Substring(0,last.index+last.stringLength);}
                float maxWidth=Mathf.Min(594,width*.88f);
                float bubbleWidth=Mathf.Clamp(bubble.Text.GetPreferredValues(shown).x+68,bubble.IsPlayer?128:84,maxWidth);
                float textHeight=Mathf.Max(32,bubble.Text.GetPreferredValues(shown,bubbleWidth-68,0).y);
                bubble.Row.anchoredPosition=new Vector2(8,-y);bubble.Row.sizeDelta=new Vector2(-16,textHeight+52);
                bubble.Panel.sizeDelta=new Vector2(bubbleWidth,textHeight+52);bubble.Text.rectTransform.sizeDelta=new Vector2(bubbleWidth-68,textHeight);
                bubble.Text.ForceMeshUpdate();y+=textHeight+62;
            }
            y=LayoutTaskCards(width,y);
            emptyPrompt.gameObject.SetActive(history.Count==0&&taskCards.Count==0);
            content.sizeDelta=new Vector2(0,Mathf.Max(historyScroll.viewport.rect.height,y+8));historyScroll.verticalNormalizedPosition=0;
            lastLayoutCharacters=historyText!=null?historyText.maxVisibleCharacters:-1;
        }
        void RefreshTypingLayout()
        {
            if(historyText!=null&&lastLayoutCharacters!=historyText.maxVisibleCharacters)RefreshHistoryLayout();
        }
        void FinishTyping()
        {
            bool hadPendingLine=typing||linePreparationActive;
            CancelPendingLinePreparation();
            if(hadPendingLine&&!followingSpeech&&!string.IsNullOrEmpty(portraitEmotion))expressionEndsAt=ExpressionClock+ExpressionHoldSeconds;
            typing=false;
            if(historyText!=null){historyText.maxVisibleCharacters=int.MaxValue;RefreshTypingLayout();}
            if(character!=null)character.SetState(portraitEmotion,followingSpeech&&memory.Client.SpeechPlaying);
        }
        void BeginPortraitLine(string speaker,string expression)
        {
            CancelPendingLinePreparation();expressionEndsAt=0;portraitEmotion=null;
            if(character==null)return;
            // Every incoming line owns a fresh parameter frame. This is wider than
            // the expression list so c06 tears, eye closure, gaze and mouth shape
            // cannot leak through a line switch or an NPC/player handoff.
            character.ResetToDefault();
            bool speaking=speaker!=null&&speaker==selectedActor;
            string key=expression?.Trim();
            var profile=character.CurrentProfile;
            if(speaking && profile!=null && profile.portraitMotions!=null && profile.portraitMotions.Length>0)
            {
                character.SetState(null,true);
                if(character.PlayPortraitMotion(key))return;
            }
            if(speaking&&!string.IsNullOrEmpty(key)&&(character.CurrentProfile?.FindExpression(key)!=null||Live2DCharacterView.SupportsExternalExpression(character.CurrentProfile,key)))portraitEmotion=key;
            float preparationSeconds=speaking?PreparationSeconds(character.CurrentProfile,key):0f;
            bool prepare=preparationSeconds>.001f&&!string.IsNullOrEmpty(portraitEmotion);
            // 梨园的笑哭表演、留洋叹气的准备动作先于文字和口型。
            character.SetState(portraitEmotion,prepare?false:speaking);
            if(prepare)
            {
                linePreparationActive=true;
                linePreparation=StartCoroutine(PrepareLineAfter(preparationSeconds,portraitEmotion));
            }
            else if(speaking&&!string.IsNullOrEmpty(key)&&portraitEmotion==null)character.PlayPortraitMotion(key);
        }
        IEnumerator PrepareLineAfter(float seconds,string emotion)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if(!linePreparationActive||!string.Equals(portraitEmotion,emotion,StringComparison.OrdinalIgnoreCase))
            {
                linePreparation=null;linePreparationActive=false;yield break;
            }
            linePreparation=null;linePreparationActive=false;
            if(character!=null)character.SetState(portraitEmotion,!followingSpeech||memory.Client.SpeechPlaying);
            typing=true;
        }
        void CancelPendingLinePreparation()
        {
            linePreparationActive=false;
            if(linePreparation!=null){StopCoroutine(linePreparation);linePreparation=null;}
        }
        static bool IsLiyuanProfile(Live2DModelProfile value)
        {
            return value!=null&&string.Equals(value.characterId,"liyuan",StringComparison.OrdinalIgnoreCase);
        }
        static float PreparationSeconds(Live2DModelProfile profile,string expression)
        {
            if(profile!=null&&profile.characterId=="c01"&&string.Equals(expression,"sigh",StringComparison.OrdinalIgnoreCase))
                return profile.FindExpressionTransition(expression)?.introSeconds??0f;
            if(!IsLiyuanProfile(profile)) return 0f;
            switch((expression??string.Empty).Trim().ToLowerInvariant().Replace('-','_'))
            {
                case "smile":
                case "dialogue_smile":
                case "happy":
                case "warm":
                    return LiyuanSmilePreparationSeconds;
                case "bawl":
                case "bawl_dry":
                case "bawl_wet":
                case "bawl_tears_preview":
                case "bawling":
                    return LiyuanBawlPreparationSeconds;
                default:
                    return 0f;
            }
        }
        void ResetPortraitIdle()
        {
            EndFollowingSpeech();speechStarted=false;CancelPendingLinePreparation();expressionEndsAt=0;portraitEmotion=null;character?.ResetToDefault();
        }

        public void ShowLine(string speaker,string text,Vector3 position,bool playerConversation,string expression=null)
        {
            if(playerConversation)
            {
                EndFollowingSpeech();speechStarted=false;FinishTyping();AddBubble(text,speaker=="player");lastSpeaker=speaker;revealed=0;
                BeginPortraitLine(speaker,expression);
                typing=speaker!="player"&&!linePreparationActive;historyText.maxVisibleCharacters=(typing||linePreparationActive)?0:int.MaxValue;RefreshTypingLayout();return;
            }
            speechTitle.text=SevenCharacterCatalog.Name(speaker);speechText.text=text;speechTitle.color=SevenCharacterCatalog.Find(speaker)?.Accent??Paper;
            speechUntil=Time.unscaledTime+Mathf.Clamp(1.3f+text.Length*.05f,2.5f,6);speechPanel.gameObject.SetActive(true);
        }
        public void ResetView()
        {
            StopAllCoroutines();
            var cancelledStory=ResetStoryState();
            transition=null;Closing=entering=false;ClearHistory();FinishTyping();ResetPortraitIdle();memory.ResetZhouboPortraitSelection();backdrop.Release();playerPanel.gameObject.SetActive(false);playerGroup.alpha=0;character.Hide();speechPanel.gameObject.SetActive(false);ClearTaskCards();
            // Notify after the view is fully reset. A callback cannot leave a half-reset new view.
            cancelledStory?.Invoke(false);
        }
        void Update()
        {
            if(memory?.Client==null||root==null)return;
            if(expressionEndsAt>0&&ExpressionClock>=expressionEndsAt){expressionEndsAt=0;portraitEmotion=null;if(character!=null)character.SetState(null,false);}
            bool fullView=playerPanel.gameObject.activeSelf,overlay=GameUI.Instance!=null&&(GameUI.Instance.HasOverlay||GameUI.Instance.IsOverlayTransitioning);
            if(fullView&&!Mathf.Approximately(previousColumnWidth,column.rect.width))RefreshHistoryLayout();

            if(speechPanel.gameObject.activeSelf&&(Time.unscaledTime>speechUntil||fullView||overlay))speechPanel.gameObject.SetActive(false);
            if(ScriptedOpen){TickStory();return;}
            if(PlayerInputReady&&GameInput.Pressed(Key.Escape))CloseConversationView();
            if(!Closing)RefreshTasks();
            UpdateTaskActions(fullView&&!overlay);
            send.interactable=PlayerInputReady&&memory.PlayerChatOpen&&memory.Client.CanGenerate&&!memory.Conversations.Sending;
            playerState.text=memory.Conversations.Sending?"对方正在想怎么说…":memory.Client.CanGenerate?"":"暂时无法连接。Esc 返回后，可在暂停菜单设置对话服务。";
            playerState.color=new Color(.94f,.94f,1,.82f);
            if(followingSpeech&&PlayerInputReady)
            {
                bool playing=memory.Client.SpeechPlaying;
                if(playing)
                {
                    speechStarted=true;
                    if(typing)
                    {
                        int visible=0,end=memory.Client.SpeechCaptionEnd;
                        while(visible<historyText.textInfo.characterCount&&historyText.textInfo.characterInfo[visible].index<end)visible++;
                        revealed=Mathf.Max(revealed,visible);
                        historyText.maxVisibleCharacters=Mathf.FloorToInt(revealed);
                        if(historyText.maxVisibleCharacters>=historyText.textInfo.characterCount)FinishTyping();
                    }
                }
                character.SetState(portraitEmotion,playing);
                if(playing)
                {
                    if(!character.HasSpeechDrive)character.BeginSpeechDrive(portraitSpeechToken,0);
                    character.SetSpeechAmplitude(portraitSpeechToken,0,memory.Client.SpeechAmplitude);
                }
                RefreshTypingLayout();
                if(memory.Client.SpeechBusy)return;
                EndFollowingSpeech();
                if(speechStarted){FinishTyping();expressionEndsAt=ExpressionClock+ExpressionHoldSeconds;}
                speechStarted=false;
            }
            if(typing&&PlayerInputReady){revealed+=Time.unscaledDeltaTime*charactersPerSecond;historyText.maxVisibleCharacters=Mathf.FloorToInt(revealed);if(historyText.maxVisibleCharacters>=historyText.textInfo.characterCount)FinishTyping();else character.SetState(portraitEmotion,lastSpeaker==selectedActor);}
            RefreshTypingLayout();

        }
    }
}
