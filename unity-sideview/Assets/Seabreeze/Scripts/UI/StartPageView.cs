using System;
using System.Collections;
using System.Collections.Generic;
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
        RectTransform root, composition, menu;
        RawImage background;
        StartPageAtmosphere atmosphere;
        StartPageCoastalMotion coastalMotion;
        StartPageConfig config;
        Texture2D gradient;
        GameObject modal;
        TextMeshProUGUI status, notice;
        Button primary, lastMenuSelection, soundButton, motionButton;
        Button modalDefaultSelection, lastModalSelection;
        Action<bool> enter;
        Action modalBack;
        bool hasSave, busy, dismissed, confirmFresh, motion = true;
        int lastModalClosedFrame = -1;
        string savePreview;
        readonly List<Button> mainItems = new List<Button>();
        static readonly Color White = new Color(.96f, .97f, .96f);
        static readonly Color Quiet = new Color(.78f, .84f, .87f);
        static readonly Color Warm = new Color(1f, .89f, .68f);

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
            var matte = Stretch("Cinematic side matte", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            matte.color = new Color(.035f, .065f, .10f); matte.raycastTarget = true;
            composition = Rect("Coastal composition", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1080));
            composition.gameObject.AddComponent<RectMask2D>();
            background = Stretch("Painted coast", composition, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            background.texture = config != null ? config.background : Resources.Load<Texture2D>("Art/street_day");
            background.raycastTarget = true;
            coastalMotion = background.gameObject.AddComponent<StartPageCoastalMotion>();
            coastalMotion.Initialize(config);
            atmosphere = Stretch("Sea light and distant birds", composition, Vector2.zero, Vector2.zero).gameObject.AddComponent<StartPageAtmosphere>();
            atmosphere.Sea = config != null ? config.sea : new Rect(.05f, .32f, .40f, .17f);
            atmosphere.Sky = config != null ? config.sky : new Rect(.10f, .68f, .45f, .19f);
            var shade = Stretch("Soft coastal vignette", composition, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            gradient = MakeGradient(config != null ? config.shade : new Color(.045f, .10f, .17f, .14f));
            shade.texture = gradient; shade.raycastTarget = false;

            // Keep the title and every primary action together in the quiet sky beside the coast.
            menu = Rect("Title and menu", composition, new Vector2(.73f, .53f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(520, 560));
            menuGroup = menu.gameObject.AddComponent<CanvasGroup>();
            menuGroup.alpha = 0; menuGroup.interactable = false; menuGroup.blocksRaycasts = false;
            var moon = Rect("Moon signature", menu, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 222), new Vector2(42, 42))
                .gameObject.AddComponent<StartPageMoonGraphic>();
            moon.color = new Color(White.r, White.g, White.b, .9f);
            var title = Label("Game name", menu, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 150), new Vector2(520, 90),
                "来自月亮的 Z", 64, White, TextAlignmentOptions.Center);
            title.characterSpacing = 1.5f;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true; title.fontSizeMin = 58; title.fontSizeMax = 64;

            primary = MainItem("Start game", "开始游戏", () => { if (hasSave || confirmFresh) ConfirmNew(); else Choose(true); });
            if (hasSave) MainItem("Continue journey", "继续旅程", () => Choose(false));
            MainItem("Settings", "设置", ShowSettings);
            MainItem("Exit", "退出", ConfirmQuit);
            LinkVertical(mainItems);
            lastMenuSelection = primary;

            float messageY = 40 - mainItems.Count * 54 - 18;
            status = Label("Entry status", menu, new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(0, messageY), new Vector2(480, 30), "", 16, Quiet, TextAlignmentOptions.Top);
            notice = Label("Save notice", menu, new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(0, messageY - 38), new Vector2(480, 90), "", 17, Warm, TextAlignmentOptions.Top);
            savePreview = hasSave && preview != null
                ? $"上次停在：第 {preview.day} 天 · {GameClock.PeriodName((TimePeriod)preview.period)} / {GameUI.PlaceName(ParsePlace(preview.place))}"
                : "";
            LayoutTitle(); LayoutBackground();
            StartCoroutine(RevealMenu());
        }

        static PlaceId ParsePlace(string id) => Enum.TryParse(id, out PlaceId p) ? p : PlaceId.Street;

        Button MainItem(string name, string caption, Action action)
        {
            var button = TextButton(name, menu, new Vector2(0, 40 - mainItems.Count * 54), new Vector2(330, 50), caption, action, 27);
            mainItems.Add(button);
            return button;
        }

        Button TextButton(string name, Transform parent, Vector2 position, Vector2 size, string caption, Action action, float fontSize)
        {
            var rect = Rect(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, size);
            var hitArea = rect.gameObject.AddComponent<Image>(); hitArea.color = Color.clear; hitArea.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hitArea; button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { if (!busy && Time.frameCount > lastModalClosedFrame) action(); });
            var label = Label("Label", rect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size - new Vector2(54, 4), caption, fontSize, White, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.characterSpacing = 1;
            var marker = Rect("Selection light", rect, new Vector2(.5f, .5f), new Vector2(1, .5f), new Vector2(-size.x * .33f, 0), new Vector2(20, 1.5f)).gameObject.AddComponent<Image>();
            marker.color = new Color(Warm.r, Warm.g, Warm.b, 0); marker.raycastTarget = false;
            rect.gameObject.AddComponent<TitleMenuItem>().Initialize(button, label, marker, White, Warm);
            return button;
        }

        static void LinkVertical(IList<Button> items)
        {
            for (int i = 0; i < items.Count; i++)
                items[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = items[(i + items.Count - 1) % items.Count], selectOnDown = items[(i + 1) % items.Count] };
        }

        void ShowSettings()
        {
            var card = ModalCard("设置", 500);
            soundButton = TextButton("Music setting", card, new Vector2(0, 100), new Vector2(380, 54), MusicCaption(), ToggleSound, 25);
            motionButton = TextButton("Motion setting", card, new Vector2(0, 38), new Vector2(380, 54), motion ? "动效 · 开" : "动效 · 关", ToggleMotion, 25);
            var controls = TextButton("Controls", card, new Vector2(0, -24), new Vector2(380, 54), "如何游玩", ShowControls, 25);
            var back = TextButton("Back to title", card, new Vector2(0, -122), new Vector2(380, 54), "返回", CloseModal, 23);
            Label("Last journey", card, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(610, 30), savePreview, 16, Quiet, TextAlignmentOptions.Center);
            LinkVertical(new[] { soundButton, motionButton, controls, back });
            SetModalSelection(soundButton);
        }

        string MusicCaption() => MusicDirector.Instance == null || MusicDirector.Instance.MusicEnabled ? "音乐 · 开" : "音乐 · 关";
        void ToggleSound()
        {
            MusicDirector.Instance?.ToggleMusic();
            if (soundButton != null) soundButton.GetComponentInChildren<TextMeshProUGUI>().text = MusicCaption();
        }
        void ToggleMotion()
        {
            motion = !motion; atmosphere.MotionEnabled = motion;
            if (coastalMotion != null) coastalMotion.MotionEnabled = motion;
            if (motionButton != null) motionButton.GetComponentInChildren<TextMeshProUGUI>().text = motion ? "动效 · 开" : "动效 · 关";
        }

        IEnumerator RevealMenu()
        {
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / .8f)
            {
                if (busy) yield break;
                float e = 1 - Mathf.Pow(1 - t, 3); menuGroup.alpha = e; menu.anchoredPosition = new Vector2(0, -8 * (1 - e));
                yield return null;
            }
            menuGroup.alpha = 1; menu.anchoredPosition = Vector2.zero;
            if (modal == null) { menuGroup.interactable = true; menuGroup.blocksRaycasts = true; Select(lastMenuSelection); }
        }

        void Update()
        {
            if (root == null || dismissed) return;
            LayoutTitle(); LayoutBackground();
            if (busy) return;
            if (GameInput.Pressed(Key.Escape)) { if (modal != null) modalBack?.Invoke(); else ConfirmQuit(); }
        }

        void LateUpdate()
        {
            if (busy || dismissed || menuGroup == null || Time.frameCount <= lastModalClosedFrame) return;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (modal != null)
            {
                var button = selected != null ? selected.GetComponent<Button>() : null;
                if (IsModalButton(button)) lastModalSelection = button;
                else
                {
                    var restore = IsModalButton(lastModalSelection) ? lastModalSelection : modalDefaultSelection;
                    if (IsModalButton(restore)) Select(restore);
                }
                return;
            }
            if (!menuGroup.interactable) return;
            if (selected == null) Select(lastMenuSelection != null ? lastMenuSelection : primary);
            else
            {
                var button = selected.GetComponent<Button>();
                if (button != null && mainItems.Contains(button)) lastMenuSelection = button;
            }
        }

        static void Select(Button button)
        {
            if (EventSystem.current != null && button != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        bool IsModalButton(Button button) => modal != null && button != null && button.IsActive() && button.IsInteractable()
            && button.transform.IsChildOf(modal.transform);

        void SetModalSelection(Button button)
        {
            modalDefaultSelection = lastModalSelection = button;
            Select(button);
        }

        void LayoutTitle()
        {
            if (root.rect.width < 1 || root.rect.height < 1) return;
            // Preserve the full lighthouse/sea composition on ultrawide displays.
            composition.sizeDelta = new Vector2(Mathf.Min(root.rect.width, root.rect.height * 16f / 9f), root.rect.height);
            float aspect = composition.rect.width / composition.rect.height;
            menu.anchorMin = menu.anchorMax = new Vector2(aspect < 1.45f ? .68f : .73f, .53f);
            float scale = Mathf.Min(1, Mathf.Min(root.rect.width * .43f / 520, root.rect.height * .77f / 560));
            menu.localScale = Vector3.one * scale;
        }

        void LayoutBackground()
        {
            if (background.texture == null || root.rect.height < 1) return;
            float imageAspect = (float)background.texture.width / background.texture.height;
            float displayAspect = composition.rect.width / composition.rect.height;
            // The viewpoint is still; water and birds animate in their own layers.
            float zoom = config != null ? config.overscan : 1.015f;
            float w = Mathf.Min(1, displayAspect / imageAspect) / zoom, h = Mathf.Min(1, imageAspect / displayAspect) / zoom;
            float center = config != null ? config.focusX : .5f;
            float x = Mathf.Clamp(center - w * .5f, 0, 1 - w);
            float y = (1 - h) * .5f;
            background.uvRect = new Rect(x, Mathf.Clamp(y, 0, 1 - h), w, h);
            atmosphere.ImageUV = background.uvRect;
        }

        void ConfirmNew()
        {
            ShowModal("重新开始小岛生活？", hasSave ? "将从 Z 的直播邀请重新开始，替换当前旅程进度。\n如果只是想接着探索，请返回并选择「继续旅程」。" : "确认后将从 Z 的直播间开启新的旅程。\n无法读取的旧存档会被新进度替换。",
                "确认重新开始", () => Choose(true));
        }
        void ConfirmQuit() => ShowModal("今天，先到这里。", "关闭游戏后，你仍可以从上次保存的进度继续。", "退出游戏", Quit);
        void ShowControls() => ShowModal("在小岛，慢慢来。", "A / D 或方向键   沿街行走\n空格   跳跃     E   与附近的人或物交互\nTab   打开手机     M   地图     P   拍照\nEsc   返回 / 暂停\n\n走到街道边缘会前往相邻街道；\n上下路口和建筑入口，靠近后按 E。", "知道了", ShowSettings, ShowSettings);

        RectTransform ModalCard(string title, float height)
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(menu)) lastMenuSelection = selected.GetComponent<Button>();
            CloseModal();
            menuGroup.interactable = false; menuGroup.blocksRaycasts = false;
            var veil = Panel("Title modal veil", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Color(.025f, .055f, .10f, .42f), true);
            var vr = veil.rectTransform; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero; modal = veil.gameObject;
            modalBack = CloseModal;
            var card = Panel("Title modal", veil.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, height), new Color(.075f, .14f, .21f, .96f), true);
            card.rectTransform.localScale = Vector3.one * Mathf.Min(1, Mathf.Min((root.rect.width - 48) / 700, (root.rect.height - 48) / height));
            Label("Heading", card.transform, Vector2.up, Vector2.up, new Vector2(42, -38), new Vector2(616, 48), title, 32, White);
            return card.rectTransform;
        }

        void ShowModal(string title, string text, string actionLabel, Action action, Action back = null)
        {
            var card = ModalCard(title, 470);
            modalBack = back ?? CloseModal;
            Label("Body", card, Vector2.up, Vector2.up, new Vector2(42, -113), new Vector2(616, 225), text, 22, Quiet);
            var cancel = UIPrimitives.Button("Cancel", card, Vector2.zero, Vector2.zero, new Vector2(42, 34), new Vector2(210, 58), "返回", () => modalBack?.Invoke());
            var confirm = UIPrimitives.Button("Confirm", card, Vector2.zero, Vector2.zero, new Vector2(275, 34), new Vector2(380, 58), actionLabel, () => { if (!busy) action(); }, true);
            cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = confirm };
            confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancel };
            SetModalSelection(cancel);
        }

        void CloseModal()
        {
            lastModalClosedFrame = Time.frameCount;
            if (modal != null) { modal.SetActive(false); Destroy(modal); modal = null; }
            modalBack = null;
            modalDefaultSelection = lastModalSelection = null;
            if (menuGroup != null) { menuGroup.interactable = !busy; menuGroup.blocksRaycasts = !busy; }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        void Choose(bool fresh)
        {
            if (busy) return;
            if (fresh) { ShowNameEntry(); return; }
            ShowLoading(); enter?.Invoke(false);
        }
        void ShowNameEntry()
        {
            var card = ModalCard("你希望在游戏里的名字", 400);
            var fieldRect = Rect("Player name", card, Vector2.up, Vector2.up, new Vector2(42,-128), new Vector2(616,72));
            var fill = fieldRect.gameObject.AddComponent<Image>(); fill.color = new Color(.95f,.97f,1,.15f);
            var field = fieldRect.gameObject.AddComponent<TMP_InputField>();
            var text = Label("Name text", fieldRect, Vector2.zero, Vector2.zero, new Vector2(16,8), new Vector2(584,56), "", 26, White);
            field.textViewport = fieldRect; field.textComponent = text; field.richText = false;
            field.lineType = TMP_InputField.LineType.SingleLine; field.characterLimit = 48;
            var hint = Label("Name hint", card, Vector2.up, Vector2.up, new Vector2(42,-220), new Vector2(616,40), "1–12 个字。岛上的朋友会这样称呼你。", 18, Quiet);
            void AcceptName()
            {
                if (busy || !string.IsNullOrEmpty(EventSystem.current?.currentInputModule?.input?.compositionString)) return;
                var name = PlayerIdentity.Normalize(field.text);
                if (string.IsNullOrWhiteSpace(name)) { hint.text = "请先写下你的名字。"; field.ActivateInputField(); return; }
                PlayerIdentity.PendingName = name;
                ShowLoading(); enter?.Invoke(true);
            }
            var confirm = UIPrimitives.Button("Confirm player name", card, Vector2.zero, Vector2.zero, new Vector2(275,34), new Vector2(380,58), "确认，开始旅程", AcceptName, true);
            UIPrimitives.Button("Cancel player name", card, Vector2.zero, Vector2.zero, new Vector2(42,34), new Vector2(210,58), "返回", CloseModal);
            field.onSubmit.AddListener(_ => AcceptName());
            field.text = ""; field.Select(); field.ActivateInputField();
        }
        public void ShowLoading() { busy = true; CloseModal(); menuGroup.alpha = 1; group.interactable = false; status.text = "海风正在捎来你的故事…"; }
        public void ShowSaveNotice(string message) { if (notice != null) notice.text = message; if (!hasSave) confirmFresh = true; }
        public void ShowError(string message)
        {
            busy = false; group.interactable = true; menuGroup.interactable = true; menuGroup.blocksRaycasts = true;
            ShowSaveNotice(message); status.text = "暂时没能抵达小岛，请再试一次。"; Select(lastMenuSelection != null ? lastMenuSelection : primary);
        }
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
                float edge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1.15f, new Vector2((u - .5f) * 1.6f, (v - .5f) * 1.4f).magnitude));
                pixels[y * w + x] = new Color(tint.r, tint.g, tint.b, tint.a * (.12f + edge * .88f));
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        void OnDestroy()
        {
            if (gradient != null) Destroy(gradient);
            if (root != null) Destroy(root.gameObject);
        }
    }
}
