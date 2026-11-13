using System;
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
        static MemorySettings settings;
        public static MemorySettings Settings
        {
            get
            {
                if(settings != null) return settings;
                var asset=Resources.Load<TextAsset>("Memory/integration");
                settings=asset!=null?JsonUtility.FromJson<MemorySettings>(asset.text):new MemorySettings { enabled=false };
                string[] args=Environment.GetCommandLineArgs();
                if(Array.IndexOf(args,"-memorySmokeOutput")>=0)
                {
                    string saveRoot=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
                    if(!saveRoot.StartsWith("/tmp/",StringComparison.Ordinal)&&!saveRoot.StartsWith("/private/tmp/",StringComparison.Ordinal)&&!saveRoot.StartsWith(System.IO.Path.GetTempPath(),StringComparison.Ordinal))
                        throw new InvalidOperationException("Standalone smoke requires an isolated temporary save path.");
                }
                if(Array.IndexOf(args,"-memoryDisabled")>=0) settings.enabled=false;
                int index=Array.IndexOf(args,"-memoryService");
                if(index>=0&&index+1<args.Length) settings.serviceUrl=args[index+1];
                else
                {
                    // Finder launches do not preserve --args. The game launcher
                    // records its verified loopback service for this demo build.
                    string endpoint=LastEndingService();
                    if(!string.IsNullOrEmpty(endpoint))settings.serviceUrl=endpoint;
                }
                if(!Uri.TryCreate(settings.serviceUrl,UriKind.Absolute,out var uri)||!uri.IsLoopback||uri.Scheme!="http")
                    throw new InvalidOperationException("Memory service must use a local HTTP address.");
                settings.serviceUrl=settings.serviceUrl.TrimEnd('/');
                settings.maxLines=Mathf.Clamp(settings.maxLines,2,10);
                return settings;
            }
        }
        public static bool Enabled => Settings.enabled;
        [Serializable] sealed class EndingService { public string serviceUrl; }
        static string LastEndingService()
        {
            if(Application.identifier!="com.projectz.islandending")return null;
            string file=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),".project-z","ending-demo","game-service.json");
            try
            {
                if(!System.IO.File.Exists(file))return null;
                string value=JsonUtility.FromJson<EndingService>(System.IO.File.ReadAllText(file))?.serviceUrl;
                return Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.IsLoopback&&uri.Scheme=="http"?value:null;
            }
            catch{return null;}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { settings=null; }
    }
}
