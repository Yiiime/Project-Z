using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Seabreeze
{
    public class SceneAtmosphere : MonoBehaviour
    {
        WorldScene stage;
        SpriteRenderer tint, fog;
        Light2D global;
        Sprite dayArtwork, eveningArtwork;
        TimeOfDayMapArtwork periodArtwork;
        GameObject effects;
        void Start()
        {
            stage = GetComponent<WorldScene>();
            eveningArtwork = stage.backdrop != null ? stage.backdrop.sprite : null;
            periodArtwork = new TimeOfDayMapArtwork(stage.backdrop, stage.place, stage.floorImageFraction);
            dayArtwork = Resources.Load<Sprite>("Art/" + stage.place.ToString().ToLowerInvariant() + "_day");
            effects = new GameObject("Independent foreground and parallax");
            effects.transform.SetParent(transform);
            global = new GameObject("Global 2D light").AddComponent<Light2D>();
            global.transform.SetParent(transform);
            global.lightType = Light2D.LightType.Global;
            global.intensity = .95f;
            tint = WorldArt.Sprite("Time and weather wash", WorldArt.Pixel, new Vector2(0, 5), new Vector2(80, 40), Color.clear, 15, effects.transform);
            if (stage.place != PlaceId.Cafe)
            {
                for (int i = 0; i < 18; i++)
                {
                    var r = WorldArt.Sprite("Sea shimmer " + i, WorldArt.Oval, new Vector2((stage.place == PlaceId.Street ? -5 : 0) + i * 1.2f, .9f + (i % 4) * .55f), new Vector2(.25f + (i % 3) * .2f, .025f), new Color(.94f, .82f, .59f, .22f), -9, effects.transform);
                    var p = r.gameObject.AddComponent<ParallaxDrift>();
                    p.factor = .035f;
                    p.phase = i;
                }

                for (int i = 0; i < 4; i++)
                {
                    var g = new GameObject("Foreground wind grass " + i);
                    g.transform.SetParent(effects.transform);
                    g.transform.localPosition = new Vector3(-15 + i * 10, -.9f, 0);
                    for (int j = 0; j < 7; j++)
                    {
                        var r = WorldArt.Sprite("Blade", WorldArt.Oval, new Vector2(j * .06f, j % 3 * .09f), new Vector2(.06f, .6f + (j % 3) * .1f), new Color(.06f, .17f, .23f, .85f), 40, g.transform);
                        r.transform.rotation = Quaternion.Euler(0, 0, -35 + j * 12);
                    }

                    g.AddComponent<ParallaxDrift>().factor = -.055f;
                }
            }

            var warm = new GameObject("Warm practical 2D light").AddComponent<Light2D>();
            warm.transform.SetParent(transform);
            warm.transform.position = new Vector3(-10, 3, 0);
            warm.lightType = Light2D.LightType.Point;
            warm.color = new Color(1, .66f, .33f);
            warm.intensity = .6f;
            warm.pointLightOuterRadius = 5;
            warm.pointLightInnerRadius = 1;
            fog = WorldArt.Sprite("Weather mist", WorldArt.Pixel, new Vector2(0, 5), new Vector2(80, 40), Color.clear, 36, effects.transform);
            Refresh();
            GameSession.Instance.Clock.Changed += Refresh;
        }

        void Refresh()
        {
            int p = (int)GameSession.Instance.Clock.Period;
            bool hasArtwork = periodArtwork.SetPeriod((TimePeriod)p);
            if (!hasArtwork && stage.backdrop != null)
            {
                var selected = p < 2 && dayArtwork != null ? dayArtwork : eveningArtwork;
                if (selected != null)
                {
                    stage.backdrop.sprite = selected;
                    float scale = stage.width / selected.bounds.size.x;
                    stage.backdrop.transform.localScale = Vector3.one * scale;
                    stage.backdrop.transform.position = new Vector3(0, selected.bounds.size.y * scale * (.5f - stage.floorImageFraction), 0);
                }
            }

            Color[] colors = {new Color(.54f, .73f, 1, .025f), new Color(.96f, .73f, .38f, .035f), new Color(.28f, .2f, .5f, .12f), new Color(.04f, .09f, .25f, .39f)};
            tint.color = hasArtwork ? Color.clear : stage.place == PlaceId.Cafe ? colors[Mathf.Min(p, 2)] : colors[p];
            global.color = p == 3 ? new Color(.55f, .68f, 1) : Color.white;
            if (fog != null)
                fog.color = stage.place != PlaceId.Cafe && GameSession.Instance.Clock.WeatherLabel.Contains("薄雾") ? new Color(.64f, .75f, .8f, .2f) : Color.clear;
        }

        void OnDestroy()
        {
            if (GameSession.Instance != null)
                GameSession.Instance.Clock.Changed -= Refresh;
            periodArtwork?.Release();
        }
    }

    public class ParallaxDrift : MonoBehaviour
    {
        public float factor = .04f, phase;
        Vector3 origin;
        float reference;
        void Start()
        {
            origin = transform.position;
            reference = Camera.main != null ? Camera.main.transform.position.x : 0;
        }

        void LateUpdate()
        {
            if (Camera.main == null)
                return;
            transform.position = origin + Vector3.right * ((Camera.main.transform.position.x - reference) * factor);
            if (name.StartsWith("Sea shimmer"))
            {
                var r = GetComponent<SpriteRenderer>();
                Color c = r.color;
                c.a = .14f + Mathf.Sin(Time.time * .7f + phase) * .09f;
                r.color = c;
            }
        }
    }
}
