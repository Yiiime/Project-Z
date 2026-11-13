using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace Seabreeze
{
    [Serializable] public sealed class ActorAction { public string type,target,description,action_id; public JObject parameters; }
    [Serializable] public sealed class ActorTurn { public string dialogue,expression; public ActorAction action; public JObject info; }
    [Serializable] public sealed class MemoryEvent
    {
        public string id,kind,conversationId,speakerId,recipientId,text,proposalId,location,locationId,title,gist,fulfilledId,channel,actionId,taskId,actorId,responderId,stepId,performerId,receiptId,messageEventId;
        public string reasonCode,arrivalId; public float positionX; public int routeIndex;
        public int seq,day,period; public bool verified,succeeded,accepted;
        public string[] observers=Array.Empty<string>(),discloseTo=Array.Empty<string>(),participants=Array.Empty<string>(),voluntaryConsents=Array.Empty<string>();
    }
    [Serializable] public sealed class MemoryRequest
    {
        public string worldId,runId,requestId,checkpoint,actorId,recipientId,conversationId,topic,scene,locationId,channel,proposalId,taskId,stepId,receiptId,performerId,responderId,text;
        public int day,period,turn;public bool accepted,succeeded;
        public string[] candidates=Array.Empty<string>(),observers=Array.Empty<string>(),reachableLocations=Array.Empty<string>();
        public string[][] companionRoutes=Array.Empty<string[]>();
        public string weather,syncMode="reference-v1",sourceEventId,roomId,roomToken;public bool portable;
        public string[] walkableLocations=Array.Empty<string>();public JObject details;public MemoryEvent eventData;
        public NpcRuntimeEvent runtimeEvent;public NpcRuntimeSnapshot runtimeSnapshot;public int runtimeSeq;
    }
    [Serializable] public sealed class MemorySpeechAudio
    {
        public string format,base64; public int sampleRate,channels;
    }
    [Serializable] public sealed class MemorySpeechWord
    {
        public string word;public float startTime,endTime;
    }
    [Serializable] public sealed class MemorySpeechPacket
    {
        public string type,runId,proposalId,error,speechError,speechWarning;
        public MemorySpeechAudio audio;public MemorySpeechWord[] words;
        public int totalSamples;public float? firstAudioMs,firstSubtitleMs,completeMs;
    }
    [Serializable] public sealed class MemoryReply
    {
        public bool ok,configured,verified,endConversation;
        public string error,warning,app,mode,model,runId,requestId,actorId,targetId,reason,topic,proposalId,text,expression,checkpoint,delivery,speechError,speechWarning;
        public int seq,configurationRevision,responseRevision;
        public MemorySpeechAudio audio;
        public ActorTurn result;public JObject state,binding,storyPlan;
        public string[] recalledMemoryIds=Array.Empty<string>();
        public string runtimeActorId;public int runtimeSeq;
    }
    [Serializable] public sealed class NpcRuntimeSnapshot
    {
        public string sceneId,locationId,locationName,movementState,actionId,targetLocationId,conversationId,reason;
        public float x,y;
    }
    [Serializable] public sealed class NpcRuntimeEvent
    {
        public string id,actorId;public int seq,day,period;public NpcRuntimeSnapshot snapshot;
    }
    [Serializable] public sealed class NpcRuntimeCursor
    {
        public string actorId;public int nextSeq,acknowledgedSeq;
    }
    [Serializable] public sealed class NpcSavedPosition { public string actorId,place,locationId,escortActionId,travelTo,arrivalId;public float x;public int day,period; }
    [Serializable] public sealed class MemorySaveState
    {
        public string worldId="",checkpoint="";public int nextSeq,acknowledgedSeq;
        public List<MemoryEvent> outbox=new List<MemoryEvent>();public List<NpcSavedPosition> positions=new List<NpcSavedPosition>();
        public List<NpcRuntimeEvent> runtimeOutbox=new List<NpcRuntimeEvent>();
        public List<NpcRuntimeCursor> runtimeCursors=new List<NpcRuntimeCursor>();
        public NpcRuntimeCursor RuntimeCursor(string actor)
        {var cursor=runtimeCursors.Find(c=>c.actorId==actor);if(cursor==null){cursor=new NpcRuntimeCursor{actorId=actor};runtimeCursors.Add(cursor);}return cursor;}
        public void Sanitize(){if(string.IsNullOrEmpty(worldId))worldId=Guid.NewGuid().ToString("N");if(checkpoint==null)checkpoint="";if(outbox==null)outbox=new List<MemoryEvent>();if(positions==null)positions=new List<NpcSavedPosition>();if(runtimeOutbox==null)runtimeOutbox=new List<NpcRuntimeEvent>();if(runtimeCursors==null)runtimeCursors=new List<NpcRuntimeCursor>();nextSeq=Math.Max(nextSeq,acknowledgedSeq);foreach(var e in outbox)if(e!=null)nextSeq=Math.Max(nextSeq,e.seq);foreach(var c in runtimeCursors)c.nextSeq=Math.Max(c.nextSeq,c.acknowledgedSeq);foreach(var e in runtimeOutbox)if(e!=null){var c=RuntimeCursor(e.actorId);c.nextSeq=Math.Max(c.nextSeq,e.seq);}}
    }
}
