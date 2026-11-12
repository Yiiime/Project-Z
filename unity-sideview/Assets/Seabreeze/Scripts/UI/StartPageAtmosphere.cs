using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Small, independent ambient layers over existing painted scenery; no world simulation.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class StartPageAtmosphere : MaskableGraphic
    {
        public Rect ImageUV = new Rect(0, 0, 1, 1);
        public Rect Sea, Sky;
        public bool MotionEnabled = true;
        float elapsed;

        protected override void Awake() { base.Awake(); raycastTarget = false; }
        void Update()
        {
            if (!MotionEnabled) return;
            elapsed += Time.unscaledDeltaTime;
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Vector2 Point(Vector2 uv) => new Vector2(r.xMin + (uv.x - ImageUV.x) / ImageUV.width * r.width,
                r.yMin + (uv.y - ImageUV.y) / ImageUV.height * r.height);
            // Broken, low contrast highlights. Each glint has its own period and moves with the tide.
            for (int i = 0; i < 36; i++)
            {
                float seed = Mathf.Repeat(i * .618034f, 1);
                float pulse = Mathf.Pow(.5f + .5f * Mathf.Sin(elapsed * (.65f + seed * .35f) + i * 2.4f), 3);
                float x = Sea.x + Mathf.Repeat(seed + elapsed * .0008f, 1) * Sea.width;
                float y = Sea.y + Mathf.Repeat(i * .38196f, 1) * Sea.height;
                Vector2 p = Point(new Vector2(x, y));
                float width = (5 + 20 * seed) * r.height / 1080;
                Segment(vh, p - Vector2.right * width, p + Vector2.right * width, 1.1f * r.height / 1080,
                    new Color(.94f, .97f, .90f, .16f * pulse));
            }
            // Distant birds travel slowly; wing beats are curves, not a flipped sprite sequence.
            for (int i = 0; i < 3; i++)
            {
                float progress = Mathf.Repeat(elapsed / (64 + i * 6) + i * .16f, 1);
                Vector2 p = Point(new Vector2(Sky.x + progress * Sky.width,
                    Sky.y + Sky.height * (.30f + i * .18f + .04f * Mathf.Sin(elapsed * .3f + i))));
                float span = (5 + i) * r.height / 1080;
                float flap = Mathf.Sin(elapsed * 2.3f + i) * span * .35f;
                float opacity = Mathf.SmoothStep(0, 1, Mathf.Min(progress, 1 - progress) * 12) * .65f;
                var c = new Color(.93f, .96f, .97f, opacity);
                Segment(vh, p + new Vector2(-span, flap + 1), p + new Vector2(-span * .45f, 2), .95f, c);
                Segment(vh, p + new Vector2(-span * .45f, 2), p, .95f, c);
                Segment(vh, p, p + new Vector2(span * .45f, 2), .95f, c);
                Segment(vh, p + new Vector2(span * .45f, 2), p + new Vector2(span, flap + 1), .95f, c);
            }
        }
        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int j = vh.currentVertCount;
            vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
            vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
            vh.AddTriangle(j, j + 1, j + 2); vh.AddTriangle(j, j + 2, j + 3);
        }
    }
}
