using System;
using System.Collections.Generic;
namespace Seabreeze
{
    [Serializable] public sealed class MemoryEvent
    {
        public string id, kind, conversationId, speakerId, recipientId, text, proposalId, location, title, gist, fulfilledId;
        public int seq, day, period, personaRevision;
        public string[] observers = Array.Empty<string>();
    }
    [Serializable] public sealed class MemoryRequest
    {
        public string worldId, runId, requestId, checkpoint, actorId, recipientId, conversationId, topic, scene;
        public int day, period, turn;
        public string[] candidates = Array.Empty<string>(), observers = Array.Empty<string>();
        public MemoryEvent eventData;
    }
    [Serializable] public sealed class MemoryReply
    {
        public bool ok, configured, verified, endConversation;
        public string error, warning, app, mode, model, runId, requestId, actorId, targetId, reason, topic, proposalId, text, checkpoint;
        public int seq, configurationRevision, responseRevision;
        public string[] recalledMemoryIds = Array.Empty<string>();
    }
    [Serializable] public sealed class NpcSavedPosition
    {
        public string actorId, place;
        public float x;
        public int day, period;
    }
    [Serializable] public sealed class MemorySaveState
    {
        public string worldId = "", checkpoint = "";
        public int nextSeq, acknowledgedSeq;
        public bool day4Published, day4Shown;
        public string[] publicDay4KnownBy = Array.Empty<string>();
        public List<MemoryEvent> outbox = new List<MemoryEvent>();
        public List<NpcSavedPosition> positions = new List<NpcSavedPosition>();
        public void Sanitize()
        {
            if (string.IsNullOrEmpty(worldId)) worldId = Guid.NewGuid().ToString("N");
            if (checkpoint == null) checkpoint = "";
            if (outbox == null) outbox = new List<MemoryEvent>();
            if (positions == null) positions = new List<NpcSavedPosition>();
            nextSeq = Math.Max(nextSeq, acknowledgedSeq);
            foreach (var e in outbox) if(e != null) nextSeq = Math.Max(nextSeq,e.seq);
        }
    }
}
