using System;
using System.Collections.Generic;

namespace Seabreeze
{
    [Serializable]
    public sealed class RelationshipValue
    {
        public string id;
        public int value;
    }

    [Serializable]
    public sealed class SaveData
    {
        public int version = 1;
        public int day = 1;
        public int period;
        public string weather = "晴 · 海风轻柔";
        public List<string> flags = new List<string>();
        public List<string> journal = new List<string>();
        public List<string> items = new List<string>();
        public List<RelationshipValue> relationships = new List<RelationshipValue>();
        public string ending = "";
        public string place = "Street";
        public float playerX = -8f;
        public bool started;
        public string resumeDialogue = "";
        public MemorySaveState memory;

        public void Sanitize()
        {
            version = 1;
            day = Math.Max(1, day);
            period = Math.Max(0, Math.Min(3, period));
            if (flags == null) flags = new List<string>();
            if (journal == null) journal = new List<string>();
            if (items == null) items = new List<string>();
            if (relationships == null) relationships = new List<RelationshipValue>();
            if (!Enum.TryParse(place, out PlaceId _)) place = "Street";
            if (float.IsNaN(playerX) || float.IsInfinity(playerX)) playerX = -8f;
            if (ending == null) ending = "";
            if (weather == null) weather = "晴 · 海风轻柔";
        }
    }
}
