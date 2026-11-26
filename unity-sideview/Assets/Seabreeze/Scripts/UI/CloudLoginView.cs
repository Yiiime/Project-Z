using System;
using System.Net.Http;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    public sealed class CloudLoginView : MonoBehaviour
    {
        static readonly Vector2 Center = new Vector2(.5f, .5f);
        CloudGameSession session;
        Action connected;
        GameObject canvasRoot;
        TextMeshProUGUI message, code;
        Button open, retry, copy;
        bool pending;

        public void Initialize(CloudGameSession connection, Action onConnected)
        {
            session = connection; connected = onConnected;
            canvasRoot = new GameObject("Team sign-in", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 325;
            var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var root = (RectTransform)canvasRoot.transform;
            var art = Stretch("Coast", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            var config = Resources.Load<StartPageConfig>("UI/StartPage");
            art.texture = config != null ? config.background : null;
            art.color = art.texture != null ? new Color(.47f,.53f,.59f) : Ink;
            var card = Panel("Sign in", root, Center, Center, Vector2.zero, new Vector2(800, 380), Ink, true).rectTransform;
            Label("Title", card, Center, Center, new Vector2(0, 112), new Vector2(740, 64), "连接小岛", 42, Paper, TextAlignmentOptions.Center);
            message = Label("Session status", card, Center, Center, new Vector2(0, 45), new Vector2(730, 76), "", 24, Paper, TextAlignmentOptions.Center);
            code = Label("Pairing code", card, Center, Center, new Vector2(0, -34), new Vector2(440, 74), "", 52, Accent, TextAlignmentOptions.Center);
            open = UIPrimitives.Button("Open browser", card, Center, Center, new Vector2(-145,-117), new Vector2(255,58),
                "打开登录页", () => { if (session.LoginUrl != null) Application.OpenURL(session.LoginUrl); });
            copy = UIPrimitives.Button("Copy code", card, Center, Center, new Vector2(145,-117), new Vector2(255,58),
                "复制配对码", () => { if (session.PairingCode != null) GUIUtility.systemCopyBuffer = session.PairingCode; });
            retry = UIPrimitives.Button("Retry", card, Center, Center, new Vector2(0,-116), new Vector2(260,58),
                "重新连接", Begin);
            Begin();
        }

        void Begin()
        {
            if (pending) return;
            pending = true;
            message.text = "正在恢复团队登录…";
            code.text = "";
            open.gameObject.SetActive(false); copy.gameObject.SetActive(false); retry.gameObject.SetActive(false);
            _ = Connect();
        }

        async Task Connect()
        {
            try { await session.LoginAsync(); }
            catch (Exception error)
            {
                if (!this) return;
                pending = false;
                message.text = error is OperationCanceledException ? "登录等待已结束，请重新连接。"
                    : error is HttpRequestException ? "无法连接小岛服务，请检查网络后重试。"
                    : error is InvalidOperationException ? error.Message : "团队登录暂时不可用，请稍后重试。";
                code.text = ""; open.gameObject.SetActive(false); copy.gameObject.SetActive(false); retry.gameObject.SetActive(true);
                return;
            }
            if (!this) return;
            pending = false; canvasRoot.SetActive(false);
            connected(); Destroy(this);
        }

        void Update()
        {
            if (!pending || session.PairingCode == null || !code || code.text == session.PairingCode) return;
            message.text = "在浏览器中输入下方配对码，再使用团队 GitHub 账号登录。";
            code.text = session.PairingCode;
            open.gameObject.SetActive(true); copy.gameObject.SetActive(true);
        }

        void OnDestroy() { if (canvasRoot) Destroy(canvasRoot); }
    }
}
