using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    // Keeps the existing overlay/world lifecycle. Only the phone's content changes on navigation.
    public sealed partial class GameUI
    {
        sealed class PhoneRoute
        {
            public string Page, Id, Filter = "", Tab = "about";
            public float Scroll;
            public PhoneRoute(string page, string id = "") { Page=page;Id=id; }
        }
        PhoneDataService phoneData;
        TextMeshProUGUI phoneBadge;
        RectTransform phonePage;
        CanvasGroup phonePageGroup;
        ScrollRect phoneScroll;
        PhoneRoute phoneRoute;
        readonly Stack<PhoneRoute> phoneBackStack = new Stack<PhoneRoute>();
        readonly Dictionary<string,string> phoneFilters = new Dictionary<string,string>();
        Coroutine phoneNavigation;
        GameObject phoneLightbox;
        bool phoneDirty;
        SaveData phoneOwner;
        static readonly Color PhoneWhite=new Color(.96f,.95f,.98f);
        static readonly Color PhoneBorder=new Color(.96f,.95f,1,.28f);
        static readonly Color PhoneMuted=new Color(.69f,.77f,.85f);
        static readonly Color PhoneLilac=new Color(.79f,.73f,.88f);
        static readonly Color PhoneWarm=new Color(.90f,.81f,.65f);
        static bool IsPhonePage(string mode)=>mode=="phone"||mode=="messages"||mode=="album"||mode=="people"||mode=="tasks";
        void InitializePhone()
        {
            phoneData=gameObject.AddComponent<PhoneDataService>();phoneData.Initialize(session);
            phoneData.Changed+=OnPhoneDataChanged;phoneOwner=session.State;
        }
        void DisposePhone(){if(phoneData!=null)phoneData.Changed-=OnPhoneDataChanged;}
        void OnPhoneDataChanged(){phoneDirty=true;RefreshPhoneBadge();}
        void TickPhone()
        {
            if(phoneOwner!=session.State)
            {
                phoneOwner=session.State;phoneFilters.Clear();
                if(IsPhonePage(overlayMode)){CloseOverlayImmediately();return;}
            }
            if(!phoneDirty||overlayBusy)return;
            // A background sync must not destroy the ScrollRect while the pointer is dragging it.
            if(Mouse.current!=null&&Mouse.current.leftButton.isPressed)return;
            phoneDirty=false;
            if(IsPhonePage(overlayMode)&&phonePage!=null)
            { RememberPhoneScroll();RenderPhonePage(); }
        }
        Button KitCircle(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,float size,string icon,Action action)
        {
            var surface=(RoundedPanel)Panel(name,parent,anchor,pivot,position,Vector2.one*size,new Color(.19f,.19f,.31f,.16f),true);
            surface.CornerRadius=size/2;surface.SetBorder(Color.clear,0);
            var button=surface.gameObject.AddComponent<Button>();button.targetGraphic=surface;
            button.navigation=new Navigation{mode=Navigation.Mode.None};
            button.onClick.AddListener(()=>{if(!IsTransitioning&&!overlayBusy&&!(session.Memory?.PlayerChatOpen??false))action();});
            UIHoverFeedback.Attach(button);
            FinalKitAssets.Icon("outer-ring",surface.transform,C,C,Vector2.zero,Vector2.one*size);
            FinalKitAssets.Icon("inner-ring",surface.transform,C,C,Vector2.zero,Vector2.one*(size*88/104));
            FinalKitAssets.Icon(icon,surface.transform,C,C,Vector2.zero,Vector2.one*(size==64?24:48));
            return button;
        }
        TextMeshProUGUI MakeBadge(Transform parent,Vector2 position)
        {
            var bg=(RoundedPanel)Panel("Unread badge",parent,Vector2.one,Vector2.one,position,new Vector2(24,24),new Color(.94f,.92f,.96f));
            bg.CornerRadius=12;bg.SetBorder(Color.clear,0);
            return Label("Count",bg.transform,C,C,Vector2.zero,new Vector2(24,24),"",14,new Color(.22f,.20f,.31f),TextAlignmentOptions.Center);
        }
        int UnreadCount=>phoneData!=null?phoneData.UnreadCount:0;
        void RefreshPhoneBadge()
        {
            if(phoneBadge==null)return;
            phoneBadge.transform.parent.gameObject.SetActive(UnreadCount>0);
            phoneBadge.text=UnreadCount>9?"9+":UnreadCount.ToString();
        }
        string PhoneTime()
        {
            string value=session.Clock.TimeLabel;int start=value.IndexOfAny("0123456789".ToCharArray());
            return start<0?value:value.Substring(start).Trim();
        }
        void BuildPhoneShell(string mode)
        {
            phoneData.Refresh();phoneOwner=session.State;phoneBackStack.Clear();
            phoneRoute=new PhoneRoute(mode);phoneDirty=false;
            var shell=(RoundedPanel)Panel("Phone shell",overlayRoot,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-96,18),new Vector2(384,704),new Color(.16f,.22f,.32f,.82f),true);
            shell.CornerRadius=54;shell.SetBorder(PhoneBorder,1.4f);overlayContent=shell.transform;
            var inner=(RoundedPanel)Panel("Inner hairline",shell.transform,C,C,Vector2.zero,new Vector2(372,692),Color.clear);
            inner.CornerRadius=49;inner.SetBorder(new Color(.96f,.95f,1,.22f),.6f);
            phonePage=Rect("Phone pages",shell.transform,TL,TL,Vector2.zero,new Vector2(384,704));
            phonePageGroup=phonePage.gameObject.AddComponent<CanvasGroup>();
            RenderPhonePage();
        }
        void RememberPhoneScroll(){if(phoneRoute!=null&&phoneScroll!=null)phoneRoute.Scroll=phoneScroll.content.anchoredPosition.y;}
        void PhoneNavigate(string page,string id="")
        {
            if(overlayBusy||phoneRoute==null)return;
            RememberPhoneScroll();phoneBackStack.Push(phoneRoute);
            var route=new PhoneRoute(page,id);if(phoneFilters.TryGetValue(page,out var filter))route.Filter=filter;
            BeginPhoneNavigation(route);
        }
        void PhoneBack()
        {
            if(overlayBusy)return;
            if(phoneLightbox!=null){Destroy(phoneLightbox);phoneLightbox=null;return;}
            if(phoneBackStack.Count>0)BeginPhoneNavigation(phoneBackStack.Pop());
            else if(phoneRoute!=null&&phoneRoute.Page!="phone")BeginPhoneNavigation(new PhoneRoute("phone"));
            else CloseOverlay();
        }
        void BeginPhoneNavigation(PhoneRoute next)
        {overlayBusy=true;phoneNavigation=StartCoroutine(ChangePhonePage(next));}
        IEnumerator ChangePhonePage(PhoneRoute next)
        {
            phonePageGroup.interactable=false;phonePageGroup.blocksRaycasts=false;
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/.09f){phonePageGroup.alpha=1-t;yield return null;}
            phoneRoute=next;
            if(next.Page=="thread")phoneData.MarkThreadRead(next.Id);
            phoneDirty=false;RenderPhonePage();
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/.16f)
            {float eased=t*t*(3-2*t);phonePageGroup.alpha=eased;phonePage.anchoredPosition=new Vector2(6*(1-eased),0);yield return null;}
            phonePage.anchoredPosition=Vector2.zero;phonePageGroup.alpha=1;
            phonePageGroup.interactable=true;phonePageGroup.blocksRaycasts=true;overlayBusy=false;phoneNavigation=null;
        }
        void FinishPhoneClose()
        {
            if(phoneNavigation!=null){StopCoroutine(phoneNavigation);phoneNavigation=null;}
            if(phoneLightbox!=null)Destroy(phoneLightbox);phoneLightbox=null;
            phoneBackStack.Clear();phoneRoute=null;phoneScroll=null;
        }
        void RenderPhonePage()
        {
            if(phonePage==null||phoneRoute==null)return;
            Clear(phonePage);phoneScroll=null;
            if(phoneRoute.Page=="phone"){BuildPhoneHome();return;}
            PhoneText(phonePage,26,22,112,20,PhoneTime(),12,PhoneMuted);
            var date=PhoneText(phonePage,150,22,208,20,"小岛 · 第 "+session.Clock.Day+" 天",12,PhoneMuted);date.alignment=TextAlignmentOptions.TopRight;
            PhoneButton(phonePage,"Phone back",24,48,40,40,"‹",PhoneBack,23);
            PhoneButton(phonePage,"Phone close",320,48,40,40,"×",CloseOverlay,23);
            var heading=PhoneText(phonePage,76,52,232,34,PhonePageTitle(),23,PhoneWhite);heading.alignment=TextAlignmentOptions.Center;
            if(phoneRoute.Page=="messages")BuildPhoneMessages();
            else if(phoneRoute.Page=="thread")BuildPhoneThread();
            else if(phoneRoute.Page=="tasks")BuildPhoneTasks();
            else if(phoneRoute.Page=="task")BuildPhoneTask();
            else if(phoneRoute.Page=="album")BuildPhoneAlbum();
            else if(phoneRoute.Page=="memory")BuildPhoneMemory();
            else if(phoneRoute.Page=="people")BuildPhonePeople();
            else if(phoneRoute.Page=="person")BuildPhonePerson();
            Line(phonePage,TL,TL,new Vector2(24,-657),new Vector2(336,.6f),new Color(.96f,.95f,1,.15f));
            var hint=PhoneText(phonePage,24,671,336,20,"Esc 返回上一层    ·    Tab 收起手机",12,PhoneMuted);hint.alignment=TextAlignmentOptions.Center;
        }
        string PhonePageTitle()
        {
            switch(phoneRoute.Page){case "messages":return "消息";case "thread":return "来信详情";case "tasks":return "任务";case "task":return "事情详情";case "album":return "相册";case "memory":return "回忆详情";case "people":return "人物";default:return "人物档案";}
        }
        void BuildPhoneHome()
        {
            var widget=PhoneSurface("Time place task",phonePage,20,38,346,246,new Color(.12f,.18f,.28f,.27f),40);
            var day=PhoneText(widget.transform,22,24,302,26,"第 "+session.Clock.Day+" 天 · "+PeriodName(session.Clock.Period),18,PhoneWhite);day.alignment=TextAlignmentOptions.Center;
            var clock=PhoneText(widget.transform,22,48,302,80,PhoneTime(),68,PhoneWhite);clock.alignment=TextAlignmentOptions.Center;
            if(FinalKitAssets.ClockFont!=null)clock.font=FinalKitAssets.ClockFont;
            clock.rectTransform.sizeDelta=new Vector2(302,80);
            FinalKitAssets.Icon("pin",widget.transform,TL,TL,new Vector2(40,-134),new Vector2(18,20));
            var place=PhoneText(widget.transform,67,128,244,32,PlaceName(session.CurrentPlace),18,PhoneWhite);place.enableAutoSizing=true;place.fontSizeMin=14;
            Line(widget.transform,TL,TL,new Vector2(22,-177),new Vector2(302,.6f),new Color(.96f,.95f,1,.22f));
            var task=phoneData.Tasks.FirstOrDefault(t=>t.Id==phoneData.PinnedTaskId);
            var remind=PhoneButton(widget.transform,"Pinned task",22,188,302,42,task==null?"选择一件事，放在心上":"已关注  ·  "+task.Title,()=>{if(task==null)PhoneNavigate("tasks");else PhoneNavigate("task",task.Id);},17);
            remind.GetComponentInChildren<TextMeshProUGUI>().overflowMode=TextOverflowModes.Ellipsis;
            string[] labels={"消息","任务","相册","地图","人物","直播"};
            string[] icons={"messages","tasks","album","map","people","live"};
            for(int i=0;i<6;i++)
            {
                string mode=icons[i];
                var button=KitCircle("Phone app "+mode,phonePage,TL,TL,new Vector2(23+(i%3)*118,-320-(i/3)*142),104,mode,()=>{if(mode=="map"||mode=="live")OpenOverlay(mode);else PhoneNavigate(mode);});
                Label("Caption",button.transform,new Vector2(.5f,0),TC,new Vector2(0,-4),new Vector2(104,26),labels[i],16,PhoneWhite,TextAlignmentOptions.Top);
                if(i==0&&UnreadCount>0){var badge=MakeBadge(button.transform,new Vector2(-8,1));badge.text=UnreadCount>9?"9+":UnreadCount.ToString();}
            }
            PhoneButton(phonePage,"Seventh night ending",24,614,336,42,"第七晚 · 进入结局 →",OpenSeventhNight,16,true);
            var hint=PhoneText(phonePage,24,676,336,22,"Tab 收起手机 · 回到眼前的风景",13,PhoneMuted);hint.alignment=TextAlignmentOptions.Center;
        }

        // Shared sizing/typography, with untrusted authored/AI text always rendered literally.
        RoundedPanel PhoneSurface(string name,Transform parent,float x,float y,float w,float h,Color fill,float radius=18)
        {
            var p=(RoundedPanel)Panel(name,parent,TL,TL,new Vector2(x,-y),new Vector2(w,h),fill);
            p.CornerRadius=radius;p.SetBorder(PhoneBorder,.65f);return p;
        }
        TextMeshProUGUI PhoneText(Transform parent,float x,float y,float w,float h,string value,float size,Color color)
        {
            // The CJK font has a 1.45-em line box. An ellipsis field smaller than that
            // suppresses even its first line in TMP, so reserve at least one complete line.
            var t=Label("Phone text",parent,TL,TL,new Vector2(x,-y),new Vector2(w,Mathf.Max(h,size*1.5f+1)),value??"",size,color);
            t.richText=false;t.lineSpacing=3;return t;
        }
        float PhoneParagraph(Transform parent,float y,string text,float size=17,Color? tint=null,float x=8,float width=320)
        {
            var label=PhoneText(parent,x,y,width,40,text,size,tint??PhoneWhite);
            float h=Mathf.Max(size+7,label.GetPreferredValues(text??"",width,0).y+6);
            label.rectTransform.sizeDelta=new Vector2(width,h);return y+h;
        }
        Button PhoneButton(Transform parent,string name,float x,float y,float w,float h,string text,Action action,float size=16,bool accent=false)
        {
            var panel=PhoneSurface(name,parent,x,y,w,h,accent?new Color(.55f,.52f,.66f,.36f):new Color(.55f,.66f,.81f,.09f),Mathf.Min(18,h/2));panel.raycastTarget=true;
            var button=panel.gameObject.AddComponent<Button>();button.targetGraphic=panel;button.navigation=new Navigation{mode=Navigation.Mode.None};
            button.onClick.AddListener(()=>{if(!overlayBusy&&!IsTransitioning)action?.Invoke();});UIHoverFeedback.Attach(button,accent);
            var label=PhoneText(panel.transform,12,0,w-24,h,text,size,accent?PhoneLilac:PhoneWhite);label.alignment=TextAlignmentOptions.Center;
            return button;
        }
        void PhoneTabs(string[] labels,string[] values,string defaultValue)
        {
            if(string.IsNullOrEmpty(phoneRoute.Filter))phoneRoute.Filter=defaultValue;
            float width=(336-6*(values.Length-1))/values.Length;
            for(int i=0;i<values.Length;i++)
            {
                string value=values[i];PhoneButton(phonePage,"Filter "+value,24+i*(width+6),106,width,34,labels[i],()=>{phoneRoute.Filter=value;phoneFilters[phoneRoute.Page]=value;phoneRoute.Scroll=0;RenderPhonePage();},14,phoneRoute.Filter==value);
            }
        }
        RectTransform PhoneList(float top=156)
        {
            var view=Rect("Phone scroll",phonePage,TL,TL,new Vector2(24,-top),new Vector2(336,646-top));
            var bg=view.gameObject.AddComponent<Image>();bg.color=new Color(1,1,1,.001f);bg.raycastTarget=true;
            view.gameObject.AddComponent<RectMask2D>();
            phoneScroll=view.gameObject.AddComponent<ScrollRect>();phoneScroll.horizontal=false;phoneScroll.viewport=view;
            phoneScroll.movementType=ScrollRect.MovementType.Clamped;phoneScroll.scrollSensitivity=30;phoneScroll.decelerationRate=.12f;
            var content=Rect("Phone content",view,TL,TL,Vector2.zero,new Vector2(336,view.sizeDelta.y));phoneScroll.content=content;return content;
        }
        void EndPhoneList(float height)
        {
            phoneScroll.content.sizeDelta=new Vector2(336,Mathf.Max(height+8,phoneScroll.viewport.rect.height));
            phoneScroll.content.anchoredPosition=new Vector2(0,Mathf.Clamp(phoneRoute.Scroll,0,Mathf.Max(0,phoneScroll.content.sizeDelta.y-phoneScroll.viewport.rect.height)));
        }
        float PhoneEmpty(Transform root,string title,string text,string icon="messages")
        {
            FinalKitAssets.Icon(icon,root,TL,TL,new Vector2(144,-50),new Vector2(48,48));
            var heading=PhoneText(root,12,126,312,38,title,22,PhoneWhite);heading.alignment=TextAlignmentOptions.Center;
            var body=PhoneText(root,24,179,288,140,text,17,PhoneMuted);body.alignment=TextAlignmentOptions.Center;return 340;
        }
        string PersonName(string id)=>phoneData.People.FirstOrDefault(p=>p.Id==id)?.Name??SevenCharacterCatalog.Find(id)?.displayName??"小岛来信";
        void PhoneAvatar(Transform parent,float x,float y,string id,float size=44)
        {
            var person=phoneData.People.FirstOrDefault(p=>p.Id==id);Color tint=person?.Color??PhoneLilac;
            var p=PhoneSurface("Avatar",parent,x,y,size,size,new Color(tint.r,tint.g,tint.b,.15f),size/2);p.SetBorder(new Color(tint.r,tint.g,tint.b,.6f),.7f);
            string name=person?.Name??"信";var t=PhoneText(p.transform,0,0,size,size,name.Substring(0,1),size*.44f,tint);t.alignment=TextAlignmentOptions.Center;
        }
        float PhonePersonLink(Transform root,float y,string id)
        {
            var person=phoneData.People.FirstOrDefault(p=>p.Id==id);if(person==null)return y;
            var button=PhoneButton(root,"Person "+id,0,y,336,66,"",()=>PhoneNavigate("person",id));
            PhoneAvatar(button.transform,12,11,id);PhoneText(button.transform,68,13,224,24,person.Name,18,PhoneWhite);
            PhoneText(button.transform,68,37,224,20,"查看人物档案  ›",13,PhoneMuted);return y+78;
        }
        float PhoneTaskLink(Transform root,float y,PhoneTask task)
        {
            var b=PhoneButton(root,"Task "+task.Id,0,y,336,78,"",()=>PhoneNavigate("task",task.Id));
            PhoneText(b.transform,14,12,304,22,TaskStatus(task.Status),13,PhoneMuted);
            var t=PhoneText(b.transform,14,39,304,28,task.Title+"  ›",18,PhoneWhite);t.overflowMode=TextOverflowModes.Ellipsis;return y+90;
        }
        static string TaskStatus(string status)=>status=="completed"?"已完成":status=="offered"?"待回应":status=="declined"?"已婉拒":"进行中";
    }
}
