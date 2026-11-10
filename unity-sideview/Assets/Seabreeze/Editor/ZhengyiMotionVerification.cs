using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
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
    public static class ZhengyiMotionVerification
    {
        const string Folder = "Assets/Seabreeze/Resources/Art/ZhengyiMotion";
        const string Key = "Seabreeze.ZhengyiMotionVerification";
        static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
        static readonly List<string> checks = new List<string>(), editorIssues = new List<string>();
        static readonly List<Sample> samples = new List<Sample>();
        static readonly List<Frame> frames = new List<Frame>();
        static string output, failure; static int lastFrame; static bool ending, recording;
        static double deadline; static float nextCapture, recordingStart;
        static Camera camera; static RenderTexture render;
        static Keyboard keyboard; static InputSettings originalInput, testInput; static HideFlags originalFlags;
        static int oldTarget, oldCapture, oldVSync;
        static GameSession S => GameSession.Instance;
        static WorldDirector W => WorldDirector.Instance;
        static MemoryGameController M => S != null ? S.Memory : null;
        [Serializable] sealed class Asset { public string file, sha256; public int width, height; public float pixelsPerUnit; public Vector2 pivot; }
        [Serializable] sealed class Definition { public float cycleSeconds, walkSpeed; public float[] frameDurations; public int[] stopFrames; public string[] walk; public Asset[] assets; }
        [Serializable] sealed class Reviewed { public Asset[] frames; }
        [Serializable] sealed class Sample { public string stage, sprite; public float time, x, physicalX, speed, phase, scaleX, scaleY; public int frame; }
        [Serializable] sealed class Frame { public string file, stage; public float time; }
        [Serializable] sealed class Report { public bool passed; public string error, savePath, unityVersion, mode; public string[] checks, editorIssues; public Sample[] samples; public Frame[] frames; }

        static ZhengyiMotionVerification()
        {
            if (SessionState.GetBool(Key, false)) SaveRepository.VerificationDirectory = SessionState.GetString(Key + ".saves", "");
            EditorApplication.playModeStateChanged += Changed;
        }
        [MenuItem("Seabreeze/Import Zhengyi approved video motion")]
        public static void Import()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Import from Edit mode.");
            var definition = JsonUtility.FromJson<Definition>(File.ReadAllText(Folder + "/motion.json"));
            var reviewed = JsonUtility.FromJson<Reviewed>(File.ReadAllText(Folder + "/source~/approved-video.json"));
            Require(definition.walk.Length == 32 && definition.frameDurations.Length == 32 && definition.assets.Length == 33, "32 approved video frames and one separate static idle.");
            Require(reviewed.frames.Length == 32 && reviewed.frames.All(a => definition.assets.Any(b => b.file == Path.GetFileName(a.file) && b.sha256 == a.sha256)), "Walk bytes match the reviewed extraction.");
            Require(Mathf.Abs(definition.frameDurations.Sum() - 4f / 3) < .00001f, "Original 4/3-second cycle.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var asset in definition.assets)
            {
                string path = Folder + "/" + asset.file;
                using (var hash = SHA256.Create())
                    Require(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() == asset.sha256, "Reviewed/declared bytes: " + asset.file);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Require(importer.DoesSourceTextureHaveAlpha(), "True alpha: " + asset.file);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = asset.pixelsPerUnit; importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 2048;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = asset.pivot; settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Require(sprite != null && sprite.texture.width == 600 && sprite.texture.height == 1056, "Native 600x1056 canvas retained.");
            }
            Debug.Log("[Zhengyi] Imported 32 original video frames and approved colour-matched static idle.");
        }
        [MenuItem("Seabreeze/Verify Zhengyi video motion in game")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run from Edit mode.");
            Require(!Application.isBatchMode, "The current dialogue backdrop requires a visible Game view. Run without -batchmode; Import supports batch mode.");
            Require(!string.IsNullOrEmpty(Arg("-memoryService", "")), "Provide an explicitly isolated simulated Memory Lab URL with -memoryService.");
            output = Path.GetFullPath(Arg("-zhengyiOutput", Path.Combine(Path.GetTempPath(), "zhengyi-" + Guid.NewGuid().ToString("N"))));
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            SessionState.SetString(Key + ".output", output); SessionState.SetString(Key + ".saves", Path.Combine(output, "saves"));
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".finished", false);
            SaveRepository.VerificationDirectory = Path.Combine(output, "saves");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity", OpenSceneMode.Single);
            if (!Application.isBatchMode)
            {
                var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                view.Show(); view.Focus();
            }
            EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                output = SessionState.GetString(Key + ".output", ""); ending = recording = false; failure = null; lastFrame = -1;
                samples.Clear(); frames.Clear(); checks.Clear(); editorIssues.Clear(); steps.Clear(); deadline = EditorApplication.timeSinceStartup + 360;
                oldTarget = Application.targetFrameRate; oldCapture = Time.captureFramerate; oldVSync = QualitySettings.vSyncCount;
                Time.captureFramerate = 60; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
                Application.logMessageReceived += Log; EditorApplication.update += Tick; steps.Push(Verify());
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(Key + ".finished", false)) Complete("Play mode interrupted.");
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SaveRepository.VerificationDirectory = null; SessionState.SetBool(Key, false);
                if (Application.isBatchMode || Arg("-zhengyiExit", "") == "1") EditorApplication.Exit(SessionState.GetInt(Key + ".exit", 1));
            }
        }
        static void Log(string text, string stack, LogType type)
        {
            if (stack.Contains("UnityEditor.Search.SearchDatabase")) { editorIssues.Add(text); return; }
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = text + "\n" + stack;
        }
        static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (ending || !EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                Require(failure == null, failure); Require(EditorApplication.timeSinceStartup < deadline, "Verification timeout.");
                if (recording && Time.time >= nextCapture) { Capture(); nextCapture = Time.time + 1f / 60; }
                while (steps.Count > 0)
                {
                    var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                    if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
                }
                Complete(null);
            }
            catch (Exception error) { Complete(error.ToString()); }
        }
        static IEnumerator Verify()
        {
            yield return Wait(() => M != null && W?.Stage != null && !W.IsTransitioning && M.Spawner.Find("zhengyi")?.Movement?.IsReady == true, 45, "Scene startup");
            Require(SaveRepository.PathOnDisk.StartsWith(Path.Combine(output, "saves") + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Isolated save root.");
            var npc = M.Spawner.Find("zhengyi");
            Require(npc.Visual.UsesVariableNpcTiming && npc.motionResource == "Art/ZhengyiMotion" && npc.RuntimeData.id == "zhengyi", "Current Zhengyi ID binds the approved video.");
            Require(string.Join(",", SevenCharacterCatalog.Actors.Select(a => a.id)) == "z,liyuan,liting,axing,zhengyi,xiaoxiaoqing,linxu,zhou_bobo", "Upstream eight-role roster preserved.");
            Require(SevenCharacterCatalog.Name("zhengyi") == "正义" && SevenCharacterCatalog.Name("xiaoxiaoqing") == "小小晴", "Upstream names preserved.");
            Require(W.Player.GetComponentInChildren<CharacterVisual>().UsesPlayerMotion, "Approved player visual remains active.");
            Require(M.Spawner.Actors.Where(a => a.npcId != "zhengyi").All(a => !a.Visual.UsesNpcMotion), "Only Zhengyi changes appearance.");
            checks.Add("zhengyi alone binds the video; eight-role identities, other NPC artwork and player motion remain intact");
            VerifyClock();
            M.Conversations.CancelAll("Movement verification setup"); M.Conversations.enabled = false;
            M.GetComponent<MemoryActionExecutor>().enabled = false;
            foreach (var a in M.Spawner.Actors) { a.Movement.SimulateDecisions = false; a.Movement.Interrupt(); }
            var player = W.Player.GetComponent<PlayerController>();
            player.Teleport(npc.transform.position + Vector3.left * 2.5f); W.Follow.Snap(); SetupCapture();
            recordingStart = Time.time; nextCapture = 0; recording = true;
            float origin = npc.transform.position.x;
            yield return Move(npc, origin - 3.4f, -1);
            yield return Frames(18); Require(npc.Visual.NpcMotionFrame == -1 && !npc.Visual.walking, "Leftward route settles to approved idle."); Capture();
            yield return Move(npc, origin, 1);
            yield return Frames(18); Require(npc.Visual.NpcMotionFrame == -1, "Rightward route settles to approved idle."); Capture();
            recording = false; checks.Add("Actual left/right movement displays all 32 frames with fixed scale, turns and returns to aligned idle");

            yield return Wait(() => M.Client.CanGenerate, 30, "Isolated model service");
            Require(M.Client.Mode == "simulated", "Use an explicitly isolated simulated provider for this check.");
            var partner = M.Spawner.Find("linxu");
            foreach (var a in M.Spawner.Actors) if (a != npc && a != partner) a.Movement.SetInteractionHold(true);
            M.Conversations.enabled = true;
            yield return Wait(() => M.Conversations.Stage == "交谈" && (M.Conversations.First == npc || M.Conversations.Second == npc), 50, "Zhengyi autonomous conversation");
            var pair = new[] { M.Conversations.First.npcId, M.Conversations.Second.npcId };
            Require(pair.Contains("zhengyi") && pair.Contains("linxu"), "Real coordinator reserves the two available actors.");
            var observer = M.Spawner.Find("xiaoxiaoqing"); observer.Movement.SetInteractionHold(false); observer.Movement.SimulateDecisions = true;
            player.Teleport(npc.transform.position + Vector3.left * 2.5f); W.Follow.Snap();
            yield return Wait(() => !string.IsNullOrEmpty(M.View.VisibleSpeech) && npc.Visual.NpcMotionFrame == -1, 25, "Existing A2A presentation and planted idle");
            Require(npc.Movement.InteractionHeld && partner.Movement.InteractionHeld && !S.InputBlocked, "Only participants are held; player remains free.");
            Capture(); File.Copy(Path.Combine(output, "frames", frames.Last().file), Path.Combine(output, "npc-chat.png"), true);
            int completed = M.Conversations.CompletedConversations;
            float low = observer.transform.position.x, high = low;
            double until = EditorApplication.timeSinceStartup + 95;
            while (M.Conversations.CompletedConversations == completed)
            {
                Require(EditorApplication.timeSinceStartup < until, "Autonomous conversation ends.");
                low = Mathf.Min(low, observer.transform.position.x); high = Mathf.Max(high, observer.transform.position.x); yield return null;
            }
            Require(high - low > .08f, "Unrelated available NPC keeps moving.");
            yield return Wait(() => M.Client.Synchronized, 25, "A2A receipt sync");
            Require(new[] { "zhengyi", "linxu" }.All(id => Messages().Any(m => (string)m["speakerId"] == id)), "Both participants' actual lines reach the current memory history.");
            checks.Add("Current A2A generates both participants, holds only them on idle, preserves speech receipts and lets another NPC move");
            M.Conversations.CancelAll("Player entry setup"); M.Conversations.enabled = false;
            foreach (var a in M.Spawner.Actors) { a.Movement.SetInteractionHold(false); a.Movement.SimulateDecisions = false; a.Movement.Interrupt(); }

            originalInput = InputSystem.settings; originalFlags = originalInput.hideFlags; originalInput.hideFlags = HideFlags.DontSave;
            testInput = Object.Instantiate(originalInput); testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testInput; keyboard = InputSystem.AddDevice<Keyboard>();
            player.Teleport(npc.transform.position + Vector3.right * .25f); W.Follow.Snap(); yield return Frames(5);
            Require(player.GetComponent<PlayerInteraction>().Current?.id == "zhengyi", "E targets Zhengyi.");
            Press(UnityEngine.InputSystem.Key.E); yield return null; Press();
            yield return Wait(() => M.Conversations.PlayerActor == npc && M.View.PlayerInputReady && M.Client.CanGenerate, 30, "Current player dialogue entry");
            M.Conversations.SendPlayer("你今天怎么样？");
            yield return Wait(() => M.LastReply?.actorId == "zhengyi" && !M.Conversations.Sending && M.Client.Synchronized, 30, "Current player reply and receipt");
            string reply = M.LastReply.result.dialogue;
            yield return Wait(() => M.View.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text == reply && t.maxVisibleCharacters >= t.textInfo.characterCount), 12, "Reply text visible");
            Require(npc.Visual.NpcMotionFrame == -1 && npc.Movement.InteractionHeld, "Player conversation keeps the approved idle.");
            Capture(); File.Copy(Path.Combine(output, "frames", frames.Last().file), Path.Combine(output, "player-chat.png"), true);
            M.Conversations.CancelAll("Save/load verification");
            yield return Wait(() => !M.PlayerChatOpen && M.Client.Synchronized, 20, "Conversation closes and syncs");
            var messageIds = Messages().Where(m => (m["participants"] as JArray)?.Any(a => (string)a == "zhengyi") == true).Select(m => (string)m["id"]).ToArray();
            Require(messageIds.Length > 0 && S.Save(), "Save isolated Zhengyi history.");
            S.LoadSavedGame();
            yield return Wait(() => !W.IsTransitioning && M.Client.CanGenerate && M.Client.Synchronized && M.Spawner.Find("zhengyi")?.Visual?.UsesVariableNpcTiming == true && M.Spawner.Find("zhengyi")?.Movement?.IsReady == true, 35, "Load rebinds current actor");
            Require(messageIds.All(id => Messages().Any(m => (string)m["id"] == id)), "Save/load preserves existing message IDs.");
            checks.Add("E uses current dialogue UI with planted idle; isolated save/load retains history IDs and visual binding");
            npc = M.Spawner.Find("zhengyi"); player = W.Player.GetComponent<PlayerController>();
            player.Teleport(npc.transform.position + Vector3.right * .25f); W.Follow.Snap(); yield return Frames(3);
            M.BeginPlayerChat("zhengyi"); yield return Wait(() => M.PlayerChatOpen, 5, "Chat before travel");
            S.Travel(PlaceId.Cafe);
            yield return Wait(() => !W.IsTransitioning && W.Stage.place == PlaceId.Cafe && !M.PlayerChatOpen, 25, "Travel cancels chat");
            S.Travel(PlaceId.Street);
            yield return Wait(() => !W.IsTransitioning && W.Stage.place == PlaceId.Street && M.Spawner.Find("zhengyi")?.Visual?.UsesVariableNpcTiming == true && M.Spawner.Find("zhengyi")?.Movement?.IsReady == true, 25, "Return to street");
            Require(!M.Spawner.Find("zhengyi").Movement.InteractionHeld, "No stale movement hold after travel.");
            checks.Add("Scene travel safely cancels chat and restores Zhengyi's timing without stale holds");
        }

        static void VerifyClock()
        {
            var def = JsonUtility.FromJson<Definition>(File.ReadAllText(Folder + "/motion.json"));
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var root = new GameObject("Inactive video clock check"); root.SetActive(false);
                try
                {
                    var visual = root.AddComponent<CharacterVisual>(); visual.Build(1, "Art/ZhengyiMotion");
                    var renderer = root.GetComponentInChildren<SpriteRenderer>(true); var seen = new HashSet<int>();
                    for (int i = 0; i < fps * 4; i++)
                    {
                        visual.AnimateNpcDisplacement(visual.NpcWalkSpeed / fps, visual.NpcWalkSpeed, 1f / fps);
                        seen.Add(visual.NpcMotionFrame);
                    }
                    Require(seen.Count == 32, "All original frames at " + fps + " FPS.");
                    Require(Mathf.Min(visual.NpcMotionPhase, 1 - visual.NpcMotionPhase) < .0001f, "Three original cycles in four seconds.");
                    float before = visual.NpcMotionPhase;
                    for (int i = 0; i < fps; i++) visual.AnimateNpcDisplacement(0, visual.NpcWalkSpeed, 1f / fps);
                    Require(visual.NpcMotionPhase == before, "Intent without movement does not animate travel.");
                    visual.AnimateNpcDisplacement(0, 0, 1f / fps);
                    Require(def.stopFrames.Contains(visual.NpcMotionFrame), "Only declared ground-contact stop transition.");
                    for (int i = 0; i < fps; i++) visual.AnimateNpcDisplacement(0, 0, 1f / fps);
                    Require(visual.NpcMotionFrame == -1 && renderer.sprite.name == "idle", "Stops use the separate standing image.");
                    Require(renderer.transform.localScale == Vector3.one && renderer.transform.localPosition == Vector3.zero, "No per-frame fitting/scaling.");
                }
                finally { Object.Destroy(root); }
            }
            var probe = new GameObject("Inactive frame timestamp probe"); probe.SetActive(false);
            try
            {
                var v = probe.AddComponent<CharacterVisual>(); v.Build(1, "Art/ZhengyiMotion");
                float elapsed = 0, previous = 0;
                for (int i = 0; i < 32; i++)
                {
                    float target = elapsed + def.frameDurations[i] * .5f;
                    v.AnimateNpcDisplacement((target - previous) * v.NpcWalkSpeed, v.NpcWalkSpeed, target - previous);
                    Require(v.NpcMotionFrame == i, "Original variable frame midpoint " + i);
                    Require(Mathf.Abs(v.NpcFrameStartSeconds(i) - elapsed) < .00001f, "Cumulative original timestamp " + i);
                    previous = target; elapsed += def.frameDurations[i];
                }
            }
            finally { Object.Destroy(probe); }
            checks.Add("Original 33.333/50ms intervals, cycle wrap and distance-driven timing at 30/60/120 FPS; no per-frame scaling or lifted-foot final idle");
        }
        static IEnumerator Move(NpcPresence npc, float target, int facing)
        {
            var body = npc.GetComponent<Rigidbody2D>(); float start = body.position.x, began = Time.time;
            Require(npc.Movement.MoveForConversation(new Vector2(target, body.position.y), facing), "Reachable movement target.");
            var used = new HashSet<int>(); var route = new List<Sample>();
            while (npc.Movement.IsMoving)
            {
                Require(Time.time - began < 12, "Route arrival timeout."); yield return null;
                if (npc.Visual.walking) used.Add(npc.Visual.NpcMotionFrame);
                var renderer = npc.Visual.GetComponentInChildren<SpriteRenderer>();
                var sample = new Sample { stage = facing < 0 ? "left" : "right", frame = Time.frameCount, time = Time.time,
                    x = npc.transform.position.x, physicalX = body.position.x, speed = npc.Movement.ActualSpeed, phase = npc.Visual.NpcMotionPhase,
                    sprite = renderer.sprite.name, scaleX = renderer.transform.localScale.x, scaleY = renderer.transform.localScale.y };
                route.Add(sample); samples.Add(sample);
                if (npc.Visual.walking) Require(Mathf.Sign(npc.Visual.transform.localScale.x) == facing, "Facing follows actual movement.");
            }
            Require(Mathf.Abs(body.position.x - target) < .07f && used.Count == 32, "Arrives with all 32 video frames.");
            var steady = route.Where(s => Mathf.Abs(s.physicalX - start) > .5f && Mathf.Abs(s.physicalX - target) > .5f).ToArray();
            Require(steady.Length > 10, "Steady physical samples.");
            float measured = Mathf.Abs(steady.Last().physicalX - steady.First().physicalX) / (steady.Last().time - steady.First().time);
            Require(Mathf.Abs(measured - npc.Visual.NpcWalkSpeed) < .04f, "Configured and actual speeds agree.");
            Require(route.All(s => Mathf.Abs(s.scaleX - 1) < .00001f && Mathf.Abs(s.scaleY - 1) < .00001f), "Fixed sprite scale while walking.");
        }
        static JToken[] Messages() => (JObject.Parse(S.State.memory.checkpoint)["records"]?["message"] as JArray ?? new JArray()).ToArray();
        static void SetupCapture()
        {
            camera = W.Follow.GetComponent<Camera>(); render = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); render.Create();
            // Keep the real Game view rendering: the current dialogue backdrop
            // captures it at end of frame before its entry animation can finish.
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        }
        static void Capture()
        {
            if (camera == null || render == null) return;
            var old = RenderTexture.active; var pixels = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
                RenderTexture.active = render; pixels.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); pixels.Apply();
                string file = "frame_" + frames.Count.ToString("D5") + ".png"; File.WriteAllBytes(Path.Combine(output, "frames", file), pixels.EncodeToPNG());
                frames.Add(new Frame { file = file, time = Time.time - recordingStart, stage = M?.Conversations.Stage ?? "" });
            }
            finally { RenderTexture.active = old; Object.Destroy(pixels); }
        }
        static void Complete(string error)
        {
            if (ending) return; ending = true; recording = false;
            if (error != null)
            {
                error += "\nPlayer actor: " + (M?.Conversations.PlayerActor != null ? M.Conversations.PlayerActor.npcId : "none") + "; stage: " + M?.Conversations.Stage;
                if (camera != null && render != null) Capture();
            }
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(new Report { passed = error == null, error = error ?? "", savePath = SaveRepository.PathOnDisk,
                unityVersion = Application.unityVersion, mode = M?.Client.Mode ?? "unknown", checks = checks.ToArray(), editorIssues = editorIssues.ToArray(), samples = samples.ToArray(), frames = frames.ToArray() }, true));
            SessionState.SetBool(Key + ".finished", true); SessionState.SetInt(Key + ".exit", error == null ? 0 : 1);
            Time.captureFramerate = oldCapture; Application.targetFrameRate = oldTarget; QualitySettings.vSyncCount = oldVSync;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (originalInput != null) { InputSystem.settings = originalInput; originalInput.hideFlags = originalFlags; }
            if (testInput != null) Object.Destroy(testInput);
            if (camera != null) camera.targetTexture = null;
            if (render != null) { render.Release(); Object.Destroy(render); }
            if (error == null) Debug.Log("[Zhengyi] Verification passed."); else Debug.LogError("[Zhengyi] " + error);
            EditorApplication.ExitPlaymode();
        }
        static IEnumerator Wait(Func<bool> ready, float seconds, string label) { double until = EditorApplication.timeSinceStartup + seconds; while (!ready()) { Require(EditorApplication.timeSinceStartup < until, label + " timed out."); yield return null; } }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static void Press(params UnityEngine.InputSystem.Key[] keys) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
        static string Arg(string name, string fallback) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
