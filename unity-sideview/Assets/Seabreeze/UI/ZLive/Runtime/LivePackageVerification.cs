using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;
using Seabreeze;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
    // Exercises the shipped UI Toolkit shaders with an isolated save and a loopback fixture.
    public sealed class LivePackageVerification : MonoBehaviour {
        const BindingFlags Member = BindingFlags.NonPublic | BindingFlags.Instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() {
            if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-live-exit") < 0) return;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT"))) return;
            new GameObject("Packaged exit verification").AddComponent<LivePackageVerification>();
        }
        static void Submit(Button button) {
            button.Focus();
            using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
        }
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(1);
            var ui = FindFirstObjectByType<GameUI>();
            var game = GameSession.Instance;
            if (!ui || !game || game.State.day != 1 || game.State.period != 3) { Fail("evening save"); yield break; }
            typeof(GameUI).GetMethod("ToggleOverlay",Member).Invoke(ui,new object[]{"phone"});
            yield return new WaitForSecondsRealtime(1);
            var liveIcon = GameObject.Find("Phone app live")?.GetComponent<UnityEngine.UI.Button>();
            if (!liveIcon || !liveIcon.interactable) { Fail("phone live icon"); yield break; }
            liveIcon.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1);
            var feature = FindFirstObjectByType<LiveFeatureController>();
            var connected = typeof(LiveFeatureController).GetField("connected",Member);
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (feature && !(bool)connected.GetValue(feature) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (!feature || !(bool)connected.GetValue(feature)) { Fail("room connection"); yield break; }
            var root = feature.GetComponent<UIDocument>().rootVisualElement;
            string folder = Path.GetDirectoryName(SaveRepository.PathOnDisk);
            var draft = root.Q<TextField>(className:"chat-input");
            var chat = root.Q<ScrollView>(className:"chat");
            var send = (Button)typeof(LiveFeatureController).GetField("sendButton",Member).GetValue(feature);
            var lines = new[]{"晚上的海风好舒服。","想听你唱一首歌。"};
            foreach (string line in lines) {
                draft.value = line;
                deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!send.enabledInHierarchy && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (!send.enabledInHierarchy) { Fail("send button"); yield break; }
                Submit(send);
                deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!HasMessage(chat,line) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (!HasMessage(chat,line)) { Fail("sent chat message: "+line); yield break; }
            }
            // Rebuild the visible list from the real session snapshot, as on room entry.
            var type = typeof(LiveFeatureController);
            var snapshot = (JObject)type.GetField("state",Member).GetValue(feature);
            ((HashSet<string>)type.GetField("messageIds",Member).GetValue(feature)).Clear();
            chat.Clear();
            type.GetMethod("ApplyState",Member).Invoke(feature,new object[]{snapshot});
            if (!HasMessage(chat,lines[0]) || !HasMessage(chat,lines[1])) { Fail("restored chat"); yield break; }
            yield return new WaitForSecondsRealtime(.3f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"live-chat.png"));
            yield return new WaitForSecondsRealtime(.3f);
            feature.HandleBack();
            yield return new WaitForSecondsRealtime(.5f);
            var stay = root.Q<Button>("continue-watching");
            var end = root.Q<Button>("end-live-button");
            if (stay == null || end == null || stay.worldBound.width < 1 || end.worldBound.width < 1 || !Shader.Find("Hidden/UIR/GaussianBlur")) { Fail("rendered exit dialog"); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"exit-dialog.png"));
            yield return new WaitForSecondsRealtime(.3f);
            Submit(stay);
            if (!ui.HasOverlay || game.State.day != 1 || root.Q<Button>("end-live-button") != null) { Fail("continue watching"); yield break; }
            feature.HandleBack();
            yield return null;
            Submit(root.Q<Button>("end-live-button"));
            deadline = Time.realtimeSinceStartupAsDouble + 45;
            while (ui.HasOverlay && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (ui.HasOverlay || SaveRepository.Load().day != 2 || game.State.period != 0) { Fail("next morning save"); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"next-morning.png"));
            yield return new WaitForSecondsRealtime(.3f);
            Debug.Log("LIVE_PACKAGE_EXIT_PASS");
            Application.Quit(0);
        }
        static bool HasMessage(ScrollView chat,string text) {
            foreach (var label in chat.Query<Label>(className:"message-text").ToList()) {
                if (label.text == text && label.parent.childCount == 2 && label.parent.Q<Label>(className:"nickname") != null) return true;
            }
            return false;
        }
        void Fail(string step) { Debug.LogError("LIVE_PACKAGE_EXIT_FAIL " + step); Application.Quit(1); }
    }
}
