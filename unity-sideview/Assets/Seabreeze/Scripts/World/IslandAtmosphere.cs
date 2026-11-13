using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Seabreeze
{
    // Keep the authored base map for every period except an explicit evening variant.
    // Never load legacy street_day or procedural sea/grass over the new geography.
    public sealed class IslandAtmosphere : MonoBehaviour
    {
        public bool indoor;
        Light2D ambient;
        SpriteRenderer wash;
        EveningMapArtwork evening;
        void Start()
        {
            var stage=GetComponent<WorldScene>();
            evening=new EveningMapArtwork(stage.backdrop,stage.place);
            var light=new GameObject("Island ambient light");light.transform.SetParent(transform,false);
            ambient=light.AddComponent<Light2D>();ambient.lightType=Light2D.LightType.Global;
            wash=WorldArt.Sprite("Island time wash",WorldArt.Pixel,new Vector2(0,8),new Vector2(stage.width+8,40),Color.clear,15,transform);
            Refresh();GameSession.Instance.Clock.Changed+=Refresh;
        }
        void Refresh()
        {
            int period=(int)GameSession.Instance.Clock.Period;
            // Test the real period before the indoor night-lighting clamp.
            bool hasEvening=evening.SetEnabled(period==(int)TimePeriod.Evening);
            if(indoor)period=Mathf.Min(period,2);
            Color[] colors={new Color(.55f,.76f,1,.02f),new Color(1,.76f,.48f,.045f),new Color(.32f,.23f,.49f,.13f),new Color(.035f,.07f,.20f,.48f)};
            wash.color=hasEvening?Color.clear:colors[period];ambient.intensity=1;
            ambient.color=period==3?new Color(.64f,.72f,1):Color.white;
        }
        void OnDestroy()
        {
            if(GameSession.Instance!=null)GameSession.Instance.Clock.Changed-=Refresh;
            evening?.Release();
        }
    }

    // Per-scene, lazy resource ownership shared by the island and legacy lighthouse.
    // Fitting both axes to the original authored rectangle keeps doors/ground aligned
    // when an evening image has a different pixel resolution or import PPU/pivot.
    internal sealed class EveningMapArtwork
    {
        SpriteRenderer backdrop;
        Sprite original,variant;
        readonly string resourcePath;
        readonly Vector3 originalScale,originalPosition,originalCenter,originalSize;
        readonly Quaternion originalRotation;
        bool attempted,applied;

        public EveningMapArtwork(SpriteRenderer renderer,PlaceId place)
        {
            backdrop=renderer;
            resourcePath="Art/IslandEvening/"+place;
            if(backdrop==null||backdrop.sprite==null)return;
            original=backdrop.sprite;
            var t=backdrop.transform;
            originalScale=t.localScale;originalPosition=t.localPosition;
            originalRotation=t.localRotation;
            originalSize=Vector3.Scale(original.bounds.size,originalScale);
            originalCenter=originalPosition+originalRotation*Vector3.Scale(original.bounds.center,originalScale);
        }

        public bool SetEnabled(bool enabled)
        {
            if(!enabled){Restore();return false;}
            if(backdrop==null||original==null)return false;
            if(!attempted)
            {
                attempted=true;
                variant=Resources.Load<Sprite>(resourcePath);
            }
            if(variant==null||variant.bounds.size.x<=0||variant.bounds.size.y<=0)return false;
            var size=variant.bounds.size;
            var scale=new Vector3(originalSize.x/size.x,originalSize.y/size.y,originalScale.z);
            backdrop.sprite=variant;
            backdrop.transform.localScale=scale;
            backdrop.transform.localRotation=originalRotation;
            backdrop.transform.localPosition=originalCenter-originalRotation*Vector3.Scale(variant.bounds.center,scale);
            applied=true;
            return true;
        }

        void Restore()
        {
            if(!applied)return;
            if(backdrop!=null)
            {
                backdrop.sprite=original;
                backdrop.transform.localPosition=originalPosition;
                backdrop.transform.localScale=originalScale;
                backdrop.transform.localRotation=originalRotation;
            }
            applied=false;
        }

        public void Release()
        {
            Restore();
            // No global cache and no forced UnloadAsset of a possibly shared texture.
            // WorldDirector loads Single scenes; Unity reclaims unused assets then.
            variant=null;original=null;backdrop=null;
        }
    }
}
