using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Text-only menu feedback. Runs while the title page freezes gameplay time.</summary>
    public sealed class TitleMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        Button button;
        TextMeshProUGUI label;
        Image marker;
        Color idle, active;
        bool over, selected, pressed;
        float emphasis;

        public void Initialize(Button source, TextMeshProUGUI text, Image selectionMark, Color normal, Color highlighted)
        {
            button = source; label = text; marker = selectionMark; idle = normal; active = highlighted;
            button.transition = Selectable.Transition.None;
        }

        void Update()
        {
            if (button == null || label == null || marker == null) return;
            bool enabled = button.IsInteractable();
            emphasis = Mathf.MoveTowards(emphasis, enabled && (over || selected) ? 1 : 0, Time.unscaledDeltaTime / .16f);
            float blend = Mathf.SmoothStep(0, 1, emphasis);
            var tint = Color.Lerp(idle, active, blend);
            if (pressed && enabled) tint.a *= .75f;
            label.color = tint;
            marker.color = new Color(active.r, active.g, active.b, blend * .88f);
            marker.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(8, 20, blend), 1.5f);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            over = true;
            if (button != null && button.IsInteractable() && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }
        public void OnPointerExit(PointerEventData e) { over = false; pressed = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) pressed = true; }
        public void OnPointerUp(PointerEventData e) => pressed = false;
        public void OnSelect(BaseEventData e) => selected = true;
        public void OnDeselect(BaseEventData e) { selected = false; over = false; pressed = false; }
        void OnDisable()
        {
            over = selected = pressed = false; emphasis = 0;
            if (label != null) label.color = idle;
            if (marker != null) marker.color = new Color(active.r, active.g, active.b, 0);
        }
    }
}
