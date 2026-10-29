using System;
using System.IO;
using System.Linq;
using ProjectZ.Core;
using UnityEngine;

namespace ProjectZ.Runtime
{
    public sealed class ProjectZGame : MonoBehaviour
    {
        private EpisodeCatalog catalog;
        private GameSession session;
        private SaveStore saves;
        private CharacterGateway gateway;
        private readonly IslandScene island = new IslandScene();
        private EpisodeDefinition active;
        private CharacterRequest retryRequest;
        private string dialogue = "";
        private string notice = "";
        private string input = "";
        private bool busy, showJournal, confirmNew, showSettings;
        private GUIStyle heading, body, muted, button, inputStyle;
        private Font font;
        private Vector2 journalScroll;
        private Vector2 taskScroll;
        private Vector2 dialogueScroll;
        private float uiScale;
        private bool initialized;
        private bool traceInput;
        public bool HideInterfaceForArtReview { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureGame()
        {
            if (FindFirstObjectByType<ProjectZGame>() == null) new GameObject("Project Z").AddComponent<ProjectZGame>();
        }

        private void Awake()
        {
            try
            {
                Application.targetFrameRate = 60;
                // macOS Metal may wait indefinitely for the first presentation callback.
                // The explicit frame cap also applies when the window is in the background.
                QualitySettings.vSyncCount = Application.platform == RuntimePlatform.OSXPlayer ? 0 : 1;
                var asset = Resources.Load<TextAsset>("Content/episodes");
                if (asset == null) throw new InvalidDataException("未找到事件配置。");
                catalog = JsonUtility.FromJson<EpisodeCatalog>(asset.text);
                ContentRules.Validate(catalog);
                var saveDirectory = Path.Combine(Application.persistentDataPath, "Saves");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var arguments = Environment.GetCommandLineArgs();
                traceInput = Array.IndexOf(arguments, "-z-trace-input") >= 0;
                var saveArgument = Array.IndexOf(arguments, "-z-save-root");
                if (saveArgument >= 0 && saveArgument + 1 < arguments.Length)
                {
                    if (!Path.IsPathRooted(arguments[saveArgument + 1]))
                        throw new ArgumentException("测试存档目录需要绝对路径。");
                    saveDirectory = arguments[saveArgument + 1];
                }
#endif
                saves = new SaveStore(saveDirectory, new UnityStateCodec(), catalog);
                gateway = gameObject.AddComponent<CharacterGateway>();
                island.Create();
                foreach (var episode in catalog.episodes)
                    if (!island.Locations.ContainsKey(episode.locationId))
                        throw new InvalidDataException("事件引用了尚未配置的地点：" + episode.locationId);
                font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
                if (font == null) throw new InvalidDataException("未找到内嵌中文字体。");
                initialized = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-z-art-review") >= 0)
                    gameObject.AddComponent<ArtReviewCapture>().Initialize(island, this);
#endif
            }
            catch (Exception ex) { notice = "工程初始化失败：" + ex.Message; Debug.LogException(ex); }
        }

        private void Update()
        {
            if (!initialized) return;
            var blocked = session == null || active != null || busy || confirmNew || showJournal || showSettings;
            island.Player.Locked = blocked;
            island.Camera.Locked = blocked;
            island.Camera.ConversationTarget = active != null ? island.Z.transform : null;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                gateway.Cancel(); busy = false; active = null; retryRequest = null;
                confirmNew = showSettings = showJournal = false;
            }
            if (session == null) return;
            if (Input.GetKeyDown(KeyCode.F1)) showJournal = !showJournal;
            if (!blocked && Input.GetKeyDown(KeyCode.E))
            {
                var nearest = catalog.episodes.OrderBy(e => Distance(e)).FirstOrDefault();
                if (nearest != null && Distance(nearest) < 2.7f) OpenEpisode(nearest);
            }
        }

        private float Distance(EpisodeDefinition episode) => island.Locations.TryGetValue(episode.locationId, out var point)
            ? Vector3.Distance(island.Player.transform.position, point) : float.PositiveInfinity;

        private void NewGame()
        {
            gateway.Cancel();
            session = new GameSession(catalog);
            active = null; retryRequest = null; busy = false; confirmNew = false;
            ResetPlayer();
            Save();
        }

        private void Continue()
        {
            gateway.Cancel();
            try
            {
                session = new GameSession(catalog, saves.Load(out var recovered));
                active = null; retryRequest = null; busy = false;
                ResetPlayer();
                notice = recovered ? "已从上一个完整备份恢复。" : "已继续这段相处。";
            }
            catch (Exception ex) { notice = ex.Message; }
        }

        private void ResetPlayer()
        {
            var controller = island.Player.GetComponent<CharacterController>();
            controller.enabled = false;
            island.Player.transform.position = new Vector3(1, .3f, -1);
            controller.enabled = true;
            island.PlaceZ("cove");
            island.Z.Perform("look", "neutral");
        }

        private void Save()
        {
            try { saves.Save(session.Snapshot()); notice = "进度已保存在本机。"; }
            catch (Exception ex) { notice = "保存未成功：" + ex.Message + "。请先重试保存，再关闭游戏。"; }
        }

        private void EndDay()
        {
            gateway.Cancel();
            session.NextDay();
            retryRequest = null;
            Save();
        }

        private void HandleKeyboard(Event inputEvent)
        {
            if (inputEvent.type != EventType.KeyDown || busy || confirmNew || showSettings || showJournal) return;
            var enter = inputEvent.keyCode == KeyCode.Return || inputEvent.keyCode == KeyCode.KeypadEnter;
            if (session == null && enter)
            {
                if (saves.Exists) Continue(); else NewGame();
                inputEvent.Use();
                return;
            }
            if (session == null) return;
            if (GUI.GetNameOfFocusedControl() == "ChatInput") return;
            if (active != null && session.Progress(active.id) == null)
            {
                var index = (int)inputEvent.keyCode - (int)KeyCode.Alpha1;
                if (index >= 0 && index < active.choices.Count && index < 9)
                {
                    var request = session.Choose(active.id, active.choices[index].id);
                    Save();
                    Send(request);
                    inputEvent.Use();
                }
            }
            else if (active != null && enter && retryRequest == null)
            {
                active = null;
                inputEvent.Use();
            }
            else if (active == null && inputEvent.keyCode == KeyCode.N)
            {
                EndDay();
                inputEvent.Use();
            }
        }

        private void OpenEpisode(EpisodeDefinition episode)
        {
            active = episode;
            dialogueScroll = Vector2.zero;
            retryRequest = null;
            island.PlaceZ(episode.locationId);
            if (session.CanFollowup(episode.id)) Send(session.BeginFollowup(episode.id));
            else if (session.Progress(episode.id) == null) dialogue = episode.invitation;
            else dialogue = "这段经历已经留下来了。我们先去走走吧。";
        }

        private void Send(CharacterRequest request)
        {
            busy = true;
            retryRequest = request;
            dialogue = "……";
            StartCoroutine(gateway.Respond(request, (response, error) =>
            {
                busy = false;
                if (error != null) { dialogue = error; return; }
                if (!session.Accept(request, response, out var reason)) { dialogue = "这次回应没有生效：" + reason; return; }
                dialogue = response.text;
                dialogueScroll = Vector2.zero;
                retryRequest = null;
                island.Z.Perform(response.action, response.emotion);
                Save();
            }));
        }

        private void Styles()
        {
            if (heading != null) return;
            heading = new GUIStyle(GUI.skin.label) { font = font, fontSize = 30, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(.95f, .93f, .87f) } };
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 19, wordWrap = true,
                normal = { textColor = new Color(.92f, .93f, .91f) } };
            muted = new GUIStyle(body) { fontSize = 14, normal = { textColor = new Color(.67f, .76f, .73f) } };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17, padding = new RectOffset(15, 15, 10, 10),
                wordWrap = true, fixedHeight = 46 };
            button.normal.background = ButtonFill(new Color(.16f,.25f,.31f));
            button.hover.background = ButtonFill(new Color(.23f,.36f,.42f));
            button.active.background = ButtonFill(new Color(.31f,.43f,.49f));
            button.focused.background = button.hover.background;
            button.normal.textColor = button.hover.textColor = button.active.textColor = button.focused.textColor = Color.white;
            inputStyle = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 17 };
        }
        private static Texture2D ButtonFill(Color color)
        {
            var texture = new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            texture.SetPixel(0,0,color.linear);texture.Apply();return texture;
        }

        private void OnGUI()
        {
            if (HideInterfaceForArtReview) return;
            Styles();
            uiScale = Mathf.Max(.5f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiScale);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (traceInput && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyDown))
                Debug.Log("Z_INPUT " + Event.current.type + " gui=" + Event.current.mousePosition +
                    " pointer=" + Input.mousePosition + " scale=" + uiScale + " focus=" + Application.isFocused);
#endif
            var width = Screen.width / uiScale;
            var height = Screen.height / uiScale;
            if (!initialized) { GUI.Label(new Rect(30, 30, width - 60, 160), notice, body); return; }
            HandleKeyboard(Event.current);
            // Modal panels own input; no clicks pass through into world controls.
            if (confirmNew) { DrawConfirmation(width, height); return; }
            if (showSettings) { DrawSettings(width); return; }
            if (showJournal && session != null) { DrawJournal(width, height); return; }
            if (session == null)
            {
                Box(new Rect(70, 100, 480, 470));
                GUILayout.BeginArea(new Rect(100, 125, 420, 420));
                GUILayout.Label("PROJECT Z", muted);
                GUILayout.Space(16);
                GUILayout.Label("一起，把今天留下", heading);
                GUILayout.Space(20);
                GUILayout.Label("走到海湾或地图桌旁，与 Z 一起做一件小事。明天再见时，听听她记住了什么。", body);
                GUILayout.Space(25);
                if (saves.Exists && GUILayout.Button("继续相处", button)) Continue();
                if (GUILayout.Button("开始一段新的相处", button)) { if (saves.Exists) confirmNew = true; else NewGame(); }
                GUILayout.Space(18);
                GUILayout.Label("海湾美术试作 · 默认使用预写对话", muted);
                GUILayout.Label("按 Enter 开始；已有存档时继续", muted);
                GUILayout.Label(notice, muted);
                GUILayout.EndArea();
            }
            else
            {
                var state = session.Snapshot();
                Box(new Rect(24, 20, width - 48, 66));
                GUI.Label(new Rect(46, 34, 320, 36), "PROJECT Z   /   第 " + state.day + " 天", body);
                GUI.Label(new Rect(width - 580, 39, 180, 28), gateway.UseLiveService ? "在线 AI" : "预写对话", muted);
                GUI.enabled = !busy && active == null;
                if (GUI.Button(new Rect(width - 380, 31, 110, 44), "设置", button)) showSettings = !showSettings;
                if (GUI.Button(new Rect(width - 254, 31, 100, 44), "保存", button)) Save();
                if (GUI.Button(new Rect(width - 142, 31, 94, 44), "重开", button)) confirmNew = true;
                GUI.enabled = true;
                if (active == null)
                {
                Box(new Rect(24, 105, 330, 272));
                GUILayout.BeginArea(new Rect(44, 125, 290, 235));
                GUILayout.Label("今天可以一起做的事", body);
                GUILayout.Space(10);
                taskScroll = GUILayout.BeginScrollView(taskScroll);
                foreach (var episode in catalog.episodes)
                {
                    var progress = session.Progress(episode.id);
                    var status = progress == null ? "尚未开始" : progress.followupSeen ? "已接续" : session.CanFollowup(episode.id) ? "可以再聊聊" : "等待明天";
                    GUILayout.Label(episode.title + " · " + status, muted);
                    GUI.enabled = active == null && !busy && Distance(episode) < 2.7f;
                    if (GUILayout.Button("与 Z 聊这件事", button)) OpenEpisode(episode);
                    GUI.enabled = true;
                    GUILayout.Space(8);
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                }
                if (active == null)
                {
                    GUI.enabled = !busy;
                    if (GUI.Button(new Rect(width - 235, height - 90, 210, 48), "结束今天，去往明天", button))
                    { EndDay(); }
                    GUI.enabled = true;
                }
                Box(new Rect(16, height - 74, width - 270, 68));
                GUI.Label(new Rect(26, height - 65, width - 290, 26), "WASD 移动   ← → 镜头   E 交谈   1/2 选择   Enter 继续   N 下一天   Esc 返回   F1 经历", muted);
                GUI.Label(new Rect(26, height - 35, width - 52, 28), notice, muted);
            }
            if (active != null && session != null) DrawDialogue(width, height);
        }

        private void DrawDialogue(float width, float height)
        {
            var panelHeight = session.Progress(active.id) == null || gateway.UseLiveService || retryRequest != null ? 242 : 180;
            var rect = new Rect(58, height - panelHeight - 95, width - 116, panelHeight);
            Box(rect);
            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 18, rect.width - 48, rect.height - 30));
            dialogueScroll = GUILayout.BeginScrollView(dialogueScroll);
            GUILayout.Label("Z   /   " + active.title, muted);
            GUILayout.Label(dialogue, body, GUILayout.MinHeight(58));
            GUI.enabled = !busy;
            if (session.Progress(active.id) == null)
            {
                for (var index = 0; index < active.choices.Count; index++)
                {
                    var choice = active.choices[index];
                    if (GUILayout.Button((index + 1) + " · " + choice.label, button))
                    { var request = session.Choose(active.id, choice.id); Save(); Send(request); }
                }
            }
            else if (retryRequest != null)
            {
                if (GUILayout.Button("重试回应", button)) Send(retryRequest);
                if (gateway.UseLiveService && GUILayout.Button("切换为预写对话并继续", button))
                { gateway.UseLiveService = false; Send(retryRequest); }
            }
            else
            {
                if (gateway.UseLiveService)
                {
                    GUI.SetNextControlName("ChatInput");
                    input = GUILayout.TextField(input, 500, inputStyle, GUILayout.Height(32));
                    if (GUILayout.Button("聊聊你的想法", button) && !string.IsNullOrWhiteSpace(input))
                    { var text = input; input = ""; Send(session.Conversation(text)); }
                }
                if (GUILayout.Button("一起去走走  ·  Enter", button)) active = null;
            }
            GUI.enabled = true;
            if (busy && GUILayout.Button("结束等待", button)) { gateway.Cancel(); busy = false; dialogue = "已结束等待，可以重试。"; }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSettings(float width)
        {
            Box(new Rect(width - 445, 100, 420, 235));
            GUILayout.BeginArea(new Rect(width - 425, 120, 380, 205));
            GUILayout.Label("对话方式", body);
            var live = GUILayout.Toggle(gateway.UseLiveService, " 使用本机在线 AI 服务", button);
            if (live != gateway.UseLiveService) { gateway.Cancel(); busy = false; gateway.UseLiveService = live; }
            GUILayout.Label("预写对话用于体验流程。在线模式需要先启动项目附带的本机服务并配置模型。", muted);
            if (GUILayout.Button("关闭", button)) showSettings = false;
            GUILayout.EndArea();
        }

        private void DrawJournal(float width, float height)
        {
            Box(new Rect(width - 585, 108, 560, height - 205));
            GUILayout.BeginArea(new Rect(width - 563, 128, 516, height - 245));
            GUILayout.Label("共同经历", heading);
            journalScroll = GUILayout.BeginScrollView(journalScroll);
            foreach (var entry in session.Snapshot().events)
            {
                GUILayout.Label("第 " + entry.day + " 天 · " + entry.locationId, muted);
                GUILayout.Label(entry.fact, body);
                GUILayout.Space(16);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("关闭记录", button)) showJournal = false;
            GUILayout.EndArea();
        }

        private void DrawConfirmation(float width, float height)
        {
            Box(new Rect(width / 2 - 240, height / 2 - 120, 480, 240));
            GUILayout.BeginArea(new Rect(width / 2 - 215, height / 2 - 95, 430, 195));
            GUILayout.Label("开始新的相处？", heading);
            GUILayout.Label("当前进度将被替换。新游戏不会继承之前的共同经历。", body);
            if (GUILayout.Button("确认开始新游戏", button)) NewGame();
            if (GUILayout.Button("返回", button)) confirmNew = false;
            GUILayout.EndArea();
        }

        private static void Box(Rect rect)
        {
            var color = GUI.color;
            var background = new Color(.075f, .12f, .17f, .92f);
            GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? background.linear : background;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
        }
    }
}
