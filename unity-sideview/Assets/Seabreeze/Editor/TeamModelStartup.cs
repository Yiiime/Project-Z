using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class TeamModelStartup
    {
        [Serializable] sealed class TeamConfig { public bool enabled; }

        const string LiveFileVariable = "Z_LIVE_CONNECTION_FILE";
        const string OwnedLiveFile = "Seabreeze.TeamModelStartup.LiveFile";

        static TeamModelStartup() { EditorApplication.playModeStateChanged += Changed; }

#if UNITY_EDITOR_OSX
        const string CloudMenu = "Seabreeze/服务连接/团队云端（默认）";
        const string LocalMenu = "Seabreeze/服务连接/本地开发";

        [MenuItem(CloudMenu, false, 100)]
        static void SelectCloud() => CloudGameSession.EditorPrefersLocal = false;

        [MenuItem(LocalMenu, false, 101)]
        static void SelectLocal() => CloudGameSession.EditorPrefersLocal = true;

        [MenuItem(CloudMenu, true)]
        static bool ValidateCloud() => ValidateModeMenu(CloudMenu, false);

        [MenuItem(LocalMenu, true)]
        static bool ValidateLocal() => ValidateModeMenu(LocalMenu, true);

        static bool ValidateModeMenu(string menuPath, bool local)
        {
            bool configurable = CloudGameSession.CanChooseEditorMode;
            Menu.SetChecked(menuPath, configurable && CloudGameSession.EditorPrefersLocal == local);
            return configurable && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying;
        }
#endif

        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                var owned = SessionState.GetString(OwnedLiveFile, "");
                if (owned.Length > 0 && Environment.GetEnvironmentVariable(LiveFileVariable) == owned)
                    Environment.SetEnvironmentVariable(LiveFileVariable, null);
                SessionState.EraseString(OwnedLiveFile);
                return;
            }
            if(state != PlayModeStateChange.EnteredPlayMode || Application.isBatchMode || CloudGameSession.Enabled) return;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-memoryService") >= 0 ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LiveFileVariable))) return;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string config = Environment.GetEnvironmentVariable("Z_TEAM_MODEL_CONFIG");
            if(string.IsNullOrEmpty(config)) config = Path.Combine(root, "Tools/team-model/client.json");
            try
            {
                if (!string.IsNullOrWhiteSpace(MemoryRuntimeConfig.ReadEditorMemoryUrl(MemoryRuntimeConfig.EditorConnectionFile))) return;
                if(!File.Exists(config) || !JsonUtility.FromJson<TeamConfig>(File.ReadAllText(config)).enabled) return;
                string liveFile = Path.Combine(root,"UserSettings/ZLive/team-connection.json");
                Environment.SetEnvironmentVariable(LiveFileVariable,liveFile);
                SessionState.SetString(OwnedLiveFile,liveFile);
                string node = FindNode();
                bool windows = Application.platform == RuntimePlatform.WindowsEditor;
                string script = Path.Combine(root, windows ? "Tools/MemoryLab/launcher.mjs" : "Tools/MemoryLab/start.sh");
                var start = new System.Diagnostics.ProcessStartInfo {
                    FileName = windows ? node : "/bin/sh",
                    Arguments = "\"" + script.Replace("\"", "\\\"") + "\"" + (windows ? "" : " --background"),
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
                };
                start.EnvironmentVariables["Z_MEMORY_NODE"] = node;
                using(var process = System.Diagnostics.Process.Start(start)) { }
            }
            catch(Exception error) { Debug.LogWarning("团队对话服务未启动：" + error.Message); }
        }

        static string FindNode()
        {
            string specified = Environment.GetEnvironmentVariable("Z_MEMORY_NODE");
            if(!string.IsNullOrEmpty(specified)) return specified;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach(string candidate in new[]{
                Path.Combine(home,".project-z/bin/node"), Path.Combine(home,".local/bin/node"),
                "/opt/homebrew/bin/node", "/usr/local/bin/node"
            }) if(File.Exists(candidate)) return candidate;
            return "node";
        }
    }
}
