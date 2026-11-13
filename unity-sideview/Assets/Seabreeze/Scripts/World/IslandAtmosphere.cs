using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Seabreeze
{
    // Use authored morning/evening variants; retain the base map for other periods.
    // Never load legacy street_day or procedural sea/grass over the new geography.
    public sealed class IslandAtmosphere : MonoBehaviour
    {
        public bool indoor;
        Light2D ambient;
        SpriteRenderer wash;
        TimeOfDayMapArtwork artwork;
        void Start()
        {
            var stage=GetComponent<WorldScene>();
            artwork=new TimeOfDayMapArtwork(stage.backdrop,stage.place,stage.floorImageFraction);
            var light=new GameObject("Island ambient light");light.transform.SetParent(transform,false);
            ambient=light.AddComponent<Light2D>();ambient.lightType=Light2D.LightType.Global;
            wash=WorldArt.Sprite("Island time wash",WorldArt.Pixel,new Vector2(0,8),new Vector2(stage.width+8,40),Color.clear,15,transform);
            Refresh();GameSession.Instance.Clock.Changed+=Refresh;
        }
        void Refresh()
        {
            int period=(int)GameSession.Instance.Clock.Period;
            // Test the real period before the indoor night-lighting clamp.
            bool hasArtwork=artwork.SetPeriod((TimePeriod)period);
            if(indoor)period=Mathf.Min(period,2);
            Color[] colors={new Color(.55f,.76f,1,.02f),new Color(1,.76f,.48f,.045f),new Color(.32f,.23f,.49f,.13f),new Color(.035f,.07f,.20f,.48f)};
            wash.color=hasArtwork?Color.clear:colors[period];ambient.intensity=1;
            ambient.color=period==3?new Color(.64f,.72f,1):Color.white;
        }
        void OnDestroy()
        {
            if(GameSession.Instance!=null)GameSession.Instance.Clock.Changed-=Refresh;
            artwork?.Release();
        }
    }

    // Per-scene, lazy resource ownership shared by the island and legacy lighthouse.
    // Fit variants independently of the currently displayed sprite. Selected morning
    // panoramas preserve aspect ratio and use an authored floor line in Layout.json.
    internal sealed class TimeOfDayMapArtwork
    {
        [System.Serializable]
        sealed class MapLayout
        {
            public MapLayoutEntry[] entries;
        }

        [System.Serializable]
        sealed class MapLayoutEntry
        {
            public string place;
            public float floorFromBottom;
        }

        SpriteRenderer backdrop;
        Sprite original,morning,evening;
        readonly PlaceId place;
        readonly Vector3 originalScale,originalPosition,originalCenter,originalSize,originalFloorAnchor;
        readonly Quaternion originalRotation;
        bool attemptedMorning,attemptedEvening,applied;
        bool attemptedLayout,hasMorningFloor;
        float morningFloorFromBottom;

        public TimeOfDayMapArtwork(SpriteRenderer renderer,PlaceId place,float originalFloorFraction)
        {
            backdrop=renderer;
            this.place=place;
            if(backdrop==null||backdrop.sprite==null)return;
            original=backdrop.sprite;
            var t=backdrop.transform;
            originalScale=t.localScale;originalPosition=t.localPosition;
            originalRotation=t.localRotation;
            originalSize=Vector3.Scale(original.bounds.size,originalScale);
            originalCenter=originalPosition+originalRotation*Vector3.Scale(original.bounds.center,originalScale);
            originalFloorAnchor=originalCenter+originalRotation*Vector3.up*(originalSize.y*(originalFloorFraction-.5f));
        }

        public bool SetPeriod(TimePeriod period)
        {
            if(backdrop==null||original==null)return false;
            Sprite variant=null;
            if(period==TimePeriod.Morning)
            {
                if(!attemptedMorning)
                {
                    attemptedMorning=true;
                    morning=Resources.Load<Sprite>("Art/IslandMorning/"+place);
                }
                variant=morning;
            }
            else if(period==TimePeriod.Evening)
            {
                if(!attemptedEvening)
                {
                    attemptedEvening=true;
                    evening=Resources.Load<Sprite>("Art/IslandEvening/"+place);
                }
                variant=evening;
            }
            if(variant==null||variant.bounds.size.x<=0||variant.bounds.size.y<=0)
            {
                // A missing requested variant must not leave the previous period visible.
                Restore();
                return false;
            }
            var size=variant.bounds.size;
            var scale=new Vector3(originalSize.x/size.x,originalSize.y/size.y,originalScale.z);
            var center=originalCenter;
            if(period==TimePeriod.Morning&&TryGetMorningFloor(out float floorFromBottom))
            {
                // Preserve the authored horizontal extent, image proportions and the
                // existing logical floor. Physics, camera and entrance positions stay put.
                scale.y=scale.x;
                float height=size.y*scale.y;
                center=originalFloorAnchor-originalRotation*Vector3.up*(height*(floorFromBottom-.5f));
            }
            backdrop.sprite=variant;
            backdrop.transform.localScale=scale;
            backdrop.transform.localRotation=originalRotation;
            backdrop.transform.localPosition=center-originalRotation*Vector3.Scale(variant.bounds.center,scale);
            applied=true;
            return true;
        }

        bool TryGetMorningFloor(out float floorFromBottom)
        {
            if(!attemptedLayout)
            {
                attemptedLayout=true;
                var json=Resources.Load<TextAsset>("Art/IslandMorning/Layout");
                if(json!=null)
                {
                    MapLayout layout=null;
                    try{layout=JsonUtility.FromJson<MapLayout>(json.text);}
                    catch(System.ArgumentException)
                    {
                        Debug.LogWarning("Invalid morning map Layout.json; using the original map rectangle.");
                    }
                    if(layout?.entries!=null)
                    {
                        foreach(var entry in layout.entries)
                        {
                            if(entry==null||entry.place!=place.ToString()||
                                float.IsNaN(entry.floorFromBottom)||float.IsInfinity(entry.floorFromBottom)||
                                entry.floorFromBottom<0||entry.floorFromBottom>1)continue;
                            morningFloorFromBottom=entry.floorFromBottom;
                            hasMorningFloor=true;
                            break;
                        }
                    }
                }
            }
            floorFromBottom=morningFloorFromBottom;
            return hasMorningFloor;
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
            morning=null;evening=null;original=null;backdrop=null;
        }
    }
}
