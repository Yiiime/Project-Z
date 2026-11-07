#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Seabreeze.Editor
{
    // One integration walkthrough in the actual Play Mode world, not a separate test project.
    public sealed class RuntimeVerification : MonoBehaviour
    {
        [Serializable]
        public class Report
        {
            public string timestamp;
            public List<string> passed = new List<string>();
            public List<string> failed = new List<string>();
        }

        Report report = new Report();
        Keyboard keyboard;
        InputSettings originalInputSettings, verificationInputSettings;
        HideFlags originalSettingsFlags;
        GameSession S => GameSession.Instance;
        [MenuItem("Seabreeze/Verify current Play Mode")]
        public static void Run()
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("Enter Play Mode first.");
            GameSession.Instance.gameObject.AddComponent<RuntimeVerification>();
        }

        const string MapBatchKey = "Seabreeze.MapVerification.Active";
        const string MapBatchOutput = "Seabreeze.MapVerification.Output";
        bool mapOnly;
        Mouse mapMouse;

        // Uses the real scene/UI with isolated saves and synthetic keyboard/mouse input.
        public static void RunMapBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-zMapVerifyOutput");
            if (index < 0 || index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
                throw new ArgumentException("Supply an absolute -zMapVerifyOutput report path.");
            string output = args[index + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Environment.SetEnvironmentVariable("Z_MEMORY_SAVE_ROOT", Path.Combine(Path.GetTempPath(), "z-map-" + Guid.NewGuid().ToString("N")));
            SessionState.SetString(MapBatchOutput, output);
            SessionState.SetBool(MapBatchKey, true);
            SessionState.SetFloat(MapBatchKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 150f);
            InstallMapBatchHooks();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            if (!Application.isBatchMode)
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType);
                gameView.Show(); gameView.Focus();
            }
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void InstallMapBatchHooks()
        {
            if (!SessionState.GetBool(MapBatchKey, false)) return;
            EditorApplication.playModeStateChanged -= OnMapBatchPlayState;
            EditorApplication.playModeStateChanged += OnMapBatchPlayState;
            EditorApplication.update -= MapBatchWatchdog;
            EditorApplication.update += MapBatchWatchdog;
        }

        static void OnMapBatchPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            var runner = new GameObject("Isolated map verification").AddComponent<RuntimeVerification>();
            runner.mapOnly = true;
        }

        static void MapBatchWatchdog()
        {
            if (EditorApplication.timeSinceStartup <= SessionState.GetFloat(MapBatchKey + ".Deadline", 0)) return;
            FinishMapBatch(new Report { timestamp = DateTime.UtcNow.ToString("o"), failed = new List<string> { "Map verification timed out" } });
        }

        static void FinishMapBatch(Report result)
        {
            File.WriteAllText(SessionState.GetString(MapBatchOutput, ""), JsonUtility.ToJson(result, true));
            SessionState.SetBool(MapBatchKey, false);
            EditorApplication.update -= MapBatchWatchdog;
            EditorApplication.playModeStateChanged -= OnMapBatchPlayState;
            Debug.Log("MAP_VERIFICATION_COMPLETE: " + result.passed.Count + " passed, " + result.failed.Count + " failed");
            EditorApplication.Exit(result.failed.Count == 0 ? 0 : 1);
        }

        IEnumerator VerifyMapSafely()
        {
            var steps = VerifyMap();
            while (true)
            {
                object current;
                try { if (!steps.MoveNext()) break; current = steps.Current; }
                catch (Exception ex) { Check(false, ex.ToString()); break; }
                yield return current;
            }
            OnDestroy();
            FinishMapBatch(report);
        }

        void MoveMapMouse(Vector2 position, bool click = false)
        {
            mapMouse.MakeCurrent();
            InputSystem.QueueStateEvent(mapMouse, new MouseState { position = position, buttons = click ? (ushort)1 : (ushort)0 });
        }

        Vector2 MapScreenPoint(IslandMapView view, Vector2 pixel)
        {
            Rect r = view.ImageRect.rect;
            var local = new Vector2(r.xMin + r.width * pixel.x / 1672f, r.yMax - r.height * pixel.y / 941f);
            return RectTransformUtility.WorldToScreenPoint(null, view.ImageRect.TransformPoint(local));
        }

        IEnumerator VerifyMap()
        {
            report.timestamp = DateTime.UtcNow.ToString("o");
            originalInputSettings = InputSystem.settings;
            originalSettingsFlags = originalInputSettings.hideFlags;
            originalInputSettings.hideFlags = HideFlags.DontSave;
            verificationInputSettings = Instantiate(originalInputSettings);
            verificationInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            verificationInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = verificationInputSettings;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mapMouse = InputSystem.AddDevice<Mouse>();
            while (WorldDirector.Instance == null || WorldDirector.Instance.IsTransitioning) yield return null;
            yield return FinishDialogue();
            var ui = GameUI.Instance;
            var player = WorldDirector.Instance.Player.GetComponent<PlayerController>();
            player.Teleport(new Vector2(-8, .1f));
            yield return new WaitForSecondsRealtime(.2f);
            Press(Key.M);
            yield return null; yield return null;
            Check(ui.HasOverlay && ui.CurrentMapView != null, "M opens the map in the actual Street scene");
            yield return new WaitForSecondsRealtime(.2f);
            Check(ui.HasOverlay && ui.CurrentMapView != null, "Holding M does not repeatedly toggle the map");
            Press(); yield return null;
            Check(Time.timeScale == 0 && S.InputBlocked, "Map pauses world time and blocks world input");
            float x = player.transform.position.x;
            Press(Key.D); yield return new WaitForSecondsRealtime(.2f); Press(); yield return null;
            Check(Mathf.Abs(player.transform.position.x - x) < .02f, "Map blocks player movement");
            yield return Tap(Key.M);
            Check(!ui.HasOverlay && Time.timeScale == 1 && !S.InputBlocked, "M closes map and restores time and input");
            Press(Key.D); yield return new WaitForSecondsRealtime(.2f); Press(); yield return null;
            Check(player.transform.position.x > x + .05f, "Movement resumes after closing map");
            yield return Tap(Key.M); yield return Tap(Key.Escape);
            Check(!ui.HasOverlay && Time.timeScale == 1, "Escape closes map");
            yield return Tap(Key.M);
            var view = ui.CurrentMapView;
            if (view == null) { Check(false, "Map view exists for hover verification"); yield break; }
            var texture = Resources.Load<Texture2D>("Map/IslandMap");
            Check(texture != null && texture.width == 1672 && texture.height == 941, "Original dark map texture imports without resampling");
            Check(view.LocationCount == 22, "All 22 authored map labels have introductions");
            // Independent coordinates sampled from the supplied artwork, not hotspot definitions.
            string[] names = { "船长家", "咖啡店老板家", "花店老板家", "Z的家", "Z的父母家", "研究所", "山顶平台", "林间木屋", "游客中心", "街角花园", "花店", "咖啡店", "照相馆", "古树广场", "码头", "海边活动屋", "海景餐厅", "海风酒吧", "沙滩", "居住街", "主街", "滨海步道" };
            Vector2[] pixels = { new Vector2(341,294), new Vector2(505,294), new Vector2(670,294), new Vector2(822,294), new Vector2(978,294), new Vector2(1159,214), new Vector2(1415,214), new Vector2(1207,429), new Vector2(357,595), new Vector2(553,595), new Vector2(690,595), new Vector2(825,595), new Vector2(958,595), new Vector2(1125,605), new Vector2(169,628), new Vector2(466,757), new Vector2(735,757), new Vector2(1008,757), new Vector2(1280,776), new Vector2(638,400), new Vector2(744,630), new Vector2(730,801) };
            var viewport = (RectTransform)view.transform;
            Vector2 originalSize = viewport.sizeDelta;
            foreach (var size in new[] { originalSize, new Vector2(900, 800), new Vector2(1400, 550) })
            {
                viewport.sizeDelta = size;
                Canvas.ForceUpdateCanvases(); yield return null;
                Check(Mathf.Abs(view.ImageRect.rect.width / view.ImageRect.rect.height - 1672f / 941f) < .001f, "Map preserves aspect ratio in viewport " + size);
                for (int i = 0; i < names.Length; i++)
                {
                    MoveMapMouse(MapScreenPoint(view, pixels[i]));
                    yield return null; yield return null;
                    Check(view.TooltipVisible && view.HoveredLocation == names[i] && !string.IsNullOrWhiteSpace(view.HoveredDescription), "Mouse hover " + names[i] + " in viewport " + size);
                    Rect panel = ((RectTransform)view.TooltipRect.parent).rect;
                    var corners = new Vector3[4]; view.TooltipRect.GetWorldCorners(corners);
                    Check(corners.All(c => { Vector2 p = view.TooltipRect.parent.InverseTransformPoint(c); return p.x >= panel.xMin - 1 && p.x <= panel.xMax + 1 && p.y >= panel.yMin - 1 && p.y <= panel.yMax + 1; }), "Tooltip stays inside panel: " + names[i]);
                }
                MoveMapMouse(MapScreenPoint(view, new Vector2(30, 30)));
                yield return null; yield return null;
                Check(!view.TooltipVisible, "Ocean hover hides tooltip in viewport " + size);
                Vector2 padding = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(new Vector2(viewport.rect.xMin + 2, 0)));
                MoveMapMouse(padding); yield return null; yield return null;
                Check(!view.TooltipVisible, "Image padding does not trigger a place introduction");
            }
            viewport.sizeDelta = originalSize; Canvas.ForceUpdateCanvases(); yield return null;
            PlaceId place = S.CurrentPlace;
            Vector2 cafe = MapScreenPoint(view, new Vector2(825, 595));
            MoveMapMouse(cafe, true); yield return null; yield return null;
            MoveMapMouse(cafe); yield return null;
            Check(S.CurrentPlace == place && !WorldDirector.Instance.IsTransitioning && ui.HasOverlay, "Clicking an illustrated place does not teleport or dismiss map");
            foreach (Key key in new[] { Key.Tab, Key.J })
            {
                yield return Tap(key);
                Check(ui.HasOverlay && ui.CurrentMapView == null, "Other overlay opens without a stale map view: " + key);
                yield return Tap(Key.M);
                view = ui.CurrentMapView;
                MoveMapMouse(MapScreenPoint(view, new Vector2(30,30))); yield return null; yield return null;
                Check(view != null && !view.TooltipVisible, "Map reopens without stale hover after " + key);
            }
            MoveMapMouse(MapScreenPoint(view, new Vector2(1125, 605))); yield return null; yield return null;
            string screenshot = Path.Combine(Path.GetDirectoryName(SessionState.GetString(MapBatchOutput, "")), "map-hover.png");
            ScreenCapture.CaptureScreenshot(screenshot, 2);
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log("MAP_SCREENSHOT: " + screenshot + "; Screen " + Screen.width + "x" + Screen.height);
            yield return Tap(Key.Escape);
            Check(!ui.HasOverlay && !view.TooltipVisible && Time.timeScale == 1, "Closing map hides tooltip and restores world");
            yield return Tap(Key.Escape);
            Check(ui.HasOverlay && ui.CurrentMapView == null, "Pause overlay remains available");
            yield return Tap(Key.Escape);
            Check(!ui.HasOverlay && Time.timeScale == 1, "Pause closes normally");
        }

        void Check(bool ok, string name)
        {
            (ok ? report.passed : report.failed).Add(name);
            Debug.Log((ok ? "CHECK PASS: " : "CHECK FAIL: ") + name);
        }

        void Press(params Key[] keys)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        }

        IEnumerator Tap(Key key)
        {
            Press(key);
            yield return null;
            yield return null;
            Press();
            yield return null;
        }

        IEnumerator FinishDialogue(int choice = 0)
        {
            int budget = 60;
            while (S.Dialogue.IsOpen && budget-- > 0)
            {
                if (S.Dialogue.Choices.Count > 0)
                    S.Dialogue.Choose(Mathf.Min(choice, S.Dialogue.Choices.Count - 1));
                else
                    S.Dialogue.Advance();
                yield return null;
            }

            Check(budget > 0, "Dialogue terminates without loop");
        }

        IEnumerator Interact(string id, int choice = 0)
        {
            S.Story.Interact(id);
            Check(S.Dialogue.IsOpen, "Interaction opens dialogue: " + id);
            yield return FinishDialogue(choice);
        }

        IEnumerator Travel(PlaceId p)
        {
            GameUI.Instance.CloseOverlay();
            S.Travel(p);
            float limit = Time.realtimeSinceStartup + 10;
            do
            {
                yield return null;
            }
            while (WorldDirector.Instance.IsTransitioning && Time.realtimeSinceStartup < limit);
            Check(S.CurrentPlace == p && WorldDirector.Instance.Stage.place == p, "Scene transition: " + p);
        }

        IEnumerator Start()
        {
            if (mapOnly) { yield return VerifyMapSafely(); yield break; }
            report.timestamp = DateTime.UtcNow.ToString("o");
            // Route the synthetic keyboard to Play Mode even when tool calls return focus to Codex.
            // Clone rather than modify the project's actual Input System settings asset.
            originalInputSettings = InputSystem.settings;
            originalSettingsFlags = originalInputSettings.hideFlags;
            // InputManager destroys HideAndDontSave default settings when replaced. Keep this instance alive for restoration.
            originalInputSettings.hideFlags = HideFlags.DontSave;
            verificationInputSettings = Instantiate(originalInputSettings);
            verificationInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            verificationInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = verificationInputSettings;
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            while (WorldDirector.Instance.IsTransitioning)
                yield return null;
            S.NewGame();
            while (WorldDirector.Instance.IsTransitioning)
                yield return null;
            GameUI.Instance.CloseOverlay();
            yield return FinishDialogue();
            WorldDirector.Instance.Player.GetComponent<PlayerController>().Teleport(new Vector2(-8, .1f));
            yield return new WaitForSeconds(.3f);
            var world = WorldDirector.Instance;
            var player = world.Player.GetComponent<PlayerController>();
            yield return new WaitForSecondsRealtime(.2f);
            float start = world.Player.position.x;
            float moveElapsed = 0;
            while (moveElapsed < .65f)
            {
                Press(Key.D);
                yield return null;
                moveElapsed += Time.deltaTime;
            }

            Press();
            yield return new WaitForSeconds(.25f);
            Check(world.Player.position.x > start + 1.2f, "Input System D moves physical player (delta=" + (world.Player.position.x - start).ToString("F2") + ")");
            float stopped = world.Player.position.x;
            yield return new WaitForSecondsRealtime(.3f);
            Check(Mathf.Abs(world.Player.position.x - stopped) < .15f, "Movement decelerates to rest");
            float ground = world.Player.position.y, peak = ground;
            float jumpElapsed = 0;
            Press(Key.Space);
            while (jumpElapsed < .8f)
            {
                yield return null;
                if (jumpElapsed > .06f)
                    Press();
                jumpElapsed += Time.deltaTime;
                peak = Mathf.Max(peak, world.Player.position.y);
            }

            Check(peak > ground + .35f, "Buffered jump leaves ground (height=" + (peak - ground).ToString("F2") + ")");
            yield return new WaitForSeconds(.4f);
            Check(player.Grounded, "Ground cast detects landing");
            player.Teleport(new Vector2(12,.1f));
            float stroll = 0;
            while(stroll < .75f){Press(Key.D);yield return null;stroll += Time.deltaTime;}
            Press();yield return new WaitForSeconds(.25f);
            Check(world.Player.position.x > 14.3f,"Street bench does not block walking route");
            player.Teleport(new Vector2(12,.1f));
            yield return new WaitForSeconds(.3f);
            float benchStartY = world.Player.position.y;
            float benchJump = 0;
            while (benchJump < .65f)
            {
                if (benchJump < .06f) Press(Key.D,Key.Space); else Press(Key.D);
                yield return null;
                benchJump += Time.deltaTime;
            }
            Press();
            yield return new WaitForSeconds(.9f);
            Check(benchStartY < .2f && player.Grounded && world.Player.position.y > 1.1f,"Jump from pavement lands on the illustrated bench seat");
            foreach (var key in new[]{Key.M, Key.Tab, Key.J, Key.Escape})
            {
                yield return Tap(key);
                Check(GameUI.Instance.HasOverlay, "Keyboard opens overlay: " + key);
                float before = world.Player.position.x;
                Press(Key.D);
                yield return new WaitForSecondsRealtime(.15f);
                Press();
                Check(Mathf.Abs(world.Player.position.x - before) < .12f, "Overlay blocks movement: " + key);
                yield return Tap(Key.Escape);
                Check(!GameUI.Instance.HasOverlay, "Escape closes overlay: " + key);
            }

            // E interaction is exercised at the actual world marker, including proximity selection.
            player.Teleport(new Vector2(13.1f, .7f));
            yield return new WaitForSecondsRealtime(.15f);
            yield return Tap(Key.E);
            Check(S.Dialogue.IsOpen, "E activates nearest physical interaction marker");
            yield return FinishDialogue();
            for (int branch = 0; branch < 3; branch++)
            {
                S.NewGame();
                while (world.IsTransitioning)
                    yield return null;
                GameUI.Instance.CloseOverlay();
                yield return FinishDialogue();
                yield return Interact("postcard");
                yield return Travel(PlaceId.Cafe);
                yield return Interact("npc_aya");
                yield return Travel(PlaceId.Street);
                yield return Interact("npc_chen", branch);
                Check(S.HasFlag("chen_story"), "Story knowledge recorded branch " + branch);
                Check(S.State.flags.Count(x => x.StartsWith("choice_")) == 1, "Exactly one narrative approach branch " + branch);
                while (S.Clock.Period < TimePeriod.Evening)
                {
                    yield return Interact("bench", 0);
                }

                yield return Travel(PlaceId.Lighthouse);
                Check(S.Story.IsAvailable("npc_aya") == (branch == 2) && S.Story.IsAvailable("npc_chen") == (branch == 2), "Invitation changes physical NPC schedule " + branch);
                Check(world.Interactables.Count(i => (i.id=="npc_aya" || i.id=="npc_chen") && i.enabled) == (branch==2?2:0), "Invited NPC scene instances visible and interactive "+branch);
                yield return Interact("npc_mio");
                Check(S.HasFlag("evening_mio"), "Evening NPC resolves approach " + branch);
                if(branch==2){player.Teleport(new Vector2(1.0f,.1f));yield return new WaitForSeconds(.2f);UnityEngine.ScreenCapture.CaptureScreenshot("Docs/Verification/lighthouse-gathering.png");yield return new WaitForEndOfFrame();}
                yield return Interact("letterbox");
                Check(!string.IsNullOrEmpty(S.State.ending), "Ending reached branch " + branch);
                Check(S.State.journal.Count >= 4, "Journal contains story memory branch " + branch);
                S.Save();
                var loaded = SaveRepository.Load();
                Check(loaded.ending == S.State.ending && loaded.flags.Count == S.State.flags.Count, "Save roundtrip retains ending and flags " + branch);
                GameUI.Instance.CloseOverlay();
            }

            S.NewGame();
            while (world.IsTransitioning)
                yield return null;
            GameUI.Instance.CloseOverlay();
            yield return FinishDialogue();
            yield return Interact("postcard");
            yield return Travel(PlaceId.Cafe);
            yield return Interact("npc_aya");
            while (S.Clock.Period < TimePeriod.Evening)
            {
                yield return Interact("coffee", 0);
            }

            yield return Interact("guestbook", 2);
            Check(S.HasFlag("chen_story"), "Missed daytime NPC recoverable through guestbook");
            yield return Travel(PlaceId.Lighthouse);
            yield return Interact("npc_mio");
            yield return Interact("letterbox");
            Check(!string.IsNullOrEmpty(S.State.ending), "Late exploration recovery reaches ending");
            GameUI.Instance.CloseOverlay();
            S.NewGame();
            while (world.IsTransitioning)
                yield return null;
            GameUI.Instance.CloseOverlay();
            InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            Directory.CreateDirectory("Docs/Verification");
            File.WriteAllText("Docs/Verification/runtime-verification.json", JsonUtility.ToJson(report, true));
            Debug.Log("SEABREEZE_VERIFICATION_COMPLETE: " + report.passed.Count + " passed, " + report.failed.Count + " failed");
            Destroy(this);
        }

        void OnDestroy()
        {
            if (mapMouse != null) { InputSystem.RemoveDevice(mapMouse); mapMouse = null; }
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (originalInputSettings != null)
            {
                InputSystem.settings = originalInputSettings;
                originalInputSettings.hideFlags = originalSettingsFlags;
                originalInputSettings = null;
            }
            if (verificationInputSettings != null) { Destroy(verificationInputSettings); verificationInputSettings = null; }
        }
    }
}
#endif

