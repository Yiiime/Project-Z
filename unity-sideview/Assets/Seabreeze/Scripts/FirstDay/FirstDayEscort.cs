using UnityEngine;
namespace Seabreeze
{
    public sealed class FirstDayEscort : MonoBehaviour
    {
        const float WaitDistance = 6f;
        const float ResumeDistance = 3.5f;
        GameSession session; WorldDirector world; NpcPresence leader;
        NpcMovementController movement;
        bool originalSimulateDecisions, waitingForPlayer;
        string routeRequest;
        float nextRouteAttempt;
        public void Initialize(GameSession owner, WorldDirector director) { session = owner; world = director; }
        public void ResetRoute()
        {
            if (movement != null)
            {
                // Release only this guide's route, not a newer conversation action.
                if (!string.IsNullOrEmpty(routeRequest) && movement.ActiveRequestId == routeRequest)
                    movement.Interrupt("guide_released");
                movement.ClearGuidePace();
                movement.SimulateDecisions = originalSimulateDecisions;
            }
            leader = null; movement = null; routeRequest = null;
            waitingForPlayer = false; nextRouteAttempt = 0;
        }
        void ChangeLeader(NpcPresence next)
        {
            if (leader == next && movement != null) return;
            ResetRoute();
            if (next == null || next.Movement == null) return;
            leader = next; movement = next.Movement;
            originalSimulateDecisions = movement.SimulateDecisions;
            movement.SimulateDecisions = false;
        }
        void OnDisable() => ResetRoute();
        public float ArrivalX(PlaceId place)
        {
            var s = session.State.firstDay;
            return s.escortPlace == place.ToString() ? s.escortX : -world.Stage.width / 2 + 2;
        }
        void Update()
        {
            if (session == null || world == null) return;
            if (session.FirstDay?.Phase != FirstDayPhase.WalkToSquare) { ResetRoute(); return; }
            // Pause and travel suspend movement without losing the guide's state.
            if (world.IsTransitioning || world.Stage == null || world.Player == null || session.NonPlayerInputBlocked) return;
            var nextLeader = session.Memory?.Spawner.Find("z");
            // A conversation temporarily owns the actor's decision flag. Wait
            // until it releases a new leader before recording that flag.
            if (nextLeader != leader && nextLeader?.Movement != null && nextLeader.Movement.InteractionHeld) return;
            ChangeLeader(nextLeader);
            if (movement == null || !movement.IsReady) return;
            movement.SetGuidePace(world.Player.GetComponent<PlayerController>());
            if (movement.InteractionHeld) return;
            var state = session.State.firstDay;
            state.escortPlace = session.CurrentPlace.ToString(); state.escortX = leader.transform.position.x;
            float distance = leader.transform.position.x - world.Player.position.x;
            if (waitingForPlayer)
            {
                if (distance > ResumeDistance) return;
                waitingForPlayer = false;
            }
            else if (distance > WaitDistance)
            {
                waitingForPlayer = true;
                if (movement.IsMoving) movement.Interrupt("waiting_for_player");
                return;
            }
            if (!movement.IsMoving && Time.time >= nextRouteAttempt)
            {
                // A single street-length route avoids restarting the authored start/
                // stop animation every time Z reaches the player's former position.
                float target = world.Stage.width / 2 - 1.5f;
                if (target > leader.transform.position.x + .4f)
                {
                    if (movement.MoveForConversation(new Vector2(target, leader.transform.position.y), 1))
                        routeRequest = movement.ActiveRequestId;
                    else nextRouteAttempt = Time.time + .5f;
                }
            }
        }
    }
}
