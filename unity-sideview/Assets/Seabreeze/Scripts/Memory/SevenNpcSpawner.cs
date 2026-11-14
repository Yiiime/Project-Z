using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class SevenNpcSpawner : MonoBehaviour
    {
        GameSession session; WorldDirector world; WorldScene stage;
        bool prepared, nightStarted, liyuanEntered;
        float nightAt;
        readonly Dictionary<string,float> leaveAt = new Dictionary<string,float>();
        readonly HashSet<string> leaving = new HashSet<string>();
        NpcPresence arriving;
        readonly List<NpcPresence> actors=new List<NpcPresence>();
        readonly List<NpcData> data=new List<NpcData>();
        public IReadOnlyList<NpcPresence> Actors => actors;
        public void Initialize(GameSession s,WorldDirector w)
        {
            session=s;world=w;
            // Also support a bootstrap that attached the scene before creating Memory.
            if(world!=null&&!world.IsTransitioning)PrepareStage(world.Stage);
        }
        public bool IsAmbientMoving(string id)=>leaving.Contains(id)||arriving!=null&&arriving.npcId==id;
        public NpcPresence Find(string id)=>actors.FirstOrDefault(a=>a!=null&&a.npcId==id&&a.IsPresent);
        void Update()
        {
            if(world==null||world.Stage==null||world.IsTransitioning||session?.State?.memory==null)return;
            PrepareStage(world.Stage);
            TickEvening();
        }
        public void Reload(){Clear();stage=null;}
        public void PrepareStage(WorldScene destination)
        {
            if(destination==null||world==null||session?.State?.memory==null||stage==destination)return;
            // Preparation belongs inside the opaque part of a scene transition.
            // Movement still observes WorldDirector.IsTransitioning and stays blocked.
            Clear();stage=destination;
            try{session.Memory.Actions.PrepareScene(stage);Spawn();prepared=true;}
            catch(System.Exception error)
            {
                Clear();
                Debug.LogException(new System.InvalidOperationException("NPC preparation failed in "+destination.place+".",error),this);
            }
        }
        public bool IsStageReady(WorldScene expected)
        {
            if(expected==null||stage!=expected||!prepared)return false;
            foreach(var actor in actors)
                if(actor==null||actor.Movement==null||!actor.Movement.IsReady)return false;
            return true; // An intentionally empty location needs no extra frame.
        }
        void Spawn()
        {
            foreach(var old in stage.GetComponentsInChildren<NpcPresence>(true))old.gameObject.SetActive(false);
            var staged=session.FirstDay?.StagedCast(stage.place);
            foreach(var definition in staged??SevenCharacterCatalog.Actors.Where(a=>session.Memory.Actions.UsesSavedPosition(a.id)
                ?session.State.memory.positions.Find(p=>p.actorId==a.id)?.place==stage.place.ToString()
                :a.schedule!=null&&a.schedule.Length>0?a.schedule.Any(s=>s.place==stage.place):a.Place==stage.place))
            {
                if(stage.place==PlaceId.Square&&session.State.day==1&&session.State.flags.Contains("firstday_square_left_"+definition.id))continue;
                SpawnActor(definition, staged != null);
            }
        }
        NpcPresence SpawnActor(SevenCharacter definition, bool staged, float? entryX=null)
        {
                var saved=session.State.memory.positions.Find(a=>a.actorId==definition.id&&a.place==stage.place.ToString());
                bool evening=staged&&(session.FirstDay.Phase==FirstDayPhase.WelcomeTask||session.FirstDay.Phase==FirstDayPhase.EveningReady);
                float x=entryX??(evening&&saved!=null?saved.x:staged?session.FirstDay.CastPosition(definition.id,stage.place):saved!=null?saved.x:definition.x);
                var root=new GameObject("岛民 "+definition.displayName);root.transform.SetParent(stage.transform,false);
                root.transform.position=new Vector3(Mathf.Clamp(x,-stage.width/2+1,stage.width/2-1),0,0);
                var interact=root.AddComponent<Interactable>();interact.id=definition.id;interact.label=definition.displayName;interact.npc=true;if(staged)interact.promptOffset=new Vector3(0,3.5f,0);
                var npc=root.AddComponent<NpcPresence>();npc.npcId=definition.id;npc.character=definition.appearance;npc.location=stage.place;
                npc.motionResource=definition.motion;
                var runtime=ScriptableObject.CreateInstance<NpcData>();runtime.hideFlags=HideFlags.DontSave;
                runtime.id=definition.id;runtime.displayName=definition.displayName;runtime.description=definition.occupation;runtime.accent=definition.Accent;
                runtime.schedule=!staged&&!session.Memory.Actions.UsesSavedPosition(definition.id)&&definition.schedule!=null&&definition.schedule.Length>0?definition.schedule:new[]{new NpcSchedule { place=stage.place,from=TimePeriod.Morning,through=TimePeriod.Night }};
                npc.RuntimeData=runtime;data.Add(runtime);actors.Add(npc);world.Interactables.Add(interact);
                return npc;
        }
        void TickEvening()
        {
            if(stage.place!=PlaceId.Square||session.State.day!=1||session.FirstDay?.Phase!=FirstDayPhase.WelcomeTask)return;
            if(!nightStarted)
            {
                nightStarted=true;nightAt=Time.time;
                // Independent local social choices, staggered in real time. Keep enough residents for the welcome task.
                var candidates=actors.Where(a=>a!=null&&a.npcId!="z"&&a.npcId!="liyuan").OrderBy(a=>Random.value).Take(3).ToArray();
                for(int i=0;i<candidates.Length;i++)leaveAt[candidates[i].npcId]=Time.time+6+i*8;
            }
            if(session.InputBlocked||Time.timeScale<=0)return;
            var exit=SevenCharacterCatalog.Map.routes.FirstOrDefault(r=>r.from==PlaceId.Square);
            if(exit==null)return;
            if(!liyuanEntered&&Time.time>nightAt+9)
            {
                liyuanEntered=true;
                if(Find("liyuan")==null)arriving=SpawnActor(SevenCharacterCatalog.Find("liyuan"),true,exit.x);
            }
            if(arriving!=null&&arriving.Movement!=null&&arriving.Movement.IsReady)
            {
                if(!arriving.Movement.InteractionHeld&&arriving.ConversationPartner==null)
                {
                    if(arriving.Movement.SimulateDecisions)
                    { arriving.Movement.SimulateDecisions=false; arriving.Movement.AmbientPace=.6f; arriving.Movement.MoveToPoint(new Vector2(exit.x>0?5.5f:-5.5f,arriving.transform.position.y),exit.x>0?-1:1,true,false); }
                    else if(!arriving.Movement.IsMoving){arriving.Movement.SimulateDecisions=true;arriving.Movement.AmbientPace=1;arriving.Movement.RebaseIdleArea();arriving=null;}
                }
            }
            foreach(var entry in leaveAt.ToArray())
            {
                var npc=Find(entry.Key);if(npc==null){leaveAt.Remove(entry.Key);continue;}
                var movement=npc.Movement;
                // Active conversations and accepted actions always win over ambient departures.
                if(movement==null||!movement.IsReady||movement.InteractionHeld||npc.ConversationPartner!=null||session.Memory.Actions.HasPending(npc.npcId))continue;
                if(Time.time<entry.Value)continue;
                if(NeededForTask(npc.npcId)){if(leaving.Remove(npc.npcId)){movement.Interrupt("welcome_task_priority");movement.SimulateDecisions=true;}continue;}
                if(!leaving.Contains(npc.npcId))
                {
                    movement.SimulateDecisions=false;
                    if(movement.MoveToPoint(new Vector2(exit.x,npc.transform.position.y),exit.x>npc.transform.position.x?1:-1,true,false))leaving.Add(npc.npcId);
                    else {movement.SimulateDecisions=true;leaveAt[npc.npcId]=Time.time+5;}
                    continue;
                }
                if(movement.IsMoving)continue;
                if(Mathf.Abs(npc.transform.position.x-exit.x)>.3f){leaving.Remove(npc.npcId);movement.SimulateDecisions=true;leaveAt[npc.npcId]=Time.time+3;continue;}
                session.Memory.PublishStoryEvent("square_departure_"+npc.npcId,SevenCharacterCatalog.Name(npc.npcId)+"看完沙滩烟花回到广场后，走到出口离开了广场。",session.Memory.Listeners(npc.transform.position,npc.npcId,npc.npcId),true);
                session.State.flags.Add("firstday_square_left_"+npc.npcId);
                session.Memory.Runtimes?.SceneUnloaded(npc);world.Interactables.Remove(npc.GetComponent<Interactable>());
                actors.Remove(npc);npc.gameObject.SetActive(false);Destroy(npc.gameObject);leaveAt.Remove(entry.Key);leaving.Remove(entry.Key);session.Save();
            }
        }
        bool NeededForTask(string actor)
        {
            var checkpoint=session.State.memory.checkpoint;
            if(string.IsNullOrEmpty(checkpoint))return false;
            try {
                var tasks=JObject.Parse(checkpoint)["tasks"];
                foreach(var definition in tasks?["definitions"]??new JArray()) {
                    string id=(string)definition["id"];
                    var row=(tasks?["state"]??new JArray()).FirstOrDefault(r=>(string)r["task"]==id);
                    string status=(string)row?["status"];
                    if(status!="active"&&status!="offered"&&!(status=="available"&&id.StartsWith("welcome_")))continue;
                    if((string)definition["owner"]==actor||(definition["steps"]??new JArray()).Any(step=>(string)step["target"]==actor))return true;
                }
            } catch(System.Exception) { return true; }
            return false;
        }
        public void CapturePositions()
        {
            if(session?.State?.memory==null||stage==null)return;
            foreach(var a in actors)
            {
                if(a==null)continue;var p=session.State.memory.positions.Find(n=>n.actorId==a.npcId);
                if(p==null){p=new NpcSavedPosition { actorId=a.npcId };session.State.memory.positions.Add(p);}
                p.locationId=SevenCharacterCatalog.Location(stage.place,a.transform.position.x,a.npcId);p.place=stage.place.ToString();p.x=a.transform.position.x;p.day=session.State.day;p.period=session.State.period;
            }
        }
        void Clear()
        {
            prepared=false;nightStarted=liyuanEntered=false;arriving=null;leaveAt.Clear();leaving.Clear();
            foreach(var npc in actors)if(npc!=null){session?.Memory?.Runtimes?.SceneUnloaded(npc);if(world!=null)world.Interactables.Remove(npc.GetComponent<Interactable>());npc.gameObject.SetActive(false);Destroy(npc.gameObject);}
            actors.Clear();foreach(var d in data)if(d!=null)Destroy(d);data.Clear();
        }
        void OnDestroy(){Clear();}
    }
}
