using System;
using System.Collections.Generic;

namespace Seabreeze
{
    // Player-owned preferences and witnessed experiences only. Never a copy of NPC memory.
    [Serializable]
    public sealed class PhoneSaveState
    {
        public string worldId = "";
        public string pinnedTaskId = "";
        public List<string> readMessageIds = new List<string>();
        public List<string> favoriteMemoryIds = new List<string>();
        public List<PhoneMetPerson> metPeople = new List<PhoneMetPerson>();
        public List<PhoneSavedMemory> memories = new List<PhoneSavedMemory>();

        public void Sanitize()
        {
            if (worldId == null) worldId = "";
            if (pinnedTaskId == null) pinnedTaskId = "";
            if (readMessageIds == null) readMessageIds = new List<string>();
            if (favoriteMemoryIds == null) favoriteMemoryIds = new List<string>();
            if (metPeople == null) metPeople = new List<PhoneMetPerson>();
            if (memories == null) memories = new List<PhoneSavedMemory>();
            readMessageIds.RemoveAll(string.IsNullOrEmpty);
            favoriteMemoryIds.RemoveAll(string.IsNullOrEmpty);
            metPeople.RemoveAll(p => p == null || string.IsNullOrEmpty(p.actorId));
            memories.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id));
            foreach (var memory in memories)
                if (memory.actorIds == null) memory.actorIds = Array.Empty<string>();
        }
    }

    [Serializable]
    public sealed class PhoneMetPerson
    {
        public string actorId, source, place;
        public int day, period;
    }

    [Serializable]
    public sealed class PhoneSavedMemory
    {
        public string id, title, text, place, sourceId;
        public int day, period;
        public string[] actorIds = Array.Empty<string>();
    }
}
