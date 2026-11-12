using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Original crescent signature, drawn as vector geometry at the canvas scale.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class StartPageMoonGraphic : MaskableGraphic
    {
        protected override void Awake() { base.Awake(); raycastTarget = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) * .42f;
            // A slim crescent, with both boundaries meeting at the tips.
            for (int i = 0; i < 80; i++)
            {
                float a = Mathf.PI * (.5f + i / 80f);
                float b = Mathf.PI * (.5f + (i + 1) / 80f);
                Vector2 outerA = rect.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Vector2 outerB = rect.center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius;
                Vector2 innerA = rect.center + new Vector2(Mathf.Cos(a) * .68f, Mathf.Sin(a)) * radius;
                Vector2 innerB = rect.center + new Vector2(Mathf.Cos(b) * .68f, Mathf.Sin(b)) * radius;
                Quad(vh, outerA, outerB, innerB, innerA, color);
            }
            var star = rect.center + new Vector2(.26f, .04f) * radius;
            Quad(vh, star + Vector2.up * 7, star + Vector2.right * 3,
                star + Vector2.down * 7, star + Vector2.left * 3, color);
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int offset = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(offset, offset + 1, offset + 2); vh.AddTriangle(offset, offset + 2, offset + 3);
        }
    }
}
