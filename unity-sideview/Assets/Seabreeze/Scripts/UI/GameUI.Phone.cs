using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    // Phone shell owns navigation; existing map, journal and native live implementations remain reusable.
    public sealed partial class GameUI
    {
        TextMeshProUGUI phoneBadge;
        int readMessageCount;
        static readonly Color PhoneWhite=new Color(.96f,.95f,.98f);
        static readonly Color PhoneBorder=new Color(.96f,.95f,1,.45f);
        static bool IsPhonePage(string mode)=>mode=="phone"||mode=="messages"||mode=="album"||mode=="people"||mode=="tasks";
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
        int UnreadCount=>Mathf.Max(0,session.Story.PhoneMessages.Count()-readMessageCount);
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
            var shell=(RoundedPanel)Panel("Phone shell",overlayRoot,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-96,18),new Vector2(384,664),new Color(.16f,.22f,.32f,.82f),true);
            shell.CornerRadius=54;shell.SetBorder(PhoneBorder,1.4f);overlayContent=shell.transform;
            var inner=(RoundedPanel)Panel("Inner hairline",shell.transform,C,C,Vector2.zero,new Vector2(372,652),Color.clear);
            inner.CornerRadius=49;inner.SetBorder(new Color(.96f,.95f,1,.22f),.6f);
            if(mode=="phone"){BuildPhoneHome();return;}
            var back=Button("Back to phone",shell.transform,TL,TL,new Vector2(24,-28),new Vector2(68,40),"‹ 返回",()=>OpenOverlay("phone"),false,16);
            string title=mode=="messages"?"消息":mode=="tasks"?"任务":mode=="album"?"相册":"人物";
            Label("Page title",shell.transform,TC,TC,new Vector2(0,-36),new Vector2(170,40),title,24,PhoneWhite,TextAlignmentOptions.Center);
            var scroll=MakeScroll(new Vector2(24,-98),new Vector2(336,500));float y=0;
            if(mode=="messages")
            {
                foreach(var message in session.Story.PhoneMessages)y=TextEntry(scroll,y,message,18,new Color(.65f,.65f,.81f,.14f));
                if(y==0)y=TextEntry(scroll,0,"暂时没有新消息。\n去街上走走，遇见的人会让这里慢慢热闹起来。",18,new Color(.65f,.65f,.81f,.14f));
                readMessageCount=session.Story.PhoneMessages.Count();RefreshPhoneBadge();
            }
            else if(mode=="tasks")
            {
                y=TextEntry(scroll,y,"进行中\n"+session.Story.Objective,20,new Color(.65f,.65f,.81f,.14f));
                foreach(var entry in session.State.journal.AsEnumerable().Reverse())y=TextEntry(scroll,y,entry,18,new Color(.65f,.65f,.81f,.10f));
            }
            else if(mode=="people")
            {
                foreach(var actor in SevenCharacterCatalog.Actors)y=TextEntry(scroll,y,actor.displayName+"\n"+actor.occupation,20,new Color(.65f,.65f,.81f,.14f));
            }
            else y=TextEntry(scroll,0,"还没有照片\n\n相册的位置为你留好了。拍照功能尚未开放，先好好看看眼前的小岛吧。",20,new Color(.65f,.65f,.81f,.14f));
            SetContentHeight(scroll,y);
            Label("Back hint",shell.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,24),new Vector2(330,24),"Esc 返回首页  ·  Tab 收起手机",14,Muted,TextAlignmentOptions.Center);
        }
        void BuildPhoneHome()
        {
            var widget=(RoundedPanel)Panel("Time place task",overlayContent,TL,TL,new Vector2(20,-38),new Vector2(346,246),new Color(.12f,.18f,.28f,.27f));
            widget.CornerRadius=40;widget.SetBorder(PhoneBorder,.65f);
            Label("Day period",widget.transform,TC,TC,new Vector2(0,-24),new Vector2(302,26),"第 "+session.Clock.Day+" 天 · "+PeriodName(session.Clock.Period),18,PhoneWhite,TextAlignmentOptions.Center);
            var clock=Label("Clock",widget.transform,TC,TC,new Vector2(0,-48),new Vector2(302,80),PhoneTime(),68,PhoneWhite,TextAlignmentOptions.Center);
            if(FinalKitAssets.ClockFont!=null)clock.font=FinalKitAssets.ClockFont;
            FinalKitAssets.Icon("pin",widget.transform,TC,TC,new Vector2(-125,-134),new Vector2(18,20));
            var place=Label("Location",widget.transform,TC,TC,new Vector2(16,-128),new Vector2(244,32),PlaceName(session.CurrentPlace),18,PhoneWhite,TextAlignmentOptions.MidlineLeft);
            place.enableAutoSizing=true;place.fontSizeMin=14;place.fontSizeMax=18;
            Line(widget.transform,TL,TL,new Vector2(22,-177),new Vector2(302,.6f),new Color(.96f,.95f,1,.22f));
            FinalKitAssets.Icon("task-check",widget.transform,TL,TL,new Vector2(30,-197),new Vector2(22,22));
            Label("Status",widget.transform,TL,TL,new Vector2(64,-194),new Vector2(58,32),"进行中",16,PhoneWhite,TextAlignmentOptions.MidlineLeft);
            var objective=Label("Task",widget.transform,TL,TL,new Vector2(133,-190),new Vector2(188,42),session.Story.Objective,20,PhoneWhite,TextAlignmentOptions.MidlineLeft);
            objective.enableAutoSizing=true;objective.fontSizeMin=14;objective.fontSizeMax=20;objective.overflowMode=TextOverflowModes.Ellipsis;
            string[] labels={"消息","任务","相册","地图","人物","直播"};
            string[] icons={"messages","tasks","album","map","people","live"};
            string[] modes={"messages","tasks","album","map","people","live"};
            for(int i=0;i<6;i++)
            {
                string mode=modes[i];
                var button=KitCircle("Phone app "+mode,overlayContent,TL,TL,new Vector2(23+(i%3)*118,-320-(i/3)*142),104,icons[i],()=>OpenOverlay(mode));
                Label("Caption",button.transform,new Vector2(.5f,0),TC,new Vector2(0,-4),new Vector2(104,26),labels[i],16,PhoneWhite,TextAlignmentOptions.Top);
                if(i==0&&UnreadCount>0){var badge=MakeBadge(button.transform,new Vector2(-8,1));badge.text=UnreadCount>9?"9+":UnreadCount.ToString();}
            }
        }
    }
}
