using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace Seabreeze
{
    [Serializable] public sealed class SevenCharacter
    {
        public string id,displayName,occupation,place,color,portrait,previewPortrait,locationId,motion;
        public int appearance;
        public float dialogueCharactersPerSecond;
        public float x;
        public NpcSchedule[] schedule = Array.Empty<NpcSchedule>();
        public PlaceId Place => Enum.TryParse(place,out PlaceId p)?p:PlaceId.Street;
        public Color Accent => ColorUtility.TryParseHtmlString(color,out var c)?c:Color.white;
    }
    [Serializable] sealed class SevenRoster { public int version; public SevenCharacter[] actors; }
    public static class SevenCharacterCatalog
    {
        [Serializable] public sealed class NavAnchor {public string id,place;public float x;}
        [Serializable] sealed class NavRoster {public NavAnchor[] anchors;public string[][] edges;}
        static NavRoster navigation;
        static NavRoster Navigation => navigation??(navigation=Newtonsoft.Json.JsonConvert.DeserializeObject<NavRoster>(Resources.Load<TextAsset>("Memory/locations").text));
        public static NavAnchor Anchor(string id)=>Navigation.anchors.FirstOrDefault(a=>a.id==id);
        public static string Location(PlaceId place,float x,string actor=null)=>Navigation.anchors.Where(a=>a.place==place.ToString()).OrderBy(a=>Mathf.Abs(a.x-x)).FirstOrDefault()?.id??"main_street";
        public static string RuntimeLocation(PlaceId place,float x)=>Navigation.anchors.Where(a=>a.place==place.ToString()).OrderBy(a=>Mathf.Abs(a.x-x)).FirstOrDefault()?.id??"scene:"+place;
        public static string[] Walkable(PlaceId place)=>Navigation.anchors.Where(a=>a.place==place.ToString()).Select(a=>a.id).ToArray();
        public static string[] Reachable(PlaceId place,string id)=>Navigation.edges.Where(e=>e[0]==id||e[1]==id).Select(e=>e[0]==id?e[1]:e[0]).Where(n=>Anchor(n)?.place==place.ToString()).ToArray();
        // Use the authored scene/door graph, never the narrative knowledge graph.
        public static IslandMapCatalog Map => Resources.Load<IslandMapCatalog>("Content/IslandMap");
        public static IslandMapLink[] SceneRoute(PlaceId from,PlaceId to)
        {
            if(from==to)return Array.Empty<IslandMapLink>();
            var queue=new Queue<(PlaceId place,IslandMapLink[] path)>();var seen=new HashSet<PlaceId>{from};queue.Enqueue((from,Array.Empty<IslandMapLink>()));
            while(queue.Count>0)
            {
                var item=queue.Dequeue();
                foreach(var edge in Map.routes.Where(e=>e.from==item.place))
                {if(!seen.Add(edge.to))continue;var path=item.path.Concat(new[]{edge}).ToArray();if(edge.to==to)return path;queue.Enqueue((edge.to,path));}
            }
            return null;
        }
        public static string[][] CompanionRoutes(PlaceId place,string from)=>Navigation.anchors
            .Where(a=>a.id!=from&&Enum.TryParse(a.place,out PlaceId target)&&SceneRoute(place,target)!=null)
            .Select(a=>new[]{from,a.id}).ToArray();
        static SevenCharacter[] actors;
        public static SevenCharacter[] Actors
        {
            get
            {
                if(actors!=null)return actors;
                var text=Resources.Load<TextAsset>("Memory/characters");
                var roster=text!=null?JsonUtility.FromJson<SevenRoster>(text.text):null;
                if(roster==null||roster.version!=3||roster.actors==null||roster.actors.Length!=8
                    ||string.Join(",",roster.actors.Select(a=>a.id))!="z,liyuan,liting,axing,zhengyi,xiaoxiaoqing,linxu,zhou_bobo")throw new InvalidOperationException("Island character roster is invalid.");
                actors=roster.actors;return actors;
            }
        }
        public static SevenCharacter Find(string id) => Actors.FirstOrDefault(a=>a.id==id);
        public static string Name(string id) => id=="player"?PlayerIdentity.DisplayName:id=="z"?"Z":Find(id)?.displayName??id;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){actors=null;navigation=null;}
    }
}
