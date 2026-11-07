using TMPro;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>Shared presentation tokens. Optional Resources/UI/DefaultUIStyle overrides these defaults.</summary>
    [CreateAssetMenu(menuName = "Seabreeze/UI Style", fileName = "DefaultUIStyle")]
    public sealed class UIStyleConfig : ScriptableObject
    {
        static UIStyleConfig current;
        public static UIStyleConfig Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<UIStyleConfig>("UI/DefaultUIStyle");
                if (current == null)
                {
                    current = CreateInstance<UIStyleConfig>();
                    current.name = "Seabreeze UI defaults";
                    current.hideFlags = HideFlags.DontSave;
                }
                return current;
            }
        }

        [Header("Glass and ink")]
        public Color PanelFill = new Color(.055f, .105f, .18f, .86f);
        public Color PanelBorder = new Color(.67f, .81f, .88f, .30f);
        public Color TextPrimary = new Color(.91f, .95f, .97f, 1f);
        public Color TextSecondary = new Color(.63f, .74f, .82f, 1f);
        public Color AccentCool = new Color(.58f, .80f, .86f, 1f);
        public Color AccentWarm = new Color(.94f, .75f, .51f, 1f);
        public Color Scrim = new Color(.02f, .045f, .085f, .47f);
        public Color ButtonFill = new Color(.12f, .20f, .28f, .48f);
        public Color ButtonHoverFill = new Color(.20f, .32f, .40f, .78f);
        public Color HudFill = new Color(.035f, .08f, .15f, .48f);
        public Color CardFill = new Color(.15f, .23f, .31f, .40f);

        [Header("Typography")]
        public TMP_FontAsset Font;
        public float BodySize = 24f;
        public float SmallSize = 18f;
        public float TitleSize = 36f;
        public float LineSpacing = 7f;
        public float DialogueBodySize = 28f;
        [Min(1)] public float TypewriterSpeed = 43f;

        [Header("Shape and motion")]
        [Min(0)] public float CornerRadius = 14f;
        [Min(0)] public float BorderWidth = 1f;
        [Min(.01f)] public float FadeInDuration = .24f;
        [Min(.01f)] public float FadeOutDuration = .18f;
        [Min(.01f)] public float HoverDuration = .16f;
        [Min(.01f)] public float DialogueEnterDuration = .36f;
        [Min(.01f)] public float DialogueExitDuration = .28f;
        public Vector2 ReferenceResolution = new Vector2(1920, 1080);
    }
}
