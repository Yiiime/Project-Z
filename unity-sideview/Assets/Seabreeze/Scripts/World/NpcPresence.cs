using UnityEngine;

namespace Seabreeze
{
    public class NpcPresence : MonoBehaviour
    {
        public string npcId;
        public int character;
        public PlaceId location;
        GameObject visual;
        Interactable interaction;
        bool subscribed;
        bool presenceInitialized;
        void Start()
        {
            interaction = GetComponent<Interactable>();
            visual = new GameObject("Independent character sprite");
            visual.transform.SetParent(transform, false);
            var v = visual.AddComponent<CharacterVisual>();
            v.Build(character);
            visual.transform.localScale = Vector3.one * 1.17f;
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
            if (GameSession.Instance && GameSession.Instance.Conversation && GameSession.Instance.Conversation.IsActive) return;
            if (visual == null || !visual.activeSelf || WorldDirector.Instance == null || WorldDirector.Instance.Player == null)
                return;
            float dx = WorldDirector.Instance.Player.position.x - transform.position.x;
            if (Mathf.Abs(dx) > .6f && Mathf.Abs(dx) < 6)
                visual.transform.localScale = new Vector3(Mathf.Sign(dx) * 1.17f, 1.17f, 1);
        }

        void Refresh()
        {
            var s = GameSession.Instance;
            if (s == null)
                return;
            if (presenceInitialized && s.Conversation && s.Conversation.IsActive) return;
            var data = s.Database != null ? s.Database.FindNpc(npcId) : null;
            bool present = data != null && data.IsPresent(location, s.Clock.Period, s.State.flags);
            if (s.Conversation && s.Conversation.IsActive && s.Conversation.ActiveNpc == data && s.CurrentPlace == location) present = true;
            presenceInitialized = true;
            if (visual != null)
                visual.SetActive(present);
            if (interaction != null)
                interaction.enabled = present; // Availability also filters disabled markers.
        }
    }
}
