using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Local exports from UI Kit · 终稿. No network dependency at runtime.</summary>
    public static class FinalKitAssets
    {
        const string Folder = "UI/FinalKit/";
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static TMP_FontAsset clockFont;
        static bool clockFontLoaded;

        public static TMP_FontAsset ClockFont
        {
            get
            {
                if (clockFontLoaded) return clockFont != null ? clockFont : UIPrimitives.Font;
                clockFontLoaded = true;
                var source = Resources.Load<Font>(Folder + "BodoniModa");
                if (source != null)
                {
                    clockFont = TMP_FontAsset.CreateFontAsset(source, 90, 9,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 512, 512,
                        AtlasPopulationMode.Dynamic, false);
                    if (clockFont != null)
                    {
                        clockFont.name = "Bodoni Moda · phone clock";
                        clockFont.TryAddCharacters("0123456789:");
                    }
                }
                return clockFont != null ? clockFont : UIPrimitives.Font;
            }
        }

        public static Sprite Sprite(string name, bool sliced = false)
        {
            string key = name + (sliced ? ":sliced" : "");
            if (sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            var texture = Resources.Load<Texture2D>(Folder + name);
            if (texture == null)
            {
                Debug.LogWarning("Missing FinalKit asset: " + name);
                return null;
            }
            // SVGs were rasterized at 3x. PPU 300 keeps the slice corners at design scale
            // on the project's Canvas (referencePixelsPerUnit = 100).
            Vector4 border = Vector4.zero;
            if (sliced) border = name == "bubble-current"
                ? new Vector4(48, 50, 48, 40) * 3
                : new Vector4(48, 42, 48, 38) * 3;
            var sprite = UnityEngine.Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f),
                300, 0, SpriteMeshType.FullRect, border);
            sprite.name = "UI Kit / " + name;
            sprites[key] = sprite;
            return sprite;
        }

        public static Image Icon(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = UIPrimitives.Rect("Kit / " + name, parent, anchor, pivot, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprite(name);
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        public static Image Bubble(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var image = Icon(name, parent, anchor, pivot, position, size);
            image.sprite = Sprite(name, true);
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            return image;
        }
    }
}
