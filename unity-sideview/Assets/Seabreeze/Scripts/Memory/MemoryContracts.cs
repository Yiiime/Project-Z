using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace Seabreeze
{
    [Serializable] public sealed class ActorAction { public string type,target,description,action_id; public JObject parameters; }
    [Serializable] public sealed class ActorTurn { public string dialogue; public ActorAction action; public JObject info; }
    [Serializable] public sealed class MemoryEvent
    {
        public string id,kind,conversationId,speakerId,recipientId,text,proposalId,location,locationId,title,gist,fulfilledId,channel,actionId,taskId,actorId,responderId,stepId,performerId,receiptId,messageEventId;
        public int seq,day,period; public bool verified,succeeded,accepted;
        public string[] observers=Array.Empty<string>(),discloseTo=Array.Empty<string>(),participants=Array.Empty<string>(),voluntaryConsents=Array.Empty<string>();
    }
    [Serializable] public sealed class MemoryRequest
    {
        public string worldId,runId,requestId,checkpoint,actorId,recipientId,conversationId,topic,scene,locationId,channel,proposalId,taskId,stepId,receiptId,performerId,responderId,text;
        public int day,period,turn;public bool accepted,succeeded;
        public string[] candidates=Array.Empty<string>(),observers=Array.Empty<string>(),reachableLocations=Array.Empty<string>();
        public JObject details;public MemoryEvent eventData;
    }
    [Serializable] public sealed class MemoryReply
    {
        public bool ok,configured,verified,endConversation;
        public string error,warning,app,mode,model,runId,requestId,actorId,targetId,reason,topic,proposalId,text,checkpoint;
        public int seq,configurationRevision,responseRevision;
        public ActorTurn result;public JObject state;
        public string[] recalledMemoryIds=Array.Empty<string>();
    }
    [Serializable] public sealed class NpcSavedPosition { public string actorId,place,locationId;public float x;public int day,period; }
    [Serializable] public sealed class MemorySaveState
    {
        public string worldId="",checkpoint="";public int nextSeq,acknowledgedSeq;
        public List<MemoryEvent> outbox=new List<MemoryEvent>();public List<NpcSavedPosition> positions=new List<NpcSavedPosition>();
        public void Sanitize(){if(string.IsNullOrEmpty(worldId))worldId=Guid.NewGuid().ToString("N");if(checkpoint==null)checkpoint="";if(outbox==null)outbox=new List<MemoryEvent>();if(positions==null)positions=new List<NpcSavedPosition>();nextSeq=Math.Max(nextSeq,acknowledgedSeq);foreach(var e in outbox)if(e!=null)nextSeq=Math.Max(nextSeq,e.seq);}
    }
}
