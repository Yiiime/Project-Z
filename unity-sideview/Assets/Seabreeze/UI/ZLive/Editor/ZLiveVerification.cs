using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZLive.EditorTools
{
    [InitializeOnLoad]
    public static class ZLiveVerification
    {
        const string Key = "ZLive.Verify.Pending";
        static ZLiveVerification() { EditorApplication.playModeStateChanged += StateChanged; }

        [MenuItem("Seabreeze/Verify Live Z Integration")]
        public static void Run()
        {
            ZLiveModelSetup.Prepare();
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            EditorApplication.isPlaying = true;
        }

        public static void Build()
        {
            ZLiveModelSetup.Prepare();
            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, x=>x.enabled), x=>x.path),
                locationPathName = "Builds/ZLiveQA/ZLiveQA.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });
            if(report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Z Live build failed: " + report.summary.result);
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false);
            var runner = new GameObject("Live Z integration verification").AddComponent<ZLiveVerificationRunner>();
            runner.OutputDirectory = Environment.GetEnvironmentVariable("Z_LIVE_QA_DIR") ?? Path.GetFullPath("Temp/ZLiveVerification");
            runner.Completed = passed => {
                Debug.Log("LIVE_Z_VERIFICATION_" + (passed ? "PASSED" : "FAILED"));
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
                else EditorApplication.isPlaying = false;
            };
        }
    }
}
