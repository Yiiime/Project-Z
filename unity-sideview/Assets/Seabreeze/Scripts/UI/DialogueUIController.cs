using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    // Owns presentation and input only. DialogueRunner remains the sole owner of graph/effects.
    public sealed class DialogueUIController : MonoBehaviour
    {
        GameSession session;
        DialogueFlowController flow;
        RectTransform root, portrait, column, content, choices;
        CanvasGroup group, lineGroup, choicesGroup;
        TextMeshProUGUI npcName, location, speaker, body, hint, missing;
        Live2DCharacterView character;
        Button continueButton, returnButton;
        readonly List<Button> buttons = new List<Button>();
        Coroutine transition, lineTransition;
        float revealed;
        int lineFrame;
        int inputFrame = -1;
        bool fullyRevealed, changingLine;
        string shownNode = "";
        Vector2 portraitRest, columnRest;
        UIStyleConfig style;
        public bool IsVisible => root && root.gameObject.activeSelf;

        public void Initialize(GameSession owner, RectTransform canvasRect)
        {
            session = owner; flow = owner.Conversation; style = UIStyleConfig.Current;
            root = Stretch("Conversation experience", canvasRect, Vector2.zero, Vector2.zero);
            group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0;
            var shade = Stretch("Scene dim", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            shade.color = style.Scrim; shade.raycastTarget = true;
            portrait = Region("Live2D portrait viewport", root, new Vector2(.025f,.015f), new Vector2(.51f,.95f));
            portrait.gameObject.AddComponent<RectMask2D>();
            character = portrait.gameObject.AddComponent<Live2DCharacterView>(); character.Initialize(portrait);
            missing = Label("Unbound character", portrait, new Vector2(.5f,.2f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(540,85), "", 20, Muted, TextAlignmentOptions.Center);
            column = Region("Conversation column", root, new Vector2(.545f,.12f), new Vector2(.925f,.87f));
            npcName = Label("Conversation partner", column, new Vector2(0,1), new Vector2(0,1), Vector2.zero, new Vector2(690,58), "", 42, Paper);
            location = Label("Place and time", column, new Vector2(0,1), new Vector2(0,1), new Vector2(0,-68), new Vector2(690,36), "", 20, Muted);
            var line = Stretch("Conversation separator", column, Vector2.zero, Vector2.zero);
            line.anchorMin = new Vector2(0,1); line.anchorMax = Vector2.one; line.pivot = new Vector2(.5f,1);
            line.anchoredPosition = new Vector2(0,-120); line.sizeDelta = new Vector2(0,1);
            line.gameObject.AddComponent<Image>().color = style.PanelBorder;
            var viewport = Region("Conversation scroll viewport", column, Vector2.zero, new Vector2(1,1));
            viewport.offsetMin = new Vector2(-5,65); viewport.offsetMax = new Vector2(5,-145);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 34; scroll.viewport = viewport;
            content = Region("Conversation content", viewport, new Vector2(0,1), Vector2.one);
            content.pivot = new Vector2(.5f,1); content.sizeDelta = new Vector2(0,0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(5,5,5,5); layout.spacing = 18; layout.childControlWidth = true; layout.childForceExpandWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            var bubble = Panel("Dialogue bubble", content, Vector2.zero, new Vector2(0,1), Vector2.zero, new Vector2(690,220), style.PanelFill, true);
            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>(); bubbleLayout.padding = new RectOffset(30,30,25,25); bubbleLayout.spacing = 15; bubbleLayout.childControlWidth = true; bubbleLayout.childControlHeight = true; bubbleLayout.childForceExpandHeight = false;
            lineGroup = bubble.gameObject.AddComponent<CanvasGroup>();
            speaker = Label("Line speaker", bubble.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, "", 20, Cool);
            speaker.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            body = Label("Spoken text", bubble.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, "", style.DialogueBodySize, Paper);
            body.lineSpacing = 12; body.gameObject.AddComponent<LayoutElement>().minHeight = 90;
            var click = bubble.gameObject.AddComponent<Button>(); click.transition = Selectable.Transition.None; click.onClick.AddListener(Continue); click.navigation = new Navigation { mode = Navigation.Mode.None };
            choices = Rect("Dialogue choices", content, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var choiceLayout = choices.gameObject.AddComponent<VerticalLayoutGroup>(); choiceLayout.spacing = 12; choiceLayout.childControlWidth = true; choiceLayout.childControlHeight = true; choiceLayout.childForceExpandHeight = false;
            choicesGroup = choices.gameObject.AddComponent<CanvasGroup>();
            continueButton = Button("Continue conversation", column, new Vector2(1,0), new Vector2(1,0), Vector2.zero, new Vector2(276,48), "继续  →", Continue, false, 21);
            hint = Label("Continue hint", column, Vector2.zero, Vector2.zero, new Vector2(0,-37), new Vector2(730,30), "", 18, Muted);
            foreach (var label in new[] { npcName, location, hint })
            {
                label.rectTransform.anchorMax = new Vector2(1,label.rectTransform.anchorMax.y);
                label.rectTransform.sizeDelta = new Vector2(0,label.rectTransform.sizeDelta.y);
            }
            returnButton = Button("Return to the street", root, new Vector2(1,1), new Vector2(1,1), new Vector2(-42,-40), new Vector2(56,56), "", Return, false, 20);
            ((RoundedPanel)returnButton.targetGraphic).CornerRadius = 28;
            var close = Stretch("Close icon",returnButton.transform,new Vector2(14,14),new Vector2(-14,-14)).gameObject.AddComponent<LineIcon>(); close.symbol = LineIcon.Symbol.Close; close.color = Paper; close.raycastTarget = false;
            Label("Return hint", returnButton.transform, new Vector2(.5f,0), new Vector2(.5f,1), new Vector2(0,-12), new Vector2(130,30), "Esc 返回", 16, Muted, TextAlignmentOptions.Top);
            portraitRest = portrait.anchoredPosition; columnRest = column.anchoredPosition;
            root.gameObject.SetActive(false);
            flow.Changed += OnFlowChanged; owner.Dialogue.Changed += OnLineChanged;
            OnFlowChanged();
        }
        static RectTransform Region(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var rt = Stretch(name,parent,Vector2.zero,Vector2.zero); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; return rt;
        }
        void OnFlowChanged()
        {
            if (flow.State == DialogueFlowController.Phase.Entering)
            {
                if (transition != null) StopCoroutine(transition);
                root.gameObject.SetActive(true); root.SetAsLastSibling();
                bool npc = flow.IsNpcConversation;
                portrait.gameObject.SetActive(npc);
                column.anchorMin = new Vector2(npc ? .545f : .27f,.12f);
                column.anchorMax = new Vector2(npc ? .925f : .73f,.87f);
                column.offsetMin = column.offsetMax = Vector2.zero; columnRest = column.anchoredPosition;
                npcName.text = npc ? flow.ActiveNpc.displayName : "海风里的片刻";
                location.text = GameUI.PlaceName(session.CurrentPlace)+"  ·  "+GameUI.PeriodName(session.Clock.Period);
                if (npc && flow.ActiveNpc.live2DProfile)
                {
                    missing.gameObject.SetActive(false);
                    try { character.Show(flow.ActiveNpc.live2DProfile); }
                    catch (System.Exception error)
                    {
                        character.Hide();
                        missing.text = flow.ActiveNpc.displayName+"\n动态角色暂时无法显示";
                        missing.gameObject.SetActive(true);
                        Debug.LogException(error);
                    }
                }
                else
                {
                    character.Hide(); missing.gameObject.SetActive(npc);
                    missing.text = npc ? flow.ActiveNpc.displayName+"\n动态角色尚未绑定" : "";
                }
                SetLine(); transition = StartCoroutine(Enter(flow.Sequence));
            }
            else if (flow.State == DialogueFlowController.Phase.Exiting)
            {
                if (transition != null) StopCoroutine(transition);
                if (lineTransition != null) StopCoroutine(lineTransition);
                changingLine = false;
                SetTalking(false);
                transition = StartCoroutine(Exit(flow.Sequence));
            }
            else if (!flow.IsActive)
            {
                if (transition != null) StopCoroutine(transition);
                if (lineTransition != null) StopCoroutine(lineTransition);
                changingLine = false;
                root.gameObject.SetActive(false); character.Hide(); shownNode = "";
            }
            group.interactable = flow.CanAdvance; group.blocksRaycasts = flow.IsActive;
        }
        IEnumerator Enter(int token)
        {
            group.alpha = 0;
            while (WorldDirector.Instance == null || WorldDirector.Instance.Stage == null || WorldDirector.Instance.IsTransitioning) yield return null;
            location.text = GameUI.PlaceName(session.CurrentPlace)+"  ·  "+GameUI.PeriodName(session.Clock.Period);
            for (float t=0;t<1;t+=Time.unscaledDeltaTime/Mathf.Max(.1f,style.DialogueEnterDuration))
            {
                float p = 1-Mathf.Pow(1-t,3); SetPose(p); yield return null;
            }
            SetPose(1); transition = null; lineFrame = Time.frameCount; flow.CompleteEnter(token);
        }
        IEnumerator Exit(int token)
        {
            float start = group.alpha;
            for (float t=0;t<1;t+=Time.unscaledDeltaTime/Mathf.Max(.1f,style.DialogueExitDuration))
            {
                float p = t*t*(3-2*t); SetPose(start*(1-p)); yield return null;
            }
            SetPose(0); character.Hide(); root.gameObject.SetActive(false); shownNode = ""; transition = null; flow.CompleteExit(token);
        }
        void SetPose(float alpha)
        {
            group.alpha = alpha;
            portrait.anchoredPosition = portraitRest+Vector2.left*(1-alpha)*34;
            portrait.localScale = Vector3.one*Mathf.Lerp(.975f,1,alpha);
            column.anchoredPosition = columnRest+Vector2.right*(1-alpha)*18;
        }
        void OnLineChanged()
        {
            if (!session.Dialogue.IsOpen || session.Dialogue.CurrentNodeId == shownNode) return;
            if (flow.State == DialogueFlowController.Phase.Entering) { SetLine(); return; }
            if (lineTransition != null) StopCoroutine(lineTransition);
            lineTransition = StartCoroutine(ChangeLine());
        }
        IEnumerator ChangeLine()
        {
            changingLine = true; SetTalking(false); choicesGroup.interactable = false;
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/.09f) { lineGroup.alpha=1-t; yield return null; }
            SetLine();
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/.13f) { lineGroup.alpha=t; yield return null; }
            lineGroup.alpha=1; changingLine=false; lineFrame=Time.frameCount; lineTransition=null;
        }
        void SetLine()
        {
            var runner = session.Dialogue; if (!runner.IsOpen) return;
            shownNode = runner.CurrentNodeId; lineFrame = Time.frameCount;
            speaker.text = runner.Speaker; body.text = runner.Text; body.maxVisibleCharacters=0;
            revealed=0; fullyRevealed=false; lineGroup.alpha=1;
            Clear(choices); buttons.Clear();
            for(int i=0;i<runner.Choices.Count;i++)
            {
                int index=i;
                var b=Button("Dialogue choice "+(i+1),choices,Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(690,72),(i+1)+"   "+runner.Choices[i].text,()=>Choose(index),false,23);
                var label=b.GetComponentInChildren<TextMeshProUGUI>();
                var rt=label.rectTransform;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=new Vector2(24,12);rt.offsetMax=new Vector2(-24,-12);
                float width=Mathf.Max(330,column.rect.width-58);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight=Mathf.Max(68,label.GetPreferredValues(label.text,width,0).y+30);
                buttons.Add(b);
            }
            choicesGroup.alpha=0;choicesGroup.interactable=false;choicesGroup.blocksRaycasts=false;
            continueButton.gameObject.SetActive(true);
            continueButton.GetComponentInChildren<TextMeshProUGUI>().text="显示全文  →";
            hint.text="E / 空格 / 点击文字  显示全文";
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(content); body.ForceMeshUpdate();
            var scroll=content.parent.GetComponent<ScrollRect>();if(scroll)scroll.verticalNormalizedPosition=1;
            SetTalking(false);
        }
        void Update()
        {
            if (!session || !flow.CanAdvance || changingLine) return;
            if (!fullyRevealed)
            {
                revealed+=Time.unscaledDeltaTime*style.TypewriterSpeed;
                body.maxVisibleCharacters=Mathf.FloorToInt(revealed);
                if (body.maxVisibleCharacters>=body.textInfo.characterCount) Reveal();
                else SetTalking(true);
            }
            choicesGroup.alpha=Mathf.MoveTowards(choicesGroup.alpha,fullyRevealed?1:0,Time.unscaledDeltaTime*7);
            if(Time.frameCount<=lineFrame)return;
            if(GameInput.Pressed(Key.E)||GameInput.Pressed(Key.Space)||GameInput.Pressed(Key.Enter))Continue();
            if(GameInput.Pressed(Key.Digit1))Choose(0);
            if(GameInput.Pressed(Key.Digit2))Choose(1);
            if(GameInput.Pressed(Key.Digit3))Choose(2);
            if(GameInput.Pressed(Key.Escape))Return();
        }
        void Reveal()
        {
            fullyRevealed=true;body.maxVisibleCharacters=int.MaxValue;
            choicesGroup.interactable=true;choicesGroup.blocksRaycasts=true;
            continueButton.GetComponentInChildren<TextMeshProUGUI>().text="继续  →";
            continueButton.gameObject.SetActive(buttons.Count==0);
            hint.text=buttons.Count>0?"1 / 2 / 3 或点击选项    ·    滚动查看":"E / 空格 / 点击文字  继续    ·    Esc 返回";
            SetTalking(false);
        }
        void Continue()
        {
            if(!flow.CanAdvance||changingLine||Time.frameCount<=lineFrame||inputFrame==Time.frameCount)return;
            inputFrame=Time.frameCount;
            if(!fullyRevealed){Reveal();return;}
            if(buttons.Count==0)session.Dialogue.Advance();
        }
        void Choose(int index)
        {
            if(!flow.CanAdvance||changingLine||!fullyRevealed||Time.frameCount<=lineFrame||index>=buttons.Count||inputFrame==Time.frameCount)return;
            inputFrame=Time.frameCount;
            buttons[index].Select();
            session.Dialogue.Choose(index);
        }
        void Return() { if(flow.CanAdvance&&!changingLine)flow.RequestReturn(); }
        void SetTalking(bool talking)
        {
            bool isNpcLine=flow.ActiveNpc && (session.Dialogue.SpeakerId==flow.ActiveNpc.id || (string.IsNullOrEmpty(session.Dialogue.SpeakerId)&&session.Dialogue.Speaker==flow.ActiveNpc.displayName));
            character.SetState(session.Dialogue.Emotion,talking&&isNpcLine,session.Dialogue.Motion);
        }
        void OnDestroy()
        {
            if(flow)flow.Changed-=OnFlowChanged;
            if(session && session.Dialogue)session.Dialogue.Changed-=OnLineChanged;
        }
    }
}
