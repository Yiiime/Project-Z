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
            var runner = new GameObject("Packaged exit verification");
            DontDestroyOnLoad(runner);
            runner.AddComponent<LivePackageVerification>();
        }
        static void Submit(Button button) {
            button.Focus();
            using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
        }
        IEnumerator Start() {
            yield return new WaitForSecondsRealtime(1);
            var ui = FindFirstObjectByType<GameUI>();
            var game = GameSession.Instance;
            bool verifyDay=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-daytime-recap")>=0;
            if(verifyDay && game && game.State.day==1 && game.State.period==0) {
                game.Memory.CommitLine("verify-daytime","player","z","测试私话：蓝信封不能在直播里说。","","private_player_chat");
                for(int i=0;i<3;i++)game.Clock.Advance();
            }
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
            if(verifyDay) {
                deadline=Time.realtimeSinceStartupAsDouble+35;
                bool heard=false;
                while(!heard && Time.realtimeSinceStartupAsDouble<deadline) {
                    var current=(JObject)typeof(LiveFeatureController).GetField("state",Member).GetValue(feature);
                    foreach(var ev in (JArray)current["publicEvents"]) {
                        string kind=(string)ev["type"], text=(string)ev["text"];
                        if(text.Contains("蓝信封") || (!heard && (kind=="npc.message" || kind=="player.message"))) { Fail("recap privacy or ordering"); yield break; }
                        if(kind=="speech.completed" && !heard) {
                            if(!text.Contains("上午") || !text.Contains("朋友")) { Fail("missing daytime opening"); yield break; }
                            heard=true;
                        }
                    }
                    yield return null;
                }
                if(!heard) { Fail("no unprompted host speech"); yield break; }
                Debug.Log("LIVE_PACKAGE_DAYTIME_RECAP_PASS");
            }
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
            if (Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-live-audience") >= 0) {
                var names = new HashSet<string>{"晚风来信","月亮邮差","海盐汽水","橘子浮标","倒带一格","困困豆","听雨同学","烤面包边"};
                deadline = Time.realtimeSinceStartupAsDouble + 65;
                while (names.Count > 0 && Time.realtimeSinceStartupAsDouble < deadline) {
                    foreach (var label in chat.Query<Label>(className:"nickname").ToList())
                        if (label.worldBound.width > 0) names.Remove(label.text);
                    yield return null;
                }
                if (names.Count != 0 || chat.Query<Label>(className:"viewer-initial").ToList().Count < 5) {
                    Fail("audience rows or avatars: "+string.Join(",",names)); yield break;
                }
                Debug.Log("LIVE_PACKAGE_EIGHT_VIEWERS_PASS");
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
            if (Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-live-memory") >= 0) {
                deadline=Time.realtimeSinceStartupAsDouble+25;
                while(!game.Memory.Client.Synchronized && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                var saved=SaveRepository.Load();
                var cp=JObject.Parse(saved.memory.checkpoint);
                var publicRows=(JArray)cp["broadcast"]["entries"];
                foreach(string line in lines) if(!System.Linq.Enumerable.Any(publicRows,e=>(string)e["text"]==line&&(int)e["day"]==1)) {
                    Fail("missing first-night memory");yield break;
                }
                game.LoadSavedGame();
                yield return null;
                deadline=Time.realtimeSinceStartupAsDouble+25;
                while((!game.Memory.Client.Synchronized || WorldDirector.Instance.IsTransitioning) && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                typeof(GameUI).GetMethod("ToggleOverlay",Member).Invoke(ui,new object[]{"phone"});
                yield return new WaitForSecondsRealtime(1);
                GameObject.Find("Phone app live").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                yield return new WaitForSecondsRealtime(1);
                feature=FindFirstObjectByType<LiveFeatureController>();
                deadline=Time.realtimeSinceStartupAsDouble+20;
                while(feature && !(bool)connected.GetValue(feature) && Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                if(!feature || !(bool)connected.GetValue(feature)){Fail("second room");yield break;}
                root=feature.GetComponent<UIDocument>().rootVisualElement;
                root.Q<TextField>(className:"chat-input").value="昨天我在直播间说了什么？";
                Submit((Button)type.GetField("sendButton",Member).GetValue(feature));
                deadline=Time.realtimeSinceStartupAsDouble+25;
                bool recalled=false;
                while(!recalled && Time.realtimeSinceStartupAsDouble<deadline){
                    var current=(JObject)type.GetField("state",Member).GetValue(feature);
                    foreach(var ev in (JArray)current["publicEvents"])
                        if((string)ev["type"]=="speech.completed" && ((string)ev["text"]).Contains(lines[0]))recalled=true;
                    yield return null;
                }
                if(!recalled){Fail("broadcast recall after reload");yield break;}
                Debug.Log("LIVE_PACKAGE_MEMORY_PASS");
            }
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
