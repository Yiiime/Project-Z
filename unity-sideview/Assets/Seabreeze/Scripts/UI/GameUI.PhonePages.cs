using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    public sealed partial class GameUI
    {
        void BuildPhoneMessages()
        {
            PhoneTabs(new[]{"全部","未读"},new[]{"all","unread"},"all");
            var root=PhoneList();float y=0;
            var threads=phoneData.Messages.GroupBy(m=>m.ThreadId).Where(g=>phoneRoute.Filter!="unread"||g.Any(m=>!m.IsRead)).Reverse().ToList();
            foreach(var thread in threads)
            {
                string id=thread.Key;var latest=thread.Last();int unread=thread.Count(m=>!m.IsRead);
                var button=PhoneButton(root,"Thread "+id,0,y,336,118,"",()=>PhoneNavigate("thread",id));
                PhoneAvatar(button.transform,14,17,latest.SenderId,40);
                var title=PhoneText(button.transform,66,15,234,25,latest.Title,19,PhoneWhite);title.overflowMode=TextOverflowModes.Ellipsis;
                PhoneText(button.transform,66,43,234,20,latest.DateLabel,12,PhoneMuted);
                var preview=PhoneText(button.transform,14,76,304,28,latest.Text.Replace('\n',' '),15,PhoneMuted);preview.overflowMode=TextOverflowModes.Ellipsis;
                if(unread>0){var b=MakeBadge(button.transform,new Vector2(-10,-10));b.text=unread>9?"9+":unread.ToString();}
                y+=130;
            }
            if(y==0)y=PhoneEmpty(root,phoneRoute.Filter=="unread"?"来信都读过了":"还没有来信",phoneRoute.Filter=="unread"?"有新的消息时，角标会提醒你。":"先沿着街走走。收到的通知，会好好留在这里。");
            EndPhoneList(y);
        }
        void BuildPhoneThread()
        {
            phoneData.MarkThreadRead(phoneRoute.Id);
            var root=PhoneList(110);float y=0;
            var messages=phoneData.Messages.Where(m=>m.ThreadId==phoneRoute.Id).ToList();
            if(messages.Count==0){EndPhoneList(PhoneEmpty(root,"这封信暂时不在这里","返回消息页，看看其他来信。"));return;}
            foreach(var message in messages)
            {
                y=PhoneParagraph(root,y,message.DateLabel,12,PhoneMuted);y+=12;
                y=PhoneParagraph(root,y,message.Title,20);y+=12;
                // Measure first, then size the soft letter surface. No rich-text interpretation.
                var letter=PhoneSurface("Letter",root,0,y,336,60,new Color(.59f,.56f,.72f,.20f),20);
                float height=PhoneParagraph(letter.transform,18,message.Text,17,PhoneWhite,18,300)+12;
                letter.rectTransform.sizeDelta=new Vector2(336,height);y+=height+20;
                if(!string.IsNullOrEmpty(message.TaskId))
                {var task=phoneData.Tasks.FirstOrDefault(t=>t.Id==message.TaskId);if(task!=null)y=PhoneTaskLink(root,y,task);}
            }
            y=PhonePersonLink(root,y,messages.Last().SenderId);
            y=PhoneParagraph(root,y+8,"有话想说，见面的时候慢慢聊。",14,PhoneMuted);EndPhoneList(y);
        }
        void BuildPhoneTasks()
        {
            PhoneTabs(new[]{"进行中","待回应","已完成"},new[]{"active","offered","completed"},"active");
            var root=PhoneList();float y=0;
            if(!phoneData.TaskDataAvailable)
            {
                y=PhoneEmpty(root,"等待委托记录",phoneData.TaskDataHint,"tasks");
                PhoneButton(root,"Refresh tasks",54,y,228,44,"重新读取",()=>phoneData.Refresh());EndPhoneList(y+60);return;
            }
            y=PhoneParagraph(root,y,phoneData.TaskDataHint,12,PhoneMuted)+10;
            int shown=0;
            foreach(var task in phoneData.Tasks.Where(t=>t.Status==phoneRoute.Filter).OrderByDescending(t=>t.Id==phoneData.PinnedTaskId))
            {
                shown++;
                string id=task.Id;var card=PhoneButton(root,"Task "+id,0,y,336,150,"",()=>PhoneNavigate("task",id));
                PhoneText(card.transform,14,13,306,22,(task.Id==phoneData.PinnedTaskId?"已关注  ·  ":"")+PersonName(task.OwnerId),13,task.Id==phoneData.PinnedTaskId?PhoneWarm:PhoneMuted);
                var title=PhoneText(card.transform,14,42,306,30,task.Title,21,PhoneWhite);title.overflowMode=TextOverflowModes.Ellipsis;
                string next=task.Status=="offered"?"见面时回应这份邀请":task.Status=="completed"?"这件事，已经一起完成":task.Steps.FirstOrDefault(s=>!s.Complete&&s.Ready)?.Label??"等待前面的事情完成";
                var hint=PhoneText(card.transform,14,83,306,24,next,14,PhoneMuted);hint.overflowMode=TextOverflowModes.Ellipsis;
                int done=task.Steps.Count(s=>s.Complete);PhoneText(card.transform,14,118,306,20,done+" / "+task.Steps.Count+" 件事已完成",12,PhoneMuted);
                y+=162;
            }
            if(shown==0){var empty=Rect("Empty tasks",root,TL,TL,new Vector2(0,-y),new Vector2(336,400));y+=PhoneEmpty(empty,phoneRoute.Filter=="offered"?"没有等待回应的邀请":phoneRoute.Filter=="completed"?"故事还在慢慢发生":"暂时没有进行中的事",phoneRoute.Filter=="completed"?"一起完成的事情，会在这里留下记录。":"不用急着找事情做。去见见熟悉的人吧。","tasks");}
            EndPhoneList(y);
        }
        void BuildPhoneTask()
        {
            var root=PhoneList(110);var task=phoneData.Tasks.FirstOrDefault(t=>t.Id==phoneRoute.Id);
            if(task==null){EndPhoneList(PhoneEmpty(root,"这件事暂时不在记录中",phoneData.TaskDataAvailable?"返回任务页，查看当前的事情。":phoneData.TaskDataHint,"tasks"));return;}
            float y=PhoneParagraph(root,0,TaskStatus(task.Status),13,PhoneMuted);
            y=PhoneParagraph(root,y+10,task.Title,25);y=PhoneParagraph(root,y+14,task.Description,17);y+=18;
            y=PhonePersonLink(root,y,task.OwnerId);
            y=PhoneParagraph(root,y+10,"一步一步来",13,PhoneMuted);y+=14;
            foreach(var step in task.Steps)
            {
                string mark=step.Complete?"✓":step.Ready?"·":"—";
                var dot=PhoneSurface("Step status",root,8,y+1,24,24,new Color(.65f,.65f,.80f,.15f),12);
                var symbol=PhoneText(dot.transform,0,0,24,24,mark,15,step.Complete?PhoneLilac:PhoneMuted);symbol.alignment=TextAlignmentOptions.Center;
                float bottom=PhoneParagraph(root,y,step.Label,16,step.Complete?PhoneMuted:PhoneWhite,44,284);
                string state=step.Complete?"已完成":task.Status=="offered"?"回应邀请后再开始":step.Ready?"接下来可以做":"完成前置事项后继续";
                bottom=PhoneParagraph(root,bottom+3,state,12,PhoneMuted,44,284);y=bottom+18;
            }
            if(task.Status=="active"||task.Status=="offered")
            {
                bool pinned=phoneData.PinnedTaskId==task.Id;
                PhoneButton(root,"Pin task",0,y+8,336,48,pinned?"✓ 已关注到首页 · 点击取消":"关注到手机首页",()=>{RememberPhoneScroll();phoneData.TogglePinnedTask(task.Id);},16,pinned);y+=74;
            }
            y=PhoneParagraph(root,y,task.Status=="offered"?"是否接受，见面时再告诉对方。":"阅读与关注不会推进进度。",13,PhoneMuted);EndPhoneList(y);
        }
        void BuildPhoneAlbum()
        {
            PhoneTabs(new[]{"全部","照片","回忆","收藏"},new[]{"all","photos","memories","favorites"},"all");
            var root=PhoneList();float y=ExperienceNotice(root);
            int shown=0;
            foreach(var memory in phoneData.Memories.Where(m=>phoneRoute.Filter=="all"||(phoneRoute.Filter=="photos"&&m.IsPhoto)||(phoneRoute.Filter=="memories"&&!m.IsPhoto)||(phoneRoute.Filter=="favorites"&&phoneData.IsFavorite(m.Id))))
            {
                shown++;string id=memory.Id;
                if(memory.IsPhoto)
                {
                    var photoCard=PhoneButton(root,"Photo "+id,0,y,336,268,"",()=>PhoneNavigate("memory",id));
                    var image=Rect("Photo thumbnail",photoCard.transform,TL,TL,new Vector2(12,-12),new Vector2(312,176)).gameObject.AddComponent<Image>();
                    image.color=new Color(.08f,.12f,.18f,.6f);image.raycastTarget=false;
                    bool loaded=image.gameObject.AddComponent<PhotoImageView>().Load(memory.ThumbnailPath);
                    if(!loaded)PhoneText(photoCard.transform,24,72,288,40,"照片文件暂时无法读取",15,PhoneMuted).alignment=TextAlignmentOptions.Center;
                    var caption=PhoneText(photoCard.transform,14,197,308,30,memory.Title,18,PhoneWhite);caption.overflowMode=TextOverflowModes.Ellipsis;
                    var date=PhoneText(photoCard.transform,14,235,308,22,MemoryMeta(memory)+(phoneData.IsFavorite(id)?" · ★":""),12,PhoneMuted);date.overflowMode=TextOverflowModes.Ellipsis;
                    y+=280;continue;
                }
                var card=PhoneButton(root,"Memory "+id,0,y,336,126,"",()=>PhoneNavigate("memory",id));
                FinalKitAssets.Icon(memory.IsPhoto?"album":"tasks",card.transform,TL,TL,new Vector2(16,-17),new Vector2(30,30));
                PhoneText(card.transform,58,17,260,22,(memory.IsPhoto?"留影":memory.ActorIds.Length>0?"共同经历":"岛上见闻")+(phoneData.IsFavorite(id)?" · 已收藏":""),13,PhoneLilac);
                var title=PhoneText(card.transform,16,57,304,29,memory.Title,20,PhoneWhite);title.overflowMode=TextOverflowModes.Ellipsis;
                var meta=PhoneText(card.transform,16,94,304,20,MemoryMeta(memory),12,PhoneMuted);meta.overflowMode=TextOverflowModes.Ellipsis;
                y+=138;
            }
            if(shown==0)
            {
                var empty=Rect("Empty album",root,TL,TL,new Vector2(0,-y),new Vector2(336,400));
                string title=phoneRoute.Filter=="photos"?"还没有留下照片":phoneRoute.Filter=="favorites"?"想记住的，慢慢收藏":"把相遇留在这里";
                string text=phoneRoute.Filter=="photos"?"在小岛的任何地方按 P，留下眼前的画面。输入文字时，先退出输入框再拍照。":phoneRoute.Filter=="favorites"?"打开一段回忆，点一下收藏，就能在这里再找到它。":"一次见面，一件一起完成的小事，都会让这里慢慢丰富起来。按 P 也可以拍下此刻。";
                y+=PhoneEmpty(empty,title,text,"album");
                if(phoneRoute.Filter=="photos")y=PhonePersonLink(root,y,"zhou_bobo");
            }
            EndPhoneList(y);
        }
        string MemoryMeta(PhoneMemory m)
        {
            string place=m.Place;
            if(Enum.TryParse<PlaceId>(place,out var id))place=PlaceName(id);
            return string.IsNullOrEmpty(place)?m.DateLabel:m.DateLabel+" · "+place;
        }
        void BuildPhoneMemory()
        {
            var root=PhoneList(110);var memory=phoneData.Memories.FirstOrDefault(m=>m.Id==phoneRoute.Id);
            if(memory==null){EndPhoneList(PhoneEmpty(root,"这段回忆暂时不在这里","返回相册，看看其他经历。","album"));return;}
            float y=0;
            // The photo file is a captured frame belonging to this save. Text memories stay text.
            bool photoLoaded=false;
            if(memory.IsPhoto)
            {
                var surface=PhoneSurface("Acquired photo",root,0,0,336,206,new Color(.05f,.08f,.13f,.35f),18);
                var picture=Rect("Photo",surface.transform,C,C,Vector2.zero,new Vector2(336,206)).gameObject.AddComponent<Image>();
                photoLoaded=picture.gameObject.AddComponent<PhotoImageView>().Load(memory.ImagePath);
                if(photoLoaded)
                {
                    var b=surface.gameObject.AddComponent<Button>();surface.raycastTarget=true;b.targetGraphic=surface;
                    b.onClick.AddListener(()=>ShowPhoneLightbox(memory.ImagePath,memory.Title));
                    y=PhoneParagraph(root,216,"点击照片查看大图",12,PhoneMuted)+12;
                }
                else Destroy(surface.gameObject);
            }
            if(!photoLoaded)
            {
                var banner=PhoneSurface("Memory heading",root,0,0,336,72,new Color(.54f,.51f,.66f,.18f),18);
                FinalKitAssets.Icon("tasks",banner.transform,TL,TL,new Vector2(18,-20),new Vector2(32,32));
                PhoneText(banner.transform,66,24,250,26,memory.IsPhoto?"照片暂时无法读取":memory.ActorIds.Length>0?"共同经历 · 文字记录":"岛上见闻 · 文字记录",16,PhoneLilac);y=96;
            }
            y=PhoneParagraph(root,y,memory.Title,25);y=PhoneParagraph(root,y+8,MemoryMeta(memory),12,PhoneMuted);
            y=PhoneParagraph(root,y+22,memory.Text,17);y+=24;
            if(memory.ActorIds.Length>0){y=PhoneParagraph(root,y,"相关的人",13,PhoneMuted)+12;foreach(string id in memory.ActorIds)y=PhonePersonLink(root,y,id);}
            bool saved=phoneData.IsFavorite(memory.Id);
            PhoneButton(root,"Favorite memory",0,y+8,336,48,saved?"★ 已收藏 · 点击取消":"收藏这段回忆",()=>{RememberPhoneScroll();phoneData.ToggleFavorite(memory.Id);},16,saved);
            EndPhoneList(y+72);
        }
        void ShowPhoneLightbox(string photoPath,string title)
        {
            if(overlayBusy||phoneLightbox!=null)return;
            var view=Stretch("Photo lightbox",overlayRoot,Vector2.zero,Vector2.zero);phoneLightbox=view.gameObject;
            var cover=view.gameObject.AddComponent<Image>();cover.color=new Color(.025f,.045f,.075f,.96f);cover.raycastTarget=true;
            var image=Stretch("Full photo",view,new Vector2(70,110),new Vector2(-70,-100)).gameObject.AddComponent<Image>();image.raycastTarget=false;
            // Image.preserveAspect uses the pivot to align its letterboxed content.
            // Keep the photo centred without changing the surrounding safe margins.
            image.rectTransform.pivot=Vector2.one*.5f;
            image.rectTransform.offsetMin=new Vector2(70,110);image.rectTransform.offsetMax=new Vector2(-70,-100);
            if(!image.gameObject.AddComponent<PhotoImageView>().Load(photoPath))
            {
                image.color=Color.clear;
                Label("Missing photo",view,C,C,Vector2.zero,new Vector2(900,60),"照片文件暂时无法读取",24,PhoneMuted,TextAlignmentOptions.Center);
            }
            var heading=Label("Caption",view,TC,TC,new Vector2(0,-32),new Vector2(900,40),title,24,PhoneWhite,TextAlignmentOptions.Center);heading.richText=false;
            var close=PhoneButton(view,"Close photo",30,26,130,44,"‹ 返回回忆",PhoneBack);
            Label("Photo back hint",view,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,38),new Vector2(700,30),"Esc 返回这段回忆  ·  Tab 收起手机",16,PhoneMuted,TextAlignmentOptions.Center);
        }
        float ExperienceNotice(Transform root)
        {return string.IsNullOrEmpty(phoneData.ExperienceDataHint)?0:PhoneParagraph(root,0,phoneData.ExperienceDataHint,13,PhoneWarm)+16;}
        void BuildPhonePeople()
        {
            var root=PhoneList(110);float y=ExperienceNotice(root);y=PhoneParagraph(root,y,"已经相识的人",13,PhoneMuted)+16;
            foreach(var person in phoneData.People)
            {
                string id=person.Id;var card=PhoneButton(root,"Person "+id,0,y,336,100,"",()=>PhoneNavigate("person",id));
                PhoneAvatar(card.transform,14,24,id,50);
                PhoneText(card.transform,78,18,236,28,person.Name,21,PhoneWhite);
                var job=PhoneText(card.transform,78,52,236,36,person.Occupation,13,PhoneMuted);job.overflowMode=TextOverflowModes.Ellipsis;
                y+=112;
            }
            if(phoneData.People.Count==0)
            {var empty=Rect("Empty people",root,TL,TL,new Vector2(0,-y),new Vector2(336,400));y+=PhoneEmpty(empty,"还没有正式打过招呼","走近一个人，按 E 聊聊天。相识之后，这里会留下他的名片。","people");}
            EndPhoneList(y);
        }
        void BuildPhonePerson()
        {
            var root=PhoneList(110);var person=phoneData.People.FirstOrDefault(p=>p.Id==phoneRoute.Id);
            if(person==null){EndPhoneList(PhoneEmpty(root,"还没有这位朋友的名片","有机会见面，再慢慢了解吧。","people"));return;}
            PhoneAvatar(root,0,8,person.Id,72);
            PhoneText(root,90,6,240,38,person.Name,28,PhoneWhite);
            float y=PhoneParagraph(root,53,person.Occupation,14,PhoneMuted,90,238);
            y=Mathf.Max(112,y+24);
            string[] tabs={"about","together"};string[] titles={"关于 TA","一起经历"};
            for(int i=0;i<2;i++)
            {
                string tab=tabs[i];PhoneButton(root,"Person tab "+tab,i*172,y,164,34,titles[i],()=>{phoneRoute.Tab=tab;phoneRoute.Scroll=0;RenderPhonePage();},15,phoneRoute.Tab==tab);
            }
            y+=58;
            if(phoneRoute.Tab=="about")
            {
                y=PhoneParagraph(root,y,"你已经知道的事",13,PhoneMuted)+14;
                foreach(var fact in person.Facts)
                {y=PhoneParagraph(root,y,fact.Text,17)+4;y=PhoneParagraph(root,y,fact.Source,12,PhoneMuted)+22;}
                y=PhoneParagraph(root,y,"还没聊到的部分",13,PhoneMuted)+6;y=PhoneParagraph(root,y,"留给以后的见面，慢慢了解。",16)+25;
                var tasks=phoneData.Tasks.Where(t=>t.OwnerId==person.Id).ToList();
                if(tasks.Count>0){y=PhoneParagraph(root,y,"与 TA 有关的事情",13,PhoneMuted)+14;foreach(var task in tasks)y=PhoneTaskLink(root,y,task);}
            }
            else
            {
                var records=phoneData.Memories.Where(m=>m.ActorIds.Contains(person.Id)).ToList();
                foreach(var memory in records)
                {
                    string id=memory.Id;var b=PhoneButton(root,"Person memory "+id,0,y,336,92,"",()=>PhoneNavigate("memory",id));
                    PhoneText(b.transform,14,14,308,22,memory.DateLabel,12,PhoneMuted);
                    var t=PhoneText(b.transform,14,47,308,28,memory.Title+"  ›",18,PhoneWhite);t.overflowMode=TextOverflowModes.Ellipsis;y+=104;
                }
                if(records.Count==0)y=PhoneParagraph(root,y,"还没有一起经历的事情。下次见面，也许就会有新的故事。",17,PhoneMuted)+24;
            }
            EndPhoneList(y);
        }
    }
}
