using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seabreeze
{
    [Serializable]
    public sealed class NpcSchedule
    {
        public PlaceId place;
        public TimePeriod from;
        public TimePeriod through;
        public string requiredFlag;
        public string forbiddenFlag;
    }
    [CreateAssetMenu(menuName = "Seabreeze/NPC")]
    public sealed class NpcData : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public Color accent = Color.white;
        [Header("Conversation presentation")]
        public Live2DModelProfile live2DProfile;
        public NpcSchedule[] schedule = Array.Empty<NpcSchedule>();
        public bool IsPresent(PlaceId place, TimePeriod period, IEnumerable<string> flags = null)
        {
            foreach (var entry in schedule)
                if (entry.place == place && period >= entry.from && period <= entry.through
                    && (string.IsNullOrEmpty(entry.requiredFlag) || Contains(flags, entry.requiredFlag))
                    && (string.IsNullOrEmpty(entry.forbiddenFlag) || !Contains(flags, entry.forbiddenFlag))) return true;
            return false;
        }
        static bool Contains(IEnumerable<string> flags, string flag)
        {
            if (flags != null) foreach (var value in flags) if (value == flag) return true;
            return false;
        }
    }
}
