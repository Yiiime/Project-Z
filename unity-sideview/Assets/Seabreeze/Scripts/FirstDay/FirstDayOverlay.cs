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
        public void SetActionEnabled(bool enabled, string text) { if(accept!=null)accept.interactable=enabled;if(actionLabel!=null)actionLabel.text=text; }
        public void SetBody(string text) { if(body!=null)body.text=text; }
        public void Hide(){if(card!=null)Destroy(card);card=null;IsModal=false;accept=null;body=null;actionLabel=null;}
        void OnDestroy(){if(root!=null)Destroy(root.gameObject);}
    }
}
