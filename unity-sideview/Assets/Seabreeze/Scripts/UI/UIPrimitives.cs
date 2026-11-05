using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    public static class UIPrimitives
    {
        public static UIStyleConfig Style => UIStyleConfig.Current;
        public static Color Ink => Style.PanelFill;
        public static Color Paper => Style.TextPrimary;
        public static Color Muted => Style.TextSecondary;
        public static Color Accent => Style.AccentWarm;
        public static Color Cool => Style.AccentCool;
        public static TMP_FontAsset Font;
        static Sprite circleSprite;
        public static Sprite CircleSprite
        {
            get
            {
                if(circleSprite != null)return circleSprite;
                const int size=96;
                var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
                texture.name="UI soft circle"; texture.filterMode=FilterMode.Bilinear;
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(size*.5f,size*.5f));
                    pixels[y*size+x]=new Color(1,1,1,Mathf.Clamp01(size*.5f-distance));
                }
                texture.SetPixels(pixels);texture.Apply();
                circleSprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100);
                circleSprite.name="UI circle";
                return circleSprite;
            }
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }
        public static RectTransform Stretch(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = min; rt.offsetMax = max;
            return rt;
        }
        public static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent, anchor, pivot, position, size);
            var image = rt.gameObject.AddComponent<RoundedPanel>();
            image.color = color; image.raycastTarget = raycast;
            image.CornerRadius = size.y <= 3 || size.x <= 3 ? 0 : Style.CornerRadius;
            image.SetBorder(size.y <= 3 || size.x <= 3 ? Color.clear : Style.PanelBorder, Style.BorderWidth);
            return image;
        }
        public static TextMeshProUGUI Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, string value, float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var rt = Rect(name, parent, anchor, pivot, position, size);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Font != null ? Font : Style.Font; text.text = value; text.fontSize = fontSize; text.color = color;
            text.alignment = align; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.lineSpacing = Style.LineSpacing;
            return text;
        }
        public static Button Button(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, string caption, Action action, bool accent = false, float fontSize = 23)
        {
            Color fill = Style.ButtonFill;
            if (accent) fill = Color.Lerp(fill, new Color(.24f,.26f,.29f,.58f),.35f);
            var image = Panel(name, parent, anchor, pivot, position, size, fill, true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (action != null) button.onClick.AddListener(() => { if(WorldDirector.Instance == null || !WorldDirector.Instance.IsTransitioning) action(); });
            var label = Label("Label", image.transform, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, size - new Vector2(36,10), caption, fontSize, accent ? Accent : Paper, TextAlignmentOptions.MidlineLeft);
            label.textWrappingMode = TextWrappingModes.Normal;
            UIHoverFeedback.Attach(button,accent);
            return button;
        }
        public static Image Line(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
            => Panel("Hairline", parent, anchor, pivot, pos, size, color);

        public static void Clear(Transform transform)
        {
            for(int i = transform.childCount-1; i>=0; i--) {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }

    // Small code-native UI line icons; no texture/font dependency or missing-symbol glyphs.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LineIcon : MaskableGraphic
    {
        public enum Symbol { Map, Phone, Journal, Close, Sun, Pin, Sound, Moon, Arrow, Circle }
        public Symbol symbol;
        public bool ring;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            Vector2 center = r.center;
            float s = Mathf.Min(r.width,r.height) * (ring ? .27f : .43f);
            if (ring) { Circle(vh,center,Mathf.Min(r.width,r.height)*.47f,1.15f); Circle(vh,center,Mathf.Min(r.width,r.height)*.41f,.6f); }
            Action<float,float,float,float> line = (x,y,x2,y2) => Segment(vh, center+new Vector2(x,y)*s, center+new Vector2(x2,y2)*s, 1.25f);
            switch(symbol)
            {
                case Symbol.Map:
                    line(-.85f,-.65f,-.85f,.65f); line(-.85f,.65f,-.28f,.88f); line(-.28f,.88f,.28f,.58f); line(.28f,.58f,.85f,.8f);
                    line(.85f,.8f,.85f,-.55f); line(.85f,-.55f,.28f,-.8f); line(.28f,-.8f,-.28f,-.55f); line(-.28f,-.55f,-.85f,-.65f);
                    line(-.28f,-.55f,-.28f,.88f); line(.28f,-.8f,.28f,.58f); break;
                case Symbol.Phone:
                    line(-.5f,-.85f,.5f,-.85f); line(.5f,-.85f,.5f,.85f); line(.5f,.85f,-.5f,.85f); line(-.5f,.85f,-.5f,-.85f); line(-.15f,-.6f,.15f,-.6f); break;
                case Symbol.Journal:
                    line(-.6f,-.8f,.65f,-.8f); line(.65f,-.8f,.65f,.8f); line(.65f,.8f,-.6f,.8f); line(-.6f,.8f,-.6f,-.8f);
                    for(int i=0;i<4;i++)line(-.84f,-.54f+i*.36f,-.4f,-.54f+i*.36f);
                    line(-.12f,.35f,.35f,.35f);line(-.12f,0,.35f,0);line(-.12f,-.35f,.22f,-.35f);break;
                case Symbol.Close: line(-.55f,-.55f,.55f,.55f);line(-.55f,.55f,.55f,-.55f);break;
                case Symbol.Sun:
                    Circle(vh,center,s*.4f,1.5f);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;line(Mathf.Cos(a)*.63f,Mathf.Sin(a)*.63f,Mathf.Cos(a)*.9f,Mathf.Sin(a)*.9f);}break;
                case Symbol.Moon:
                    for(int i=0;i<28;i++)
                    {
                        float a=(60+i*240f/28)*Mathf.Deg2Rad,b=(60+(i+1)*240f/28)*Mathf.Deg2Rad;
                        Segment(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*s*.8f,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*s*.8f,1.3f);
                    }
                    for(int i=0;i<20;i++)
                    {
                        float a=(110+i*140f/20)*Mathf.Deg2Rad,b=(110+(i+1)*140f/20)*Mathf.Deg2Rad;
                        Vector2 offset=Vector2.right*s*.60f;
                        Segment(vh,center+offset+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*s*.75f,center+offset+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*s*.75f,1.3f);
                    }
                    break;
                case Symbol.Pin:
                    Circle(vh,center+Vector2.up*s*.2f,s*.52f,1.5f);line(-.38f,-.15f,0,-.85f);line(0,-.85f,.38f,-.15f);Circle(vh,center+Vector2.up*s*.2f,s*.14f,1.2f);break;
                case Symbol.Sound:
                    line(-.8f,-.28f,-.4f,-.28f);line(-.4f,-.28f,.1f,-.7f);line(.1f,-.7f,.1f,.7f);line(.1f,.7f,-.4f,.28f);line(-.4f,.28f,-.8f,.28f);line(-.8f,.28f,-.8f,-.28f);
                    line(.4f,-.4f,.6f,0);line(.6f,0,.4f,.4f);line(.65f,-.68f,.95f,0);line(.95f,0,.65f,.68f);break;
                case Symbol.Arrow:line(-.7f,0,.65f,0);line(.2f,.45f,.65f,0);line(.2f,-.45f,.65f,0);break;
                default:Circle(vh,center,s*.65f,1.5f);break;
            }
        }
        void Circle(VertexHelper vh,Vector2 c,float r,float w)
        { for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;Segment(vh,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r,w);} }
        void Segment(VertexHelper vh,Vector2 a,Vector2 b,float w)
        { Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*w*.5f;int i=vh.currentVertCount;vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3); }
    }
}
