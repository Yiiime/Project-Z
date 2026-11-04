using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Seabreeze
{
    // Optional SDK boundary: teammates without the evaluation plugin can still compile/play.
    public interface ILiveWebSurface : IDisposable
    {
        event Action<string> Message;
        event Action Failed;
        Task Initialize(Transform parent);
        void Load(string url);
        void Send(string json);
        void SetMuted(bool muted);
    }

    public sealed class LiveWebViewBridge : MonoBehaviour
    {
        public const string LiveUrl = "https://z-moon-island-live.daiziqing2.chatgpt.site/";
        public bool IsOpen { get; private set; }
        public bool Ready { get; private set; }
        public string Status { get; private set; } = "";
        public bool DebugMuted { get; private set; } = true;
        public string LastSkillEvent { get; private set; } = "";
        public Func<ILiveWebSurface> SurfaceFactory { get; set; } = () => new VuplexLiveSurface();
        private ILiveWebSurface surface;
        private GameObject canvasRoot;
        private string sessionId;
        private int generation;
        private float openedAt;
        private bool focused = true;

        [Serializable] private sealed class Envelope
        {
            public string channel = "project-z-live";
            public int version = 1;
            public string sessionId, type;
            public string kind = "", phase = "", taskId = "";
            public bool paused, muted;
        }

        public async Task Open(Transform parent = null)
        {
            if (IsOpen) return;
            IsOpen = true; Ready = false; focused = Application.isFocused;
            Status = "正在打开 Z 的直播…";
            LastSkillEvent = "";
            sessionId = Guid.NewGuid().ToString("N");
            openedAt = Time.realtimeSinceStartup;
            var ticket = ++generation;
            try
            {
                canvasRoot = new GameObject("Z Live WebView", typeof(RectTransform));
                if (parent != null)
                {
                    canvasRoot.transform.SetParent(parent, false);
                    var rect = (RectTransform)canvasRoot.transform;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                }
                else
                {
                    var canvas = canvasRoot.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 101;
                }
                surface = SurfaceFactory();
                var current = surface;
                current.Message += json => { if (IsOpen && ticket == generation) Receive(json); };
                current.Failed += () => { if (IsOpen && ticket == generation) Fail("直播加载失败，请关闭后重试。"); };
                await current.Initialize(canvasRoot.transform);
                if (!IsOpen || ticket != generation) return;
                current.SetMuted(DebugMuted);
                current.Load(BuildUrl(PreviewUrl(), sessionId));
            }
            catch (Exception error)
            {
                if (IsOpen && ticket == generation)
                    Fail(error is NotSupportedException ? "尚未安装桌面 WebView 插件；导入试用包后重试。" : "直播容器未能启动，请检查插件与 Unity 日志。");
                Debug.LogWarning("Z live initialization: " + error.GetType().Name);
            }
        }

        private static string PreviewUrl()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Local preview only; never accept arbitrary remote origins or credentials.
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-z-live-preview");
            if (index >= 0 && index + 1 < args.Length && Uri.TryCreate(args[index + 1], UriKind.Absolute, out var url)
                && url.Scheme == "http" && url.Host == "127.0.0.1" && url.AbsolutePath == "/" && string.IsNullOrEmpty(url.UserInfo))
                return url.GetLeftPart(UriPartial.Path);
#endif
            return LiveUrl;
        }

        public static string BuildUrl(string url, string id) => url + "?embed=unity&bridge=1&session=" + Uri.EscapeDataString(id);

        private void Receive(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 4096) return;
            Envelope message;
            try { message = JsonUtility.FromJson<Envelope>(json); } catch { return; }
            if (message == null || message.channel != "project-z-live" || message.version != 1 || message.sessionId != sessionId) return;
            if (message.type == "ready" && !Ready)
            {
                Ready = true; Status = "Z 的直播 · 静音调试";
                Send("start");
            }
            else if (message.type == "closeRequested") Close();
            else if (Ready && message.type == "skillState" &&
                new[] { "tarot", "drawing", "singing", "voice-performance" }.Contains(message.kind) &&
                !string.IsNullOrEmpty(message.phase) && message.phase.Length <= 40)
                LastSkillEvent = message.kind + ":" + message.phase;
            // Presentation receipts never mutate inventory, relationships, time or saves.
        }

        private void Send(string type)
        {
            if (surface == null) return;
            surface.Send(JsonUtility.ToJson(new Envelope { sessionId = sessionId, type = type, paused = !focused, muted = DebugMuted }));
        }
        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (IsOpen && Ready) Send("visibility");
        }
        private void Update()
        {
            if (IsOpen && surface != null && !Ready && Time.realtimeSinceStartup - openedAt > 45)
                Fail("直播连接超时或网页尚未部署接入接口，请关闭后重试。");
        }
        private void Fail(string message)
        {
            generation++; Ready = false; Status = message;
            Release(); // Keep a closable error panel, but no hidden renderer/audio/network activity.
        }
        private void Release()
        {
            var current = surface; surface = null;
            try { current?.Dispose(); } catch { Debug.LogWarning("Z live cleanup failed."); }
            if (canvasRoot != null) Destroy(canvasRoot);
            canvasRoot = null;
        }
        public void Close()
        {
            if (!IsOpen) return;
            try { Send("close"); } catch { /* Disposal is authoritative even if JS has stopped. */ }
            IsOpen = false; Ready = false; generation++;
            Release(); Status = "";
        }
        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }

    // Reflection intentionally keeps the evaluation SDK optional. Supported target: desktop Mono.
    // Import Vuplex + its uGUI dependency locally; no SDK binaries or expiring licence go into Git.
    internal sealed class VuplexLiveSurface : ILiveWebSurface
    {
        public event Action<string> Message;
        public event Action Failed;
        private Component prefab;
        private object web;
        private Type api;
        private GameObject ownedEventSystem;
        private bool disposed;
        private readonly System.Collections.Generic.List<(EventInfo info, Delegate handler)> subscriptions = new();
        private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        private static object Read(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj) ?? obj.GetType().GetField(name)?.GetValue(obj);
        private static void Set(object obj, string name, object value)
        {
            var field = obj.GetType().GetField(name);
            if (field != null) field.SetValue(obj, value);
            else obj.GetType().GetProperty(name).SetValue(obj, value);
        }
        public async Task Initialize(Transform parent)
        {
            var type = Find("Vuplex.WebView.CanvasWebViewPrefab");
            var raycaster = Find("UnityEngine.UI.GraphicRaycaster");
            var events = Find("UnityEngine.EventSystems.EventSystem");
            var input = Find("UnityEngine.InputSystem.UI.InputSystemUIInputModule") ?? Find("UnityEngine.EventSystems.StandaloneInputModule");
            if (type == null || raycaster == null || events == null || input == null) throw new NotSupportedException();
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas.GetComponent(raycaster) == null) canvas.gameObject.AddComponent(raycaster);
            if (UnityEngine.Object.FindAnyObjectByType(events) == null)
                ownedEventSystem = new GameObject("Z Live Input", events, input);
            prefab = (Component)type.GetMethod("Instantiate", Type.EmptyTypes).Invoke(null, null);
            prefab.transform.SetParent(parent, false);
            var rect = (RectTransform)prefab.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = new Vector2(0, -64);
            Set(prefab, "Resolution", 1f);
            Set(prefab, "PixelDensity", 2f);
            Set(prefab, "AudioSourceEnabled", true);
            Set(prefab, "InitialUrl", "");
            await (Task)type.GetMethod("WaitUntilInitialized").Invoke(prefab, null);
            if (disposed) return;
            web = Read(prefab, "WebView");
            api = Find("Vuplex.WebView.IWebView");
            if (web == null || api == null) throw new InvalidOperationException();
            Subscribe("MessageEmitted", nameof(OnMessage));
            Subscribe("LoadFailed", nameof(OnFailure));
            Subscribe("Terminated", nameof(OnFailure));
            Subscribe("UrlChanged", nameof(OnUrlChanged));
            // Fail closed: no system-audio fallback may leak sound during silent debugging.
            if (!(Read(prefab, "AudioSource") is AudioSource)) throw new NotSupportedException();
            SetMuted(true);
        }
        private void Subscribe(string name, string method)
        {
            var info = api.GetEvent(name);
            var handler = Delegate.CreateDelegate(info.EventHandlerType, this, GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance));
            info.AddEventHandler(web, handler); subscriptions.Add((info, handler));
        }
        private void OnMessage(object sender, EventArgs args) { if (!disposed) Message?.Invoke(Read(args, "Value") as string); }
        private void OnFailure(object sender, EventArgs args) { if (!disposed) Failed?.Invoke(); }
        private void OnUrlChanged(object sender, EventArgs args)
        {
            var url = Read(web, "Url") as string;
            if (string.IsNullOrEmpty(url) || url == "about:blank") return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !((uri.Scheme == "https" && uri.Host == "z-moon-island-live.daiziqing2.chatgpt.site" && uri.IsDefaultPort) ||
                  (uri.Scheme == "http" && uri.Host == "127.0.0.1"))) Failed?.Invoke();
        }
        public void Load(string url) => api.GetMethod("LoadUrl", new[] { typeof(string) }).Invoke(web, new object[] { url });
        public void Send(string json) { if (!disposed && web != null) api.GetMethod("PostMessage").Invoke(web, new object[] { json }); }
        public void SetMuted(bool muted) { if (prefab != null && Read(prefab, "AudioSource") is AudioSource audio) audio.mute = muted; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SetMuted(true);
            if (web != null) foreach (var pair in subscriptions) pair.info.RemoveEventHandler(web, pair.handler);
            subscriptions.Clear();
            if (prefab != null) prefab.GetType().GetMethod("Destroy", Type.EmptyTypes).Invoke(prefab, null);
            if (ownedEventSystem != null) UnityEngine.Object.Destroy(ownedEventSystem);
            prefab = null; web = null;
        }
    }
}
