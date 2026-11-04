using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ZDialogueDemo
{
    /// <summary>
    /// 最小 uGUI 对话面板。
    /// 面板只显示服务端已返回的内容，不在客户端生成兜底台词。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialoguePanel : MonoBehaviour
    {
        [Header("业务组件")]
        [SerializeField] private DialogueClient client;
        [SerializeField] private ZPerformanceController performanceController;

        [Header("输入")]
        [SerializeField] private InputField saveIdInput;
        [SerializeField] private Button saveIdButton;
        [SerializeField] private InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button cancelButton;

        [Header("状态")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text memoryText;

        [Header("动作预览")]
        [SerializeField] private bool createMotionPreviewUi = true;
        [SerializeField] private Button motionIdleButton;
        [SerializeField] private Button motionNodButton;
        [SerializeField] private Button motionShakeButton;
        [SerializeField] private Button motionThinkButton;
        [SerializeField] private Button motionListenButton;
        [SerializeField] private Button motionHappyButton;
        [SerializeField] private Button motionStopButton;
        [SerializeField] private Button motionSurpriseButton;
        [SerializeField] private Button motionCuriousButton;
        [SerializeField] private Button motionShyButton;
        [SerializeField] private Button motionSadButton;
        [SerializeField] private Button motionAngryButton;
        [SerializeField] private Button motionWinkButton;
        [SerializeField] private Button motionSorrowButton;
        [SerializeField] private Button motionCryButton;
        [SerializeField] private Button motionBawlButton;
        [SerializeField] private Button motionWarmButton;
        [SerializeField] private Button motionSeriousButton;
        [SerializeField] private Button motionLookLeftButton;
        [SerializeField] private Button motionLookRightButton;
        [SerializeField] private Button motionLeanButton;
        [SerializeField] private Button motionTiltButton;
        [SerializeField] private Button motionEmphasisButton;
        [SerializeField] private Button motionStartleButton;
        [SerializeField] private Button motionDiscoverButton;
        [SerializeField] private Button motionGloomButton;
        [SerializeField] private Button motionSighButton;
        [SerializeField] private Button motionAmplitudeDownButton;
        [SerializeField] private Button motionAmplitudeUpButton;
        [SerializeField] private Button motionSpeedDownButton;
        [SerializeField] private Button motionSpeedUpButton;
        [SerializeField] private Text motionTuningText;

        [Header("历史")]
        [SerializeField] private ScrollRect historyScrollRect;
        [SerializeField] private Transform historyRoot;
        [SerializeField] private Text historyItemPrefab;

        [Header("字体")]
        [SerializeField] private bool preferPlatformChineseFont = true;
        [SerializeField, Min(12)] private int platformFontSize = 24;

        private readonly List<Text> historyItems = new List<Text>();
        private Color normalStatusColor = Color.white;
        private int viewGeneration;
        private int sessionRefreshGeneration;
        private bool hasReceivedSession;
        private bool motionListenersBound;
        private bool motionTuningListenersBound;
        private RectTransform runtimeMotionRoot;

        public DialogueClient Client => client;
        public ZPerformanceController PerformanceController => performanceController;
        public InputField InputField => inputField;
        public bool IsSending => client != null && client.IsBusy;

        private void Awake()
        {
            if (client == null)
                client = GetComponent<DialogueClient>();
            if (performanceController == null)
                performanceController = FindObjectOfType<ZPerformanceController>();

            if (statusText != null)
                normalStatusColor = statusText.color;
            ApplyPreferredChineseFont();
        }

        private void OnEnable()
        {
            if (sendButton != null)
                sendButton.onClick.AddListener(SendFromInput);
            if (cancelButton != null)
                cancelButton.onClick.AddListener(CancelCurrentTurn);
            if (saveIdButton != null)
                saveIdButton.onClick.AddListener(ApplySaveId);
            BindMotionButtonListeners();
            BindMotionTuningListeners();

            if (client != null)
            {
                client.HealthUpdated += OnHealthUpdated;
                client.SessionLoaded += OnSessionLoaded;
                client.TurnStarted += OnTurnStarted;
                client.ChatTurnReady += OnChatTurnReady;
                client.TurnAcknowledged += OnTurnAcknowledged;
                client.TurnCancelled += OnTurnCancelled;
                client.RequestFailed += OnRequestFailed;
            }
        }

        private void Start()
        {
            EnsureMotionPreviewUi();
            // OnEnable runs before Start, so an old serialized button row may
            // already have set the listener guard before runtime emotion buttons
            // were created. Rebind once after supplementation to include both.
            if (motionListenersBound)
                UnbindMotionButtonListeners();
            BindMotionButtonListeners();
            BindMotionTuningListeners();
            UpdateMotionTuningText();
            if (client != null)
            {
                if (saveIdInput != null)
                    saveIdInput.text = client.SaveId;
                SetStatus("正在连接本地服务…", false);
                client.Initialize();
            }
            else
            {
                SetStatus("未配置 DialogueClient", true);
            }
        }

        private void Update()
        {
            // 中文输入法确认键不直接发送；仅 Ctrl/Cmd+Enter 发送，按钮始终可用。
            var keyboard = Keyboard.current;
            var modifierPressed = keyboard != null &&
                                  (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed ||
                                   keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed);
            if (inputField != null && inputField.isFocused && keyboard != null &&
                keyboard.enterKey.wasPressedThisFrame && modifierPressed)
            {
                SendFromInput();
            }
        }

        private void OnDisable()
        {
            if (sendButton != null)
                sendButton.onClick.RemoveListener(SendFromInput);
            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(CancelCurrentTurn);
            if (saveIdButton != null)
                saveIdButton.onClick.RemoveListener(ApplySaveId);
            UnbindMotionButtonListeners();
            UnbindMotionTuningListeners();

            if (client != null)
            {
                client.HealthUpdated -= OnHealthUpdated;
                client.SessionLoaded -= OnSessionLoaded;
                client.TurnStarted -= OnTurnStarted;
                client.ChatTurnReady -= OnChatTurnReady;
                client.TurnAcknowledged -= OnTurnAcknowledged;
                client.TurnCancelled -= OnTurnCancelled;
                client.RequestFailed -= OnRequestFailed;
            }
        }

        public void SendFromInput()
        {
            if (client == null || inputField == null)
            {
                SetStatus("未配置输入组件", true);
                return;
            }

            var message = (inputField.text ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                SetStatus("请输入内容", true);
                return;
            }

            if (!string.IsNullOrEmpty(client.PendingAckTurnId))
            {
                // 已显示但尚未持久化的回合优先重试 ACK，避免新消息把它丢掉。
                client.RetryPendingAcknowledge();
                SetStatus("上一回合尚未保存，正在重试…", false);
                return;
            }

            client.SendMessage(message);
            if (string.IsNullOrEmpty(client.PendingAckTurnId))
                inputField.text = string.Empty;
        }

        public void ApplySaveId()
        {
            if (client == null || saveIdInput == null)
                return;

            var requested = (saveIdInput.text ?? string.Empty).Trim();
            var previous = client.SaveId;
            client.SetSaveId(requested);
            if (client.SaveId != requested && requested != previous)
            {
                saveIdInput.text = previous;
                return;
            }

            saveIdInput.text = client.SaveId;
            client.LoadSession();
            SetStatus("已切换存档 · " + client.SaveId, false);
        }

        public void CancelCurrentTurn()
        {
            client?.CancelCurrentTurn();
            performanceController?.CancelCurrentPerformance();
        }

        public void PreviewIdleMotion()
        {
            PreviewMotion("idle", "待机");
        }

        public void PreviewNodMotion()
        {
            PreviewMotion("nod", "点头");
        }

        public void PreviewShakeMotion()
        {
            PreviewMotion("shake", "摇头");
        }

        public void PreviewThinkMotion()
        {
            PreviewMotion("think", "思考");
        }

        public void PreviewListenMotion()
        {
            PreviewMotion("listen", "倾听");
        }

        public void PreviewHappyMotion()
        {
            PreviewMotion("happy", "开心");
        }

        public void PreviewSurpriseMotion()
        {
            PreviewMotion("surprise", "惊讶·！");
        }

        public void PreviewCuriousMotion()
        {
            PreviewMotion("curious", "疑惑");
        }

        public void PreviewShyMotion()
        {
            PreviewMotion("shy", "害羞");
        }

        public void PreviewSadMotion()
        {
            PreviewMotion("sad", "失落");
        }

        public void PreviewAngryMotion()
        {
            PreviewMotion("angry", "生气/不满");
        }

        public void PreviewWinkMotion()
        {
            PreviewMotion("wink", "俏皮眨眼");
        }

        public void PreviewSorrowMotion()
        {
            PreviewMotion("sorrow", "悲伤");
        }

        public void PreviewCryMotion()
        {
            PreviewMotion("cry", "哭泣");
        }

        public void PreviewBawlMotion()
        {
            PreviewMotion("bawl", "大哭");
        }

        public void PreviewWarmMotion()
        {
            PreviewMotion("warm", "温柔");
        }

        public void PreviewSeriousMotion()
        {
            PreviewMotion("serious", "严肃");
        }

        public void PreviewLookLeftMotion()
        {
            PreviewMotion("lookleft", "向左看");
        }

        public void PreviewLookRightMotion()
        {
            PreviewMotion("lookright", "向右看");
        }

        public void PreviewLeanMotion()
        {
            PreviewMotion("lean", "前倾试作");
        }

        public void PreviewTiltMotion()
        {
            PreviewMotion("tilt", "歪头");
        }

        public void PreviewEmphasisMotion()
        {
            PreviewMotion("emphasis", "强调");
        }

        public void PreviewStartleMotion()
        {
            PreviewMotion("startle", "受惊·闪电");
        }

        public void PreviewDiscoverMotion()
        {
            PreviewMotion("discover", "发现/回看");
        }

        public void PreviewGloomMotion()
        {
            PreviewMotion("gloom", "黑化");
        }

        public void PreviewSighMotion()
        {
            PreviewMotion("sigh", "无奈·叹气");
        }

        public void StopMotionPreview()
        {
            performanceController?.StopMotionPreview();
            SetStatus("动作预览已停止 · 待机", false);
        }

        public void DecreaseMotionAmplitude()
        {
            AdjustMotionAmplitude(-0.25f);
        }

        public void IncreaseMotionAmplitude()
        {
            AdjustMotionAmplitude(0.25f);
        }

        public void DecreaseMotionSpeed()
        {
            AdjustMotionSpeed(-0.25f);
        }

        public void IncreaseMotionSpeed()
        {
            AdjustMotionSpeed(0.25f);
        }

        private void AdjustMotionAmplitude(float delta)
        {
            if (performanceController == null)
            {
                SetStatus("未绑定动作控制器", true);
                return;
            }

            performanceController.SetMotionAmplitudeMultiplier(
                performanceController.MotionAmplitudeMultiplier + delta);
            UpdateMotionTuningText();
        }

        private void AdjustMotionSpeed(float delta)
        {
            if (performanceController == null)
            {
                SetStatus("未绑定动作控制器", true);
                return;
            }

            performanceController.SetMotionSpeedMultiplier(
                performanceController.MotionSpeedMultiplier + delta);
            UpdateMotionTuningText();
        }

        private void UpdateMotionTuningText()
        {
            if (motionTuningText == null || performanceController == null)
                return;

            motionTuningText.text = string.Format(
                "幅 {0:0.00} / 速 {1:0.00}",
                performanceController.MotionAmplitudeMultiplier,
                performanceController.MotionSpeedMultiplier);
        }

        private void PreviewMotion(string motion, string label)
        {
            if (performanceController == null)
            {
                SetStatus("未绑定动作控制器", true);
                return;
            }

            performanceController.PreviewMotion(motion);
            var expressionLabel = string.Equals(motion, "happy", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(motion, "nod", StringComparison.OrdinalIgnoreCase)
                ? " · 微笑"
                : string.Equals(motion, "think", StringComparison.OrdinalIgnoreCase)
                    ? " · 轻微困惑 · 视线移开"
                    : string.Equals(motion, "shake", StringComparison.OrdinalIgnoreCase)
                        ? " · 轻微困惑"
                        : string.Equals(motion, "listen", StringComparison.OrdinalIgnoreCase)
                            ? " · 看向玩家"
                            : string.Equals(motion, "surprise", StringComparison.OrdinalIgnoreCase)
                                ? " · 惊讶嘴型 · 视线上抬"
                                : string.Equals(motion, "curious", StringComparison.OrdinalIgnoreCase)
                                    ? " · 困扰眉 · 看回玩家"
                                    : string.Equals(motion, "shy", StringComparison.OrdinalIgnoreCase)
                                        ? " · 脸红 · 侧看回正"
                                        : string.Equals(motion, "sad", StringComparison.OrdinalIgnoreCase)
                                            ? " · 困扰眉 · 下视半闭眼"
                                            : string.Equals(motion, "angry", StringComparison.OrdinalIgnoreCase)
                                                ? " · 压眉 · 偏头短摇"
                                                : string.Equals(motion, "wink", StringComparison.OrdinalIgnoreCase)
                                                    ? " · 歪头 · 单眼眨"
                                                    : string.Equals(motion, "sorrow", StringComparison.OrdinalIgnoreCase)
                                                        ? " · 深度悲伤 · 慢低头"
                                                        : string.Equals(motion, "cry", StringComparison.OrdinalIgnoreCase)
                                                            ? " · 张口哭泣 · 轻抽泣"
                                                            : string.Equals(motion, "bawl", StringComparison.OrdinalIgnoreCase)
                                                                ? " · 张口大哭 · 明显抽泣"
                                                                : string.Equals(motion, "warm", StringComparison.OrdinalIgnoreCase)
                                                                    ? " · 自然睁眼 · 轻柔点头"
                                                                    : string.Equals(motion, "serious", StringComparison.OrdinalIgnoreCase)
                                                                        ? " · 直嘴 · 稳定头身微动"
                                                                        : string.Equals(motion, "lookleft", StringComparison.OrdinalIgnoreCase)
                                                                            ? " · 视线向左 · 头部微转"
                                                                            : string.Equals(motion, "lookright", StringComparison.OrdinalIgnoreCase)
                                                                                ? " · 视线向右 · 头部微转"
                                                                                : string.Equals(motion, "lean", StringComparison.OrdinalIgnoreCase)
                                                                                    ? " · 现有头身参数"
                                                                                    : string.Equals(motion, "tilt", StringComparison.OrdinalIgnoreCase)
                                                                                        ? " · 自然睁眼 · 小笑歪头"
                                                                                        : string.Equals(motion, "emphasis", StringComparison.OrdinalIgnoreCase)
                                                                                            ? " · 短点头 · 躯干轻顿"
                                                                                            : string.Equals(motion, "startle", StringComparison.OrdinalIgnoreCase)
                                                                                                ? " · 快速后缩 · O嘴回弹"
                                                                                                : string.Equals(motion, "discover", StringComparison.OrdinalIgnoreCase)
                                                                                                    ? " · 先右看 · 再看回玩家"
                                                                                                    : string.Equals(motion, "gloom", StringComparison.OrdinalIgnoreCase)
                                                                                                        ? " · 前视冷淡 · 上半脸阴影"
                                                                                                        : string.Equals(motion, "sigh", StringComparison.OrdinalIgnoreCase)
                                                                                                            ? " · 完整闭眼呼气 · 慢低头"
                                                                                    : string.Empty;
            SetStatus("动作预览 · " + label + expressionLabel, false);
        }

        private void BindMotionButtonListeners()
        {
            if (motionListenersBound)
                return;

            var bound = false;
            if (motionIdleButton != null)
            {
                motionIdleButton.onClick.AddListener(PreviewIdleMotion);
                bound = true;
            }
            if (motionNodButton != null)
            {
                motionNodButton.onClick.AddListener(PreviewNodMotion);
                bound = true;
            }
            if (motionShakeButton != null)
            {
                motionShakeButton.onClick.AddListener(PreviewShakeMotion);
                bound = true;
            }
            if (motionThinkButton != null)
            {
                motionThinkButton.onClick.AddListener(PreviewThinkMotion);
                bound = true;
            }
            if (motionListenButton != null)
            {
                motionListenButton.onClick.AddListener(PreviewListenMotion);
                bound = true;
            }
            if (motionHappyButton != null)
            {
                motionHappyButton.onClick.AddListener(PreviewHappyMotion);
                bound = true;
            }
            if (motionSurpriseButton != null)
            {
                motionSurpriseButton.onClick.AddListener(PreviewSurpriseMotion);
                bound = true;
            }
            if (motionCuriousButton != null)
            {
                motionCuriousButton.onClick.AddListener(PreviewCuriousMotion);
                bound = true;
            }
            if (motionShyButton != null)
            {
                motionShyButton.onClick.AddListener(PreviewShyMotion);
                bound = true;
            }
            if (motionSadButton != null)
            {
                motionSadButton.onClick.AddListener(PreviewSadMotion);
                bound = true;
            }
            if (motionAngryButton != null)
            {
                motionAngryButton.onClick.AddListener(PreviewAngryMotion);
                bound = true;
            }
            if (motionWinkButton != null)
            {
                motionWinkButton.onClick.AddListener(PreviewWinkMotion);
                bound = true;
            }
            if (motionSorrowButton != null)
            {
                motionSorrowButton.onClick.AddListener(PreviewSorrowMotion);
                bound = true;
            }
            if (motionCryButton != null)
            {
                motionCryButton.onClick.AddListener(PreviewCryMotion);
                bound = true;
            }
            if (motionBawlButton != null)
            {
                motionBawlButton.onClick.AddListener(PreviewBawlMotion);
                bound = true;
            }
            if (motionWarmButton != null)
            {
                motionWarmButton.onClick.AddListener(PreviewWarmMotion);
                bound = true;
            }
            if (motionSeriousButton != null)
            {
                motionSeriousButton.onClick.AddListener(PreviewSeriousMotion);
                bound = true;
            }
            if (motionLookLeftButton != null)
            {
                motionLookLeftButton.onClick.AddListener(PreviewLookLeftMotion);
                bound = true;
            }
            if (motionLookRightButton != null)
            {
                motionLookRightButton.onClick.AddListener(PreviewLookRightMotion);
                bound = true;
            }
            if (motionLeanButton != null)
            {
                motionLeanButton.onClick.AddListener(PreviewLeanMotion);
                bound = true;
            }
            if (motionTiltButton != null)
            {
                motionTiltButton.onClick.AddListener(PreviewTiltMotion);
                bound = true;
            }
            if (motionEmphasisButton != null)
            {
                motionEmphasisButton.onClick.AddListener(PreviewEmphasisMotion);
                bound = true;
            }
            if (motionStartleButton != null)
            {
                motionStartleButton.onClick.AddListener(PreviewStartleMotion);
                bound = true;
            }
            if (motionDiscoverButton != null)
            {
                motionDiscoverButton.onClick.AddListener(PreviewDiscoverMotion);
                bound = true;
            }
            if (motionGloomButton != null)
            {
                motionGloomButton.onClick.AddListener(PreviewGloomMotion);
                bound = true;
            }
            if (motionSighButton != null)
            {
                motionSighButton.onClick.AddListener(PreviewSighMotion);
                bound = true;
            }
            if (motionStopButton != null)
            {
                motionStopButton.onClick.AddListener(StopMotionPreview);
                bound = true;
            }
            motionListenersBound = bound;
        }

        private void UnbindMotionButtonListeners()
        {
            if (!motionListenersBound)
                return;

            if (motionIdleButton != null)
                motionIdleButton.onClick.RemoveListener(PreviewIdleMotion);
            if (motionNodButton != null)
                motionNodButton.onClick.RemoveListener(PreviewNodMotion);
            if (motionShakeButton != null)
                motionShakeButton.onClick.RemoveListener(PreviewShakeMotion);
            if (motionThinkButton != null)
                motionThinkButton.onClick.RemoveListener(PreviewThinkMotion);
            if (motionListenButton != null)
                motionListenButton.onClick.RemoveListener(PreviewListenMotion);
            if (motionHappyButton != null)
                motionHappyButton.onClick.RemoveListener(PreviewHappyMotion);
            if (motionSurpriseButton != null)
                motionSurpriseButton.onClick.RemoveListener(PreviewSurpriseMotion);
            if (motionCuriousButton != null)
                motionCuriousButton.onClick.RemoveListener(PreviewCuriousMotion);
            if (motionShyButton != null)
                motionShyButton.onClick.RemoveListener(PreviewShyMotion);
            if (motionSadButton != null)
                motionSadButton.onClick.RemoveListener(PreviewSadMotion);
            if (motionAngryButton != null)
                motionAngryButton.onClick.RemoveListener(PreviewAngryMotion);
            if (motionWinkButton != null)
                motionWinkButton.onClick.RemoveListener(PreviewWinkMotion);
            if (motionSorrowButton != null)
                motionSorrowButton.onClick.RemoveListener(PreviewSorrowMotion);
            if (motionCryButton != null)
                motionCryButton.onClick.RemoveListener(PreviewCryMotion);
            if (motionBawlButton != null)
                motionBawlButton.onClick.RemoveListener(PreviewBawlMotion);
            if (motionWarmButton != null)
                motionWarmButton.onClick.RemoveListener(PreviewWarmMotion);
            if (motionSeriousButton != null)
                motionSeriousButton.onClick.RemoveListener(PreviewSeriousMotion);
            if (motionLookLeftButton != null)
                motionLookLeftButton.onClick.RemoveListener(PreviewLookLeftMotion);
            if (motionLookRightButton != null)
                motionLookRightButton.onClick.RemoveListener(PreviewLookRightMotion);
            if (motionLeanButton != null)
                motionLeanButton.onClick.RemoveListener(PreviewLeanMotion);
            if (motionTiltButton != null)
                motionTiltButton.onClick.RemoveListener(PreviewTiltMotion);
            if (motionEmphasisButton != null)
                motionEmphasisButton.onClick.RemoveListener(PreviewEmphasisMotion);
            if (motionStartleButton != null)
                motionStartleButton.onClick.RemoveListener(PreviewStartleMotion);
            if (motionDiscoverButton != null)
                motionDiscoverButton.onClick.RemoveListener(PreviewDiscoverMotion);
            if (motionGloomButton != null)
                motionGloomButton.onClick.RemoveListener(PreviewGloomMotion);
            if (motionSighButton != null)
                motionSighButton.onClick.RemoveListener(PreviewSighMotion);
            if (motionStopButton != null)
                motionStopButton.onClick.RemoveListener(StopMotionPreview);
            motionListenersBound = false;
        }

        private void BindMotionTuningListeners()
        {
            if (motionTuningListenersBound)
                return;

            var bound = false;
            if (motionAmplitudeDownButton != null)
            {
                motionAmplitudeDownButton.onClick.AddListener(DecreaseMotionAmplitude);
                bound = true;
            }
            if (motionAmplitudeUpButton != null)
            {
                motionAmplitudeUpButton.onClick.AddListener(IncreaseMotionAmplitude);
                bound = true;
            }
            if (motionSpeedDownButton != null)
            {
                motionSpeedDownButton.onClick.AddListener(DecreaseMotionSpeed);
                bound = true;
            }
            if (motionSpeedUpButton != null)
            {
                motionSpeedUpButton.onClick.AddListener(IncreaseMotionSpeed);
                bound = true;
            }
            motionTuningListenersBound = bound;
        }

        private void UnbindMotionTuningListeners()
        {
            if (!motionTuningListenersBound)
                return;

            if (motionAmplitudeDownButton != null)
                motionAmplitudeDownButton.onClick.RemoveListener(DecreaseMotionAmplitude);
            if (motionAmplitudeUpButton != null)
                motionAmplitudeUpButton.onClick.RemoveListener(IncreaseMotionAmplitude);
            if (motionSpeedDownButton != null)
                motionSpeedDownButton.onClick.RemoveListener(DecreaseMotionSpeed);
            if (motionSpeedUpButton != null)
                motionSpeedUpButton.onClick.RemoveListener(IncreaseMotionSpeed);
            motionTuningListenersBound = false;
        }

        /// <summary>
        /// 旧场景不需要重建：运行时自动补一排动作按钮。新生成场景若已绑定按钮，
        /// 该入口会直接复用序列化引用。
        /// </summary>
        private void EnsureMotionPreviewUi()
        {
            if (!createMotionPreviewUi)
                return;

            var canvas = GetComponentInParent<Canvas>();
            var parent = canvas == null ? transform : canvas.transform;
            var hasLegacyMotionUi = motionIdleButton != null || motionNodButton != null ||
                                    motionShakeButton != null || motionThinkButton != null ||
                                    motionListenButton != null || motionHappyButton != null ||
                                    motionStopButton != null;
            if (!hasLegacyMotionUi)
                CreateRuntimeLegacyMotionUi(parent);

            // 新场景可保留右侧聊天卡片里的旧动作；新增情绪和倍率统一在左下控制面板。
            // 完全没有旧 UI 的旧场景则把全部预览控制合并到同一个运行时面板。
            EnsureRuntimeEmotionMotionUi(parent);
        }

        private void CreateRuntimeLegacyMotionUi(Transform parent)
        {
            // 旧场景没有动作区域时，把控制条放在人物左下方的独立区域。
            // 这里固定为旧动作、四排情绪、倍率控制，并限制在屏幕左下区域。
            var rowObject = new GameObject("Motion Preview (Runtime)", typeof(RectTransform), typeof(Image));
            rowObject.transform.SetParent(parent, false);
            var row = rowObject.GetComponent<RectTransform>();
            SetAnchors(row, new Vector2(0.03f, 0.02f), new Vector2(0.36f, 0.37f), Vector2.zero, Vector2.zero);
            rowObject.GetComponent<Image>().color = new Color(0.08f, 0.16f, 0.17f, 0.76f);
            runtimeMotionRoot = row;

            // 从上到下：旧动作 7 个、五排扩展预览、倍率一排。
            CreateRuntimeMotionButton(row, "待机", 0.01f, 0.14f, 0.80f, 0.98f, out motionIdleButton);
            CreateRuntimeMotionButton(row, "点头", 0.15f, 0.28f, 0.80f, 0.98f, out motionNodButton);
            CreateRuntimeMotionButton(row, "摇头", 0.29f, 0.42f, 0.80f, 0.98f, out motionShakeButton);
            CreateRuntimeMotionButton(row, "思考", 0.43f, 0.56f, 0.80f, 0.98f, out motionThinkButton);
            CreateRuntimeMotionButton(row, "倾听", 0.57f, 0.70f, 0.80f, 0.98f, out motionListenButton);
            CreateRuntimeMotionButton(row, "开心", 0.71f, 0.84f, 0.80f, 0.98f, out motionHappyButton);
            CreateRuntimeMotionButton(row, "停止", 0.85f, 0.98f, 0.80f, 0.98f, out motionStopButton);
            CreateRuntimeMotionButton(row, "幅−", 0.02f, 0.17f, 0.02f, 0.14f, out motionAmplitudeDownButton);
            CreateRuntimeMotionButton(row, "幅＋", 0.18f, 0.33f, 0.02f, 0.14f, out motionAmplitudeUpButton);
            CreateRuntimeMotionButton(row, "速−", 0.42f, 0.57f, 0.02f, 0.14f, out motionSpeedDownButton);
            CreateRuntimeMotionButton(row, "速＋", 0.58f, 0.73f, 0.02f, 0.14f, out motionSpeedUpButton);
            motionTuningText = CreateRuntimeMotionText(row, 0.75f, 0.99f, 0.02f, 0.14f);
        }

        private void EnsureRuntimeEmotionMotionUi(Transform parent)
        {
            var hasAllEmotionButtons = motionSurpriseButton != null && motionCuriousButton != null &&
                                       motionShyButton != null && motionSadButton != null &&
                                       motionAngryButton != null && motionWinkButton != null &&
                                       motionSorrowButton != null && motionCryButton != null &&
                                       motionBawlButton != null && motionWarmButton != null &&
                                       motionSeriousButton != null && motionLookLeftButton != null &&
                                       motionLookRightButton != null && motionLeanButton != null &&
                                       motionTiltButton != null && motionEmphasisButton != null &&
                                       motionStartleButton != null && motionDiscoverButton != null &&
                                       motionGloomButton != null && motionSighButton != null;
            var hasTuningUi = motionAmplitudeDownButton != null && motionAmplitudeUpButton != null &&
                              motionSpeedDownButton != null && motionSpeedUpButton != null &&
                              motionTuningText != null;
            if (hasAllEmotionButtons && hasTuningUi)
                return;

            var row = runtimeMotionRoot;
            if (row == null)
            {
                var rowObject = new GameObject("Emotion Preview (Runtime)", typeof(RectTransform), typeof(Image));
                rowObject.transform.SetParent(parent, false);
                row = rowObject.GetComponent<RectTransform>();
                SetAnchors(row, new Vector2(0.03f, 0.02f), new Vector2(0.36f, 0.37f), Vector2.zero, Vector2.zero);
                rowObject.GetComponent<Image>().color = new Color(0.08f, 0.16f, 0.17f, 0.76f);
                runtimeMotionRoot = row;
            }

            // 二十个扩展预览统一为五排四列；所有按钮都留在左下控制面板内。
            CreateRuntimeEmotionButtonIfMissing(row, "MotionSurprise", "惊讶·！", 0.01f, 0.245f, 0.68f, 0.79f,
                ref motionSurpriseButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionCurious", "疑惑", 0.255f, 0.49f, 0.68f, 0.79f,
                ref motionCuriousButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionShy", "害羞", 0.50f, 0.735f, 0.68f, 0.79f,
                ref motionShyButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionSad", "失落", 0.745f, 0.99f, 0.68f, 0.79f,
                ref motionSadButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionAngry", "生气", 0.01f, 0.245f, 0.55f, 0.66f,
                ref motionAngryButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionWink", "眨眼", 0.255f, 0.49f, 0.55f, 0.66f,
                ref motionWinkButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionSorrow", "悲伤", 0.50f, 0.735f, 0.55f, 0.66f,
                ref motionSorrowButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionCry", "哭泣", 0.745f, 0.99f, 0.55f, 0.66f,
                ref motionCryButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionBawl", "大哭", 0.01f, 0.245f, 0.42f, 0.53f,
                ref motionBawlButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionWarm", "温柔", 0.255f, 0.49f, 0.42f, 0.53f,
                ref motionWarmButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionSerious", "严肃", 0.50f, 0.735f, 0.42f, 0.53f,
                ref motionSeriousButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionLookLeft", "向左看", 0.745f, 0.99f, 0.42f, 0.53f,
                ref motionLookLeftButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionLookRight", "向右看", 0.01f, 0.245f, 0.29f, 0.40f,
                ref motionLookRightButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionLean", "前倾·试作", 0.255f, 0.49f, 0.29f, 0.40f,
                ref motionLeanButton, 18);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionTilt", "歪头", 0.50f, 0.735f, 0.29f, 0.40f,
                ref motionTiltButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionEmphasis", "强调", 0.745f, 0.99f, 0.29f, 0.40f,
                ref motionEmphasisButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionStartle", "受惊·闪电", 0.01f, 0.245f, 0.16f, 0.27f,
                ref motionStartleButton, 18);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionDiscover", "发现/回看", 0.255f, 0.49f, 0.16f, 0.27f,
                ref motionDiscoverButton, 18);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionGloom", "黑化", 0.50f, 0.735f, 0.16f, 0.27f,
                ref motionGloomButton);
            CreateRuntimeEmotionButtonIfMissing(row, "MotionSigh", "无奈·叹气", 0.745f, 0.99f, 0.16f, 0.27f,
                ref motionSighButton, 18);

            if (!hasTuningUi)
            {
                CreateRuntimeMotionButtonIfMissing(row, "幅−", 0.02f, 0.17f, 0.02f, 0.14f,
                    ref motionAmplitudeDownButton);
                CreateRuntimeMotionButtonIfMissing(row, "幅＋", 0.18f, 0.33f, 0.02f, 0.14f,
                    ref motionAmplitudeUpButton);
                CreateRuntimeMotionButtonIfMissing(row, "速−", 0.42f, 0.57f, 0.02f, 0.14f,
                    ref motionSpeedDownButton);
                CreateRuntimeMotionButtonIfMissing(row, "速＋", 0.58f, 0.73f, 0.02f, 0.14f,
                    ref motionSpeedUpButton);
                if (motionTuningText == null)
                    motionTuningText = CreateRuntimeMotionText(row, 0.75f, 0.99f, 0.02f, 0.14f);
            }
        }

        private void CreateRuntimeEmotionButtonIfMissing(Transform parent, string objectName, string label,
            float minX, float maxX, float minY, float maxY, ref Button button, int fontSize = 22)
        {
            if (button != null)
            {
                var existingText = button.GetComponentInChildren<Text>(true);
                if (existingText != null)
                {
                    existingText.text = label;
                    existingText.fontSize = fontSize;
                }
                return;
            }
            CreateRuntimeMotionButton(parent, label, minX, maxX, minY, maxY, out button);
            button.gameObject.name = objectName;
            button.GetComponentInChildren<Text>().fontSize = fontSize;
        }

        private void CreateRuntimeMotionButtonIfMissing(Transform parent, string label,
            float minX, float maxX, float minY, float maxY, ref Button button)
        {
            if (button != null)
                return;
            CreateRuntimeMotionButton(parent, label, minX, maxX, minY, maxY, out button);
        }

        private void CreateRuntimeMotionButton(Transform parent, string label, float minX, float maxX,
            float minY, float maxY, out Button button)
        {
            var buttonObject = new GameObject("Motion " + label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            SetAnchors(rect, new Vector2(minX, minY), new Vector2(maxX, maxY), Vector2.zero, Vector2.zero);
            var image = buttonObject.GetComponent<Image>();
            image.color = label == "停止"
                ? new Color(0.74f, 0.80f, 0.80f, 1f)
                : new Color(0.42f, 0.60f, 0.61f, 1f);
            button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            SetAnchors(textRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var text = textObject.GetComponent<Text>();
            text.font = GetFallbackFont();
            text.fontSize = minY > 0.4f ? 22 : 18;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            text.raycastTarget = false;
        }

        private Text CreateRuntimeMotionText(Transform parent, float minX, float maxX, float minY, float maxY)
        {
            var textObject = new GameObject("Motion Tuning", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            SetAnchors(rect, new Vector2(minX, minY), new Vector2(maxX, maxY), Vector2.zero, Vector2.zero);
            var text = textObject.GetComponent<Text>();
            text.font = GetFallbackFont();
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>供 QA runtime 组件在运行时替换依赖。</summary>
        public void Bind(DialogueClient dialogueClient, ZPerformanceController controller)
        {
            if (isActiveAndEnabled)
                OnDisable();
            client = dialogueClient;
            performanceController = controller;
            if (isActiveAndEnabled)
                OnEnable();
        }

        private void OnHealthUpdated(DialogueClient.HealthResponse health)
        {
            if (health == null)
            {
                SetStatus("健康检查无响应", true);
                return;
            }

            var provider = string.IsNullOrWhiteSpace(health.provider) ? "本地服务" : health.provider;
            SetStatus(health.configured ? "已连接 · " + provider : "服务已连接，但未配置模型", !health.configured);
        }

        private void OnSessionLoaded(DialogueClient.SessionResponse session)
        {
            if (session == null)
                return;

            if (saveIdInput != null && !saveIdInput.isFocused && !string.IsNullOrWhiteSpace(session.save_id))
                saveIdInput.text = session.save_id;

            UpdateMemoryText(session.memories);
            if (hasReceivedSession && sessionRefreshGeneration != viewGeneration)
                return;

            ClearHistoryView();
            if (session.history != null)
            {
                foreach (var item in session.history)
                {
                    if (item == null)
                        continue;
                    var content = string.IsNullOrWhiteSpace(item.content) ? item.text : item.content;
                    if (!string.IsNullOrWhiteSpace(content))
                        AppendHistory(RoleLabel(item.role), content.Trim());
                }
            }

            hasReceivedSession = true;
            ScrollToBottom();
        }

        private void OnTurnStarted(string turnId, string message)
        {
            viewGeneration++;
            AppendHistory("你", message);
            SetStatus("正在生成回复…", false);
            performanceController?.CancelCurrentPerformance();
            if (inputField != null)
                inputField.ActivateInputField();
        }

        private void OnChatTurnReady(DialogueClient.ChatResponse response)
        {
            if (response == null || response.segments == null)
                return;

            // 先完整显示，再 ACK；服务端只会在 ACK 后持久化本轮和记忆。
            foreach (var segment in response.segments)
            {
                if (segment != null && !string.IsNullOrWhiteSpace(segment.text))
                    AppendHistory("Z", segment.text.Trim());
            }

            performanceController?.PlaySegments(response.turn_id, response.segments);
            SetStatus("已显示，正在保存…", false);
            client?.AcknowledgeTurn(response.turn_id);
            ScrollToBottom();
        }

        private void OnTurnAcknowledged(string turnId, DialogueClient.AckResponse ack)
        {
            var count = ack != null && ack.memory_count > 0 ? ack.memory_count : client?.LastMemoryCount ?? 0;
            SetStatus("已保存 · 记忆 " + count, false);

            // 只刷新记忆时保留本地刚显示的历史，避免异步刷新覆盖新回合。
            sessionRefreshGeneration = viewGeneration;
            client?.LoadSession();
        }

        private void OnTurnCancelled(string turnId)
        {
            performanceController?.CancelCurrentPerformance();
            SetStatus("本回合已取消", false);
        }

        private void OnRequestFailed(string turnId, string error)
        {
            var suffix = client != null && turnId == client.PendingAckTurnId
                ? "；再次点击发送可重试保存"
                : string.Empty;
            SetStatus((string.IsNullOrWhiteSpace(error) ? "连接失败" : error) + suffix, true);
        }

        private void AppendHistory(string role, string content)
        {
            if (historyRoot == null)
                return;

            Text item;
            if (historyItemPrefab != null)
            {
                item = Instantiate(historyItemPrefab, historyRoot);
                item.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("HistoryItem", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(historyRoot, false);
                item = go.GetComponent<Text>();
                item.fontSize = 24;
                item.horizontalOverflow = HorizontalWrapMode.Wrap;
                item.verticalOverflow = VerticalWrapMode.Overflow;
            }

            item.text = string.IsNullOrWhiteSpace(role) ? content : role + "  " + content;
            item.font = item.font == null ? GetFallbackFont() : item.font;
            historyItems.Add(item);
            ScrollToBottom();
        }

        public void ClearHistoryView()
        {
            for (var i = historyItems.Count - 1; i >= 0; i--)
            {
                if (historyItems[i] != null)
                    Destroy(historyItems[i].gameObject);
            }
            historyItems.Clear();
        }

        private void UpdateMemoryText(DialogueClient.MemoryEntry[] memories)
        {
            if (memoryText == null)
                return;

            var count = memories == null ? 0 : memories.Length;
            if (count == 0)
            {
                memoryText.text = "记忆  0\n暂无已确认记忆";
                return;
            }

            var lines = new List<string> { "记忆  " + count };
            var max = Mathf.Min(count, 5);
            for (var i = 0; i < max; i++)
            {
                var memory = memories[i];
                if (memory == null || !memory.active)
                    continue;
                var value = string.IsNullOrWhiteSpace(memory.value) ? "（空）" : memory.value;
                lines.Add("· " + value);
            }
            memoryText.text = string.Join("\n", lines);
        }

        private void SetStatus(string message, bool isError)
        {
            if (statusText == null)
                return;
            statusText.text = message ?? string.Empty;
            statusText.color = isError ? new Color(0.68f, 0.24f, 0.28f, 1f) : normalStatusColor;
        }

        private void ScrollToBottom()
        {
            if (historyScrollRect == null)
                return;
            Canvas.ForceUpdateCanvases();
            historyScrollRect.verticalNormalizedPosition = 0f;
        }

        private void ApplyPreferredChineseFont()
        {
            if (!preferPlatformChineseFont)
                return;

            var font = GetPlatformChineseFont();
            if (font == null)
                return;

            foreach (var text in GetComponentsInChildren<Text>(true))
                text.font = font;
        }

        private Font GetFallbackFont()
        {
            return GetPlatformChineseFont() ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private Font GetPlatformChineseFont()
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(
                    new[]
                    {
                        "PingFang SC",
                        "PingFang HK",
                        "Heiti SC",
                        "Heiti TC",
                        "Microsoft YaHei",
                        "Noto Sans CJK SC",
                    },
                    Mathf.Max(12, platformFontSize));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string RoleLabel(string role)
        {
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                return "你";
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
                return "Z";
            return role ?? string.Empty;
        }
    }
}
