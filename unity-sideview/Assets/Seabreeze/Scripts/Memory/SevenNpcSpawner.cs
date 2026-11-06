using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    public sealed class SevenNpcSpawner : MonoBehaviour
    {
        GameSession session; WorldDirector world; WorldScene stage;
        readonly List<NpcPresence> actors=new List<NpcPresence>();
        readonly List<NpcData> data=new List<NpcData>();
        public IReadOnlyList<NpcPresence> Actors => actors;
        public void Initialize(GameSession s,WorldDirector w){session=s;world=w;}
        public NpcPresence Find(string id)=>actors.FirstOrDefault(a=>a!=null&&a.npcId==id&&a.IsPresent);
        void Update()
        {
            if(world==null||world.Stage==null||world.IsTransitioning||session?.State?.memory==null)return;
            if(stage!=world.Stage){Clear();stage=world.Stage;Spawn();}
        }
        public void Reload(){Clear();stage=null;}
        void Spawn()
        {
            foreach(var old in stage.GetComponentsInChildren<NpcPresence>(true))old.gameObject.SetActive(false);
            foreach(var definition in SevenCharacterCatalog.Actors.Where(a=>a.Place==stage.place))
            {
                var saved=session.State.memory.positions.Find(a=>a.actorId==definition.id&&a.place==definition.place&&a.day==session.State.day&&a.period==session.State.period);
                float x=saved!=null?saved.x:definition.x;
                var root=new GameObject("七人NPC "+definition.displayName+"（形象占位）");root.transform.SetParent(stage.transform,false);
                root.transform.position=new Vector3(Mathf.Clamp(x,-stage.width/2+1,stage.width/2-1),0,0);
                var interact=root.AddComponent<Interactable>();interact.id=definition.id;interact.label=definition.displayName;interact.npc=true;
                var npc=root.AddComponent<NpcPresence>();npc.npcId=definition.id;npc.character=definition.appearance;npc.location=stage.place;
                var runtime=ScriptableObject.CreateInstance<NpcData>();runtime.hideFlags=HideFlags.DontSave;
                runtime.id=definition.id;runtime.displayName=definition.displayName;runtime.description=definition.occupation;runtime.accent=definition.Accent;
                runtime.schedule=new[]{new NpcSchedule { place=stage.place,from=TimePeriod.Morning,through=TimePeriod.Night }};
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
                p.place=stage.place.ToString();p.x=a.transform.position.x;p.day=session.State.day;p.period=session.State.period;
            }
        }
        void Clear()
        {
            foreach(var npc in actors)if(npc!=null){if(world!=null)world.Interactables.Remove(npc.GetComponent<Interactable>());Destroy(npc.gameObject);}
            actors.Clear();foreach(var d in data)if(d!=null)Destroy(d);data.Clear();
        }
        void OnDestroy(){Clear();}
    }
}
