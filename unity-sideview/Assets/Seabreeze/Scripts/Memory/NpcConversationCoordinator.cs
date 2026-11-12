using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class NpcConversationCoordinator : MonoBehaviour
    {
        sealed class Conversation
        {
            public string id,stage;
            public NpcPresence first,second;
            public bool player,cancelled,sending;
            public float started;
            public int playerTurns, npcReplies;
            public string lastEvidence;
            public readonly Dictionary<NpcPresence,bool> previous = new Dictionary<NpcPresence,bool>();
        }
        MemoryGameController memory; GameSession session; WorldDirector world;
        readonly Dictionary<string,Conversation> sessions = new Dictionary<string,Conversation>();
        readonly Dictionary<NpcPresence,Conversation> held = new Dictionary<NpcPresence,Conversation>();
        readonly Dictionary<string,float> cooldown = new Dictionary<string,float>();
        Conversation player; float nextDecision; int planner;
        public NpcPresence PlayerActor => player?.first;
        public NpcPresence First => sessions.Values.FirstOrDefault(c=>!c.player)?.first;
        public NpcPresence Second => sessions.Values.FirstOrDefault(c=>!c.player)?.second;
        public string ActiveId => player?.id ?? sessions.Values.FirstOrDefault()?.id ?? "";
        public string Stage => player?.stage ?? sessions.Values.FirstOrDefault()?.stage ?? "等待";
        public bool Sending => player?.sending ?? false;
        public int CompletedConversations {get; private set;}
        public int CommittedNpcLines {get; private set;}
        public string LastIntentReason {get; private set;}="";
        public string[] LastRecalledMemoryIds {get; private set;}=Array.Empty<string>();
        bool WorldReady => session!=null&&session.IsReady&&world?.Stage!=null&&!world.IsTransitioning&&!session.NonPlayerInputBlocked&&Time.timeScale>0;
        bool Present(NpcPresence a)=>a!=null&&a.IsPresent&&a.isActiveAndEnabled&&a.Movement!=null&&a.Movement.IsReady&&a.gameObject.scene==world.Stage.gameObject.scene;
        bool Eligible(NpcPresence a)=>Present(a)&&!held.ContainsKey(a)&&!a.Movement.InteractionHeld;
        static string Pair(string a,string b)=>string.CompareOrdinal(a,b)<0?a+"_"+b:b+"_"+a;
        bool Cooling(NpcPresence a,NpcPresence b)=>cooldown.TryGetValue(Pair(a.npcId,b.npcId),out float until)&&until>Time.unscaledTime;
        Vector2 Point(NpcPresence a,NpcPresence b)=>new Vector2(b.transform.position.x+(a.transform.position.x<=b.transform.position.x?-1.45f:1.45f),b.transform.position.y);
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w){memory=m;session=s;world=w;nextDecision=Time.unscaledTime+4;}
        void Update()
        {
            foreach(var c in sessions.Values.ToArray())if(!WorldReady||!Present(c.first)||(!c.player&&c.stage!="选择是否交谈"&&!Present(c.second))||Time.unscaledTime-c.started>MemoryRuntimeConfig.Settings.conversationTimeout)Finish(c,false);
            if(!WorldReady||!memory.Client.CanGenerate||Time.unscaledTime<nextDecision||sessions.Values.Any(c=>!c.player))return;
            nextDecision=Time.unscaledTime+MemoryRuntimeConfig.Settings.decisionInterval;
            var actors=memory.Spawner.Actors.Where(Eligible).ToArray();if(actors.Length<2)return;
            for(int n=0;n<actors.Length;n++)
            {
                var first=actors[(planner++)%actors.Length];
                var candidates=actors.Where(a=>a!=first&&!Cooling(first,a)&&MemoryGameController.CanHear(first.transform.position,a.transform.position)&&first.Movement.CanReachConversationPoint(Point(first,a))).ToArray();
                if(candidates.Length==0)continue;
                var c=new Conversation{id="npc_"+Guid.NewGuid().ToString("N"),first=first,stage="选择是否交谈",started=Time.unscaledTime};sessions[c.id]=c;Hold(c,first);StartCoroutine(Autonomous(c,candidates));break;
            }
        }
        void Hold(Conversation c,NpcPresence a){c.previous[a]=a.Movement.SimulateDecisions;held[a]=c;a.Movement.SimulateDecisions=false;a.Movement.SetInteractionHold(true);}
        bool Valid(Conversation c)=>!c.cancelled&&sessions.ContainsKey(c.id)&&WorldReady&&Present(c.first)&&(c.player||Present(c.second));
        IEnumerator Autonomous(Conversation c,NpcPresence[] candidates)
        {
            bool done=false;MemoryReply decision=null;string error=null;
            var request=memory.Client.RequestBase();request.actorId=c.first.npcId;request.conversationId=c.id;request.scene=session.CurrentPlace.ToString();request.locationId=memory.Location(c.first);request.candidates=candidates.Select(a=>a.npcId).ToArray();
            memory.Client.Generate("/api/game/intent",request,r=>{decision=r;done=true;},e=>{error=e;done=true;});
            while(!done&&!c.cancelled)yield return null;
            if(c.cancelled)yield break;
            c.second=candidates.FirstOrDefault(a=>Eligible(a)&&a.npcId==decision?.targetId);
            if(error!=null||c.second==null){Finish(c,false);yield break;}
            LastIntentReason=decision.reason;Hold(c,c.second);c.first.ConversationPartner=c.second.transform;c.second.ConversationPartner=c.first.transform;c.stage="走近";
            c.first.Movement.SetInteractionHold(false);
            if(!c.first.Movement.MoveForConversation(Point(c.first,c.second),c.second.transform.position.x>c.first.transform.position.x?1:-1)){Finish(c,false);yield break;}
            float until=Time.unscaledTime+MemoryRuntimeConfig.Settings.approachTimeout;
            while(!c.cancelled&&Present(c.first)&&Present(c.second)&&c.first.Movement.IsMoving&&Time.unscaledTime<until)yield return null;
            if(c.cancelled)yield break;
            if(!Valid(c)||Vector2.Distance(c.first.transform.position,c.second.transform.position)>1.75f){Finish(c,false);yield break;}
            c.first.Movement.SetInteractionHold(true);c.stage="交谈";memory.RecordMeeting(c.first,c.second,c.id);
            for(int turn=0;turn<MemoryRuntimeConfig.Settings.maxLines;turn++)
            {
                while(Valid(c)&&!memory.Client.CanGenerate)yield return null;
                if(!Valid(c)){Finish(c,false);yield break;}
                var speaker=turn%2==0?c.first:c.second;var recipient=turn%2==0?c.second:c.first;done=false;MemoryReply line=null;error=null;
                request=memory.Client.RequestBase();request.actorId=speaker.npcId;request.recipientId=recipient.npcId;request.conversationId=c.id;request.scene=session.CurrentPlace.ToString();request.locationId=memory.Location(speaker);request.topic=decision.topic;request.turn=turn;request.channel="face_to_face";request.reachableLocations=SevenCharacterCatalog.Reachable(session.CurrentPlace,request.locationId);
                request.observers=memory.Listeners(speaker.transform.position,speaker.npcId,recipient.npcId);
                memory.Client.Generate("/api/game/utterance",request,r=>{line=r;done=true;},e=>{error=e;done=true;});
                while(!done&&!c.cancelled)yield return null;
                if(c.cancelled)yield break;
                if(error!=null||line==null||!Valid(c)||Vector2.Distance(speaker.transform.position,recipient.transform.position)>2){Finish(c,false);yield break;}
                // The model planned for the submitted audience. A new listener
                // changes what may be said; discard before presentation/receipt.
                if(memory.Listeners(speaker.transform.position,speaker.npcId,recipient.npcId).Except(request.observers).Any()){Finish(c,false);yield break;}
                string dialogue=line.result?.dialogue??line.text;string expression=line.result?.expression??line.expression;
                if(string.IsNullOrEmpty(dialogue)){memory.Client.CommitDecision(line,speaker.npcId,request.observers,request.locationId);break;}
                memory.ShowResult(line);memory.CommitLine(c.id,speaker.npcId,recipient.npcId,dialogue,line.proposalId,"face_to_face",expression);LastRecalledMemoryIds=line.recalledMemoryIds??Array.Empty<string>();CommittedNpcLines++;
                float visibleUntil=Time.unscaledTime+Mathf.Clamp(1.3f+dialogue.Length*.05f,2.5f,6);
                while(!c.cancelled&&Time.unscaledTime<visibleUntil)yield return null;
                if(c.cancelled)yield break;
                if(line.endConversation)break;
            }
            Finish(c,true);
        }
        public void BeginPlayer(NpcPresence npc)
        {
            if(!Present(npc))return;
            if(player!=null)Finish(player,false);
            if(held.TryGetValue(npc,out var previous))Finish(previous,false);
            var c=new Conversation{id="player_"+Guid.NewGuid().ToString("N"),first=npc,player=true,stage="玩家交谈",started=Time.unscaledTime};player=c;sessions[c.id]=c;Hold(c,npc);npc.ConversationPartner=world.Player;
            memory.Client.Focus(npc.npcId);memory.View.OpenPlayer(npc.npcId);
            if (session.FirstDay?.Active ?? false)
            {
                memory.PublishStoryEvent("encounter_" + c.id, PlayerIdentity.DisplayName + "实际走近了" + SevenCharacterCatalog.Name(npc.npcId) + "，准备交谈。", new[] { "player", npc.npcId }, true);
                c.sending = true; StartCoroutine(PlayerReply(c));
            }
        }
        public void SendPlayer(string message)
        {
            var c=player;if(c==null||c.sending||!memory.Client.CanQueuePlayerInput||string.IsNullOrWhiteSpace(message))return;
            memory.Client.StopSpeech();
            c.playerTurns++; c.sending=true;c.started=Time.unscaledTime;memory.CommitLine(c.id,"player",c.first.npcId,message.Trim(),"","private_player_chat");StartCoroutine(PlayerReply(c));
        }
        IEnumerator PlayerReply(Conversation c)
        {
            while(Valid(c)&&!memory.Client.CanGenerate)yield return null;if(!Valid(c))yield break;
            bool done=false;MemoryReply line=null;string error=null;var request=memory.Client.RequestBase();request.actorId=c.first.npcId;request.recipientId="player";request.conversationId=c.id;request.scene=session.CurrentPlace.ToString();request.locationId=memory.Location(c.first);request.channel="private_player_chat";request.reachableLocations=SevenCharacterCatalog.Reachable(session.CurrentPlace,request.locationId);
            memory.Client.Generate("/api/game/utterance",request,r=>{line=r;done=true;},e=>{error=e;done=true;});
            while(!done&&!c.cancelled)yield return null;if(c.cancelled)yield break;
            if(error==null&&line!=null&&Valid(c))
            {
                memory.ShowResult(line);
                string dialogue=line.result?.dialogue??line.text;string expression=line.result?.expression??line.expression;
                if(!string.IsNullOrEmpty(dialogue)){memory.CommitLine(c.id,c.first.npcId,"player",dialogue,line.proposalId,"private_player_chat",expression);c.npcReplies++; c.lastEvidence=session.State.memory.outbox.LastOrDefault()?.id; CommittedNpcLines++;memory.View.FollowSpeech();memory.Client.Speak(line,c.id,c.first.npcId,()=>memory.View.ReadyForSpeech);}
                else memory.Client.CommitDecision(line,c.first.npcId,new[]{c.first.npcId,"player"},request.locationId);
                LastRecalledMemoryIds=line.recalledMemoryIds??Array.Empty<string>();
            }
            c.sending=false;
        }
        public void RequestPlayerReply(){if(player!=null&&!player.sending){memory.Client.StopSpeech();player.sending=true;StartCoroutine(PlayerReply(player));}}
        public void Cancel(string reason="玩家结束交谈"){if(player!=null)Finish(player,false,reason=="玩家结束交谈");else foreach(var c in sessions.Values.ToArray())Finish(c,false);}
        public void CancelAll(string reason="世界状态变化"){foreach(var c in sessions.Values.ToArray())Finish(c,false);}
        public void ResetTimeline(){CancelAll();cooldown.Clear();planner=0;LastIntentReason="";LastRecalledMemoryIds=Array.Empty<string>();CompletedConversations=CommittedNpcLines=0;}
        void Finish(Conversation c,bool completed,bool userClosed=false)
        {
            if(c==null||c.cancelled)return;
            if (userClosed && c.player && c.playerTurns > 0 && c.npcReplies > 1 && !c.sending && memory.View.LineFullyVisible)
                session.FirstDay?.ConversationCompleted(c.first.npcId, c.id, c.lastEvidence);
            c.cancelled=true;memory.Client?.CancelConversation(c.id);
            if(c.second!=null)cooldown[Pair(c.first.npcId,c.second.npcId)]=Time.unscaledTime+MemoryRuntimeConfig.Settings.pairCooldown;
            foreach(var old in c.previous)if(old.Key!=null&&old.Key.Movement!=null){held.Remove(old.Key);old.Key.ConversationPartner=null;old.Key.Movement.Interrupt("conversation_finished");old.Key.Movement.SetInteractionHold(false);old.Key.Movement.SimulateDecisions=old.Value;}
            sessions.Remove(c.id);if(completed)CompletedConversations++;
            if(player==c){player=null;memory.Client.Focus(null);memory.View.HidePlayer();}
        }
        void OnDestroy(){CancelAll();}
    }
}
