using UnityEngine;
namespace Seabreeze
{
    public sealed class FirstDayEscort : MonoBehaviour
    {
        GameSession session; WorldDirector world; NpcPresence leader;
        public void Initialize(GameSession owner, WorldDirector director) { session = owner; world = director; }
        public void ResetRoute() { if (leader?.Movement != null) leader.Movement.SimulateDecisions = true; leader = null; }
        public float ArrivalX(PlaceId place)
        {
            var s = session.State.firstDay;
            return s.escortPlace == place.ToString() ? s.escortX : -world.Stage.width / 2 + 2;
        }
        void Update()
        {
            if (session.FirstDay?.Phase != FirstDayPhase.WalkToSquare) { if (leader != null) ResetRoute(); return; }
            if (world.IsTransitioning || world.Stage == null || world.Player == null || session.NonPlayerInputBlocked) return;
            leader = session.Memory?.Spawner.Find("z");
            if (leader?.Movement == null || !leader.Movement.IsReady || leader.Movement.InteractionHeld) return;
            leader.Movement.SimulateDecisions = false;
            var state = session.State.firstDay;
            state.escortPlace = session.CurrentPlace.ToString(); state.escortX = leader.transform.position.x;
            float distance = leader.transform.position.x - world.Player.position.x;
            if (distance > 6) { if (leader.Movement.IsMoving) leader.Movement.Interrupt("waiting_for_player"); return; }
            if (!leader.Movement.IsMoving)
            {
                float target = Mathf.Min(world.Stage.width / 2 - 1.5f, world.Player.position.x + 4);
                if (target > leader.transform.position.x + .4f)
                    leader.Movement.MoveForConversation(new Vector2(target, leader.transform.position.y), 1);
            }
        }
    }
}
