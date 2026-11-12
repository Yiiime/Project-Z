#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class FirstDayPlayVerification
    {
        const string Key = "Seabreeze.FirstDayPlayVerification";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static int stage;
        static double since, deadline;
        static float leaderX;
        static string output;
        static readonly JArray checks = new JArray();
        static readonly JArray errors = new JArray();
        static readonly JArray editorWarnings = new JArray();
        static FirstDayPlayVerification() { EditorApplication.update += Tick; Application.logMessageReceived += (message, stack, type) => { if (SessionState.GetBool(Key, false) && EditorApplication.isPlaying && type == LogType.Exception) { if (stack.Contains("UnityEditor.Search.")) editorWarnings.Add(message); else errors.Add(message); } }; }
        public static void Run()
        {
            output = Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
            if (string.IsNullOrEmpty(output) || !output.StartsWith("/private/tmp/")) throw new Exception("Use an isolated temporary save directory");
            Directory.CreateDirectory(output); SessionState.SetString(Key + "Output", output); SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); view.Show(); view.Focus();
            EditorApplication.EnterPlaymode();
        }
        static void Require(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        static void Next(int value) { stage = value; since = EditorApplication.timeSinceStartup; }
        static void Capture(string name) { Canvas.ForceUpdateCanvases(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                output = SessionState.GetString(Key + "Output", "");
                if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 150;
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout at stage " + stage);
                var game = GameSession.Instance; var world = WorldDirector.Instance;
                if (game == null || !game.IsReady || world?.Stage == null) return;
                double elapsed = EditorApplication.timeSinceStartup - since;
                if (stage == 0)
                {
                    var title = game.GetComponent<StartPageView>(); if (title == null) return;
                    typeof(StartPageView).GetMethod("Choose", Private).Invoke(title, new object[] { true });
                    Next(1); return;
                }
                if (stage == 1 && elapsed > 1)
                {
                    var field = UnityEngine.Object.FindFirstObjectByType<TMP_InputField>();
                    Require(field != null, "Landing opens a real name input"); field.text = "验收小海"; Capture("01-name"); Next(2); return;
                }
                if (stage == 2 && elapsed > .5)
                {
                    UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Confirm player name").onClick.Invoke(); Next(3); return;
                }
                if (game.IsAtTitle || game.Memory == null || world.IsTransitioning || !game.Memory.Spawner.IsStageReady(world.Stage)) return;
                if (stage == 3)
                {
                    Require(game.State.playerName == "验收小海" && game.CurrentPlace == PlaceId.Harbor, "Name reaches the actual new save and Harbor scene");
                    game.State.firstDay.phase = FirstDayPhase.Harbor; game.Memory.Spawner.Reload(); Next(4); return;
                }
                if (stage == 4 && game.Memory.Conversations.PlayerActor != null)
                {
                    Require(game.Memory.Conversations.PlayerActor.npcId == "z", "Dock automatically opens the Z conversation"); Capture("02-dock-chat"); Next(5); return;
                }
                if (stage == 5 && elapsed > 1)
                {
                    game.Memory.Conversations.CancelAll("verification");
                    Require(!game.State.firstDay.harborReady, "Cancelling an unfinished chat does not complete the meeting");
                    game.State.firstDay.phase = FirstDayPhase.WalkToSquare;
                    var z = game.Memory.Spawner.Find("z"); leaderX = z.transform.position.x;
                    world.Player.GetComponent<PlayerController>().Teleport(new Vector2(leaderX - 1, .08f)); Next(6); return;
                }
                if (stage == 6 && elapsed > 4)
                {
                    Require(game.Memory.Spawner.Find("z").transform.position.x > leaderX + .2f, "Z walks using actual NPC movement while the player stays independent");
                    world.Player.GetComponent<PlayerController>().Teleport(new Vector2(world.Stage.width / 2 - .4f, .08f)); Next(7); return;
                }
                if (stage == 7 && game.CurrentPlace == PlaceId.Street)
                {
                    Require(game.Memory.Spawner.Find("z") != null, "Harbor boundary loads Street and restores escort");
                    world.Player.GetComponent<PlayerController>().Teleport(new Vector2(world.Stage.width / 2 - .4f, .08f)); Next(8); return;
                }
                if (stage == 8 && game.FirstDay.Phase == FirstDayPhase.MeetIslanders)
                {
                    Require(game.Memory.Spawner.Actors.Count == 7 && game.Memory.Spawner.Find("liyuan") == null, "Square stages seven residents without Liyuan");
                    Require(!world.TravelToMapPoint(PlaceId.Street, 0), "Actual map travel cannot bypass incomplete introductions");
                    var announce = (System.Collections.IEnumerator)typeof(FirstDayDirector).GetMethod("Announce", Private).Invoke(game.FirstDay, new object[] { "这是验收小海，刚来到岛上的新朋友。" });
                    game.FirstDay.StartCoroutine(announce); Next(9); return;
                }
                if (stage == 9 && elapsed > 1)
                {
                    var bubble = game.GetComponents<NpcSpeechBubble>().FirstOrDefault(b => b.Visible);
                    Require(bubble != null && bubble.VisibleText.Contains("验收小海"), "Z introduction bubble is actually visible"); Capture("03-square"); Next(10); return;
                }
                if (stage == 10 && game.State.firstDay.announced)
                {
                    foreach (string actor in FirstDayDirector.FirstDayCast) game.FirstDay.ConversationCompleted(actor, "test", "fixture");
                    Require(game.FirstDay.GreetedCount == 7 && game.FirstDay.CanTravel(PlaceId.Street), "Seven completed conversation receipts release the boundary");
                    game.State.firstDay.phase = FirstDayPhase.WelcomeTask;
                    game.State.memory.checkpoint = new JObject { ["tasks"] = new JObject {
                        ["definitions"] = new JArray(new JObject { ["id"] = "welcome_ui_fixture", ["owner"] = "z", ["dynamic"] = true, ["title"] = "现场任务卡验收", ["description"] = "来自本局的动态任务定义", ["steps"] = new JArray(new JObject { ["id"] = "step_1", ["label"] = "拍下榕树", ["depends_on"] = new JArray() }) }),
                        ["state"] = new JArray(new JObject { ["task"] = "welcome_ui_fixture", ["status"] = "offered", ["progress"] = "{}" }) } }.ToString();
                    game.Memory.BeginPlayerChat("z"); Next(20); return;
                }
                if (stage == 20 && elapsed > 1)
                {
                    Require(UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None).Any(t => t.text == "现场任务卡验收" && t.gameObject.activeInHierarchy), "Generated task definition renders in the existing conversation task card");
                    Capture("04-task-card"); Next(21); return;
                }
                if (stage == 21 && elapsed > .5)
                {
                    game.Memory.Conversations.CancelAll("verification"); game.FirstDay.StartCoroutine(GameUI.Instance.ShowLiveNotification()); Next(11); return;
                }
                if (stage == 11 && elapsed > .65)
                {
                    var liveButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).SingleOrDefault(b => b.name == "Phone app live");
                    if (!GameUI.Instance.HasOverlay || liveButton == null || liveButton.GetComponent<UIHoverFeedback>().enabled) return;
                    Require(GameUI.Instance.HasOverlay, "Phone notification appears before livestream");
                    Require(liveButton.targetGraphic.color.r > .9f && !liveButton.GetComponent<UIHoverFeedback>().enabled, "Live icon stays highlighted during notification"); Capture("04-phone"); Next(12); return;
                }
                if (stage == 12 && elapsed > 1)
                {
                    Require(!GameUI.Instance.HasOverlay, "Phone notification closes after its one-second animation");
                    game.State.firstDay.phase = FirstDayPhase.EveningLive;
                    Require(game.Clock.CompleteLiveSession(Guid.NewGuid().ToString()) && game.State.day == 2 && !game.FirstDay.Active, "Evening completion returns to normal Day 2");
                    for (int i = 0; i < 4; i++) game.Clock.Advance();
                    Require(game.Clock.LiveReady && game.State.day == 2 && game.Clock.RestActions == 4, "Four real clock advances wait for daily livestream without changing day");
                    Capture("05-day2"); Next(13); return;
                }
                if (stage == 13 && elapsed > 1) { Require(errors.Count == 0, "No runtime exceptions during Play Mode verification: " + errors.ToString()); Finish(true, null); }
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static void Finish(bool passed, string error)
        {
            SessionState.SetBool(Key, false);
            File.WriteAllText(Path.Combine(output, "play-report.json"), new JObject { ["passed"] = passed, ["stage"] = stage, ["checks"] = checks, ["error"] = error,
                ["editorWarnings"] = editorWarnings, ["scope"] = "Actual Unity Play Mode UI, movement and scene transitions; model replies and livestream completion are injected test receipts, not an AI end-to-end run." }.ToString());
            Debug.Log("FIRST_DAY_PLAY_VERIFICATION_" + (passed ? "PASSED" : "FAILED") + " stage=" + stage + " " + error);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
#endif
