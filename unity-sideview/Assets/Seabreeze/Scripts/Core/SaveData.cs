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
    public sealed class DialogueBookmark
    {
        public string source;
        public string node;
        public int day;
        public int period;
    }
    [Serializable]
    public sealed class SaveData
    {
        public int version = 2;
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
        public string resumeInteractionId = "";
        public List<DialogueBookmark> dialogueBookmarks = new List<DialogueBookmark>();

        public void Sanitize()
        {
            version = 2;
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
            if (resumeDialogue == null) resumeDialogue = "";
            if (resumeInteractionId == null) resumeInteractionId = "";
            if (dialogueBookmarks == null) dialogueBookmarks = new List<DialogueBookmark>();
            // The visible name changed; stable flags, node ids and relationship ids remain intact.
            for (int i = 0; i < journal.Count; i++) journal[i] = journal[i]?.Replace("澪", "Z");
        }
    }
}
