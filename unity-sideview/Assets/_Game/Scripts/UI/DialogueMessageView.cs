using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZFromMoon.Dialogue;

namespace ZFromMoon.UI
{
    /// <summary>A plain-text bubble whose layout is independent of portrait and game state.</summary>
    [DisallowMultipleComponent]
    public sealed class DialogueMessageView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image bubble;
        [SerializeField] private LayoutElement bubbleLayout;
        [SerializeField] private LayoutElement rowLayout;
        [SerializeField] private HorizontalLayoutGroup rowAlignment;

        private const float HorizontalInset = 18f;
        private const float VerticalInset = 12f;
        private const float WidthFraction = 0.86f;
        private static readonly Color NpcBubble = new Color(0.25f, 0.19f, 0.37f, 0.82f);
        private static readonly Color PlayerBubble = new Color(0.15f, 0.40f, 0.44f, 0.82f);
        private bool reportedMissingReferences;

        public void Bind(DialogueMessage message, float availableWidth)
        {
            if (!HasReferences()) return;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.text = message != null ? message.Text : string.Empty;
            bool isPlayer = message != null && message.IsPlayer;
            bubble.color = isPlayer ? PlayerBubble : NpcBubble;
            label.color = new Color(0.97f, 0.97f, 0.99f, 1f);
            rowAlignment.childAlignment = isPlayer ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            Relayout(availableWidth);
        }

        public void Relayout(float availableWidth)
        {
            if (!HasReferences()) return;
            float rowWidth = Mathf.Max(1f, SafeSize(availableWidth, 600f) - rowAlignment.padding.horizontal);
            float maximumWidth = Mathf.Max(1f, rowWidth * WidthFraction);
            float minimumWidth = Mathf.Min(80f, maximumWidth);
            float naturalWidth = SafeSize(label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x, maximumWidth);
            float width = Mathf.Clamp(naturalWidth + HorizontalInset * 2f, minimumWidth, maximumWidth);
            float textWidth = Mathf.Max(1f, width - HorizontalInset * 2f);
            float textHeight = SafeSize(label.GetPreferredValues(label.text, textWidth, Mathf.Infinity).y, label.fontSize * 1.5f);
            float height = Mathf.Max(label.fontSize * 1.2f, textHeight) + VerticalInset * 2f;

            bubbleLayout.minWidth = 0f;
            bubbleLayout.preferredWidth = width;
            bubbleLayout.flexibleWidth = 0f;
            bubbleLayout.minHeight = height;
            bubbleLayout.preferredHeight = height;
            bubbleLayout.flexibleHeight = 0f;
            rowLayout.minHeight = height + rowAlignment.padding.vertical;
            rowLayout.preferredHeight = height + rowAlignment.padding.vertical;
            rowLayout.flexibleHeight = 0f;
        }

        private bool HasReferences()
        {
            if (label != null && bubble != null && bubbleLayout != null
                && rowLayout != null && rowAlignment != null) return true;
            if (!reportedMissingReferences)
            {
                Debug.LogError("DialogueMessageView requires its text, bubble and row layout references.", this);
                reportedMissingReferences = true;
            }
            return false;
        }

        private static float SafeSize(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? fallback : value;
        }
    }
}
