using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Seabreeze.Editor
{
    // Exercises the actual scene, movement, E interaction, story callbacks and save
    // lifecycle. All progress belongs to a new temporary save, never the player.
    [InitializeOnLoad]
    public static class FirstDayGatheringVerification
    {
        const string Key = "Seabreeze.FirstDayGatheringVerification";
        static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
        static readonly List<string> checks = new List<string>();
        static readonly List<string> editorIssues = new List<string>();
        static readonly List<Sample> samples = new List<Sample>();
        static readonly string[][] groups = {
            new[] { "z", "zhengyi", "liyuan" },
            new[] { "xiaoxiaoqing", "linxu", "liting" },
            new[] { "zhou_bobo", "axing" }
        };
        static string output, failure;
        static int lastFrame, oldCaptureRate;
        static bool finishing;
        static double deadline;
        static double lastGameFrameAt, nextProgress;
        static EditorWindow gameView;
        static Keyboard keyboard;
        static InputSettings originalInput, testInput;
        static GameSession S => GameSession.Instance;
        static WorldDirector W => WorldDirector.Instance;
        static MemoryGameController M => S != null ? S.Memory : null;
        [Serializable] sealed class Sample { public float time, x; public string actor, pose, partner; }
        [Serializable] sealed class Report { public bool complete, passed; public string error, unityVersion, savePath; public string[] checks, editorIssues; public Sample[] samples; }

        static FirstDayGatheringVerification()
        {
            if (SessionState.GetBool(Key, false)) SaveRepository.VerificationDirectory = SessionState.GetString(Key + ".saves", "");
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.quitting += () => {
                if (SessionState.GetBool(Key, false) && !SessionState.GetBool(Key + ".finished", false))
                    WriteReport(false, "Editor exited before the verification completed.");
            };
        }

        [MenuItem("Seabreeze/First Day/Verify square gathering")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Run in Edit mode after compilation.");
            Require(!Application.isBatchMode, "Use a visible Game view for the existing story backdrop capture.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty, "Save scene edits before verification.");
            output = Path.GetFullPath(Arg("-firstDayGatheringOutput", Path.Combine(Path.GetTempPath(), "z-gathering-" + Guid.NewGuid().ToString("N"))));
            Directory.CreateDirectory(output);
            string saves = Path.Combine(output, "saves-" + Guid.NewGuid().ToString("N"));
            SessionState.SetString(Key + ".output", output);
            SessionState.SetString(Key + ".saves", saves);
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".finished", false);
            SaveRepository.VerificationDirectory = saves;
            Require(SaveRepository.Write(new SaveData { started = true, day = 1, period = (int)TimePeriod.Evening,
                place = nameof(PlaceId.Square), playerX = -9,
                firstDay = new FirstDayState { phase = FirstDayPhase.MeetIslanders, invitationAccepted = true },
                memory = new MemorySaveState() }), "Create isolated first-day checkpoint.");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Square.unity", OpenSceneMode.Single);
            gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.autoRepaintOnSceneChange = true; gameView.Show(); gameView.Focus();
            EditorApplication.EnterPlaymode();
        }

        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                output = SessionState.GetString(Key + ".output", "");
                finishing = false; failure = null; lastFrame = -1;
                checks.Clear(); editorIssues.Clear(); samples.Clear(); steps.Clear();
                deadline = EditorApplication.timeSinceStartup + 900;
                lastGameFrameAt = EditorApplication.timeSinceStartup; nextProgress = 0;
                gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                gameView.autoRepaintOnSceneChange = true;
                oldCaptureRate = Time.captureFramerate; Time.captureFramerate = 30;
                Application.logMessageReceived += Log;
                EditorApplication.update += Tick;
                steps.Push(Verify());
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(Key + ".finished", false))
                Complete("Play mode stopped before verification finished.");
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SaveRepository.VerificationDirectory = null;
                SessionState.SetBool(Key, false);
                if (Arg("-firstDayGatheringExit", "") == "1") EditorApplication.Exit(SessionState.GetInt(Key + ".exit", 1));
            }
        }

        static void Log(string message, string trace, LogType type)
        {
            // The project already tracks this Unity search-index startup defect
            // separately in its movement verifiers. Preserve it in the report.
            if (trace.Contains("UnityEditor.Search.SearchDatabase") && trace.Contains("IndexationOnStartup"))
            { editorIssues.Add(message + "\n" + trace); return; }
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message + "\n" + trace;
        }
        static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            gameView?.Repaint();
            if (finishing) return;
            try
            {
                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "Verification timed out.");
                if (EditorApplication.timeSinceStartup >= nextProgress)
                {
                    nextProgress = EditorApplication.timeSinceStartup + 5;
                    File.WriteAllText(Path.Combine(output, "progress.txt"),
                        $"frame={Time.frameCount} paused={EditorApplication.isPaused} scale={Time.timeScale} scene={W?.Stage?.place} phase={S?.FirstDay?.Phase} story={M?.View?.ScriptedOpen} ready={M?.View?.PlayerInputReady} closing={M?.View?.Closing} step={(steps.Count > 0 ? steps.Peek().GetType().Name : "finished")}");
                }
                Require(EditorApplication.timeSinceStartup - lastGameFrameAt < 45, "The Game view stopped producing frames; keep the verification Game view visible.");
                if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount; lastGameFrameAt = EditorApplication.timeSinceStartup;
                if (steps.Count == 0) { Complete(null); return; }
                var current = steps.Peek();
                if (!current.MoveNext()) steps.Pop();
                else if (current.Current is IEnumerator nested) steps.Push(nested);
            }
            catch (Exception e) { Complete(e.ToString()); }
        }

        static IEnumerator Verify()
        {
            yield return Wait(() => S != null && S.IsReady && Button("Continue journey") != null, 120, "Title ready");
            Button("Continue journey").onClick.Invoke();
            yield return Wait(Ready, 180, "Eight native NPCs ready in the square");
            originalInput = InputSystem.settings; testInput = Object.Instantiate(originalInput);
            testInput.hideFlags = HideFlags.DontSave;
            testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testInput; keyboard = InputSystem.AddDevice<Keyboard>();
            yield return Frames(10);
            Require(S.FirstDay.GreetedCount == 0, "Ambient groups do not complete introductions.");
            CheckGroups();
            yield return Capture("01-three-groups.png");

            var start = M.Spawner.Actors.ToDictionary(a => a.npcId, a => a.transform.position.x);
            float until = Time.time + 32;
            while (Time.time < until)
            {
                CheckGroups();
                foreach (var a in M.Spawner.Actors)
                {
                    Require(Mathf.Abs(a.transform.position.x - start[a.npcId]) < .32f, "Local activity stays near its group: " + a.npcId);
                    samples.Add(new Sample { actor = a.npcId, time = Time.time, x = a.transform.position.x,
                        pose = a.Visual.Pose, partner = a.ConversationPartner?.GetComponent<NpcPresence>()?.npcId ?? "" });
                }
                yield return null;
            }
            foreach (string actor in start.Keys)
            {
                var movement = samples.Where(s => s.actor == actor).ToArray();
                Require(movement.Max(s => s.x) - movement.Min(s => s.x) > .1f, "Visible local step: " + actor);
                Require(movement.Any(s => s.pose == "Idle") && movement.Any(s => s.pose != "Idle"), "Existing standing and motion poses: " + actor);
            }
            Require(M.Conversations.CompletedConversations == 0 && M.Conversations.CommittedNpcLines == 0, "No AI chatter or fabricated conversation receipts.");
            Record("32 game seconds: three separate groups, all eight take short real steps, no overlapping, wandering or new dialogue.");
            yield return Capture("02-local-activity.png");

            S.SetOverlay(true); yield return Frames(3);
            var paused = M.Spawner.Actors.ToDictionary(a => a.npcId, a => a.transform.position.x);
            yield return Frames(45);
            Require(M.Spawner.Actors.All(a => Mathf.Abs(a.transform.position.x - paused[a.npcId]) < .02f), "Overlay pauses all local steps.");
            S.SetOverlay(false);
            yield return BeginGreeting("z");
            yield return Frames(4);
            var z = M.Spawner.Find("z");
            Require(z.ConversationPartner == W.Player && !z.Movement.IsMoving && z.Visual.transform.localScale.x < 0,
                "Greeting stops Z and turns her to the player.");
            yield return Capture("03-player-greeting.png");
            yield return Tap(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(() => !S.InputBlocked && !M.View.ScriptedOpen, 20, "Cancel greeting");
            Require(S.FirstDay.GreetedCount == 0, "Cancelled introduction does not count.");
            yield return Frames(3); CheckGroups();
            Record("Pause and cancelled E interaction stop movement; the NPC faces the player then returns to its group without adding progress.");

            yield return BeginGreeting("z"); yield return FinishGreeting();
            Require(S.FirstDay.GreetedCount == 1, "Completed introduction counts once.");
            Require(S.Save(), "Save greeting checkpoint.");
            S.LoadSavedGame();
            yield return Wait(Ready, 120, "Reload gathering checkpoint"); yield return Frames(5);
            Require(S.FirstDay.GreetedCount == 1, "Reload preserves the greeted list."); CheckGroups();
            S.Travel(PlaceId.Street);
            yield return Wait(() => W.Stage?.place == PlaceId.Street && !W.IsTransitioning && M.Spawner.Actors.Any(a => a != null && a.Movement?.IsReady == true), 90, "Leave square");
            yield return Frames(5);
            Require(!S.FirstDay.GatheringActive && M.Spawner.Actors.All(a => a.ConversationPartner == null && a.Movement.SimulateDecisions), "Other scenes retain normal movement.");
            S.Travel(PlaceId.Square); yield return Wait(Ready, 120, "Return to square"); yield return Frames(5);
            Require(S.FirstDay.GreetedCount == 1, "Travel preserves introduction progress."); CheckGroups();
            Record("Save/load and leave/return restore the three groups and greeting progress; other scenes retain their usual behaviour.");

            foreach (string actor in groups.SelectMany(g => g).Where(id => id != "z"))
            {
                yield return BeginGreeting(actor); yield return FinishGreeting();
                Require(S.State.firstDay.greeted.Contains(actor), "Actual introduction completed: " + actor);
            }
            Require(S.FirstDay.GreetedCount == 8, "All eight NPCs reachable and counted.");
            yield return BeginGreeting("axing"); yield return FinishGreeting();
            Require(S.FirstDay.GreetedCount == 8, "Repeat introduction does not double count.");
            yield return Capture("04-eight-introductions.png");
            Record("Real E interactions reach all eight NPCs; completion and repeated introductions preserve the 8/8 counter.");

            yield return BeginGreeting("z"); yield return FinishGreeting(true);
            yield return Wait(() => Button("Accept invitation") != null, 20, "Fireworks invitation");
            Button("Accept invitation").onClick.Invoke(); yield return Frames(3);
            Require(S.FirstDay.Phase == FirstDayPhase.Fireworks && !S.FirstDay.GatheringActive, "Fireworks releases gathering ownership.");
            Require(M.Spawner.Actors.All(a => a.ConversationPartner == null && !a.Movement.IsMoving), "No stale group partners or steps in the fireworks sequence.");
            Record("The fireworks invitation releases group partners and stops local movement.");
            yield return Wait(() => S.FirstDay.Phase == FirstDayPhase.Home && W.Stage?.place == PlaceId.PlayerHome && !W.IsTransitioning && !S.InputBlocked, 90, "Fireworks finishes at home");
            Record("The existing fireworks invitation, film and arrival home complete after the gathering releases its actors.");
            yield return Capture("05-after-fireworks.png");
        }

        static bool Ready() => S != null && !S.IsAtTitle && S.FirstDay.GatheringActive && W.Stage?.place == PlaceId.Square
            && !W.IsTransitioning && M?.Spawner != null && groups.SelectMany(g => g).All(id => M.Spawner.Find(id)?.Movement?.IsReady == true);

        static void CheckGroups()
        {
            var actors = M.Spawner.Actors.OrderBy(a => a.transform.position.x).ToArray();
            Require(actors.Length == 8 && actors.Select(a => a.npcId).Distinct().Count() == 8, "Eight unique actors.");
            int clusters = 1;
            for (int i = 1; i < actors.Length; i++)
            {
                float gap = actors[i].transform.position.x - actors[i - 1].transform.position.x;
                Require(gap > 1, "NPC bodies and interaction points stay separated.");
                if (gap > 2) clusters++;
            }
            Require(clusters == 3, "Three visibly separate groups.");
            foreach (var group in groups)
                foreach (string id in group)
                {
                    var npc = M.Spawner.Find(id);
                    string partner = npc.ConversationPartner?.GetComponent<NpcPresence>()?.npcId;
                    Require(partner != null && partner != id && group.Contains(partner), "Attention stays inside the authored group: " + id);
                }
        }

        static IEnumerator BeginGreeting(string actor)
        {
            var npc = M.Spawner.Find(actor);
            W.Player.GetComponent<PlayerController>().Teleport(npc.transform.position + Vector3.left * .4f);
            W.Follow.Snap(); yield return Frames(4);
            Require(W.Player.GetComponent<PlayerInteraction>().Current?.id == actor, "Nearest E target: " + actor);
            yield return Tap(UnityEngine.InputSystem.Key.E);
            yield return Wait(() => M.View.ScriptedOpen && M.View.PlayerInputReady, 30, "Open introduction: " + actor);
        }
        static IEnumerator FinishGreeting(bool invitation = false)
        {
            double until = EditorApplication.timeSinceStartup + 45;
            while (M.View.ScriptedOpen)
            {
                Require(EditorApplication.timeSinceStartup < until, "Introduction can finish.");
                yield return Tap(UnityEngine.InputSystem.Key.E);
            }
            yield return Wait(() => !M.View.Closing && (invitation || !S.InputBlocked), 20, "Return from introduction");
            yield return Frames(3);
        }
        static Button Button(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .FirstOrDefault(b => b.name == name && b.isActiveAndEnabled && b.IsInteractable());
        static IEnumerator Tap(UnityEngine.InputSystem.Key key)
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return Frames(2);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(2);
        }
        static IEnumerator Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(output, name), 3);
            yield return Wait(() => File.Exists(Path.Combine(output, name)), 15, "Screenshot " + name);
        }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Wait(Func<bool> ready, float seconds, string label)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (!ready()) { Require(EditorApplication.timeSinceStartup < until, label + " timed out."); yield return null; }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string Arg(string name, string fallback)
        { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

        static void Record(string check)
        {
            checks.Add(check);
            Debug.Log("[FirstDayGathering] " + check);
            WriteReport(false, "Verification still running.");
        }
        static void WriteReport(bool complete, string error)
        {
            if (string.IsNullOrEmpty(output)) return;
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(new Report { complete = complete, passed = complete && error == null,
                error = error ?? "", unityVersion = Application.unityVersion, savePath = SaveRepository.PathOnDisk,
                checks = checks.ToArray(), editorIssues = editorIssues.ToArray(), samples = samples.ToArray() }, true));
        }

        static void Complete(string error)
        {
            if (finishing) return; finishing = true;
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            WriteReport(true, error);
            Time.captureFramerate = oldCaptureRate;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (originalInput != null) InputSystem.settings = originalInput;
            if (testInput != null) Object.Destroy(testInput);
            SessionState.SetBool(Key + ".finished", true); SessionState.SetInt(Key + ".exit", error == null ? 0 : 1);
            if (error == null) Debug.Log("FIRST_DAY_GATHERING_VERIFICATION_PASSED"); else Debug.LogError(error);
            EditorApplication.ExitPlaymode();
        }
    }
}
