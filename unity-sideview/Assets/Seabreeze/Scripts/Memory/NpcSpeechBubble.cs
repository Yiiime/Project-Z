using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;
namespace Seabreeze
{
    // One current speaker at a time avoids overlapping bubbles for nearby NPCs.
    public sealed class NpcSpeechBubble : MonoBehaviour
    {
        RectTransform canvasRoot,card;TextMeshProUGUI title,body;MemoryGameController memory;WorldDirector world;
        NpcPresence speaker;string[] pages;int page;float nextPage;
        Renderer[] renderers;readonly Plane[] frustum=new Plane[6];
        const int PageCharacters=70;
        public bool Visible=>card!=null&&card.gameObject.activeSelf;
        public string VisibleText=>Visible?title.text+"："+body.text:"";
        public string SpeakerId=>speaker!=null?speaker.npcId:"";
        public RectTransform Card=>card;
        public void Initialize(RectTransform root,MemoryGameController controller,WorldDirector director)
        {
            canvasRoot=root;memory=controller;world=director;
            card=Panel("NPC头顶气泡",root,new Vector2(.5f,.5f),new Vector2(.5f,0),Vector2.zero,new Vector2(430,180),new Color(.045f,.09f,.15f,.96f)).rectTransform;
            var group=card.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            title=Label("说话者",card,Vector2.up,Vector2.up,new Vector2(20,-13),new Vector2(390,28),"",20,Accent);title.richText=false;
            body=Label("气泡文字",card,Vector2.up,Vector2.up,new Vector2(20,-49),new Vector2(390,110),"",22,Paper);body.richText=false;
            var tail=Rect("气泡尾部",card,new Vector2(.5f,0),new Vector2(.5f,1),Vector2.zero,new Vector2(22,12)).gameObject.AddComponent<SpeechBubbleTail>();tail.color=new Color(.045f,.09f,.15f,.96f);tail.raycastTarget=false;
            Clear();
        }
        static string[] Pages(string text)
        {
            var chars=new List<string>();var e=StringInfo.GetTextElementEnumerator(text??"");while(e.MoveNext())chars.Add(e.GetTextElement());
            var result=new List<string>();for(int i=0;i<chars.Count;i+=PageCharacters)result.Add(string.Concat(chars.Skip(i).Take(PageCharacters)));
            return result.Count>0?result.ToArray():new[]{""};
        }
        static float PageDuration(string text)=>Mathf.Clamp(2.2f+text.Length*.075f,3.5f,8);
        public static float ReadDuration(string text)=>Pages(text).Sum(PageDuration);
        public void Show(string actorId,string text)
        {
            speaker=memory.Spawner.Find(actorId);if(speaker==null){Clear();return;}
            renderers=speaker.GetComponentsInChildren<Renderer>(true);
            pages=Pages(text);page=0;title.text=SevenCharacterCatalog.Name(actorId);title.color=SevenCharacterCatalog.Find(actorId)?.Accent??Accent;
            ShowPage();Tick(false);
        }
        void ShowPage()
        {
            body.text=pages[page];float height=body.GetPreferredValues(body.text,390,0).y;
            body.rectTransform.sizeDelta=new Vector2(390,Mathf.Max(36,height));card.sizeDelta=new Vector2(430,Mathf.Max(110,height+69));
            nextPage=Time.unscaledTime+PageDuration(pages[page]);
        }
        public void Clear(){speaker=null;pages=null;renderers=null;if(card!=null)card.gameObject.SetActive(false);}
        public void Tick(bool suppressed)
        {
            if(speaker==null||!speaker.IsPresent||world==null||world.Stage==null||world.IsTransitioning||speaker.gameObject.scene!=world.Stage.gameObject.scene){Clear();return;}
            if(Time.unscaledTime>=nextPage){if(++page>=pages.Length){Clear();return;}ShowPage();}
            var camera=world.Follow?.GetComponent<Camera>();if(camera==null){card.gameObject.SetActive(false);return;}
            var screen=camera.WorldToScreenPoint(speaker.transform.position+Vector3.up*3.9f);
            GeometryUtility.CalculateFrustumPlanes(camera,frustum);
            bool onScreen=renderers!=null&&renderers.Any(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy&&(camera.cullingMask&(1<<r.gameObject.layer))!=0&&GeometryUtility.TestPlanesAABB(frustum,r.bounds));
            bool visible=!suppressed&&screen.z>0&&onScreen;
            card.gameObject.SetActive(visible);if(!visible)return;
            var canvas=canvasRoot.GetComponent<Canvas>();
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var local))
            {
                float halfWidth=canvasRoot.rect.width/2,halfHeight=canvasRoot.rect.height/2;
                card.anchoredPosition=new Vector2(Mathf.Clamp(local.x,-halfWidth+card.rect.width/2+12,halfWidth-card.rect.width/2-12),Mathf.Clamp(local.y,-halfHeight+20,halfHeight-card.rect.height-16));
            }
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SpeechBubbleTail : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;vh.AddVert(new Vector2(r.xMin,r.yMax),color,Vector2.zero);vh.AddVert(new Vector2(r.xMax,r.yMax),color,Vector2.zero);vh.AddVert(new Vector2(r.center.x,r.yMin),color,Vector2.zero);vh.AddTriangle(0,1,2);
        }
    }
}
