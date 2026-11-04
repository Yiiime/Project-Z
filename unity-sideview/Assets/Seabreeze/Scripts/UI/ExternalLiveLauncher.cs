using System;
using UnityEngine;

namespace Seabreeze
{
    // Route 3: the OS browser owns the page. Unity only owns its paused game state.
    public sealed class ExternalLiveLauncher : MonoBehaviour
    {
        public const string LiveUrl = "https://z-moon-island-live.daiziqing2.chatgpt.site/";
        public bool IsOpen { get; private set; }
        public string Status { get; private set; } = "";
        public Action<string> LaunchUrl { get; set; } = Application.OpenURL;
        bool previousAudioPause;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            previousAudioPause = AudioListener.pause;
            AudioListener.pause = true;
            Retry();
        }
        public void Retry()
        {
            if (!IsOpen) return;
            try
            {
                // No embed/session flags: an external browser cannot complete a WebView handshake.
                LaunchUrl(LiveUrl);
                Status = "已请求在默认浏览器打开直播。";
            }
            catch (Exception)
            {
                Status = "未能打开浏览器。请重试，或复制网址手动打开。";
            }
            // OpenURL has no success callback. Never claim the page loaded or the live session ended.
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            AudioListener.pause = previousAudioPause;
            Status = "";
        }
        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
