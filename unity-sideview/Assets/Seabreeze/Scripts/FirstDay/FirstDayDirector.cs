using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seabreeze
{
    // Durable Day 1 checkpoints; world, chat, cinema and phone retain their own lifecycles.
    public sealed class FirstDayDirector : MonoBehaviour
    {
        GameSession session; WorldDirector world;
        FirstDayCinema cinema; FirstDayOverlay invitations;
        NativeLiveHost openingLive;
        FirstDayContent content;
        WorldScene decorated;
        bool busy, resuming, waitingInvitation, travelSucceeded;
        float liveOpened;
        int generation;
        public bool BlocksInput => busy;
        public bool Active => session?.State?.firstDay != null && Phase != FirstDayPhase.None && Phase != FirstDayPhase.Complete;
        public FirstDayPhase Phase => session?.State?.firstDay?.phase ?? FirstDayPhase.None;
        public string[] Cast => SevenCharacterCatalog.Actors.Select(a=>a.id).ToArray();
        public int GreetedCount => session.State.firstDay?.greeted.Count(id=>Cast.Contains(id)) ?? 0;
        public string Objective
        {
            get
            {
                switch(Phase)
                {
                    case FirstDayPhase.Live: return "出发前夜 · 在 Z 的直播间，等一封小岛来信。";
                    case FirstDayPhase.Arrival: return "船正靠近小岛。";
                    case FirstDayPhase.Harbor: return "Z 正在码头等你。靠近她，按 E 打声招呼。";
                    case FirstDayPhase.WalkToSquare: return "沿街向右：码头 → 公共街 → 古树广场。走到街道边缘即可通行。";
                    case FirstDayPhase.MeetIslanders:
                        var remaining = SevenCharacterCatalog.Actors.Where(a=>!session.State.firstDay.greeted.Contains(a.id)).Select(a=>a.displayName);
                        return GreetedCount < Cast.Length ? $"认识岛民 {GreetedCount}/{Cast.Length} · 靠近按 E\n还没聊过："+string.Join("、",remaining) : "大家都认识啦。再和 Z 交谈，邀请她一起看烟花。";
                    case FirstDayPhase.Fireworks: return "把今晚的光，留在心里。";
                    case FirstDayPhase.Home: return "这是留给你的海边小屋。靠近左侧的床按 E，和小岛道一声晚安。";
                    case FirstDayPhase.Bedtime: return "故事落幕 · 谢谢你来到小岛。";
                    case FirstDayPhase.Complete: return "这段故事已落幕。可以继续散步、交谈，或按 P 留下一张照片。";
                    default: return "";
                }
            }
        }
        public void Initialize(GameSession owner,WorldDirector director)
        {
            session=owner;world=director;
            cinema=gameObject.AddComponent<FirstDayCinema>();cinema.Initialize(owner);
            invitations=gameObject.AddComponent<FirstDayOverlay>();invitations.Initialize();
            var file=Resources.Load<TextAsset>("FirstDay/conversations");
            content=file!=null?JsonUtility.FromJson<FirstDayContent>(file.text):null;
        }
        public void ResetForStateChange()
        {
            generation++;StopAllCoroutines();cinema.Cancel();invitations.Hide();
            if(openingLive!=null){openingLive.Close();openingLive=null;}
            session.Memory?.View.ResetView();MusicDirector.Instance?.SetMoodOverride(null);
            busy=resuming=waitingInvitation=false;decorated=null;GameUI.Instance.SetTitleVisible(false);
        }
        public void ResumeAfterTravel(){StartCoroutine(ResumeWhenReady());}
        IEnumerator ResumeWhenReady(){yield return null;while(world.IsTransitioning)yield return null;Resume();}
        public void Resume()
        {
            if(resuming||session.IsAtTitle||!Active)return;
            resuming=true;
            switch(Phase)
            {
                case FirstDayPhase.Live: BeginLive();break;
                case FirstDayPhase.Arrival: PlayArrival();break;
                case FirstDayPhase.Fireworks: PlayFireworks();break;
                case FirstDayPhase.Home: if(session.CurrentPlace!=PlaceId.PlayerHome)StartCoroutine(GoHome());break;
                case FirstDayPhase.Bedtime: ShowCredits();break;
            }
            session.Memory?.Spawner.Reload();
            resuming=false;
        }
        void SetPhase(FirstDayPhase phase,string journal=null)
        {
            session.State.firstDay.phase=phase;
            if(journal!=null)session.Story.AddJournal(journal);
            session.NotifyChanged();session.Save();
        }
        void BeginLive()
        {
            busy=true;GameUI.Instance.CloseOverlayImmediately();GameUI.Instance.SetTitleVisible(true);Time.timeScale=0;
            openingLive=NativeLiveHost.Open(transform,OfferInvitation,_=>OfferInvitation());
            liveOpened=Time.unscaledTime;waitingInvitation=true;
            invitations.Show("出发前夜 · Z 的直播间","今晚，她想寄出一封特别的小岛邀请。", "先听 Z 聊一会儿…",OfferInvitation,null,true);
            invitations.SetActionEnabled(false,"先听 Z 聊一会儿…");
        }
        void OfferInvitation()
        {
            if(Phase!=FirstDayPhase.Live)return;
            waitingInvitation=false;
            invitations.Show("这封邀请，写给你。","Z：抽到你啦！如果最近的日子有些匆忙，就来岛上住一晚吧。\n\n明天我会在码头等你。不用准备什么，带上你自己就好。","收下邀请，向小岛出发",()=>
            {
                session.State.firstDay.invitationAccepted=true;
                if(openingLive!=null){openingLive.Close();openingLive=null;}
                SetPhase(FirstDayPhase.Arrival,"在 Z 的直播间收到了一封小岛邀请。她说，明天会在码头等我。");
                PlayArrival();
            },()=>
            {
                invitations.Show("小岛来信 · 邀请已收到","可以继续看直播，准备好了再出发。","带着邀请出发",OfferInvitation,null,true);
            });
        }
        void PlayArrival()
        {
            busy=true;GameUI.Instance.SetTitleVisible(true);
            MusicDirector.Instance?.SetMoodOverride("day");
            cinema.PlayArrival(()=>{MusicDirector.Instance?.SetMoodOverride(null);StartCoroutine(ArriveAtHarbor());});
        }
        IEnumerator ArriveAtHarbor()
        {
            session.State.period=(int)TimePeriod.Afternoon;session.Clock.Refresh();
            SetPhase(FirstDayPhase.Harbor,"船靠岸了。码头的风里，已经有人在喊我的名字。");
            yield return Travel(PlaceId.Harbor,-10);
            if(!travelSucceeded)yield break;
            session.Memory?.Spawner.Reload();
            GameUI.Instance.SetTitleVisible(false);busy=false;Time.timeScale=1;
            session.Story.Notify("A / D 行走 · E 交谈 · P 拍照 · Tab 手机");
        }
        IEnumerator Travel(PlaceId place,float x)
        {
            busy=true;Time.timeScale=1;travelSucceeded=false;
            session.Memory?.Conversations.CancelAll("第一天场景切换");
            world.Travel(place);
            while(world.IsTransitioning)yield return null;
            if(world.Stage==null||world.Stage.place!=place)
            {
                busy=false;GameUI.Instance.SetTitleVisible(false);
                session.Story.Notify("地点尚未准备好，请从暂停菜单读档重试。");yield break;
            }
            world.Player.GetComponent<PlayerController>().Teleport(new Vector2(x,.08f));world.Follow.Snap();
            session.State.playerX=x;session.Save();travelSucceeded=true;
            yield return null;
        }
        void Update()
        {
            if(session==null||session.IsAtTitle||!session.IsReady)return;
            if(waitingInvitation&&openingLive!=null&&invitations.Visible&&Phase==FirstDayPhase.Live)
            {
                float wait=12-(Time.unscaledTime-liveOpened);
                if(wait>0)invitations.SetActionEnabled(false,"小岛来信 · "+Mathf.CeilToInt(wait)+" 秒后打开");
                else invitations.SetActionEnabled(true,"打开小岛来信 →");
            }
            if(openingLive!=null && Phase==FirstDayPhase.Live && !invitations.IsModal && GameInput.Pressed(UnityEngine.InputSystem.Key.Escape))
                if(!openingLive.HandleBack())OfferInvitation();
            if(world.Stage==null||world.IsTransitioning)return;
            if(decorated!=world.Stage){decorated=world.Stage;Decorate();}
            if(Active&&Phase==FirstDayPhase.WalkToSquare&&session.CurrentPlace==PlaceId.Square)
            {
                session.State.period=(int)TimePeriod.Evening;session.Clock.Refresh();
                SetPhase(FirstDayPhase.MeetIslanders,"来到古树广场。烟花开始前，先认识今晚一起看烟花的人。");
                session.Memory?.Spawner.Reload();session.Story.Notify("广场上的每个人，都有一句想对你说的话。");
            }
            if(Active&&session.Memory?.Spawner!=null&&StagedCast(session.CurrentPlace)!=null)
                foreach(var npc in session.Memory.Spawner.Actors)if(npc!=null&&npc.Movement!=null){npc.Movement.SimulateDecisions=false;npc.Movement.SetInteractionHold(true);}
        }
        void Decorate()
        {
            if(session.State.firstDay==null)return;
            if(session.CurrentPlace==PlaceId.Harbor)
            {
                var door=new GameObject("Guest room doorway");door.transform.SetParent(world.Stage.transform,false);door.transform.position=new Vector3(-4,0,0);
                var i=door.AddComponent<Interactable>();i.id="firstday_home_door";i.label="回海边小屋";i.promptOffset=new Vector3(0,2.1f,0);world.Interactables.Add(i);
            }
        }
        public IEnumerable<SevenCharacter> StagedCast(PlaceId place)
        {
            if(!Active)return null;
            if(place==PlaceId.Harbor&&(Phase==FirstDayPhase.Harbor||Phase==FirstDayPhase.WalkToSquare))return SevenCharacterCatalog.Actors.Where(a=>a.id=="z");
            if(place==PlaceId.Square&&(Phase==FirstDayPhase.WalkToSquare||Phase==FirstDayPhase.MeetIslanders||Phase==FirstDayPhase.Fireworks))return SevenCharacterCatalog.Actors;
            return null;
        }
        public float CastPosition(string actor,PlaceId place)
        {
            if(place==PlaceId.Harbor)return -7.8f;
            int index=Array.IndexOf(Cast,actor);
            return -7.4f+index*2.05f;
        }
        public bool IsAvailable(string id)
        {
            if(id=="firstday_home_door")return Phase==FirstDayPhase.Home||Phase==FirstDayPhase.Complete;
            return true;
        }
        public string Prompt(string id)
        {
            if(id=="firstday_bed")return Phase==FirstDayPhase.Complete?"休息，迎接新的一天":"睡个好觉 · 播放片尾";
            if(id=="firstday_home_door")return "回海边小屋";
            if(id=="z"&&Phase==FirstDayPhase.MeetIslanders&&GreetedCount==Cast.Length)return "邀请 Z 一起看烟花";
            return null;
        }
        public bool Interact(string id)
        {
            if(id=="firstday_bed")
            {
                if(Phase==FirstDayPhase.Complete){session.State.period=3;session.Clock.Advance();return true;}
                if(Phase!=FirstDayPhase.Home)return true;
                busy=true;invitations.Show("把今天，轻轻放下。","海风还在窗外。今晚认识的那些名字，好像已经没有那么陌生了。\n\n躺下后将播放游戏片尾，回望岛上的风景与相遇。","晚安，小岛",()=>{SetPhase(FirstDayPhase.Bedtime,"在烟花结束后的夜里，第一次在小岛睡着。");ShowCredits();},()=>busy=false);return true;
            }
            if(id=="firstday_home_door"){StartCoroutine(ReturnHome());return true;}
            if(!Active)return false;
            if(id=="bench"||id=="lighthouse_bench"||id=="coffee") {session.Story.Notify("今晚和大家约好了看烟花。先慢慢逛，时间会等你。");return true;}
            if(id=="z"&&Phase==FirstDayPhase.Harbor&&session.CurrentPlace==PlaceId.Harbor)
            {
                Talk("harbor",()=>SetPhase(FirstDayPhase.WalkToSquare,"Z 在码头接到了我。往右穿过公共街，就是今晚看烟花的古树广场。"));return true;
            }
            if(Phase==FirstDayPhase.MeetIslanders&&session.CurrentPlace==PlaceId.Square&&Cast.Contains(id))
            {
                if(id=="z"&&GreetedCount==Cast.Length){Talk("fireworks_invite",AskFireworks);return true;}
                Talk("meet_"+id,()=>
                {
                    if(!session.State.firstDay.greeted.Contains(id))
                    {
                        session.State.firstDay.greeted.Add(id);
                        session.Story.AddJournal("在古树广场认识了"+SevenCharacterCatalog.Name(id)+"。");
                        session.Memory?.PublishStoryEvent("firstday_meet_"+id,"第一天，玩家在古树广场听完了"+SevenCharacterCatalog.Name(id)+"的自我介绍。",new[]{"player",id},true);
                    }
                    session.NotifyChanged();session.Save();
                    if(GreetedCount==Cast.Length)session.Story.Notify("大家都认识啦。再和 Z 说一声，就可以开始烟花了。");
                });return true;
            }
            return false;
        }
        void Talk(string id,Action completed)
        {
            var conversation=content?.conversations?.FirstOrDefault(c=>c.id==id);
            if(conversation==null||session.Memory?.View==null){session.Story.Notify("这段故事暂时没有准备好，请稍后再试。");return;}
            busy=true;var owner=session.State;int stamp=generation;
            session.Memory.View.OpenStory(conversation.actor,conversation.lines,finished=>{if(owner!=session.State||stamp!=generation)return;busy=false;if(finished)completed?.Invoke();});
        }
        void AskFireworks()
        {
            busy=true;invitations.Show("要开始今晚的烟花吗？","已经和广场上的每个人打过招呼。\n\nZ：那就把今天最后的一点光，留给我们吧。\n看完烟花，我带你回海边的小屋。","和大家一起抬头",()=>{SetPhase(FirstDayPhase.Fireworks,"大家在古树下站到了一起。Z 说，今晚的烟花也有一份是为我点亮的。");PlayFireworks();},()=>busy=false);
        }
        void PlayFireworks()
        {
            busy=true;session.State.period=(int)TimePeriod.Night;session.Clock.Refresh();GameUI.Instance.SetTitleVisible(true);
            MusicDirector.Instance?.SetMoodOverride("evening");
            cinema.PlayFireworks(()=>{MusicDirector.Instance?.SetMoodOverride(null);StartCoroutine(GoHome());});
        }
        IEnumerator GoHome()
        {
            SetPhase(FirstDayPhase.Home,"烟花散去，Z 把钥匙交给我。小屋的灯亮着，像是早就知道我会来。");
            yield return Travel(PlaceId.PlayerHome,4.2f);
            if(!travelSucceeded)yield break;
            GameUI.Instance.SetTitleVisible(false);busy=false;Time.timeScale=1;session.Memory?.Spawner.Reload();
            session.Story.Notify("欢迎回家。走近左侧的床，按 E 和小岛道一声晚安。");
        }
        IEnumerator ReturnHome(){yield return Travel(PlaceId.PlayerHome,4.2f);
            if(!travelSucceeded)yield break;busy=false;}
        void ShowCredits()
        {
            busy=true;GameUI.Instance.SetTitleVisible(true);
            MusicDirector.Instance?.SetMoodOverride("credits");
            cinema.PlayCredits(()=>
            {
                MusicDirector.Instance?.SetMoodOverride(null);
                session.State.day=2;session.State.period=0;session.State.firstDay.phase=FirstDayPhase.Complete;session.Clock.Refresh();
                SetPhase(FirstDayPhase.Complete,"故事落幕。希望在这个越来越快速的时代，找回一些原初的情感联系和交流。");
                busy=false;Time.timeScale=1;GameUI.Instance.SetTitleVisible(false);session.Memory?.Spawner.Reload();session.NotifyChanged();session.Save();
                session.Story.Notify("片尾已结束。现在可以继续小岛生活，照片和回忆都留在手机里。");
            });
        }
        void OnDestroy(){if(openingLive!=null)openingLive.Close();}
    }
}
