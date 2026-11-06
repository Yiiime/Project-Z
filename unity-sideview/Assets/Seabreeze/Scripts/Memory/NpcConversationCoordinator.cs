using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class NpcConversationCoordinator : MonoBehaviour
    {
        MemoryGameController memory;GameSession session;WorldDirector world;
        readonly Dictionary<string,float> pairCooldown=new Dictionary<string,float>();
        readonly Dictionary<NpcPresence,bool> oldSimulation=new Dictionary<NpcPresence,bool>();
        string conversation="",stage="等待";NpcPresence first,second;int plannerIndex,operation;
        float nextDecision,startedAt;bool running,playerSending,shuttingDown;
        public NpcPresence PlayerActor {get;private set;}
        public int CompletedConversations {get;private set;}
        public int CommittedNpcLines {get;private set;}
        public string LastIntentReason {get;private set;}="";
        public string[] LastRecalledMemoryIds {get;private set;}=Array.Empty<string>();
        public string Stage => stage;
        public string ActiveId => conversation;
        public NpcPresence First => first;
        public NpcPresence Second => second;
        public bool Sending => playerSending;
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w){memory=m;session=s;world=w;nextDecision=Time.unscaledTime+4;}
        bool WorldReady=>session!=null&&session.IsReady&&world!=null&&world.Stage!=null&&!world.IsTransitioning&&!session.NonPlayerInputBlocked&&Time.timeScale>0;
        bool Present(NpcPresence a)=>a!=null&&a.IsPresent&&a.isActiveAndEnabled&&a.Movement!=null&&a.Movement.IsReady&&world.Stage!=null&&a.gameObject.scene==world.Stage.gameObject.scene;
        bool Eligible(NpcPresence a)=>Present(a)&&!a.Movement.InteractionHeld;
        static string Pair(string a,string b)=>string.CompareOrdinal(a,b)<0?a+"_"+b:b+"_"+a;
        bool Cooling(string a,string b)=>pairCooldown.TryGetValue(Pair(a,b),out float until)&&until>Time.unscaledTime;
        Vector2 MeetingPoint(NpcPresence a,NpcPresence b)
        {
            float side=a.transform.position.x<=b.transform.position.x?-1:1;
            return new Vector2(b.transform.position.x+side*1.45f,b.transform.position.y);
        }
        void Update()
        {
            if(running&&(!WorldReady||!Present(first)||(PlayerActor==null&&!Present(second))||Time.unscaledTime-startedAt>MemoryRuntimeConfig.Settings.conversationTimeout))
            {Cancel("参与者离开或等待超时");return;}
            if(running||!WorldReady||!memory.Client.CanGenerate||Time.unscaledTime<nextDecision)return;
            nextDecision=Time.unscaledTime+MemoryRuntimeConfig.Settings.decisionInterval;
            var available=memory.Spawner.Actors.Where(Eligible).ToArray();if(available.Length<2)return;
            for(int n=0;n<available.Length;n++)
            {
                var actor=available[(plannerIndex++)%available.Length];
                var candidates=available.Where(b=>b!=actor&&!Cooling(actor.npcId,b.npcId)&&actor.Movement.CanReachConversationPoint(MeetingPoint(actor,b))).ToArray();
                if(candidates.Length==0)continue;
                first=actor;second=candidates[0];running=true;stage="选择交谈对象";startedAt=Time.unscaledTime;
                int token=++operation;StartCoroutine(Autonomous(actor,candidates,token));break;
            }
        }
        IEnumerator Autonomous(NpcPresence actor,NpcPresence[] candidates,int token)
        {
            MemoryReply decision=null;bool done=false;string error=null;
            var request=memory.Client.RequestBase();request.actorId=actor.npcId;request.scene=session.CurrentPlace.ToString();request.candidates=candidates.Select(a=>a.npcId).ToArray();
            memory.Client.Generate("/api/game/intent",request,r=>{decision=r;done=true;},e=>{error=e;done=true;});
            while(!done&&token==operation)yield return null;
            if(token!=operation)yield break;
            if(error!=null||decision==null||string.IsNullOrEmpty(decision.targetId)){Finish(false);yield break;}
            second=candidates.FirstOrDefault(a=>Eligible(a)&&a.npcId==decision.targetId);
            if(!Eligible(actor)||second==null||Cooling(actor.npcId,second.npcId)){Finish(false);yield break;}
            LastIntentReason=decision.reason;conversation="talk_"+Guid.NewGuid().ToString("N");
            Hold(actor);Hold(second);first.ConversationPartner=second.transform;second.ConversationPartner=first.transform;
            stage="走近";var target=MeetingPoint(first,second);first.Movement.SetInteractionHold(false);
            if(!first.Movement.MoveForConversation(target,second.transform.position.x>first.transform.position.x?1:-1)){Finish(false);yield break;}
            float timeout=Time.unscaledTime+MemoryRuntimeConfig.Settings.approachTimeout;
            while(token==operation&&Present(first)&&Present(second)&&first.Movement.IsMoving&&Time.unscaledTime<timeout)yield return null;
            if(token!=operation)yield break;
            if(!Present(first)||!Present(second)||Vector2.Distance(first.transform.position,second.transform.position)>1.75f){Finish(false);yield break;}
            first.Movement.SetInteractionHold(true);stage="交谈";memory.RecordMeeting(first,second);
            for(int turn=0;turn<MemoryRuntimeConfig.Settings.maxLines;turn++)
            {
                while(token==operation&&WorldReady&&!memory.Client.CanGenerate&&Time.unscaledTime-startedAt<MemoryRuntimeConfig.Settings.conversationTimeout)yield return null;
                if(token!=operation)yield break;
                if(!WorldReady||!memory.Client.CanGenerate||!Present(first)||!Present(second)){Finish(false);yield break;}
                var speaker=turn%2==0?first:second;var recipient=turn%2==0?second:first;
                MemoryReply line=null;done=false;error=null;
                request=memory.Client.RequestBase();request.actorId=speaker.npcId;request.recipientId=recipient.npcId;request.scene=session.CurrentPlace.ToString();request.conversationId=conversation;request.topic=decision.topic;request.turn=turn;
                request.observers=memory.Listeners(speaker.transform.position,speaker.npcId,recipient.npcId);
                memory.Client.Generate("/api/game/utterance",request,r=>{line=r;done=true;},e=>{error=e;done=true;});
                while(!done&&token==operation)yield return null;
                if(token!=operation)yield break;
                if(error!=null||line==null||!Present(first)||!Present(second)||!WorldReady||Vector2.Distance(first.transform.position,second.transform.position)>2){Finish(false);yield break;}
                memory.CommitLine(conversation,speaker.npcId,recipient.npcId,line.text,line.proposalId);LastRecalledMemoryIds=line.recalledMemoryIds??Array.Empty<string>();CommittedNpcLines++;
                float displayUntil=Time.unscaledTime+Mathf.Clamp(1.3f+line.text.Length*.05f,2.5f,6);
                while(token==operation&&Time.unscaledTime<displayUntil)yield return null;
                if(token!=operation)yield break;
                if(line.endConversation)break;
                // Finish on a complete exchange when model round-trips have
                // consumed the budget, instead of cutting off the other side.
                if(turn%2==1&&Time.unscaledTime-startedAt>MemoryRuntimeConfig.Settings.conversationTimeout*.45f)break;
            }
            Finish(true);
        }
        void Hold(NpcPresence actor)
        {
            oldSimulation[actor]=actor.Movement.SimulateDecisions;actor.Movement.SimulateDecisions=false;actor.Movement.SetInteractionHold(true);
        }
        public void BeginPlayer(NpcPresence npc)
        {
            Cancel("玩家介入");if(!Present(npc))return;
            PlayerActor=first=npc;second=null;running=true;stage="玩家交谈";startedAt=Time.unscaledTime;conversation="player_"+Guid.NewGuid().ToString("N");
            Hold(npc);npc.ConversationPartner=world.Player;memory.View.OpenPlayer(npc.npcId);
        }
        public void SendPlayer(string message)
        {
            if(PlayerActor==null||playerSending||!memory.Client.CanGenerate||string.IsNullOrWhiteSpace(message))return;
            playerSending=true;startedAt=Time.unscaledTime;
            memory.CommitLine(conversation,"player",PlayerActor.npcId,message.Trim(),"");
            StartCoroutine(PlayerReply(operation));
        }
        IEnumerator PlayerReply(int token)
        {
            while(token==operation&&PlayerActor!=null&&!memory.Client.CanGenerate&&Time.unscaledTime-startedAt<MemoryRuntimeConfig.Settings.conversationTimeout)yield return null;
            if(token!=operation||PlayerActor==null)yield break;
            if(!memory.Client.CanGenerate){playerSending=false;yield break;}
            bool done=false;MemoryReply line=null;string error=null;
            var request=memory.Client.RequestBase();request.actorId=PlayerActor.npcId;request.recipientId="player";request.conversationId=conversation;request.scene=session.CurrentPlace.ToString();request.topic="与玩家的交流";
            request.observers=memory.Listeners(PlayerActor.transform.position,PlayerActor.npcId,"player");
            memory.Client.Generate("/api/game/utterance",request,r=>{line=r;done=true;},e=>{error=e;done=true;});
            while(!done&&token==operation)yield return null;
            if(token!=operation)yield break;
            if(error==null&&line!=null&&Present(PlayerActor)&&WorldReady){memory.CommitLine(conversation,PlayerActor.npcId,"player",line.text,line.proposalId);LastRecalledMemoryIds=line.recalledMemoryIds??Array.Empty<string>();CommittedNpcLines++;}
            playerSending=false;
            if(line!=null&&line.endConversation){yield return new WaitForSecondsRealtime(3);if(token==operation)Finish(true);}
        }
        public void Cancel(string reason="会话取消") {operation++;StopAllCoroutines();Finish(false);}
        public void ResetTimeline(){Cancel("载入时间线");pairCooldown.Clear();plannerIndex=0;LastIntentReason="";LastRecalledMemoryIds=Array.Empty<string>();CompletedConversations=CommittedNpcLines=0;}
        void Finish(bool completed)
        {
            if(!shuttingDown&&!string.IsNullOrEmpty(conversation)&&memory.Client!=null)memory.Client.CancelConversation(conversation);
            if(first!=null&&second!=null)pairCooldown[Pair(first.npcId,second.npcId)]=Time.unscaledTime+MemoryRuntimeConfig.Settings.pairCooldown;
            foreach(var entry in oldSimulation)if(entry.Key!=null&&entry.Key.Movement!=null)
            {
                entry.Key.ConversationPartner=null;entry.Key.Movement.Interrupt("conversation_finished");entry.Key.Movement.SetInteractionHold(false);entry.Key.Movement.SimulateDecisions=entry.Value;
            }
            oldSimulation.Clear();if(completed)CompletedConversations++;
            first=second=PlayerActor=null;running=playerSending=false;conversation="";stage="等待";
            nextDecision=Time.unscaledTime+MemoryRuntimeConfig.Settings.decisionInterval;
            if(memory.View!=null)memory.View.HidePlayer();
        }
        void OnDestroy(){shuttingDown=true;if(memory!=null)Cancel("退出");}
    }
}
