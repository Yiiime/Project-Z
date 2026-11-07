using System;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class MemoryGameController : MonoBehaviour
    {
        GameSession session;WorldDirector world;
        int lastDay,lastPeriod;bool changing;
        public MemoryLabClient Client {get;private set;}
        public SevenNpcSpawner Spawner {get;private set;}
        public NpcConversationCoordinator Conversations {get;private set;}
        public MemoryDialogueView View {get;private set;}
        public bool PlayerChatOpen => (Conversations!=null&&Conversations.PlayerActor!=null)||(View!=null&&View.Closing);
        public void Initialize(GameSession s,WorldDirector w)
        {
            session=s;world=w;
            if(session.State.memory==null)session.State.memory=new MemorySaveState();
            session.State.memory.Sanitize();
            Spawner=gameObject.AddComponent<SevenNpcSpawner>();Spawner.Initialize(s,w);
            View=gameObject.AddComponent<MemoryDialogueView>();View.Initialize(this,s,w);
            Client=gameObject.AddComponent<MemoryLabClient>();Client.Initialize(s);
            Conversations=gameObject.AddComponent<NpcConversationCoordinator>();Conversations.Initialize(this,s,w);
            lastDay=s.State.day;lastPeriod=s.State.period;
            s.Clock.Changed+=ClockChanged;s.TravelRequested+=TravelRequested;
            Client.ConnectionChanged+=ConnectionChanged;EnsureDay4();
        }
        void ConnectionChanged(){if(!Client.CanGenerate)Conversations?.Cancel("连接状态改变");}
        void ClockChanged()
        {
            if(changing||session.State.memory==null)return;
            if(lastDay==session.State.day&&lastPeriod==session.State.period)return;
            lastDay=session.State.day;lastPeriod=session.State.period;
            Conversations?.Cancel("游戏时间改变");
            Append(new MemoryEvent { kind="clock" });EnsureDay4();
        }
        public void EnsureDay4()
        {
            if(changing||session?.State?.memory==null||View==null||session.State.day<4)return;
            var state=session.State.memory;
            state.publicDay4KnownBy=SevenCharacterCatalog.Actors.Select(a=>a.id).Concat(new[]{"player"}).Distinct().ToArray();
            if(!state.day4Published)
            {
                state.day4Published=true;
                session.Story.AddJournal("Day4 全岛公告：小岛将要毁灭。所有角色收到公开消息，未公布具体期限。");
                Append(new MemoryEvent { id=state.worldId+"_day4",kind="day4",day=4,period=0 });
            }
            if(!state.day4Shown)View.ShowDay4(()=>{if(session.State.memory==state){state.day4Shown=true;session.Save();}});
        }
        public void Append(MemoryEvent e)
        {
            var state=session.State.memory;
            e.personaRevision=2;
            e.seq=++state.nextSeq;
            if(string.IsNullOrEmpty(e.id))e.id="evt_"+Guid.NewGuid().ToString("N");
            if(e.day==0){e.day=session.State.day;e.period=session.State.period;}
            state.outbox.Add(e);session.Save();
        }
        public string[] Listeners(Vector3 origin,string speaker,string recipient)
        {
            var heard=Spawner.Actors.Where(a=>a!=null&&a.IsPresent&&a.gameObject.scene==world.Stage.gameObject.scene
                &&Vector2.Distance(a.transform.position,origin)<=MemoryRuntimeConfig.Settings.hearingDistance
                &&CanHear(origin,a.transform.position)).Select(a=>a.npcId).ToList();
            if(!heard.Contains(speaker))heard.Add(speaker);
            if(!heard.Contains(recipient))heard.Add(recipient);
            if(PlayerCanHear(origin))heard.Add("player");
            return heard.Distinct().ToArray();
        }
        public bool PlayerCanHear(Vector3 origin)=>world?.Player!=null&&Vector2.Distance(world.Player.position,origin)<=MemoryRuntimeConfig.Settings.hearingDistance&&CanHear(origin,world.Player.position);
        static bool CanHear(Vector3 from,Vector3 to)
        {
            var hits=Physics2D.LinecastAll(from+Vector3.up*1.5f,to+Vector3.up*1.5f,1<<8);
            return !hits.Any(h=>h.collider!=null&&!h.collider.isTrigger&&h.collider.GetComponentInParent<NpcPresence>()==null&&h.collider.GetComponentInParent<PlayerController>()==null);
        }
        public void CommitLine(string conversation,string speaker,string recipient,string text,string proposalId)
        {
            Vector3 origin=speaker=="player"?world.Player.position:Spawner.Find(speaker).transform.position;
            var observers=Listeners(origin,speaker,recipient);
            Append(new MemoryEvent { kind="utterance",conversationId=conversation,speakerId=speaker,recipientId=recipient,text=text,proposalId=proposalId,
                observers=observers,location=session.CurrentPlace.ToString() });
            bool playerConversation=speaker=="player"||recipient=="player";
            // Screen presentation is independent from the witnesses used by memory.
            if(!playerConversation||observers.Contains("player"))View.ShowLine(speaker,text,origin,playerConversation);
        }
        public void RecordMeeting(NpcPresence first,NpcPresence second)
        {
            Append(new MemoryEvent { kind="observation",title="岛民会面",text=SevenCharacterCatalog.Name(first.npcId)+"走到"+SevenCharacterCatalog.Name(second.npcId)+"身边，双方停下并准备交谈。",
                gist="岛民走近，准备交谈。",observers=Listeners(first.transform.position,first.npcId,second.npcId),location=session.CurrentPlace.ToString() });
        }
        public bool CanInteract(string id)=>Spawner!=null&&Spawner.Find(id)!=null;
        public void BeginPlayerChat(string id)
        {
            var npc=Spawner.Find(id);
            if(npc==null||world.Player==null||Vector2.Distance(npc.transform.position,world.Player.position)>2)return;
            Conversations.BeginPlayer(npc);
        }
        public void BeforeSave(){if(!changing)Spawner?.CapturePositions();}
        public void BeforeStateChange(){changing=true;Conversations?.ResetTimeline();}
        public void AfterStateChange()
        {
            if(session.State.memory==null)session.State.memory=new MemorySaveState();
            session.State.memory.Sanitize();
            changing=false;lastDay=session.State.day;lastPeriod=session.State.period;
            Spawner.Reload();Client.BeginWorld();View.ResetView();EnsureDay4();
        }
        void TravelRequested(PlaceId place){Spawner?.CapturePositions();Conversations?.Cancel("切换场景");}
        void OnDestroy()
        {
            if(session!=null){session.Clock.Changed-=ClockChanged;session.TravelRequested-=TravelRequested;}
            if(Client!=null)Client.ConnectionChanged-=ConnectionChanged;
        }
    }
}
