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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class NpcMovementVerification
    {
        const string RunKey = "Seabreeze.NpcVerification";
        const string RootKey = RunKey + ".root";
        static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
        static readonly List<string> checks = new List<string>();
        static readonly List<MotionSample> samples = new List<MotionSample>();
        static readonly List<string> editorIssues = new List<string>();
        static string failure;
        static bool finishing;
        static int frame;
        static double deadline;
        static Keyboard keyboard;
        static InputSettings originalSettings, testSettings;
        static HideFlags originalFlags;
        static GameSession S => GameSession.Instance;
        static WorldDirector W => WorldDirector.Instance;

        [Serializable]
        sealed class Report
        {
            public bool passed;
            public string unityVersion, savePath, error;
            public string[] checks, editorIssues;
            public MotionSample[] motion;
        }

        [Serializable]
        sealed class MotionSample
        {
            public string actor;
            public float time, x, speed;
        }

        static NpcMovementVerification()
        {
            if (SessionState.GetBool(RunKey, false)) SaveRepository.VerificationDirectory = SessionState.GetString(RootKey, "");
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("Seabreeze/Verify NPC Movement")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Start verification in Edit mode after compilation.");
            if (!Application.isBatchMode)
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save scene edits before verification.");
            string root = Path.GetFullPath(Argument("-npc-save-root", Path.Combine(Path.GetTempPath(), "seabreeze-npc-" + Guid.NewGuid().ToString("N"))));
            if (root == Path.GetFullPath(Application.persistentDataPath))
                throw new InvalidOperationException("Verification cannot use the player's save directory.");
            Directory.CreateDirectory(root);
            SessionState.SetString(RootKey, root);
            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(RunKey + ".finished", false);
            SaveRepository.VerificationDirectory = root;
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                finishing = false;
                checks.Clear(); samples.Clear(); editorIssues.Clear(); steps.Clear();
                failure = null;
                frame = -1;
                deadline = EditorApplication.timeSinceStartup + 300;
                steps.Push(Verify());
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(RunKey + ".finished", false))
                Complete("Play mode stopped before verification completed.", false);
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
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "Verification timed out.");
                while (steps.Count > 0)
                {
                    IEnumerator current = steps.Peek();
                    if (!current.MoveNext()) { steps.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { steps.Push(nested); continue; }
                    return;
                }
                Complete(null, true);
            }
            catch (Exception exception) { Complete(exception.ToString(), true); }
        }

        static void Complete(string error, bool exitPlayMode)
        {
            if (finishing) return;
            finishing = true;
            SessionState.SetBool(RunKey + ".finished", true);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (originalSettings != null)
            {
                InputSystem.settings = originalSettings;
                originalSettings.hideFlags = originalFlags;
            }
            if (testSettings != null) Object.Destroy(testSettings);
            Time.timeScale = 1;
            string path = Argument("-npcReport", Path.Combine(SessionState.GetString(RootKey, Path.GetTempPath()), "report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(new Report {
                passed = error == null, error = error, unityVersion = Application.unityVersion,
                savePath = SaveRepository.PathOnDisk, checks = checks.ToArray(), motion = samples.ToArray(), editorIssues = editorIssues.ToArray()
            }, true));
            SessionState.SetInt(RunKey + ".exit", error == null ? 0 : 1);
            if (error == null) Debug.Log($"[NPC Verification] Passed {checks.Count} checks. {path}");
            else Debug.LogError("[NPC Verification] " + error);
            if (exitPlayMode) EditorApplication.ExitPlaymode();
        }

        static IEnumerator Verify()
        {
            yield return Wait(() => S != null && S.IsReady && W != null && W.Stage != null && !W.IsTransitioning, 30, "Seabreeze startup");
            Require(SaveRepository.PathOnDisk == Path.Combine(SessionState.GetString(RootKey, ""), "seabreeze-save.json"), "Save isolation is active before gameplay.");
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
            yield return FinishDialogue();
            yield return ReadyActors();
            Require(S.Save() && File.Exists(SaveRepository.PathOnDisk), "Verification save writes only to its isolated directory.");
            checks.Add("Isolated editor saves; original player save directory is not used");

            NpcPresence presence = Actors().Single(p => p.npcId == "npc_mio");
            NpcMovementController npc = presence.Movement;
            Rigidbody2D body = npc.GetComponent<Rigidbody2D>();
            var receipts = new List<NpcMoveResult>();
            npc.MovementFinished += receipts.Add;
            string initialFacts = Facts();
            NpcMoveRequest request = npc.BeginDecision();
            Require(request.actor_id == "npc_mio" && request.location_id == "Street", "Request identifies the current actor and scene.");
            Require(!npc.AcceptDecision(request.request_id, "{}", out _), "Missing action rejected.");
            Require(!npc.AcceptDecision(request.request_id, "[", out _), "Malformed JSON rejected.");
            Require(!npc.AcceptDecision(request.request_id, Json(request, "not_a_place"), out _), "Unknown target rejected.");
            Require(!npc.AcceptDecision(request.request_id, Json(request, "right", "npc_chen"), out _), "Wrong actor rejected.");
            Vector2 expected = request.destinations.Single(p => p.id == "right").position;
            request.destinations.Single(p => p.id == "right").position = new Vector2(10000, 10000);
            Require(npc.AcceptDecision(request.request_id, Json(request, "right"), out string reason), "Valid response accepted: " + reason);
            Require(!npc.AcceptDecision(request.request_id, Json(request, "right"), out _), "Duplicate response rejected.");
            checks.Add("Actor/scene identity, malformed data, unknown targets, wrong actor and duplicate responses");
            yield return Delay(.2f);
            Require(presence.Visual.Pose == "Walk" && npc.ActualSpeed > 0 && npc.ActualSpeed < 1.8f, "Movement accelerates and drives the NPC visual.");
            Capture(null);
            yield return Delay(.1f);
            Capture("01-mio-moving");
            yield return Track(npc, 8);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Arrived && Vector2.Distance(body.position, expected) <= .05f, "Arrival uses the engine-owned destination, not modified request data.");
            Require(receipts.Count == 1 && Mathf.Abs(body.position.y) < .05f, "One receipt and grounded motion.");
            yield return Delay(.2f);
            Require(presence.Visual.Pose == "Idle", "Visual settles to idle.");
            Capture("02-mio-arrived");
            checks.Add("Smooth grounded movement, visual pose, engine-owned destination and exactly-once arrival");

            Issue(npc, "left");
            yield return Delay(.3f);
            Issue(npc, "right");
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Interrupted, "Replacement interrupts the old command.");
            yield return Track(npc, 8);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Arrived, "Reversal reaches its new target.");
            Issue(npc, "left");
            yield return Delay(.2f);
            Issue(npc, null);
            Require(!npc.IsMoving && npc.LastResult.Outcome == NpcMoveOutcome.Waiting, "Wait cancels movement.");
            Require(Facts() == initialFacts, "Movement does not advance time or alter narrative state.");
            checks.Add("Mid-route reversal and wait; no changes to game time, items, relationships or story facts");

            Issue(npc, "home");
            yield return Delay(.2f);
            request = npc.BeginDecision();
            string oldJson = Json(request, "right");
            yield return Tap(Key.M);
            Require(GameUI.Instance.HasOverlay && S.InputBlocked, "Actual map overlay blocks input.");
            Vector2 paused = body.position;
            yield return Delay(.25f, true);
            Require(Vector2.Distance(body.position, paused) < .002f, "Overlay freezes NPC position.");
            yield return Tap(Key.Escape);
            Require(!npc.AcceptDecision(request.request_id, oldJson, out _), "Pre-overlay response stays invalid after close.");
            yield return Track(npc, 8);
            checks.Add("Real map overlay pauses movement, invalidates pending decisions, and allows the route to resume");

            Issue(npc, "right");
            yield return Delay(.15f);
            var player = W.Player.GetComponent<PlayerController>();
            player.Teleport(new Vector2(body.position.x - .7f, .1f));
            var interaction = player.GetComponent<PlayerInteraction>();
            yield return Wait(() => interaction.Current != null && interaction.Current.id == npc.ActorId, 3, "NPC proximity interaction");
            request = npc.BeginDecision();
            oldJson = Json(request, "left");
            yield return Tap(Key.E);
            Require(S.Dialogue.IsOpen && !npc.IsMoving, "E opens real dialogue and interrupts movement.");
            paused = body.position;
            yield return Delay(.2f);
            Require(Vector2.Distance(body.position, paused) < .002f, "Conversation does not drift.");
            yield return FinishDialogue();
            Require(!npc.AcceptDecision(request.request_id, oldJson, out _), "Pre-dialogue reply cannot restart motion.");
            checks.Add("Actual E interaction with a moving NPC; dialogue stops it and rejects late replies");

            npc.Interrupt();
            player.Teleport(new Vector2(body.position.x - 1.2f, .1f));
            float npcX = body.position.x;
            yield return Hold(Key.D, .85f);
            yield return Delay(.2f);
            Require(W.Player.position.x > npcX + .8f, "NPC physics does not block the player's horizontal route.");
            Require(player.Grounded && player.GetComponentInChildren<CharacterVisual>().Pose == "Idle", "Player still settles on ground with its normal visual.");
            checks.Add("Player can walk through NPCs as in the original Seabreeze exploration flow");

            Issue(npc, "left");
            yield return Delay(.15f);
            GameObject obstacle = new GameObject("NPC verification obstacle");
            obstacle.layer = 8;
            obstacle.transform.position = new Vector3(body.position.x - .8f, 1, 0);
            obstacle.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 2);
            Physics2D.SyncTransforms();
            yield return Track(npc, 6);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Blocked && body.position.x > obstacle.transform.position.x + .39f, "Dynamic obstacle stops NPC without penetration or false arrival.");
            request = npc.BeginDecision();
            Require(!request.available_actions.Any(a => a.target == "left"), "An obstructed destination is not offered to the model.");
            Object.Destroy(obstacle);
            yield return null;
            Issue(npc, "home");
            yield return Track(npc, 8);
            checks.Add("Dynamic obstacle, truthful Blocked receipt, reachable target filtering and recovery");

            request = npc.BeginDecision();
            oldJson = Json(request, "right");
            S.SetFlag("npc_verification_context");
            Require(!npc.AcceptDecision(request.request_id, oldJson, out _), "Mutable flag changes invalidate a snapshot even without NotifyChanged.");
            S.State.flags.Remove("npc_verification_context");
            S.NotifyChanged();
            NpcMovementController other = Actors().Single(p => p.npcId == "npc_chen").Movement;
            request = npc.BeginDecision();
            Require(!other.AcceptDecision(request.request_id, Json(request, "right"), out _), "Requests cannot control another NPC.");
            Issue(npc, "right"); Issue(other, "left");
            yield return Track(npc, 8);
            yield return Track(other, 8);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Arrived && other.LastResult.Outcome == NpcMoveOutcome.Arrived, "Two distinct NPCs move independently.");
            checks.Add("Mutable-state request isolation and independent movement of Mio and Chen");

            Issue(npc, "left");
            yield return Delay(.15f);
            bool redirected = false;
            Action<NpcMoveResult> redirect = result => {
                if (redirected || result.Reason != "replaced") return;
                redirected = true;
                Issue(npc, "home");
            };
            npc.MovementFinished += redirect;
            Issue(npc, "right");
            npc.MovementFinished -= redirect;
            yield return Track(npc, 8);
            Require(redirected && npc.LastResult.TargetId == "home" && npc.LastResult.Outcome == NpcMoveOutcome.Arrived, "Callback-submitted destination remains authoritative.");
            checks.Add("Receipt callback can safely submit a newer target");

            Issue(npc, "right");
            yield return Delay(.15f);
            request = npc.BeginDecision();
            oldJson = Json(request, "left");
            npc.enabled = false;
            yield return null;
            Require(!npc.IsMoving && !body.simulated && presence.Visual.Pose == "Idle", "Disabling movement stops physics and pose.");
            npc.enabled = true;
            yield return null;
            Require(!npc.AcceptDecision(request.request_id, oldJson, out _), "Re-enabled controller rejects old requests.");
            checks.Add("Disable/re-enable preserves a stopped pose and rejects stale decisions");

            string beforeSimulation = Facts();
            npc.SimulateDecisions = true;
            yield return Wait(() => npc.IsMoving, 6, "Offline autonomous decision");
            npc.SimulateDecisions = false;
            yield return Track(npc, 8);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Arrived && Facts() == beforeSimulation, "Offline autonomous decisions use normal movement without mutating game time or facts.");
            checks.Add("Default offline autonomy executes the same target pipeline without a model service");

            var fixture = new GameObject("NPC one-way platform verification");
            fixture.transform.SetParent(W.Stage.transform);
            fixture.transform.position = new Vector3(12, 0, 0);
            fixture.AddComponent<Interactable>().id = "npc_mio";
            var fixturePresence = fixture.AddComponent<NpcPresence>();
            fixturePresence.npcId = "npc_mio"; fixturePresence.character = 1; fixturePresence.location = PlaceId.Street;
            yield return Wait(() => fixturePresence.Movement != null && fixturePresence.Movement.IsReady, 5, "Platform fixture");
            fixturePresence.Movement.SimulateDecisions = false;
            Issue(fixturePresence.Movement, "right");
            yield return Track(fixturePresence.Movement, 8);
            Require(fixturePresence.Movement.LastResult.Outcome == NpcMoveOutcome.Arrived && Mathf.Abs(fixture.transform.position.y) < .05f,
                "NPC walks along pavement beneath the existing one-way bench platform.");
            Object.Destroy(fixture);
            checks.Add("Existing one-way bench does not obstruct ground-level NPC movement");

            Issue(npc, "right");
            yield return Delay(.15f);
            request = npc.BeginDecision();
            oldJson = Json(request, "left");
            S.Clock.Advance();
            Require(!npc.IsMoving && !npc.AcceptDecision(request.request_id, oldJson, out _), "Clock changes interrupt old decisions.");
            S.Clock.Advance();
            yield return null;
            Require(Actors().All(p => !p.IsPresent && !p.Movement.GetComponent<Rigidbody2D>().simulated), "Street NPCs disappear without invisible colliders at evening.");
            checks.Add("Clock transitions and authored NPC schedules control presence and physics");

            yield return Travel(PlaceId.Cafe);
            yield return ReadyActors();
            NpcPresence aya = Actors().Single(p => p.npcId == "npc_aya");
            Require(aya.IsPresent, "Aya remains at the cafe before invitation.");
            yield return VerifyActor(aya);
            Capture("03-cafe");
            Issue(aya.Movement, "right");
            yield return Delay(.15f);
            request = aya.Movement.BeginDecision();
            oldJson = Json(request, "left");
            S.SetFlag("choice_invite"); S.NotifyChanged();
            Require(!aya.IsPresent && !aya.Movement.IsMoving && !aya.Movement.GetComponent<Rigidbody2D>().simulated,
                "Invitation removes Aya from the cafe and cancels her movement.");
            Require(!aya.Movement.AcceptDecision(request.request_id, oldJson, out _), "Hidden NPC cannot accept old movement.");
            checks.Add("Cafe movement and flag-driven schedule cancellation");

            yield return Travel(PlaceId.Lighthouse);
            yield return ReadyActors();
            Require(Actors().Length == 3 && Actors().All(p => p.IsPresent), "Invited lighthouse gathering contains all three characters.");
            foreach (NpcPresence actor in Actors()) yield return VerifyActor(actor);
            Capture("04-lighthouse");
            checks.Add("Mio, Aya and Chen all execute destinations using the same controller at the lighthouse");
            npc = Actors().Single(p => p.npcId == "npc_mio").Movement;
            Issue(npc, "right");
            yield return Delay(.15f);
            NpcMoveResult last = null;
            npc.MovementFinished += result => last = result;
            yield return Travel(PlaceId.Street);
            Require(npc == null && last != null && last.Outcome == NpcMoveOutcome.Interrupted, "Scene unload cancels movement and leaves no actor behind.");
            Require(SaveRepository.Load().place == "Street", "New-framework save/load uses the isolated path through scene changes.");
            checks.Add("Scene unloading, stale actor cancellation and isolated save roundtrip");
        }

        static NpcPresence[] Actors() => Object.FindObjectsByType<NpcPresence>(FindObjectsSortMode.None)
            .Where(p => p.gameObject.scene == W.Stage.gameObject.scene).OrderBy(p => p.npcId, StringComparer.Ordinal).ToArray();

        static IEnumerator ReadyActors()
        {
            yield return Wait(() => Actors().Length > 0 && Actors().All(p => p.Movement != null && p.Movement.IsReady), 8, "NPC initialization");
            foreach (NpcPresence actor in Actors()) actor.Movement.SimulateDecisions = false;
            yield return null;
        }

        static IEnumerator VerifyActor(NpcPresence actor)
        {
            Require(actor.IsPresent, "Scheduled actor is visible: " + actor.npcId);
            NpcMoveRequest request = actor.Movement.BeginDecision();
            NpcDestination point = request.destinations.FirstOrDefault(p => Vector2.Distance(p.position, request.position) > .7f);
            Require(point != null, "Scheduled actor has a reachable destination: " + actor.npcId);
            Require(actor.Movement.AcceptDecision(request.request_id, Json(request, point.id), out string reason), "Actor accepts destination: " + reason);
            yield return Track(actor.Movement, 8);
            Require(actor.Movement.LastResult.Outcome == NpcMoveOutcome.Arrived && Vector2.Distance(actor.transform.position, point.position) <= .05f,
                "Scheduled actor reaches target: " + actor.npcId);
            Issue(actor.Movement, "home");
            yield return Track(actor.Movement, 8);
            Require(actor.Movement.LastResult.Outcome == NpcMoveOutcome.Arrived, "Scheduled actor returns home: " + actor.npcId);
        }

        static IEnumerator Track(NpcMovementController npc, float timeout)
        {
            double end = EditorApplication.timeSinceStartup + timeout;
            Rigidbody2D body = npc.GetComponent<Rigidbody2D>();
            Vector2 last = body.position;
            float lastTime = Time.time, fixedTime = -1;
            while (npc.IsMoving)
            {
                Require(EditorApplication.timeSinceStartup < end, "Movement timed out: " + npc.ActorId);
                yield return null;
                Require(Vector2.Distance(body.position, last) <= 1.8f * Mathf.Max(0, Time.time - lastTime) + .06f, "No teleport or speed-bound violation.");
                if (fixedTime != Time.fixedTime)
                {
                    samples.Add(new MotionSample { actor = npc.ActorId, time = Time.time, x = body.position.x, speed = npc.ActualSpeed });
                    fixedTime = Time.fixedTime;
                }
                last = body.position; lastTime = Time.time;
            }
        }

        static void Issue(NpcMovementController npc, string target)
        {
            NpcMoveRequest request = npc.BeginDecision();
            Require(request != null, "NPC accepts decisions: " + npc.ActorId);
            Require(npc.AcceptDecision(request.request_id, Json(request, target), out string reason), "Move accepted for " + npc.ActorId + ": " + reason);
        }

        static string Json(NpcMoveRequest request, string target, string actor = null) => JsonUtility.ToJson(new NpcMoveOutput {
            dialogue = "", info = new NpcMoveOutput.ActorInfo { actor_id = actor ?? request.actor_id },
            action = new NpcMoveCommand { type = target == null ? "wait" : "move", target = target,
                action_id = target == null ? "wait_here" : "move_" + target, description = "verification" }
        });

        static string Facts()
        {
            SaveData state = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(S.State));
            state.playerX = 0;
            return JsonUtility.ToJson(state);
        }

        static IEnumerator FinishDialogue()
        {
            int budget = 60;
            while (S.Dialogue.IsOpen && budget-- > 0)
            {
                if (S.Dialogue.Choices.Count > 0) S.Dialogue.Choose(0); else S.Dialogue.Advance();
                yield return null;
            }
            Require(!S.Dialogue.IsOpen, "Dialogue terminates.");
            yield return null;
        }

        static IEnumerator Travel(PlaceId place)
        {
            GameUI.Instance.CloseOverlay();
            S.Travel(place);
            yield return null;
            yield return Wait(() => !W.IsTransitioning && W.Stage != null && W.Stage.place == place, 15, "Travel " + place);
            yield return null;
        }

        static void Press(params Key[] keys)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        }

        static IEnumerator Tap(Key key)
        {
            Press(key); yield return null; yield return null; Press(); yield return null; yield return null;
        }

        static IEnumerator Hold(Key key, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) { Press(key); yield return null; }
            Press(); yield return null;
        }

        static IEnumerator Wait(Func<bool> predicate, float seconds, string label)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (!predicate()) { Require(EditorApplication.timeSinceStartup < end, label + " timed out."); yield return null; }
        }

        static IEnumerator Delay(float seconds, bool unscaled = false)
        {
            float end = (unscaled ? Time.unscaledTime : Time.time) + seconds;
            while ((unscaled ? Time.unscaledTime : Time.time) < end) yield return null;
        }

        static void Capture(string name)
        {
            string folder = Argument("-npcCaptureDir", null);
            if (folder == null) return;
            Directory.CreateDirectory(folder);
            Camera camera = Camera.main;
            RenderTexture previous = RenderTexture.active;
            RenderTexture render = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                if (name != null) File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(render); Object.Destroy(pixels); }
        }

        static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
