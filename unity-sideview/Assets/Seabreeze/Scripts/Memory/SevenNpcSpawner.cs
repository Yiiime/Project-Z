using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class SevenNpcSpawner : MonoBehaviour
    {
        GameSession session; WorldDirector world; WorldScene stage;
        bool prepared;
        readonly List<NpcPresence> actors=new List<NpcPresence>();
        readonly List<NpcData> data=new List<NpcData>();
        public IReadOnlyList<NpcPresence> Actors => actors;
        public void Initialize(GameSession s,WorldDirector w)
        {
            session=s;world=w;
            // Also support a bootstrap that attached the scene before creating Memory.
            if(world!=null&&!world.IsTransitioning)PrepareStage(world.Stage);
        }
        public NpcPresence Find(string id)=>actors.FirstOrDefault(a=>a!=null&&a.npcId==id&&a.IsPresent);
        void Update()
        {
            if(world==null||world.Stage==null||world.IsTransitioning||session?.State?.memory==null)return;
            PrepareStage(world.Stage);
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
                var saved=session.State.memory.positions.Find(a=>a.actorId==definition.id&&a.place==stage.place.ToString());
                float x=staged!=null?session.FirstDay.CastPosition(definition.id,stage.place):saved!=null?saved.x:definition.x;
                var root=new GameObject("岛民 "+definition.displayName);root.transform.SetParent(stage.transform,false);
                root.transform.position=new Vector3(Mathf.Clamp(x,-stage.width/2+1,stage.width/2-1),0,0);
                var interact=root.AddComponent<Interactable>();interact.id=definition.id;interact.label=definition.displayName;interact.npc=true;if(staged!=null)interact.promptOffset=new Vector3(0,3.5f,0);
                var npc=root.AddComponent<NpcPresence>();npc.npcId=definition.id;npc.character=definition.appearance;npc.location=stage.place;
                npc.motionResource=definition.motion;
                var runtime=ScriptableObject.CreateInstance<NpcData>();runtime.hideFlags=HideFlags.DontSave;
                runtime.id=definition.id;runtime.displayName=definition.displayName;runtime.description=definition.occupation;runtime.accent=definition.Accent;
                runtime.schedule=staged==null&&!session.Memory.Actions.UsesSavedPosition(definition.id)&&definition.schedule!=null&&definition.schedule.Length>0?definition.schedule:new[]{new NpcSchedule { place=stage.place,from=TimePeriod.Morning,through=TimePeriod.Night }};
                npc.RuntimeData=runtime;data.Add(runtime);actors.Add(npc);world.Interactables.Add(interact);
            }
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
            prepared=false;
            foreach(var npc in actors)if(npc!=null){session?.Memory?.Runtimes?.SceneUnloaded(npc);if(world!=null)world.Interactables.Remove(npc.GetComponent<Interactable>());npc.gameObject.SetActive(false);Destroy(npc.gameObject);}
            actors.Clear();foreach(var d in data)if(d!=null)Destroy(d);data.Clear();
        }
        void OnDestroy(){Clear();}
    }
}
