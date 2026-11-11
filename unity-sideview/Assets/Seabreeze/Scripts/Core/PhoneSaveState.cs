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
        // A save-owned album survives loading and never mixes photographs from a new game.
        public string albumId = "";
        public List<PhoneSavedPhoto> photos = new List<PhoneSavedPhoto>();

        public void Sanitize()
        {
            if (worldId == null) worldId = "";
            if (pinnedTaskId == null) pinnedTaskId = "";
            if (readMessageIds == null) readMessageIds = new List<string>();
            if (favoriteMemoryIds == null) favoriteMemoryIds = new List<string>();
            if (metPeople == null) metPeople = new List<PhoneMetPerson>();
            if (memories == null) memories = new List<PhoneSavedMemory>();
            if (albumId == null) albumId = "";
            if (photos == null) photos = new List<PhoneSavedPhoto>();
            readMessageIds.RemoveAll(string.IsNullOrEmpty);
            favoriteMemoryIds.RemoveAll(string.IsNullOrEmpty);
            metPeople.RemoveAll(p => p == null || string.IsNullOrEmpty(p.actorId));
            memories.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id));
            photos.RemoveAll(p => p == null || string.IsNullOrEmpty(p.id));
            foreach (var memory in memories)
                if (memory.actorIds == null) memory.actorIds = Array.Empty<string>();
        }
    }

    [Serializable]
    public sealed class PhoneSavedPhoto
    {
        // Relative basenames only. Absolute OS paths are never serialized into a save.
        public string id, fileName, thumbnailFileName, title, place, capturedUtc;
        public int day, period, width, height;
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
