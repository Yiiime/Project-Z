using UnityEngine;

namespace Seabreeze
{
    public static class WorldArt
    {
        static Sprite oval, pixel;
        public static Sprite Pixel
        {
            get
            {
                if (pixel == null)
                {
                    var t = new Texture2D(2, 2);
                    t.SetPixels(new[]{Color.white, Color.white, Color.white, Color.white});
                    t.Apply();
                    pixel = UnityEngine.Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2);
                }

                return pixel;
            }
        }

        public static Sprite Oval
        {
            get
            {
                if (oval == null)
                {
                    const int n = 64;
                    var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                    var c = new Color[n * n];
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float d = new Vector2((x + .5f) / n * 2 - 1, (y + .5f) / n * 2 - 1).magnitude;
                            c[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01((1 - d) * n / 2));
                        }

                    t.SetPixels(c);
                    t.Apply();
                    oval = UnityEngine.Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
                }

                return oval;
            }
        }

        public static SpriteRenderer Sprite(string name, Sprite sprite, Vector2 position, Vector2 size, Color color, int order, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = position;
            var r = g.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            if (order >= 20 && order < 40)
            {
                var m = Resources.Load<Material>("Art/SpriteLit");
                if (m != null)
                    r.sharedMaterial = m;
            }

            r.color = color;
            r.sortingOrder = order;
            g.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1);
            return r;
        }

        public static Sprite Polygon(Vector2[] p)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in p)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
            }

            const float ppu = 180;
            int w = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) * ppu)), h = Mathf.Max(2, Mathf.CeilToInt((maxY - minY) * ppu));
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] cs = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float px = minX + (x + .5f) / ppu, py = minY + (y + .5f) / ppu;
                    bool inside = false;
                    for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                    {
                        if ((p[i].y > py) != (p[j].y > py) && px < (p[j].x - p[i].x) * (py - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                            inside = !inside;
                    }

                    cs[y * w + x] = inside ? Color.white : Color.clear;
                }

            t.SetPixels(cs);
            t.Apply();
            return UnityEngine.Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, .5f), ppu);
        }
    }
}
