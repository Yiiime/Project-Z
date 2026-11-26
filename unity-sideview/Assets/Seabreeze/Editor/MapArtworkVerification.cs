#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Seabreeze.Editor
{
    public static class MapArtworkVerification
    {
        // Asset/selector regression fixture. Actual lighting and film transitions are
        // checked separately in FirstDayJourneyVerification's Play Mode journey.
        public static void Verify(string output)
        {
            var samples = new JArray();
            foreach (var scene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                EditorSceneManager.OpenScene(scene.path);
                var stage = UnityEngine.Object.FindFirstObjectByType<WorldScene>();
                if (stage?.backdrop?.sprite == null) throw new Exception("Missing backdrop: " + scene.path);
                var backdrop = stage.backdrop;
                var original = backdrop.sprite;
                var position = backdrop.transform.localPosition;
                var scale = backdrop.transform.localScale;
                float width = backdrop.bounds.size.x;
                var selectorType = typeof(IslandAtmosphere).Assembly.GetType("Seabreeze.TimeOfDayMapArtwork", true);
                var selector = Activator.CreateInstance(selectorType, backdrop, stage.place, stage.floorImageFraction);
                try
                {
                    foreach (var period in new[] { TimePeriod.Afternoon, TimePeriod.Morning, TimePeriod.Evening, TimePeriod.Night, TimePeriod.Morning, TimePeriod.Afternoon })
                    {
                        string variant = period == TimePeriod.Morning ? "Morning" : period == TimePeriod.Evening || (period == TimePeriod.Night && stage.place == PlaceId.Square) ? "Evening" : null;
                        var expected = variant == null ? original : Resources.Load<Sprite>("Art/Island" + variant + "/" + stage.place);
                        bool applied = (bool)selectorType.GetMethod("SetPeriod").Invoke(selector, new object[] { period });
                        if (expected == null || backdrop.sprite != expected || applied != (variant != null)) throw new Exception("Wrong map: " + stage.place + " / " + period);
                        if (Mathf.Abs(backdrop.bounds.size.x - width) > .01f) throw new Exception("Map width drift: " + stage.place);
                        if (variant == null && (backdrop.transform.localPosition != position || backdrop.transform.localScale != scale)) throw new Exception("Base layout not restored: " + stage.place);
                        samples.Add(new JObject { ["scene"] = scene.path, ["period"] = period.ToString(), ["sprite"] = AssetDatabase.GetAssetPath(backdrop.sprite), ["width"] = backdrop.bounds.size.x });
                    }
                }
                finally { selectorType.GetMethod("Release").Invoke(selector, null); }
            }
            File.WriteAllText(Path.Combine(output, "map-selection-report.json"), new JObject { ["passed"] = true, ["scope"] = "Editor scene assets and real selector, six period transitions per enabled scene; not a Play Mode screenshot audit.", ["samples"] = samples }.ToString());
        }
    }
}
#endif
