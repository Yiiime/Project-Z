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
    public static class CastMotionVerification
    {
        const string Folder = "Assets/Seabreeze/Resources/Art/CastMotion";
        const string Key = "Seabreeze.CastMotionVerification";
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
        [Serializable] sealed class Definition { public int version; public string characterId, idle; public float canvasHeightUnits, cycleSeconds, walkSpeed, strideLength; public float[] frameDurations, startDurations, stopDurations; public string[] walk, start, stop; public Asset[] assets; }
        [Serializable] sealed class CastIndex { public Entry[] characters; }
        [Serializable] sealed class Entry { public string id, name, folder; public int kind, frameCount; }
        static Entry[] Entries => JsonUtility.FromJson<CastIndex>(File.ReadAllText(Folder + "/cast-motion.json")).characters;
        static readonly List<float> frameTimes = new List<float>();
        static string captureStage = "";
        public static void DiagnoseCapture()
        {
            var texture = Resources.Load<Texture2D>("Art/CastMotion/z/z_walk_004");
            Debug.Log("CAST_TEXTURE " + texture.width + "x" + texture.height + " " + texture.format + " mips=" + texture.mipmapCount + " bytes=" + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture));
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
            foreach (string name in new[] { "GameView", "GameViewSizes", "GameViewSize", "GameViewSizeGroup", "GameViewSizeType", "GameViewSizeGroupType" })
            {
                var t = typeof(EditorWindow).Assembly.GetType("UnityEditor." + name);
                if (t == null) continue;
                Debug.Log("CAST_VIEW " + name + " " + (t.IsEnum ? string.Join(",", Enum.GetNames(t)) : string.Join(";", t.GetConstructors(flags).Select(x => x.ToString()))));
                foreach (var p in t.GetProperties(flags).Where(x => new[] { "selectedSizeIndex", "currentGroupType", "instance", "targetSize" }.Contains(x.Name))) Debug.Log("CAST_PROPERTY " + name + " " + p);
                foreach (var m in t.GetMethods(flags).Where(x => new[] { "GetGroup", "AddCustomSize", "RemoveCustomSize", "GetTotalCount", "GetBuiltinCount", "GetCustomCount" }.Contains(x.Name))) Debug.Log("CAST_METHOD " + name + " " + m);
            }
        }
        public static void ClockOnly()
        {
            checks.Clear(); VerifyClocks();
            string path = Arg("-castClockOutput", "Builds/CastMotionReview/Verification/clocks.json");
            File.WriteAllText(path, JsonUtility.ToJson(new Report { passed = true, mode = "clock-only", checks = checks.ToArray(), unityVersion = Application.unityVersion }, true));
            Debug.Log("[Cast] Native animation clocks passed.");
        }
        static void DestroyProbe(GameObject probe)
        { if (Application.isPlaying) Object.Destroy(probe); else Object.DestroyImmediate(probe); }
        [Serializable] sealed class Reviewed { public Asset[] frames; }
        [Serializable] sealed class Sample { public string stage, sprite, actor; public float time, x, physicalX, speed, phase, scaleX, scaleY; public int frame; }
        [Serializable] sealed class Frame { public string file, stage; public float time, realTime; public int gameFrame; }
        [Serializable] sealed class Report { public bool passed; public string error, savePath, unityVersion, mode; public string[] checks, editorIssues; public Sample[] samples; public Frame[] frames; public float[] frameTimes; public long graphicsMemoryBytes; }

        static CastMotionVerification()
        {
            if (SessionState.GetBool(Key, false)) SaveRepository.VerificationDirectory = SessionState.GetString(Key + ".saves", "");
            EditorApplication.playModeStateChanged += Changed;
            string resume = Path.Combine(Arg("-castOutput", ""), "resume-verifier");
            if (File.Exists(resume) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(resume);
                EditorApplication.delayCall += () => { RestoreVerificationView(); Run(); };
            }
        }
        [MenuItem("Seabreeze/Import cast video motion")]
        public static void Import()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Import from Edit mode.");
            var entries = Entries; Require(entries.Length == 9 && entries.All(e => e.id != "chenchen"), "Eight existing NPCs and one player, no deferred NPC.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            int total = 0;
            foreach (var entry in entries)
            {
                var def = JsonUtility.FromJson<Definition>(File.ReadAllText(entry.folder + "/motion.json"));
                var source = JObject.Parse(File.ReadAllText(entry.folder + "/source~/video-extraction.json"));
                var hashes = source["states"].Children<JProperty>().SelectMany(p => p.Value["frames"]).ToDictionary(f => Path.GetFileName((string)f["file"]), f => (string)f["sha256"]);
                Require(def.characterId == entry.id && def.assets.Length == entry.frameCount && def.version == 2, "Manifest identity and frame count: " + entry.id);
                foreach (var asset in def.assets)
                {
                    string path = entry.folder + "/" + asset.file;
                    using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
                        Require(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == asset.sha256 && hashes[asset.file] == asset.sha256, "Exact extracted bytes: " + path);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    Require(importer.DoesSourceTextureHaveAlpha(), "RGBA source: " + path);
                    var platform = importer.GetPlatformTextureSettings("Standalone");
                    if (importer.textureType != TextureImporterType.Sprite || Mathf.Abs(importer.spritePixelsPerUnit - asset.pixelsPerUnit) > .001f || !platform.overridden || platform.format != TextureImporterFormat.BC7 || importer.mipmapEnabled || importer.filterMode != FilterMode.Bilinear)
                    { ConfigureTexture(importer, path); importer.SaveAndReimport(); }
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    Require(sprite != null && sprite.texture.width == asset.width && sprite.texture.height == asset.height, "Native pixel dimensions: " + path);
                    Require(sprite.texture.format == TextureFormat.BC7, "Actual texture remains compressed, without RGBA fallback: " + path);
                    Require((sprite.pivot - new Vector2(asset.width * asset.pivot.x, asset.height * asset.pivot.y)).sqrMagnitude < .001f, "Shared pivot: " + path);
                    total++;
                }
                Require(Mathf.Abs(def.frameDurations.Sum() - def.cycleSeconds) < .0001f, "Native loop duration.");
            }
            Debug.Log("[Cast] Imported " + total + " native RGBA frames, BC7 without downscaling.");
        }
        public static void ConfigureTexture(TextureImporter importer, string path)
        {
            if (!path.StartsWith(Folder + "/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            string directory = Path.GetDirectoryName(path), manifest = Path.Combine(directory, "motion.json");
            if (!File.Exists(manifest)) return;
            var definition = JsonUtility.FromJson<Definition>(File.ReadAllText(manifest));
            var a = definition.assets.FirstOrDefault(x => x.file == Path.GetFileName(path)); if (a == null) return;
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = a.pixelsPerUnit; importer.alphaIsTransparency = true; importer.sRGBTexture = true;
            // Unity expands these NPOT sprites to RGBA32 when mipmaps are enabled.
            // Preserve native dimensions and verify the actual BC7 format above.
            importer.mipmapEnabled = false;
            importer.isReadable = false; importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Compressed;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = a.pivot; settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.BC7, textureCompression = TextureImporterCompression.Compressed, compressionQuality = 50 });
        }
        [MenuItem("Seabreeze/Verify cast video motion in game")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run from Edit mode.");
            Require(!Application.isBatchMode, "The current dialogue backdrop requires a visible Game view. Run without -batchmode; Import supports batch mode.");
            Require(!string.IsNullOrEmpty(Arg("-memoryService", "")), "Provide an explicitly isolated simulated Memory Lab URL with -memoryService.");
            output = Path.GetFullPath(Arg("-castOutput", Path.Combine(Path.GetTempPath(), "zhengyi-" + Guid.NewGuid().ToString("N"))));
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            SessionState.SetString(Key + ".output", output); SessionState.SetString(Key + ".saves", Path.Combine(output, "saves"));
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".finished", false);
            SaveRepository.VerificationDirectory = Path.Combine(output, "saves");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity", OpenSceneMode.Single);
            if (!Application.isBatchMode)
            {
                var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                view.Show(); view.Focus();
                ConfigureVerificationView();
            }
            EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                ConfigureVerificationView();
                output = SessionState.GetString(Key + ".output", ""); ending = recording = false; failure = null; lastFrame = -1;
                samples.Clear(); frames.Clear(); checks.Clear(); editorIssues.Clear(); frameTimes.Clear(); steps.Clear(); deadline = EditorApplication.timeSinceStartup + 1200;
                oldTarget = Application.targetFrameRate; oldCapture = Time.captureFramerate; oldVSync = QualitySettings.vSyncCount;
                Time.captureFramerate = 60; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
                Application.logMessageReceived += Log; EditorApplication.update += Tick; steps.Push(Verify());
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(Key + ".finished", false)) Complete("Play mode interrupted.");
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SaveRepository.VerificationDirectory = null; SessionState.SetBool(Key, false);
                if (Application.isBatchMode || Arg("-castExit", "") == "1") EditorApplication.Exit(SessionState.GetInt(Key + ".exit", 1));
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
            yield return Wait(() => Screen.width >= 1000 && Screen.height >= 600 && Screen.width < Screen.height * 3, 30, "Usable maximized verification viewport");
            yield return Wait(() => M != null && W?.Stage != null && !W.IsTransitioning && M.Spawner.Find("zhengyi")?.Movement?.IsReady == true, 180, "Cast scene startup");
            Require(SaveRepository.PathOnDisk.StartsWith(Path.Combine(output, "saves") + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Isolated save root.");
            Require(string.Join(",", SevenCharacterCatalog.Actors.Select(a => a.id)) == "z,liyuan,liting,axing,zhengyi,xiaoxiaoqing,linxu,zhou_bobo", "Current main roster preserved.");
            Require(SevenCharacterCatalog.Actors.All(a => a.motion == "Art/CastMotion/" + a.id), "No shared-appearance replacement or deferred actor.");
            M.Conversations.CancelAll("Cast verification setup"); M.Conversations.enabled = false;
            M.GetComponent<MemoryActionExecutor>().enabled = false;
            foreach (var a in M.Spawner.Actors) { a.Movement.SimulateDecisions = false; a.Movement.Interrupt(); }
            SetupCapture();
            VerifyClocks();
            originalInput = InputSystem.settings; originalFlags = originalInput.hideFlags; originalInput.hideFlags = HideFlags.DontSave;
            testInput = Object.Instantiate(originalInput); testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testInput; keyboard = InputSystem.AddDevice<Keyboard>();
            var player = W.Player.GetComponent<PlayerController>(); var playerVisual = W.Player.GetComponentInChildren<CharacterVisual>();
            player.Teleport(new Vector2(-12, .1f)); W.Follow.Snap(); yield return Frames(15);
            float low = player.transform.position.x, high = low; float bottom = player.transform.position.y, top = bottom;
            var playerFrames = new HashSet<string>(); captureStage = "player"; recordingStart = Time.time; nextCapture = 0; recording = true;
            for (int i = 0; i < 360; i++)
            {
                if (i < 110) Press(UnityEngine.InputSystem.Key.D); else if (i < 135) Press(); else if (i < 245) Press(UnityEngine.InputSystem.Key.A);
                else if (i < 275) Press(UnityEngine.InputSystem.Key.D); else if (i < 300) Press(UnityEngine.InputSystem.Key.A); else if (i < 315) Press(UnityEngine.InputSystem.Key.Space); else Press();
                yield return null;
                low = Mathf.Min(low, player.transform.position.x); high = Mathf.Max(high, player.transform.position.x);
                bottom = Mathf.Min(bottom, player.transform.position.y); top = Mathf.Max(top, player.transform.position.y);
                if (playerVisual.Pose == "Walk") playerFrames.Add(playerVisual.GetComponentInChildren<SpriteRenderer>().sprite.name);
                Require(playerVisual.GetComponentInChildren<SpriteRenderer>().transform.localScale == Vector3.one, "Player keeps native uniform scale.");
            }
            Press(); recording = false; yield return Frames(15);
            Require(playerVisual.UsesPlayerStateMotion && playerVisual.Pose == "Idle" && high - low > 4, "Player actually walks both ways and settles.");
            Require(player.speed == 4.2f && player.acceleration == 28 && player.deceleration == 36 && top - bottom < .15f, "Original movement and removed jump retained.");
            Require(playerFrames.Count >= 28, "Real player input exercises the high-rate video loop.");
            checks.Add("Player real input: preserved speed/acceleration/collision/no-jump, stable canvas, turn, start and prompt stop");
            foreach (var place in new[] { PlaceId.Street, PlaceId.Cafe, PlaceId.Lighthouse })
            {
                if (W.Stage.place != place) { S.Travel(place); yield return Wait(() => !W.IsTransitioning && W.Stage.place == place, 30, "Scene travel"); }
                var expected = SevenCharacterCatalog.Actors.Where(a => a.Place == place).Select(a => a.id).ToArray();
                yield return Wait(() => expected.All(id => M.Spawner.Find(id)?.Movement?.IsReady == true), 180, "All current scene actors load native sprites");
                foreach (var a in M.Spawner.Actors) { a.Movement.SimulateDecisions = false; a.Movement.SetInteractionHold(true); }
                foreach (string id in expected)
                {
                    var npc = M.Spawner.Find(id); var def = DefinitionFor(id); var body = npc.GetComponent<Rigidbody2D>(); var origin = body.position;
                    Require(npc.Visual.UsesNpcStateMotion && npc.motionResource == "Art/CastMotion/" + id, "Correct actor art: " + id);
                    foreach (var a in M.Spawner.Actors) if (a != npc) a.GetComponent<Collider2D>().enabled = false;
                    npc.Movement.SetInteractionHold(false); int facing = origin.x > 0 ? -1 : 1;
                    player = W.Player.GetComponent<PlayerController>(); player.Teleport(origin + Vector2.right * facing * 2); W.Follow.Snap();
                    captureStage = id; bool film = id == "zhengyi" || id == "z" || id == "liyuan"; recording = film;
                    yield return Move(npc, origin.x + facing * 5.5f, facing, def.walk.Length);
                    recording = false; CaptureNamed(id + "-idle.png");
                    yield return Move(npc, body.position.x - facing * 1.5f, -facing, 0);
                    Require(npc.Visual.Pose == "Idle", "Reverse route finishes grounded.");
                    npc.Movement.SetInteractionHold(true); body.position = origin; npc.transform.position = origin;
                    foreach (var a in M.Spawner.Actors) a.GetComponent<Collider2D>().enabled = true;
                    checks.Add(id + ": all loop frames during real long route, short reverse, planted idle and fixed scale");
                }
            }
            S.Travel(PlaceId.Street);
            yield return Wait(() => !W.IsTransitioning && M.Spawner.Find("zhengyi")?.Movement?.IsReady == true && M.Spawner.Find("linxu")?.Movement?.IsReady == true, 180, "Return to street");
            foreach (var a in M.Spawner.Actors) { a.Movement.SimulateDecisions = false; a.Movement.Interrupt(); }
            var target = M.Spawner.Find("zhengyi"); var partner = M.Spawner.Find("linxu");
            foreach (var a in M.Spawner.Actors) a.Movement.SetInteractionHold(a != target && a != partner);
            yield return Wait(() => M.Client.CanGenerate, 30, "Simulated service connected"); Require(M.Client.Mode == "simulated", "Tests must use simulated model.");
            M.Conversations.enabled = true;
            yield return Wait(() => M.Conversations.Stage == "交谈" && (M.Conversations.First == target || M.Conversations.Second == target), 80, "NPCs choose and approach each other");
            var observer = M.Spawner.Find("xiaoxiaoqing"); observer.Movement.SetInteractionHold(false); observer.Movement.SimulateDecisions = true;
            player = W.Player.GetComponent<PlayerController>(); player.Teleport(target.transform.position + Vector3.left * 2.5f); W.Follow.Snap();
            yield return Wait(() => !string.IsNullOrEmpty(M.View.VisibleSpeech), 25, "NPC speech visible");
            Require(target.Visual.Pose == "Idle" && partner.Visual.Pose == "Idle" && !S.InputBlocked, "Talk participants stand while player remains free."); CaptureNamed("npc-chat.png");
            int completed = M.Conversations.CompletedConversations; low = high = observer.transform.position.x;
            double until = EditorApplication.timeSinceStartup + 95;
            while (M.Conversations.CompletedConversations == completed)
            { Require(EditorApplication.timeSinceStartup < until, "NPC conversation ends"); low = Mathf.Min(low, observer.transform.position.x); high = Mathf.Max(high, observer.transform.position.x); yield return null; }
            Require(high - low > .08f, "Unrelated NPC continues moving.");
            yield return Wait(() => M.Client.Synchronized, 25, "Speech receipts synchronize");
            Require(new[] { "zhengyi", "linxu" }.All(id => Messages().Any(m => (string)m["speakerId"] == id)), "Both actors retain actual lines in current history.");
            checks.Add("Current A2A and speech UI: only participants held, another NPC moves, independent speech receipts retained (simulated model)");
            M.Conversations.CancelAll("Player E setup"); M.Conversations.enabled = false;
            foreach (var a in M.Spawner.Actors) { a.Movement.SetInteractionHold(false); a.Movement.SimulateDecisions = false; a.Movement.Interrupt(); }
            playerVisual = W.Player.GetComponentInChildren<CharacterVisual>(); player.Teleport(target.transform.position + Vector3.right * .25f); W.Follow.Snap(); yield return Frames(5);
            Require(player.GetComponent<PlayerInteraction>().Current?.id == "zhengyi", "E targets the current actor.");
            Press(UnityEngine.InputSystem.Key.E); yield return null; Press();
            yield return Wait(() => M.Conversations.PlayerActor == target && M.View.PlayerInputReady && M.Client.CanGenerate, 30, "Current dialogue UI opens");
            Require(target.Visual.Pose == "Idle" && playerVisual.Pose == "Idle", "Interaction immediately grounds both visuals.");
            M.Conversations.SendPlayer("你今天怎么样？");
            yield return Wait(() => M.LastReply?.actorId == "zhengyi" && !M.Conversations.Sending && M.Client.Synchronized, 30, "Current model reply");
            string reply = M.LastReply.result.dialogue;
            yield return Wait(() => M.View.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text == reply && t.maxVisibleCharacters >= t.textInfo.characterCount), 15, "Reply fully visible"); CaptureNamed("player-chat.png");
            M.Conversations.CancelAll("Save/load"); yield return Wait(() => !M.PlayerChatOpen && M.Client.Synchronized, 20, "Chat closes");
            var saved = Messages().Select(m => (string)m["id"]).ToArray(); Require(saved.Length > 0 && S.Save(), "Save isolated current-world history."); S.LoadSavedGame();
            yield return Wait(() => !W.IsTransitioning && M.Client.Synchronized && M.Spawner.Find("zhengyi")?.Movement?.IsReady == true, 180, "Reload native sprites");
            Require(saved.All(id => Messages().Any(m => (string)m["id"] == id)), "Read restores the same memory history IDs.");
            Require(M.Spawner.Find("zhengyi").Visual.Pose == "Idle", "No stale animation after reload.");
            checks.Add("E uses latest dialogue UI; saved history survives reload and transient animation resets");
            // Sampling without screenshot capture avoids counting encode/readback cost.
            Time.captureFramerate = 0; recording = false;
            foreach (var a in M.Spawner.Actors) { a.Movement.SetInteractionHold(false); a.Movement.SimulateDecisions = true; }
            yield return Frames(120);
            for (int i = 0; i < 180; i++) { frameTimes.Add(Time.unscaledDeltaTime); yield return null; }
            checks.Add("Street multi-character frame times sampled with screenshot capture disabled");
        }
        static Definition DefinitionFor(string id) => JsonUtility.FromJson<Definition>(File.ReadAllText(Folder + "/" + id + "/motion.json"));
        static float DistanceAt(float t, float speed, float start, float walk, float stop)
        { if (t < start) return speed * t * t / (2 * start); float d = speed * start * .5f; t -= start; if (t < walk * 2) return d + speed * t; d += speed * walk * 2; t = Mathf.Clamp(t - walk * 2, 0, stop); return d + speed * (t - t * t / (2 * stop)); }
        static void VerifyClocks()
        {
            foreach (var e in Entries.Where(e => e.id != "player"))
            {
                var def = DefinitionFor(e.id); float start = def.startDurations.Sum(), stop = def.stopDurations.Sum(), total = start + def.cycleSeconds * 2 + stop;
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    var root = new GameObject("Isolated cast clock " + e.id); root.SetActive(false);
                    try
                    {
                        var v = root.AddComponent<CharacterVisual>(); v.Build(e.kind, "Art/CastMotion/" + e.id); Require(v.UsesNpcStateMotion, "Four-state binding " + e.id);
                        v.BeginNpcMotionRoute(DistanceAt(total, v.NpcWalkSpeed, start, def.cycleSeconds, stop)); float previous = 0; var seen = new HashSet<int>();
                        for (int i = 1; i <= Mathf.CeilToInt(total * fps); i++)
                        { float d = DistanceAt(Mathf.Min(total, i / (float)fps), v.NpcWalkSpeed, start, def.cycleSeconds, stop); v.AnimateNpcDisplacement(d - previous, (d - previous) * fps, 1f / fps); previous = d; if (v.Pose == "Walk") seen.Add(v.NpcMotionFrame); }
                        Require(seen.Count == def.walk.Length, "Every native loop pose at " + fps + " FPS: " + e.id); v.CompleteNpcMotionRoute();
                        for (int i = 0; i < fps * 3; i++) v.AnimateNpcDisplacement(0, 0, 1f / fps);
                        Require(v.Pose == "Idle" && !v.HasNpcMotionRoute, "Settled route " + e.id);
                        v.BeginNpcMotionRoute(5); v.AnimateNpcDisplacement(.5f, 1, .1f); v.StopNpcMotionImmediately(); Require(v.Pose == "Idle" && !v.walking, "Immediate interruption " + e.id);
                    }
                    finally { DestroyProbe(root); }
                }
            }
            var actor = new GameObject("Isolated player video clock"); actor.SetActive(false);
            try
            {
                var v = actor.AddComponent<CharacterVisual>(); v.Build(0); var d = DefinitionFor("player"); Require(v.UsesPlayerStateMotion, "Player v2 active");
                var seen = new HashSet<int>(); v.SetPlayerMotionIntent(1);
                for (int i = 0; i < 360; i++) { v.Animate(4.2f, true, 0, 1f / 60); if (v.Pose == "Walk") seen.Add(v.PlayerMotionFrame); }
                Require(seen.Count == d.walk.Length, "All player walk frames reachable at original speed."); v.SetPlayerMotionIntent(0);
                for (int i = 0; i < 15; i++) v.Animate(0, true, 0, 1f / 60);
                Require(v.Pose == "Idle", "Prompt player stop.");
            }
            finally { DestroyProbe(actor); }
            checks.Add("Eight NPC profiles at 30/60/120 FPS plus player clock, complete native cycles, same-source idle and immediate cancellation");
        }
        static IEnumerator Move(NpcPresence npc, float target, int facing, int loopFrames)
        {
            var body = npc.GetComponent<Rigidbody2D>(); var before = body.position; float began = Time.time;
            Require(npc.Movement.MoveForConversation(new Vector2(target, body.position.y), facing), "Supported visual route " + npc.npcId);
            var seen = new HashSet<int>(); var poses = new HashSet<string>();
            while (npc.Movement.IsMoving || npc.Visual.HasNpcMotionRoute)
            {
                Require(Time.time - began < 25, "Finite route " + npc.npcId); yield return null; poses.Add(npc.Visual.Pose);
                if (npc.Visual.Pose == "Walk") seen.Add(npc.Visual.NpcMotionFrame);
                var renderer = npc.Visual.GetComponentInChildren<SpriteRenderer>();
                Require(renderer.transform.localScale == Vector3.one && renderer.transform.localPosition == Vector3.zero, "Shared canvas " + npc.npcId);
                if (npc.Movement.IsMoving && npc.Visual.walking) Require(Mathf.Sign(npc.Visual.transform.localScale.x) == facing, "Facing " + npc.npcId);
                samples.Add(new Sample { actor = npc.npcId, stage = npc.Visual.Pose, sprite = renderer.sprite.name, frame = Time.frameCount, time = Time.time, x = npc.transform.position.x, physicalX = body.position.x, speed = npc.Movement.ActualSpeed, phase = npc.Visual.NpcMotionPhase, scaleX = renderer.transform.localScale.x, scaleY = renderer.transform.localScale.y });
            }
            Require(Mathf.Abs(body.position.x - target) < .07f && npc.Visual.Pose == "Idle", "Arrived grounded " + npc.npcId);
            Require(poses.Contains("Start") && poses.Contains("Stop") && (loopFrames == 0 || seen.Count == loopFrames), "Actual source poses " + npc.npcId);
        }
        static void CaptureNamed(string name)
        { captureStage = name; Capture(); File.Copy(Path.Combine(output, "frames", frames.Last().file), Path.Combine(output, name), true); }
        static JToken[] Messages() => (JObject.Parse(S.State.memory.checkpoint)["records"]?["message"] as JArray ?? new JArray()).ToArray();
        static object VerificationSizeGroup()
        {
            var assembly = typeof(EditorWindow).Assembly;
            var t = assembly.GetType("UnityEditor.GameViewSizes");
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
            var instance = t.GetProperty("instance", flags).GetValue(null);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
            return t.GetMethod("GetGroup").Invoke(instance, new[] { Enum.Parse(groupType, "Standalone") });
        }
        static void ConfigureVerificationView()
        {
            if (Application.isBatchMode) return;
            var assembly = typeof(EditorWindow).Assembly; var type = assembly.GetType("UnityEditor.GameView");
            var view = EditorWindow.GetWindow(type); var property = type.GetProperty("selectedSizeIndex");
            var group = VerificationSizeGroup(); var gt = group.GetType();
            int count = (int)gt.GetMethod("GetCustomCount").Invoke(group, null);
            if (!SessionState.GetBool(Key + ".viewOverride", false))
            {
                SessionState.SetInt(Key + ".viewIndex", (int)property.GetValue(view));
                SessionState.SetInt(Key + ".viewCustom", count); SessionState.SetBool(Key + ".viewMax", view.maximized);
                SessionState.SetBool(Key + ".viewOverride", true);
            }
            int custom = SessionState.GetInt(Key + ".viewCustom", count);
            if (count <= custom)
            {
                var sizeType = assembly.GetType("UnityEditor.GameViewSize"); var kind = assembly.GetType("UnityEditor.GameViewSizeType");
                var size = Activator.CreateInstance(sizeType, new[] { Enum.Parse(kind, "FixedResolution"), (object)1600, 900, "Cast verification (temporary)" });
                gt.GetMethod("AddCustomSize").Invoke(group, new[] { size });
            }
            int builtins = (int)gt.GetMethod("GetBuiltinCount").Invoke(group, null);
            property.SetValue(view, builtins + custom); view.maximized = true; view.Show(); view.Focus(); view.Repaint();
            Debug.Log("CAST_VIEWPORT selected=" + property.GetValue(view) + " target=" + type.GetProperty("targetSize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(view) + " rect=" + view.position + " screen=" + Screen.width + "x" + Screen.height);
        }
        static void RestoreVerificationView()
        {
            if (!SessionState.GetBool(Key + ".viewOverride", false)) return;
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"); var view = EditorWindow.GetWindow(type);
            type.GetProperty("selectedSizeIndex").SetValue(view, SessionState.GetInt(Key + ".viewIndex", 0));
            view.maximized = SessionState.GetBool(Key + ".viewMax", false);
            var group = VerificationSizeGroup(); var gt = group.GetType(); int custom = SessionState.GetInt(Key + ".viewCustom", -1);
            if (custom >= 0 && custom < (int)gt.GetMethod("GetCustomCount").Invoke(group, null)) gt.GetMethod("RemoveCustomSize").Invoke(group, new object[] { custom });
            SessionState.SetBool(Key + ".viewOverride", false);
        }
        static void SetupCapture()
        {
            camera = W.Follow.GetComponent<Camera>();
            // Match the actual Game view so screen-space UI is not cropped by
            // an offscreen target with a different aspect ratio.
            render = new RenderTexture(Mathf.Max(2, Screen.width), Mathf.Max(2, Screen.height), 24, RenderTextureFormat.ARGB32); render.Create();
            Debug.Log("CAST_CAPTURE screen=" + Screen.width + "x" + Screen.height + " camera=" + camera.pixelWidth + "x" + camera.pixelHeight + " aspect=" + camera.aspect);
            // Keep the real Game view rendering: the current dialogue backdrop
            // captures it at end of frame before its entry animation can finish.
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        }
        static void Capture()
        {
            if (render == null || W?.Follow == null) return; camera = W.Follow.GetComponent<Camera>();
            var old = RenderTexture.active; var target = camera.targetTexture;
            var pixels = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render; Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
                RenderTexture.active = render; pixels.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); pixels.Apply();
                string file = "frame_" + frames.Count.ToString("D5") + ".png"; File.WriteAllBytes(Path.Combine(output, "frames", file), pixels.EncodeToPNG());
                frames.Add(new Frame { file = file, time = Time.time, realTime = Time.unscaledTime, gameFrame = Time.frameCount, stage = captureStage });
            }
            finally { camera.targetTexture = target; Canvas.ForceUpdateCanvases(); RenderTexture.active = old; Object.Destroy(pixels); }
        }
        static void Complete(string error)
        {
            if (ending) return; ending = true; recording = false;
            if (error != null)
            {
                error += "\nViewport: " + Screen.width + "x" + Screen.height;
                error += "\nPlayer actor: " + (M?.Conversations.PlayerActor != null ? M.Conversations.PlayerActor.npcId : "none") + "; stage: " + M?.Conversations.Stage;
                if (camera != null && render != null) Capture();
            }
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(new Report { passed = error == null, error = error ?? "", savePath = SaveRepository.PathOnDisk,
                unityVersion = Application.unityVersion, mode = M?.Client.Mode ?? "unknown", checks = checks.ToArray(), editorIssues = editorIssues.ToArray(), samples = samples.ToArray(), frames = frames.ToArray(), frameTimes = frameTimes.ToArray(), graphicsMemoryBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() }, true));
            SessionState.SetBool(Key + ".finished", true); SessionState.SetInt(Key + ".exit", error == null ? 0 : 1);
            Time.captureFramerate = oldCapture; Application.targetFrameRate = oldTarget; QualitySettings.vSyncCount = oldVSync;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (originalInput != null) { InputSystem.settings = originalInput; originalInput.hideFlags = originalFlags; }
            if (testInput != null) Object.Destroy(testInput);
            RestoreVerificationView();
            if (camera != null) camera.targetTexture = null;
            if (render != null) { render.Release(); Object.Destroy(render); }
            if (error == null) Debug.Log("[CastMotion] Verification passed."); else Debug.LogError("[CastMotion] " + error);
            EditorApplication.ExitPlaymode();
        }
        static IEnumerator Wait(Func<bool> ready, float seconds, string label) { double until = EditorApplication.timeSinceStartup + seconds; while (!ready()) { Require(EditorApplication.timeSinceStartup < until, label + " timed out."); yield return null; } }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static void Press(params UnityEngine.InputSystem.Key[] keys) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
        static string Arg(string name, string fallback) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

namespace Seabreeze.Editor
{
    public sealed class CastMotionTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture() => CastMotionVerification.ConfigureTexture((TextureImporter)assetImporter, assetPath);
    }
}
