using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Live2D.Cubism.Framework.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;
using Newtonsoft.Json.Linq;

namespace Seabreeze
{
    public sealed class NativeLiveHost : MonoBehaviour
    {
        readonly Dictionary<Camera, int> cameraMasks = new Dictionary<Camera, int>();
        LiveFeatureController feature;
        bool previousAudioPause, previousBackground, previousUnscaled, closing, restored;

        public static NativeLiveHost Open(Transform parent, Action returnToGame, Action<string> sessionEnded = null, MemoryGameController memory = null, string storyMode = "daily", Action<JObject> sceneCommand = null)
        {
            var root = new GameObject("Z Native Live");
            root.transform.SetParent(parent, false);
            var host = root.AddComponent<NativeLiveHost>();
            host.previousAudioPause = AudioListener.pause;
            host.previousBackground = Application.runInBackground;
            host.previousUnscaled = CubismTime.UseUnscaledTime;
            AudioListener.pause = true;
            Application.runInBackground = true;
            CubismTime.UseUnscaledTime = true;
            foreach (var camera in Camera.allCameras)
            {
                host.cameraMasks[camera] = camera.cullingMask;
                camera.cullingMask &= ~(1 << 30);
            }
            host.feature = root.AddComponent<LiveFeatureController>();
            if(memory!=null){host.feature.BindCharacters=memory.Client.BeginLive;host.feature.ReleaseCharacters=memory.Client.EndLive;}
            host.feature.ExitRequested = returnToGame;
            host.feature.SessionEnded = sessionEnded;
            host.feature.SceneMode = storyMode;
            host.feature.SceneCommand = sceneCommand;
            return host;
        }

        public Task SynchronizeExit() => feature != null ? feature.Leave() : Task.CompletedTask;

        public bool HandleBack() => feature != null && feature.HandleBack();
        public void AcceptArrival() => feature?.AcceptArrival();

        public async void Close()
        {
            if (closing) return;
            closing = true;
            feature.SceneCommand = null;
            feature.ExitRequested = null;
            feature.SessionEnded = null;
            feature.StopPlayback();
            var document = GetComponent<UIDocument>();
            if (document != null) document.enabled = false;
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            RestoreGame();
            try
            {
                var end = EndSession();
                await Task.WhenAny(end, Task.Delay(1500));
            }
            finally { if (this != null) Destroy(gameObject); }
        }

        async Task EndSession()
        {
            try { await feature.Leave(); }
            catch (Exception) { /* The server expires a disconnected executor lease. */ }
        }

        void RestoreGame()
        {
            if (restored) return;
            restored = true;
            AudioListener.pause = previousAudioPause;
            Application.runInBackground = previousBackground;
            CubismTime.UseUnscaledTime = previousUnscaled;
            foreach (var entry in cameraMasks)
                if (entry.Key != null) entry.Key.cullingMask = entry.Value;
        }

        void OnDestroy() { RestoreGame(); }
    }
}
