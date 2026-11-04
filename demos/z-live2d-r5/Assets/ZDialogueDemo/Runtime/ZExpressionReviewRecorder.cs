using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZDialogueDemo
{
    /// <summary>Opt-in baseline preview capture: --z-review-record=/absolute/output --z-review-quit.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1100)]
    public sealed class ZExpressionReviewRecorder : MonoBehaviour
    {
        private const string RecordArg = "--z-review-record=";
        private const string QuitArg = "--z-review-quit";
        private const int Fps = 15;
        private const float Lead = .4f, Tail = .6f, IdleDuration = 2f, ModelTimeout = 30f;
        // Expanded previews first, then the six base previews. Keep this order stable.
        private static readonly ClipSpec[] Specs =
        {
            new ClipSpec(ZMotionPreset.Surprise,"surprise"), new ClipSpec(ZMotionPreset.Curious,"curious"),
            new ClipSpec(ZMotionPreset.Shy,"shy"), new ClipSpec(ZMotionPreset.Sad,"sad"),
            new ClipSpec(ZMotionPreset.Angry,"angry"), new ClipSpec(ZMotionPreset.Wink,"wink"),
            new ClipSpec(ZMotionPreset.Sorrow,"sorrow"), new ClipSpec(ZMotionPreset.Cry,"cry"),
            new ClipSpec(ZMotionPreset.Bawl,"bawl"), new ClipSpec(ZMotionPreset.Warm,"warm"),
            new ClipSpec(ZMotionPreset.Serious,"serious"), new ClipSpec(ZMotionPreset.LookLeft,"lookleft"),
            new ClipSpec(ZMotionPreset.LookRight,"lookright"), new ClipSpec(ZMotionPreset.Lean,"lean"),
            new ClipSpec(ZMotionPreset.Tilt,"tilt"), new ClipSpec(ZMotionPreset.Emphasis,"emphasis"),
            new ClipSpec(ZMotionPreset.Startle,"startle"), new ClipSpec(ZMotionPreset.Discover,"discover"),
            new ClipSpec(ZMotionPreset.Gloom,"gloom"), new ClipSpec(ZMotionPreset.Sigh,"sigh"),
            new ClipSpec(ZMotionPreset.Idle,"idle",IdleDuration), new ClipSpec(ZMotionPreset.Nod,"nod"),
            new ClipSpec(ZMotionPreset.Shake,"shake"), new ClipSpec(ZMotionPreset.Think,"think"),
            new ClipSpec(ZMotionPreset.Listen,"listen"), new ClipSpec(ZMotionPreset.Happy,"happy"),
        };

        [Serializable] private sealed class RectData { public int x, y, width, height; public string coordinateSystem; }
        [Serializable] private sealed class ClipData
        {
            public int index, neutralFrames, activeFrames, returnFrames, frameCount;
            public string name, preset, framesPath;
            public float neutralSeconds, activeSeconds, returnSeconds, durationSeconds;
        }
        [Serializable] private sealed class Manifest
        {
            public string schemaVersion = "z-expression-review-v1", captureSource = "screen-readpixels";
            public string startedUtc, completedUtc, faceCropBasis, neutralFrame;
            public int fps, frameWidth, frameHeight, screenWidth, screenHeight, clipCount;
            public RectData screenCrop, faceCrop;
            public ClipData[] clips;
        }
        [Serializable] private sealed class Partial
        {
            public string schemaVersion = "z-expression-review-partial-v1";
            public string state, startedUtc, updatedUtc, error;
            public int fps, completedClipCount, currentClip;
        }
        private readonly struct ClipSpec
        {
            public readonly ZMotionPreset motion; public readonly string name; public readonly float seconds;
            public ClipSpec(ZMotionPreset motion, string name, float seconds = -1f)
            { this.motion = motion; this.name = name; this.seconds = seconds > 0f ? seconds : ZPerformanceController.GetPreviewDuration(motion); }
        }
        private readonly struct BehaviourState
        {
            public readonly Behaviour component; public readonly bool enabled;
            public BehaviourState(Behaviour component) { this.component = component; enabled = component != null && component.enabled; }
        }

        private string outputDirectory, startedUtc;
        private bool enabledByCommandLine, quitWhenDone, running, abortRequested, restored, statePrepared;
        private int completedClipCount, currentClip = -1, frameIndex;
        private ZPerformanceController controller;
        private DialogueClient dialogueClient;
        private DialoguePanel dialoguePanel;
        private readonly List<BehaviourState> savedBehaviours = new List<BehaviourState>();
        private readonly List<ClipData> clips = new List<ClipData>();
        private int oldCaptureFramerate;
        private float oldTimeScale, oldCaptureDeltaTime, oldAmplitude = 1f, oldSpeed = 1f;
        private RectInt crop;
        private RectData faceCrop;
        private Texture2D captureTexture;

        public bool IsFinished { get; private set; }
        public string OutputDirectory => outputDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var output = string.Empty; var quit = false;
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(RecordArg, StringComparison.Ordinal)) output = arg.Substring(RecordArg.Length);
                else if (arg == QuitArg) quit = true;
            }
            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output) || FindObjectOfType<ZExpressionReviewRecorder>() != null) return;
            var go = new GameObject("Z Expression Review Recorder (command line)"); DontDestroyOnLoad(go);
            var recorder = go.AddComponent<ZExpressionReviewRecorder>();
            recorder.outputDirectory = Path.GetFullPath(output); recorder.quitWhenDone = quit; recorder.enabledByCommandLine = true;
            // Stop client/panel and ordinary UI synchronously where possible, before scene Start methods.
            recorder.PrepareRuntimeState();
        }
        private void Start() { if (enabledByCommandLine) StartCoroutine(Run()); }
        private void OnApplicationQuit()
        { if (running && completedClipCount < Specs.Length) { abortRequested = true; WritePartial("application_quit", "application_quit"); } }

        private IEnumerator Run()
        {
            running = true;
            var stack = new Stack<IEnumerator>(); stack.Push(RunBody());
            Exception error = null;
            while (stack.Count > 0)
            {
                var current = stack.Peek(); bool next;
                try { next = current.MoveNext(); }
                catch (Exception exception) { error = exception; break; }
                if (!next) { stack.Pop(); continue; }
                var yielded = current.Current;
                if (yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
            }
            if (error != null)
            {
                abortRequested = true; Debug.LogException(error, this);
                WritePartial("exception", error.GetType().Name + ": " + error.Message);
            }
            else if (!abortRequested && completedClipCount == Specs.Length)
            {
                try { WriteManifest(); }
                catch (Exception exception) { Debug.LogException(exception, this); WritePartial("exception", exception.Message); }
            }
            else WritePartial("aborted", "recording_aborted");
            RestoreRuntimeState(); running = false; IsFinished = true;
            if (quitWhenDone) { yield return null; Application.Quit(); }
        }

        private IEnumerator RunBody()
        {
            Directory.CreateDirectory(outputDirectory);
            startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); WritePartial("starting", string.Empty);
            if (!statePrepared) PrepareRuntimeState();
            yield return WaitForModel();
            if (controller == null || !controller.IsModelReady) throw new InvalidOperationException("ZPerformanceController 未就绪");
            for (var i = 0; i < 3; i++) { if (abortRequested) yield break; yield return new WaitForEndOfFrame(); }
            if (!TryBuildLayout()) throw new InvalidOperationException("无法确定 Z 模型或 Face_Skin 固定裁剪范围");
            captureTexture = new Texture2D(crop.width, crop.height, TextureFormat.RGBA32, false);
            controller.PreviewMotion(ZMotionPreset.Idle); yield return new WaitForEndOfFrame();
            CaptureFrame(Path.Combine(outputDirectory, "neutral-face.png")); WritePartial("recording", string.Empty);
            for (var i = 0; i < Specs.Length; i++)
            {
                if (abortRequested) yield break;
                currentClip = i; yield return CaptureClip(i, Specs[i]); completedClipCount = i + 1; WritePartial("recording", string.Empty);
            }
        }
        private IEnumerator WaitForModel()
        {
            var deadline = Time.realtimeSinceStartup + ModelTimeout;
            while (Time.realtimeSinceStartup < deadline)
            { if (controller == null) controller = FindObjectOfType<ZPerformanceController>(); if (controller != null && controller.IsModelReady) yield break; yield return null; }
        }
        private IEnumerator CaptureClip(int index, ClipSpec spec)
        {
            var neutral = Frames(Lead); var active = Frames(spec.seconds); var tail = Frames(Tail);
            var directory = Path.Combine(outputDirectory, "clips", (index + 1).ToString("00") + "-" + spec.name); Directory.CreateDirectory(directory); frameIndex = 0;
            controller.SetMotionAmplitudeMultiplier(1f); controller.SetMotionSpeedMultiplier(1f); controller.PreviewMotion(ZMotionPreset.Idle);
            yield return CaptureFrames(directory, neutral); controller.PreviewMotion(spec.motion);
            yield return CaptureFrames(directory, active); controller.PreviewMotion(ZMotionPreset.Idle); yield return CaptureFrames(directory, tail);
            clips.Add(new ClipData
            {
                index = index + 1, name = spec.name, preset = spec.motion.ToString(), framesPath = "clips/" + (index + 1).ToString("00") + "-" + spec.name + "/",
                neutralFrames = neutral, activeFrames = active, returnFrames = tail, frameCount = frameIndex,
                neutralSeconds = neutral / (float)Fps, activeSeconds = active / (float)Fps, returnSeconds = tail / (float)Fps, durationSeconds = frameIndex / (float)Fps,
            });
        }
        private IEnumerator CaptureFrames(string directory, int count)
        {
            for (var i = 0; i < count; i++)
            { if (abortRequested) yield break; yield return new WaitForEndOfFrame(); CaptureFrame(Path.Combine(directory, "frame-" + frameIndex.ToString("0000") + ".png")); frameIndex++; }
        }
        private void CaptureFrame(string path)
        {
            captureTexture.ReadPixels(new Rect(crop.x, crop.y, crop.width, crop.height), 0, 0, false); captureTexture.Apply(false, false);
            File.WriteAllBytes(path, captureTexture.EncodeToPNG());
        }

        private void PrepareRuntimeState()
        {
            if (statePrepared) return; statePrepared = true;
            controller = FindObjectOfType<ZPerformanceController>(); dialogueClient = FindObjectOfType<DialogueClient>(); dialoguePanel = FindObjectOfType<DialoguePanel>();
            oldCaptureFramerate = Time.captureFramerate; oldTimeScale = Time.timeScale; oldCaptureDeltaTime = Time.captureDeltaTime;
            if (controller != null) { oldAmplitude = controller.MotionAmplitudeMultiplier; oldSpeed = controller.MotionSpeedMultiplier; controller.SetMotionAmplitudeMultiplier(1f); controller.SetMotionSpeedMultiplier(1f); }
            RememberAndDisable(dialoguePanel); RememberAndDisable(dialogueClient); dialoguePanel?.StopAllCoroutines(); dialogueClient?.StopAllCoroutines();
            foreach (var canvas in FindObjectsOfType<Canvas>(true))
            {
                var keep = canvas != null && (canvas.name.IndexOf("Emotion Accents", StringComparison.OrdinalIgnoreCase) >= 0 || canvas.GetComponentsInChildren<ZEmotionAccents>(true).Length > 0);
                if (!keep) RememberAndDisable(canvas);
            }
            foreach (var eventSystem in FindObjectsOfType<EventSystem>(true)) RememberAndDisable(eventSystem);
            foreach (var input in FindObjectsOfType<BaseInputModule>(true)) RememberAndDisable(input);
            Time.captureFramerate = Fps; Time.captureDeltaTime = 1f / Fps; Time.timeScale = 1f;
        }
        private void RememberAndDisable(Behaviour component)
        {
            if (component == null) return;
            for (var i = 0; i < savedBehaviours.Count; i++) if (savedBehaviours[i].component == component) return;
            savedBehaviours.Add(new BehaviourState(component)); component.enabled = false;
        }
        private void RestoreRuntimeState()
        {
            if (restored) return; restored = true;
            if (captureTexture != null) { Destroy(captureTexture); captureTexture = null; }
            if (controller != null) { controller.SetMotionAmplitudeMultiplier(oldAmplitude); controller.SetMotionSpeedMultiplier(oldSpeed); controller.PreviewMotion(ZMotionPreset.Idle); }
            for (var i = savedBehaviours.Count - 1; i >= 0; i--) { var state = savedBehaviours[i]; if (state.component != null) state.component.enabled = state.enabled; }
            Time.captureFramerate = oldCaptureFramerate; Time.captureDeltaTime = oldCaptureDeltaTime; Time.timeScale = oldTimeScale;
        }

        private bool TryBuildLayout()
        {
            var camera = Camera.main;
            if (camera == null || controller == null || controller.CubismModel == null || Screen.width <= 0 || Screen.height <= 0 || !ScreenRect(controller.CubismModel.transform, camera, out var model)) return false;
            var mx = Mathf.Max(48f, model.width * .30f); var minX = Mathf.Max(0f, model.xMin - mx); var maxX = Mathf.Min(Screen.width, model.xMax + mx);
            var width = Mathf.Min(Screen.width, Mathf.Clamp(Mathf.CeilToInt(maxX - minX), 700, 1000)); var x = Mathf.Clamp(Mathf.RoundToInt((minX + maxX - width) * .5f), 0, Screen.width - width);
            var my = Mathf.Max(24f, model.height * .08f); var minY = Mathf.Max(0f, model.yMin - my); var maxY = Mathf.Min(Screen.height, model.yMax + my);
            var height = Mathf.Clamp(Mathf.CeilToInt(maxY - minY), 720, Screen.height); var y = Mathf.Clamp(Mathf.RoundToInt((minY + maxY - height) * .5f), 0, Screen.height - height); crop = new RectInt(x, y, width, height);
            var skin = FindDescendant(controller.CubismModel.transform, "Face_Skin"); var renderer = skin == null ? null : skin.GetComponentInChildren<Renderer>(true);
            if (renderer == null || !ScreenRect(renderer.transform, camera, out var face)) return false;
            faceCrop = ToImageRect(face); return faceCrop.width > 0 && faceCrop.height > 0;
        }
        private RectData ToImageRect(Rect screenRect)
        {
            var top = Screen.height - crop.y - crop.height; var left = Mathf.FloorToInt(screenRect.xMin) - crop.x; var upper = Mathf.FloorToInt(Screen.height - screenRect.yMax) - top;
            var right = Mathf.CeilToInt(screenRect.xMax) - crop.x; var lower = Mathf.CeilToInt(Screen.height - screenRect.yMin) - top;
            left = Mathf.Clamp(left, 0, crop.width); upper = Mathf.Clamp(upper, 0, crop.height); right = Mathf.Clamp(right, left, crop.width); lower = Mathf.Clamp(lower, upper, crop.height);
            return new RectData { x = left, y = upper, width = right - left, height = lower - upper, coordinateSystem = "top-left-relative-to-image" };
        }
        private static bool ScreenRect(Transform root, Camera camera, out Rect rect)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true); var minX = float.PositiveInfinity; var minY = float.PositiveInfinity; var maxX = float.NegativeInfinity; var maxY = float.NegativeInfinity;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue; var bounds = renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var p = camera.WorldToScreenPoint(new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x, (corner & 2) == 0 ? bounds.min.y : bounds.max.y, (corner & 4) == 0 ? bounds.min.z : bounds.max.z));
                    minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
                }
            }
            rect = new Rect(minX, minY, maxX - minX, maxY - minY); return !float.IsInfinity(minX) && rect.width > 1f && rect.height > 1f;
        }
        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null; if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++) { var found = FindDescendant(root.GetChild(i), name); if (found != null) return found; }
            return null;
        }

        private void WriteManifest()
        {
            var manifest = new Manifest
            {
                startedUtc = startedUtc, completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), fps = Fps,
                frameWidth = crop.width, frameHeight = crop.height, screenWidth = Screen.width, screenHeight = Screen.height,
                screenCrop = new RectData { x = crop.x, y = Screen.height - crop.y - crop.height, width = crop.width, height = crop.height, coordinateSystem = "top-left-relative-to-screen" },
                faceCrop = faceCrop, faceCropBasis = "Face_Skin.Renderer.bounds", neutralFrame = "neutral-face.png", clipCount = clips.Count, clips = clips.ToArray(),
            };
            File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), JsonUtility.ToJson(manifest, true));
            var partial = Path.Combine(outputDirectory, "partial.json"); if (File.Exists(partial)) File.Delete(partial);
        }
        private void WritePartial(string state, string error)
        {
            if (string.IsNullOrEmpty(outputDirectory)) return;
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var marker = new Partial
                {
                    state = state, startedUtc = startedUtc, updatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), error = error,
                    fps = Fps, completedClipCount = completedClipCount, currentClip = currentClip < 0 ? 0 : currentClip + 1,
                };
                File.WriteAllText(Path.Combine(outputDirectory, "partial.json"), JsonUtility.ToJson(marker, true));
            }
            catch (Exception exception) { Debug.LogError("[ZExpressionReviewRecorder] partial.json 写入失败：" + exception.Message); }
        }
        private static int Frames(float seconds) { return Mathf.Max(1, Mathf.CeilToInt(seconds * Fps)); }
    }
}
