using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seabreeze
{
    public enum TimePeriod { Morning, Afternoon, Evening, Night }
    public enum PlaceId { Street, Cafe, Lighthouse, ResidentialWest, ResidentialCenter, ResidentialEast, Forest, ForestCabin, Research, Summit, Harbor, Garden, Square, EastCrossing, Boardwalk, GeneralStore, Bookshop, Seafront, Beach, ForestPath1, ForestPath2, CabinInterior, ResearchInterior, VisitorCenter, FlowerShop, PhotoStudio, Restaurant, BeachEntrance, RestaurantInterior, BarInterior, CaptainHome, LiTingHome, ZiqingHome, ZHome, ZhouHome, ZBedroom, PlayerHome }

    [Serializable]
    public sealed class StoryEffect
    {
        public string flag;
        [TextArea] public string journal;
        public string item;
        public string removeItem;
        public string ending;
        public string relationship;
        public int relationshipDelta;
        public bool advanceTime;
    }

    [Serializable]
    public sealed class DialogueChoice
    {
        public string text;
        public string next;
        public string requiredFlag;
        public StoryEffect[] effects = Array.Empty<StoryEffect>();
    }

    [Serializable]
    public sealed class DialogueNode
    {
        public string id;
        public string speaker;
        [TextArea(3, 10)] public string text;
        public string next;
        public DialogueChoice[] choices = Array.Empty<DialogueChoice>();
        public StoryEffect[] effects = Array.Empty<StoryEffect>();
    }

    [CreateAssetMenu(menuName = "Seabreeze/Story Database")]
    public sealed class StoryDatabase : ScriptableObject
    {
        public DialogueNode[] dialogues = Array.Empty<DialogueNode>();
        public NpcData[] npcs = Array.Empty<NpcData>();
        public ItemData[] items = Array.Empty<ItemData>();
        public StoryEventData[] events = Array.Empty<StoryEventData>();
        Dictionary<string, DialogueNode> lookup;
        public DialogueNode FindDialogue(string id)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
                foreach (var n in dialogues) if (n != null && !string.IsNullOrEmpty(n.id)) lookup[n.id] = n;
            }
            return !string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out var result) ? result : null;
        }
        public NpcData FindNpc(string id)
        {
            foreach (var npc in npcs) if (npc && npc.id == id) return npc;
            return null;
        }
        public void InvalidateLookup() { lookup = null; }
    }
}
