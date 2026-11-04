#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ZDialogueDemo.EditorTools
{
    /// <summary>
    /// 创建独立的 ZDialogueDemo 场景，不修改 SampleScene 或 BuildSettings。
    /// 命令行入口：
    /// -executeMethod ZDialogueDemo.EditorTools.ZDialogueDemoSceneBuilder.CreateScene
    /// -executeMethod ZDialogueDemo.EditorTools.ZDialogueDemoSceneBuilder.BuildMacDemo
    /// </summary>
    public static class ZDialogueDemoSceneBuilder
    {
        private const string RootFolder = "Assets/ZDialogueDemo";
        private const string SceneFolder = RootFolder + "/Scenes";
        private const string ScenePath = SceneFolder + "/ZDialogueDemo.unity";
        private const string ModelPrefabPath = RootFolder + "/Models/Z/Model.prefab";
        private const string ModelJsonPath = RootFolder + "/Models/Z/Model.model3.json";

        private static readonly Color BackgroundColor = new Color(0.79f, 0.86f, 0.87f, 1f);
        private static readonly Color CardColor = new Color(0.98f, 0.99f, 0.98f, 0.96f);
        private static readonly Color InkColor = new Color(0.12f, 0.20f, 0.22f, 1f);
        private static readonly Color MutedColor = new Color(0.35f, 0.45f, 0.46f, 1f);

        private sealed class SceneReferences
        {
            public DialogueClient client;
            public DialoguePanel panel;
            public ZPerformanceController performance;
            public GameObject modelStage;
        }

        [MenuItem("Tools/Z Dialogue/Create Demo Scene")]
        public static void CreateScene()
        {
            var refs = CreateSceneInternal(requireModel: true, batchMode: false);
            if (refs == null)
                return;

            AttachModelImmediate(refs, requireModel: true);
            SaveActiveScene();
            Debug.Log("[ZDialogueDemo] 已创建独立场景：" + ScenePath);
        }

        [MenuItem("Tools/Z Dialogue/Validate Assets")]
        public static void ValidateAssets()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            Debug.Log("[ZDialogueDemo] scene=" + (scene != null ? "OK " : "MISSING ") + ScenePath +
                      ", modelPrefab=" + (prefab != null ? "OK " : "MISSING ") + ModelPrefabPath);
        }

        [MenuItem("Tools/Z Dialogue/Validate Build Inputs")]
        public static void ValidateBuildInputs()
        {
            if (ValidateBuildInputsInternal(out var report))
                Debug.Log("[ZDialogueDemo] Build acceptance inputs OK: " + report);
            else
                Debug.LogError("[ZDialogueDemo] Build acceptance inputs failed: " + report);
        }

        /// <summary>
        /// 只把独立 Demo 场景传给 BuildPipeline，不把场景加入全局 BuildSettings。
        /// 输出优先使用 Z_DEMO_BUILD_PATH，否则写入当前 Unity 工程的 Builds 目录。
        /// </summary>
        [MenuItem("Tools/Z Dialogue/Build Mac Demo")]
        public static void BuildMacDemo()
        {
            var refs = CreateSceneInternal(requireModel: true, batchMode: Application.isBatchMode);
            if (refs == null)
                throw new InvalidOperationException("无法创建 ZDialogueDemo 场景");

            AttachModelImmediate(refs, requireModel: true);
            SaveActiveScene();
            if (!ValidateBuildInputsInternal(out var validationError))
                throw new InvalidOperationException(validationError);

            var outputPath = Environment.GetEnvironmentVariable("Z_DEMO_BUILD_PATH");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                outputPath = Path.Combine(projectRoot, "Builds", "ZDialogueDemo.app");
            }
            outputPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("ZDialogueDemo macOS build failed: " + report.summary);

            Debug.Log("[ZDialogueDemo] macOS build succeeded: " + outputPath);
        }

        private static SceneReferences CreateSceneInternal(bool requireModel, bool batchMode)
        {
            // 菜单操作先让用户决定是否保存当前场景；-executeMethod/CI 构建不弹窗，
            // 由调用方承担当前场景状态，避免批处理被交互对话框阻塞。
            if (!batchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            EnsureFolder(RootFolder);
            EnsureFolder(SceneFolder);

            ForceImportModelAsset();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (requireModel && prefab == null)
                throw new FileNotFoundException("未找到 Z 模型 Prefab", ModelPrefabPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[ZDialogueDemo] 无法新建场景");
                return null;
            }

            var refs = new SceneReferences();
            refs.modelStage = new GameObject("Z Model Stage");

            CreateCamera();
            CreateEventSystem();
            CreateUi(refs);

            SaveActiveScene();
            return refs;
        }

        private static void ForceImportModelAsset()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var absoluteModelJsonPath = Path.Combine(
                projectRoot,
                ModelJsonPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absoluteModelJsonPath))
                return;

            AssetDatabase.ImportAsset(
                ModelJsonPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.orthographic = true;
            camera.orthographicSize = 5.2f;
            camera.aspect = 1440f / 900f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystemObject.GetComponent<EventSystem>().sendNavigationEvents = true;
        }

        private static void CreateUi(SceneReferences refs)
        {
            var canvasObject = new GameObject("Z Dialogue UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            var card = CreateImage(canvasObject.transform, "Conversation Card", CardColor);
            SetAnchors(card.rectTransform, new Vector2(0.38f, 0.07f), new Vector2(0.96f, 0.93f),
                Vector2.zero, Vector2.zero);

            var title = CreateText(card.transform, "Title", 30, InkColor, TextAnchor.MiddleLeft);
            SetAnchors(title.rectTransform, new Vector2(0.06f, 0.89f), new Vector2(0.62f, 0.98f),
                Vector2.zero, Vector2.zero);
            title.text = "和 Z 说说话";
            title.fontStyle = FontStyle.Bold;

            var saveIdInput = CreateInputField(card.transform, "SaveId", 16);
            SetAnchors(saveIdInput.GetComponent<RectTransform>(), new Vector2(0.65f, 0.89f), new Vector2(0.83f, 0.98f),
                Vector2.zero, Vector2.zero);
            saveIdInput.text = "main";
            saveIdInput.placeholder.GetComponent<Text>().text = "save_id";
            saveIdInput.GetComponent<Image>().color = new Color(0.91f, 0.95f, 0.95f, 1f);

            var saveIdButton = CreateButton(card.transform, "SaveIdButton", "切换", 15,
                new Color(0.29f, 0.56f, 0.58f, 1f));
            SetAnchors(saveIdButton.GetComponent<RectTransform>(), new Vector2(0.85f, 0.89f), new Vector2(0.94f, 0.98f),
                Vector2.zero, Vector2.zero);

            var memory = CreateText(card.transform, "Memory", 16, MutedColor, TextAnchor.UpperLeft);
            SetAnchors(memory.rectTransform, new Vector2(0.06f, 0.71f), new Vector2(0.94f, 0.87f),
                Vector2.zero, Vector2.zero);
            memory.text = "记忆  0\n正在加载…";

            var scrollObject = new GameObject("History Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scrollObject.transform.SetParent(card.transform, false);
            var scrollRect = scrollObject.GetComponent<RectTransform>();
            SetAnchors(scrollRect, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.70f), Vector2.zero, Vector2.zero);
            var scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = new Color(0.91f, 0.95f, 0.95f, 0.54f);
            scrollObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("History Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(scrollObject.transform, false);
            var content = contentObject.GetComponent<RectTransform>();
            SetAnchors(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-14f, 0f));
            content.pivot = new Vector2(0.5f, 1f);
            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(10, 10, 14, 14);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var historyTemplate = CreateText(content, "History Item Template", 22, InkColor, TextAnchor.UpperLeft);
            historyTemplate.gameObject.SetActive(false);
            historyTemplate.horizontalOverflow = HorizontalWrapMode.Wrap;
            historyTemplate.verticalOverflow = VerticalWrapMode.Overflow;
            historyTemplate.raycastTarget = false;
            var historyElement = historyTemplate.gameObject.AddComponent<LayoutElement>();
            historyElement.minHeight = 30f;
            historyElement.flexibleWidth = 1f;
            scrollObject.GetComponent<ScrollRect>().content = content;
            scrollObject.GetComponent<ScrollRect>().vertical = true;
            scrollObject.GetComponent<ScrollRect>().horizontal = false;
            scrollObject.GetComponent<ScrollRect>().movementType = ScrollRect.MovementType.Clamped;

            var input = CreateInputField(card.transform, "Input", 22);
            SetAnchors(input.GetComponent<RectTransform>(), new Vector2(0.06f, 0.10f), new Vector2(0.70f, 0.22f),
                Vector2.zero, Vector2.zero);
            input.GetComponent<Image>().color = new Color(0.91f, 0.95f, 0.95f, 1f);

            var sendButton = CreateButton(card.transform, "Send", "发送", 20, new Color(0.29f, 0.56f, 0.58f, 1f));
            SetAnchors(sendButton.GetComponent<RectTransform>(), new Vector2(0.73f, 0.10f), new Vector2(0.94f, 0.22f),
                Vector2.zero, Vector2.zero);

            var motionLabel = CreateText(card.transform, "MotionLabel", 14, MutedColor, TextAnchor.MiddleLeft);
            SetAnchors(motionLabel.rectTransform, new Vector2(0.06f, 0.235f), new Vector2(0.15f, 0.285f),
                Vector2.zero, Vector2.zero);
            motionLabel.text = "动作预览";

            var motionIdleButton = CreateButton(card.transform, "MotionIdle", "待机", 14,
                new Color(0.42f, 0.60f, 0.61f, 1f));
            SetAnchors(motionIdleButton.GetComponent<RectTransform>(), new Vector2(0.15f, 0.235f), new Vector2(0.26f, 0.285f),
                Vector2.zero, Vector2.zero);
            var motionNodButton = CreateButton(card.transform, "MotionNod", "点头", 14,
                new Color(0.42f, 0.60f, 0.61f, 1f));
            SetAnchors(motionNodButton.GetComponent<RectTransform>(), new Vector2(0.27f, 0.235f), new Vector2(0.38f, 0.285f),
                Vector2.zero, Vector2.zero);
            var motionShakeButton = CreateButton(card.transform, "MotionShake", "摇头", 14,
                new Color(0.42f, 0.60f, 0.61f, 1f));
            SetAnchors(motionShakeButton.GetComponent<RectTransform>(), new Vector2(0.39f, 0.235f), new Vector2(0.50f, 0.285f),
                Vector2.zero, Vector2.zero);
            var motionThinkButton = CreateButton(card.transform, "MotionThink", "思考", 14,
                new Color(0.42f, 0.60f, 0.61f, 1f));
            SetAnchors(motionThinkButton.GetComponent<RectTransform>(), new Vector2(0.51f, 0.235f), new Vector2(0.62f, 0.285f),
                Vector2.zero, Vector2.zero);
            var motionListenButton = CreateButton(card.transform, "MotionListen", "倾听", 14,
                new Color(0.42f, 0.60f, 0.61f, 1f));
            SetAnchors(motionListenButton.GetComponent<RectTransform>(), new Vector2(0.63f, 0.235f), new Vector2(0.74f, 0.285f),
                Vector2.zero, Vector2.zero);
            var motionHappyButton = CreateButton(card.transform, "MotionHappy", "开心", 14,
                new Color(0.42f, 0.60f, 0.61f, 1f));
            SetAnchors(motionHappyButton.GetComponent<RectTransform>(), new Vector2(0.75f, 0.235f), new Vector2(0.86f, 0.285f),
                Vector2.zero, Vector2.zero);
            var motionStopButton = CreateButton(card.transform, "MotionStop", "停止", 14,
                new Color(0.74f, 0.80f, 0.80f, 1f));
            SetAnchors(motionStopButton.GetComponent<RectTransform>(), new Vector2(0.87f, 0.235f), new Vector2(0.94f, 0.285f),
                Vector2.zero, Vector2.zero);

            var cancelButton = CreateButton(card.transform, "Cancel", "取消", 16, new Color(0.74f, 0.80f, 0.80f, 1f));
            SetAnchors(cancelButton.GetComponent<RectTransform>(), new Vector2(0.39f, 0.025f), new Vector2(0.52f, 0.085f),
                Vector2.zero, Vector2.zero);

            var status = CreateText(card.transform, "Status", 16, MutedColor, TextAnchor.MiddleLeft);
            SetAnchors(status.rectTransform, new Vector2(0.06f, 0.025f), new Vector2(0.37f, 0.085f),
                Vector2.zero, Vector2.zero);
            status.text = "等待连接本地服务…";

            var clientObject = new GameObject("Dialogue Client");
            refs.client = clientObject.AddComponent<DialogueClient>();
            refs.panel = canvasObject.AddComponent<DialoguePanel>();

            Wire(refs.panel, "client", refs.client);
            Wire(refs.panel, "saveIdInput", saveIdInput);
            Wire(refs.panel, "saveIdButton", saveIdButton);
            Wire(refs.panel, "inputField", input);
            Wire(refs.panel, "sendButton", sendButton);
            Wire(refs.panel, "cancelButton", cancelButton);
            Wire(refs.panel, "motionIdleButton", motionIdleButton);
            Wire(refs.panel, "motionNodButton", motionNodButton);
            Wire(refs.panel, "motionShakeButton", motionShakeButton);
            Wire(refs.panel, "motionThinkButton", motionThinkButton);
            Wire(refs.panel, "motionListenButton", motionListenButton);
            Wire(refs.panel, "motionHappyButton", motionHappyButton);
            Wire(refs.panel, "motionStopButton", motionStopButton);
            Wire(refs.panel, "statusText", status);
            Wire(refs.panel, "memoryText", memory);
            Wire(refs.panel, "historyScrollRect", scrollObject.GetComponent<ScrollRect>());
            Wire(refs.panel, "historyRoot", content);
            Wire(refs.panel, "historyItemPrefab", historyTemplate);
        }

        private static void AttachModelImmediate(SceneReferences refs, bool requireModel)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (prefab == null)
            {
                if (requireModel)
                    throw new FileNotFoundException("未找到 Z 模型 Prefab", ModelPrefabPath);
                return;
            }

            if (refs.modelStage.transform.childCount == 0)
                AttachModel(refs, prefab);
        }

        private static void AttachModel(SceneReferences refs, GameObject prefab)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[ZDialogueDemo] Prefab 实例化失败：" + ModelPrefabPath);
                return;
            }

            instance.name = "Z Model";
            instance.transform.SetParent(refs.modelStage.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            if (instance.GetComponent<ZDemoModelLayout>() == null)
                instance.AddComponent<ZDemoModelLayout>();

            FitModelToLeftRegion(instance);

            refs.performance = instance.GetComponentInChildren<ZPerformanceController>(true);
            if (refs.performance == null)
                refs.performance = instance.AddComponent<ZPerformanceController>();
            refs.panel.Bind(refs.client, refs.performance);
            refs.performance.RefreshCubismUpdateController();
        }

        private static void FitModelToLeftRegion(GameObject instance)
        {
            var camera = Camera.main;
            if (camera == null || !TryGetRendererBounds(instance, out var bounds))
                return;

            var visibleHeight = camera.orthographicSize * 2f;
            var visibleWidth = visibleHeight * camera.aspect;
            var leftWidth = visibleWidth * 0.35f;
            var targetWidth = leftWidth * 0.88f;
            var targetHeight = visibleHeight * 0.86f;
            if (bounds.size.x > 0.0001f && bounds.size.y > 0.0001f)
            {
                var scale = Mathf.Min(targetWidth / bounds.size.x, targetHeight / bounds.size.y);
                instance.transform.localScale *= Mathf.Clamp(scale, 0.01f, 100f);
                TryGetRendererBounds(instance, out bounds);
            }

            var targetCenter = new Vector3(
                camera.transform.position.x - visibleWidth * 0.5f + leftWidth * 0.5f,
                camera.transform.position.y,
                bounds.center.z);
            instance.transform.position += targetCenter - bounds.center;
        }

        private static bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            var found = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static void Wire(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning("[ZDialogueDemo] 找不到字段：" + propertyName);
                return;
            }
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, int fontSize, Color color, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static InputField CreateInputField(Transform parent, string name, int fontSize)
        {
            var inputObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            var input = inputObject.GetComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            input.caretColor = InkColor;

            var text = CreateText(inputObject.transform, "Text", fontSize, InkColor, TextAnchor.MiddleLeft);
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 3f), new Vector2(-14f, -3f));
            text.raycastTarget = false;
            var placeholder = CreateText(inputObject.transform, "Placeholder", fontSize, MutedColor, TextAnchor.MiddleLeft);
            SetAnchors(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 3f), new Vector2(-14f, -3f));
            placeholder.text = "写下你想告诉 Z 的事…（Ctrl/Cmd+Enter 发送）";
            placeholder.raycastTarget = false;
            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        private static Button CreateButton(Transform parent, string name, string label, int fontSize, Color color)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = color;
            var button = buttonObject.GetComponent<Button>();
            var text = CreateText(buttonObject.transform, "Label", fontSize, Color.white, TextAnchor.MiddleCenter);
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            text.text = label;
            text.raycastTarget = false;
            return button;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static bool ValidateBuildInputsInternal(out string report)
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (scene == null)
            {
                report = "场景不存在：" + ScenePath;
                return false;
            }
            if (prefab == null)
            {
                report = "模型 Prefab 不存在：" + ModelPrefabPath;
                return false;
            }

            report = ScenePath + " + " + ModelPrefabPath;
            return true;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static void SaveActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
