using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>A single UGUI graphic draws a rounded translucent fill and a fine inside border.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedPanel : Image
    {
        [SerializeField] float cornerRadius = 14f;
        [SerializeField] float borderWidth = 1f;
        [SerializeField] Color borderColor = new Color(.67f,.81f,.88f,.3f);
        public float CornerRadius
        {
            get => cornerRadius;
            set
            {
                value=Mathf.Max(0,value);
                if(Mathf.Approximately(cornerRadius,value))return;
                cornerRadius=value;SetVerticesDirty();
            }
        }
        public Color BorderColor => borderColor;
        public float BorderWidth => borderWidth;
        public void SetBorder(Color value, float width = 1f)
        {
            width=Mathf.Max(0,width);
            if(borderColor==value && Mathf.Approximately(borderWidth,width))return;
            borderColor=value;borderWidth=width;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // Circle sprites remain usable by the existing icon-button API.
            if (sprite != null) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Min(cornerRadius, Mathf.Min(rect.width,rect.height)*.5f);
            const int segments = 10;
            const int count = segments * 4;
            float stroke = Mathf.Min(borderWidth, Mathf.Min(rect.width,rect.height)*.5f);
            Vector2[] outer = new Vector2[count];
            Vector2[] inner = new Vector2[count];
            for (int corner = 0; corner < 4; corner++)
            {
                float start = corner * Mathf.PI * .5f;
                Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax-radius : rect.xMin+radius,
                    corner < 2 ? rect.yMax-radius : rect.yMin+radius);
                for (int step = 0; step < segments; step++)
                {
                    float angle = start + (step/(float)(segments-1))*Mathf.PI*.5f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                    int index = corner*segments+step;
                    outer[index] = center + direction*radius;
                    inner[index] = center + direction*Mathf.Max(0,radius-stroke);
                }
            }
            vh.AddVert(rect.center,color,Vector2.zero);
            for(int i=0;i<count;i++) vh.AddVert(outer[i],color,Vector2.zero);
            for(int i=0;i<count;i++) vh.AddTriangle(0,i+1,(i+1)%count+1);
            if(stroke<=0 || borderColor.a<=0)return;
            int offset = vh.currentVertCount;
            for(int i=0;i<count;i++) { vh.AddVert(outer[i],borderColor,Vector2.zero); vh.AddVert(inner[i],borderColor,Vector2.zero); }
            for(int i=0;i<count;i++)
            {
                int next=(i+1)%count;
                vh.AddTriangle(offset+i*2,offset+next*2,offset+next*2+1);
                vh.AddTriangle(offset+i*2,offset+next*2+1,offset+i*2+1);
            }
        }
    }
}
