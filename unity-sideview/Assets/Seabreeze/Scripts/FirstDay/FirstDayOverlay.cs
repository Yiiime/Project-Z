using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    // Small cinematic invitations above the existing livestream; does not replace its gameplay.
    public sealed class FirstDayOverlay : MonoBehaviour
    {
        RectTransform root;
        GameObject card;
        TextMeshProUGUI body, actionLabel;
        Button accept;
        public bool Visible => card != null;
        public RectTransform Surface => root;
        public bool IsModal { get; private set; }
        public void Initialize()
        {
            var go = new GameObject("First day invitations", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false); root = (RectTransform)go.transform;
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            go.GetComponent<Canvas>().sortingOrder = 650;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
        }
        public void Show(string title, string text, string action, Action callback, Action dismiss = null, bool compact = false)
        {
            if (!compact) { ShowLetter(title,text,action,callback,dismiss); return; }
            Hide(); IsModal = !compact;
            var parent = root;
            if (!compact)
            {
                var veil = Panel("Invitation veil", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.03f,.06f,.12f,.68f), true);
                veil.rectTransform.anchorMax = Vector2.one; veil.rectTransform.offsetMin = veil.rectTransform.offsetMax = Vector2.zero;
                card = veil.gameObject; parent = veil.rectTransform;
            }
            var panel = Panel("A letter from the island",parent, compact ? Vector2.zero : Vector2.one*.5f, compact ? Vector2.zero : Vector2.one*.5f,
                compact ? new Vector2(32,118) : Vector2.zero, compact ? new Vector2(620,184) : new Vector2(780,450),new Color(.075f,.14f,.23f,.96f),true);
            ((RoundedPanel)panel).CornerRadius=24;((RoundedPanel)panel).SetBorder(new Color(.75f,.82f,.89f,.55f),1);
            if (compact) card=panel.gameObject;
            Label("Title",panel.transform,Vector2.up,Vector2.up,new Vector2(30,-23),new Vector2(compact?550:710,50),title,compact?24:33,new Color(.97f,.85f,.66f));
            body=Label("Letter",panel.transform,Vector2.up,Vector2.up,new Vector2(30,compact?-66:-102),new Vector2(compact?550:710,compact?50:210),text,compact?18:24,new Color(.88f,.93f,.97f));body.richText=false;
            accept=Button("Accept invitation",panel.transform,compact?Vector2.right:Vector2.zero,compact?Vector2.right:Vector2.zero,new Vector2(compact?-24:30,compact?18:28),new Vector2(compact?280:440,compact?44:62),action,()=>{Hide();callback?.Invoke();},true,compact?19:23);
            actionLabel=accept.GetComponentInChildren<TextMeshProUGUI>();
            if(dismiss!=null)Button("Later",panel.transform,Vector2.right,Vector2.right,new Vector2(-30,28),new Vector2(230,62),"再待一会儿",()=>{Hide();dismiss();},false,21);
        }
        void ShowLetter(string title, string text, string action, Action callback, Action dismiss)
        {
            Hide(); IsModal = true;
            var veil = Panel("Invitation veil",root,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.one,new Color(.025f,.035f,.09f,.65f),true);
            veil.rectTransform.anchorMax = Vector2.one; veil.rectTransform.offsetMin = veil.rectTransform.offsetMax = Vector2.zero;
            card = veil.gameObject;
            var panel = Panel("A letter from the island",veil.transform,Vector2.one*.5f,Vector2.one*.5f,Vector2.zero,new Vector2(760,510),new Color(.98f,.96f,.92f,1),true);
            ((RoundedPanel)panel).CornerRadius=26;
            ((RoundedPanel)panel).SetBorder(new Color(.70f,.58f,.79f),2);
            var art = Resources.Load<Texture2D>("FirstDay/InvitationPaper");
            if (art != null) {
                panel.color=Color.clear;
                var image=Stretch("Illustrated invitation",panel.transform,Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
                image.texture=art; image.raycastTarget=false;
            }
            var ink=new Color(.14f,.23f,.31f);
            Label("Letter eyebrow",panel.transform,Vector2.up,Vector2.up,new Vector2(150,-48),new Vector2(550,34),"来自月亮岛的一封信",17,new Color(.48f,.38f,.56f));
            var heading=Label("Title",panel.transform,Vector2.up,Vector2.up,new Vector2(150,-83),new Vector2(550,80),title,32,ink);
            heading.richText=false; heading.enableAutoSizing=true; heading.fontSizeMin=22;heading.fontSizeMax=32;heading.overflowMode=TextOverflowModes.Ellipsis;
            Label("Recipient",panel.transform,Vector2.up,Vector2.up,new Vector2(60,-170),new Vector2(590,40),"致  "+PlayerIdentity.DisplayName,25,ink).richText=false;
            body=Label("Letter",panel.transform,Vector2.up,Vector2.up,new Vector2(60,-222),new Vector2(550,150),text,22,ink);
            body.richText=false; body.enableAutoSizing=true;body.fontSizeMin=17;body.fontSizeMax=22;body.overflowMode=TextOverflowModes.Ellipsis;
            Label("Signature",panel.transform,Vector2.up,Vector2.up,new Vector2(60,-382),new Vector2(590,36),"七天小岛生活  ·  邀请人 Z",17,new Color(.39f,.40f,.49f));
            float width=dismiss==null?640:390;
            accept=Button("Accept invitation",panel.transform,Vector2.zero,Vector2.zero,new Vector2(dismiss==null?60:310,38),new Vector2(width,58),action,()=>{Hide();callback?.Invoke();},true,23);
            Decorate(accept,true); actionLabel=accept.GetComponentInChildren<TextMeshProUGUI>();
            if(dismiss!=null) Decorate(Button("Later",panel.transform,Vector2.zero,Vector2.zero,new Vector2(60,38),new Vector2(230,58),"再待一会儿",()=>{Hide();dismiss();},false,21),false);
            panel.gameObject.AddComponent<ModalViewportFit>().Fit();
        }
        static void Decorate(Button button,bool primary)
        {
            button.GetComponent<Image>().color=primary?new Color(.37f,.29f,.49f,1):new Color(.88f,.84f,.91f,1);
            var label=button.GetComponentInChildren<TextMeshProUGUI>(); label.alignment=TextAlignmentOptions.Center;
            UIHoverFeedback.Attach(button,primary);
            label.color=primary?Color.white:new Color(.25f,.23f,.35f);
        }
        public void SetActionEnabled(bool enabled, string text) { if(accept!=null)accept.interactable=enabled;if(actionLabel!=null)actionLabel.text=text; }
        public void SetBody(string text) { if(body!=null)body.text=text; }
        public void Hide(){if(card!=null)Destroy(card);card=null;IsModal=false;accept=null;body=null;actionLabel=null;}
        void OnDestroy(){if(root!=null)Destroy(root.gameObject);}
    }
}
