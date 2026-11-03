using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZFromMoon.Core;
using ZFromMoon.Dialogue;
using ZFromMoon.Interaction;
using ZFromMoon.Player;
using ZFromMoon.World;
using Object = UnityEngine.Object;

namespace ZFromMoon.EditorTools
{
    [InitializeOnLoad]
    public static class NpcMovementVerification
    {
        private const string RunningKey = "ProjectZ.NpcMovementVerification";
        private const string Street = "Assets/_Game/Scenes/Locations/Loc_TownStreet.unity";
        private const string Cabin = "Assets/_Game/Scenes/Locations/Loc_HarborCabin.unity";
        private static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
        private static readonly List<string> checks = new List<string>();
        private static readonly List<MotionSample> samples = new List<MotionSample>();
        private static readonly List<string> editorIssues = new List<string>();
        private static string failure;
        private static int frame = -1;
        private static double deadline;

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string error;
            public string[] checks;
            public MotionSample[] motion;
            public string[] editorIssues;
        }

        [Serializable]
        private sealed class MotionSample
        {
            public float time;
            public float x;
            public float speed;
        }

        static NpcMovementVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(RunningKey, false) && EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += () => {
                    // Package initialization can reload assemblies after Play mode has begun.
                    // Recreate the runtime session; its nonserialized state cannot survive that reload.
                    SceneManager.LoadScene("Assets/_Game/Scenes/App.unity", LoadSceneMode.Single);
                    StartSuite();
                };
            }
        }

        [MenuItem("Z From Moon/Verify NPC Movement")]
        public static void Run()
        {
            if (!Application.isBatchMode) LocationRouteRepair.RequireEditableScenes();
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/App.unity", OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            EditorApplication.EnterPlaymode();
        }

        public static void ConfigureAndRun()
        {
            WorldBuilder.ConfigureNpcMovementAssets();
            Run();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                StartSuite();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(RunningKey, false);
                if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(RunningKey + ".exit", 1));
            }
        }

        private static void StartSuite()
        {
            checks.Clear();
            samples.Clear();
            editorIssues.Clear();
            steps.Clear();
            failure = null;
            frame = -1;
            deadline = EditorApplication.timeSinceStartup + 180;
            steps.Push(Verify());
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[NPC Verification] Runtime suite started.");
        }

        private static void OnLog(string text, string trace, LogType type)
        {
            if (trace.Contains("UnityEditor.Search.SearchDatabase"))
            {
                editorIssues.Add(text + "\n" + trace);
                return;
            }
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                failure = text + "\n" + trace;
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "Verification exceeded 180 seconds.");
                while (steps.Count > 0)
                {
                    IEnumerator current = steps.Peek();
                    if (!current.MoveNext()) { steps.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { steps.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
            string path = Argument("-npcReport", Path.Combine(Path.GetTempPath(), "project-z-npc-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(new Report {
                passed = error == null, error = error, checks = checks.ToArray(), motion = samples.ToArray(),
                editorIssues = editorIssues.ToArray()
            }, true));
            SessionState.SetInt(RunningKey + ".exit", error == null ? 0 : 1);
            if (error == null) Debug.Log($"[NPC Verification] Passed {checks.Count} checks. {path}");
            else Debug.LogError("[NPC Verification] " + error);
            EditorApplication.ExitPlaymode();
        }

        private static IEnumerator Verify()
        {
            GameSession session = null;
            yield return Wait(() => (session = Object.FindFirstObjectByType<GameSession>()) != null && session.IsReady, 40, "App ready");
            yield return Delay(0.2f);
            yield return Travel(session, Street, "from_harbor_cabin");
            NpcMovementController npc = Object.FindFirstObjectByType<NpcMovementController>();
            Require(npc != null && npc.isActiveAndEnabled, "Street NPC has a configured movement controller.");
            npc.SimulateDecisions = false;
            yield return Wait(() => npc.BeginDecision() != null, 5, "NPC ready");
            GameState originalState = session.State.Snapshot;
            var results = new List<NpcMoveResult>();
            npc.MovementFinished += results.Add;
            Rigidbody2D body = npc.GetComponent<Rigidbody2D>();
            Vector2 start = body.position;
            PlayerMotor player = Object.FindFirstObjectByType<PlayerMotor>();
            Require(player != null, "The existing player is present.");
            player.Teleport(new Vector2(-8.3f, start.y));
            CameraFollow camera = Object.FindFirstObjectByType<CameraFollow>();
            camera.FocusOn(start.x, 5.4f, 0.01f);
            yield return Delay(0.2f);
            Capture(null);
            yield return Delay(0.1f);
            Capture("01-before");

            NpcMoveRequest pending = npc.BeginDecision();
            Require(!npc.AcceptDecision(pending.request_id, "{}", out _), "Missing action is rejected.");
            Require(!npc.AcceptDecision(pending.request_id, Json(pending, "teleport"), out _), "Unknown target is rejected.");
            Require(!npc.AcceptDecision(pending.request_id, Json(pending, "right", "another_actor"), out _), "Another actor is rejected.");
            string json = Json(pending, "right");
            Require(npc.AcceptDecision(pending.request_id, json, out string reason), "Valid movement accepted: " + reason);
            Require(!npc.AcceptDecision(pending.request_id, json, out _), "Duplicate response is rejected.");
            checks.Add("Structured move input; missing action, unknown target, wrong actor and duplicate response rejected");
            yield return Wait(() => body.position.x > start.x + 0.6f, 5, "NPC begins walking");
            Require(npc.ActualSpeed > 0f && npc.FacingSign == 1, "Moving NPC faces its travel direction.");
            Animator animator = npc.GetComponentInChildren<Animator>();
            Require(animator != null && animator.GetBool("IsMoving"), "Existing walk animation is active.");
            Capture("02-walking");
            yield return TrackUntilDone(npc, 8);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Arrived, "Movement reports actual arrival.");
            Require(Mathf.Abs(body.position.x - (start.x + 3f)) <= 0.05f, "Arrival is within five centimetres.");
            Require(Mathf.Abs(body.position.y - start.y) <= 0.03f, "Feet remain on the ground.");
            Require(results.Count == 1, "Arrival receipt is emitted exactly once.");
            yield return Delay(0.3f);
            Require(!animator.GetBool("IsMoving"), "Idle animation resumes after arrival.");
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Animator has actually returned to Idle.");
            Capture("03-arrived");
            checks.Add("Walk animation, facing, grounded travel, accurate arrival and exactly-once receipt");

            Issue(npc, "left");
            yield return Delay(0.6f);
            float pausedX = body.position.x;
            Time.timeScale = 0f;
            yield return Delay(0.25f, true);
            Require(Mathf.Abs(body.position.x - pausedX) < 0.002f, "Paused NPC does not drift.");
            Time.timeScale = 1f;
            yield return Delay(0.2f);
            string previousId = results.Last().RequestId;
            Issue(npc, "right");
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Interrupted && npc.LastResult.RequestId != previousId,
                "New destination interrupts the previous command.");
            yield return TrackUntilDone(npc, 8);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Arrived, "NPC naturally reverses toward its new target.");
            checks.Add("Pause preserves position; replacement command turns and completes without teleporting");

            pending = npc.BeginDecision();
            json = Json(pending, "left");
            session.State.SetFlag("npc_verification", true);
            Require(!npc.AcceptDecision(pending.request_id, json, out _), "A changed world snapshot rejects an old response.");
            session.State.SetFlag("npc_verification", false);
            originalState = session.State.Snapshot;
            Issue(npc, "left");
            yield return Delay(0.4f);
            pending = npc.BeginDecision();
            json = Json(pending, "right");
            session.Modes.SetMode(GameMode.Dialogue);
            float stoppedX = body.position.x;
            yield return Delay(0.2f);
            Require(!npc.IsMoving && Mathf.Abs(body.position.x - stoppedX) < 0.002f, "Dialogue immediately interrupts motion.");
            session.Modes.SetMode(GameMode.Exploration);
            Require(!npc.AcceptDecision(pending.request_id, json, out _), "Late pre-dialogue response stays invalid.");
            checks.Add("World changes and dialogue invalidate pending requests; dialogue stops motion");

            Issue(npc, "left");
            yield return Delay(0.2f);
            GameObject obstacle = new GameObject("Verification obstacle");
            obstacle.layer = 9;
            obstacle.transform.position = new Vector3(body.position.x - 1f, start.y + 1f, 0f);
            obstacle.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 2f);
            Physics2D.SyncTransforms();
            float boundary = obstacle.transform.position.x + 0.15f;
            yield return TrackUntilDone(npc, 6);
            Require(npc.LastResult.Outcome == NpcMoveOutcome.Blocked, "Obstruction reports failure, not arrival.");
            Require(body.position.x >= boundary + 0.30f, "NPC does not penetrate the obstacle.");
            Object.Destroy(obstacle);
            yield return null;
            checks.Add("Dynamic obstruction stops the body and returns Blocked without a false arrival");

            pending = npc.BeginDecision();
            GameObject secondObject = Object.Instantiate(npc.gameObject, new Vector3(-5f, start.y, 0f), Quaternion.identity);
            secondObject.name = "Verification second NPC";
            NpcMovementController second = secondObject.GetComponent<NpcMovementController>();
            second.Configure("second_npc", secondObject.transform.Find("Visual"), secondObject.GetComponentInChildren<Animator>(),
                new[] { new NpcDestination("right", new Vector2(1f, 0f)) });
            second.SimulateDecisions = false;
            yield return Wait(() => second.BeginDecision() != null, 5, "Second NPC ready");
            Require(!second.AcceptDecision(pending.request_id, Json(pending, "right"), out _), "NPC request tokens are isolated.");
            Issue(npc, "right");
            Issue(second, "right");
            yield return TrackUntilDone(second, 5);
            yield return TrackUntilDone(npc, 10);
            Require(second.LastResult.Outcome == NpcMoveOutcome.Arrived && npc.LastResult.Outcome == NpcMoveOutcome.Arrived,
                $"Both NPCs independently execute destinations. First: {npc.LastResult.Outcome}/{npc.LastResult.Reason}/{npc.LastResult.Position}. "
                + $"Second: {second.LastResult.Outcome}/{second.LastResult.Reason}/{second.LastResult.Position}.");
            Object.Destroy(secondObject);
            checks.Add("Two NPCs move independently; a response cannot control the other NPC");

            player.Teleport(new Vector2(body.position.x - 1.2f, start.y));
            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            DialogueController dialogue = Object.FindFirstObjectByType<DialogueController>();
            yield return Wait(() => interactor.CurrentTarget == npc.GetComponent<Interactable>(), 3, "NPC interaction target");
            typeof(PlayerInteractor).GetMethod("OnInteractRequested", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interactor, null);
            Require(dialogue.IsOpen && dialogue.CurrentActor.gameObject == npc.gameObject, "Existing interaction opens the NPC dialogue.");
            Require(!npc.IsMoving && session.Modes.Current == GameMode.Dialogue, "NPC remains stopped throughout dialogue.");
            yield return Delay(0.6f);
            Capture("04-dialogue");
            dialogue.Close();
            yield return Delay(0.5f);
            player.Teleport(new Vector2(-8.3f, start.y));
            checks.Add("Existing player interaction opens and closes dialogue with the moving NPC integration");

            Issue(npc, "left");
            yield return Delay(0.3f);
            pending = npc.BeginDecision();
            Require(npc.AcceptDecision(pending.request_id, Json(pending, null), out _), "Wait action is accepted.");
            Require(!npc.IsMoving && npc.LastResult.Outcome == NpcMoveOutcome.Waiting, "Wait stops and reports Waiting.");
            Require(session.State.Snapshot == originalState, "Movement does not mutate story state or game time.");
            checks.Add("Wait cancels movement; movement does not advance time or mutate story state");

            Issue(npc, "left");
            yield return Delay(0.2f);
            bool redirected = false;
            Action<NpcMoveResult> redirect = result => {
                if (redirected || result.Reason != "replaced") return;
                redirected = true;
                Issue(npc, "home");
            };
            npc.MovementFinished += redirect;
            Issue(npc, "right");
            npc.MovementFinished -= redirect;
            yield return TrackUntilDone(npc, 8);
            Require(redirected && npc.LastResult.Outcome == NpcMoveOutcome.Arrived && npc.LastResult.TargetId == "home",
                "A callback-issued destination is not overwritten by the outer transition.");
            checks.Add("A receipt callback can submit a newer destination without losing the new command");

            pending = npc.BeginDecision();
            json = Json(pending, "left");
            npc.enabled = false;
            yield return null;
            npc.enabled = true;
            Require(!npc.AcceptDecision(pending.request_id, json, out _), "Disable/enable rejects old responses.");
            Issue(npc, "left");
            yield return Delay(0.2f);
            yield return Travel(session, Cabin, "from_town_street");
            Require(results.Last().Outcome == NpcMoveOutcome.Interrupted, "Scene transition interrupts movement.");
            Require(npc == null, "Unloaded scene leaves no NPC behind.");
            checks.Add("Disable/re-enable and scene unload invalidate movement without leaving a ghost actor");

            foreach (string scenePath in new[] {
                "Assets/_Game/Scenes/DockLocation.unity",
                "Assets/_Game/Scenes/SeasidePlaceholder.unity" })
            {
                yield return Travel(session, scenePath, "from_shore");
                NpcMovementController[] actors = Object.FindObjectsByType<NpcMovementController>(FindObjectsSortMode.None)
                    .Where(candidate => candidate.gameObject.scene == session.Location.gameObject.scene).ToArray();
                Require(actors.Length > 0, "Existing NPC assets are wired in " + scenePath);
                foreach (NpcMovementController actor in actors)
                {
                    actor.SimulateDecisions = false;
                    yield return Wait(() => actor.BeginDecision() != null, 5, "Existing NPC asset ready");
                    Rigidbody2D actorBody = actor.GetComponent<Rigidbody2D>();
                    Vector2 origin = actorBody.position;
                    player.Teleport(origin + Vector2.left * 2f);
                    Issue(actor, "right");
                    yield return Wait(() => actor.ActualSpeed > 0.3f, 4, "Existing NPC asset walking");
                    Animator actorAnimator = actor.GetComponentInChildren<Animator>();
                    Require(actorAnimator != null && actorAnimator.GetBool("IsMoving"), "Existing NPC asset uses its walk animation.");
                    yield return TrackUntilDone(actor, 8);
                    Require(actor.LastResult.Outcome == NpcMoveOutcome.Arrived
                        && Mathf.Abs(actorBody.position.x - origin.x - 3f) <= 0.05f,
                        "Existing NPC asset reaches the authored destination: " + scenePath);
                    yield return Delay(0.3f);
                    Require(!actorAnimator.GetBool("IsMoving") && actorAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                        "Existing NPC asset settles into idle.");
                }
                checks.Add("Existing NPC asset movement and animation verified in " + scenePath);
            }
        }

        private static IEnumerator TrackUntilDone(NpcMovementController npc, float timeout)
        {
            double end = EditorApplication.timeSinceStartup + timeout;
            var body = npc.GetComponent<Rigidbody2D>();
            Vector2 last = body.position;
            float lastTime = Time.time;
            float sampledFixedTime = -1f;
            while (npc.IsMoving)
            {
                Require(EditorApplication.timeSinceStartup < end, "Movement did not settle before timeout.");
                yield return null;
                float elapsed = Mathf.Max(0f, Time.time - lastTime);
                Require(Vector2.Distance(body.position, last) <= 1.8f * elapsed + 0.06f, "Motion exceeds its speed bound or teleports.");
                if (Time.fixedTime != sampledFixedTime)
                {
                    samples.Add(new MotionSample { time = Time.time, x = body.position.x, speed = npc.ActualSpeed });
                    sampledFixedTime = Time.fixedTime;
                }
                last = body.position;
                lastTime = Time.time;
            }
        }

        private static IEnumerator Travel(GameSession session, string path, string entry)
        {
            bool done = false;
            bool success = false;
            session.Modes.SetMode(GameMode.Transition);
            session.StartCoroutine(session.TravelRoutine(path, entry, value => { success = value; done = true; }));
            yield return Wait(() => done, 30, "Location travel");
            Require(success, "Location travel succeeded.");
            session.CompleteTravelMode();
            yield return null;
        }

        private static void Issue(NpcMovementController npc, string target)
        {
            NpcMoveRequest request = npc.BeginDecision();
            Require(request != null, "NPC is accepting requests.");
            Require(npc.AcceptDecision(request.request_id, Json(request, target), out string reason), "Destination accepted: " + reason);
        }

        private static string Json(NpcMoveRequest request, string target, string actor = null)
        {
            return JsonUtility.ToJson(new NpcMoveOutput {
                dialogue = "", info = new NpcMoveOutput.ActorInfo { actor_id = actor ?? request.actor_id },
                action = new NpcMoveCommand { type = target == null ? "wait" : "move", target = target,
                    action_id = target == null ? "wait_here" : "move_" + target, description = "verification" }
            });
        }

        private static IEnumerator Wait(Func<bool> condition, float seconds, string description)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                Require(EditorApplication.timeSinceStartup < end, description + " timed out.");
                yield return null;
            }
        }

        private static IEnumerator Delay(float seconds, bool unscaled = false)
        {
            float end = (unscaled ? Time.unscaledTime : Time.time) + seconds;
            while ((unscaled ? Time.unscaledTime : Time.time) < end) yield return null;
        }

        private static void Capture(string name)
        {
            string folder = Argument("-npcCaptureDir", null);
            if (folder == null) return;
            Directory.CreateDirectory(folder);
            Camera camera = Camera.main;
            Require(camera != null, "A game camera is available for visual verification.");
            RenderTexture previous = RenderTexture.active;
            RenderTexture render = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                if (name != null) File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(render);
                Object.Destroy(pixels);
            }
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
