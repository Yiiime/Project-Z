using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>
    /// Opens the ordinary Street Z dialogue and optionally runs model, speech,
    /// motion, exit and switch checks.
    ///
    /// -zPortraitPreview                         opens the normal UI only
    /// -zPortraitCapture &lt;dir&gt;                   writes screenshots/result.txt
    /// -zFramingCapture &lt;dir&gt;                    writes four resolution samples
    /// </summary>
    public sealed class ZPortraitPlaytest : MonoBehaviour
    {
        const string ActorId = "z";
        const float Timeout = 120f;
        const float FramingTolerancePercent = 5f;
        static bool Has(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        static readonly bool PreviewEnabled = Has("-zPortraitPreview");
        static readonly bool PortraitEnabled = Has("-zPortraitCapture");
        static readonly bool FramingEnabled = Has("-zFramingCapture");
        MemoryGameController memory;
        string output;
        float deadline;

        [Serializable]
        sealed class CaptureReport
        {
            public bool passed;
            public string mode = "zPortraitCapture", failure, actorId = ActorId;
            public bool modelVisible, profileId, requiredParameters, speechObserved, speechReturnedIdle;
            public bool motionsObserved, motionsReturnedIdle, closed, switched;
            public int parameterCount, drawableCount;
            public string[] textures = Array.Empty<string>();
            public string[] motionResults = Array.Empty<string>();
            public float speechPeak, speechFinal;
        }

        struct Stage
        {
            public string name, image;
            public int width, height;
            public FullScreenMode mode;
            public Stage(string n, string i, int w, int h, FullScreenMode m) { name = n; image = i; width = w; height = h; mode = m; }
        }

        [Serializable]
        sealed class StageReport
        {
            public string stage, image, failure;
            public int requestedWidth, requestedHeight, actualWidth, actualHeight, boundedDrawables;
            public string requestedMode, actualMode;
            public float heightRatio, headTop, heightErrorPercent, headTopErrorPercent;
            public bool screenPassed, metricsPassed, framingPassed, passed;
        }

        [Serializable]
        sealed class FramingReport
        {
            public bool passed;
            public string mode = "zFramingCapture", error;
            public float tolerancePercent = FramingTolerancePercent;
            public StageReport[] stages = Array.Empty<StageReport>();
        }

        struct Metrics
        {
            public bool valid;
            public int bounded;
            public float height, headTop;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!PreviewEnabled && !PortraitEnabled && !FramingEnabled) return;
            var go = new GameObject("Z portrait playtest");
            DontDestroyOnLoad(go);
            go.AddComponent<ZPortraitPlaytest>();
        }

        void Update()
        {
            if (deadline > 0 && output != null && Time.realtimeSinceStartup > deadline)
            {
                File.WriteAllText(Path.Combine(output, "result.txt"), "TIMEOUT\n");
                Debug.LogError("Z portrait capture timed out.");
                output = null;
                Application.Quit(2);
            }
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            output = ZLive2DProbe.Argument(args, FramingEnabled ? "-zFramingCapture" : "-zPortraitCapture");
            if ((FramingEnabled || PortraitEnabled) && string.IsNullOrEmpty(output))
            {
                Debug.LogError("Portrait capture requires an output directory.");
                Application.Quit(2);
                yield break;
            }
            yield return OpenZDialogue();
            if (memory == null || memory.View == null || !memory.View.PlayerInputReady)
            {
                if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL\n"); }
                Debug.LogError("Z dialogue could not open for portrait playtest.");
                if (!string.IsNullOrEmpty(output)) Application.Quit(2);
                yield break;
            }
            if (string.IsNullOrEmpty(output)) yield break;
            Directory.CreateDirectory(output);
            deadline = Time.realtimeSinceStartup + (FramingEnabled ? Timeout : 75f);
            if (FramingEnabled) yield return CaptureFraming();
            else yield return CapturePortrait();
        }

        IEnumerator OpenZDialogue()
        {
            float until = Time.realtimeSinceStartup + 40;
            while (Time.realtimeSinceStartup < until)
            {
                var session = GameSession.Instance; memory = session?.Memory; var world = WorldDirector.Instance;
                var z = memory?.Spawner?.Find(ActorId);
                if (session != null && session.IsReady && world?.Player != null && !world.IsTransitioning && z != null && z.IsPresent && z.Movement != null && z.Movement.IsReady)
                {
                    if (!memory.PlayerChatOpen)
                    {
                        world.Player.GetComponent<PlayerController>().Teleport(z.transform.position + Vector3.left * 1.2f);
                        world.Follow?.Snap(); memory.BeginPlayerChat(ActorId);
                    }
                    if (memory.View != null && memory.View.PlayerInputReady) yield break;
                }
                yield return null;
            }
        }

        IEnumerator CapturePortrait()
        {
            var report = new CaptureReport();
            var portrait = memory.View.GetComponentInChildren<Live2DCharacterView>(true);
            var model = portrait?.CurrentModel;
            var parameters = model?.Parameters?.Where(p => p != null).ToDictionary(p => p.Id) ?? new Dictionary<string, CubismParameter>();
            var required = new[] { "ParamZSpeech", "ParamEyeSmile", "ParamZStarry", "ParamZRoundMouth", "ParamZTearQuiet", "ParamZTearSob" };
            var textures = model == null ? Array.Empty<Texture>() : model.GetComponentsInChildren<CubismRenderer>(true).Select(r => r.MainTexture).Where(t => t != null).Distinct().ToArray();
            report.modelVisible = portrait != null && portrait.IsVisible && model != null;
            report.profileId = portrait?.CurrentProfile != null && portrait.CurrentProfile.characterId == ActorId;
            report.requiredParameters = required.All(parameters.ContainsKey);
            report.parameterCount = model?.Parameters?.Length ?? 0;
            report.drawableCount = model?.Drawables?.Length ?? 0;
            report.textures = textures.Select(t => t.name + " " + t.width + "x" + t.height).ToArray();
            if (!report.modelVisible || !report.profileId || !report.requiredParameters)
            {
                report.failure = "Z 画像、profile ID 或实际参数不完整。";
                Finish(report); yield break;
            }
            yield return SaveFrame("01-idle.png");
            portrait.StopPortraitMotion();
            memory.View.ShowLine(ActorId, "刚刚那道光，你也看见了吗？等浪退一点，我们再往前走走吧。", Vector3.zero, true);
            var speech = parameters["ParamZSpeech"];
            float peak = 0; float until = Time.unscaledTime + 5;
            while (Time.unscaledTime < until)
            {
                peak = Mathf.Max(peak, speech.Value);
                if (speech.Value > .25f && !report.speechObserved) yield return SaveFrame("02-speaking.png");
                report.speechObserved |= speech.Value > .25f;
                yield return new WaitForEndOfFrame();
            }
            report.speechPeak = peak; report.speechFinal = speech.Value; report.speechReturnedIdle = speech.Value < .08f;
            var motions = new Dictionary<string, string> { { "starry_curious", "ParamZStarry" }, { "surprise", "ParamZRoundMouth" }, { "cry", "ParamZTearQuiet" }, { "bawl", "ParamZTearSob" } };
            bool motionPass = true; var motionResults = new List<string>();
            foreach (var pair in motions)
            {
                bool started = portrait.PlayPortraitMotion(pair.Key); bool reached = false; float end = Time.unscaledTime + 4;
                while (Time.unscaledTime < end)
                {
                    if (!reached && parameters[pair.Value].Value > .8f) { reached = true; yield return SaveFrame(pair.Key + ".png"); }
                    yield return null;
                }
                bool reset = parameters[pair.Value].Value < .1f;
                bool passed = started && reached && reset; motionResults.Add(pair.Key + ":" + (passed ? "PASS" : "FAIL")); motionPass &= passed;
            }
            report.motionResults = motionResults.ToArray(); report.motionsObserved = motionResults.Any(v => v.EndsWith(":PASS", StringComparison.Ordinal)); report.motionsReturnedIdle = motionPass;
            memory.Conversations.Cancel("Z 画像验收结束"); yield return new WaitForSecondsRealtime(.8f);
            report.closed = !memory.PlayerChatOpen && !portrait.IsVisible;
            var other = memory.Spawner.Actors.FirstOrDefault(a => a != null && a.IsPresent && a.npcId != ActorId && string.IsNullOrEmpty(SevenCharacterCatalog.Find(a.npcId)?.portrait));
            if (other != null)
            {
                WorldDirector.Instance.Player.GetComponent<PlayerController>().Teleport(other.transform.position + Vector3.left * 1.2f);
                memory.BeginPlayerChat(other.npcId); yield return new WaitForSecondsRealtime(.8f);
                report.switched = memory.View.PlayerInputReady && !portrait.IsVisible;
                memory.Conversations.Cancel("切换验收结束");
            }
            report.passed = report.modelVisible && report.profileId && report.requiredParameters && report.speechObserved && report.speechReturnedIdle && report.motionsReturnedIdle && report.closed && report.switched;
            if (!report.passed) report.failure = "实际模型参数、说话复位、动作复位、退出或切换未全部通过。";
            Finish(report);
        }

        IEnumerator CaptureFraming()
        {
            var report = new FramingReport(); var stages = new List<StageReport>();
            var display = Display.main; int nativeWidth = display?.systemWidth ?? Screen.currentResolution.width, nativeHeight = display?.systemHeight ?? Screen.currentResolution.height;
            var requested = new[]
            {
                new Stage("01-window-1440x900", "01-window-1440x900.png", 1440, 900, FullScreenMode.Windowed),
                new Stage("02-window-960x600", "02-window-960x600.png", 960, 600, FullScreenMode.Windowed),
                new Stage("03-fullscreen-native", "03-fullscreen-native.png", nativeWidth, nativeHeight, FullScreenMode.FullScreenWindow),
                new Stage("04-window-1440x900-return", "04-window-1440x900-return.png", 1440, 900, FullScreenMode.Windowed)
            };
            Metrics baseline = default; bool baselineReady = false; int oldWidth = Screen.width, oldHeight = Screen.height; FullScreenMode oldMode = Screen.fullScreenMode;
            foreach (var expected in requested)
            {
                var row = new StageReport { stage = expected.name, image = expected.image, requestedWidth = expected.width, requestedHeight = expected.height, requestedMode = expected.mode.ToString() };
                Screen.SetResolution(expected.width, expected.height, expected.mode);
                float wait = Time.realtimeSinceStartup + 12; int stable = 0;
                while (Time.realtimeSinceStartup < wait)
                {
                    stable = Screen.width == expected.width && Screen.height == expected.height && Screen.fullScreenMode == expected.mode ? stable + 1 : 0;
                    if (stable >= 4) break; yield return null;
                }
                yield return new WaitForSecondsRealtime(.35f);
                row.actualWidth = Screen.width; row.actualHeight = Screen.height; row.actualMode = Screen.fullScreenMode.ToString();
                row.screenPassed = stable >= 4 && Screen.width == expected.width && Screen.height == expected.height && Screen.fullScreenMode == expected.mode;
                var metrics = ReadMetrics(memory.View.GetComponentInChildren<Live2DCharacterView>(true)); row.boundedDrawables = metrics.bounded; row.heightRatio = metrics.height; row.headTop = metrics.headTop; row.metricsPassed = metrics.valid;
                if (row.metricsPassed && !baselineReady) { baseline = metrics; baselineReady = true; row.framingPassed = true; }
                else if (row.metricsPassed && baselineReady) { row.heightErrorPercent = Error(metrics.height, baseline.height); row.headTopErrorPercent = Error(metrics.headTop, baseline.headTop); row.framingPassed = row.heightErrorPercent <= FramingTolerancePercent && row.headTopErrorPercent <= FramingTolerancePercent; }
                row.passed = row.screenPassed && row.metricsPassed && row.framingPassed && (stages.Count == 0 || oldWidth != row.actualWidth || oldHeight != row.actualHeight || oldMode != Screen.fullScreenMode);
                if (!row.passed) row.failure = "screen/portrait frame drift";
                stages.Add(row); oldWidth = row.actualWidth; oldHeight = row.actualHeight; oldMode = Screen.fullScreenMode;
                yield return SaveFrame(expected.image);
            }
            report.stages = stages.ToArray(); report.passed = stages.Count == requested.Length && stages.All(s => s.passed); report.error = report.passed ? "" : string.Join("; ", stages.Where(s => !s.passed).Select(s => s.stage + ": " + s.failure));
            File.WriteAllText(Path.Combine(output, "framing-report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "result.txt"), report.passed ? "PASS\n" : "FAIL\n"); output = null; Application.Quit(report.passed ? 0 : 1);
        }

        static Metrics ReadMetrics(Live2DCharacterView portrait)
        {
            var model = portrait?.CurrentModel; var camera = portrait?.PortraitCamera; var result = new Metrics();
            if (portrait == null || model == null || camera == null || !portrait.IsVisible || model.Drawables == null) return result;
            float min = float.MaxValue, max = float.MinValue; bool found = false;
            foreach (var drawable in model.Drawables)
            {
                if (drawable == null || !drawable.gameObject.activeInHierarchy || drawable.VertexPositions == null) continue;
                bool bounded = false;
                foreach (var vertex in drawable.VertexPositions)
                {
                    var point = camera.WorldToViewportPoint(drawable.transform.TransformPoint(vertex));
                    if (point.z <= 0 || float.IsNaN(point.y) || float.IsInfinity(point.y)) continue;
                    bounded = found = true; min = Mathf.Min(min, point.y); max = Mathf.Max(max, point.y);
                }
                if (bounded) result.bounded++;
            }
            if (!found) return result;
            result.height = max - min; result.headTop = max; result.valid = result.height > .001f && result.bounded >= 2 && max >= 0 && max <= 1; return result;
        }

        static float Error(float value, float baseline) => Mathf.Abs(value - baseline) / Mathf.Max(.0001f, Mathf.Abs(baseline)) * 100f;

        IEnumerator SaveFrame(string name)
        {
            yield return new WaitForEndOfFrame(); var frame = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, name), frame.EncodeToPNG()); Destroy(frame);
        }

        void Finish(CaptureReport report)
        {
            File.WriteAllText(Path.Combine(output, "portrait-report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "result.txt"), report.passed ? "PASS\n" : "FAIL\n"); output = null; Application.Quit(report.passed ? 0 : 1);
        }
    }
}
