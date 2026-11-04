#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class ExternalLiveVerification
    {
        const string Running = "Seabreeze.LiveVerification";
        static ExternalLiveVerification()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Seabreeze.LivePreview", false))
                    new GameObject("Live phone preview").AddComponent<LivePhonePreview>();
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Live lifecycle verification").AddComponent<ExternalLiveVerificationRunner>();
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
            SaveRepository.VerificationDirectory = Path.GetFullPath("TestResults/ExternalLive/Save-" + Guid.NewGuid().ToString("N"));
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
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-z-test-external-browser") >= 0)
            {
                yield return null;
                var button = GameObject.Find("Open Z Live").GetComponent<UnityEngine.UI.Button>();
                button.onClick.Invoke();
                Debug.Log("EXTERNAL LIVE PREVIEW: actual phone button invoked; OS browser requested");
            }
        }
    }
    public sealed class ExternalLiveVerificationRunner : MonoBehaviour
    {
        [Serializable] public class Report { public List<string> passed = new(); public List<string> failed = new(); }
        readonly Report report = new();
        void Check(bool ok, string name) { (ok ? report.passed : report.failed).Add(name); }
        IEnumerator Start()
        {
            yield return null;
            try { Verify(); } catch(Exception error) { report.failed.Add(error.ToString()); }
            Directory.CreateDirectory("TestResults/ExternalLive");
            File.WriteAllText("TestResults/ExternalLive/lifecycle.json",JsonUtility.ToJson(report,true));
            Debug.Log($"LIVE VERIFICATION: {report.passed.Count} passed / {report.failed.Count} failed");
            ExternalLiveVerification.Finish(report.failed.Count == 0 ? 0 : 1);
        }
        void Verify()
        {
            var ui = GameUI.Instance;
            var session = GameSession.Instance;
            session.Dialogue.Cancel(); ui.CloseOverlay();
            var saved = JsonUtility.ToJson(session.State);
            var position = WorldDirector.Instance.Player.transform.position;
            var originalAudioPause = AudioListener.pause;
            AudioListener.pause = false;
            var launcher = ui.gameObject.AddComponent<ExternalLiveLauncher>();
            var urls = new List<string>();
            launcher.LaunchUrl = url => urls.Add(url);
            typeof(GameUI).GetMethod("OpenOverlay",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{"live"});
            Check(ui.HasOverlay && session.InputBlocked && Time.timeScale==0,"Opening live freezes world and blocks gameplay input");
            Check(launcher.IsOpen && AudioListener.pause,"Game audio is paused while browser visit is active");
            Check(urls.Count==1 && urls[0]==ExternalLiveLauncher.LiveUrl && !urls[0].Contains("embed="),"Launches ordinary HTTPS live page without WebView handshake flags");
            launcher.Open(); Check(urls.Count==1,"Duplicate Open does not create extra browser tabs");
            launcher.Retry(); Check(urls.Count==2,"Explicit retry launches another browser request");
            Check(JsonUtility.ToJson(session.State)==saved,"External visit cannot mutate game facts");
            ui.CloseOverlay();
            Check(!launcher.IsOpen && !session.InputBlocked && Time.timeScale==1 && !AudioListener.pause,"Continue restores gameplay and previous audio state");
            Check(WorldDirector.Instance.Player.transform.position==position,"Player returns at original position");
            launcher.Close(); launcher.Retry(); Check(urls.Count==2,"Closed visit cannot retry or alter browser state");
            AudioListener.pause=true; launcher.Open(); launcher.Close();
            Check(AudioListener.pause,"Previously paused audio remains paused after return");
            launcher.LaunchUrl = url => throw new InvalidOperationException("Simulated launch failure");
            launcher.Open(); Check(launcher.IsOpen && launcher.Status.Contains("未能"),"Launch failure remains recoverable with a visible explanation");
            launcher.Close(); AudioListener.pause=originalAudioPause;
            Check(JsonUtility.ToJson(session.State)==saved,"Retry and failure preserve save state");
        }
    }
}
#endif
