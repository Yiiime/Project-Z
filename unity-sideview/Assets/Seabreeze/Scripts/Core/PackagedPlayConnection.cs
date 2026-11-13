using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Seabreeze
{
    // The app and launcher share one identity/save folder. Provider credentials
    // remain in the host; the app only starts its bundled local relay.
    public static class PackagedPlayConnection
    {
        [Serializable] sealed class Connection { public string memoryUrl; }
        public static string PackageRoot { get; private set; }
        static string runtime, script, node;
        public static string Error { get; private set; }
        public static void Configure()
        {
            PackageRoot = runtime = script = node = null;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            bool lan = File.Exists(Path.Combine(root, "connection-config.json"));
            string entry = Path.Combine(root, lan ? "Tools/LanPlay/start-client.mjs" : "Tools/start-day01-playtest.mjs");
            if (!File.Exists(entry)) return;
            // Explicit test/developer launches keep their isolated configuration.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-memoryService") >= 0) return;
            PackageRoot = root; script = entry;
            runtime = Path.Combine(root, lan ? ".local/lan-client" : ".local/day01");
            var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            node = Path.Combine(root, lan ? "Runtime/node-" + (architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64") : "Runtime/node");
            Environment.SetEnvironmentVariable("Z_MEMORY_SAVE_ROOT", Path.Combine(runtime, "saves"));
#endif
        }
        public static IEnumerator Prepare()
        {
            Error = null;
            if (PackageRoot == null) yield break;
            Process process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo {
                    FileName = node, Arguments = "\"" + script.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" --check",
                    WorkingDirectory = PackageRoot, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                });
                // Drain bounded launcher diagnostics without exposing provider data.
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
            }
            catch { Error = "连接程序未能启动。请完整解压试玩文件夹，并允许 macOS 打开其中的应用。"; }
            if (Error != null) { process?.Dispose(); yield break; }
            float deadline = Time.realtimeSinceStartup + 50;
            while (!process.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            try
            {
                if (!process.HasExited) { process.Kill(); throw new IOException("connection_timeout"); }
                if (process.ExitCode != 0) throw new IOException("connection_failed");
                var connection = JsonUtility.FromJson<Connection>(File.ReadAllText(Path.Combine(runtime, "game-connection.json")));
                if (!Uri.TryCreate(connection.memoryUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http")
                    throw new IOException("invalid_connection");
                MemoryRuntimeConfig.Settings.serviceUrl = connection.memoryUrl.TrimEnd('/');
                Environment.SetEnvironmentVariable("Z_LIVE_CONNECTION_FILE", Path.Combine(runtime, "connection.json"));
            }
            catch { Error = "暂时连接不到小岛主机。请确认主机已启动、两台电脑在同一局域网，然后再次点击开始或继续。你的进度已保留。"; }
            finally { process.Dispose(); }
        }
    }
}
