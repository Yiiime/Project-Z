using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Seabreeze
{
    // Explicit QA entry. Uses an isolated save and real game input/AI protocol, never runs in normal play.
    public sealed class BenchTaskVerification : MonoBehaviour
    {
        const string TaskId = "street_corner_bench";
        readonly List<object> checks = new List<object>();
        int failures;
        string output;
        Keyboard keyboard;
        GameSession S => GameSession.Instance;
        MemoryGameController M => S?.Memory;
        WorldDirector W => WorldDirector.Instance;
        WorldTaskController Tasks => M.GetComponent<WorldTaskController>();
        static string Arg(string key) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, key); return i >= 0 && i + 1 < a.Length ? a[i + 1] : ""; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (string.IsNullOrEmpty(Arg("-benchTaskOutput"))) return;
            if (!(Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT") ?? "").StartsWith("/private/tmp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Bench QA requires isolated temporary saves.");
            var root = new GameObject("Bench task QA"); DontDestroyOnLoad(root); root.AddComponent<BenchTaskVerification>();
        }
        void Check(string name, bool passed, object detail = null)
        {
            checks.Add(new { name, passed, detail }); if (!passed) failures++;
            File.WriteAllText(output, JsonConvert.SerializeObject(new { complete = false, failures, checks }, Formatting.Indented));
        }
        IEnumerator Wait(Func<bool> ready, string name, float seconds = 50)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!ready() && Time.realtimeSinceStartup < until) yield return null;
            Check(name, ready(), M?.Client?.LastError);
        }
        bool Ready() => M != null && M.Client.Synchronized && !W.IsTransitioning;
        IEnumerator KeyE()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            yield return new WaitForSecondsRealtime(.2f);
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(output), name + ".png"));
            yield return new WaitForSecondsRealtime(.6f);
        }
        int Receipts() => JObject.Parse(S.State.memory.checkpoint)["tasks"]["receipts"].Count(r => ((string)r["receipt"]).Contains(TaskId));
        IEnumerator Travel(PlaceId place)
        {
            M.Conversations.CancelAll(); yield return new WaitForSecondsRealtime(1);
            S.Travel(place); yield return new WaitForSecondsRealtime(2);
            yield return Wait(() => Ready() && W.Stage.place == place, "arrived:" + place);
            yield return null;
        }
        IEnumerator Start()
        {
            output = Arg("-benchTaskOutput"); Directory.CreateDirectory(Path.GetDirectoryName(output));
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return Wait(Ready, "service-ready", 120);
            if (!Ready()) { Finish(); yield break; }
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            if (Arg("-benchTaskResume") == "completed")
            {
                Check("process-restart-restores-completed", Tasks.Status(TaskId) == "completed");
                Check("process-restart-keeps-one-receipt", Receipts() == 1);
                Finish(); yield break;
            }
            if (W.Stage.place != PlaceId.Street) yield return Travel(PlaceId.Street);
            yield return new WaitForSecondsRealtime(1);
            var target = W.Interactables.FirstOrDefault(i => i != null && i.id == TaskId);
            Check("chair-bound-at-flower-cafe-staircase", target != null && Mathf.Abs(target.transform.position.x + 2.616667f) < .01f);
            if (target == null) { Finish(); yield break; }
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(target.transform.position.x, .08f)); W.Follow.Snap();
            yield return new WaitForSecondsRealtime(.5f); yield return KeyE();
            Check("unaccepted-E-does-not-complete", Tasks.Status(TaskId) == "available");
            yield return Travel(PlaceId.Cafe);
            var npc = M.Spawner.Find("liting");
            Check("liting-present", npc != null);
            if (npc == null) { Finish(); yield break; }
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(npc.transform.position.x - .7f, .08f));
            M.BeginPlayerChat("liting"); yield return new WaitForSecondsRealtime(1);
            yield return Wait(() => M.Client.CanGenerate && !M.Conversations.Sending, "dialogue-ready");
            M.Conversations.SendPlayer("有什么我能帮忙的吗？想接下街角长椅的委托。");
            yield return Wait(() => Ready() && Tasks.Status(TaskId) == "offered", "AI-published-bench-task");
            yield return new WaitForSecondsRealtime(4); yield return Capture("01-ai-offer");
            var accept = M.View.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "接受任务");
            Check("accept-button-available", accept != null && accept.interactable);
            if (accept == null || !accept.interactable) { Finish(); yield break; }
            accept.onClick.Invoke();
            yield return Wait(() => Ready() && Tasks.Status(TaskId) == "active", "accepted-in-existing-system");
            M.Conversations.CancelAll(); yield return new WaitForSecondsRealtime(1);
            S.Save(); S.LoadSavedGame(); yield return new WaitForSecondsRealtime(2);
            yield return Wait(() => Ready() && Tasks.Status(TaskId) == "active", "reload-active-task");
            yield return Travel(PlaceId.Street);
            target = W.Interactables.First(i => i != null && i.id == TaskId);
            var other = W.Interactables.First(i => i != null && i.id == "bench");
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(other.transform.position.x, .08f));
            yield return new WaitForSecondsRealtime(.4f); yield return KeyE();
            yield return Wait(Ready, "after-other-bench");
            Check("other-bench-does-not-complete", Tasks.Status(TaskId) == "active");
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(target.transform.position.x + 4, .08f));
            target.Activate(); Check("far-activation-rejected", !S.State.memory.outbox.Any(e => e.kind == "task_receipt"));
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(target.transform.position.x - .35f, .08f)); W.Follow.Snap();
            yield return new WaitForSecondsRealtime(.6f);
            Check("E-prompt-points-to-target", W.Player.GetComponent<PlayerInteraction>().Current == target && target.Prompt == "查看长椅");
            yield return Capture("02-chair-and-tracker");
            int day = S.State.day, period = S.State.period;
            // Pause delivery for two presses, then check the actual disk outbox before reconnecting.
            M.Client.enabled = false; yield return KeyE(); yield return KeyE(); S.Save();
            var queued = SaveRepository.Load().memory.outbox.Where(e => e.kind == "task_receipt" && e.taskId == TaskId).ToArray();
            Check("repeated-E-queues-once", queued.Length == 1);
            Check("disk-save-preserves-interaction-evidence", queued.Length == 1 && queued[0].interactionInput == "E" && queued[0].interactionPlace == "Street" && queued[0].interactionTarget == TaskId);
            M.Client.enabled = true;
            yield return Wait(() => Ready() && Tasks.Status(TaskId) == "completed", "E-completes-on-site");
            Check("task-does-not-advance-time", S.State.day == day && S.State.period == period);
            Check("exactly-one-completion-receipt", Receipts() == 1);
            yield return Capture("03-completed"); yield return KeyE();
            Check("completed-E-does-not-reward-again", Receipts() == 1);
            S.Save(); S.LoadSavedGame(); yield return new WaitForSecondsRealtime(2);
            yield return Wait(() => Ready() && Tasks.Status(TaskId) == "completed", "reload-completed-task");
            Check("reload-keeps-one-receipt", Receipts() == 1);
            Finish();
        }
        void Finish()
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            File.WriteAllText(output, JsonConvert.SerializeObject(new { complete = true, failures, unity = Application.unityVersion, checks }, Formatting.Indented));
            Application.Quit(failures == 0 ? 0 : 1);
        }
    }
}
