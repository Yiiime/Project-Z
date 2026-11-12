using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class CharacterMotionVerification
    {
        const string Folder = "Assets/Seabreeze/Resources/Art/PlayerMotion";
        const string RunKey = "Seabreeze.CharacterMotionVerification";
        static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
        static readonly List<Sample> samples = new List<Sample>();
        static readonly List<string> checks = new List<string>();
        static readonly List<string> editorIssues = new List<string>();
        static Keyboard keyboard;
        static InputSettings originalSettings, testSettings;
        static HideFlags originalFlags;
        static Camera captureCamera;
        static string failure, output;
        static int frame, fps, captureIndex, originalCaptureRate, originalTargetRate;
        static bool ending, baseline;
        static double deadline;
        static GameSession S => GameSession.Instance;
        static WorldDirector W => WorldDirector.Instance;

        [Serializable] sealed class Definition
        {
            public float canvasHeightUnits, strideLength;
            public string idle;
            public string[] walk;
            public string[] sourceKeyframes;
            public string[] referenceWalk;
            public Asset[] assets;
        }
        [Serializable] sealed class Asset
        {
            public string name, file, sha256, sourceFrame, sourceSha256;
            public int width, height;
            public int preservedLowerFromRow;
            public float pixelsPerUnit;
            public Vector2 pivot;
        }
        [Serializable] sealed class Sample
        {
            public float time, simulationTime, x, y, physicalX, velocity, scaleX, scaleY;
            public int renderedFrame;
            public string sprite, pose;
        }
        [Serializable] sealed class Report
        {
            public bool passed, baseline;
            public string unityVersion, error, savePath, trajectoryNote;
            public int fps, captureFrames;
            public string[] checks, editorIssues, walkingSprites;
            public float scaleSpan, maxPhysicsXDifference;
            public Sample[] samples;
        }

        static CharacterMotionVerification()
        {
            if (SessionState.GetBool(RunKey, false))
                SaveRepository.VerificationDirectory = SessionState.GetString(RunKey + ".save", "");
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("Seabreeze/Import Figma Player Motion")]
        public static void Import()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Import in Edit mode.");
            var definition = JsonUtility.FromJson<Definition>(File.ReadAllText(Folder + "/motion.json"));
            Require(definition.walk.Length >= 2 && definition.walk.Length <= 64
                && definition.assets.Length == definition.walk.Length + 1
                && definition.assets.Select(a => a.name).Distinct().Count() == definition.assets.Length
                && definition.walk.Distinct().Count() == definition.walk.Length
                && definition.walk.Concat(new[] { definition.idle }).All(name => definition.assets.Any(a => a.name == name)),
                "One idle and 2-64 unique, registered walk assets are required.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (Asset asset in definition.assets)
            {
                string path = Folder + "/" + asset.file;
                Require(Hash(path) == asset.sha256, "Original Figma bytes changed: " + asset.file);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Require(importer.DoesSourceTextureHaveAlpha(), "Animation sprites require a real alpha channel: " + asset.file);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = asset.pixelsPerUnit;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = asset.pivot;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Require(sprite != null && sprite.texture.width == asset.width && sprite.texture.height == asset.height,
                    "Import must preserve source dimensions: " + asset.file);
            }
            Debug.Log("[Player Motion] Imported " + definition.assets.Length + " registered images with calibrated scale and pivots.");
        }

        [MenuItem("Seabreeze/Verify Player Motion")]
        public static void Run()
        {
            if (Resources.Load<TextAsset>("Art/CastMotion/player/motion") != null) { CastMotionVerification.Run(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run from Edit mode after compilation.");
            if (!Application.isBatchMode)
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
            string root = Path.GetFullPath(Argument("-motionOutput", Path.Combine(Path.GetTempPath(), "player-motion-" + Guid.NewGuid().ToString("N"))));
            Directory.CreateDirectory(root);
            SessionState.SetString(RunKey + ".output", root);
            SessionState.SetString(RunKey + ".save", Path.Combine(root, "saves"));
            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(RunKey + ".finished", false);
            SaveRepository.VerificationDirectory = Path.Combine(root, "saves");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                output = SessionState.GetString(RunKey + ".output", "");
                baseline = Environment.GetCommandLineArgs().Contains("-motionBaseline");
                fps = int.Parse(Argument("-motionFps", "60"));
                Require(fps == 30 || fps == 60, "Verification FPS must be 30 or 60.");
                samples.Clear(); checks.Clear(); editorIssues.Clear(); steps.Clear();
                ending = false; failure = null; frame = -1; captureIndex = 0;
                deadline = EditorApplication.timeSinceStartup + 300;
                originalCaptureRate = Time.captureFramerate;
                originalTargetRate = Application.targetFrameRate;
                Time.captureFramerate = fps;
                Application.targetFrameRate = fps;
                steps.Push(Verify());
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(RunKey + ".finished", false))
                Complete("Play mode stopped before verification finished.", false);
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SaveRepository.VerificationDirectory = null;
                SessionState.SetBool(RunKey, false);
                if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(RunKey + ".exit", 1));
            }
        }

        static void OnLog(string text, string trace, LogType type)
        {
            if (trace.Contains("UnityEditor.Search.SearchDatabase")) { editorIssues.Add(text); return; }
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = text + "\n" + trace;
        }

        static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying || Time.frameCount == frame) return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "Verification timed out.");
                while (steps.Count > 0)
                {
                    var current = steps.Peek();
                    if (!current.MoveNext()) { steps.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { steps.Push(nested); continue; }
                    return;
                }
                Complete(null, true);
            }
            catch (Exception error) { Complete(error.ToString(), true); }
        }

        static IEnumerator Verify()
        {
            yield return Wait(() => S != null && S.IsReady && W != null && W.Stage != null && !W.IsTransitioning, 30, "Startup");
            Require(SaveRepository.PathOnDisk.StartsWith(Path.Combine(output, "saves") + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Isolated saves.");
            originalSettings = InputSystem.settings;
            originalFlags = originalSettings.hideFlags;
            originalSettings.hideFlags = HideFlags.DontSave;
            testSettings = Object.Instantiate(originalSettings);
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testSettings;
            keyboard = InputSystem.AddDevice<Keyboard>();
            S.NewGame();
            yield return Wait(() => !W.IsTransitioning && W.Stage.place == PlaceId.Street, 15, "New game");
            int budget = 60;
            while (S.Dialogue.IsOpen && budget-- > 0)
            {
                if (S.Dialogue.Choices.Count > 0) S.Dialogue.Choose(0); else S.Dialogue.Advance();
                yield return null;
            }
            Require(!S.Dialogue.IsOpen, "Introduction completes.");
            yield return null;
            foreach (var npc in Object.FindObjectsByType<NpcMovementController>(FindObjectsSortMode.None)) npc.SimulateDecisions = false;
            var player = W.Player.GetComponent<PlayerController>();
            var visual = player.GetComponentInChildren<CharacterVisual>();
            var motionProperty = typeof(CharacterVisual).GetProperty("UsesPlayerMotion");
            bool usesMotion = motionProperty != null && (bool)motionProperty.GetValue(visual);
            Require(baseline ? !usesMotion : usesMotion, baseline
                ? "Baseline recording requires the original pre-change visual code."
                : "The calibrated Figma player motion must be active.");
            var renderer = visual.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Character artwork");
            var body = player.GetComponent<Rigidbody2D>();
            var cameraObject = new GameObject("Temporary motion comparison camera") { hideFlags = HideFlags.DontSave };
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.orthographicSize = 2.75f;
            captureCamera.aspect = 16f / 9;
            captureCamera.transform.position = new Vector3(-11.2f, 2.2f, -10);
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Camera.main.backgroundColor;
            player.Teleport(new Vector2(-13, .1f));
            yield return Frames(15);
            for (int i = 0; i < fps * 1.4f; i++)
            {
                Press(i < fps * .7f ? Key.D : Key.A);
                yield return null;
                if (i % (fps / 30) == 0) Capture(null);
            }
            Press();
            player.Teleport(new Vector2(-13, .1f));
            yield return Frames(fps / 3);
            checks.Add("Same real scene, camera, input device and isolated save path for replay");
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            string facts = Facts();
            for (int i = 0; i < fps * 6; i++)
            {
                float t = (float)i / fps;
                if (t >= .5f && t < 1.5f) Press(Key.D);
                else if (t >= 2 && t < 3) Press(Key.A);
                else if (t >= 3.5f && t < 3.85f) Press(Key.D);
                else if (t >= 3.85f && t < 4.4f) Press(Key.A);
                else if (t >= 4.9f && t < 5.0f) Press(Key.Space);
                else if (t >= 5.2f && t < 5.3f) Press(Key.W);
                else if (t >= 5.5f && t < 5.6f) Press(Key.UpArrow);
                else Press();
                yield return null;
                Vector3 scale = renderer.transform.localScale;
                samples.Add(new Sample { time = t, simulationTime = Time.time, renderedFrame = Time.frameCount,
                    x = player.transform.position.x, y = player.transform.position.y,
                    physicalX = body.position.x, velocity = body.linearVelocity.x, scaleX = Mathf.Abs(scale.x), scaleY = scale.y,
                    sprite = renderer.sprite != null ? renderer.sprite.name : "missing", pose = visual.Pose });
                if (i % (fps / 30) == 0)
                    Capture(Path.Combine(output, "frames", "frame_" + (captureIndex++).ToString("D4") + ".png"));
            }
            Press();
            yield return Frames(fps / 3);
            Require(Facts() == facts, "Walking does not change story state or time.");
            Require(player.speed == 4.2f && player.acceleration == 28 && player.deceleration == 36,
                "Horizontal movement parameters unchanged.");
            Require(samples.Any(s => s.velocity > 3.5f) && samples.Any(s => s.velocity < -3.5f), "Real left and right input recorded.");
            Require(samples.Max(s => s.y) - samples.Min(s => s.y) < .15f && player.Grounded, "Space, W and Up no longer cause jumping.");
            checks.Add("Real input covers walk, stop, reversal and removed jump bindings without changing horizontal physics or story");
            if (!baseline)
            {
                var definition = JsonUtility.FromJson<Definition>(File.ReadAllText(Folder + "/motion.json"));
                string[] used = samples.Where(s => s.pose == "Walk").Select(s => s.sprite).Distinct().ToArray();
                Require(used.Length == definition.walk.Length && used.All(n => definition.walk.Contains(n)), "All registered walk poses are used.");
                Require(samples.All(s => Mathf.Abs(s.scaleX - 1) < .0001f && Mathf.Abs(s.scaleY - 1) < .0001f), "Sprite scale stays fixed in idle, walk and turn.");
                Require(visual.Pose == "Idle" && renderer.sprite.name == "idle", "Stops settle to the matching Figma idle.");
                foreach (Asset asset in definition.assets)
                {
                    Sprite sprite = Resources.Load<Sprite>("Art/PlayerMotion/" + asset.name);
                    Require(sprite != null && Hash(Folder + "/" + asset.file) == asset.sha256, "Every registered asset is present and unchanged.");
                    Require(Mathf.Abs(sprite.rect.height / sprite.pixelsPerUnit - definition.canvasHeightUnits) < .0001f, "Common canvas scale.");
                    Require(Vector2.Distance(sprite.pivot, new Vector2(asset.width * asset.pivot.x, asset.height * asset.pivot.y)) < .01f, "Calibrated body and sole pivot.");
                    if (!string.IsNullOrEmpty(asset.sourceFrame)) CheckPreservedLowerPixels(asset);
                }
                checks.Add(definition.assets.Length + " registered assets, " + definition.walk.Length + "-pose cycle, constant scale and calibrated pivots");
                bool revisedArms = definition.referenceWalk != null && definition.referenceWalk.Length > 0;
                if (revisedArms)
                {
                    Require(definition.referenceWalk.Length == definition.walk.Length, "Arm revision keeps the same number of lower-body poses.");
                    for (int i = 0; i < definition.walk.Length; i++)
                        Require(definition.assets.Single(a => a.name == definition.walk[i]).sourceFrame == definition.referenceWalk[i],
                            "Each revised pose uses the corresponding original lower-body frame.");
                    checks.Add("Arm revision preserves source PNGs, calibrated lower-body pixels and the existing 16-frame sequence");
                }
                if (definition.sourceKeyframes != null && definition.sourceKeyframes.Length > 0)
                {
                    Require(definition.walk.Length == definition.sourceKeyframes.Length * 2, "Each source interval receives one midpoint.");
                    string[] reference = revisedArms ? definition.referenceWalk : definition.walk;
                    for (int i = 0; i < definition.sourceKeyframes.Length; i++)
                        Require(reference[i * 2] == definition.sourceKeyframes[i], "Original lower-body keyframe order and normalized cycle time remain unchanged.");
                    checks.Add("Original lower-body keyframe timing retained with exactly one midpoint per interval");
                }
                float phase30 = CheckClock(30);
                float phase60 = CheckClock(60);
                float error = Mathf.Abs(phase30 - phase60);
                Require(Mathf.Min(error, 1 - error) < .0001f, "Walk distance produces the same phase at 30 and 60 FPS.");
                checks.Add("Walk cadence is distance-driven and independent of render FPS or idle duration");
            }
        }

        static void CheckPreservedLowerPixels(Asset asset)
        {
            string referencePath = Folder + "/" + asset.sourceFrame + ".png";
            Require(Hash(referencePath) == asset.sourceSha256, "The source image remains unchanged: " + asset.sourceFrame);
            Require(asset.preservedLowerFromRow > 0 && asset.preservedLowerFromRow < asset.height,
                "The arm sample declares a non-empty preserved lower-body region.");
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var revised = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Require(ImageConversion.LoadImage(source, File.ReadAllBytes(referencePath)), "Decode source PNG.");
                Require(ImageConversion.LoadImage(revised, File.ReadAllBytes(Folder + "/" + asset.file)), "Decode revised PNG.");
                Require(source.width == asset.width && source.height == asset.height
                    && revised.width == asset.width && revised.height == asset.height, "The revision keeps native source dimensions.");
                Color32[] before = source.GetPixels32(), after = revised.GetPixels32();
                // PNG row metadata is top-down; Unity pixel arrays begin at the bottom.
                int preservedPixels = (asset.height - asset.preservedLowerFromRow) * asset.width;
                for (int i = 0; i < preservedPixels; i++)
                    if (before[i].r != after[i].r || before[i].g != after[i].g
                        || before[i].b != after[i].b || before[i].a != after[i].a)
                        throw new InvalidOperationException("Lower-body pixels changed in " + asset.file);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(revised); }
        }

        static float CheckClock(int rate)
        {
            var preview = new GameObject("Inactive motion clock verification");
            preview.SetActive(false);
            preview.transform.localScale = Vector3.one * 1.17f;
            var visual = preview.AddComponent<CharacterVisual>();
            visual.Build(0);
            var reset = typeof(CharacterVisual).GetMethod("ResetPlayerMotion");
            var phase = typeof(CharacterVisual).GetProperty("PlayerMotionPhase");
            Require(reset != null && phase != null, "Player motion clock is inspectable.");
            reset.Invoke(visual, null);
            float initial = (float)phase.GetValue(visual);
            for (int i = 0; i < rate * 2; i++) visual.Animate(0, true, 0, 1f / rate);
            Require(Mathf.Abs((float)phase.GetValue(visual) - initial) < .00001f, "Idle does not advance the walk clock.");
            for (int i = 0; i < rate; i++) visual.Animate(4.2f, true, 0, 1f / rate);
            float result = (float)phase.GetValue(visual);
            Object.Destroy(preview);
            return result;
        }

        static void Complete(string error, bool exitPlayMode)
        {
            if (ending) return;
            ending = true;
            SessionState.SetBool(RunKey + ".finished", true);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (originalSettings != null) { InputSystem.settings = originalSettings; originalSettings.hideFlags = originalFlags; }
            if (testSettings != null) Object.Destroy(testSettings);
            if (captureCamera != null) Object.Destroy(captureCamera.gameObject);
            Time.captureFramerate = originalCaptureRate;
            Application.targetFrameRate = originalTargetRate;
            var report = new Report { passed = error == null, baseline = baseline, unityVersion = Application.unityVersion,
                error = error, savePath = SaveRepository.PathOnDisk, fps = fps, captureFrames = captureIndex,
                checks = checks.ToArray(), editorIssues = editorIssues.ToArray(), samples = samples.ToArray(),
                walkingSprites = samples.Where(s => s.pose == "Walk").Select(s => s.sprite).Distinct().ToArray(),
                scaleSpan = samples.Count == 0 ? 0 : samples.Max(s => s.scaleX) - samples.Min(s => s.scaleX) };
            string reference = Argument("-motionReference", null);
            if (reference != null && File.Exists(reference) && samples.Count > 0)
            {
                var before = JsonUtility.FromJson<Report>(File.ReadAllText(reference));
                if (before.fps == fps && before.samples.Length == samples.Count)
                {
                    report.maxPhysicsXDifference = samples.Select((sample, i) => Mathf.Abs(sample.physicalX - before.samples[i].physicalX)).Max();
                    // Separate Editor sessions do not start on the same physics-tick boundary.
                    // Keep this diagnostic; unchanged physics is checked by parameters and source diff.
                    report.trajectoryNote = "Independent editor recordings are not fixed-step synchronized; this delta is diagnostic, not a physics-change assertion.";
                }
            }
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            SessionState.SetInt(RunKey + ".exit", error == null ? 0 : 1);
            if (error == null) Debug.Log("[Player Motion] Verification passed: " + Path.Combine(output, "report.json"));
            else Debug.LogError("[Player Motion] " + error);
            if (exitPlayMode) EditorApplication.ExitPlaymode();
        }

        static void Capture(string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture render = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
                if (path == null) return;
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(render); Object.Destroy(pixels); }
        }

        static string Hash(string file)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant();
        }

        static string Facts()
        {
            var state = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(S.State));
            state.playerX = 0;
            return JsonUtility.ToJson(state);
        }

        static void Press(params Key[] keys) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Wait(Func<bool> condition, float timeout, string label)
        {
            double end = EditorApplication.timeSinceStartup + timeout;
            while (!condition()) { Require(EditorApplication.timeSinceStartup < end, label + " timed out."); yield return null; }
        }
        static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
