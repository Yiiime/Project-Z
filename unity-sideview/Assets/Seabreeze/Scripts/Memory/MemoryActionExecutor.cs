using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Seabreeze
{
    // A pending action owns locomotion until a real receipt, cancellation or
    // failure. Conversation holds pause this owner; they do not complete it.
    public sealed class MemoryActionExecutor : MonoBehaviour
    {
        sealed class Running
        {
            public string id,actorId,target;public bool escort,previous,started;
            public NpcPresence actor;public float activeSeconds,retrySeconds;
            public Vector2 requestedPoint;
        }
        readonly Dictionary<string,Running> running=new Dictionary<string,Running>();
        readonly HashSet<string> completed=new HashSet<string>();
        MemoryGameController memory;GameSession session;WorldDirector world;
        string checkpoint;JObject cached;
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w){memory=m;session=s;world=w;}
        JObject Checkpoint()
        {
            string text=session?.State?.memory?.checkpoint;
            if(text!=checkpoint){checkpoint=text;try{cached=string.IsNullOrEmpty(text)?null:JObject.Parse(text);}catch{cached=null;}}
            return cached;
        }
        IEnumerable<JToken> Pending=> (Checkpoint()?["records"]?["action"]??new JArray()).Where(a=>(string)a["status"]=="pending");
        public bool HasPending(string actorId)=>Pending.Any(a=>(string)a["actor"]==actorId);
        public bool HasPendingMove(string actorId)=>Pending.Any(a=>(string)a["actor"]==actorId&&new[]{"move","escort_player"}.Contains((string)a["type"]));
        public string PendingActionId(string actorId)=>(string)Pending.FirstOrDefault(a=>(string)a["actor"]==actorId)?["id"];
        public bool UsesSavedPosition(string actorId)
        {
            var p=session.State.memory.positions.Find(n=>n.actorId==actorId);
            return p!=null&&!string.IsNullOrEmpty(p.escortActionId)&&(HasPending(actorId)||p.day==session.State.day&&p.period==session.State.period);
        }
        public void BeforeTravel(PlaceId destination)
        {
            if(world.Stage==null)return;
            foreach(var action in Pending.ToArray())
            {
                string id=(string)action["id"],actorId=(string)action["actor"];
                if(completed.Contains(id))continue;
                var actor=memory.Spawner.Find(actorId);if(actor==null)continue;
                var link=SevenCharacterCatalog.Map.routes.FirstOrDefault(r=>r.from==world.Stage.place&&r.to==destination&&Mathf.Abs(world.Player.position.x-r.x)<1.2f&&Mathf.Abs(actor.transform.position.x-r.x)<4.2f&&Mathf.Abs(actor.transform.position.x-world.Player.position.x)<3.2f&&actor.Movement.CanReachConversationPoint(new Vector2(r.x,actor.transform.position.y)));
                if((string)action["type"]!="escort_player"||link==null)
                {
                    Finish(new Running{id=id,actorId=actorId,actor=actor,target=(string)action["target"],previous=actor.Movement.SimulateDecisions},false,"companion_did_not_reach_exit");continue;
                }
                if(running.TryGetValue(id,out var old)){Release(old,"companion_scene_transition");running.Remove(id);}
                var p=session.State.memory.positions.Find(n=>n.actorId==actorId);
                p.escortActionId=id;p.travelTo=destination.ToString();p.arrivalId=link.arrivalId;
            }
        }
        // Called by the spawner before new actors are created. Both sides of the
        // actual portal must match; map jumps do not teleport companions.
        public void PrepareScene(WorldScene stage)
        {
            foreach(var p in session.State.memory.positions.Where(n=>!string.IsNullOrEmpty(n.travelTo)).ToArray())
            {
                var marker=stage.GetComponentsInChildren<IslandArrival>(true).FirstOrDefault(a=>a.id==p.arrivalId);
                bool reached=p.travelTo==stage.place.ToString()&&marker!=null&&Mathf.Abs(world.Player.position.x-marker.transform.position.x)<.4f;
                string arrival=p.arrivalId;p.travelTo=null;p.arrivalId=null;
                if(!HasPending(p.actorId))continue;
                if(!reached)
                {
                    completed.Add(p.escortActionId);
                    memory.Append(new MemoryEvent{kind="action_result",actionId=p.escortActionId,succeeded=false,locationId=p.locationId,reasonCode="companion_arrival_mismatch",text="玩家没有经共同出口到达，同行未完成。"});continue;
                }
                p.place=stage.place.ToString();p.x=Mathf.Clamp(marker.transform.position.x+(marker.transform.position.x>0?-.7f:.7f),-stage.width/2+1,stage.width/2-1);
                p.locationId=SevenCharacterCatalog.Location(stage.place,p.x);p.day=session.State.day;p.period=session.State.period;
                memory.Append(new MemoryEvent{kind="action_transition",actionId=p.escortActionId,location=p.place,locationId=p.locationId,positionX=p.x,arrivalId=arrival,text="NPC与玩家经相邻出口进入下一场景，尚未确认最终目的地。"});
            }
        }
        void Update()
        {
            if(memory?.Client==null||world?.Stage==null)return;
            var actions=Pending.ToDictionary(a=>(string)a["id"]);
            foreach(var work in running.Values.ToArray())if(!actions.ContainsKey(work.id)){Release(work,"action_no_longer_pending");running.Remove(work.id);}
            if(!memory.Client.Synchronized||world.IsTransitioning)return;
            foreach(var action in actions.Values)
            {
                string id=(string)action["id"],kind=(string)action["type"];
                if(completed.Contains(id)||!new[]{"move","escort_player"}.Contains(kind)||session.State.memory.positions.Any(p=>p.escortActionId==id&&!string.IsNullOrEmpty(p.travelTo)))continue;
                if(!running.TryGetValue(id,out var work))
                {
                    var actor=memory.Spawner.Find((string)action["actor"]);
                    if(actor?.Movement==null||!actor.Movement.IsReady||actor.Movement.InteractionHeld)continue;
                    work=new Running{id=id,actorId=actor.npcId,actor=actor,target=(string)action["target"],escort=kind=="escort_player",previous=actor.Movement.SimulateDecisions};
                    actor.Movement.SimulateDecisions=false;running[id]=work;
                    if(work.escort){memory.Spawner.CapturePositions();var saved=session.State.memory.positions.Find(p=>p.actorId==work.actorId);saved.escortActionId=id;}
                }
                Tick(work,action);
            }
        }
        void Tick(Running work,JToken action)
        {
            var actor=work.actor;
            if(actor==null){Finish(work,false,"actor_left_scene");return;}
            var movement=actor.Movement;
            movement.SimulateDecisions=false;
            if(movement.InteractionHeld||session.NonPlayerInputBlocked||Time.timeScale<=0)
            {
                if(work.started){movement.Interrupt("action_paused_for_conversation");work.started=false;}
                return;
            }
            int index=(int?)action["progress_index"]??0;
            var route=action["parameters"]?["route"]?.ToObject<string[]>();
            if(work.escort&&(route==null||route.Length<2)){Finish(work,false,"invalid_route");return;}
            string next=work.escort?(index+1<route.Length?route[index+1]:work.target):work.target;
            var anchor=SevenCharacterCatalog.Anchor(next);
            if(anchor==null||!Enum.TryParse(anchor.place,out PlaceId destination)){Finish(work,false,"unbound_destination");return;}
            var sceneRoute=SevenCharacterCatalog.SceneRoute(session.CurrentPlace,destination);
            bool finalScene=destination==session.CurrentPlace;
            if(!finalScene&&(!work.escort||sceneRoute==null||sceneRoute.Length==0)){Finish(work,false,"destination_unreachable");return;}
            float targetX=finalScene?anchor.x:sceneRoute[0].x;
            // Reaching a portal is not reaching the promised destination.
            if(finalScene&&Mathf.Abs(actor.transform.position.x-anchor.x)<.15f)
            {
                if(work.started)movement.Interrupt("waypoint_arrived");work.started=false;work.activeSeconds=work.retrySeconds=0;
                if(work.escort&&index+1<route.Length)
                {
                    memory.Append(new MemoryEvent{kind="action_progress",actionId=work.id,routeIndex=index+1,locationId=next,text="同行实际到达路线节点。"});
                    return;
                }
                Finish(work,true,"arrived");return;
            }
            Vector2 point=new Vector2(targetX,actor.transform.position.y);
            if(work.escort)
            {
                if(world.Player==null)return;
                float direction=Mathf.Sign(targetX-actor.transform.position.x);
                float playerX=world.Player.position.x;
                // Stay with the player. Waiting for them is not a timeout.
                if(Mathf.Abs(playerX-targetX)>1.5f)
                {
                    if((playerX-actor.transform.position.x)*direction<1.2f)
                    {
                        if(work.started)movement.Interrupt("waiting_for_companion");work.started=false;return;
                    }
                    // The controller paces this leg behind the moving player.
                }
            }
            if(work.started&&movement.IsMoving)
            {
                work.activeSeconds+=Time.unscaledDeltaTime;
                if(work.activeSeconds>90){Finish(work,false,"movement_timeout");return;}
                // Continue the physical route; a moving player extends the next leg.
                return;
            }
            if(work.started)
            {
                work.started=false;
                if(movement.LastResult?.Outcome==NpcMoveOutcome.Blocked){Finish(work,false,movement.LastResult.Reason);return;}
            }
            if(movement.IsMoving)movement.Interrupt("committed_action_owns_movement");
            if((point-(Vector2)actor.transform.position).sqrMagnitude<.04f)return;
            if(!movement.MoveToPoint(point,point.x>=actor.transform.position.x?1:-1,work.escort))
            {
                work.retrySeconds+=Time.unscaledDeltaTime;
                if(work.retrySeconds>3)Finish(work,false,movement.LastStartFailure??"cannot_start_movement");
                return;
            }
            work.requestedPoint=point;work.started=true;work.retrySeconds=0;
        }
        void Release(Running work,string reason)
        {
            if(work.actor?.Movement!=null){if(work.started)work.actor.Movement.Interrupt(reason);work.actor.Movement.SimulateDecisions=work.previous;}
        }
        void Finish(Running work,bool success,string reason)
        {
            Release(work,reason);running.Remove(work.id);completed.Add(work.id);
            if(success)work.actor?.Movement?.RebaseIdleArea();
            memory.Append(new MemoryEvent{kind="action_result",actionId=work.id,succeeded=success,locationId=success?work.target:memory.Location(work.actor),reasonCode=reason,text=success?"已与实际移动回执确认到达目的地。":"移动未完成（"+reason+"），需要重新选择或说明情况。"});
        }
        public void ResetTimeline(){foreach(var work in running.Values)Release(work,"timeline_reset");running.Clear();completed.Clear();checkpoint=null;cached=null;}
        void OnDestroy(){ResetTimeline();}
    }
}
