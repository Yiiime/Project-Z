using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace Seabreeze
{
    public sealed class MemoryDialogueView : MonoBehaviour
    {
        MemoryGameController memory;GameSession session;WorldDirector world;
        RectTransform root,statusPanel,speechPanel,playerPanel,day4Panel;
        TextMeshProUGUI statusText,speechTitle,speechText,playerTitle,historyText;
        TMP_InputField input;Button send;
        readonly List<string> history=new List<string>();
        readonly Dictionary<NpcPresence,TextMeshProUGUI> names=new Dictionary<NpcPresence,TextMeshProUGUI>();
        TMP_FontAsset font;float speechUntil,day4Until;
        Action day4Shown;int day4Frame;
        public bool Day4Visible => day4Panel!=null&&day4Panel.gameObject.activeSelf;
        public string VisibleSpeech => speechText!=null&&speechPanel.gameObject.activeSelf?speechTitle.text+"："+speechText.text:"";
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w)
        {
            memory=m;session=s;world=w;font=Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            var canvas=new GameObject("七人记忆界面",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform,false);canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=150;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            root=canvas.GetComponent<RectTransform>();
            statusPanel=Panel("记忆状态",root,new Vector2(1,1),new Vector2(1,1),new Vector2(-26,-132),new Vector2(380,154));
            statusText=Label("状态",statusPanel,new Vector2(18,-14),new Vector2(344,68),21);
            ButtonAt("模型设置",statusPanel,new Vector2(16,-104),new Vector2(116,36),()=>Application.OpenURL(MemoryRuntimeConfig.Settings.serviceUrl+"/settings"));
            ButtonAt("保存",statusPanel,new Vector2(146,-104),new Vector2(96,36),()=>{if(session.Save())session.Story.Notify("七人试玩进度已保存。");});
            ButtonAt("读档",statusPanel,new Vector2(256,-104),new Vector2(104,36),()=>session.LoadSavedGame());
            speechPanel=Panel("附近的交谈",root,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,32),new Vector2(1060,200));
            speechTitle=Label("说话者",speechPanel,new Vector2(26,-16),new Vector2(1008,35),25);
            speechText=Label("台词",speechPanel,new Vector2(26,-60),new Vector2(1008,122),25);speechText.enableAutoSizing=true;speechText.fontSizeMin=18;speechText.fontSizeMax=25;
            speechPanel.gameObject.SetActive(false);
            playerPanel=Panel("玩家交谈",root,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,28),new Vector2(1120,340));
            playerTitle=Label("交谈对象",playerPanel,new Vector2(26,-12),new Vector2(1000,35),26);
            historyText=Label("交谈记录",playerPanel,new Vector2(26,-58),new Vector2(1068,182),22);historyText.enableAutoSizing=true;historyText.fontSizeMin=16;historyText.fontSizeMax=22;
            var field=Panel("输入",playerPanel,new Vector2(0,1),new Vector2(0,1),new Vector2(26,-258),new Vector2(860,54));
            field.GetComponent<Image>().color=new Color(.92f,.95f,.91f,.98f);
            input=field.gameObject.AddComponent<TMP_InputField>();input.characterLimit=300;input.lineType=TMP_InputField.LineType.SingleLine;
            var area=Rect("Text Area",field,new Vector2(0,1),new Vector2(0,1),new Vector2(12,-8),new Vector2(836,38));
            var entered=Label("Text",area,Vector2.zero,new Vector2(836,38),22);entered.color=new Color(.07f,.14f,.19f);
            input.textViewport=area;input.textComponent=entered;
            var hint=Label("Placeholder",area,Vector2.zero,new Vector2(836,38),21);hint.text="输入要对这位岛民说的话，Enter 发送";hint.color=new Color(.3f,.4f,.4f,.8f);input.placeholder=hint;
            input.onSubmit.AddListener(_=>Send());
            send=ButtonAt("发送",playerPanel,new Vector2(904,-258),new Vector2(88,54),Send);
            ButtonAt("结束",playerPanel,new Vector2(1002,-258),new Vector2(90,54),()=>memory.Conversations.Cancel("玩家结束交谈"));
            playerPanel.gameObject.SetActive(false);
            day4Panel=Panel("Day4全岛公告",root,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-160),new Vector2(790,156));
            var title=Label("公告标题",day4Panel,new Vector2(28,-20),new Vector2(734,34),26);title.text="DAY 4 · 全岛公告";title.color=new Color(1,.79f,.42f);
            var notice=Label("公告内容",day4Panel,new Vector2(28,-68),new Vector2(734,64),34);notice.text="小岛将要毁灭。";
            day4Panel.gameObject.SetActive(false);
        }
        void Send()
        {
            if(memory.Conversations==null||!memory.Client.CanGenerate||memory.Conversations.Sending||string.IsNullOrWhiteSpace(input.text))return;
            string value=input.text;input.text="";memory.Conversations.SendPlayer(value);input.ActivateInputField();
        }
        public void OpenPlayer(string actorId)
        {
            history.Clear();historyText.text="走近后，你可以和这位岛民交流。";playerTitle.text="与"+SevenCharacterCatalog.Name(actorId)+"交谈";
            playerPanel.gameObject.SetActive(true);speechPanel.gameObject.SetActive(false);input.text="";input.ActivateInputField();
        }
        public void HidePlayer(){if(playerPanel!=null)playerPanel.gameObject.SetActive(false);}
        public void ShowLine(string speaker,string text,Vector3 position,bool playerConversation)
        {
            if(playerConversation)
            {
                history.Add(SevenCharacterCatalog.Name(speaker)+"："+text);while(history.Count>3)history.RemoveAt(0);
                historyText.text=string.Join("\n",history);return;
            }
            speechTitle.text=SevenCharacterCatalog.Name(speaker);speechText.text=text;
            speechTitle.color=SevenCharacterCatalog.Find(speaker)?.Accent??Color.white;
            speechUntil=Time.unscaledTime+Mathf.Clamp(1.3f+text.Length*.05f,2.5f,6);
            speechPanel.gameObject.SetActive(true);
        }
        public void ShowDay4(Action shown)
        {
            if(day4Shown!=null||Day4Visible)return;
            day4Panel.gameObject.SetActive(true);day4Until=Time.unscaledTime+9;day4Frame=Time.frameCount;day4Shown=shown;
        }
        public void ResetView(){history.Clear();HidePlayer();speechPanel.gameObject.SetActive(false);day4Panel.gameObject.SetActive(false);day4Shown=null;}
        void Update()
        {
            if(memory?.Client==null||root==null)return;
            statusText.text="七人记忆 · 形象占位\n"+memory.Client.Status+(string.IsNullOrEmpty(memory.Client.LastError)?"":"\n"+memory.Client.LastError);
            statusText.enableAutoSizing=true;statusText.fontSizeMin=14;statusText.fontSizeMax=21;
            if(speechPanel.gameObject.activeSelf&&Time.unscaledTime>speechUntil)speechPanel.gameObject.SetActive(false);
            if(Day4Visible&&Time.unscaledTime>day4Until)day4Panel.gameObject.SetActive(false);
            if(day4Shown!=null&&Time.frameCount>day4Frame+1){var shown=day4Shown;day4Shown=null;shown();}
            if(memory.PlayerChatOpen&&GameInput.Pressed(Key.Escape))memory.Conversations.Cancel("玩家结束交谈");
            send.interactable=memory.PlayerChatOpen&&memory.Client.CanGenerate&&!memory.Conversations.Sending;
            foreach(var dead in names.Keys.Where(n=>n==null).ToArray()){if(names[dead]!=null)Destroy(names[dead].gameObject);names.Remove(dead);}
            foreach(var npc in memory.Spawner.Actors)
            {
                if(npc==null||!npc.IsPresent||world.Follow==null)continue;
                if(!names.TryGetValue(npc,out var label))
                {
                    label=Label("NPC姓名",root,Vector2.zero,new Vector2(240,52),18);label.alignment=TextAlignmentOptions.Center;
                    label.text=SevenCharacterCatalog.Name(npc.npcId)+"\n"+(SevenCharacterCatalog.Find(npc.npcId)?.occupation??"");label.color=SevenCharacterCatalog.Find(npc.npcId)?.Accent??Color.white;names[npc]=label;
                    label.outlineWidth=.16f;label.outlineColor=new Color(0,0,0,.9f);
                    label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,.5f);label.rectTransform.pivot=new Vector2(.5f,0);
                }
                var worldCamera=world.Follow.GetComponent<Camera>();
                var p=worldCamera.WorldToScreenPoint(npc.transform.position+Vector3.up*3.45f);
                label.gameObject.SetActive(p.z>0&&worldCamera.pixelRect.Contains(p));
                var canvas=root.GetComponent<Canvas>();
                if(RectTransformUtility.ScreenPointToLocalPointInRectangle(root,p,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var local))label.rectTransform.anchoredPosition=local;
            }
        }
        RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
        {
            var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);var r=g.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=position;r.sizeDelta=size;return r;
        }
        RectTransform Panel(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
        {
            var r=Rect(name,parent,anchor,pivot,position,size);var image=r.gameObject.AddComponent<Image>();image.color=new Color(.035f,.075f,.13f,.94f);return r;
        }
        TextMeshProUGUI Label(string name,Transform parent,Vector2 position,Vector2 size,float fontSize)
        {
            var r=Rect(name,parent,new Vector2(0,1),new Vector2(0,1),position,size);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=fontSize;t.color=new Color(.91f,.94f,.92f);t.richText=false;t.raycastTarget=false;t.text="";t.enableWordWrapping=true;return t;
        }
        Button ButtonAt(string caption,Transform parent,Vector2 position,Vector2 size,Action click)
        {
            var r=Panel(caption,parent,new Vector2(0,1),new Vector2(0,1),position,size);r.GetComponent<Image>().color=new Color(.2f,.39f,.39f,.98f);
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=r.GetComponent<Image>();b.onClick.AddListener(()=>click());b.navigation=new Navigation { mode=Navigation.Mode.None };
            var label=Label("文字",r,Vector2.zero,size,20);label.alignment=TextAlignmentOptions.Center;label.text=caption;return b;
        }
    }
}
