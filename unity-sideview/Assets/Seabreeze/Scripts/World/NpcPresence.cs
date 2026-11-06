using UnityEngine;

namespace Seabreeze
{
    public class NpcPresence : MonoBehaviour
    {
        public string npcId;
        public int character;
        public PlaceId location;
        public NpcDestination[] movementDestinations = System.Array.Empty<NpcDestination>();
        public NpcMovementController Movement { get; private set; }
        public CharacterVisual Visual { get; private set; }
        public bool IsPresent { get; private set; }
        public NpcData RuntimeData { get; set; }
        public Transform ConversationPartner { get; set; }
        GameObject visual;
        Interactable interaction;
        bool subscribed;
        void Start()
        {
            interaction = GetComponent<Interactable>();
            visual = new GameObject("Independent character sprite");
            visual.transform.SetParent(transform, false);
            var v = visual.AddComponent<CharacterVisual>();
            v.Build(character);
            Visual = v;
            visual.transform.localScale = Vector3.one * 1.17f;
            Movement = GetComponent<NpcMovementController>();
            if (Movement == null) Movement = gameObject.AddComponent<NpcMovementController>();
            Movement.Configure(this, v, movementDestinations);
            Subscribe();
            Refresh();
        }

        void Subscribe()
        {
            if (GameSession.Instance != null && !subscribed)
            {
                GameSession.Instance.Changed += Refresh;
                GameSession.Instance.Clock.Changed += Refresh;
                subscribed = true;
            }
        }

        void OnDestroy()
        {
            if (GameSession.Instance != null && subscribed)
            {
                GameSession.Instance.Changed -= Refresh;
                GameSession.Instance.Clock.Changed -= Refresh;
            }
        }

        void LateUpdate()
        {
            if (visual == null || !visual.activeSelf || WorldDirector.Instance == null || WorldDirector.Instance.Player == null)
                return;
            if (Movement != null && Movement.IsMoving) return;
            if (ConversationPartner != null)
            {
                float direction = ConversationPartner.position.x - transform.position.x;
                if (Mathf.Abs(direction) > .01f) visual.transform.localScale = new Vector3(Mathf.Sign(direction) * 1.17f, 1.17f, 1);
                return;
            }
            float dx = WorldDirector.Instance.Player.position.x - transform.position.x;
            if (Mathf.Abs(dx) > .6f && Mathf.Abs(dx) < 6)
                visual.transform.localScale = new Vector3(Mathf.Sign(dx) * 1.17f, 1.17f, 1);
        }

        void Refresh()
        {
            var s = GameSession.Instance;
            if (s == null)
                return;
            var data = RuntimeData != null ? RuntimeData : s.Database != null ? s.Database.FindNpc(npcId) : null;
            bool present = data != null && data.IsPresent(location, s.Clock.Period, s.State.flags);
            IsPresent = present;
            if (visual != null)
                visual.SetActive(present);
            if (interaction != null)
                interaction.enabled = present; // Availability also filters disabled markers.
            if (Movement != null) Movement.SetAvailable(present);
        }
    }
}
