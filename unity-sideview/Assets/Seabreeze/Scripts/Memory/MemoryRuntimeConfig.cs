using System;
using System.IO;
using UnityEngine;
namespace Seabreeze
{
    [Serializable] public sealed class MemorySettings
    {
        public bool enabled = true;
        public string serviceUrl = "http://127.0.0.1:4330";
        public float decisionInterval = 8, pairCooldown = 90, hearingDistance = 5.5f, approachTimeout = 20, conversationTimeout = 120;
        public int maxLines = 6;
    }
    public static class MemoryRuntimeConfig
    {
#if UNITY_EDITOR
        [Serializable] sealed class ServiceConnection { public string memoryUrl = ""; }

        public static string EditorConnectionFile =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/ZLive/local-connection.json"));

        public static string ReadEditorMemoryUrl(string file)
        {
            if (!File.Exists(file)) return null;
            return JsonUtility.FromJson<ServiceConnection>(File.ReadAllText(file))?.memoryUrl;
        }

        static string EditorServiceUrl(string fallback)
        {
            var file = Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE");
            var url = ReadEditorMemoryUrl(string.IsNullOrWhiteSpace(file) ? EditorConnectionFile : file);
            // Existing live-only connections use the project's configured memory port.
            return string.IsNullOrWhiteSpace(url) ? fallback : url;
        }
#endif

        static MemorySettings settings;
        public static MemorySettings Settings
        {
            get
            {
                if(settings != null) return settings;
                var asset=Resources.Load<TextAsset>("Memory/integration");
                var resolved=asset!=null?JsonUtility.FromJson<MemorySettings>(asset.text):new MemorySettings { enabled=false };
                string[] args=Environment.GetCommandLineArgs();
                if(Array.IndexOf(args,"-memorySmokeOutput")>=0)
                {
                    string saveRoot=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
                    if(!saveRoot.StartsWith("/tmp/",StringComparison.Ordinal)&&!saveRoot.StartsWith("/private/tmp/",StringComparison.Ordinal)&&!saveRoot.StartsWith(System.IO.Path.GetTempPath(),StringComparison.Ordinal))
                        throw new InvalidOperationException("Standalone smoke requires an isolated temporary save path.");
                }
                if(Array.IndexOf(args,"-memoryDisabled")>=0) resolved.enabled=false;
                int index=Array.IndexOf(args,"-memoryService");
                if(index>=0&&index+1<args.Length) resolved.serviceUrl=args[index+1];
#if UNITY_EDITOR
                else resolved.serviceUrl=EditorServiceUrl(resolved.serviceUrl);
#endif
                if(!Uri.TryCreate(resolved.serviceUrl,UriKind.Absolute,out var uri)||!uri.IsLoopback||uri.Scheme!="http")
                    throw new InvalidOperationException("Memory service must use a local HTTP address.");
                resolved.serviceUrl=resolved.serviceUrl.TrimEnd('/');
                resolved.maxLines=Mathf.Clamp(resolved.maxLines,2,10);
                settings=resolved;
                return settings;
            }
        }
        public static bool Enabled => Settings.enabled;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { settings=null; }
    }
}
