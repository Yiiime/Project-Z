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
                    GameUI.Instance.ShowInteraction("E  " + (Current.isDoor ? Current.label : s.Story.PromptFor(Current.id)), Current.transform.position + Current.promptOffset);
                else
                    GameUI.Instance.HideInteraction();
            }

            if (Current != null && GameInput.InteractPressed)
            {
                // Keep Shift+E on the same world interaction route as E. The
                // special case is intentionally limited to Zhoubo and the
                // explicit local preview launcher flag; all other NPCs and
                // props continue through Interactable.Activate unchanged.
                bool finalPreview = Current.npc && Current.id == "zhou_bobo" &&
                    GameInput.ShiftInteractPressed && s.Memory != null &&
                    s.Memory.LocalPortraitPreviewControls;
                if (finalPreview)
                    s.Memory.BeginPlayerChat(Current.id, true);
                else
                    Current.Activate();
            }
        }
    }
}
