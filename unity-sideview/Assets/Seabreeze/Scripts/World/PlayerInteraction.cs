using UnityEngine;

namespace Seabreeze
{
    public class PlayerInteraction : MonoBehaviour
    {
        public Interactable Current { get; private set; }

        void Update()
        {
            var s = GameSession.Instance;
            if (s == null || !s.IsReady || s.InputBlocked || WorldDirector.Instance.IsTransitioning)
            {
                Current = null;
                if (GameUI.Instance != null)
                    GameUI.Instance.HideInteraction();
                return;
            }

            float nearest = float.MaxValue;
            Current = null;
            foreach (var item in WorldDirector.Instance.Interactables)
            {
                if (item == null || !item.enabled || !item.gameObject.activeInHierarchy || !item.Available)
                    continue;
                float distance = Vector2.Distance(transform.position, item.transform.position);
                if (distance < item.radius && distance < nearest)
                {
                    nearest = distance;
                    Current = item;
                }
            }

            if (GameUI.Instance != null)
            {
                if (Current != null)
                    GameUI.Instance.ShowInteraction(Current.npc ? "交谈" : (Current.isDoor ? Current.label : s.Story.PromptFor(Current.id)), Current.transform.position + Current.promptOffset);
                else
                    GameUI.Instance.HideInteraction();
            }

            if (Current != null && GameInput.InteractPressed)
                Current.Activate();
        }
    }
}
