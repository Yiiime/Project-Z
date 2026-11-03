using System;
using UnityEngine;

namespace Seabreeze
{
    // This is the movement projection of the team's dialogue/action/info protocol.
    // Dialogue and info changes are deliberately left for their owning systems.
    [Serializable]
    public sealed class NpcMoveCommand
    {
        public string type;
        public string target;
        public string description;
        public string action_id;
    }

    [Serializable]
    public sealed class NpcMoveOutput
    {
        public string dialogue;
        public NpcMoveCommand action;
        public ActorInfo info;

        [Serializable]
        public sealed class ActorInfo
        {
            public string actor_id;
        }
    }

    [Serializable]
    public sealed class NpcMoveRequest
    {
        public string request_id;
        public string actor_id;
        public string location_id;
        public Vector2 position;
        public NpcDestination[] destinations;
        public NpcMoveCommand[] available_actions;
    }

    public enum NpcMoveOutcome { Arrived, Waiting, Interrupted, Blocked }

    public sealed class NpcMoveResult
    {
        public string RequestId { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public NpcMoveOutcome Outcome { get; }
        public string Reason { get; }
        public Vector2 Position { get; }

        public NpcMoveResult(string requestId, string actorId, string targetId,
            NpcMoveOutcome outcome, string reason, Vector2 position)
        {
            RequestId = requestId;
            ActorId = actorId;
            TargetId = targetId;
            Outcome = outcome;
            Reason = reason;
            Position = position;
        }
    }
}
