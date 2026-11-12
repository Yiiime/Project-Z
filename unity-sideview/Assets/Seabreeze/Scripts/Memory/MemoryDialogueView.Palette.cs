using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    // Local presentation only. The source UI Kit textures, blur and night palette stay intact.
    public sealed partial class MemoryDialogueView
    {
        static readonly Color DayInk = new Color(.075f, .09f, .15f, 1);
        static readonly Color DayNpcTint = new Color(.78f, .82f, .92f, 1);
        static readonly Color DayPlayerTint = new Color(.79f, .85f, .94f, 1);
        Image replySurface, sendSurface;
        TextMeshProUGUI sendCaption;
        RawImage daylightVeil;
        Texture2D daylightFeather;
        readonly System.Collections.Generic.List<Image> headerControls = new System.Collections.Generic.List<Image>();
        Shadow titleShadow, locationShadow;
        bool IsDaylight => session != null && session.Clock != null &&
            (session.Clock.Period == TimePeriod.Morning || session.Clock.Period == TimePeriod.Afternoon);

        void BuildDaylightPresentation()
        {
            // A feathered scrim protects the column over bright sky without creating a large card.
            // The mask is tiny and bilinearly filtered; the captured scene retains its full blur quality.
            var rect = Stretch("Daylight reading veil", column, new Vector2(-85, -80), new Vector2(85, 70));
            rect.SetAsFirstSibling();
            daylightVeil = rect.gameObject.AddComponent<RawImage>();
            daylightVeil.raycastTarget = false;
            const int width = 96, height = 64;
            daylightFeather = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Dialogue daylight feather",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1), v = y / (float)(height - 1);
                float edgeX = Mathf.SmoothStep(0, 1, Mathf.Min(u, 1-u) / .17f);
                float edgeY = Mathf.SmoothStep(0, 1, Mathf.Min(v, 1-v) / .15f);
                pixels[y * width + x] = new Color(1, 1, 1, edgeX * edgeY);
            }
            daylightFeather.SetPixels(pixels);
            daylightFeather.Apply(false, true);
            daylightVeil.texture = daylightFeather;
            daylightVeil.color = new Color(.12f, .17f, .27f, .27f);
            titleShadow = HeaderShadow(playerTitle);
            locationShadow = HeaderShadow(locationText);
            RefreshDialoguePalette();
        }

        static Shadow HeaderShadow(TextMeshProUGUI label)
        {
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.06f, .09f, .17f, .48f);
            shadow.effectDistance = new Vector2(0, -1.3f);
            return shadow;
        }

        void RefreshDialoguePalette()
        {
            bool day = IsDaylight;
            if (daylightVeil != null) daylightVeil.gameObject.SetActive(day);
            if (titleShadow != null) titleShadow.enabled = day;
            if (locationShadow != null) locationShadow.enabled = day;
            if (replySurface != null)
            {
                replySurface.color = day ? new Color(.67f, .73f, .84f, .88f) : new Color(.94f, .95f, 1, .64f);
                ((RoundedPanel)replySurface).SetBorder(day ? new Color(.85f, .9f, 1, .44f) : new Color(1, 1, 1, .28f));
            }
            if (sendSurface != null)
            {
                sendSurface.color = day ? new Color(.84f, .88f, .97f, .68f) : new Color(1, 1, 1, .28f);
                sendCaption.color = day ? DayInk : BubbleInk;
            }
            if (input != null)
            {
                input.textComponent.color = input.caretColor = day ? DayInk : BubbleInk;
                input.placeholder.color = day ? new Color(.11f, .15f, .23f, .82f) : new Color(.2f, .23f, .3f, .63f);
            }
            foreach (var surface in headerControls)
                surface.color = day ? new Color(.74f, .8f, .91f, .78f) : new Color(.92f, .94f, 1, .45f);
            foreach (var bubble in history) ApplyBubblePalette(bubble);
        }

        void ApplyBubblePalette(ChatBubble bubble)
        {
            bubble.Surface.color = IsDaylight ? (bubble.IsPlayer ? DayPlayerTint : DayNpcTint) : Color.white;
            bubble.Text.color = IsDaylight ? DayInk : BubbleInk;
        }

        void OnDestroy()
        {
            if (session != null && session.Clock != null) session.Clock.Changed -= RefreshDialoguePalette;
            if (daylightFeather != null) Destroy(daylightFeather);
        }
    }
}
