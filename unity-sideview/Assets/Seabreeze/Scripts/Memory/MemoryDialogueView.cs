using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;
namespace Seabreeze
{
    // PR32 presentation, driven by the existing model conversation and memory events.
    public sealed class MemoryDialogueView : MonoBehaviour
    {
        MemoryGameController memory; GameSession session; WorldDirector world;
        RectTransform root,statusPanel,speechPanel,playerPanel,day4Panel,portrait,column,content;
        TextMeshProUGUI statusText,speechTitle,speechText,playerTitle,locationText,historyText,playerState;
        TMP_InputField input; Button send; ScrollRect historyScroll;
        CanvasGroup playerGroup; Live2DCharacterView character; Coroutine transition;
        readonly List<string> history=new List<string>();
        readonly Dictionary<NpcPresence,TextMeshProUGUI> names=new Dictionary<NpcPresence,TextMeshProUGUI>();
        string selectedActor,lastSpeaker; bool entering,typing; float revealed,speechUntil,day4Until;
        Action day4Shown; int day4Frame;
        public bool Closing {get;private set;}
        public bool PlayerInputReady => !entering&&!Closing&&playerPanel!=null&&playerPanel.gameObject.activeSelf;
        public bool Day4Visible => day4Panel!=null&&day4Panel.gameObject.activeSelf;
        public string VisibleSpeech => speechText!=null&&speechPanel.gameObject.activeSelf?speechTitle.text+"："+speechText.text:"";
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w)
        {
            memory=m;session=s;world=w;UIPrimitives.Font=Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            var canvas=new GameObject("岛民对话界面",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform,false);canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=150;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=Style.ReferenceResolution;scaler.matchWidthOrHeight=.5f;
            root=canvas.GetComponent<RectTransform>();
            statusPanel=Panel("记忆状态",root,Vector2.one,Vector2.one,new Vector2(-26,-136),new Vector2(430,148),Style.HudFill).rectTransform;
            statusText=Label("状态",statusPanel,Vector2.up,Vector2.up,new Vector2(18,-14),new Vector2(394,62),"",20,Paper);statusText.richText=false;statusText.enableAutoSizing=true;statusText.fontSizeMin=14;statusText.fontSizeMax=20;
            ButtonAt("模型设置",statusPanel,new Vector2(14,-99),new Vector2(100,35),()=>Application.OpenURL(MemoryRuntimeConfig.Settings.serviceUrl+"/settings"));
            ButtonAt("记忆",statusPanel,new Vector2(123,-99),new Vector2(88,35),()=>Application.OpenURL(MemoryRuntimeConfig.Settings.serviceUrl+"/memories"));
            ButtonAt("保存",statusPanel,new Vector2(220,-99),new Vector2(88,35),()=>{if(session.Save())session.Story.Notify("小岛进度已保存。");});
            ButtonAt("读档",statusPanel,new Vector2(317,-99),new Vector2(98,35),()=>session.LoadSavedGame());
            speechPanel=Panel("附近的交谈",root,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,30),new Vector2(1060,200),Style.PanelFill).rectTransform;
            speechTitle=Label("说话者",speechPanel,Vector2.up,Vector2.up,new Vector2(26,-16),new Vector2(1008,35),"",25,Accent);
            speechText=Label("台词",speechPanel,Vector2.up,Vector2.up,new Vector2(26,-60),new Vector2(1008,122),"",25,Paper);speechTitle.richText=speechText.richText=false;
            speechText.enableAutoSizing=true;speechText.fontSizeMin=18;speechText.fontSizeMax=25;speechPanel.gameObject.SetActive(false);
            BuildPlayerView();
            day4Panel=Panel("Day4全岛公告",root,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-160),new Vector2(790,156),Style.PanelFill).rectTransform;
            Label("公告标题",day4Panel,Vector2.up,Vector2.up,new Vector2(28,-20),new Vector2(734,34),"DAY 4 · 全岛公告",26,Accent);
            Label("公告内容",day4Panel,Vector2.up,Vector2.up,new Vector2(28,-68),new Vector2(734,64),"小岛将要毁灭。",34,Paper);
            day4Panel.gameObject.SetActive(false);
        }
        static RectTransform Region(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var r=Stretch(name,parent,Vector2.zero,Vector2.zero);r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        void BuildPlayerView()
        {
            playerPanel=Stretch("玩家交谈",root,Vector2.zero,Vector2.zero);playerGroup=playerPanel.gameObject.AddComponent<CanvasGroup>();
            var shade=Stretch("Scene dim",playerPanel,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();shade.color=Style.Scrim;shade.raycastTarget=true;
            portrait=Region("Live2D portrait viewport",playerPanel,new Vector2(.025f,.015f),new Vector2(.51f,.95f));portrait.gameObject.AddComponent<RectMask2D>();
            character=portrait.gameObject.AddComponent<Live2DCharacterView>();character.Initialize(portrait);
            column=Region("Conversation column",playerPanel,new Vector2(.545f,.11f),new Vector2(.925f,.87f));
            playerTitle=Label("交谈对象",column,Vector2.up,Vector2.up,Vector2.zero,new Vector2(690,58),"",42,Paper);
            locationText=Label("地点时段",column,Vector2.up,Vector2.up,new Vector2(0,-68),new Vector2(690,36),"",20,Muted);
            foreach(var label in new[]{playerTitle,locationText}){label.richText=false;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.sizeDelta=new Vector2(0,label.rectTransform.sizeDelta.y);}
            var separator=Stretch("Conversation separator",column,Vector2.zero,Vector2.zero);separator.anchorMin=Vector2.up;separator.anchorMax=Vector2.one;separator.pivot=new Vector2(.5f,1);separator.anchoredPosition=new Vector2(0,-120);separator.sizeDelta=new Vector2(0,1);separator.gameObject.AddComponent<Image>().color=Style.PanelBorder;
            var viewport=Stretch("Conversation scroll viewport",column,new Vector2(-5,148),new Vector2(5,-145));viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            historyScroll=viewport.gameObject.AddComponent<ScrollRect>();historyScroll.horizontal=false;historyScroll.movementType=ScrollRect.MovementType.Clamped;historyScroll.scrollSensitivity=34;historyScroll.viewport=viewport;
            content=Region("Conversation content",viewport,Vector2.up,Vector2.one);content.pivot=new Vector2(.5f,1);
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(5,5,5,5);layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;historyScroll.content=content;
            var bubble=Panel("Dialogue bubble",content,Vector2.zero,Vector2.up,Vector2.zero,new Vector2(690,220),Style.PanelFill,true);
            var bubbleLayout=bubble.gameObject.AddComponent<VerticalLayoutGroup>();bubbleLayout.padding=new RectOffset(28,28,24,24);bubbleLayout.childControlWidth=true;bubbleLayout.childControlHeight=true;bubbleLayout.childForceExpandHeight=false;
            historyText=Label("交谈记录",bubble.transform,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,"",Style.DialogueBodySize,Paper);historyText.richText=false;historyText.lineSpacing=12;historyText.gameObject.AddComponent<LayoutElement>().minHeight=90;
            var reveal=bubble.gameObject.AddComponent<Button>();reveal.transition=Selectable.Transition.None;reveal.onClick.AddListener(FinishTyping);reveal.navigation=new Navigation{mode=Navigation.Mode.None};
            var dock=Rect("回复",column,Vector2.zero,Vector2.zero,new Vector2(0,27),new Vector2(0,60));dock.anchorMax=Vector2.right;
            var field=Panel("输入",dock,Vector2.zero,Vector2.zero,Vector2.zero,new Vector2(-122,60),Style.PanelFill,true);field.rectTransform.anchorMax=Vector2.right;
            input=field.gameObject.AddComponent<TMP_InputField>();input.characterLimit=300;input.lineType=TMP_InputField.LineType.SingleLine;
            var area=Stretch("Text Area",field.transform,new Vector2(14,11),new Vector2(-14,-11));area.gameObject.AddComponent<RectMask2D>();
            var entered=Label("Text",area,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,"",23,Paper);Fill(entered.rectTransform);entered.textWrappingMode=TextWrappingModes.NoWrap;
            input.textViewport=area;input.textComponent=entered;input.richText=false;
            var hint=Label("Placeholder",area,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,"写下想说的话…",22,Muted);Fill(hint.rectTransform);hint.richText=false;input.placeholder=hint;
            input.onSubmit.AddListener(_=>Send());
            send=UIPrimitives.Button("发送",dock,Vector2.right,Vector2.right,Vector2.zero,new Vector2(108,60),"发送 →",Send,false,22);send.GetComponentInChildren<TextMeshProUGUI>().alignment=TextAlignmentOptions.Center;
            playerState=Label("连接与等待状态",column,Vector2.zero,Vector2.zero,new Vector2(0,101),new Vector2(730,30),"",18,Muted);playerState.richText=false;playerState.rectTransform.anchorMax=Vector2.right;playerState.rectTransform.sizeDelta=new Vector2(0,30);
            var help=Label("输入提示",column,Vector2.zero,Vector2.zero,new Vector2(0,-24),new Vector2(730,32),"Enter 发送    ·    滚动查看    ·    Esc 返回",18,Muted);help.rectTransform.anchorMax=Vector2.right;help.rectTransform.sizeDelta=new Vector2(0,32);
            var close=UIPrimitives.Button("结束交谈",playerPanel,Vector2.one,Vector2.one,new Vector2(-42,-40),new Vector2(56,56),"",()=>memory.Conversations.Cancel("玩家结束交谈"));((RoundedPanel)close.targetGraphic).CornerRadius=28;
            var icon=Stretch("Close icon",close.transform,new Vector2(14,14),new Vector2(-14,-14)).gameObject.AddComponent<LineIcon>();icon.symbol=LineIcon.Symbol.Close;icon.color=Paper;icon.raycastTarget=false;
            Label("Return hint",close.transform,new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-12),new Vector2(130,30),"Esc 返回",16,Muted,TextAlignmentOptions.Top);
            playerGroup.alpha=0;playerPanel.gameObject.SetActive(false);
        }
        static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        Button ButtonAt(string caption,Transform parent,Vector2 position,Vector2 size,Action action)
        {
            var b=UIPrimitives.Button(caption,parent,Vector2.up,Vector2.up,position,size,caption,action,false,18);b.GetComponentInChildren<TextMeshProUGUI>().alignment=TextAlignmentOptions.Center;return b;
        }
        static bool IsComposing(){var system=EventSystem.current?.currentInputModule?.input;return !string.IsNullOrEmpty(system!=null?system.compositionString:Input.compositionString);}
        void Send()
        {
            if(IsComposing()){StartCoroutine(ResumeInput());return;}
            if(!PlayerInputReady||memory.Conversations==null||!memory.Client.CanGenerate||memory.Conversations.Sending||string.IsNullOrWhiteSpace(input.text))return;
            FinishTyping();string value=input.text;input.text="";memory.Conversations.SendPlayer(value);input.ActivateInputField();
        }
        IEnumerator ResumeInput(){yield return null;if(PlayerInputReady&&memory.PlayerChatOpen)input.ActivateInputField();}
        public void OpenPlayer(string actorId)
        {
            if(transition!=null)StopCoroutine(transition);Closing=false;entering=true;selectedActor=actorId;history.Clear();typing=false;
            historyText.text="在下方输入，按 Enter 开始交谈。";historyText.maxVisibleCharacters=int.MaxValue;playerTitle.text=SevenCharacterCatalog.Name(actorId);
            locationText.text=GameUI.PlaceName(session.CurrentPlace)+"  ·  "+GameUI.PeriodName(session.Clock.Period);
            playerPanel.gameObject.SetActive(true);speechPanel.gameObject.SetActive(false);playerGroup.alpha=0;playerGroup.interactable=false;input.text="";
            var definition=SevenCharacterCatalog.Find(actorId);var profile=!string.IsNullOrEmpty(definition?.portrait)?Resources.Load<Live2DModelProfile>(definition.portrait):null;
            portrait.gameObject.SetActive(profile!=null);if(profile!=null)character.Show(profile);else character.Hide();
            RefreshHistoryLayout();transition=StartCoroutine(AnimatePlayer(true));
        }
        IEnumerator AnimatePlayer(bool opening)
        {
            float start=playerGroup.alpha,end=opening?1:0,duration=opening?Style.DialogueEnterDuration:Style.DialogueExitDuration;
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/Mathf.Max(.01f,duration)){SetPose(Mathf.Lerp(start,end,t*t*(3-2*t)));yield return null;}
            SetPose(end);transition=null;entering=false;Closing=false;
            if(opening){playerGroup.interactable=true;input.ActivateInputField();}
            else{playerPanel.gameObject.SetActive(false);character.Hide();}
        }
        void SetPose(float alpha){playerGroup.alpha=alpha;portrait.anchoredPosition=Vector2.left*(1-alpha)*34;portrait.localScale=Vector3.one*Mathf.Lerp(.975f,1,alpha);column.anchoredPosition=Vector2.right*(1-alpha)*18;}
        public void HidePlayer()
        {
            if(playerPanel==null||!playerPanel.gameObject.activeSelf)return;
            if(transition!=null)StopCoroutine(transition);Closing=true;entering=false;playerGroup.interactable=false;input.DeactivateInputField();FinishTyping();transition=StartCoroutine(AnimatePlayer(false));
        }
        void RefreshHistoryLayout(){Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(content);historyText.ForceMeshUpdate();historyScroll.verticalNormalizedPosition=0;}
        void FinishTyping(){typing=false;if(historyText!=null)historyText.maxVisibleCharacters=int.MaxValue;if(character!=null)character.SetState("neutral",false);}
        public void ShowLine(string speaker,string text,Vector3 position,bool playerConversation)
        {
            if(playerConversation)
            {
                FinishTyping();history.Add(SevenCharacterCatalog.Name(speaker)+"：\n"+text);while(history.Count>12)history.RemoveAt(0);
                string prefix=history.Count>1?string.Join("\n\n",history.Take(history.Count-1))+"\n\n":"";
                historyText.text=string.Join("\n\n",history);lastSpeaker=speaker;revealed=prefix.Length;
                typing=speaker!="player";historyText.maxVisibleCharacters=typing?Mathf.FloorToInt(revealed):int.MaxValue;RefreshHistoryLayout();return;
            }
            speechTitle.text=SevenCharacterCatalog.Name(speaker);speechText.text=text;speechTitle.color=SevenCharacterCatalog.Find(speaker)?.Accent??Paper;
            speechUntil=Time.unscaledTime+Mathf.Clamp(1.3f+text.Length*.05f,2.5f,6);speechPanel.gameObject.SetActive(true);
        }
        public void ShowDay4(Action shown){if(day4Shown!=null||Day4Visible)return;day4Panel.gameObject.SetActive(true);day4Until=Time.unscaledTime+9;day4Frame=Time.frameCount;day4Shown=shown;}
        public void ResetView()
        {
            StopAllCoroutines();transition=null;Closing=entering=false;history.Clear();FinishTyping();playerPanel.gameObject.SetActive(false);playerGroup.alpha=0;character.Hide();speechPanel.gameObject.SetActive(false);day4Panel.gameObject.SetActive(false);day4Shown=null;
        }
        void Update()
        {
            if(memory?.Client==null||root==null)return;
            bool fullView=playerPanel.gameObject.activeSelf,overlay=GameUI.Instance!=null&&(GameUI.Instance.HasOverlay||GameUI.Instance.IsOverlayTransitioning);
            statusPanel.gameObject.SetActive(!fullView&&!overlay);
            statusText.text="十名岛民 · 部分形象占位\n"+memory.Client.Status+(string.IsNullOrEmpty(memory.Client.LastError)?"":"\n"+memory.Client.LastError);
            if(speechPanel.gameObject.activeSelf&&(Time.unscaledTime>speechUntil||fullView||overlay))speechPanel.gameObject.SetActive(false);
            if(Day4Visible&&Time.unscaledTime>day4Until)day4Panel.gameObject.SetActive(false);
            if(day4Shown!=null&&Time.frameCount>day4Frame+1){var shown=day4Shown;day4Shown=null;shown();}
            if(PlayerInputReady&&GameInput.Pressed(Key.Escape))memory.Conversations.Cancel("玩家结束交谈");
            send.interactable=PlayerInputReady&&memory.PlayerChatOpen&&memory.Client.CanGenerate&&!memory.Conversations.Sending;
            playerState.text=memory.Conversations.Sending?"正在等待对方回应…":memory.Client.Status;
            if(!string.IsNullOrEmpty(memory.Client.LastError))playerState.text+=" · "+memory.Client.LastError;
            playerState.color=memory.Client.CanGenerate?Muted:Accent;
            if(typing&&PlayerInputReady){revealed+=Time.unscaledDeltaTime*Style.TypewriterSpeed;historyText.maxVisibleCharacters=Mathf.FloorToInt(revealed);if(historyText.maxVisibleCharacters>=historyText.textInfo.characterCount)FinishTyping();else character.SetState("neutral",lastSpeaker==selectedActor);}
            foreach(var dead in names.Keys.Where(n=>n==null).ToArray()){if(names[dead]!=null)Destroy(names[dead].gameObject);names.Remove(dead);}
            foreach(var npc in memory.Spawner.Actors)
            {
                if(npc==null)continue;
                if(!npc.IsPresent||world.Follow==null){if(names.TryGetValue(npc,out var hidden))hidden.gameObject.SetActive(false);continue;}
                if(!names.TryGetValue(npc,out var label))
                {
                    label=Label("NPC姓名",root,Vector2.zero,Vector2.zero,Vector2.zero,new Vector2(240,52),"",18,Paper,TextAlignmentOptions.Center);label.richText=false;
                    label.text=SevenCharacterCatalog.Name(npc.npcId)+"\n"+(SevenCharacterCatalog.Find(npc.npcId)?.occupation??"");label.color=SevenCharacterCatalog.Find(npc.npcId)?.Accent??Paper;names[npc]=label;
                    label.outlineWidth=.16f;label.outlineColor=new Color(0,0,0,.9f);label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,.5f);label.rectTransform.pivot=new Vector2(.5f,0);
                }
                var worldCamera=world.Follow.GetComponent<Camera>();var p=worldCamera.WorldToScreenPoint(npc.transform.position+Vector3.up*3.45f);
                label.gameObject.SetActive(!fullView&&!overlay&&p.z>0&&worldCamera.pixelRect.Contains(p));var canvas=root.GetComponent<Canvas>();
                if(RectTransformUtility.ScreenPointToLocalPointInRectangle(root,p,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var local))label.rectTransform.anchoredPosition=local;
            }
        }
    }
}
