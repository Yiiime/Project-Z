using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seabreeze
{
    // Each actor owns an ordered, persistent outbox. Movement reports never
    // enter the shared dialogue journal or become long-term memories.
    public sealed class NpcRuntimeReporter : MonoBehaviour
    {
        sealed class Watch
        {
            public NpcMovementController movement;public Action started;
            public Action<NpcMoveResult> finished;public string signature,conversation;
        }
        readonly Dictionary<NpcPresence,Watch> watches=new Dictionary<NpcPresence,Watch>();
        MemoryGameController memory;GameSession session;
        public void Initialize(MemoryGameController owner,GameSession game){memory=owner;session=game;}
        public NpcRuntimeSnapshot Capture(NpcPresence actor,string reason)
        {
            if(actor==null)return null;
            var movement=actor.Movement;
            string state=!actor.IsPresent?"unavailable":movement==null?"idle":movement.InteractionHeld?"held":movement.IsMoving?"moving":movement.LastResult?.Outcome.ToString().ToLowerInvariant()??"idle";
            return new NpcRuntimeSnapshot {
                sceneId=actor.location.ToString(),locationId=SevenCharacterCatalog.RuntimeLocation(actor.location,actor.transform.position.x),
                locationName=SevenCharacterCatalog.Map?.locations.FirstOrDefault(p=>p.place==actor.location)?.displayName??actor.location.ToString(),
                x=actor.transform.position.x,y=actor.transform.position.y,movementState=state,
                actionId=memory.Actions?.PendingActionId(actor.npcId)??movement?.ActiveRequestId??movement?.LastResult?.RequestId,
                targetLocationId=movement?.TargetId,conversationId=watches.TryGetValue(actor,out var watch)?watch.conversation:null,reason=reason,
            };
        }
        static string Signature(NpcRuntimeSnapshot snapshot)=>string.Join("|",snapshot.sceneId,snapshot.locationId,snapshot.movementState,snapshot.actionId,snapshot.targetLocationId,snapshot.conversationId);
        public void Report(NpcPresence actor,string reason,bool unavailable=false)
        {
            if(actor==null||!memory.RuntimeReportingEnabled||session.State?.memory==null)return;
            var snapshot=Capture(actor,reason);var state=session.State.memory;var cursor=state.RuntimeCursor(actor.npcId);
            if(unavailable)snapshot.movementState="unavailable";
            state.runtimeOutbox.Add(new NpcRuntimeEvent {id="runtime_"+Guid.NewGuid().ToString("N"),actorId=actor.npcId,
                seq=++cursor.nextSeq,day=session.State.day,period=session.State.period,snapshot=snapshot});
            if(watches.TryGetValue(actor,out var watch))watch.signature=Signature(snapshot);
            session.Save();
        }
        public void SceneUnloaded(NpcPresence actor)
        {Report(actor,"scene_unloaded",true);if(watches.ContainsKey(actor))Remove(actor);}
        public void Conversation(NpcPresence actor,string id,bool opened)
        {
            if(actor==null)return;Bind(actor);
            if(watches.TryGetValue(actor,out var watch))watch.conversation=opened?id:null;
            Report(actor,opened?"conversation_opened":"conversation_closed");
        }
        void Bind(NpcPresence actor)
        {
            if(watches.ContainsKey(actor)||actor.Movement==null||!actor.Movement.IsReady)return;
            var watch=new Watch {movement=actor.Movement};
            watch.started=()=>Report(actor,"movement_started");
            watch.finished=result=>Report(actor,"movement_"+result.Outcome.ToString().ToLowerInvariant());
            watches.Add(actor,watch);watch.movement.MovementStarted+=watch.started;watch.movement.MovementFinished+=watch.finished;
        }
        void LateUpdate()
        {
            if(memory?.Spawner==null||!memory.RuntimeReportingEnabled||session?.IsReady!=true)return;
            foreach(var dead in watches.Keys.Where(a=>a==null||!memory.Spawner.Actors.Contains(a)).ToArray())Remove(dead);
            foreach(var actor in memory.Spawner.Actors)
            {
                if(actor==null)continue;Bind(actor);
                if(!watches.TryGetValue(actor,out var watch))continue;
                var snapshot=Capture(actor,"state_changed");
                if(watch.signature!=Signature(snapshot))Report(actor,watch.signature==null?"spawned":"state_changed");
            }
        }
        void Remove(NpcPresence actor)
        {
            var watch=watches[actor];
            if(watch.movement!=null){watch.movement.MovementStarted-=watch.started;watch.movement.MovementFinished-=watch.finished;}
            watches.Remove(actor);
        }
        public void ResetTimeline(){foreach(var actor in watches.Keys.ToArray())Remove(actor);}
        void OnDestroy()=>ResetTimeline();
    }
}
