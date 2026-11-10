using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;

namespace ZLive.ModelPerformance
{
    public enum ZFaceExpression
    {
        Neutral,
        Smile,
        Surprise,
        Angry,
        Troubled,
        Blush,
        Cry,
        Bawl,
        Sigh,
        Gloom,
    }

    public enum ZGesture
    {
        None,
        Wave,
        Explain,
        Point,
    }

    /// <summary>
    /// 由控制器生成的连续动作。动作只写入头部和身体的连续参数，
    /// 与 exp3 姿势、表情、眨眼保持分层。
    /// </summary>
    public enum ZMotionPreset
    {
        Idle,
        Nod,
        Shake,
        Think,
        Listen,
        Happy,
        Surprise,
        Curious,
        Shy,
        Sad,
        Angry,
        Wink,
        Sorrow,
        Cry,
        Bawl,
        Warm,
        Serious,
        LookLeft,
        LookRight,
        Lean,
        Tilt,
        Emphasis,
        Startle,
        Discover,
        Gloom,
        Sigh,
    }

    public enum ZGaze
    {
        Player,
        Away,
    }

    /// <summary>从最终 Z Model.cdi3 读取的 32 个参数及其用途。</summary>
    [Serializable]
    public sealed class ZParameterDescriptor
    {
        public string id;
        public string group;
        public string purpose;

        public ZParameterDescriptor(string id, string group, string purpose)
        {
            this.id = id;
            this.group = group;
            this.purpose = purpose;
        }
    }

    /// <summary>
    /// Z Live2D 表演适配层。
    ///
    /// 表情和手势只写入 cdi3 已确认的白名单参数，不调用 ExpressionController。
    /// 组件实现 ICubismUpdatable，由 CubismUpdateController 在统一的 OnLateUpdate
    /// 阶段调用；执行顺序默认 1000，需和导入模型上的其他 Cubism 更新组件一起验收。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZPerformanceController : MonoBehaviour, ICubismUpdatable
    {
        private static readonly ZParameterDescriptor[] s_parameterDescriptors =
        {
            new ZParameterDescriptor("ParamAngleX", "head", "头部 X 角度（连续动作）"),
            new ZParameterDescriptor("ParamAngleY", "head", "头部 Y 角度（连续动作）"),
            new ZParameterDescriptor("ParamAngleZ", "head", "头部 Z 角度（连续动作）"),
            new ZParameterDescriptor("ParamBodyAngleZ", "body", "身体 Z 角度（连续动作）"),
            new ZParameterDescriptor("ParamBreath", "idle", "轻呼吸"),
            new ZParameterDescriptor("ParamEyeLOpen", "blink", "左眼开合"),
            new ZParameterDescriptor("ParamEyeROpen", "blink", "右眼开合"),
            new ZParameterDescriptor("ParamEyeSmile", "face", "smile.exp3：眼睛微笑"),
            new ZParameterDescriptor("ParamEyeBallX", "gaze", "视线 X"),
            new ZParameterDescriptor("ParamEyeBallY", "gaze", "视线 Y"),
            new ZParameterDescriptor("ParamBrowForm", "face", "angry/troubled.exp3：眉形"),
            new ZParameterDescriptor("ParamMouthOpenY", "mouth", "嘴部开合（叹气由 companion material binding 控制；聊天语音不覆盖）"),
            new ZParameterDescriptor("ParamMouthVariant", "face", "预览表情/ surprise.exp3：嘴型变体"),
            new ZParameterDescriptor("ParamHairSideL", "hair", "左侧发丝（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairSideR", "hair", "右侧发丝（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairBang", "hair", "刘海（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairBack", "hair", "后发（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairPitch", "hair", "发丝俯仰（控制器不主动写）"),
            new ZParameterDescriptor("ParamPosePoint", "gesture", "point.exp3：指向姿势"),
            new ZParameterDescriptor("ParamPoseExplain", "gesture", "explain.exp3：解释姿势"),
            new ZParameterDescriptor("ParamPoseWave", "gesture", "wave.exp3：挥手姿势"),
            new ZParameterDescriptor("ParamBodyAngleX", "body", "身体 X 角度（连续动作）"),
            new ZParameterDescriptor("ParamBodyAngleY", "body", "身体 Y 角度（连续动作）"),
            new ZParameterDescriptor("ParamHairSideL2", "hair", "左侧发丝层 2（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairSideR2", "hair", "右侧发丝层 2（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairBack2", "hair", "后发层 2（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairSideL3", "hair", "左侧发丝层 3（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairSideR3", "hair", "右侧发丝层 3（控制器不主动写）"),
            new ZParameterDescriptor("ParamHairBack3", "hair", "后发层 3（控制器不主动写）"),
            new ZParameterDescriptor("ParamMouthForm", "mouth", "预览表情嘴形（聊天语音不覆盖）"),
            new ZParameterDescriptor("ParamCheek", "face", "blush.exp3：脸颊"),
            new ZParameterDescriptor("ParamDangle1", "physics", "物理摆动（控制器不主动写）"),
        };

        private static readonly ExpressionBinding[] s_expressionBindings =
        {
            new ExpressionBinding(ZFaceExpression.Smile, "ParamEyeSmile", 1f, "smile.exp3"),
            new ExpressionBinding(ZFaceExpression.Smile, "ParamMouthForm", 0.60f, "smile.exp3"),
            new ExpressionBinding(ZFaceExpression.Surprise, "ParamMouthVariant", 1f, "surprise.exp3"),
            new ExpressionBinding(ZFaceExpression.Surprise, "ParamBrowForm", 0.25f, "surprise.exp3"),
            new ExpressionBinding(ZFaceExpression.Angry, "ParamBrowForm", -1f, "angry.exp3"),
            new ExpressionBinding(ZFaceExpression.Angry, "ParamMouthVariant", -1f, "angry.exp3"),
            new ExpressionBinding(ZFaceExpression.Angry, "ParamMouthForm", -0.40f, "angry.exp3"),
            new ExpressionBinding(ZFaceExpression.Troubled, "ParamBrowForm", 1f, "troubled.exp3"),
            new ExpressionBinding(ZFaceExpression.Troubled, "ParamMouthVariant", -1f, "troubled.exp3"),
            new ExpressionBinding(ZFaceExpression.Troubled, "ParamMouthForm", -0.25f, "troubled.exp3"),
            new ExpressionBinding(ZFaceExpression.Blush, "ParamCheek", 1f, "blush.exp3"),
            new ExpressionBinding(ZFaceExpression.Blush, "ParamEyeSmile", 0f, "blush.exp3"),
            new ExpressionBinding(ZFaceExpression.Blush, "ParamMouthForm", 0.35f, "blush.exp3"),
        };

        private static readonly GestureBinding[] s_gestureBindings =
        {
            new GestureBinding(ZGesture.Wave, "ParamPoseWave", "wave.exp3"),
            new GestureBinding(ZGesture.Explain, "ParamPoseExplain", "explain.exp3"),
            new GestureBinding(ZGesture.Point, "ParamPosePoint", "point.exp3"),
        };

        private static readonly string[] s_motionParameterIds =
        {
            "ParamAngleX",
            "ParamAngleY",
            "ParamAngleZ",
            "ParamBodyAngleX",
            "ParamBodyAngleY",
            "ParamBodyAngleZ",
        };

        private static readonly ReadOnlyCollection<ZParameterDescriptor> s_readOnlyDescriptors =
            Array.AsReadOnly(s_parameterDescriptors);

        [Header("Cubism")]
        [SerializeField] private CubismModel cubismModel;
        [Tooltip("模型上的 CubismUpdateController。为空时自动从子层级查找。")]
        [SerializeField] private CubismUpdateController cubismUpdateController;
        // SDK R4.2 的 Expression/EyeBlink/Mouth/Physics 顺序为 300/400/500/800，
        // Render 为 10000；1000 置于其后、渲染前。仍需在实际 Prefab 中做运行时验收。
        [SerializeField] private int executionOrder = 1000;

        [Header("平滑")]
        [SerializeField, Min(0.1f)] private float blendSpeed = 8f;

        [Header("连续动作预设")]
        [SerializeField, Range(0.01f, 0.3f)] private float headMotionRangeRatio = 0.12f;
        [SerializeField, Range(0.01f, 0.3f)] private float bodyMotionRangeRatio = 0.07f;
        [SerializeField, Range(0.5f, 4f)] private float activeHeadMotionGain = 3f;
        [SerializeField, Range(0.5f, 8f)] private float activeBodyMotionGain = 5f;
        [SerializeField, Range(0.5f, 2f)] private float idleMotionGain = 1.25f;
        [SerializeField, Min(0.1f)] private float motionBlendSpeed = 7f;
        [SerializeField, Min(0.5f)] private float idleMotionPeriod = 5.2f;
        [SerializeField, Range(0.25f, 2f)] private float motionAmplitudeMultiplier = 1f;
        [SerializeField, Range(0.25f, 2f)] private float motionSpeedMultiplier = 1f;
        [Tooltip("运行时暂停模型上的旧 Animator，避免动画资产与程序化动作争用参数。禁用组件时恢复原状态。")]
        [SerializeField] private bool suspendAnimatorDuringRuntime = true;

        [Header("Idle")]
        [SerializeField, Range(0f, 0.15f)] private float breathAmplitude = 0.035f;
        [SerializeField, Min(0.5f)] private float breathPeriod = 4f;
        [SerializeField, Min(0.2f)] private float blinkMinInterval = 2.2f;
        [SerializeField, Min(0.3f)] private float blinkMaxInterval = 5.4f;
        [SerializeField, Min(0.03f)] private float blinkDuration = 0.12f;
        [SerializeField, Range(-1f, 1f)] private float awayGazeX = -0.65f;

        private readonly Dictionary<string, CubismParameter> parameters =
            new Dictionary<string, CubismParameter>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> targets =
            new Dictionary<string, float>(StringComparer.Ordinal);
        // 不以 CubismParameter.Value 作为平滑源。ParameterStore、Animator 或其他
        // updater 可能在本组件之后写回 Value；独立状态可避免表情每帧被重新稀释。
        private readonly Dictionary<string, float> smoothedValues =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly HashSet<string> activeWriters = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> warnedMissingIds = new HashSet<string>(StringComparer.Ordinal);



        private ZFaceExpression currentExpression = ZFaceExpression.Neutral;
        private ZGesture currentGesture = ZGesture.None;
        private ZGaze currentGaze = ZGaze.Player;
        private ZMotionPreset currentMotion = ZMotionPreset.Idle;
        private float motionElapsed;
        private float expressionElapsed;
        private int motionTriggerSerial;
        private float nextBlinkIn;
        private float blinkRemaining;
        private bool warnedNoModel;
        private bool warnedNoUpdateController;
        private bool cacheBuilt;
        private readonly List<AnimatorState> suspendedAnimatorStates = new List<AnimatorState>();
        private bool animatorStatesCaptured;
        private bool previewPresentationActive;
        private bool previewEyeControlActive;

        public static IReadOnlyList<ZParameterDescriptor> ParameterWhitelist => s_readOnlyDescriptors;
        public CubismModel CubismModel => cubismModel;
        public CubismUpdateController CubismUpdateController => cubismUpdateController;
        public bool IsModelReady => cacheBuilt && cubismModel != null && parameters.Count > 0;
        public bool HasCubismUpdateController => cubismUpdateController != null;
        public ZFaceExpression CurrentExpression => currentExpression;
        public ZGesture CurrentGesture => currentGesture;
        public ZGaze CurrentGaze => currentGaze;
        public ZMotionPreset CurrentMotion => currentMotion;
        public float MotionElapsed => motionElapsed;
        public float ExpressionElapsed => expressionElapsed;
        public int MotionTriggerSerial => motionTriggerSerial;
        public float MotionAmplitudeMultiplier => motionAmplitudeMultiplier;
        public float MotionSpeedMultiplier => motionSpeedMultiplier;
        public bool IsPreviewPresentationActive => previewPresentationActive;

        /// <summary>
        /// 独立 Sigh 脸部素材的生命周期。手动预览和聊天 Sigh 都可以读取这些权重；
        /// 停止、打断、自然结束和禁用组件都会自动回到 false/0。
        /// </summary>
        public bool SighFaceActive => enabled &&
            ((previewPresentationActive && currentMotion == ZMotionPreset.Sigh) ||
             (!previewPresentationActive && currentExpression == ZFaceExpression.Sigh));

        /// <summary>
        /// Sigh 下垂闭眼素材权重。眼睛 Drawable 的原生闭合/睁开 opacity 负责交接，
        /// 不在新旧闭眼线之间做二次淡入，避免重影。
        /// </summary>
        public float SighEyeArtWeight => SighFaceActive ? 1f : 0f;

        /// <summary>
        /// Sigh 横向叹气嘴素材权重。手动预览沿用 .08-.25 进入、.25-.96 保持、
        /// .96-1.44 退出；聊天 Sigh 在 .08-.25 进入后保持到分段结束。
        /// 该权重独立于 Cubism 的 MouthVariant，避免旧 O 嘴成为新素材的状态来源。
        /// </summary>
        public float SighMouthArtWeight
        {
            get
            {
                if (!SighFaceActive || LiveSpeechMouth)
                    return 0f;
                return previewPresentationActive
                    ? GetSighMouthArtWeight(motionElapsed)
                    : GetChatSighMouthArtWeight(expressionElapsed);
            }
        }

        /// <summary>返回预览动作的标称时长（秒），不受速度倍率影响。</summary>
        public static float GetPreviewDuration(ZMotionPreset motion)
        {
            switch (motion)
            {
                case ZMotionPreset.Nod: return 1.35f;
                case ZMotionPreset.Shake: return 1.45f;
                case ZMotionPreset.Think: return 2.4f;
                case ZMotionPreset.Listen: return 2.8f;
                case ZMotionPreset.Happy: return 1.8f;
                case ZMotionPreset.Surprise: return 2.0f;
                case ZMotionPreset.Curious: return 2.7f;
                case ZMotionPreset.Shy: return 3.1f;
                case ZMotionPreset.Sad: return 3.2f;
                case ZMotionPreset.Angry: return 2.6f;
                case ZMotionPreset.Wink: return 1.8f;
                case ZMotionPreset.Sorrow: return 3.8f;
                case ZMotionPreset.Cry: return 4.4f;
                case ZMotionPreset.Bawl: return 4.8f;
                case ZMotionPreset.Warm: return 3.0f;
                case ZMotionPreset.Serious: return 3.0f;
                case ZMotionPreset.LookLeft: return 2.6f;
                case ZMotionPreset.LookRight: return 2.6f;
                case ZMotionPreset.Lean: return 2.8f;
                case ZMotionPreset.Tilt: return 2.8f;
                case ZMotionPreset.Emphasis: return 1.6f;
                case ZMotionPreset.Startle: return 1.8f;
                case ZMotionPreset.Discover: return 3.2f;
                case ZMotionPreset.Gloom: return 2.8f;
                case ZMotionPreset.Sigh: return 3.4f;
                default: return 0f;
            }
        }

        public int ExecutionOrder => executionOrder;
        // 该表演控制器只在运行时驱动；不让 Cubism 编辑器阶段写入模型参数。
        public bool NeedsUpdateOnEditing => false;
        public bool HasUpdateController { get; set; }

        private struct ExpressionBinding
        {
            public readonly ZFaceExpression expression;
            public readonly string parameterId;
            public readonly float value;
            public readonly string resourceName;

            public ExpressionBinding(ZFaceExpression expression, string parameterId, float value, string resourceName)
            {
                this.expression = expression;
                this.parameterId = parameterId;
                this.value = value;
                this.resourceName = resourceName;
            }
        }

        private struct GestureBinding
        {
            public readonly ZGesture gesture;
            public readonly string parameterId;
            public readonly string resourceName;

            public GestureBinding(ZGesture gesture, string parameterId, string resourceName)
            {
                this.gesture = gesture;
                this.parameterId = parameterId;
                this.resourceName = resourceName;
            }
        }

        private struct AnimatorState
        {
            public readonly Animator animator;
            public readonly bool enabled;

            public AnimatorState(Animator animator, bool enabled)
            {
                this.animator = animator;
                this.enabled = enabled;
            }
        }

        private void Awake()
        {
            nextBlinkIn = UnityEngine.Random.Range(blinkMinInterval, blinkMaxInterval);
            ResolveCubismComponents();
            CaptureAndSuspendAnimators();
            BuildParameterCache();
            ResetPerformanceToNeutral();
        }

        private void OnEnable()
        {
            ResolveCubismComponents();
            CaptureAndSuspendAnimators();
            RefreshCubismUpdateController();
            ResetPerformanceToNeutral();
            if (nextBlinkIn <= 0f)
                nextBlinkIn = UnityEngine.Random.Range(blinkMinInterval, blinkMaxInterval);
        }

        private void Start()
        {
            ResolveCubismComponents();
            BuildParameterCache();
            RefreshCubismUpdateController();
        }

        private void OnDisable()
        {
            CancelCurrentPerformance();
            WriteDefaultsImmediately();
            RestoreAnimatorStates();
            HasUpdateController = false;
        }

        public void OnLateUpdate()
        {
            // CubismUpdateController 会保留已注册的 delegate，不会替 disabled 组件过滤调用。
            if (!enabled || Paused)
                return;
            Elapsed += Time.unscaledDeltaTime;
            EvaluateAndWrite(Time.unscaledDeltaTime);
        }

        /// <summary>模型或更新控制器后补绑定，并刷新 Cubism 的 updater 列表。</summary>
        public void RefreshCubismUpdateController()
        {
            ResolveCubismComponents();
            HasUpdateController = cubismUpdateController != null;
            if (cubismUpdateController != null)
                cubismUpdateController.Refresh();
            else if (!warnedNoUpdateController)
            {
                warnedNoUpdateController = true;
                Debug.LogWarning("[ZPerformanceController] 未找到 CubismUpdateController，表演不会更新。", this);
            }
        }





        public void CancelCurrentPerformance() => ResetPerformanceToNeutral();

        public bool Paused { get; set; }
        public bool LiveSpeechMouth { get; set; }
        public float Elapsed { get; private set; }

        private Live2D.Cubism.Framework.Expression.CubismExpressionData portraitExpression;
        public void SetPortraitExpression(Live2D.Cubism.Framework.Expression.CubismExpressionData expression) => portraitExpression=expression;

        public void SetExpression(string expression)
        {
            SetExpression(ParseExpression(expression));
        }

        public void SetExpression(ZFaceExpression expression)
        {
            portraitExpression=null;
            currentExpression = expression;
            expressionElapsed = 0f;
            foreach (var binding in s_expressionBindings)
                SetTargetToDefault(binding.parameterId);
            SetTargetToDefault("ParamMouthOpenY");

            foreach (var binding in s_expressionBindings)
            {
                if (binding.expression == expression)
                    SetTarget(binding.parameterId, binding.value);
            }

            // Chat expressions own a baseline eye openness, while the idle
            // blink below temporarily overrides it. Manual previews keep
            // their own eye curves and never inherit this chat target.
            if (!previewPresentationActive)
            {
                var eyeOpen = GetChatExpressionEyeOpen(expression);
                SetTarget("ParamEyeLOpen", eyeOpen);
                SetTarget("ParamEyeROpen", eyeOpen);
            }
        }

        public void SetGesture(string gesture)
        {
            SetGesture(ParseGesture(gesture));
        }

        public void SetGesture(ZGesture gesture)
        {
            currentGesture = gesture;
            foreach (var binding in s_gestureBindings)
            {
                SetTargetToDefault(binding.parameterId);
                if (binding.gesture == gesture)
                    SetTarget(binding.parameterId, 1f);
            }
        }

        public void SetGaze(string gaze)
        {
            SetGaze(ParseGaze(gaze));
        }

        public void SetGaze(ZGaze gaze)
        {
            currentGaze = gaze;
            SetTargetToDefault("ParamEyeBallX");
            SetTargetToDefault("ParamEyeBallY");
            if (gaze == ZGaze.Away)
                SetTarget("ParamEyeBallX", awayGazeX);
        }

        /// <summary>切换到一个可中断的连续动作。切换从当前平滑值继续，不重置参数。</summary>
        public void SetMotionPreset(string motion)
        {
            SetMotionPreset(ParseMotionPreset(motion));
        }

        public void SetMotionPreset(ZMotionPreset motion)
        {
            // 同一动作再次触发也要从头开始；这让连续点击“点头”仍然可预览。
            if (currentMotion != motion || motion != ZMotionPreset.Idle)
                motionElapsed = 0f;
            if (motion != ZMotionPreset.Idle)
                motionTriggerSerial++;
            currentMotion = motion;
            if (motion == ZMotionPreset.Idle)
                motionElapsed = 0f;
        }

        public void SetMotionAmplitudeMultiplier(float multiplier)
        {
            motionAmplitudeMultiplier = Mathf.Clamp(multiplier, 0.25f, 2f);
        }

        public void SetMotionSpeedMultiplier(float multiplier)
        {
            motionSpeedMultiplier = Mathf.Clamp(multiplier, 0.25f, 2f);
        }

        /// <summary>供 Demo 一键预览使用。预览结束后回到自然待机。</summary>
        public void PreviewMotion(string motion)
        {
            PreviewMotion(ParseMotionPreset(motion));
        }

        public void PreviewMotion(ZMotionPreset motion)
        {
            // Clear every temporary face/gaze/eye target before taking ownership
            // for the next preview. This matters when switching from Wink/Sad to
            // another preview before the old motion has naturally ended.
            SetMotionPreset(ZMotionPreset.Idle);
            ClearMotionPreviewPresentation();
            if (motion == ZMotionPreset.Idle)
            {
                return;
            }

            previewPresentationActive = true;
            previewEyeControlActive = IsPreviewEyeControlled(motion);
            SetGesture(ZGesture.None);
            SetExpression(GetPreviewExpression(motion));
            SetGaze(GetPreviewGaze(motion));
            ApplyPreviewFaceProfile(motion);
            // 预览只切换状态，动作时钟统一由 UpdateMotionTargets 推进。
            SetMotionPreset(motion);
        }

        public void StopMotionPreview()
        {
            SetMotionPreset(ZMotionPreset.Idle);
            ClearMotionPreviewPresentation();
        }

        public void ResetPerformance()
        {
            CancelCurrentPerformance();
        }

        public bool IsParameterAvailable(string id)
        {
            BuildParameterCache();
            return !string.IsNullOrEmpty(id) && parameters.ContainsKey(id);
        }

        public bool TryGetParameterValue(string id, out float value)
        {
            BuildParameterCache();
            if (parameters.TryGetValue(id ?? string.Empty, out var parameter))
            {
                value = parameter.Value;
                return true;
            }

            value = 0f;
            return false;
        }

        public bool TryGetParameterRange(string id, out float minimum, out float maximum)
        {
            BuildParameterCache();
            if (parameters.TryGetValue(id ?? string.Empty, out var parameter))
            {
                minimum = parameter.MinimumValue;
                maximum = parameter.MaximumValue;
                return true;
            }

            minimum = 0f;
            maximum = 0f;
            return false;
        }

        public bool TryGetParameterInfo(string id, out float minimum, out float maximum, out float defaultValue)
        {
            BuildParameterCache();
            if (parameters.TryGetValue(id ?? string.Empty, out var parameter))
            {
                minimum = parameter.MinimumValue;
                maximum = parameter.MaximumValue;
                defaultValue = parameter.DefaultValue;
                return true;
            }

            minimum = 0f;
            maximum = 0f;
            defaultValue = 0f;
            return false;
        }



        private void ResetPerformanceToNeutral()
        {
            previewPresentationActive = false;
            previewEyeControlActive = false;
            blinkRemaining = 0f;
            nextBlinkIn = UnityEngine.Random.Range(blinkMinInterval, blinkMaxInterval);
            SetExpression(ZFaceExpression.Neutral);
            ResetPreviewFaceTargets();
            SetGesture(ZGesture.None);
            SetGaze(ZGaze.Player);
            SetTargetToDefault("ParamEyeLOpen");
            SetTargetToDefault("ParamEyeROpen");
            SetTargetToDefault("ParamMouthOpenY");
            SetMotionPreset(ZMotionPreset.Idle);
        }

        private void EndMotionPreviewPresentation()
        {
            if (!previewPresentationActive)
                return;

            ClearMotionPreviewPresentation();
        }

        private void ClearMotionPreviewPresentation()
        {
            previewPresentationActive = false;
            previewEyeControlActive = false;
            blinkRemaining = 0f;
            nextBlinkIn = UnityEngine.Random.Range(blinkMinInterval, blinkMaxInterval);
            SetExpression(ZFaceExpression.Neutral);
            ResetPreviewFaceTargets();
            SetGesture(ZGesture.None);
            SetGaze(ZGaze.Player);
            SetTargetToDefault("ParamEyeLOpen");
            SetTargetToDefault("ParamEyeROpen");
            SetTargetToDefault("ParamMouthOpenY");
        }

        private static ZFaceExpression GetPreviewExpression(ZMotionPreset motion)
        {
            switch (motion)
            {
                case ZMotionPreset.Nod:
                case ZMotionPreset.Happy:
                    return ZFaceExpression.Smile;
                case ZMotionPreset.Shake:
                case ZMotionPreset.Think:
                    return ZFaceExpression.Troubled;
                case ZMotionPreset.Surprise:
                    return ZFaceExpression.Surprise;
                case ZMotionPreset.Curious:
                case ZMotionPreset.Sad:
                    return ZFaceExpression.Troubled;
                case ZMotionPreset.Shy:
                    return ZFaceExpression.Blush;
                case ZMotionPreset.Angry:
                    return ZFaceExpression.Angry;
                case ZMotionPreset.Wink:
                    return ZFaceExpression.Neutral;
                case ZMotionPreset.Sorrow:
                case ZMotionPreset.Cry:
                case ZMotionPreset.Bawl:
                    return ZFaceExpression.Troubled;
                case ZMotionPreset.Warm:
                case ZMotionPreset.Serious:
                case ZMotionPreset.LookLeft:
                case ZMotionPreset.LookRight:
                case ZMotionPreset.Lean:
                    return ZFaceExpression.Neutral;
                case ZMotionPreset.Tilt:
                    return ZFaceExpression.Smile;
                case ZMotionPreset.Startle:
                    return ZFaceExpression.Surprise;
                case ZMotionPreset.Emphasis:
                case ZMotionPreset.Discover:
                case ZMotionPreset.Gloom:
                case ZMotionPreset.Sigh:
                    return ZFaceExpression.Neutral;
                default:
                    return ZFaceExpression.Neutral;
            }
        }

        private static ZGaze GetPreviewGaze(ZMotionPreset motion)
        {
            return motion == ZMotionPreset.Think ? ZGaze.Away : ZGaze.Player;
        }

        private static float GetChatExpressionEyeOpen(ZFaceExpression expression)
        {
            switch (expression)
            {
                case ZFaceExpression.Angry:
                    return 0.76f;
                case ZFaceExpression.Troubled:
                    return 0.82f;
                case ZFaceExpression.Blush:
                    return 0.90f;
                case ZFaceExpression.Cry:
                case ZFaceExpression.Bawl:
                case ZFaceExpression.Sigh:
                    return 0f;
                case ZFaceExpression.Neutral:
                case ZFaceExpression.Smile:
                case ZFaceExpression.Surprise:
                case ZFaceExpression.Gloom:
                default:
                    return 1f;
            }
        }

        private static bool OwnsClosedChatEyes(ZFaceExpression expression)
        {
            return expression == ZFaceExpression.Cry ||
                   expression == ZFaceExpression.Bawl ||
                   expression == ZFaceExpression.Sigh;
        }

        private void ApplyChatExpressionOverrides()
        {
            if (previewPresentationActive)
                return;

            switch (currentExpression)
            {
                case ZFaceExpression.Cry:
                    // Reuse the approved manual Cry face: closed eyes are
                    // applied below, after the body motion has written targets.
                    SetPreviewFaceParameters(0f, 1f, 0f, 2f, -0.55f);
                    SetPreviewEyeOpen(0f, 0f);
                    break;
                case ZFaceExpression.Bawl:
                    SetPreviewFaceParameters(0f, 1f, 0f, 2f, -0.72f);
                    SetPreviewEyeOpen(0f, 0f);
                    break;
                case ZFaceExpression.Gloom:
                    SetPreviewFaceParameters(0f, 0f, 0f, -1f, -0.15f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZFaceExpression.Sigh:
                    SetPreviewFaceParameters(0f, 0.28f, 0f, 0f, 0f);
                    SetTarget("ParamMouthOpenY", 0f);
                    SetPreviewEyeOpen(0f, 0f);
                    break;
            }
        }

        private void EvaluateAndWrite(float deltaTime)
        {
            BuildParameterCache();
            if (!IsModelReady)
                return;

            var safeDeltaTime = Mathf.Max(0f, deltaTime);
            if (!previewPresentationActive && currentExpression != ZFaceExpression.Neutral)
                expressionElapsed += safeDeltaTime;

            UpdateIdleTargets(safeDeltaTime);
            UpdateMotionTargets(safeDeltaTime);
            // Chat expressions are applied after body motion so Cry/Bawl/Sigh
            // own their eye channel and their face remains after the body curve
            // returns to idle. Manual previews keep their existing curves.
            ApplyChatExpressionOverrides();
            // The game portrait authors face shape. Live gesture timing, blinking,
            // physics and audio lip sync keep ownership of their own channels.
            if(!previewPresentationActive && portraitExpression!=null){
                foreach(var entry in portraitExpression.Parameters){
                    switch(entry.Id){
                        case "ParamEyeSmile": case "ParamMouthForm": case "ParamMouthVariant":
                        case "ParamBrowForm": case "ParamCheek":
                            var value=entry.Value;
                            if(entry.Blend==CubismParameterBlendMode.Additive)value+=GetDefaultValue(entry.Id);
                            if(entry.Blend==CubismParameterBlendMode.Multiply)value*=GetDefaultValue(entry.Id);
                            SetTarget(entry.Id,value);break;
                    }
                }
            }
            foreach (var id in activeWriters)
            {
                if (!parameters.TryGetValue(id, out var parameter))
                    continue;
                if (!targets.TryGetValue(id, out var target))
                    target = parameter.DefaultValue;
                if (!smoothedValues.TryGetValue(id, out var smoothedValue))
                    smoothedValue = parameter.DefaultValue;

                var speed = IsMotionParameter(id)
                    ? motionBlendSpeed * Mathf.Clamp(motionSpeedMultiplier, 0.25f, 2f)
                    : blendSpeed;
                var factor = 1f - Mathf.Exp(-Mathf.Max(0.1f, speed) * Mathf.Max(0f, deltaTime));
                smoothedValue = Mathf.Lerp(smoothedValue, target, factor);
                smoothedValue = Mathf.Clamp(smoothedValue, parameter.MinimumValue, parameter.MaximumValue);
                smoothedValues[id] = smoothedValue;
                parameter.Value = smoothedValue;
            }
        }

        private void UpdateIdleTargets(float deltaTime)
        {
            if (parameters.ContainsKey("ParamBreath"))
            {
                var breath = Mathf.Sin(Elapsed * Mathf.PI * 2f / Mathf.Max(0.5f, breathPeriod)) * breathAmplitude;
                SetTarget("ParamBreath", GetDefaultValue("ParamBreath") + breath);
            }

            // Manual preview eye curves own both eyelids. Do not let the idle
            // auto-blink coroutine close either eye underneath Wink/Sad.
            if (previewPresentationActive && previewEyeControlActive)
            {
                blinkRemaining = 0f;
                SetTargetToDefault("ParamEyeLOpen");
                SetTargetToDefault("ParamEyeROpen");
                return;
            }

            if (!previewPresentationActive && OwnsClosedChatEyes(currentExpression))
            {
                // Cry/Bawl/Sigh own both eyelids for the whole chat segment.
                // Do not let the idle blink timer reopen them underneath the
                // approved closed-eye artwork.
                blinkRemaining = 0f;
                SetTargetNormalized("ParamEyeLOpen", 0f);
                SetTargetNormalized("ParamEyeROpen", 0f);
                return;
            }

            if (blinkRemaining > 0f)
            {
                blinkRemaining -= deltaTime;
                SetTarget("ParamEyeLOpen", 0f);
                SetTarget("ParamEyeROpen", 0f);
                if (blinkRemaining <= 0f)
                    nextBlinkIn = UnityEngine.Random.Range(blinkMinInterval, blinkMaxInterval);
            }
            else
            {
                nextBlinkIn -= deltaTime;
                var chatEyeOpen = previewPresentationActive
                    ? GetDefaultValue("ParamEyeLOpen")
                    : GetChatExpressionEyeOpen(currentExpression);
                SetTarget("ParamEyeLOpen", chatEyeOpen);
                chatEyeOpen = previewPresentationActive
                    ? GetDefaultValue("ParamEyeROpen")
                    : GetChatExpressionEyeOpen(currentExpression);
                SetTarget("ParamEyeROpen", chatEyeOpen);
                if (nextBlinkIn <= 0f)
                    blinkRemaining = Mathf.Max(0.03f, blinkDuration);
            }
        }

        private void UpdateMotionTargets(float deltaTime)
        {
            motionElapsed += deltaTime * Mathf.Clamp(motionSpeedMultiplier, 0.25f, 2f);
            for (var i = 0; i < s_motionParameterIds.Length; i++)
                SetTargetToDefault(s_motionParameterIds[i]);

            var phase = Elapsed * Mathf.PI * 2f * Mathf.Clamp(motionSpeedMultiplier, 0.25f, 2f) /
                        Mathf.Max(0.5f, idleMotionPeriod);
            switch (currentMotion)
            {
                case ZMotionPreset.Nod:
                    ApplyNodMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Shake:
                    ApplyShakeMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Think:
                    ApplyThinkMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Listen:
                    ApplyListenMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Happy:
                    ApplyHappyMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Surprise:
                    ApplySurpriseMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Curious:
                    ApplyCuriousMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Shy:
                    ApplyShyMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Sad:
                    ApplySadMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Angry:
                    ApplyAngryMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Wink:
                    ApplyWinkMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Sorrow:
                    ApplySorrowMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Cry:
                    ApplyCryMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Bawl:
                    ApplyBawlMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Warm:
                    ApplyWarmMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Serious:
                    ApplySeriousMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.LookLeft:
                    ApplyLookMotion(motionElapsed, true);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.LookRight:
                    ApplyLookMotion(motionElapsed, false);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Lean:
                    ApplyLeanMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Tilt:
                    ApplyTiltMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Emphasis:
                    ApplyEmphasisMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Startle:
                    ApplyStartleMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Discover:
                    ApplyDiscoverMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Gloom:
                    ApplyGloomMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                case ZMotionPreset.Sigh:
                    ApplySighMotion(motionElapsed);
                    if (motionElapsed >= GetPreviewDuration(currentMotion))
                    {
                        SetMotionPreset(ZMotionPreset.Idle);
                        EndMotionPreviewPresentation();
                    }
                    break;
                default:
                    // 待机只做很小的漂移，避免模型在没有回复时完全僵住。
                    SetMotionOffset("ParamAngleZ", 0.18f * Mathf.Sin(phase));
                    SetMotionOffset("ParamAngleX", 0.08f * Mathf.Sin(phase * 0.61f + 0.7f));
                    SetMotionOffset("ParamBodyAngleZ", 0.12f * Mathf.Sin(phase * 0.53f + 1.1f));
                    break;
            }
        }

        private void ApplyNodMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Nod);
            var envelope = Mathf.Exp(-1.55f * elapsed);
            var wave = Mathf.Sin(elapsed * Mathf.PI * 2f * 1.15f) * envelope;
            SetMotionOffset("ParamAngleY", -0.9f * wave);
            SetMotionOffset("ParamBodyAngleY", -0.28f * wave);
            SetMotionOffset("ParamAngleZ", 0.14f * wave);
        }

        private void ApplyShakeMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Shake);
            var envelope = Mathf.Exp(-1.25f * elapsed);
            var wave = Mathf.Sin(elapsed * Mathf.PI * 2f * 2.15f) * envelope;
            SetMotionOffset("ParamAngleX", 0.72f * wave);
            SetMotionOffset("ParamBodyAngleX", 0.22f * wave);
            SetMotionOffset("ParamAngleZ", 0.10f * wave);
        }

        private void ApplyThinkMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Think);
            var settle = 1f - Mathf.Exp(-2.4f * elapsed);
            var exit = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(1.8f, 2.4f, elapsed));
            var slowWave = Mathf.Sin(elapsed * Mathf.PI * 2f * 0.48f);
            SetMotionOffset("ParamAngleZ", (0.48f * settle + 0.15f * slowWave) * exit);
            SetMotionOffset("ParamAngleY", -0.22f * settle * exit);
            SetMotionOffset("ParamBodyAngleZ", 0.20f * settle * exit);
        }

        private void ApplyListenMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Listen);
            var settle = 1f - Mathf.Exp(-2.8f * elapsed);
            var exit = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(2.15f, 2.8f, elapsed));
            var breathe = Mathf.Sin(elapsed * Mathf.PI * 2f * 0.36f);
            SetMotionOffset("ParamAngleZ", (-0.32f * settle + 0.08f * breathe) * exit);
            SetMotionOffset("ParamAngleY", 0.12f * settle * exit);
            SetMotionOffset("ParamBodyAngleZ", -0.16f * settle * exit);
        }

        private void ApplyHappyMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Happy);
            var envelope = Mathf.Exp(-1.35f * elapsed);
            var wave = Mathf.Sin(elapsed * Mathf.PI * 2f * 1.45f) * envelope;
            SetMotionOffset("ParamAngleY", -0.48f * wave);
            SetMotionOffset("ParamAngleZ", 0.28f * wave);
            SetMotionOffset("ParamBodyAngleZ", 0.32f * wave);
        }

        private void ApplySurpriseMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Surprise);
            // 快抬头，短停，再把头部和身体平滑带回中性；保持 O 嘴和双眼睁开。
            var rise = EaseInOut(Mathf.InverseLerp(0f, 0.30f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(0.78f, GetPreviewDuration(ZMotionPreset.Surprise), elapsed));
            var lift = rise * (1f - returnToNeutral);
            SetMotionOffset("ParamAngleY", 0.82f * lift);
            SetMotionOffset("ParamBodyAngleY", 0.26f * lift);
            SetMotionOffset("ParamAngleZ", -0.12f * lift);
            SetPreviewGaze(0f, 0.30f * lift);
        }

        private void ApplyCuriousMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Curious);
            // 歪头后短暂移开视线，后半段明确看回玩家。
            var tilt = EaseInOut(Mathf.InverseLerp(0f, 0.58f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(1.85f, GetPreviewDuration(ZMotionPreset.Curious), elapsed));
            var hold = tilt * (1f - returnToNeutral);
            var lookAway = EaseInOut(Mathf.InverseLerp(0.18f, 0.58f, elapsed)) *
                           (1f - EaseInOut(Mathf.InverseLerp(1.40f, 2.55f, elapsed)));
            SetMotionOffset("ParamAngleZ", 0.58f * hold);
            SetMotionOffset("ParamBodyAngleZ", 0.20f * hold);
            SetMotionOffset("ParamAngleY", -0.10f * hold);
            SetPreviewGaze(Mathf.Lerp(0f, awayGazeX, lookAway), 0.08f * lookAway);
        }

        private void ApplyShyMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Shy);
            // 低头、侧看，再在结尾回到玩家；表情层维持脸红。
            var lower = EaseInOut(Mathf.InverseLerp(0f, 0.82f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(2.35f, GetPreviewDuration(ZMotionPreset.Shy), elapsed));
            var hold = lower * (1f - returnToNeutral);
            var sideLook = EaseInOut(Mathf.InverseLerp(0.48f, 0.92f, elapsed)) *
                           (1f - EaseInOut(Mathf.InverseLerp(2.05f, 2.82f, elapsed)));
            SetMotionOffset("ParamAngleY", -0.62f * hold);
            SetMotionOffset("ParamAngleZ", -0.26f * hold);
            SetMotionOffset("ParamBodyAngleY", -0.18f * hold);
            SetMotionOffset("ParamBodyAngleZ", -0.14f * hold);
            SetPreviewGaze(Mathf.Lerp(0f, awayGazeX, sideLook), -0.14f * hold);
        }

        private void ApplySadMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Sad);
            // 失落是轻度低头和下视，眼睛仍保持约八成开；不再落回默认笑嘴。
            var lower = EaseInOut(Mathf.InverseLerp(0f, 1.05f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(2.55f, GetPreviewDuration(ZMotionPreset.Sad), elapsed));
            var hold = lower * (1f - returnToNeutral);
            var eyeOpen = Mathf.Lerp(1f, 0.80f, hold);
            SetMotionOffset("ParamAngleY", -0.72f * hold);
            SetMotionOffset("ParamAngleZ", 0.14f * hold);
            SetMotionOffset("ParamBodyAngleY", -0.24f * hold);
            SetMotionOffset("ParamBodyAngleZ", 0.10f * hold);
            SetPreviewGaze(0f, -0.58f * hold);
            SetPreviewEyeOpen(eyeOpen, eyeOpen);
        }

        private void ApplyAngryMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Angry);
            // 压眉、偏头，前段加入短促摇头，保持片刻后回正。
            var press = EaseInOut(Mathf.InverseLerp(0f, 0.35f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(2.08f, GetPreviewDuration(ZMotionPreset.Angry), elapsed));
            var hold = press * (1f - returnToNeutral);
            var shakeWindow = EaseInOut(Mathf.InverseLerp(0.20f, 0.40f, elapsed)) *
                              (1f - EaseInOut(Mathf.InverseLerp(1.05f, 1.45f, elapsed)));
            var shake = Mathf.Sin(elapsed * Mathf.PI * 2f * 3.1f) * shakeWindow;
            SetMotionOffset("ParamAngleZ", -0.38f * hold);
            SetMotionOffset("ParamBodyAngleZ", -0.18f * hold);
            SetMotionOffset("ParamAngleX", 0.46f * shake);
            SetMotionOffset("ParamBodyAngleX", 0.16f * shake);
            SetPreviewGaze(0.02f * shake, -0.08f * hold);
            SetPreviewEyeOpen(Mathf.Lerp(1f, 0.76f, hold), Mathf.Lerp(1f, 0.76f, hold));
        }

        private void ApplyWinkMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Wink);
            // 左眼闭合，右眼保持睁开；全局笑眼叠图会影响另一眼，因此不叠加。
            var close = EaseInOut(Mathf.InverseLerp(0.12f, 0.38f, elapsed));
            var release = EaseInOut(Mathf.InverseLerp(1.18f, 1.62f, elapsed));
            var wink = close * (1f - release);
            SetMotionOffset("ParamAngleZ", 0.34f * wink);
            SetMotionOffset("ParamBodyAngleZ", 0.15f * wink);
            SetPreviewGaze(0.04f * wink, 0.02f * wink);
            SetPreviewEyeOpen(1f - wink, 1f);
        }

        private void ApplySorrowMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Sorrow);
            // 悲伤比失落更慢、更深，眉眼和嘴角持续下压，但不做困倦式闭眼。
            var lower = EaseInOut(Mathf.InverseLerp(0f, 0.92f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(3.10f, GetPreviewDuration(ZMotionPreset.Sorrow), elapsed));
            var hold = lower * (1f - returnToNeutral);
            var eyeOpen = Mathf.Lerp(1f, 0.66f, hold);
            SetMotionOffset("ParamAngleY", -0.86f * hold);
            SetMotionOffset("ParamAngleZ", 0.10f * hold);
            SetMotionOffset("ParamBodyAngleY", -0.30f * hold);
            SetMotionOffset("ParamBodyAngleZ", 0.12f * hold);
            SetPreviewGaze(0f, -0.72f * hold);
            SetPreviewEyeOpen(eyeOpen, eyeOpen);
        }

        private void ApplyCryMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Cry);
            // 闭眼抽泣：0-.30 秒闭到 0，中段保持闭眼，3.85-4.4 秒回到 1。
            var bodySettle = EaseInOut(Mathf.InverseLerp(0f, 0.72f, elapsed));
            var bodyReturn = EaseInOut(Mathf.InverseLerp(3.55f, GetPreviewDuration(ZMotionPreset.Cry), elapsed));
            var bodyHold = bodySettle * (1f - bodyReturn);
            var eyeClose = EaseInOut(Mathf.InverseLerp(0f, 0.30f, elapsed));
            var eyeReturn = EaseInOut(Mathf.InverseLerp(3.85f, GetPreviewDuration(ZMotionPreset.Cry), elapsed));
            var eyeHold = eyeClose * (1f - eyeReturn);
            var sobPulse = Mathf.Max(0f, Mathf.Sin(elapsed * Mathf.PI * 2f * 1.85f));
            var twitch = sobPulse * bodyHold;
            var eyeOpen = Mathf.Lerp(1f, 0f, eyeHold);
            SetMotionOffset("ParamAngleY", -0.48f * bodyHold + 0.12f * twitch);
            SetMotionOffset("ParamBodyAngleY", -0.16f * bodyHold + 0.05f * twitch);
            SetMotionOffset("ParamAngleZ", 0.05f * twitch);
            SetMotionOffset("ParamBodyAngleZ", 0.025f * twitch);
            SetPreviewGaze(0f, 0f);
            SetPreviewEyeOpen(eyeOpen, eyeOpen);
        }

        private void ApplyBawlMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Bawl);
            // 大哭比哭泣更明显，但只沿垂直方向抽泣，避免左右摇头失真。
            // 两端用缓入缓出，保证切入、保持、收尾都不会突然跳变。
            var bodySettle = EaseInOut(Mathf.InverseLerp(0f, 0.86f, elapsed));
            var bodyReturn = EaseInOut(Mathf.InverseLerp(3.90f, GetPreviewDuration(ZMotionPreset.Bawl), elapsed));
            var bodyHold = bodySettle * (1f - bodyReturn);
            var sobPulse = Mathf.Max(0f, Mathf.Sin(elapsed * Mathf.PI * 2f * 2.10f));
            var twitch = sobPulse * bodyHold;
            var eyeClose = EaseInOut(Mathf.InverseLerp(0f, 0.30f, elapsed));
            var eyeReturn = EaseInOut(Mathf.InverseLerp(4.08f, GetPreviewDuration(ZMotionPreset.Bawl), elapsed));
            var eyeHold = eyeClose * (1f - eyeReturn);
            var eyeOpen = Mathf.Lerp(1f, 0f, eyeHold);
            SetMotionOffset("ParamAngleY", -0.68f * bodyHold + 0.15f * twitch);
            SetMotionOffset("ParamBodyAngleY", -0.24f * bodyHold + 0.075f * twitch);
            SetMotionOffset("ParamAngleZ", 0.065f * twitch);
            SetMotionOffset("ParamBodyAngleZ", 0.032f * twitch);
            SetPreviewGaze(0f, 0f);
            SetPreviewEyeOpen(eyeOpen, eyeOpen);
        }

        private void ApplyWarmMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Warm);
            // 温柔只做轻柔点头，不借用 Smile 的全局笑眼；视线保持正中。
            var settle = EaseInOut(Mathf.InverseLerp(0f, 0.48f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(2.30f, GetPreviewDuration(ZMotionPreset.Warm), elapsed));
            var hold = settle * (1f - returnToNeutral);
            var nod = Mathf.Sin(elapsed * Mathf.PI * 2f * 0.72f) * hold;
            SetMotionOffset("ParamAngleY", -0.18f * nod);
            SetMotionOffset("ParamBodyAngleY", -0.07f * nod);
            SetMotionOffset("ParamAngleZ", 0.035f * hold);
            SetPreviewGaze(0f, 0f);
            SetPreviewEyeOpen(1f, 1f);
        }

        private void ApplySeriousMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Serious);
            // 严肃不压眉、不生气，只保持直嘴和稳定头身微动。
            var settle = EaseInOut(Mathf.InverseLerp(0f, 0.52f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(2.28f, GetPreviewDuration(ZMotionPreset.Serious), elapsed));
            var hold = settle * (1f - returnToNeutral);
            SetMotionOffset("ParamAngleY", 0.11f * hold);
            SetMotionOffset("ParamBodyAngleY", 0.16f * hold);
            SetPreviewGaze(0f, 0f);
            SetPreviewEyeOpen(1f, 1f);
        }

        private void ApplyLookMotion(float elapsed, bool lookLeft)
        {
            var motion = lookLeft ? ZMotionPreset.LookLeft : ZMotionPreset.LookRight;
            ApplyPreviewFaceProfile(motion);
            // 当前 Model 的实测约定：负 X 为观众屏幕左，正 X 为右。
            // 左右看使用独立幅度，不改全局 awayGazeX，避免影响 Think。
            var settle = EaseInOut(Mathf.InverseLerp(0f, 0.44f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(1.98f, GetPreviewDuration(motion), elapsed));
            var hold = settle * (1f - returnToNeutral);
            var gazeX = lookLeft ? -0.85f : 0.85f;
            var headX = lookLeft ? -0.30f : 0.30f;
            SetMotionOffset("ParamAngleX", headX * hold);
            SetMotionOffset("ParamBodyAngleX", headX * 0.08f * hold);
            SetPreviewGaze(gazeX * hold, 0f);
            SetPreviewEyeOpen(1f, 1f);
        }

        private void ApplyLeanMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Lean);
            // 模型没有独立前后倾绑定；这里只用现有头、身 Y 参数做前倾试作，
            // 不改变镜头，也不写手部姿势。
            var settle = EaseInOut(Mathf.InverseLerp(0f, 0.52f, elapsed));
            var returnToNeutral = EaseInOut(Mathf.InverseLerp(2.20f, GetPreviewDuration(ZMotionPreset.Lean), elapsed));
            var hold = settle * (1f - returnToNeutral);
            SetMotionOffset("ParamAngleY", 0.24f * hold);
            SetMotionOffset("ParamBodyAngleY", 0.38f * hold);
        }

        private void ApplyTiltMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Tilt);
            // A clear Z-axis tilt with a small body follow-through; eyes stay
            // naturally open and the motion returns before the preview ends.
            var tilt = EaseInOut(Mathf.InverseLerp(0f, 0.42f, elapsed));
            var release = EaseInOut(Mathf.InverseLerp(2.08f, GetPreviewDuration(ZMotionPreset.Tilt), elapsed));
            var hold = tilt * (1f - release);
            SetMotionOffset("ParamAngleZ", 0.72f * hold);
            SetMotionOffset("ParamBodyAngleZ", 0.18f * hold);
            SetMotionOffset("ParamAngleY", -0.08f * hold);
            SetPreviewGaze(0.02f * hold, 0f);
            SetPreviewEyeOpen(1f, 1f);
        }

        private void ApplyEmphasisMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Emphasis);
            // One short nod and one light torso jolt; no gesture or hand pose.
            var enter = EaseInOut(Mathf.InverseLerp(0f, 0.12f, elapsed));
            var exit = EaseInOut(Mathf.InverseLerp(0.30f, 0.62f, elapsed));
            var pulse = enter * (1f - exit);
            SetMotionOffset("ParamAngleY", -0.68f * pulse);
            SetMotionOffset("ParamBodyAngleY", -0.24f * pulse);
            SetMotionOffset("ParamAngleZ", 0.05f * pulse);
            SetPreviewGaze(0f, 0f);
            SetPreviewEyeOpen(1f, 1f);
        }

        private void ApplyStartleMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Startle);
            // Startle is a fast head/body tuck and jolt (0.18s), distinct from
            // Surprise's upward lift; the existing rig has no true depth axis.
            var hit = EaseInOut(Mathf.InverseLerp(0f, 0.18f, elapsed));
            var recoilRelease = EaseInOut(Mathf.InverseLerp(0.30f, 0.82f, elapsed));
            var recoil = hit * (1f - recoilRelease);
            var faceRelease = EaseInOut(Mathf.InverseLerp(0.55f, 1.35f, elapsed));
            SetMotionOffset("ParamAngleY", -0.78f * recoil);
            SetMotionOffset("ParamBodyAngleY", -0.28f * recoil);
            SetMotionOffset("ParamAngleZ", 0.10f * recoil);
            SetMotionOffset("ParamBodyAngleZ", 0.05f * recoil);
            SetPreviewGaze(0f, -0.16f * recoil);
            SetPreviewEyeOpen(1f, 1f);
            SetTarget("ParamMouthVariant", Mathf.Lerp(1f, 0f, faceRelease));
            if (elapsed <= 0.12f)
                SnapPreviewParameterValue("ParamMouthVariant", 1f);
        }

        private void ApplyDiscoverMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Discover);
            // Stage 1: look to screen-right and hold. Stage 2: return gaze to
            // the player while the face moves from mild surprise to a small smile.
            var lookRight = EaseInOut(Mathf.InverseLerp(0f, 0.32f, elapsed));
            var lookBack = EaseInOut(Mathf.InverseLerp(0.96f, 1.48f, elapsed));
            var release = EaseInOut(Mathf.InverseLerp(2.62f, GetPreviewDuration(ZMotionPreset.Discover), elapsed));
            var rightHold = lookRight * (1f - lookBack) * (1f - release);
            var backHold = lookBack * (1f - release);
            SetMotionOffset("ParamAngleX", 0.34f * rightHold - 0.10f * backHold);
            SetMotionOffset("ParamBodyAngleX", 0.08f * rightHold - 0.03f * backHold);
            SetMotionOffset("ParamAngleZ", 0.06f * rightHold);
            SetPreviewGaze(0.86f * rightHold, 0.04f * rightHold);
            SetPreviewEyeOpen(1f, 1f);

            SetTarget("ParamBrowForm", 0.18f * rightHold);
            SetTarget("ParamMouthVariant", rightHold);
            SetTarget("ParamEyeSmile", 0f);
            SetTarget("ParamMouthForm", 0.28f * backHold);
        }

        private void ApplyGloomMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Gloom);
            // The upper-face shadow is supplied by the companion effect layer.
            // Keep the model front-facing, cold and stable; never fake gloom
            // with half-closed eyelids that could reveal white eye gaps.
            var settle = EaseInOut(Mathf.InverseLerp(0f, 0.34f, elapsed));
            var release = EaseInOut(Mathf.InverseLerp(2.18f, GetPreviewDuration(ZMotionPreset.Gloom), elapsed));
            var hold = settle * (1f - release);
            SetMotionOffset("ParamAngleY", 0.05f * hold);
            SetMotionOffset("ParamBodyAngleY", 0.04f * hold);
            SetPreviewGaze(0f, 0f);
            SetPreviewEyeOpen(1f, 1f);
        }

        private void ApplySighMotion(float elapsed)
        {
            ApplyPreviewFaceProfile(ZMotionPreset.Sigh);
            // Sigh differs from Cry/Bawl: the companion material binding owns the
            // horizontal exhale mouth, while the model stays on neutral mouth
            // parameters. Snap both eyes fully shut through 1.2s so they never
            // read as a smile; the material binding handles the closed-eye art.
            var lower = EaseInOut(Mathf.InverseLerp(0f, 0.86f, elapsed));
            var release = EaseInOut(Mathf.InverseLerp(2.58f, GetPreviewDuration(ZMotionPreset.Sigh), elapsed));
            var hold = lower * (1f - release);
            var eyeRelease = EaseInOut(Mathf.InverseLerp(1.20f, 1.55f, elapsed));
            var eyeOpen = Mathf.Lerp(0f, 1f, eyeRelease);
            SetMotionOffset("ParamAngleY", -0.50f * hold);
            SetMotionOffset("ParamBodyAngleY", -0.16f * hold);
            SetMotionOffset("ParamAngleZ", -0.04f * hold);
            SetPreviewGaze(0f, -0.20f * hold);
            SetPreviewEyeOpen(eyeOpen, eyeOpen);
            SetTarget("ParamMouthVariant", 0f);
            SetTarget("ParamMouthForm", 0f);
            SetTarget("ParamMouthOpenY", 0f);
            if (elapsed <= 1.20f)
            {
                SetPreviewEyeOpen(0f, 0f);
                SnapPreviewEyesClosed();
            }
        }

        private void ApplyPreviewFaceProfile(ZMotionPreset motion)
        {
            if (!previewPresentationActive)
                return;

            SetTargetToDefault("ParamMouthOpenY");

            // 预览表情只由本控制器写入。眼睛微笑不叠加到害羞、眨眼等单眼动作，
            // 避免全局 EyeSmile 与独立眼睑曲线发生重影。
            switch (motion)
            {
                case ZMotionPreset.Nod:
                case ZMotionPreset.Happy:
                    SetPreviewFaceParameters(1f, 0f, 0f, 0f, 0.60f);
                    break;
                case ZMotionPreset.Shake:
                case ZMotionPreset.Think:
                    SetPreviewFaceParameters(0f, 1f, 0f, -1f, -0.25f);
                    break;
                case ZMotionPreset.Listen:
                    SetPreviewFaceParameters(0f, 0f, 0f, 0f, 0.12f);
                    break;
                case ZMotionPreset.Surprise:
                    SetPreviewFaceParameters(0f, 0f, 0f, 1f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Curious:
                    SetPreviewFaceParameters(0f, 1f, 0f, -1f, 0f);
                    SetPreviewEyeOpen(0.92f, 1f);
                    break;
                case ZMotionPreset.Shy:
                    SetPreviewFaceParameters(0f, 0f, 1f, 0f, 0.35f);
                    SetPreviewEyeOpen(0.86f, 0.90f);
                    break;
                case ZMotionPreset.Sad:
                    SetPreviewFaceParameters(0f, 0.65f, 0f, -2f, -0.55f);
                    break;
                case ZMotionPreset.Angry:
                    SetPreviewFaceParameters(0f, -1f, 0f, -1f, -0.40f);
                    break;
                case ZMotionPreset.Wink:
                    SetPreviewFaceParameters(0f, 0f, 0f, 0f, 0.35f);
                    break;
                case ZMotionPreset.Sorrow:
                    SetPreviewFaceParameters(0f, 1f, 0f, -2f, -0.75f);
                    break;
                case ZMotionPreset.Cry:
                    SetPreviewFaceParameters(0f, 1f, 0f, 2f, -0.55f);
                    break;
                case ZMotionPreset.Bawl:
                    SetPreviewFaceParameters(0f, 1f, 0f, 2f, -0.72f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Warm:
                    SetPreviewFaceParameters(0f, 0f, 0.15f, 0f, 0.35f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Serious:
                    SetPreviewFaceParameters(0f, 0f, 0f, -1f, -0.15f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.LookLeft:
                case ZMotionPreset.LookRight:
                case ZMotionPreset.Lean:
                    SetPreviewFaceParameters(0f, 0f, 0f, 0f, 0f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Tilt:
                    SetPreviewFaceParameters(0f, 0f, 0f, 0f, 0.28f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Emphasis:
                    SetPreviewFaceParameters(0f, 0f, 0f, 0f, 0f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Startle:
                    SetPreviewFaceParameters(0f, 0f, 0f, 1f, 0f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Discover:
                    SetPreviewFaceParameters(0f, 0f, 0f, 0f, 0f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Gloom:
                    SetPreviewFaceParameters(0f, 0f, 0f, -1f, -0.15f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
                case ZMotionPreset.Sigh:
                    SetPreviewFaceParameters(0f, 0.28f, 0f, 0f, 0f);
                    SetPreviewGaze(0f, 0f);
                    SetPreviewEyeOpen(1f, 1f);
                    break;
            }
        }

        private void SetPreviewFaceParameters(float eyeSmile, float browForm, float cheek,
            float mouthVariant, float mouthForm)
        {
            SetTarget("ParamEyeSmile", eyeSmile);
            SetTarget("ParamBrowForm", browForm);
            SetTarget("ParamCheek", cheek);
            SetTarget("ParamMouthVariant", mouthVariant);
            SetTarget("ParamMouthForm", mouthForm);
        }

        private void ResetPreviewFaceTargets()
        {
            SetTargetToDefault("ParamEyeSmile");
            SetTargetToDefault("ParamBrowForm");
            SetTargetToDefault("ParamCheek");
            SetTargetToDefault("ParamMouthVariant");
            SetTargetToDefault("ParamMouthForm");
            SetTargetToDefault("ParamMouthOpenY");
        }

        private static float EaseInOut(float value)
        {
            var t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private static float GetSighMouthArtWeight(float elapsed)
        {
            var enter = EaseInOut(Mathf.InverseLerp(0.08f, 0.25f, elapsed));
            var exit = EaseInOut(Mathf.InverseLerp(0.96f, 1.44f, elapsed));
            return enter * (1f - exit);
        }

        private static float GetChatSighMouthArtWeight(float elapsed)
        {
            return EaseInOut(Mathf.InverseLerp(0.08f, 0.25f, elapsed));
        }

        private static bool IsPreviewEyeControlled(ZMotionPreset motion)
        {
            // New previews either have an explicit eyelid curve or deliberately
            // hold the neutral eyelid value so an auto-blink cannot obscure the
            // intended expression. Legacy previews retain their prior behavior.
            return motion >= ZMotionPreset.Surprise;
        }

        private void SetPreviewGaze(float xOffset, float yOffset)
        {
            SetTargetWithDefaultOffset("ParamEyeBallX", xOffset);
            SetTargetWithDefaultOffset("ParamEyeBallY", yOffset);
        }

        private void SetTargetWithDefaultOffset(string id, float normalizedOffset)
        {
            if (!parameters.TryGetValue(id ?? string.Empty, out var parameter))
                return;

            var range = Mathf.Max(0.001f, parameter.MaximumValue - parameter.MinimumValue);
            var value = parameter.DefaultValue + normalizedOffset * range * 0.5f;
            SetTarget(id, Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue));
        }

        private void SetPreviewEyeDefaults()
        {
            SetTargetToDefault("ParamEyeLOpen");
            SetTargetToDefault("ParamEyeROpen");
        }

        private void SetPreviewEyeOpen(float leftOpen, float rightOpen)
        {
            SetTargetNormalized("ParamEyeLOpen", leftOpen);
            SetTargetNormalized("ParamEyeROpen", rightOpen);
        }

        private void SnapPreviewEyesClosed()
        {
            SnapParameterToMinimum("ParamEyeLOpen");
            SnapParameterToMinimum("ParamEyeROpen");
        }

        private void SnapPreviewParameterValue(string id, float value)
        {
            if (!parameters.TryGetValue(id ?? string.Empty, out var parameter))
                return;

            value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
            smoothedValues[id] = value;
            parameter.Value = value;
            targets[id] = value;
            activeWriters.Add(id);
        }

        private void SnapParameterToMinimum(string id)
        {
            if (!parameters.TryGetValue(id ?? string.Empty, out var parameter))
                return;

            smoothedValues[id] = parameter.MinimumValue;
            parameter.Value = parameter.MinimumValue;
        }

        private void SetTargetNormalized(string id, float normalizedValue)
        {
            if (!parameters.TryGetValue(id ?? string.Empty, out var parameter))
                return;

            var value = Mathf.Lerp(parameter.MinimumValue, parameter.MaximumValue, Mathf.Clamp01(normalizedValue));
            SetTarget(id, value);
        }

        private void SetMotionOffset(string id, float normalizedOffset)
        {
            if (!parameters.TryGetValue(id ?? string.Empty, out var parameter))
                return;

            var range = Mathf.Max(0.001f, parameter.MaximumValue - parameter.MinimumValue);
            var isBodyParameter = id.StartsWith("ParamBody", StringComparison.Ordinal);
            var ratio = isBodyParameter ? bodyMotionRangeRatio : headMotionRangeRatio;
            var gain = currentMotion == ZMotionPreset.Idle
                ? idleMotionGain
                : (isBodyParameter ? activeBodyMotionGain : activeHeadMotionGain);
            var value = parameter.DefaultValue + normalizedOffset * range * ratio *
                        gain * Mathf.Clamp(motionAmplitudeMultiplier, 0.25f, 2f);
            SetTarget(id, Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue));
        }

        private bool IsMotionParameter(string id)
        {
            for (var i = 0; i < s_motionParameterIds.Length; i++)
            {
                if (s_motionParameterIds[i] == id)
                    return true;
            }
            return false;
        }



        private void SetTarget(string id, float value)
        {
            if (!IsWhitelisted(id))
                return;
            if (parameters.TryGetValue(id, out var parameter))
                value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
            targets[id] = value;
            activeWriters.Add(id);
        }

        private void SetTargetToDefault(string id)
        {
            SetTarget(id, GetDefaultValue(id));
        }

        private float GetDefaultValue(string id)
        {
            return parameters.TryGetValue(id ?? string.Empty, out var parameter) ? parameter.DefaultValue : 0f;
        }

        private bool IsWhitelisted(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            for (var i = 0; i < s_parameterDescriptors.Length; i++)
            {
                if (s_parameterDescriptors[i].id == id)
                    return true;
            }
            return false;
        }

        private void WriteDefaultsImmediately()
        {
            foreach (var id in activeWriters)
            {
                if (parameters.TryGetValue(id, out var parameter))
                {
                    parameter.Value = parameter.DefaultValue;
                    smoothedValues[id] = parameter.DefaultValue;
                }
            }

            targets.Clear();
            activeWriters.Clear();
            smoothedValues.Clear();
        }

        private void ResolveCubismComponents()
        {
            if (cubismModel == null)
                cubismModel = GetComponentInChildren<CubismModel>(true);
            if (cubismUpdateController == null)
                cubismUpdateController = GetComponentInChildren<CubismUpdateController>(true);

            if (cubismModel == null && !warnedNoModel)
            {
                warnedNoModel = true;
                Debug.LogWarning("[ZPerformanceController] 未找到 CubismModel，请绑定导入模型 Prefab。", this);
            }
        }

        private void CaptureAndSuspendAnimators()
        {
            if (!suspendAnimatorDuringRuntime || animatorStatesCaptured)
                return;

            var animators = GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                suspendedAnimatorStates.Add(new AnimatorState(animator, animator.enabled));
                if (animator.enabled)
                    animator.enabled = false;
            }
            animatorStatesCaptured = true;
        }

        private void RestoreAnimatorStates()
        {
            if (!animatorStatesCaptured)
                return;

            for (var i = 0; i < suspendedAnimatorStates.Count; i++)
            {
                var state = suspendedAnimatorStates[i];
                if (state.animator != null)
                    state.animator.enabled = state.enabled;
            }
            suspendedAnimatorStates.Clear();
            animatorStatesCaptured = false;
        }

        private void BuildParameterCache()
        {
            ResolveCubismComponents();
            CaptureAndSuspendAnimators();
            // MVP 只绑定场景中的一个模型生命周期；运行时换模型不在支持范围内。
            if (cacheBuilt)
                return;
            if (cubismModel == null || cubismModel.Parameters == null)
                return;

            var hadCache = cacheBuilt;
            parameters.Clear();
            targets.Clear();
            smoothedValues.Clear();
            activeWriters.Clear();
            for (var i = 0; i < s_parameterDescriptors.Length; i++)
            {
                var id = s_parameterDescriptors[i].id;
                var parameter = cubismModel.Parameters.FindById(id);
                if (parameter == null)
                {
                    if (warnedMissingIds.Add(id))
                        Debug.LogWarning("[ZPerformanceController] 模型缺少参数：" + id, this);
                    continue;
                }
                parameters[id] = parameter;
                smoothedValues[id] = parameter.DefaultValue;
            }

            cacheBuilt = parameters.Count > 0;
            if (!hadCache && cacheBuilt)
                ResetPerformanceToNeutral();
        }

        private static ZFaceExpression ParseExpression(string value)
        {
            if (string.Equals(value, "smile", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Smile;
            if (string.Equals(value, "surprise", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Surprise;
            if (string.Equals(value, "angry", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Angry;
            if (string.Equals(value, "troubled", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Troubled;
            if (string.Equals(value, "blush", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Blush;
            if (string.Equals(value, "cry", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Cry;
            if (string.Equals(value, "bawl", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Bawl;
            if (string.Equals(value, "sigh", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Sigh;
            if (string.Equals(value, "gloom", StringComparison.OrdinalIgnoreCase)) return ZFaceExpression.Gloom;
            return ZFaceExpression.Neutral;
        }

        private static ZGesture ParseGesture(string value)
        {
            if (string.Equals(value, "wave", StringComparison.OrdinalIgnoreCase)) return ZGesture.Wave;
            if (string.Equals(value, "explain", StringComparison.OrdinalIgnoreCase)) return ZGesture.Explain;
            if (string.Equals(value, "point", StringComparison.OrdinalIgnoreCase)) return ZGesture.Point;
            return ZGesture.None;
        }

        private static ZGaze ParseGaze(string value)
        {
            return string.Equals(value, "away", StringComparison.OrdinalIgnoreCase)
                ? ZGaze.Away
                : ZGaze.Player;
        }

        private static ZMotionPreset ParseMotionPreset(string value)
        {
            if (string.Equals(value, "nod", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Nod;
            if (string.Equals(value, "shake", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "shake_head", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Shake;
            if (string.Equals(value, "think", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Think;
            if (string.Equals(value, "listen", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Listen;
            if (string.Equals(value, "happy", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Happy;
            if (string.Equals(value, "surprise", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "惊讶", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Surprise;
            if (string.Equals(value, "curious", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "疑惑", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Curious;
            if (string.Equals(value, "shy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "害羞", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Shy;
            if (string.Equals(value, "sad", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "失落", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Sad;
            if (string.Equals(value, "angry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "不满", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "生气", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Angry;
            if (string.Equals(value, "wink", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "俏皮眨眼", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Wink;
            if (string.Equals(value, "sorrow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "悲伤", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Sorrow;
            if (string.Equals(value, "cry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "哭泣", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "哭", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Cry;
            if (string.Equals(value, "bawl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "bawling", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "大哭", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Bawl;
            if (string.Equals(value, "warm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "温柔", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Warm;
            if (string.Equals(value, "serious", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "严肃", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Serious;
            if (string.Equals(value, "lookleft", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "look_left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "向左看", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "左看", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.LookLeft;
            if (string.Equals(value, "lookright", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "look_right", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "向右看", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "右看", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.LookRight;
            if (string.Equals(value, "lean", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "前倾", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Lean;
            if (string.Equals(value, "tilt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "歪头", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Tilt;
            if (string.Equals(value, "emphasis", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "强调", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Emphasis;
            if (string.Equals(value, "startle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "受惊", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Startle;
            if (string.Equals(value, "discover", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "发现", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "回看", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Discover;
            if (string.Equals(value, "gloom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "黑化", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Gloom;
            if (string.Equals(value, "sigh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "叹气", StringComparison.OrdinalIgnoreCase)) return ZMotionPreset.Sigh;
            return ZMotionPreset.Idle;
        }
    }
}
