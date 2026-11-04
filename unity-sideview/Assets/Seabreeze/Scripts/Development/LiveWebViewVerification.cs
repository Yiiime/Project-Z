#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class LiveWebViewVerification
    {
        const string Running = "Seabreeze.LiveVerification";
        static LiveWebViewVerification()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Seabreeze.LivePreview", false))
                    new GameObject("Live phone preview").AddComponent<LivePhonePreview>();
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Live lifecycle verification").AddComponent<LiveWebViewVerificationRunner>();
            };
        }
        public static void RunBatch()
        {
            SessionState.SetBool(Running, true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            EditorApplication.EnterPlaymode();
        }
        public static void OpenPhonePreview()
        {
            SessionState.SetBool("Seabreeze.LivePreview", true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            EditorApplication.EnterPlaymode();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Isolate()
        {
            if (!SessionState.GetBool(Running, false) && !SessionState.GetBool("Seabreeze.LivePreview", false)) return;
            SaveRepository.VerificationDirectory = Path.GetFullPath("TestResults/LiveWebView/Save-" + Guid.NewGuid().ToString("N"));
            AudioListener.volume = 0;
        }
        public static void Finish(int result)
        {
            SessionState.SetBool(Running, false);
            EditorApplication.Exit(result);
        }
    }
    public sealed class LivePhonePreview : MonoBehaviour
    {
        IEnumerator Start()
        {
            yield return null;
            SessionState.SetBool("Seabreeze.LivePreview", false);
            GameSession.Instance.Dialogue.Cancel();
            typeof(GameUI).GetMethod("OpenOverlay", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(GameUI.Instance,new object[]{"phone"});
            AudioListener.volume=0;
        }
    }
    public sealed class LiveWebViewVerificationRunner : MonoBehaviour
    {
        [Serializable] public class Report { public List<string> passed = new(); public List<string> failed = new(); }
        readonly Report report = new();
        void Check(bool ok, string name) { (ok ? report.passed : report.failed).Add(name); }
        class FakeSurface : ILiveWebSurface
        {
            public event Action<string> Message;
            public event Action Failed;
            public bool muted, disposed;
            public string url;
            public List<string> sent = new();
            public TaskCompletionSource<bool> pending;
            public Task Initialize(Transform parent) => pending?.Task ?? Task.CompletedTask;
            public void Load(string value) { url = value; }
            public void Send(string json) { sent.Add(json); }
            public void SetMuted(bool value) { muted = value; }
            public void Dispose() { disposed = true; }
            public void Emit(string type, string id = null)
            {
                id ??= new Uri(url).Query.Split("session=")[1];
                Message?.Invoke("{\"channel\":\"project-z-live\",\"version\":1,\"sessionId\":\""+id+"\",\"type\":\""+type+"\",\"kind\":\"singing\",\"phase\":\"completed\"}");
            }
            public void Fail() => Failed?.Invoke();
        }
        IEnumerator Start()
        {
            yield return null;
            var work = Verify();
            while (!work.IsCompleted) yield return null;
            if (work.IsFaulted) report.failed.Add(work.Exception.ToString());
            Directory.CreateDirectory("TestResults/LiveWebView");
            File.WriteAllText("TestResults/LiveWebView/lifecycle.json",JsonUtility.ToJson(report,true));
            Debug.Log($"LIVE VERIFICATION: {report.passed.Count} passed / {report.failed.Count} failed");
            LiveWebViewVerification.Finish(report.failed.Count == 0 ? 0 : 1);
        }
        async Task Verify()
        {
            var ui = GameUI.Instance;
            var session = GameSession.Instance;
            session.Dialogue.Cancel(); ui.CloseOverlay();
            var saved = JsonUtility.ToJson(session.State);
            var position = WorldDirector.Instance.Player.transform.position;
            var bridge = ui.gameObject.AddComponent<LiveWebViewBridge>();
            var fake = new FakeSurface(); bridge.SurfaceFactory = () => fake;
            typeof(GameUI).GetMethod("OpenOverlay",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{"live"});
            Check(ui.HasOverlay && session.InputBlocked && Time.timeScale == 0 && bridge.IsOpen,"Opening live blocks world input and freezes gameplay");
            Check(fake.muted && fake.url.StartsWith(LiveWebViewBridge.LiveUrl + "?embed=unity"),"Muted before navigating to approved live origin");
            fake.Emit("ready",new string('b',32));
            Check(!bridge.Ready && fake.sent.Count == 0,"Wrong session cannot start the live room");
            fake.Emit("ready");
            Check(bridge.Ready && fake.sent.Count == 1,"Matching ready handshakes once");
            fake.Emit("skillState");
            Check(bridge.LastSkillEvent == "singing:completed" && JsonUtility.ToJson(session.State)==saved,"Skill receipt is observable but never changes game facts");
            ui.CloseOverlay();
            Check(fake.disposed && !bridge.IsOpen && !session.InputBlocked && Time.timeScale==1,"Closing disposes web renderer and restores exploration");
            Check(WorldDirector.Instance.Player.transform.position==position,"Player remains at original position");
            fake.Emit("ready"); Check(!bridge.Ready,"Closed surface cannot revive its session");
            var late = new FakeSurface{pending=new TaskCompletionSource<bool>()}; bridge.SurfaceFactory=()=>late;
            var opening=bridge.Open(); bridge.Close(); late.pending.SetResult(true); await opening;
            Check(late.disposed && late.url==null && !bridge.IsOpen,"Close during initialization never loads a late page");
            bridge.SurfaceFactory=()=>throw new NotSupportedException();
            _=bridge.Open(); Check(bridge.IsOpen && bridge.Status.Contains("插件"),"Missing SDK leaves a closable explanatory panel"); bridge.Close();
            Check(JsonUtility.ToJson(session.State)==saved,"Failure and retry preserve save state");
        }
    }
}
#endif
