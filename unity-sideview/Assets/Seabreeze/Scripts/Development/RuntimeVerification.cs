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
            if (keyboard != null)
                InputSystem.RemoveDevice(keyboard);
            if (originalInputSettings != null)
            {
                InputSystem.settings = originalInputSettings;
                originalInputSettings.hideFlags = originalSettingsFlags;
            }
            if (verificationInputSettings != null) Destroy(verificationInputSettings);
        }
    }
}
#endif

