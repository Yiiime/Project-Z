using System;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    [Serializable] public sealed class SevenCharacter
    {
        public string id,displayName,occupation,place,color;
        public int appearance;
        public float x;
        public PlaceId Place => Enum.TryParse(place,out PlaceId p)?p:PlaceId.Street;
        public Color Accent => ColorUtility.TryParseHtmlString(color,out var c)?c:Color.white;
    }
    [Serializable] sealed class SevenRoster { public int version; public SevenCharacter[] actors; }
    public static class SevenCharacterCatalog
    {
        static SevenCharacter[] actors;
        public static SevenCharacter[] Actors
        {
            get
            {
                if(actors!=null)return actors;
                var text=Resources.Load<TextAsset>("Memory/characters");
                var roster=text!=null?JsonUtility.FromJson<SevenRoster>(text.text):null;
                if(roster==null||roster.version!=1||roster.actors==null||roster.actors.Length!=7
                    ||string.Join(",",roster.actors.Select(a=>a.id))!="c01,c02,c03,c04,c05,c06,c07")throw new InvalidOperationException("Seven-character public roster is invalid.");
                actors=roster.actors;return actors;
            }
        }
        public static SevenCharacter Find(string id) => Actors.FirstOrDefault(a=>a.id==id);
        public static string Name(string id) => id=="player"?"玩家":id=="z_story"?"Z":Find(id)?.displayName??id;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){actors=null;}
    }
}
