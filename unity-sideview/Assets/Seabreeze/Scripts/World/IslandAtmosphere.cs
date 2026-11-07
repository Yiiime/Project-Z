using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Seabreeze
{
    // These backgrounds have a single authored time variant. Never load legacy
    // street_day or procedural sea/grass on top of the new inland geography.
    public sealed class IslandAtmosphere : MonoBehaviour
    {
        public bool indoor;
        Light2D ambient;
        SpriteRenderer wash;
        void Start()
        {
            var stage=GetComponent<WorldScene>();
            var light=new GameObject("Island ambient light");light.transform.SetParent(transform,false);
            ambient=light.AddComponent<Light2D>();ambient.lightType=Light2D.LightType.Global;
            wash=WorldArt.Sprite("Island time wash",WorldArt.Pixel,new Vector2(0,8),new Vector2(stage.width+8,40),Color.clear,15,transform);
            Refresh();GameSession.Instance.Clock.Changed+=Refresh;
        }
        void Refresh()
        {
            int period=(int)GameSession.Instance.Clock.Period;
            if(indoor)period=Mathf.Min(period,2);
            Color[] colors={new Color(.55f,.76f,1,.02f),new Color(1,.76f,.48f,.045f),new Color(.32f,.23f,.49f,.13f),new Color(.035f,.07f,.20f,.48f)};
            wash.color=colors[period];ambient.intensity=1;
            ambient.color=period==3?new Color(.64f,.72f,1):Color.white;
        }
        void OnDestroy(){if(GameSession.Instance!=null)GameSession.Instance.Clock.Changed-=Refresh;}
    }
}
