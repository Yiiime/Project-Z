using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class MemoryActionExecutor : MonoBehaviour
    {
        sealed class Running {public string id,target;public NpcPresence actor;public bool previous;public float expires;}
        readonly Dictionary<string,Running> running=new Dictionary<string,Running>();readonly HashSet<string> completed=new HashSet<string>();
        MemoryGameController memory;GameSession session;WorldDirector world;
        public void Initialize(MemoryGameController m,GameSession s,WorldDirector w){memory=m;session=s;world=w;}
        void Update()
        {
            if(memory?.Client==null||!memory.Client.Synchronized||world?.Stage==null||world.IsTransitioning)return;
            foreach(var item in running.Values.ToArray())if(item.actor==null||!item.actor.Movement.IsMoving||Time.unscaledTime>item.expires){bool arrived=item.actor!=null&&Mathf.Abs(item.actor.transform.position.x-SevenCharacterCatalog.Anchor(item.target).x)<.3f;Finish(item,arrived);}
            if(string.IsNullOrEmpty(session.State.memory.checkpoint))return;
            JObject cp;try{cp=JObject.Parse(session.State.memory.checkpoint);}catch{return;}
            foreach(var action in cp["records"]?["action"]??new JArray())
            {
                string id=(string)action["id"],actorId=(string)action["actor"],target=(string)action["target"];
                if((string)action["status"]!="pending"||(string)action["type"]!="move"||completed.Contains(id)||running.ContainsKey(id))continue;
                var actor=memory.Spawner.Find(actorId);var anchor=SevenCharacterCatalog.Anchor(target);
                if(actor==null||actor.Movement.InteractionHeld||actor.Movement.IsMoving)continue;
                if(anchor==null||anchor.place!=session.CurrentPlace.ToString())continue;
                var work=new Running{id=id,target=target,actor=actor,previous=actor.Movement.SimulateDecisions,expires=Time.unscaledTime+30};actor.Movement.SimulateDecisions=false;
                if(!actor.Movement.MoveForConversation(new Vector2(anchor.x,actor.transform.position.y),anchor.x>=actor.transform.position.x?1:-1)){Finish(work,false);continue;}running[id]=work;
            }
        }
        void Finish(Running action,bool success){if(action.actor!=null)action.actor.Movement.SimulateDecisions=action.previous;running.Remove(action.id);completed.Add(action.id);memory.Append(new MemoryEvent{kind="action_result",actionId=action.id,succeeded=success,locationId=action.target,text=success?"已实际到达地图锚点。":"实际移动受阻，未完成。"});}
        public void ResetTimeline(){foreach(var work in running.Values)if(work.actor!=null)work.actor.Movement.SimulateDecisions=work.previous;running.Clear();completed.Clear();}
        void OnDestroy(){ResetTimeline();}
    }
}
