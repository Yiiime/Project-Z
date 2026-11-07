using UnityEngine;

namespace Seabreeze
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class IslandRouteTrigger : MonoBehaviour
    {
        public PlaceId destination;
        public string arrivalId;

        void Reset() { GetComponent<BoxCollider2D>().isTrigger = true; }
        void Awake() { GetComponent<BoxCollider2D>().isTrigger = true; }

        void OnTriggerEnter2D(Collider2D other) { TryTravel(other); }
        // If a menu/dialogue temporarily blocked the boundary, walking out should
        // resume normally without requiring the player to leave and re-enter it.
        void OnTriggerStay2D(Collider2D other) { TryTravel(other); }

        void TryTravel(Collider2D other)
        {
            var world = WorldDirector.Instance;
            if (!world || !world.Player || !world.Stage ||
                world.Stage.gameObject.scene != gameObject.scene || world.IsTransitioning)
                return;
            var body = other.attachedRigidbody;
            if (!body || body.transform != world.Player) return;
            world.TravelTo(destination, arrivalId);
        }
    }
}
