using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    /// <summary>Presentation-only title page. Bootstrap owns saves, gameplay and scene lifetime.</summary>
    public sealed class StartPageView : MonoBehaviour
    {
        CanvasGroup group, menuGroup;
        RectTransform root, menu;
        RawImage background;
        StartPageAtmosphere atmosphere;
        StartPageConfig config;
        Texture2D gradient;
        TMP_FontAsset titleFont;
        GameObject modal;
        TextMeshProUGUI status, notice;
        Button primary;
        Action<bool> enter;
        bool hasSave, busy, dismissed, confirmFresh, motion = true;
        int lastModalClosedFrame = -1;
        float elapsed;
        Button soundButton, motionButton;
        static readonly Color White = new Color(.95f, .96f, .94f);
        static readonly Color Quiet = new Color(.76f, .83f, .85f);
        static readonly Color Warm = new Color(.96f, .82f, .60f);

        public void Initialize(bool canContinue, SaveData preview, Action<bool> enterGame)
        {
            hasSave = canContinue; enter = enterGame;
            config = Resources.Load<StartPageConfig>("UI/StartPage");
            var go = new GameObject("Island start page", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            go.transform.SetParent(transform, false); root = (RectTransform)go.transform;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 320;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            group = go.GetComponent<CanvasGroup>();
            background = Stretch("Painted coast", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            background.texture = config != null ? config.background : Resources.Load<Texture2D>("Art/street_day");
            background.raycastTarget = true;
            atmosphere = Stretch("Sea light and distant birds", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<StartPageAtmosphere>();
            atmosphere.Sea = config != null ? config.sea : new Rect(.05f, .32f, .40f, .17f);
            atmosphere.Sky = config != null ? config.sky : new Rect(.10f, .68f, .45f, .19f);
            var shade = Stretch("Soft coastal vignette", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            gradient = MakeGradient(config != null ? config.shade : new Color(.045f, .10f, .17f, .76f));
            shade.texture = gradient; shade.raycastTarget = false;

            var small = Label("Edition", root, Vector2.up, Vector2.up, new Vector2(64, -44), new Vector2(610, 30),
                "Z  /  A STORY OF EVERYDAY CONNECTIONS", 13, Quiet);
            small.characterSpacing = 3;
            menu = Rect("Title and invitations", root, new Vector2(.085f, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(650, 780));
            menuGroup = menu.gameObject.AddComponent<CanvasGroup>(); menuGroup.alpha = 0;
            Line(menu, Vector2.up, Vector2.up, new Vector2(0, -15), new Vector2(34, 1), Warm);
            var eyebrow = Label("Chapter", menu, Vector2.up, Vector2.up, new Vector2(50, 0), new Vector2(550, 32), "一封写给慢生活的信", 18, Warm);
            eyebrow.characterSpacing = 4;
            var name = Label("Game name", menu, Vector2.up, Vector2.up, new Vector2(-3, -62), new Vector2(620, 90), "来自月亮的", 62, White);
            name.characterSpacing = 5;
            var z = Label("Z", menu, Vector2.up, Vector2.up, new Vector2(-5, -133), new Vector2(190, 252), "Z", 230, White);
            var titleSource = Resources.Load<Font>("UI/FinalKit/BodoniModa");
            if (titleSource != null)
            {
                titleFont = TMP_FontAsset.CreateFontAsset(titleSource, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    512, 512, AtlasPopulationMode.Dynamic, false);
                if (titleFont != null) { titleFont.TryAddCharacters("Z"); z.font = titleFont; }
            }
            var seal = Rect("Moon signature", menu, Vector2.up, new Vector2(.5f, .5f), new Vector2(246, -247), new Vector2(132, 132))
                .gameObject.AddComponent<StartPageMoonGraphic>();
            seal.color = new Color(Warm.r, Warm.g, Warm.b, .78f);
            var subtitle = Label("English title", menu, Vector2.up, Vector2.up, new Vector2(338, -210), new Vector2(290, 40), "FROM THE MOON", 15, Quiet);
            subtitle.characterSpacing = 4;
            Label("Signature", menu, Vector2.up, Vector2.up, new Vector2(338, -250), new Vector2(305, 70), "愿每一次相遇，\n都有回声。", 20, White);
            Label("Invitation", menu, Vector2.up, Vector2.up, new Vector2(0, -408), new Vector2(610, 75),
                "让匆忙的日子，慢下来。\n在海风里，找回久违的陪伴。", 23, new Color(.85f, .9f, .9f));

            primary = MenuButton("Enter island", new Vector2(0, -524), new Vector2(460, 78), "开始游戏", () => { if (hasSave || confirmFresh) ConfirmNew(); else Choose(true); }, true);
            Label("Arrow", primary.transform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-26, 0), new Vector2(40, 35), "→", 29, new Color(.15f,.24f,.28f), TextAlignmentOptions.Center);
            string progress = hasSave && preview != null ? $"上次停在：第 {preview.day} 天 · {GameClock.PeriodName((TimePeriod)preview.period)}  /  {GameUI.PlaceName(ParsePlace(preview.place))}" : "从 Z 的直播间，赴一场小岛之约。";
            status = Label("Save preview", menu, Vector2.up, Vector2.up, new Vector2(2, -615), new Vector2(620, 38), progress, 16, Quiet);
            if (hasSave) MenuButton("Continue island life", new Vector2(0, -672), new Vector2(220, 52), "继续旅程", () => Choose(false), false);
            MenuButton("Controls", new Vector2(hasSave ? 240 : 0, -672), new Vector2(220, 52), "如何游玩", ShowControls, false);
            notice = Label("Save notice", menu, Vector2.up, Vector2.up, new Vector2(0, -741), new Vector2(640, 65), "", 16, Warm);

            Line(root, new Vector2(1,0), new Vector2(1,0), new Vector2(-64,150), new Vector2(270,1), new Color(.94f,.94f,.88f,.38f));
            var coast = Label("Coast caption", root, new Vector2(1,0), new Vector2(1,0), new Vector2(-64,171), new Vector2(380,34), "海风，正在等你。", 21, White, TextAlignmentOptions.Right);
            coast.characterSpacing = 3;
            Label("Coast detail", root, new Vector2(1,0), new Vector2(1,0), new Vector2(-64,111), new Vector2(380,25), "MOON ISLAND  /  海滨步道", 13, Quiet, TextAlignmentOptions.Right);
            Label("Shoreline", root, Vector2.zero, Vector2.zero, new Vector2(64, 36), new Vector2(700, 28), "一段关于相遇的故事     ·     按 Enter 开始", 15, Quiet);
            var version = Label("Demo", root, Vector2.one, Vector2.one, new Vector2(-64, -44), new Vector2(450, 26), "小岛生活  /  DEMO", 13, White, TextAlignmentOptions.Right);
            version.characterSpacing = 3;
            soundButton = CornerButton("Sound", -58, MusicDirector.Instance==null||MusicDirector.Instance.MusicEnabled ? "音乐 · 开" : "音乐 · 关", ToggleSound);
            motionButton = CornerButton("Motion", -190, "动效 · 开", ToggleMotion);
            CornerButton("Exit", -322, "退出", ConfirmQuit);
            LayoutBackground();
            StartCoroutine(RevealMenu());
        }

        static PlaceId ParsePlace(string id) => Enum.TryParse(id, out PlaceId p) ? p : PlaceId.Street;

        Button MenuButton(string name, Vector2 pos, Vector2 size, string caption, Action action, bool accent)
        {
            var image = Panel(name, menu, Vector2.up, Vector2.up, pos, size,
                accent ? new Color(.91f, .88f, .77f, .97f) : new Color(.09f, .17f, .24f, .25f), true);
            ((RoundedPanel)image).CornerRadius = accent ? 5 : 4;
            ((RoundedPanel)image).SetBorder(accent ? new Color(.98f, .95f, .84f, .8f) : new Color(.65f, .80f, .82f, .18f), 1);
            var b = image.gameObject.AddComponent<Button>(); b.targetGraphic = image;
            b.onClick.AddListener(() => { if (!busy) action(); });
            var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.10f, 1.12f, 1.13f);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.8f, .9f, .94f); colors.fadeDuration = .18f; b.colors = colors;
            Label("Label", image.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(24, 0), size - new Vector2(45, 14), caption, accent ? 26 : 19, accent ? new Color(.13f,.21f,.25f) : White, TextAlignmentOptions.MidlineLeft);
            return b;
        }
        Button CornerButton(string name, float x, string caption, Action action)
        {
            var b = MenuButton(name, Vector2.zero, new Vector2(120, 42), caption, action, false);
            var rt = (RectTransform)b.transform; rt.SetParent(root, false); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0); rt.anchoredPosition = new Vector2(x, 34);
            b.GetComponentInChildren<TextMeshProUGUI>().fontSize = 16; return b;
        }
        void ToggleSound()
        {
            MusicDirector.Instance?.ToggleMusic();
            soundButton.GetComponentInChildren<TextMeshProUGUI>().text = MusicDirector.Instance == null || MusicDirector.Instance.MusicEnabled ? "音乐 · 开" : "音乐 · 关";
        }
        void ToggleMotion()
        {
            motion = !motion; atmosphere.MotionEnabled = motion;
            motionButton.GetComponentInChildren<TextMeshProUGUI>().text = motion ? "动效 · 开" : "动效 · 关";
        }
        IEnumerator RevealMenu()
        {
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / .9f)
            {
                if (busy) yield break;
                float e = 1 - Mathf.Pow(1 - t, 3); menuGroup.alpha = e; menu.anchoredPosition = new Vector2(0, -12 * (1 - e));
                yield return null;
            }
            menuGroup.alpha = 1; menu.anchoredPosition = Vector2.zero;
        }
        void Update()
        {
            if (root == null || dismissed) return;
            if (motion) elapsed += Time.unscaledDeltaTime;
            LayoutBackground();
            if (busy) return;
            if (GameInput.Pressed(Key.Escape)) { if (modal != null) CloseModal(); else ConfirmQuit(); }
            if (GameInput.Pressed(Key.Enter) && modal == null && Time.frameCount > lastModalClosedFrame && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
                primary.onClick.Invoke();
        }
        void LayoutBackground()
        {
            if (background.texture == null || root.rect.height < 1) return;
            float imageAspect = (float)background.texture.width / background.texture.height;
            float displayAspect = root.rect.width / root.rect.height;
            float zoom = (config != null ? config.overscan : 1.04f) + .006f * Mathf.Sin(elapsed * .11f);
            float w = Mathf.Min(1, displayAspect / imageAspect) / zoom, h = Mathf.Min(1, imageAspect / displayAspect) / zoom;
            float center = config != null ? config.focusX : .5f;
            float x = Mathf.Clamp(center - w * .5f + Mathf.Sin(elapsed * .055f) * .006f, 0, 1 - w);
            float y = (1 - h) * .5f + Mathf.Sin(elapsed * .075f) * .0015f;
            background.uvRect = new Rect(x, Mathf.Clamp(y, 0, 1 - h), w, h);
            atmosphere.ImageUV = background.uvRect;
        }
        void ConfirmNew()
        {
            ShowModal("重新开始小岛生活？", hasSave ? "将从 Z 的直播邀请重新开始，替换当前旅程进度。\n如果只是想接着探索，请返回并选择「继续旅程」。" : "确认后将从 Z 的直播间开启新的旅程。\n无法读取的旧存档会被新进度替换。",
                "确认重新开始", () => Choose(true));
        }
        void ConfirmQuit() => ShowModal("今天，先到这里。", "关闭游戏后，你仍可以从上次保存的进度继续。", "退出游戏", Quit);
        void ShowControls() => ShowModal("在小岛，慢慢来。", "A / D 或方向键   沿街行走\n空格   跳跃     E   与附近的人或物交互\nTab   打开手机     M   地图     P   拍照\nEsc   返回 / 暂停\n\n走到街道边缘会前往相邻街道；\n上下路口和建筑入口，靠近后按 E。", "知道了", CloseModal);
        void ShowModal(string title, string text, string actionLabel, Action action)
        {
            CloseModal();
            var veil = Panel("Title modal veil", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Color(.025f, .055f, .10f, .52f), true);
            var vr = veil.rectTransform; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero; modal = veil.gameObject;
            var card = Panel("Title modal", veil.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, 470), new Color(.075f, .14f, .21f, .98f), true);
            Label("Heading", card.transform, Vector2.up, Vector2.up, new Vector2(42, -38), new Vector2(616, 48), title, 32, White);
            Label("Body", card.transform, Vector2.up, Vector2.up, new Vector2(42, -113), new Vector2(616, 225), text, 22, Quiet);
            var cancel = UIPrimitives.Button("Cancel", card.transform, Vector2.zero, Vector2.zero, new Vector2(42, 34), new Vector2(210, 58), "返回", CloseModal);
            var confirm = UIPrimitives.Button("Confirm", card.transform, Vector2.zero, Vector2.zero, new Vector2(275, 34), new Vector2(380, 58), actionLabel, () => { if (!busy) action(); }, true);
            cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = confirm };
            confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancel };
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }
        void CloseModal()
        {
            lastModalClosedFrame = Time.frameCount;
            if (modal != null) { modal.SetActive(false); Destroy(modal); modal = null; }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        void Choose(bool fresh) { if (busy) return; ShowLoading(); enter?.Invoke(fresh); }
        public void ShowLoading() { busy = true; CloseModal(); menuGroup.alpha = 1; group.interactable = false; status.text = "海风正在捎来你的故事…"; }
        public void ShowSaveNotice(string message) { if (notice != null) notice.text = message; if (!hasSave) confirmFresh = true; }
        public void ShowError(string message) { busy = false; group.interactable = true; ShowSaveNotice(message); status.text = "暂时没能抵达小岛，请再试一次。"; }
        public IEnumerator Dismiss()
        {
            busy = true;
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / .65f) { group.alpha = 1 - Mathf.SmoothStep(0, 1, t); yield return null; }
            group.alpha = 0; group.blocksRaycasts = false; dismissed = true;
            Destroy(root.gameObject); Destroy(this);
        }
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        static Texture2D MakeGradient(Color tint)
        {
            const int w = 256, h = 128;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Title soft light", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1), v = (float)y / (h - 1);
                float left = Mathf.Lerp(1.13f, .08f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.20f, .74f, u)));
                float bottom = .52f * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .22f, v)));
                pixels[y * w + x] = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(tint.a * left + bottom));
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        void OnDestroy()
        {
            if (gradient != null) Destroy(gradient);
            if (root != null) Destroy(root.gameObject);
            if (titleFont != null)
            {
                foreach (var texture in titleFont.atlasTextures) if (texture != null) Destroy(texture);
                if (titleFont.material != null) Destroy(titleFont.material);
                Destroy(titleFont);
            }
        }
    }
}
