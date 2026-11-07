using TMPro;
using UnityEngine;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    /// <summary>A small world-tracked action caption. The key badge is independent from the verb.</summary>
    public sealed class InteractionPromptUI : MonoBehaviour
    {
        RectTransform canvasRect, root;
        CanvasGroup group;
        TextMeshProUGUI captionText;
        Vector3 worldPosition;
        bool requested, suppressed;
        string currentCaption;
        Vector2 lastPosition;
        bool positioned;

        public void Initialize(RectTransform canvas)
        {
            canvasRect=canvas;
            root=Rect("Interaction prompt",canvas,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(148,52));
            var body=Panel("Prompt glass",root,Vector2.zero,Vector2.zero,Vector2.zero,root.sizeDelta,Style.HudFill);
            ((RoundedPanel)body).CornerRadius=23;
            ((RoundedPanel)body).SetBorder(Style.PanelBorder,.9f);
            var key=Panel("Key badge",root,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(9,0),new Vector2(34,34),new Color(.78f,.87f,.91f,.94f));
            ((RoundedPanel)key).CornerRadius=9;
            ((RoundedPanel)key).SetBorder(Color.clear,0);
            Label("Key",key.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(34,34),"E",19,new Color(.07f,.13f,.22f),TextAlignmentOptions.Midline);
            captionText=Label("Action",root,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(56,0),new Vector2(88,36),"交谈",21,Paper,TextAlignmentOptions.MidlineLeft);
            group=root.gameObject.AddComponent<CanvasGroup>();
            group.alpha=0;group.blocksRaycasts=false;group.interactable=false;
        }

        public void Show(string caption, Vector3 position, bool npc = false)
        {
            requested=true;worldPosition=position;
            if(root==null)return;
            string value=npc?"交谈":Normalize(caption);
            if(value==currentCaption)return;
            currentCaption=value;captionText.text=value;
            float width=Mathf.Clamp(captionText.GetPreferredValues(value).x+82,142,520);
            root.sizeDelta=new Vector2(width,52);
            ((RectTransform)root.GetChild(0)).sizeDelta=root.sizeDelta;
            captionText.rectTransform.sizeDelta=new Vector2(width-68,36);
        }
        static string Normalize(string value)
        {
            if(string.IsNullOrWhiteSpace(value))return "查看";
            value=value.Trim();
            if(value.StartsWith("E"))value=value.Substring(1).Trim();
            return value.Contains("交谈")?"交谈":value;
        }
        public void Hide(bool immediate = false)
        {
            requested=false;
            if(immediate && group!=null)group.alpha=0;
        }
        public void SetSuppressed(bool value){suppressed=value;if(value)Hide(true);}
        void LateUpdate()
        {
            if(root==null||canvasRect==null)return;
            float target=requested&&!suppressed?1:0;
            group.alpha=Mathf.MoveTowards(group.alpha,target,Time.unscaledDeltaTime/Mathf.Max(.01f,Style.FadeOutDuration));
            root.localScale=Vector3.one*Mathf.Lerp(.96f,1f,group.alpha);
            if(group.alpha<=0)return;
            var camera=Camera.main;
            if(camera==null)return;
            Vector3 screen=camera.WorldToScreenPoint(worldPosition);
            if(screen.z<0){group.alpha=0;return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out Vector2 point);
            float horizontal=canvasRect.rect.width*.5f;
            float vertical=canvasRect.rect.height*.5f;
            point.x=Mathf.Clamp(point.x,-horizontal+root.sizeDelta.x*.5f+16,horizontal-root.sizeDelta.x*.5f-16);
            point.y=Mathf.Clamp(point.y,-vertical+165,vertical-74);
            if(!positioned||Vector2.Distance(point,lastPosition)>240){lastPosition=point;positioned=true;}
            else lastPosition=Vector2.Lerp(lastPosition,point,1-Mathf.Exp(-22*Time.unscaledDeltaTime));
            root.anchoredPosition=lastPosition+Vector2.up*(1-group.alpha)*5;
        }
    }
}
