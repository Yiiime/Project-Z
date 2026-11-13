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

        static TeamModelStartup() { EditorApplication.playModeStateChanged += Changed; }

        static void Changed(PlayModeStateChange state)
        {
            if(state != PlayModeStateChange.EnteredPlayMode || Application.isBatchMode) return;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string config = Environment.GetEnvironmentVariable("Z_TEAM_MODEL_CONFIG");
            if(string.IsNullOrEmpty(config)) config = Path.Combine(root, "Tools/team-model/client.json");
            try
            {
                if(!File.Exists(config) || !JsonUtility.FromJson<TeamConfig>(File.ReadAllText(config)).enabled) return;
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
