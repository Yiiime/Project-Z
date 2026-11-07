using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class MemoryGameController : MonoBehaviour
    {
        GameSession session;WorldDirector world;int lastDay,lastPeriod;bool changing;
        public MemoryLabClient Client {get;private set;}
        public SevenNpcSpawner Spawner {get;private set;}
        public NpcConversationCoordinator Conversations {get;private set;}
        public MemoryDialogueView View {get;private set;}
        public MemoryReply LastReply {get;private set;}
        public bool PlayerChatOpen=>(Conversations!=null&&Conversations.PlayerActor!=null)||(View!=null&&View.Closing);
        public void Initialize(GameSession s,WorldDirector w){session=s;world=w;if(s.State.memory==null)s.State.memory=new MemorySaveState();s.State.memory.Sanitize();Spawner=gameObject.AddComponent<SevenNpcSpawner>();Spawner.Initialize(s,w);View=gameObject.AddComponent<MemoryDialogueView>();View.Initialize(this,s,w);Client=gameObject.AddComponent<MemoryLabClient>();Client.Initialize(s);Conversations=gameObject.AddComponent<NpcConversationCoordinator>();Conversations.Initialize(this,s,w);var executor=gameObject.AddComponent<MemoryActionExecutor>();executor.Initialize(this,s,w);lastDay=s.State.day;lastPeriod=s.State.period;s.Clock.Changed+=ClockChanged;s.TravelRequested+=TravelRequested;Client.ConnectionChanged+=ConnectionChanged;}
        void ConnectionChanged(){if(!Client.CanGenerate)Conversations?.CancelAll("连接状态变化");}
        void ClockChanged(){if(changing||session.State.memory==null||lastDay==session.State.day&&lastPeriod==session.State.period)return;lastDay=session.State.day;lastPeriod=session.State.period;Conversations?.CancelAll("游戏时间变化");Append(new MemoryEvent{kind="clock"});}
        public void Append(MemoryEvent e){var state=session.State.memory;e.seq=++state.nextSeq;if(string.IsNullOrEmpty(e.id))e.id="evt_"+Guid.NewGuid().ToString("N");if(e.day==0){e.day=session.State.day;e.period=session.State.period;}state.outbox.Add(e);session.Save();}
        public static bool CanHear(Vector3 from,Vector3 to){return !Physics2D.LinecastAll(from+Vector3.up*1.5f,to+Vector3.up*1.5f,1<<8).Any(h=>h.collider!=null&&!h.collider.isTrigger&&h.collider.GetComponentInParent<NpcPresence>()==null&&h.collider.GetComponentInParent<PlayerController>()==null);}
        public string[] Listeners(Vector3 origin,string speaker,string recipient){var ids=Spawner.Actors.Where(a=>a!=null&&a.IsPresent&&a.gameObject.scene==world.Stage.gameObject.scene&&a!=Conversations?.PlayerActor&&Vector2.Distance(a.transform.position,origin)<=MemoryRuntimeConfig.Settings.hearingDistance&&CanHear(origin,a.transform.position)).Select(a=>a.npcId).ToList();if(!ids.Contains(speaker))ids.Add(speaker);if(!ids.Contains(recipient))ids.Add(recipient);if(world.Player!=null&&Vector2.Distance(world.Player.position,origin)<=MemoryRuntimeConfig.Settings.hearingDistance&&CanHear(origin,world.Player.position))ids.Add("player");return ids.Distinct().ToArray();}
        public string Location(NpcPresence npc){return SevenCharacterCatalog.Location(session.CurrentPlace,npc?.transform.position.x??world.Player.position.x,npc?.npcId);}
        public void CommitLine(string conversation,string speaker,string recipient,string text,string proposalId,string channel="face_to_face"){var origin=speaker=="player"?world.Player.position:Spawner.Find(speaker).transform.position;var observers=channel=="private_player_chat"?new[]{speaker,recipient}:Listeners(origin,speaker,recipient);Append(new MemoryEvent{kind="utterance",conversationId=conversation,speakerId=speaker,recipientId=recipient,text=text,proposalId=proposalId,channel=channel,observers=observers,location=session.CurrentPlace.ToString(),locationId=Location(speaker=="player"?Spawner.Find(recipient):Spawner.Find(speaker))});if(channel=="private_player_chat"||observers.Contains("player"))View.ShowLine(speaker,text,origin,channel=="private_player_chat");}
        public void RecordMeeting(NpcPresence a,NpcPresence b,string conversation){Append(new MemoryEvent{kind="observation",conversationId=conversation,title="岛民会面",text=SevenCharacterCatalog.Name(a.npcId)+"实际走近"+SevenCharacterCatalog.Name(b.npcId)+"，双方停下准备交流。",gist="岛民走近准备交流。",verified=true,observers=Listeners(a.transform.position,a.npcId,b.npcId),location=session.CurrentPlace.ToString(),locationId=Location(a)});}
        public void ShowResult(MemoryReply reply){LastReply=reply;View.ShowResult(reply);}
        public bool CanInteract(string id)=>Spawner!=null&&Spawner.Find(id)!=null;
        public void BeginPlayerChat(string id){var npc=Spawner.Find(id);if(npc!=null&&world.Player!=null&&Vector2.Distance(npc.transform.position,world.Player.position)<=2)Conversations.BeginPlayer(npc);}
        public void PublishStoryEvent(string stableId,string text,string[] observers,bool verified=false,string[] discloseTo=null){Append(new MemoryEvent{id=stableId,kind="story_event",text=text,observers=observers,verified=verified,discloseTo=discloseTo??observers,location=session.CurrentPlace.ToString(),locationId=Location(null)});}
        public void RespondTask(string taskId,bool accepted){Client.RespondTask(taskId,accepted,r=>Conversations.RequestPlayerReply());}
        public void InviteTask(string taskId,string actorId,string text){BeginPlayerChat(actorId);Client.InviteTask(taskId,actorId,text,r=>Conversations.RequestPlayerReply());}
        public void BeforeSave(){if(!changing)Spawner?.CapturePositions();}
        public void BeforeStateChange(){changing=true;Conversations?.ResetTimeline();GetComponent<MemoryActionExecutor>()?.ResetTimeline();}
        public void AfterStateChange(){if(session.State.memory==null)session.State.memory=new MemorySaveState();session.State.memory.Sanitize();changing=false;lastDay=session.State.day;lastPeriod=session.State.period;Spawner.Reload();Client.BeginWorld();View.ResetView();}
        void TravelRequested(PlaceId place){Spawner?.CapturePositions();Conversations?.CancelAll("切换场景");}
        void OnDestroy(){if(session!=null){session.Clock.Changed-=ClockChanged;session.TravelRequested-=TravelRequested;}if(Client!=null)Client.ConnectionChanged-=ConnectionChanged;}
    }
}
