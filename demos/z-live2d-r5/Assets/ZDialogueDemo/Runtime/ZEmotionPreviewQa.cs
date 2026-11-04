using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZDialogueDemo
{
    /// <summary>
    /// Opt-in runtime QA for the emotion previews. Values are sampled after
    /// WaitForEndOfFrame so CubismParameterStore cannot hide a bad write.
    ///
    /// Unity command line:
    /// --z-emotion-verify=/absolute/output/directory --z-emotion-quit
    /// Optional focused run: --z-emotion-only=Sorrow,Cry
    ///
    /// No DialogueClient or chat service is touched. The component is normally
    /// absent; an Editor runner or the command line creates it temporarily.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class ZEmotionPreviewQa : MonoBehaviour
    {
        private const string VerifyArgument = "--z-emotion-verify=";
        private const string EmotionOnlyArgument = "--z-emotion-only=";
        private const string QuitArgument = "--z-emotion-quit";
        private const float ModelTimeoutSeconds = 30f;
        private const float SampleRateHz = 30f;
        private const float SampleInterval = 1f / SampleRateHz;
        private const float NeutralSettleSeconds = 0.70f;
        private const float RecoverySeconds = 0.80f;
        private const float TransitionSeconds = 0.55f;
        private const float RangeTolerance = 0.001f;
        private const float HeadMotionThreshold = 0.025f;
        private const float BodyMotionThreshold = 0.020f;
        private const float ExpressionActivationThreshold = 0.12f;
        private const float EyeActivationThreshold = 0.18f;
        private const float NeutralTolerance = 0.12f;
        private const float WinkDifferenceThreshold = 0.50f;
        private const float WinkOpenEyeThreshold = 0.80f;
        private const float WinkResidualTolerance = 0.25f;
        private const float ScreenshotThreshold = 0.25f;
        private const float RestartElapsedTolerance = 0.15f;
        private const float StartleImpactWindowStart = 0.15f;
        private const float StartleImpactWindowEnd = 0.30f;
        private const float StartleImpactThreshold = 0.025f;
        private const float StartleScreenshotLatest = 0.35f;
        private const float StartleOMouthMinimum = 0.98f;
        private const float StartleOMouthMaximum = 1.10f;
        private const float TiltZThreshold = 0.10f;
        private const float DiscoverLookRightThreshold = 0.18f;
        private const float DiscoverPlayerTolerance = 0.12f;
        private const float DiscoverReturnMinimumDelay = 0.10f;
        private const float DiscoverLookRightScreenshotEarliest = 0.65f;
        private const float DiscoverLookRightScreenshotLatest = 0.90f;
        private const float DiscoverReturnScreenshotEarliest = 1.65f;
        private const float SighEyeClosedMaximum = 0.12f;
        private const float SighScreenshotEarliest = 0.60f;
        private const float AccentResidualTolerance = 0.05f;
        private const float NewAccentTransitionWait = 0.22f;
        private const float SighArtActivationThreshold = 0.25f;
        private const int SighRequiredInstanceCount = 3;
        private const float SighMiddleScreenshotEarliest = 0.70f;
        private const float SighMiddleScreenshotLatest = 1.20f;
        private const float SighRepeatElapsedTolerance = 0.15f;
        private const float SighTransitionWait = 0.60f;

        private static readonly string[] HeadIds = { "ParamAngleX", "ParamAngleY", "ParamAngleZ" };
        private static readonly string[] BodyIds =
            { "ParamBodyAngleX", "ParamBodyAngleY", "ParamBodyAngleZ" };
        private static readonly string[] FaceIds =
        {
            "ParamEyeSmile", "ParamBrowForm", "ParamMouthVariant", "ParamMouthForm", "ParamCheek",
        };
        private static readonly string[] MouthIds = { "ParamMouthVariant", "ParamMouthForm" };
        private static readonly string[] OptionalMouthOpenIds = { "ParamMouthOpenY" };
        private static readonly string[] GazeIds = { "ParamEyeBallX", "ParamEyeBallY" };
        private static readonly string[] EyeIds = { "ParamEyeLOpen", "ParamEyeROpen" };

        // These IDs mirror the actual controller curves. Keeping them here
        // makes a missing or silently neutralized expression a failed check.
        private static readonly EmotionSpec[] Specs =
        {
            new EmotionSpec(ZMotionPreset.Surprise, 0.65f, new[] { "ParamMouthVariant" }, new[] { "ParamEyeBallY" },
                0.18f, EmotionExpectation.Surprise, "Surprise",
                new[] { "EmotionSurprise", "EmotionSurpriseButton", "MotionSurprise", "MotionSurpriseButton", "PreviewSurprise", "惊讶", "惊讶Button" }),
            new EmotionSpec(ZMotionPreset.Curious, 1.0f, new[] { "ParamBrowForm", "ParamMouthVariant" }, new[] { "ParamEyeBallX", "ParamEyeBallY" },
                0.18f, EmotionExpectation.Curious, "Curious",
                new[] { "EmotionCurious", "EmotionCuriousButton", "MotionCurious", "MotionCuriousButton", "PreviewCurious", "好奇", "疑惑", "疑惑Button" }),
            new EmotionSpec(ZMotionPreset.Shy, 1.35f, new[] { "ParamCheek", "ParamMouthForm" }, new[] { "ParamEyeBallX", "ParamEyeBallY" },
                0.18f, EmotionExpectation.Shy, "Shy",
                new[] { "EmotionShy", "EmotionShyButton", "MotionShy", "MotionShyButton", "PreviewShy", "害羞", "害羞Button" }),
            new EmotionSpec(ZMotionPreset.Sad, 1.4f, new[] { "ParamBrowForm", "ParamMouthVariant", "ParamMouthForm", "ParamEyeLOpen", "ParamEyeROpen" }, new[] { "ParamEyeBallY" },
                0.06f, EmotionExpectation.Sad, string.Empty,
                new[] { "EmotionSad", "EmotionSadButton", "MotionSad", "MotionSadButton", "PreviewSad", "失落", "委屈", "失落Button" }),
            new EmotionSpec(ZMotionPreset.Angry, 0.9f, new[] { "ParamBrowForm", "ParamMouthVariant", "ParamEyeLOpen", "ParamEyeROpen" }, new[] { "ParamEyeBallX", "ParamEyeBallY" },
                0.06f, EmotionExpectation.Angry, "Angry",
                new[] { "EmotionAngry", "EmotionAngryButton", "MotionAngry", "MotionAngryButton", "PreviewAngry", "生气", "不满", "生气Button" }),
            new EmotionSpec(ZMotionPreset.Wink, 0.65f, new[] { "ParamEyeLOpen", "ParamEyeROpen", "ParamMouthForm" }, new[] { "ParamEyeBallX", "ParamEyeBallY" },
                0.06f, EmotionExpectation.Wink, string.Empty,
                new[] { "EmotionWink", "EmotionWinkButton", "MotionWink", "MotionWinkButton", "PreviewWink", "眨眼", "俏皮眨眼", "眨眼Button" }),
            new EmotionSpec(ZMotionPreset.Sorrow, 1.25f, new[] { "ParamBrowForm", "ParamMouthVariant", "ParamMouthForm", "ParamEyeLOpen", "ParamEyeROpen" }, new[] { "ParamEyeBallY" },
                0.06f, EmotionExpectation.Sorrow, "Sorrow",
                new[] { "EmotionSorrow", "EmotionSorrowButton", "MotionSorrow", "MotionSorrowButton", "PreviewSorrow", "悲伤", "悲伤Button" }),
            new EmotionSpec(ZMotionPreset.Cry, 1.25f, new[] { "ParamBrowForm", "ParamMouthVariant", "ParamMouthForm", "ParamEyeLOpen", "ParamEyeROpen" }, new string[0],
                0.08f, EmotionExpectation.Cry, "Cry",
                new[] { "EmotionCry", "EmotionCryButton", "MotionCry", "MotionCryButton", "PreviewCry", "哭泣", "哭", "哭泣Button" }),
            new EmotionSpec(ZMotionPreset.Bawl, 1.25f, new[] { "ParamBrowForm", "ParamMouthVariant", "ParamMouthForm", "ParamEyeLOpen", "ParamEyeROpen" }, new string[0],
                0.08f, EmotionExpectation.Bawl, "Bawl",
                new[] { "EmotionBawl", "EmotionBawlButton", "MotionBawl", "MotionBawlButton", "PreviewBawl", "PreviewBawlMotion", "大哭", "大哭Button" }),
            new EmotionSpec(ZMotionPreset.Warm, 0.80f, new[] { "ParamMouthForm", "ParamCheek" }, new string[0],
                0.18f, EmotionExpectation.Warm, string.Empty,
                new[] { "EmotionWarm", "EmotionWarmButton", "MotionWarm", "MotionWarmButton", "PreviewWarm", "温柔", "温暖", "温柔Button" },
                true, false, 0, 0.12f),
            new EmotionSpec(ZMotionPreset.Serious, 0.80f, new[] { "ParamMouthVariant" }, new string[0],
                0.18f, EmotionExpectation.Serious, string.Empty,
                new[] { "EmotionSerious", "EmotionSeriousButton", "MotionSerious", "MotionSeriousButton", "PreviewSerious", "严肃", "严肃Button" },
                true, false, 0, 0.20f),
            new EmotionSpec(ZMotionPreset.LookLeft, 0.55f, new[] { "ParamEyeBallX" }, new[] { "ParamEyeBallX" },
                0.18f, EmotionExpectation.LookLeft, string.Empty,
                new[] { "EmotionLookLeft", "EmotionLookLeftButton", "MotionLookLeft", "MotionLookLeftButton", "PreviewLookLeft", "左看", "看左", "左看Button" },
                false, false, -1, 0.18f),
            new EmotionSpec(ZMotionPreset.LookRight, 0.55f, new[] { "ParamEyeBallX" }, new[] { "ParamEyeBallX" },
                0.18f, EmotionExpectation.LookRight, string.Empty,
                new[] { "EmotionLookRight", "EmotionLookRightButton", "MotionLookRight", "MotionLookRightButton", "PreviewLookRight", "右看", "看右", "右看Button" },
                false, false, 1, 0.18f),
            // The model only exposes a small vertical lean signal here. Keep
            // this as a BodyY smoke check, not a claim of true depth motion.
            new EmotionSpec(ZMotionPreset.Lean, 0.70f, new string[0], new string[0],
                0.18f, EmotionExpectation.Lean, string.Empty,
                new[] { "EmotionLean", "EmotionLeanButton", "MotionLean", "MotionLeanButton", "PreviewLean", "前倾", "前倾·试作", "前倾Button" },
                false, true, 0, 0.08f),
            new EmotionSpec(ZMotionPreset.Tilt, 0.30f, new string[0], new string[0],
                0.18f, EmotionExpectation.Tilt, string.Empty,
                new[] { "MotionTilt", "Tilt", "歪头" }, true, false, 0, 0.18f),
            new EmotionSpec(ZMotionPreset.Emphasis, 0.25f, new string[0], new string[0],
                0.18f, EmotionExpectation.Emphasis, string.Empty,
                new[] { "MotionEmphasis", "Emphasis", "强调" }, true, true, 0, 0.14f),
            new EmotionSpec(ZMotionPreset.Startle, StartleImpactWindowStart, new[] { "ParamMouthVariant" }, new string[0],
                0.18f, EmotionExpectation.Startle, "Startle",
                new[] { "MotionStartle", "Startle", "受惊·闪电", "受惊", "闪电" }, true, true, 0, 0.14f),
            new EmotionSpec(ZMotionPreset.Discover, DiscoverLookRightScreenshotEarliest, new[] { "ParamEyeBallX" }, new[] { "ParamEyeBallX" },
                0.18f, EmotionExpectation.Discover, string.Empty,
                new[] { "MotionDiscover", "Discover", "发现/回看", "发现", "回看" }, true, false, 1, 0.20f),
            new EmotionSpec(ZMotionPreset.Gloom, 0.45f, new[] { "ParamMouthVariant" }, new string[0],
                0.18f, EmotionExpectation.Gloom, "Gloom",
                new[] { "MotionGloom", "Gloom", "黑化" }, false, false, 0, 0.20f),
            new EmotionSpec(ZMotionPreset.Sigh, SighScreenshotEarliest, new[] { "ParamEyeLOpen", "ParamEyeROpen" }, new string[0],
                0.06f, EmotionExpectation.Sigh, string.Empty,
                new[] { "MotionSigh", "Sigh", "无奈·叹气", "无奈", "叹气" }, true, true, 0, 0.25f),
        };

        private readonly Dictionary<string, ParameterInfo> parameters =
            new Dictionary<string, ParameterInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, RangeResult> ranges =
            new Dictionary<string, RangeResult>(StringComparer.Ordinal);
        private readonly List<EventSystemState> eventSystemStates = new List<EventSystemState>();

        private bool running;
        private bool commandLineEnabled;
        private bool quitWhenDone;
        private bool focusedMode;
        private bool originalMultipliersCaptured;
        private string requestedOutputDirectory;
        private string requestedEmotionOnly;
        private string outputDirectory;
        private string runId;
        private float runStartedAt;
        private float originalAmplitude;
        private float originalSpeed;
        private ZPerformanceController performance;
        private ZEmotionAccents accents;
        private ZSighFaceArt sighFaceArt;
        private EmotionReport report;
        private Sample lastWinkTransitionSample;
        private readonly List<EmotionSpec> runSpecs = new List<EmotionSpec>();
        private bool inputIsolationAttempted;
        private bool inputIsolationActive;
        private int inputIsolationDisabledCount;

        private struct EventSystemState
        {
            public EventSystem eventSystem;
            public bool enabled;
        }

        public bool IsFinished { get; private set; }
        public bool IsRunning => running;
        public string OutputDirectory => outputDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var output = string.Empty;
            var emotionOnly = string.Empty;
            var quit = false;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(VerifyArgument, StringComparison.Ordinal))
                    output = args[i].Substring(VerifyArgument.Length);
                else if (args[i].StartsWith(EmotionOnlyArgument, StringComparison.Ordinal))
                    emotionOnly = args[i].Substring(EmotionOnlyArgument.Length);
                else if (args[i] == QuitArgument)
                    quit = true;
            }

            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output) ||
                FindObjectOfType<ZEmotionPreviewQa>() != null)
                return;

            var gameObject = new GameObject("Z Emotion Preview QA (command line)");
            DontDestroyOnLoad(gameObject);
            var qa = gameObject.AddComponent<ZEmotionPreviewQa>();
            qa.requestedOutputDirectory = output;
            qa.requestedEmotionOnly = emotionOnly;
            qa.quitWhenDone = quit;
            qa.commandLineEnabled = true;
        }

        private void Start()
        {
            if (commandLineEnabled)
                Begin();
        }

        private void OnDestroy()
        {
            RestoreEventSystems();
        }

        private void OnDisable()
        {
            RestoreEventSystems();
        }

        /// <summary>Starts one temporary QA run; safe for an independent Editor entry.</summary>
        public void Begin()
        {
            if (running || IsFinished)
                return;
            StartCoroutine(RunWithGuard());
        }

        private IEnumerator RunWithGuard()
        {
            running = true;
            IsFinished = false;
            report = new EmotionReport
            {
                schemaVersion = "z-emotion-preview-qa-v3",
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                commandLineOptIn = commandLineEnabled,
                thresholds = new ThresholdReport
                {
                    sampleRateHz = SampleRateHz,
                    rangeTolerance = RangeTolerance,
                    headMotionNormalized = HeadMotionThreshold,
                    bodyMotionNormalized = BodyMotionThreshold,
                    expressionActivationNormalized = ExpressionActivationThreshold,
                    eyeActivationNormalized = EyeActivationThreshold,
                    neutralToleranceNormalized = NeutralTolerance,
                    winkDifference = WinkDifferenceThreshold,
                    winkOpenEye = WinkOpenEyeThreshold,
                    winkResidualDifference = WinkResidualTolerance,
                    screenshotActivation = ScreenshotThreshold,
                    recoverySeconds = RecoverySeconds,
                    restartElapsedTolerance = RestartElapsedTolerance,
                    startleImpactWindowStartSeconds = StartleImpactWindowStart,
                    startleImpactWindowEndSeconds = StartleImpactWindowEnd,
                    startleImpactThresholdNormalized = StartleImpactThreshold,
                    startleScreenshotLatestSeconds = StartleScreenshotLatest,
                    startleOMouthMinimum = StartleOMouthMinimum,
                    startleOMouthMaximum = StartleOMouthMaximum,
                    tiltZThresholdNormalized = TiltZThreshold,
                    discoverLookRightThresholdNormalized = DiscoverLookRightThreshold,
                    discoverPlayerToleranceNormalized = DiscoverPlayerTolerance,
                    discoverLookRightScreenshotEarliestSeconds = DiscoverLookRightScreenshotEarliest,
                    discoverLookRightScreenshotLatestSeconds = DiscoverLookRightScreenshotLatest,
                    discoverReturnScreenshotEarliestSeconds = DiscoverReturnScreenshotEarliest,
                    sighEyeClosedMaximum = SighEyeClosedMaximum,
                    sighScreenshotEarliestSeconds = SighScreenshotEarliest,
                    accentResidualTolerance = AccentResidualTolerance,
                    sighArtActivationThreshold = SighArtActivationThreshold,
                    sighArtRequiredInstanceCount = SighRequiredInstanceCount,
                    sighMiddleScreenshotEarliestSeconds = SighMiddleScreenshotEarliest,
                    sighMiddleScreenshotLatestSeconds = SighMiddleScreenshotLatest,
                    sighRepeatElapsedTolerance = SighRepeatElapsedTolerance,
                    sighTransitionWaitSeconds = SighTransitionWait,
                },
                model = new ModelReport(),
                actions = new List<ActionReport>(),
                samples = new List<Sample>(),
                parameterRanges = new List<RangeResult>(),
                transitions = new TransitionReport(),
                uiButtons = new List<UiButtonReport>(),
                errors = new List<string>(),
            };

            CaptureAndDisableEventSystems();

            var body = RunBody();
            Exception bodyException = null;
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = body.MoveNext();
                }
                catch (Exception exception)
                {
                    bodyException = exception;
                    break;
                }
                if (!hasNext)
                    break;
                yield return body.Current;
            }

            if (bodyException != null)
            {
                report.runtimeException = bodyException.GetType().Name + ": " + bodyException.Message;
                report.errors.Add("runtime_exception_" + bodyException.GetType().Name);
                Debug.LogException(bodyException, this);
            }

            // Restore before writing the final report so the report records
            // the completed lifecycle state. OnDestroy repeats this as a
            // safety net if the QA object is destroyed during an exception.
            RestoreEventSystems();
            try
            {
                FinalizeReport();
            }
            catch (Exception exception)
            {
                if (string.IsNullOrEmpty(report.runtimeException))
                    report.runtimeException = exception.GetType().Name + ": " + exception.Message;
                report.errors.Add("finalize_exception_" + exception.GetType().Name);
                Debug.LogException(exception, this);
                WriteReport();
            }
            running = false;
            IsFinished = true;
            if (quitWhenDone)
                StartCoroutine(QuitNextFrame());
        }

        private void CaptureAndDisableEventSystems()
        {
            if (inputIsolationAttempted)
                return;

            inputIsolationAttempted = true;
            if (report != null)
                report.inputIsolationRequested = true;

            try
            {
                var seen = new HashSet<EventSystem>();
                var systems = Resources.FindObjectsOfTypeAll<EventSystem>();
                for (var i = 0; i < systems.Length; i++)
                {
                    var eventSystem = systems[i];
                    if (eventSystem == null || !eventSystem.gameObject.scene.IsValid() ||
                        !seen.Add(eventSystem))
                        continue;

                    eventSystemStates.Add(new EventSystemState
                    {
                        eventSystem = eventSystem,
                        enabled = eventSystem.enabled,
                    });
                    if (eventSystem.enabled)
                    {
                        eventSystem.enabled = false;
                        inputIsolationDisabledCount++;
                    }
                }

                inputIsolationActive = inputIsolationDisabledCount > 0;
                if (report != null)
                {
                    report.inputIsolationActive = inputIsolationActive;
                    report.inputIsolationApplied = inputIsolationActive;
                    report.inputIsolationEventSystemCount = eventSystemStates.Count;
                    report.inputIsolationDisabledEventSystemCount = inputIsolationDisabledCount;
                }
            }
            catch (Exception exception)
            {
                if (report != null)
                    report.errors.Add("input_isolation_exception_" + exception.GetType().Name);
                RestoreEventSystems();
            }
        }

        private void RestoreEventSystems()
        {
            for (var i = 0; i < eventSystemStates.Count; i++)
            {
                var state = eventSystemStates[i];
                try
                {
                    if (state.eventSystem != null && state.eventSystem.gameObject.scene.IsValid())
                        state.eventSystem.enabled = state.enabled;
                }
                catch (Exception exception)
                {
                    if (report != null)
                        report.errors.Add("input_restore_exception_" + exception.GetType().Name);
                }
            }

            eventSystemStates.Clear();
            inputIsolationActive = false;
            if (report != null)
            {
                report.inputIsolationActive = false;
                report.inputIsolationRestored = inputIsolationAttempted;
            }
        }

        private IEnumerator RunBody()
        {
            PrepareOutputDirectory();
            report.outputDirectory = outputDirectory;
            report.runId = runId;
            runStartedAt = Time.unscaledTime;
            BuildRunSpecs();
            report.focusedMode = focusedMode;
            report.runScope = focusedMode ? "focused" : "full";
            report.requestedEmotionOnly = requestedEmotionOnly ?? string.Empty;

            yield return FindRealModel();
            if (performance == null || !report.model.ready)
            {
                report.errors.Add("cubism_model_not_ready");
                yield break;
            }

            originalAmplitude = performance.MotionAmplitudeMultiplier;
            originalSpeed = performance.MotionSpeedMultiplier;
            originalMultipliersCaptured = true;
            performance.SetMotionAmplitudeMultiplier(1f);
            performance.SetMotionSpeedMultiplier(1f);
            report.measurementAmplitudeMultiplier = 1f;
            report.measurementSpeedMultiplier = 1f;

            BuildParameterMetadata();
            performance.ResetPerformance();
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(NeutralSettleSeconds);
            yield return new WaitForEndOfFrame();
            CaptureNeutralScreenshot();

            for (var i = 0; i < runSpecs.Count; i++)
                yield return RunAction(runSpecs[i], i);
            if (focusedMode)
            {
                report.transitions.skipped = true;
                report.transitions.skipReason = "focused_emotion_only";
            }
            else
            {
                yield return RunTransitions();
            }
            yield return RunNewAccentTransitions();
            yield return RunSighTransitions();
            yield return RunUiButtonChecks();

            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        private IEnumerator FindRealModel()
        {
            var deadline = Time.realtimeSinceStartup + ModelTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (performance == null)
                    performance = FindObjectOfType<ZPerformanceController>();
                var model = performance == null ? null : performance.CubismModel;
                var modelParameters = model == null ? null : model.Parameters;
                if (performance != null && model != null && modelParameters != null && modelParameters.Length > 0 &&
                    performance.IsModelReady)
                {
                    report.model.controllerFound = true;
                    report.model.controllerObject = performance.gameObject.name;
                    report.model.cubismModelFound = true;
                    report.model.cubismModelObject = model.gameObject.name;
                    report.model.parameterCount = modelParameters.Length;
                    report.model.updateControllerFound = performance.HasCubismUpdateController;
                    if (accents == null)
                        accents = FindObjectOfType<ZEmotionAccents>();
                    ResolveSighFaceArt();
                    report.model.accentsFound = accents != null;
                    report.model.sighFaceArtFound = sighFaceArt != null;
                    report.model.ready = true;
                    if (!report.model.updateControllerFound)
                        report.errors.Add("missing_cubism_update_controller");
                    yield break;
                }
                yield return null;
            }

            report.model.controllerFound = performance != null;
            report.model.controllerObject = performance == null ? string.Empty : performance.gameObject.name;
            report.model.cubismModelFound = performance != null && performance.CubismModel != null;
            report.model.cubismModelObject = report.model.cubismModelFound
                ? performance.CubismModel.gameObject.name
                : string.Empty;
            report.model.updateControllerFound = performance != null && performance.HasCubismUpdateController;
            ResolveSighFaceArt();
            report.model.sighFaceArtFound = sighFaceArt != null;
        }

        private void ResolveSighFaceArt()
        {
            if (sighFaceArt == null)
                sighFaceArt = FindObjectOfType<ZSighFaceArt>();
            if (report != null && sighFaceArt != null)
                report.model.sighFaceArtFound = true;
        }

        private void BuildParameterMetadata()
        {
            parameters.Clear();
            ranges.Clear();
            var model = performance.CubismModel;
            for (var i = 0; i < model.Parameters.Length; i++)
            {
                var parameter = model.Parameters[i];
                if (parameter == null)
                    continue;
                var info = new ParameterInfo
                {
                    id = parameter.Id,
                    minimum = parameter.MinimumValue,
                    maximum = parameter.MaximumValue,
                    defaultValue = parameter.DefaultValue,
                };
                parameters[info.id] = info;
                ranges[info.id] = new RangeResult
                {
                    id = info.id,
                    minimum = info.minimum,
                    maximum = info.maximum,
                    defaultValue = info.defaultValue,
                    minimumObserved = info.defaultValue,
                    maximumObserved = info.defaultValue,
                    inRange = true,
                };
            }

            var required = new HashSet<string>(StringComparer.Ordinal);
            AddIds(required, HeadIds);
            AddIds(required, BodyIds);
            AddIds(required, FaceIds);
            AddIds(required, GazeIds);
            AddIds(required, EyeIds);
            for (var i = 0; i < runSpecs.Count; i++)
            {
                AddIds(required, runSpecs[i].expressionIds);
                AddIds(required, runSpecs[i].gazeIds);
            }
            report.model.requiredParameterCount = required.Count;
            foreach (var id in required)
            {
                if (!parameters.ContainsKey(id))
                    report.model.missingRequiredParameterIds.Add(id);
            }
            report.model.availableRequiredParameterCount = required.Count -
                                                            report.model.missingRequiredParameterIds.Count;
            report.model.parametersReady = report.model.missingRequiredParameterIds.Count == 0;
        }

        private static void AddIds(HashSet<string> target, string[] ids)
        {
            for (var i = 0; i < ids.Length; i++)
                target.Add(ids[i]);
        }

        private void BuildRunSpecs()
        {
            runSpecs.Clear();
            focusedMode = !string.IsNullOrWhiteSpace(requestedEmotionOnly);
            if (!focusedMode)
            {
                runSpecs.AddRange(Specs);
                report.selectedEmotions = new List<string>();
                for (var i = 0; i < runSpecs.Count; i++)
                    report.selectedEmotions.Add(runSpecs[i].motion.ToString());
                return;
            }

            var selected = new HashSet<ZMotionPreset>();
            var tokens = requestedEmotionOnly.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i].Trim();
                if (string.IsNullOrEmpty(token))
                    continue;

                EmotionSpec matched = null;
                for (var j = 0; j < Specs.Length; j++)
                {
                    var candidate = Specs[j];
                    if (string.Equals(candidate.motion.ToString(), token, StringComparison.OrdinalIgnoreCase) ||
                        MatchesAlias(candidate.buttonNames, token))
                    {
                        matched = candidate;
                        break;
                    }
                }

                if (matched == null)
                {
                    report.errors.Add("emotion_only_unknown_" + Normalize(token));
                    continue;
                }

                if (selected.Add(matched.motion))
                    runSpecs.Add(matched);
            }

            report.partial = true;
            report.partialReason = "仅运行 --z-emotion-only 指定动作；旧 transition 与未选动作未执行，若包含 Startle/Gloom 则仍执行其 focused effect transition";
            report.selectedEmotions = new List<string>();
            for (var i = 0; i < runSpecs.Count; i++)
                report.selectedEmotions.Add(runSpecs[i].motion.ToString());
            if (runSpecs.Count == 0)
                report.errors.Add("emotion_only_no_matching_actions");
        }

        private static bool MatchesAlias(string[] aliases, string token)
        {
            if (aliases == null)
                return false;
            for (var i = 0; i < aliases.Length; i++)
            {
                if (string.Equals(aliases[i], token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private IEnumerator RunAction(EmotionSpec spec, int actionIndex)
        {
            yield return PrepareNeutral();
            var baseline = Capture(actionIndex, spec.motion.ToString(), "baseline", 0f);
            report.samples.Add(baseline);

            var check = new ActionReport
            {
                preset = spec.motion.ToString(),
                nominalDurationSeconds = Mathf.Max(0.1f, ZPerformanceController.GetPreviewDuration(spec.motion)),
                screenshotEarliestSeconds = spec.screenshotEarliestSeconds,
                screenshotActivationThreshold = spec.screenshotActivationThreshold,
                actionIndex = actionIndex,
                sampleStart = report.samples.Count,
                headMotionRequired = spec.requireHeadMotion,
                bodyMotionRequired = spec.requireBodyMotion,
                screenshotFile = string.Format(CultureInfo.InvariantCulture, "emotion-{0:00}-{1}.png",
                    actionIndex + 1, spec.motion.ToString().ToLowerInvariant()),
                expressions = CreateValueReports(spec.expressionIds, baseline, spec.eyeActivationThreshold),
                gaze = CreateValueReports(spec.gazeIds, baseline),
                stageScreenshots = new List<StageScreenshotReport>(),
                failureReasons = new List<string>(),
            };
            if (spec.motion == ZMotionPreset.Wink)
            {
                for (var i = 0; i < check.expressions.Count; i++)
                {
                    if (check.expressions[i].id == "ParamEyeROpen")
                        check.expressions[i].activationRequired = false;
                }
            }
            report.actions.Add(check);

            performance.PreviewMotion(spec.motion);
            check.triggerSerial = performance.MotionTriggerSerial;
            var startedAt = Time.unscaledTime;
            var deadline = startedAt + check.nominalDurationSeconds + RecoverySeconds;
            var lastRecordedElapsed = -SampleInterval;
            var last = baseline;
            var screenshotCaptured = false;
            var discoverLookRightScreenshotCaptured = false;
            var discoverReturnScreenshotCaptured = false;
            var sighMiddleScreenshotCaptured = false;

            while (Time.unscaledTime <= deadline)
            {
                yield return new WaitForEndOfFrame();
                var elapsed = Time.unscaledTime - startedAt;
                var sample = Capture(actionIndex, spec.motion.ToString(),
                    elapsed <= check.nominalDurationSeconds ? "active" : "recovery", elapsed);
                last = sample;
                var stageCapturedThisFrame = false;
                if (!check.invalidatedByExternalPreview &&
                    performance.MotionTriggerSerial != check.triggerSerial)
                {
                    check.invalidatedByExternalPreview = true;
                    check.invalidatingTriggerSerial = performance.MotionTriggerSerial;
                    check.invalidatedAtElapsed = elapsed;
                    check.invalidatedMotion = sample.currentMotion;
                }
                UpdateActionPeaks(check, spec, baseline, sample);
                if (elapsed - lastRecordedElapsed >= SampleInterval)
                {
                    report.samples.Add(sample);
                    lastRecordedElapsed = elapsed;
                }

                if (spec.expectation == EmotionExpectation.Discover)
                {
                    if (!discoverLookRightScreenshotCaptured && check.discoverLookRightObserved &&
                        sample.elapsed >= DiscoverLookRightScreenshotEarliest &&
                        sample.elapsed <= DiscoverLookRightScreenshotLatest &&
                        performance.CurrentMotion == spec.motion)
                    {
                        CaptureStageScreenshot(check, actionIndex, "look-right", sample);
                        discoverLookRightScreenshotCaptured = true;
                        stageCapturedThisFrame = true;
                    }
                    if (!discoverReturnScreenshotCaptured && check.discoverReturnedToPlayerObserved &&
                        sample.elapsed >= DiscoverReturnScreenshotEarliest &&
                        performance.CurrentMotion == spec.motion)
                    {
                        CaptureStageScreenshot(check, actionIndex, "return-player", sample);
                        discoverReturnScreenshotCaptured = true;
                        stageCapturedThisFrame = true;
                    }
                }
                if (spec.expectation == EmotionExpectation.Sigh && !sighMiddleScreenshotCaptured &&
                    sample.elapsed >= SighMiddleScreenshotEarliest &&
                    sample.elapsed <= SighMiddleScreenshotLatest &&
                    IsSighEyesAndLayerVisible(sample) && performance.CurrentMotion == spec.motion)
                {
                    CaptureStageScreenshot(check, actionIndex, "sigh-middle", sample);
                    sighMiddleScreenshotCaptured = true;
                    stageCapturedThisFrame = true;
                }

                if (!stageCapturedThisFrame && !screenshotCaptured &&
                    ShouldCaptureScreenshot(check, spec, baseline, sample))
                {
                    CaptureScreenshot(check, actionIndex, "observed_peak_window");
                    screenshotCaptured = true;
                }
            }

            if (!screenshotCaptured)
                CaptureScreenshot(check, actionIndex, "fallback_no_activation_threshold");
            if (last != null && (report.samples.Count == 0 || report.samples[report.samples.Count - 1] != last))
                report.samples.Add(last);
            check.sampleEnd = report.samples.Count - 1;
            check.sampleCount = Mathf.Max(0, check.sampleEnd - check.sampleStart + 1);
            EvaluateAction(check, spec, last);
        }

        private IEnumerator PrepareNeutral()
        {
            if (performance == null)
                yield break;
            performance.ResetPerformance();
            yield return new WaitForSecondsRealtime(NeutralSettleSeconds);
            yield return new WaitForEndOfFrame();
        }

        private Sample Capture(int actionIndex, string preset, string phase, float elapsed)
        {
            var sample = new Sample
            {
                time = Time.unscaledTime - runStartedAt,
                actionIndex = actionIndex,
                preset = preset ?? string.Empty,
                phase = phase ?? string.Empty,
                elapsed = elapsed,
                currentMotion = performance == null ? string.Empty : performance.CurrentMotion.ToString(),
                currentExpression = performance == null ? string.Empty : performance.CurrentExpression.ToString(),
                currentGaze = performance == null ? string.Empty : performance.CurrentGaze.ToString(),
                angleX = Read("ParamAngleX"),
                angleY = Read("ParamAngleY"),
                angleZ = Read("ParamAngleZ"),
                bodyAngleX = Read("ParamBodyAngleX"),
                bodyAngleY = Read("ParamBodyAngleY"),
                bodyAngleZ = Read("ParamBodyAngleZ"),
                eyeLOpen = Read("ParamEyeLOpen"),
                eyeROpen = Read("ParamEyeROpen"),
                eyeSmile = Read("ParamEyeSmile"),
                browForm = Read("ParamBrowForm"),
                mouthVariant = Read("ParamMouthVariant"),
                mouthForm = Read("ParamMouthForm"),
                mouthOpenY = Read("ParamMouthOpenY"),
                cheek = Read("ParamCheek"),
                eyeBallX = Read("ParamEyeBallX"),
                eyeBallY = Read("ParamEyeBallY"),
                sighFaceActive = performance != null && performance.SighFaceActive,
                sighEyeArtWeight = performance == null ? 0f : performance.SighEyeArtWeight,
                sighMouthArtWeight = performance == null ? 0f : performance.SighMouthArtWeight,
            };
            CheckRanges();
            return sample;
        }

        private float Read(string id)
        {
            return performance != null && performance.TryGetParameterValue(id, out var value) ? value : 0f;
        }

        private void CheckRanges()
        {
            var model = performance == null ? null : performance.CubismModel;
            var modelParameters = model == null ? null : model.Parameters;
            if (modelParameters == null)
                return;
            for (var i = 0; i < modelParameters.Length; i++)
            {
                var parameter = modelParameters[i];
                if (parameter == null || !ranges.TryGetValue(parameter.Id, out var result))
                    continue;
                var value = parameter.Value;
                result.sampleCount++;
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    result.nonFiniteSamples++;
                    result.inRange = false;
                    continue;
                }
                result.minimumObserved = Mathf.Min(result.minimumObserved, value);
                result.maximumObserved = Mathf.Max(result.maximumObserved, value);
                if (value < result.minimum - RangeTolerance)
                {
                    result.maxBelowRange = Mathf.Max(result.maxBelowRange, result.minimum - value);
                    result.inRange = false;
                }
                if (value > result.maximum + RangeTolerance)
                {
                    result.maxAboveRange = Mathf.Max(result.maxAboveRange, value - result.maximum);
                    result.inRange = false;
                }
            }
        }

        private static List<ValueReport> CreateValueReports(string[] ids, Sample baseline,
            float eyeActivationThreshold = EyeActivationThreshold)
        {
            var values = new List<ValueReport>();
            for (var i = 0; i < ids.Length; i++)
            {
                values.Add(new ValueReport
                {
                    id = ids[i],
                    baseline = Value(baseline, ids[i]),
                    minimumObserved = Value(baseline, ids[i]),
                    maximumObserved = Value(baseline, ids[i]),
                    peakValue = Value(baseline, ids[i]),
                    activationThresholdNormalized = IsEye(ids[i])
                        ? eyeActivationThreshold
                        : ExpressionActivationThreshold,
                });
            }
            return values;
        }

        private void UpdateActionPeaks(ActionReport check, EmotionSpec spec, Sample baseline, Sample sample)
        {
            UpdateValueReports(check.expressions, baseline, sample);
            UpdateValueReports(check.gaze, baseline, sample);
            var head = PeakMotion(baseline, sample, HeadIds, out var headId, out var headValue);
            if (head > check.headPeakNormalized)
            {
                check.headPeakNormalized = head;
                check.headPeakParameter = headId;
                check.headPeakValue = headValue;
            }
            var body = PeakMotion(baseline, sample, BodyIds, out var bodyId, out var bodyValue);
            if (body > check.bodyPeakNormalized)
            {
                check.bodyPeakNormalized = body;
                check.bodyPeakParameter = bodyId;
                check.bodyPeakValue = bodyValue;
            }
            if (spec.expectation == EmotionExpectation.Startle &&
                sample != null && sample.elapsed >= StartleImpactWindowStart &&
                sample.elapsed <= StartleImpactWindowEnd)
            {
                var earlyHead = PeakMotion(baseline, sample, HeadIds, out var earlyHeadId, out var earlyHeadValue);
                var earlyBody = PeakMotion(baseline, sample, BodyIds, out var earlyBodyId, out var earlyBodyValue);
                var earlyPeak = earlyHead >= earlyBody ? earlyHead : earlyBody;
                var earlyId = earlyHead >= earlyBody ? earlyHeadId : earlyBodyId;
                var earlyValue = earlyHead >= earlyBody ? earlyHeadValue : earlyBodyValue;
                if (earlyPeak > check.earlyPeakNormalized)
                {
                    check.earlyPeakNormalized = earlyPeak;
                    check.earlyPeakElapsed = sample.elapsed;
                    check.earlyPeakParameter = earlyId;
                    check.earlyPeakValue = earlyValue;
                }
                check.earlyImpactObserved = check.earlyPeakNormalized >= StartleImpactThreshold;
            }
            check.gazePeakNormalized = Mathf.Max(check.gazePeakNormalized,
                PeakMotion(baseline, sample, GazeIds, out _, out _));
            var eyeBallXDelta = sample.eyeBallX - Value(baseline, "ParamEyeBallX");
            if (Mathf.Abs(eyeBallXDelta) > Mathf.Abs(check.eyeBallXPeakDelta))
            {
                check.eyeBallXPeakDelta = eyeBallXDelta;
                check.eyeBallXPeakValue = sample.eyeBallX;
            }
            if (spec.motion == ZMotionPreset.Wink)
            {
                var difference = Mathf.Abs(sample.eyeLOpen - sample.eyeROpen);
                check.winkPeakDifference = Mathf.Max(check.winkPeakDifference, difference);
                check.winkMinimumEye = Mathf.Min(check.winkMinimumEye, Mathf.Min(sample.eyeLOpen, sample.eyeROpen));
                check.winkMaximumEye = Mathf.Max(check.winkMaximumEye, Mathf.Max(sample.eyeLOpen, sample.eyeROpen));
                if (difference > WinkDifferenceThreshold && Mathf.Max(sample.eyeLOpen, sample.eyeROpen) > WinkOpenEyeThreshold)
                    check.winkAsymmetryObserved = true;
            }
            UpdateExpectedFace(check, spec, baseline, sample);
            UpdateAccent(check, spec, sample);
            check.peakScore = Mathf.Max(check.peakScore, ActivationScore(check, spec, baseline, sample));
        }

        private void UpdateExpectedFace(ActionReport check, EmotionSpec spec, Sample baseline, Sample sample)
        {
            var minimumEye = Mathf.Min(sample.eyeLOpen, sample.eyeROpen);
            switch (spec.expectation)
            {
                case EmotionExpectation.Surprise:
                    check.expectedFaceObserved |= sample.mouthVariant > 0.5f;
                    break;
                case EmotionExpectation.Curious:
                    check.expectedFaceObserved |= sample.browForm > 0.4f && IsMouthLineOrO(sample.mouthVariant);
                    break;
                case EmotionExpectation.Shy:
                    check.expectedFaceObserved |= sample.cheek > 0.5f && sample.mouthForm > 0.10f;
                    break;
                case EmotionExpectation.Sad:
                    check.expectedFaceObserved |= sample.mouthVariant < -1.5f && sample.mouthForm < 0f &&
                                                   minimumEye >= 0.70f && minimumEye <= 0.90f;
                    break;
                case EmotionExpectation.Angry:
                    check.expectedFaceObserved |= sample.browForm < -0.4f && sample.mouthVariant < -0.5f &&
                                                   minimumEye >= 0.65f && minimumEye <= 0.85f && minimumEye > 0.15f;
                    break;
                case EmotionExpectation.Wink:
                    check.expectedFaceObserved |= sample.mouthForm > 0.10f;
                    break;
                case EmotionExpectation.Sorrow:
                    check.expectedFaceObserved |= sample.mouthVariant < -1.5f && sample.mouthForm < 0f &&
                                                   minimumEye >= 0.55f && minimumEye <= 0.75f;
                    break;
                case EmotionExpectation.Cry:
                case EmotionExpectation.Bawl:
                    check.expectedFaceObserved |= sample.mouthVariant > 1.5f && sample.mouthForm < 0f &&
                                                   Mathf.Max(sample.eyeLOpen, sample.eyeROpen) <= 0.12f;
                    break;
                case EmotionExpectation.Warm:
                    check.expectedFaceObserved |= Mathf.Abs(sample.eyeSmile) <= 0.15f &&
                                                   minimumEye >= 0.75f && minimumEye <= 1.05f &&
                                                   sample.mouthForm > 0.08f && sample.cheek >= 0.05f && sample.cheek <= 0.45f;
                    break;
                case EmotionExpectation.Serious:
                    check.expectedFaceObserved |= Mathf.Abs(sample.eyeSmile) <= 0.15f &&
                                                   minimumEye >= 0.75f && sample.mouthVariant <= -0.5f &&
                                                   sample.mouthVariant >= -1.5f && sample.mouthForm <= 0.10f &&
                                                   Mathf.Abs(sample.browForm) <= 0.25f && Mathf.Abs(sample.cheek) <= 0.20f;
                    break;
                case EmotionExpectation.LookLeft:
                case EmotionExpectation.LookRight:
                    {
                        var eyeBallXDelta = sample.eyeBallX - Value(baseline, "ParamEyeBallX");
                        var direction = spec.expectedEyeBallXDirection;
                        var directionObserved = NormalizedDelta("ParamEyeBallX", sample.eyeBallX,
                            Value(baseline, "ParamEyeBallX")) >= spec.eyeActivationThreshold &&
                            ((direction < 0 && eyeBallXDelta < 0f) || (direction > 0 && eyeBallXDelta > 0f));
                        check.gazeDirectionObserved |= directionObserved;
                        check.expectedFaceObserved |= directionObserved;
                    }
                    break;
                case EmotionExpectation.Lean:
                    check.bodyYObserved |= NormalizedDelta("ParamBodyAngleY", sample.bodyAngleY,
                        Value(baseline, "ParamBodyAngleY")) >= BodyMotionThreshold;
                    check.expectedFaceObserved |= check.bodyYObserved;
                    break;
                case EmotionExpectation.Tilt:
                    check.tiltZObserved |= NormalizedDelta("ParamAngleZ", sample.angleZ,
                        Value(baseline, "ParamAngleZ")) >= TiltZThreshold;
                    check.tiltZPeakNormalized = Mathf.Max(check.tiltZPeakNormalized,
                        NormalizedDelta("ParamAngleZ", sample.angleZ, Value(baseline, "ParamAngleZ")));
                    check.expectedFaceObserved |= check.tiltZObserved;
                    break;
                case EmotionExpectation.Emphasis:
                    check.emphasisHeadBodyObserved |= check.headPeakNormalized >= HeadMotionThreshold &&
                                                       check.bodyPeakNormalized >= BodyMotionThreshold;
                    check.expectedFaceObserved |= check.emphasisHeadBodyObserved;
                    break;
                case EmotionExpectation.Startle:
                    check.startleOMouthObserved |= sample.mouthVariant >= StartleOMouthMinimum &&
                                                  sample.mouthVariant <= StartleOMouthMaximum;
                    check.expectedFaceObserved |= check.earlyImpactObserved && check.startleOMouthObserved;
                    break;
                case EmotionExpectation.Discover:
                    UpdateDiscoverSequence(check, baseline, sample);
                    check.expectedFaceObserved |= check.discoverReturnedToPlayerObserved;
                    break;
                case EmotionExpectation.Gloom:
                    check.gloomColdFaceObserved |= IsGloomFaceObserved(sample, baseline);
                    check.expectedFaceObserved |= check.gloomColdFaceObserved;
                    break;
                case EmotionExpectation.Sigh:
                    var sighEyesClosed = Mathf.Max(sample.eyeLOpen, sample.eyeROpen) <= SighEyeClosedMaximum;
                    var sighLayerVisible = ObserveSighFaceArt(check);
                    check.sighEyesClosedObserved |= sighEyesClosed;
                    check.sighEyesAndLayerSameSampleObserved |= sighEyesClosed && sighLayerVisible;
                    check.expectedFaceObserved |= sighEyesClosed && sighLayerVisible;
                    break;
                default:
                    check.expectedFaceObserved = true;
                    break;
            }
        }

        private static bool IsMouthLineOrO(float value)
        {
            return (value >= -1.5f && value <= -0.5f) || (value >= 0.5f && value <= 1.5f);
        }

        private void UpdateDiscoverSequence(ActionReport check, Sample baseline, Sample sample)
        {
            if (check == null || baseline == null || sample == null)
                return;

            var delta = sample.eyeBallX - baseline.eyeBallX;
            var normalized = NormalizedDelta("ParamEyeBallX", sample.eyeBallX, baseline.eyeBallX);
            if (!check.discoverLookRightObserved && delta > 0f && normalized >= DiscoverLookRightThreshold)
            {
                check.discoverLookRightObserved = true;
                check.discoverLookRightElapsed = sample.elapsed;
                check.discoverLookRightDelta = delta;
                check.gazeDirectionObserved = true;
            }

            if (check.discoverLookRightObserved && !check.discoverReturnedToPlayerObserved &&
                sample.elapsed >= check.discoverLookRightElapsed + DiscoverReturnMinimumDelay &&
                normalized <= DiscoverPlayerTolerance &&
                (performance == null || performance.CurrentGaze == ZGaze.Player))
            {
                check.discoverReturnedToPlayerObserved = true;
                check.discoverReturnToPlayerElapsed = sample.elapsed;
            }

            check.discoverPeakLookRightDelta = Mathf.Max(check.discoverPeakLookRightDelta, delta);
        }

        private bool IsGloomFaceObserved(Sample sample, Sample baseline)
        {
            if (sample == null || baseline == null)
                return false;

            var mouthChanged = sample.mouthVariant <= -0.25f ||
                               NormalizedDelta("ParamMouthVariant", sample.mouthVariant, baseline.mouthVariant) >=
                               ExpressionActivationThreshold;
            var mouthClosed = sample.mouthForm <= 0.10f;
            var eyeSmileSuppressed = sample.eyeSmile <= baseline.eyeSmile + 0.05f;
            return mouthChanged && mouthClosed && eyeSmileSuppressed;
        }

        private bool ObserveSighFaceArt(ActionReport check)
        {
            ResolveSighFaceArt();
            if (check == null || sighFaceArt == null)
                return false;

            check.sighFaceArtFound = true;
            check.sighFaceArtReadyObserved |= sighFaceArt.IsSighReady;
            check.sighFaceArtVisibleObserved |= sighFaceArt.IsSighVisible;
            check.sighFaceArtPeakIntensity = Mathf.Max(check.sighFaceArtPeakIntensity,
                sighFaceArt.SighVisibleIntensity);
            check.sighFaceArtMaximumInstanceCount = Mathf.Max(check.sighFaceArtMaximumInstanceCount,
                sighFaceArt.SighVisibleInstanceCount);
            var visible = IsSighFaceArtVisible();
            check.sighFaceArtAllInstancesObserved |= visible &&
                                                     sighFaceArt.SighVisibleInstanceCount == SighRequiredInstanceCount;
            check.sighFaceArtSingleComponentObserved |= visible && IsSingleSighFaceArtComponent();
            return visible;
        }

        private bool IsSingleSighFaceArtComponent()
        {
            ResolveSighFaceArt();
            var arts = FindObjectsOfType<ZSighFaceArt>();
            return arts != null && arts.Length == 1 && arts[0] == sighFaceArt;
        }

        private bool IsSighFaceArtVisible()
        {
            ResolveSighFaceArt();
            return sighFaceArt != null && sighFaceArt.IsSighReady && sighFaceArt.IsSighVisible &&
                   sighFaceArt.SighVisibleIntensity >= SighArtActivationThreshold &&
                   sighFaceArt.SighVisibleInstanceCount == SighRequiredInstanceCount && performance != null &&
                   IsSingleSighFaceArtComponent() &&
                   performance.SighFaceActive &&
                   performance.SighEyeArtWeight >= SighArtActivationThreshold &&
                   performance.SighMouthArtWeight >= SighArtActivationThreshold;
        }

        private bool IsSighFaceArtCleared()
        {
            ResolveSighFaceArt();
            return sighFaceArt != null && sighFaceArt.IsSighReady && !sighFaceArt.IsSighVisible &&
                   sighFaceArt.SighVisibleIntensity <= AccentResidualTolerance &&
                   sighFaceArt.SighVisibleInstanceCount <= 0 && sighFaceArt.MaterialsRestored &&
                   (performance == null || (!performance.SighFaceActive &&
                                             performance.SighEyeArtWeight <= AccentResidualTolerance &&
                                             performance.SighMouthArtWeight <= AccentResidualTolerance));
        }

        private bool IsSighEyesAndLayerVisible(Sample sample)
        {
            return sample != null && Mathf.Max(sample.eyeLOpen, sample.eyeROpen) <= SighEyeClosedMaximum &&
                   IsSighFaceArtVisible();
        }

        private void UpdateAccent(ActionReport check, EmotionSpec spec, Sample sample)
        {
            if (accents == null)
                return;
            var outerTearMotion = spec.motion == ZMotionPreset.Cry || spec.motion == ZMotionPreset.Bawl;
            if (outerTearMotion && string.Equals(accents.ActiveAccent, spec.accentName, StringComparison.OrdinalIgnoreCase))
                check.outerTearPlacementObserved |= accents.TearsStartAtOuterCorners;
            if (string.IsNullOrEmpty(spec.accentName))
                return;
            if (!string.Equals(accents.ActiveAccent, spec.accentName, StringComparison.OrdinalIgnoreCase))
                return;
            check.accentPeakIntensity = Mathf.Max(check.accentPeakIntensity, accents.VisibleIntensity);
            check.accentAnchorsReadyObserved |= accents.AnchorsReady;
            check.accentActivated |= accents.VisibleIntensity >= 0.25f && accents.AnchorsReady;
            if (spec.expectation == EmotionExpectation.Startle &&
                sample != null && sample.elapsed >= StartleImpactWindowStart &&
                sample.elapsed <= StartleImpactWindowEnd)
            {
                check.earlyAccentPeakIntensity = Mathf.Max(check.earlyAccentPeakIntensity,
                    accents.VisibleIntensity);
                check.earlyAccentActivated |= accents.VisibleIntensity >= 0.25f && accents.AnchorsReady;
            }
        }

        private void UpdateValueReports(List<ValueReport> reports, Sample baseline, Sample sample)
        {
            for (var i = 0; i < reports.Count; i++)
            {
                var reportValue = reports[i];
                var value = Value(sample, reportValue.id);
                var delta = NormalizedDelta(reportValue.id, value, reportValue.baseline);
                if (delta > reportValue.peakDeltaNormalized)
                {
                    reportValue.peakDeltaNormalized = delta;
                    reportValue.peakValue = value;
                }
                reportValue.minimumObserved = Mathf.Min(reportValue.minimumObserved, value);
                reportValue.maximumObserved = Mathf.Max(reportValue.maximumObserved, value);
                reportValue.activated = reportValue.peakDeltaNormalized >= reportValue.activationThresholdNormalized;
            }
        }

        private float PeakMotion(Sample baseline, Sample sample, string[] ids, out string peakId, out float peakValue)
        {
            peakId = string.Empty;
            peakValue = 0f;
            var peak = 0f;
            for (var i = 0; i < ids.Length; i++)
            {
                var value = Value(sample, ids[i]);
                var delta = NormalizedDelta(ids[i], value, Value(baseline, ids[i]));
                if (delta > peak)
                {
                    peak = delta;
                    peakId = ids[i];
                    peakValue = value;
                }
            }
            return peak;
        }

        private float NormalizedDelta(string id, float value, float baseline)
        {
            if (!parameters.TryGetValue(id, out var info))
                return 0f;
            return Mathf.Abs(value - baseline) / Mathf.Max(0.001f, info.maximum - info.minimum);
        }

        private static bool IsEye(string id)
        {
            return id == "ParamEyeLOpen" || id == "ParamEyeROpen";
        }

        private float ActivationScore(ActionReport check, EmotionSpec spec, Sample baseline, Sample sample)
        {
            var expression = 0f;
            for (var i = 0; i < spec.expressionIds.Length; i++)
                expression = Mathf.Max(expression, NormalizedDelta(spec.expressionIds[i],
                    Value(sample, spec.expressionIds[i]), Value(baseline, spec.expressionIds[i])));
            var motion = Mathf.Max(
                PeakMotion(baseline, sample, HeadIds, out _, out _),
                PeakMotion(baseline, sample, BodyIds, out _, out _));
            // Keep the existing nine-action screenshot score unchanged. Only
            // the new explicit look actions use gaze as an independent trigger.
            var gaze = spec.expectedEyeBallXDirection == 0
                ? 0f
                : PeakMotion(baseline, sample, GazeIds, out _, out _);
            if (spec.motion == ZMotionPreset.Wink)
                return Mathf.Abs(sample.eyeLOpen - sample.eyeROpen);
            return Mathf.Max(expression, motion, gaze);
        }

        private bool ShouldCaptureScreenshot(ActionReport check, EmotionSpec spec, Sample baseline, Sample sample)
        {
            var headReady = !spec.requireHeadMotion ||
                            PeakMotion(baseline, sample, HeadIds, out _, out _) >= HeadMotionThreshold;
            var bodyReady = !spec.requireBodyMotion ||
                            PeakMotion(baseline, sample, BodyIds, out _, out _) >= BodyMotionThreshold;
            if (sample.elapsed < spec.screenshotEarliestSeconds ||
                sample.elapsed > check.nominalDurationSeconds ||
                performance == null || !performance.IsPreviewPresentationActive ||
                performance.CurrentMotion != spec.motion ||
                !headReady || !bodyReady ||
                (!string.IsNullOrEmpty(spec.accentName) && !IsAccentVisible(spec.accentName)) ||
                !CurrentExpressionsActivated(spec, baseline, sample))
                return false;
            if (spec.expectation == EmotionExpectation.Startle &&
                (sample.elapsed < StartleImpactWindowStart || sample.elapsed > StartleScreenshotLatest ||
                 !check.earlyImpactObserved || !check.earlyAccentActivated ||
                 sample.mouthVariant < StartleOMouthMinimum ||
                 sample.mouthVariant > StartleOMouthMaximum))
                return false;
            if (spec.expectation == EmotionExpectation.Sigh &&
                !IsSighEyesAndLayerVisible(sample))
                return false;
            if (spec.motion == ZMotionPreset.Wink)
                return Mathf.Abs(sample.eyeLOpen - sample.eyeROpen) > WinkDifferenceThreshold &&
                       Mathf.Max(sample.eyeLOpen, sample.eyeROpen) > WinkOpenEyeThreshold;
            return ActivationScore(check, spec, baseline, sample) >= spec.screenshotActivationThreshold;
        }

        private bool CurrentExpressionsActivated(EmotionSpec spec, Sample baseline, Sample sample)
        {
            for (var i = 0; i < spec.expressionIds.Length; i++)
            {
                var id = spec.expressionIds[i];
                if (spec.motion == ZMotionPreset.Wink && id == "ParamEyeROpen")
                    continue;
                var threshold = spec.ActivationThreshold(id);
                if (NormalizedDelta(id, Value(sample, id), Value(baseline, id)) < threshold)
                    return false;
            }
            return true;
        }

        private void CaptureNeutralScreenshot()
        {
            if (string.IsNullOrEmpty(outputDirectory))
                return;
            report.neutralScreenshotFile = "emotion-00-neutral.png";
            report.neutralScreenshotRequested = true;
            ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, report.neutralScreenshotFile));
            report.neutralFaceScreenshotFile = "emotion-00-neutral-face.png";
            report.neutralFaceScreenshotRequested = true;
            CaptureFaceScreenshot(null, report.neutralFaceScreenshotFile, "neutral");
        }

        private void CaptureScreenshot(ActionReport check, int actionIndex, string reason)
        {
            if (string.IsNullOrEmpty(outputDirectory))
                return;
            var file = string.Format(CultureInfo.InvariantCulture, "emotion-{0:00}-{1}.png",
                actionIndex + 1, check.preset.ToLowerInvariant());
            ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, file));
            check.screenshotRequested = true;
            check.screenshotFile = file;
            check.screenshotCaptureReason = reason;
            check.screenshotCaptureTime = Time.unscaledTime - runStartedAt;
            check.captureDuringActiveMotion = performance != null &&
                                              performance.IsPreviewPresentationActive &&
                                              string.Equals(performance.CurrentMotion.ToString(), check.preset,
                                                  StringComparison.Ordinal);

            var faceFile = string.Format(CultureInfo.InvariantCulture, "emotion-{0:00}-{1}-face.png",
                actionIndex + 1, check.preset.ToLowerInvariant());
            check.faceScreenshotFile = faceFile;
            check.faceScreenshotRequested = true;
            check.faceScreenshotCaptureReason = reason;
            check.faceScreenshotCaptureTime = Time.unscaledTime - runStartedAt;
            CaptureFaceScreenshot(check, faceFile, reason);
        }

        private void CaptureStageScreenshot(ActionReport check, int actionIndex, string stage, Sample sample)
        {
            if (check == null || string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(stage))
                return;

            var file = string.Format(CultureInfo.InvariantCulture, "emotion-{0:00}-{1}-{2}.png",
                actionIndex + 1, check.preset.ToLowerInvariant(), stage.Trim().ToLowerInvariant().Replace(" ", "-"));
            ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, file));
            if (check.stageScreenshots == null)
                check.stageScreenshots = new List<StageScreenshotReport>();
            check.stageScreenshots.Add(new StageScreenshotReport
            {
                stage = stage,
                file = file,
                requested = true,
                captureDuringActiveMotion = performance != null &&
                                             performance.IsPreviewPresentationActive &&
                                             performance.CurrentMotion.ToString() == check.preset,
                elapsed = sample == null ? 0f : sample.elapsed,
                eyeBallX = sample == null ? 0f : sample.eyeBallX,
                currentGaze = sample == null ? string.Empty : sample.currentGaze,
            });
        }

        private void CaptureFaceScreenshot(ActionReport check, string file, string reason)
        {
            if (string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(file))
                return;

            var paddingRatio = check != null &&
                               string.Equals(check.preset, ZMotionPreset.Bawl.ToString(), StringComparison.Ordinal)
                ? 0.35f
                : 0.10f;
            if (!TryGetFaceScreenBounds(out var bounds, out var failure, paddingRatio))
            {
                report.errors.Add("face_screenshot_failed_" + Normalize(reason) + "_" + Normalize(failure));
                return;
            }

            Texture2D texture = null;
            try
            {
                // Read the rendered backbuffer at EndOfFrame. This is an actual
                // screen-pixel crop, not a model-space or UI-layout preview.
                texture = new Texture2D(bounds.width, bounds.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(bounds.x, bounds.y, bounds.width, bounds.height), 0, 0, false);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(outputDirectory, file), texture.EncodeToPNG());

                if (check != null)
                {
                    check.faceScreenshotWidth = bounds.width;
                    check.faceScreenshotHeight = bounds.height;
                    check.faceBoundsX = bounds.x;
                    check.faceBoundsY = bounds.y;
                    check.faceScreenshotPresent = true;
                }
                else
                {
                    report.neutralFaceScreenshotWidth = bounds.width;
                    report.neutralFaceScreenshotHeight = bounds.height;
                    report.neutralFaceBoundsX = bounds.x;
                    report.neutralFaceBoundsY = bounds.y;
                    report.neutralFaceScreenshotPresent = true;
                }
            }
            catch (Exception exception)
            {
                report.errors.Add("face_screenshot_write_failed_" + exception.GetType().Name);
            }
            finally
            {
                if (texture != null)
                    Destroy(texture);
            }
        }

        private bool TryGetFaceScreenBounds(out RectInt bounds, out string failure, float paddingRatio)
        {
            bounds = default(RectInt);
            failure = "unknown";
            var model = performance == null ? null : performance.CubismModel;
            if (model == null || model.Drawables == null)
            {
                failure = "model_missing";
                return false;
            }

            CubismDrawable face = null;
            for (var i = 0; i < model.Drawables.Length; i++)
            {
                var drawable = model.Drawables[i];
                if (drawable == null)
                    continue;
                try
                {
                    if (string.Equals(drawable.Id, "Face_Skin", StringComparison.OrdinalIgnoreCase))
                    {
                        face = drawable;
                        break;
                    }
                }
                catch (Exception)
                {
                    // The model may expose a proxy before its unmanaged arrays are ready.
                }
            }

            if (face == null)
            {
                failure = "face_skin_missing";
                return false;
            }

            Vector3[] vertices;
            try
            {
                // VertexPositions is sampled after the current Cubism update, so
                // expressions and eye closure are reflected in the world bounds.
                vertices = face.VertexPositions;
            }
            catch (Exception)
            {
                failure = "face_vertices_unavailable";
                return false;
            }

            if (vertices == null || vertices.Length == 0)
            {
                failure = "face_vertices_empty";
                return false;
            }

            var camera = Camera.main;
            if (camera == null)
                camera = FindObjectOfType<Camera>();
            if (camera == null)
            {
                failure = "camera_missing";
                return false;
            }

            var minX = float.PositiveInfinity;
            var minY = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var maxY = float.NegativeInfinity;
            var visibleVertexCount = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var screen = camera.WorldToScreenPoint(face.transform.TransformPoint(vertices[i]));
                if (screen.z <= 0f || float.IsNaN(screen.x) || float.IsNaN(screen.y) ||
                    float.IsInfinity(screen.x) || float.IsInfinity(screen.y))
                    continue;
                minX = Mathf.Min(minX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxX = Mathf.Max(maxX, screen.x);
                maxY = Mathf.Max(maxY, screen.y);
                visibleVertexCount++;
            }

            if (visibleVertexCount == 0 || maxX <= minX || maxY <= minY)
            {
                failure = "face_out_of_view";
                return false;
            }

            // Face_Skin covers forehead, eye/brow area and chin. Bawl also
            // renders a long outer tear column, so its face crop gets 35%
            // padding on both axes; other actions retain the 10% crop.
            paddingRatio = Mathf.Clamp(paddingRatio, 0.10f, 0.35f);
            var paddingX = Mathf.Max(1f, (maxX - minX) * paddingRatio);
            var paddingY = Mathf.Max(1f, (maxY - minY) * paddingRatio);
            var left = Mathf.Clamp(Mathf.FloorToInt(minX - paddingX), 0, Screen.width - 1);
            var bottom = Mathf.Clamp(Mathf.FloorToInt(minY - paddingY), 0, Screen.height - 1);
            var right = Mathf.Clamp(Mathf.CeilToInt(maxX + paddingX), left + 1, Screen.width);
            var top = Mathf.Clamp(Mathf.CeilToInt(maxY + paddingY), bottom + 1, Screen.height);
            if (right <= left || top <= bottom)
            {
                failure = "face_crop_empty";
                return false;
            }

            bounds = new RectInt(left, bottom, right - left, top - bottom);
            return true;
        }

        private void EvaluateAction(ActionReport check, EmotionSpec spec, Sample finalSample)
        {
            check.relatedParametersActivated = true;
            for (var i = 0; i < check.expressions.Count; i++)
            {
                check.expressions[i].activated = check.expressions[i].IsActivated(parameters);
                if (!check.expressions[i].activated)
                    check.relatedParametersActivated = false;
            }
            MarkAvailable(check.gaze);
            check.headMoved = check.headPeakNormalized >= HeadMotionThreshold;
            check.bodyMoved = check.bodyPeakNormalized >= BodyMotionThreshold;
            check.naturalCompletion = performance != null && performance.CurrentMotion == ZMotionPreset.Idle;
            check.faceRecovered = Recovered(finalSample, FaceIds);
            check.mouthRecovered = Recovered(finalSample, MouthIds);
            check.mouthOpenRecovered = !parameters.ContainsKey("ParamMouthOpenY") ||
                                      Recovered(finalSample, OptionalMouthOpenIds);
            check.gazeRecovered = Recovered(finalSample, GazeIds);
            check.eyesRecovered = Recovered(finalSample, EyeIds);
            check.expressionNeutral = performance != null && performance.CurrentExpression == ZFaceExpression.Neutral;
            check.gazePlayer = performance != null && performance.CurrentGaze == ZGaze.Player;
            check.rangePassed = AllRangesInRange();
            check.winkPassed = spec.motion != ZMotionPreset.Wink || check.winkAsymmetryObserved;
            check.expectedFacePassed = check.expectedFaceObserved;
            check.accentRequired = !string.IsNullOrEmpty(spec.accentName);
            check.outerTearPlacementRequired = spec.motion == ZMotionPreset.Cry || spec.motion == ZMotionPreset.Bawl;
            check.accentCleared = accents == null || IsAccentCleared();
            check.finalEyeLOpen = finalSample.eyeLOpen;
            check.finalEyeROpen = finalSample.eyeROpen;
            check.finalEyeBallX = finalSample.eyeBallX;
            check.finalEyeBallY = finalSample.eyeBallY;
            check.finalEyeSmile = finalSample.eyeSmile;
            check.finalBrowForm = finalSample.browForm;
            check.finalMouthVariant = finalSample.mouthVariant;
            check.finalMouthForm = finalSample.mouthForm;
            check.finalMouthOpenY = finalSample.mouthOpenY;
            check.finalCheek = finalSample.cheek;
            if (spec.expectation == EmotionExpectation.Sigh)
            {
                ResolveSighFaceArt();
                check.sighFaceArtFound = sighFaceArt != null;
                check.sighFaceArtClearedAfterAction = IsSighFaceArtCleared();
            }
            check.specialExpectationPassed = CheckSpecialExpectation(check, spec);
            check.expectedFacePassed = check.expectedFaceObserved && check.specialExpectationPassed;
            AddActionFailures(check, spec);
        }

        private bool CheckSpecialExpectation(ActionReport check, EmotionSpec spec)
        {
            switch (spec.expectation)
            {
                case EmotionExpectation.Tilt:
                    return check.tiltZObserved;
                case EmotionExpectation.Emphasis:
                    return check.emphasisHeadBodyObserved;
                case EmotionExpectation.Startle:
                    return check.earlyImpactObserved && check.earlyAccentActivated && check.startleOMouthObserved;
                case EmotionExpectation.Discover:
                    return check.discoverLookRightObserved && check.discoverReturnedToPlayerObserved;
                case EmotionExpectation.Gloom:
                    return check.gloomColdFaceObserved;
                case EmotionExpectation.Sigh:
                    return check.sighEyesAndLayerSameSampleObserved &&
                           check.sighFaceArtReadyObserved && check.sighFaceArtAllInstancesObserved &&
                           check.sighFaceArtSingleComponentObserved &&
                           check.sighFaceArtClearedAfterAction && check.mouthOpenRecovered;
                default:
                    return true;
            }
        }

        private void MarkAvailable(List<ValueReport> values)
        {
            for (var i = 0; i < values.Count; i++)
                values[i].available = values[i].id != null && parameters.ContainsKey(values[i].id);
        }

        private bool IsAccentCleared()
        {
            return accents != null && string.Equals(accents.ActiveAccent, "None", StringComparison.OrdinalIgnoreCase) &&
                   accents.VisibleIntensity <= AccentResidualTolerance && !accents.AnchorsReady;
        }

        private bool IsAccentVisible(string expected)
        {
            return accents != null && string.Equals(accents.ActiveAccent, expected, StringComparison.OrdinalIgnoreCase) &&
                   accents.VisibleIntensity >= 0.25f && accents.AnchorsReady;
        }

        private int CountStageScreenshots(bool requested)
        {
            var count = 0;
            for (var i = 0; i < report.actions.Count; i++)
            {
                var stages = report.actions[i].stageScreenshots;
                if (stages == null)
                    continue;
                for (var j = 0; j < stages.Count; j++)
                {
                    if (requested ? stages[j].requested : stages[j].present)
                        count++;
                }
            }
            return count;
        }

        private void AddActionFailures(ActionReport check, EmotionSpec spec)
        {
            if (!check.relatedParametersActivated) check.failureReasons.Add("expression_parameter_not_activated");
            if (check.headMotionRequired && !check.headMoved) check.failureReasons.Add("head_motion_below_threshold");
            if (check.bodyMotionRequired && !check.bodyMoved) check.failureReasons.Add("body_motion_below_threshold");
            if (!check.naturalCompletion) check.failureReasons.Add("natural_motion_completion_not_observed");
            if (check.invalidatedByExternalPreview) check.failureReasons.Add("invalidated_by_external_preview");
            if (!check.faceRecovered) check.failureReasons.Add("face_not_recovered");
            if (!check.mouthRecovered) check.failureReasons.Add("mouth_not_recovered");
            if (!check.mouthOpenRecovered) check.failureReasons.Add("mouth_open_not_recovered");
            if (!check.gazeRecovered) check.failureReasons.Add("gaze_not_recovered");
            if (!check.eyesRecovered) check.failureReasons.Add("eyes_not_recovered");
            if (!check.expressionNeutral) check.failureReasons.Add("expression_state_not_neutral");
            if (!check.gazePlayer) check.failureReasons.Add("gaze_state_not_player");
            if (!check.rangePassed) check.failureReasons.Add("parameter_out_of_range");
            if (!check.winkPassed) check.failureReasons.Add("wink_asymmetry_not_visible");
            if (!check.expectedFacePassed) check.failureReasons.Add("face_expectation_not_observed");
            if (spec.expectation == EmotionExpectation.Lean && !check.bodyYObserved)
                check.failureReasons.Add("body_angle_y_not_observed");
            if ((spec.expectation == EmotionExpectation.LookLeft || spec.expectation == EmotionExpectation.LookRight) &&
                !check.gazeDirectionObserved)
                check.failureReasons.Add("eye_ball_x_direction_not_observed");
            if (spec.expectation == EmotionExpectation.Tilt && !check.tiltZObserved)
                check.failureReasons.Add("tilt_angle_z_not_observed");
            if (spec.expectation == EmotionExpectation.Emphasis && !check.emphasisHeadBodyObserved)
                check.failureReasons.Add("emphasis_head_body_motion_not_observed");
            if (spec.expectation == EmotionExpectation.Startle && !check.earlyImpactObserved)
                check.failureReasons.Add("startle_early_impact_not_observed");
            if (spec.expectation == EmotionExpectation.Startle && !check.earlyAccentActivated)
                check.failureReasons.Add("startle_early_accent_not_visible");
            if (spec.expectation == EmotionExpectation.Startle && !check.startleOMouthObserved)
                check.failureReasons.Add("startle_o_mouth_not_observed");
            if (spec.expectation == EmotionExpectation.Discover && !check.discoverLookRightObserved)
                check.failureReasons.Add("discover_look_right_not_observed");
            if (spec.expectation == EmotionExpectation.Discover && !check.discoverReturnedToPlayerObserved)
                check.failureReasons.Add("discover_return_to_player_not_observed");
            if (spec.expectation == EmotionExpectation.Gloom && !check.gloomColdFaceObserved)
                check.failureReasons.Add("gloom_cold_face_not_observed");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighEyesClosedObserved)
                check.failureReasons.Add("sigh_eyes_not_fully_closed");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighFaceArtFound)
                check.failureReasons.Add("sigh_face_art_not_found");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighFaceArtReadyObserved)
                check.failureReasons.Add("sigh_face_art_not_ready");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighFaceArtVisibleObserved)
                check.failureReasons.Add("sigh_face_art_not_visible");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighEyesAndLayerSameSampleObserved)
                check.failureReasons.Add("sigh_eyes_and_face_art_not_same_sample");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighFaceArtAllInstancesObserved)
                check.failureReasons.Add("sigh_face_art_instance_count_not_three");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighFaceArtSingleComponentObserved)
                check.failureReasons.Add("sigh_face_art_component_count_not_one");
            if (spec.expectation == EmotionExpectation.Sigh && !check.sighFaceArtClearedAfterAction)
                check.failureReasons.Add("sigh_face_art_not_cleared_after_action");
            if (!check.accentActivated && check.accentRequired) check.failureReasons.Add("accent_not_visible");
            if (check.outerTearPlacementRequired && !check.outerTearPlacementObserved)
                check.failureReasons.Add("tear_outer_corner_not_observed");
            if (!check.accentCleared) check.failureReasons.Add("accent_not_cleared");
        }

        private bool Recovered(Sample sample, string[] ids)
        {
            if (sample == null)
                return false;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!parameters.TryGetValue(ids[i], out var info))
                    return false;
                var tolerance = Mathf.Max(0.05f, (info.maximum - info.minimum) * NeutralTolerance);
                if (Mathf.Abs(Value(sample, ids[i]) - info.defaultValue) > tolerance)
                    return false;
            }
            return true;
        }

        private bool AllRangesInRange()
        {
            foreach (var pair in ranges)
            {
                if (!pair.Value.inRange || pair.Value.sampleCount == 0 || pair.Value.nonFiniteSamples > 0)
                    return false;
            }
            return ranges.Count > 0;
        }

        private IEnumerator RunTransitions()
        {
            var transition = report.transitions;
            yield return PrepareNeutral();

            // Stop while Wink is visibly asymmetric.
            performance.PreviewMotion(ZMotionPreset.Wink);
            yield return WaitForWink("stop-wink");
            var winkBeforeStop = lastWinkTransitionSample;
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(TransitionSeconds);
            yield return new WaitForEndOfFrame();
            var stopAfter = Capture(-1, "StopMotionPreview", "stop-after", 0f);
            report.samples.Add(stopAfter);
            transition.manualStopWinkObserved = winkBeforeStop != null;
            transition.manualStopDifferenceBefore = winkBeforeStop == null
                ? 0f
                : Mathf.Abs(winkBeforeStop.eyeLOpen - winkBeforeStop.eyeROpen);
            transition.manualStopDifferenceAfter = Mathf.Abs(stopAfter.eyeLOpen - stopAfter.eyeROpen);
            transition.manualStopEyeLOpen = stopAfter.eyeLOpen;
            transition.manualStopEyeROpen = stopAfter.eyeROpen;
            transition.manualStopPassed = transition.manualStopWinkObserved &&
                                           performance.CurrentMotion == ZMotionPreset.Idle &&
                                           performance.CurrentExpression == ZFaceExpression.Neutral &&
                                           performance.CurrentGaze == ZGaze.Player &&
                                           transition.manualStopDifferenceAfter <= WinkResidualTolerance &&
                                           Recovered(stopAfter, FaceIds) && Recovered(stopAfter, GazeIds) &&
                                           Recovered(stopAfter, EyeIds);

            // Stop an accent-bearing action as well; the overlay must clear,
            // not only the Cubism parameters.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Cry);
            yield return new WaitForSecondsRealtime(0.90f);
            yield return new WaitForEndOfFrame();
            transition.cryAccentObservedBeforeStop = IsAccentVisible("Cry");
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            transition.cryAccentClearedAfterStop = IsAccentCleared();
            transition.accentStopPassed = transition.cryAccentObservedBeforeStop &&
                                          transition.cryAccentClearedAfterStop;

            // A chat segment must own the face after a preview: the Nod motion
            // Must preserve the returned Surprise expression and its accent.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Cry);
            yield return new WaitForSecondsRealtime(0.70f);
            yield return new WaitForEndOfFrame();
            var cryStarted = IsAccentVisible("Cry");
            performance.PlaySegments("qa-chat-profile", new List<DialogueClient.ChatSegment>
            {
                new DialogueClient.ChatSegment { expression = "surprise", gesture = "explain", gaze = "player" },
            });
            yield return new WaitForSecondsRealtime(0.60f);
            yield return new WaitForEndOfFrame();
            var chatSample = Capture(-1, "chat-profile", "chat-after-preview", 0f);
            report.samples.Add(chatSample);
            transition.chatProfilePreserved = cryStarted && !performance.IsPreviewPresentationActive &&
                                               performance.CurrentMotion == ZMotionPreset.Nod &&
                                               chatSample.mouthVariant > 0.5f && chatSample.eyeSmile < 0.1f &&
                                               Mathf.Abs(chatSample.mouthForm) <= 0.15f && IsAccentVisible("Surprise");
            performance.CancelCurrentPerformance();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            transition.chatProfilePreserved &= performance.CurrentMotion == ZMotionPreset.Idle &&
                                                performance.CurrentExpression == ZFaceExpression.Neutral &&
                                                performance.CurrentGaze == ZGaze.Player && IsAccentCleared();

            // Wink -> Sad -> ordinary Nod: no one-eye close state may leak.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Wink);
            yield return WaitForWink("wink-to-sad");
            performance.PreviewMotion(ZMotionPreset.Sad);
            yield return new WaitForSecondsRealtime(TransitionSeconds);
            yield return new WaitForEndOfFrame();
            var sadAfter = Capture(-1, "Sad", "sad-after-wink", 0f);
            report.samples.Add(sadAfter);
            transition.winkToSadDifference = Mathf.Abs(sadAfter.eyeLOpen - sadAfter.eyeROpen);
            transition.winkToSadPassed = lastWinkTransitionSample != null &&
                                          performance.CurrentMotion == ZMotionPreset.Sad &&
                                          transition.winkToSadDifference <= WinkResidualTolerance;
            performance.PreviewMotion(ZMotionPreset.Nod);
            yield return new WaitForSecondsRealtime(TransitionSeconds);
            yield return new WaitForEndOfFrame();
            var nodAfter = Capture(-1, "Nod", "nod-after-sad", 0f);
            report.samples.Add(nodAfter);
            transition.winkToNodDifference = Mathf.Abs(nodAfter.eyeLOpen - nodAfter.eyeROpen);
            transition.nodStateObserved = performance.CurrentMotion == ZMotionPreset.Nod;
            yield return new WaitForSecondsRealtime(ZPerformanceController.GetPreviewDuration(ZMotionPreset.Nod) + RecoverySeconds);
            yield return new WaitForEndOfFrame();
            var nodFinal = Capture(-1, "Nod", "nod-final", 0f);
            report.samples.Add(nodFinal);
            transition.nodFinalEyesRecovered = Recovered(nodFinal, EyeIds);
            transition.winkToSadToNodPassed = transition.winkToSadPassed && transition.nodStateObserved &&
                                                transition.winkToNodDifference <= WinkResidualTolerance &&
                                                transition.nodFinalEyesRecovered;

            // Same-action restart: trigger serial increments and MotionElapsed
            // returns to zero before progressing on the next real frame.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Surprise);
            yield return new WaitForSecondsRealtime(0.35f);
            yield return new WaitForEndOfFrame();
            transition.restartSerialBefore = performance.MotionTriggerSerial;
            transition.restartElapsedBefore = performance.MotionElapsed;
            performance.PreviewMotion(ZMotionPreset.Surprise);
            transition.restartSerialAfter = performance.MotionTriggerSerial;
            transition.restartElapsedImmediatelyAfter = performance.MotionElapsed;
            yield return new WaitForEndOfFrame();
            transition.restartElapsedAfterFrame = performance.MotionElapsed;
            transition.sameMotionRestartPassed = transition.restartSerialAfter > transition.restartSerialBefore &&
                                                  transition.restartElapsedImmediatelyAfter <= RestartElapsedTolerance &&
                                                  transition.restartElapsedAfterFrame > transition.restartElapsedImmediatelyAfter &&
                                                  performance.CurrentMotion == ZMotionPreset.Surprise;
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            transition.passed = transition.manualStopPassed && transition.winkToSadToNodPassed &&
                                transition.sameMotionRestartPassed && transition.accentStopPassed &&
                                transition.chatProfilePreserved;
        }

        private IEnumerator RunNewAccentTransitions()
        {
            var transition = report.transitions;
            var shouldRun = !focusedMode;
            if (!shouldRun)
            {
                for (var i = 0; i < runSpecs.Count; i++)
                {
                    if (runSpecs[i].motion == ZMotionPreset.Startle || runSpecs[i].motion == ZMotionPreset.Gloom)
                    {
                        shouldRun = true;
                        break;
                    }
                }
            }

            if (!shouldRun)
            {
                transition.newAccentTransitionSkipped = true;
                transition.newAccentTransitionSkipReason = "focused_emotion_only_without_Startle_or_Gloom";
                yield break;
            }

            transition.newAccentTransitionChecked = true;
            if (performance == null || accents == null)
            {
                transition.newAccentTransitionFailureReason = "performance_or_accents_missing";
                transition.newAccentTransitionPassed = false;
                transition.passed = transition.skipped ? false : transition.passed && false;
                yield break;
            }

            // Startle -> Gloom: the yellow impact accent must be replaced by
            // the face-attached darkening effect, not linger behind it.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Startle);
            var startleDeadline = Time.unscaledTime +
                                  Mathf.Min(1.20f, ZPerformanceController.GetPreviewDuration(ZMotionPreset.Startle));
            while (Time.unscaledTime < startleDeadline && !IsAccentVisible("Startle"))
            {
                yield return new WaitForEndOfFrame();
            }
            transition.startleAccentObservedBeforeInterrupt = IsAccentVisible("Startle");
            transition.startleToGloomSample = Capture(-1, "Startle", "startle-before-gloom", 0f);
            report.samples.Add(transition.startleToGloomSample);

            performance.PreviewMotion(ZMotionPreset.Gloom);
            yield return new WaitForSecondsRealtime(NewAccentTransitionWait);
            yield return new WaitForEndOfFrame();
            transition.gloomAccentObservedAfterStartle = IsAccentVisible("Gloom");
            transition.startleClearedOnGloomInterrupt = !IsAccentVisible("Startle");
            transition.startleToGloomPassed = transition.startleAccentObservedBeforeInterrupt &&
                                               transition.gloomAccentObservedAfterStartle &&
                                               transition.startleClearedOnGloomInterrupt &&
                                               performance.CurrentMotion == ZMotionPreset.Gloom;
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            transition.newAccentStopAfterGloom = Capture(-1, "Gloom", "gloom-stop-after", 0f);
            report.samples.Add(transition.newAccentStopAfterGloom);
            transition.gloomClearedAfterStop = IsAccentCleared();
            transition.gloomFaceRecoveredAfterStop = performance.CurrentMotion == ZMotionPreset.Idle &&
                                                      performance.CurrentExpression == ZFaceExpression.Neutral &&
                                                      performance.CurrentGaze == ZGaze.Player &&
                                                      Recovered(transition.newAccentStopAfterGloom, FaceIds) &&
                                                      Recovered(transition.newAccentStopAfterGloom, MouthIds) &&
                                                      Recovered(transition.newAccentStopAfterGloom, EyeIds) &&
                                                      Recovered(transition.newAccentStopAfterGloom, GazeIds);

            // Gloom -> Startle: exercise the reverse replacement as well,
            // because the two overlays use different geometry/material paths.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Gloom);
            var gloomDeadline = Time.unscaledTime +
                                Mathf.Min(1.20f, ZPerformanceController.GetPreviewDuration(ZMotionPreset.Gloom));
            while (Time.unscaledTime < gloomDeadline && !IsAccentVisible("Gloom"))
            {
                yield return new WaitForEndOfFrame();
            }
            transition.gloomAccentObservedBeforeInterrupt = IsAccentVisible("Gloom");
            transition.gloomToStartleSample = Capture(-1, "Gloom", "gloom-before-startle", 0f);
            report.samples.Add(transition.gloomToStartleSample);

            performance.PreviewMotion(ZMotionPreset.Startle);
            yield return new WaitForSecondsRealtime(NewAccentTransitionWait);
            yield return new WaitForEndOfFrame();
            transition.startleAccentObservedAfterGloom = IsAccentVisible("Startle");
            transition.gloomClearedOnStartleInterrupt = !IsAccentVisible("Gloom");
            transition.gloomToStartlePassed = transition.gloomAccentObservedBeforeInterrupt &&
                                               transition.startleAccentObservedAfterGloom &&
                                               transition.gloomClearedOnStartleInterrupt &&
                                               performance.CurrentMotion == ZMotionPreset.Startle;
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            transition.newAccentStopAfterStartle = Capture(-1, "Startle", "startle-stop-after", 0f);
            report.samples.Add(transition.newAccentStopAfterStartle);
            transition.startleClearedAfterStop = IsAccentCleared();
            transition.startleFaceRecoveredAfterStop = performance.CurrentMotion == ZMotionPreset.Idle &&
                                                        performance.CurrentExpression == ZFaceExpression.Neutral &&
                                                        performance.CurrentGaze == ZGaze.Player &&
                                                        Recovered(transition.newAccentStopAfterStartle, FaceIds) &&
                                                        Recovered(transition.newAccentStopAfterStartle, MouthIds) &&
                                                        Recovered(transition.newAccentStopAfterStartle, EyeIds) &&
                                                        Recovered(transition.newAccentStopAfterStartle, GazeIds);

            transition.newAccentTransitionPassed = transition.startleToGloomPassed &&
                                                   transition.gloomToStartlePassed &&
                                                   transition.gloomClearedAfterStop &&
                                                   transition.startleClearedAfterStop &&
                                                   transition.gloomFaceRecoveredAfterStop &&
                                                   transition.startleFaceRecoveredAfterStop;
            transition.passed = transition.skipped
                ? transition.newAccentTransitionPassed
                : transition.passed && transition.newAccentTransitionPassed;
        }

        private IEnumerator RunSighTransitions()
        {
            var transition = report.transitions;
            var shouldRun = !focusedMode;
            if (!shouldRun)
            {
                for (var i = 0; i < runSpecs.Count; i++)
                {
                    if (runSpecs[i].motion == ZMotionPreset.Sigh)
                    {
                        shouldRun = true;
                        break;
                    }
                }
            }

            if (!shouldRun)
            {
                transition.sighTransitionSkipped = true;
                transition.sighTransitionSkipReason = "focused_emotion_only_without_Sigh";
                yield break;
            }

            transition.sighTransitionChecked = true;
            ResolveSighFaceArt();
            if (performance == null || sighFaceArt == null)
            {
                transition.sighTransitionSkipReason = "performance_or_sigh_face_art_missing";
                transition.sighTransitionPassed = false;
                transition.passed = transition.skipped ? false : transition.passed && false;
                yield break;
            }

            if (transition.sighScreenshots == null)
                transition.sighScreenshots = new List<SighTransitionScreenshotReport>();

            // Enter the Sigh middle, then restart the same action. The
            // repeated preview must restart its clock without duplicating the
            // transparent face art instance.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Sigh);
            yield return WaitForSighFaceArtVisible(1.25f);
            yield return new WaitForSecondsRealtime(0.62f);
            yield return new WaitForEndOfFrame();
            var sighMiddle = Capture(-1, "Sigh", "sigh-middle", 0f);
            report.samples.Add(sighMiddle);
            transition.sighMiddleActiveObserved = IsSighEyesAndLayerVisible(sighMiddle) &&
                                                    sighFaceArt.IsSighReady;
            CaptureSighTransitionScreenshot("sigh-middle");

            transition.sighRepeatSerialBefore = performance.MotionTriggerSerial;
            performance.PreviewMotion(ZMotionPreset.Sigh);
            transition.sighRepeatSerialAfter = performance.MotionTriggerSerial;
            // Record the immediate restart time before the art layer reaches
            // its visible mouth weight. Visibility is asserted only after a
            // settled middle sample, not on the reset frame.
            transition.sighRepeatElapsedImmediatelyAfter = performance.MotionElapsed;
            yield return new WaitForSecondsRealtime(0.30f);
            yield return new WaitForEndOfFrame();
            transition.sighRepeatElapsedAfterFrame = performance.MotionElapsed;
            var sighRepeat = Capture(-1, "Sigh", "sigh-repeat", 0f);
            report.samples.Add(sighRepeat);
            transition.sighRepeatAllInstances = IsSighFaceArtVisible() &&
                                                sighFaceArt.SighVisibleInstanceCount == SighRequiredInstanceCount;
            transition.sighRepeatPassed = transition.sighRepeatSerialAfter > transition.sighRepeatSerialBefore &&
                                           transition.sighRepeatElapsedImmediatelyAfter <= SighRepeatElapsedTolerance &&
                                           transition.sighRepeatElapsedAfterFrame > transition.sighRepeatElapsedImmediatelyAfter &&
                                           transition.sighRepeatAllInstances;
            CaptureSighTransitionScreenshot("sigh-repeat");
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();

            // Sigh -> Surprise must remove the real face art before the
            // existing expression/effect takes ownership of the face.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Sigh);
            yield return WaitForSighFaceArtVisible(1.25f);
            yield return new WaitForSecondsRealtime(0.35f);
            performance.PreviewMotion(ZMotionPreset.Surprise);
            yield return new WaitForSecondsRealtime(SighTransitionWait);
            yield return new WaitForEndOfFrame();
            transition.sighToSurpriseSample = Capture(-1, "Surprise", "sigh-to-surprise", 0f);
            report.samples.Add(transition.sighToSurpriseSample);
            transition.sighToSurpriseLayerCleared = IsSighFaceArtCleared();
            transition.sighToSurpriseStatePreserved = performance.CurrentMotion == ZMotionPreset.Surprise &&
                                                       performance.CurrentExpression == ZFaceExpression.Surprise &&
                                                       transition.sighToSurpriseSample.mouthVariant > 0.5f &&
                                                       Mathf.Min(transition.sighToSurpriseSample.eyeLOpen,
                                                           transition.sighToSurpriseSample.eyeROpen) > 0.80f &&
                                                       IsAccentVisible("Surprise");
            transition.sighToSurprisePassed = transition.sighToSurpriseLayerCleared &&
                                               transition.sighToSurpriseStatePreserved;
            CaptureSighTransitionScreenshot("sigh-to-surprise");
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();

            // Sigh -> Cry must likewise clear the art without changing Cry's
            // established closed-eye, wide-mouth, and tear behavior.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Sigh);
            yield return WaitForSighFaceArtVisible(1.25f);
            yield return new WaitForSecondsRealtime(0.35f);
            performance.PreviewMotion(ZMotionPreset.Cry);
            yield return new WaitForSecondsRealtime(SighTransitionWait);
            yield return new WaitForEndOfFrame();
            transition.sighToCrySample = Capture(-1, "Cry", "sigh-to-cry", 0f);
            report.samples.Add(transition.sighToCrySample);
            transition.sighToCryLayerCleared = IsSighFaceArtCleared();
            transition.sighToCryStatePreserved = performance.CurrentMotion == ZMotionPreset.Cry &&
                                                  transition.sighToCrySample.mouthVariant > 1.5f &&
                                                  Mathf.Max(transition.sighToCrySample.eyeLOpen,
                                                      transition.sighToCrySample.eyeROpen) <= 0.12f &&
                                                  IsAccentVisible("Cry");
            transition.sighToCryPassed = transition.sighToCryLayerCleared &&
                                         transition.sighToCryStatePreserved;
            CaptureSighTransitionScreenshot("sigh-to-cry");
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();

            // Stop from the middle and verify both visual cleanup and the
            // ordinary Cubism face state. IsSighReady intentionally remains a
            // resource/mesh readiness signal and is not expected to turn off.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Sigh);
            yield return WaitForSighFaceArtVisible(1.25f);
            yield return new WaitForSecondsRealtime(0.35f);
            performance.StopMotionPreview();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            transition.sighStopSample = Capture(-1, "Sigh", "sigh-stop-after", 0f);
            report.samples.Add(transition.sighStopSample);
            transition.sighStopLayerCleared = IsSighFaceArtCleared();
            transition.sighStopFaceRecovered = IsNeutralAfterSighStop(transition.sighStopSample);
            transition.sighStopPassed = transition.sighStopLayerCleared &&
                                         transition.sighStopFaceRecovered;
            CaptureSighTransitionScreenshot("sigh-stop-after");

            // Natural completion must restore materials even without an
            // explicit Stop call.
            yield return PrepareNeutral();
            performance.PreviewMotion(ZMotionPreset.Sigh);
            yield return new WaitForSecondsRealtime(ZPerformanceController.GetPreviewDuration(ZMotionPreset.Sigh) +
                                                    RecoverySeconds);
            yield return new WaitForEndOfFrame();
            transition.sighNaturalEndSample = Capture(-1, "Sigh", "sigh-natural-end", 0f);
            report.samples.Add(transition.sighNaturalEndSample);
            transition.sighNaturalEndLayerCleared = IsSighFaceArtCleared();
            transition.sighNaturalEndMaterialsRestored = sighFaceArt.MaterialsRestored;
            transition.sighNaturalEndPassed = transition.sighNaturalEndLayerCleared &&
                                               transition.sighNaturalEndMaterialsRestored &&
                                               IsNeutralAfterSighStop(transition.sighNaturalEndSample);
            CaptureSighTransitionScreenshot("sigh-natural-end");

            transition.sighTransitionPassed = transition.sighMiddleActiveObserved &&
                                              transition.sighRepeatPassed &&
                                              transition.sighToSurprisePassed &&
                                              transition.sighToCryPassed &&
                                              transition.sighStopPassed &&
                                              transition.sighNaturalEndPassed;
            transition.passed = transition.skipped
                ? transition.sighTransitionPassed
                : transition.passed && transition.sighTransitionPassed;
        }

        private IEnumerator WaitForSighFaceArtVisible(float timeoutSeconds)
        {
            ResolveSighFaceArt();
            var deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForEndOfFrame();
                if (IsSighFaceArtVisible())
                    yield break;
            }
        }

        private bool IsNeutralAfterSighStop(Sample sample)
        {
            return performance != null && performance.CurrentMotion == ZMotionPreset.Idle &&
                   performance.CurrentExpression == ZFaceExpression.Neutral &&
                   performance.CurrentGaze == ZGaze.Player && Recovered(sample, FaceIds) &&
                   Recovered(sample, MouthIds) && Recovered(sample, EyeIds) && Recovered(sample, GazeIds);
        }

        private void CaptureSighTransitionScreenshot(string stage)
        {
            if (string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(stage))
                return;
            if (report.transitions.sighScreenshots == null)
                report.transitions.sighScreenshots = new List<SighTransitionScreenshotReport>();
            var file = "sigh-transition-" + stage + ".png";
            ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, file));
            report.transitions.sighScreenshots.Add(new SighTransitionScreenshotReport
            {
                stage = stage,
                file = file,
                requested = true,
            });
        }

        private IEnumerator WaitForWink(string phase)
        {
            lastWinkTransitionSample = null;
            var deadline = Time.unscaledTime + ZPerformanceController.GetPreviewDuration(ZMotionPreset.Wink);
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForEndOfFrame();
                var sample = Capture(-1, ZMotionPreset.Wink.ToString(), phase, 0f);
                report.samples.Add(sample);
                if (Mathf.Abs(sample.eyeLOpen - sample.eyeROpen) > WinkDifferenceThreshold &&
                    Mathf.Max(sample.eyeLOpen, sample.eyeROpen) > WinkOpenEyeThreshold)
                {
                    lastWinkTransitionSample = sample;
                    yield break;
                }
            }
        }

        private IEnumerator RunUiButtonChecks()
        {
            for (var i = 0; i < runSpecs.Count; i++)
            {
                var spec = runSpecs[i];
                var result = new UiButtonReport
                {
                    preset = spec.motion.ToString(),
                    aliases = new List<string>(spec.buttonNames),
                    searched = true,
                };
                var button = FindButton(spec.buttonNames);
                if (button == null)
                {
                    result.failureReason = "button_not_found";
                    report.uiButtons.Add(result);
                    report.errors.Add("ui_button_not_found_" + spec.motion);
                    continue;
                }
                result.buttonFound = true;
                result.buttonObject = button.gameObject.name;
                result.buttonLabel = ButtonLabel(button);
                result.triggerSerialBefore = performance.MotionTriggerSerial;
                try
                {
                    button.onClick.Invoke();
                    result.invoked = true;
                }
                catch (Exception exception)
                {
                    result.failureReason = "invoke_exception_" + exception.GetType().Name;
                }
                yield return new WaitForEndOfFrame();
                result.triggerSerialAfter = performance.MotionTriggerSerial;
                result.observedPreset = performance.CurrentMotion.ToString();
                result.controllerTriggered = result.triggerSerialAfter > result.triggerSerialBefore &&
                                             performance.CurrentMotion == spec.motion;
                result.passed = result.invoked && result.controllerTriggered;
                if (!result.passed && string.IsNullOrEmpty(result.failureReason))
                    result.failureReason = "button_click_did_not_trigger_expected_preview";
                report.uiButtons.Add(result);
                performance.StopMotionPreview();
                yield return new WaitForSecondsRealtime(0.18f);
            }
        }

        private void EvaluateLookDirectionPair()
        {
            report.lookDirectionPairChecked = !focusedMode;
            if (!report.lookDirectionPairChecked)
            {
                report.lookDirectionPairPassed = true;
                return;
            }

            ActionReport left = null;
            ActionReport right = null;
            for (var i = 0; i < report.actions.Count; i++)
            {
                var action = report.actions[i];
                if (action.preset == ZMotionPreset.LookLeft.ToString())
                    left = action;
                else if (action.preset == ZMotionPreset.LookRight.ToString())
                    right = action;
            }

            report.lookDirectionPairPassed = left != null && right != null &&
                                             left.gazeDirectionObserved && right.gazeDirectionObserved &&
                                             left.eyeBallXPeakDelta * right.eyeBallXPeakDelta < 0f;
        }

        private static Button FindButton(string[] aliases)
        {
            var buttons = Resources.FindObjectsOfTypeAll<Button>();

            // Resolve stable object names before labels. This prevents a short
            // Chinese label such as "哭" from stealing the Cry action from
            // the exact MotionCry button.
            var exactObject = FindExactButton(buttons, aliases, true);
            if (exactObject != null)
                return exactObject;
            var exactLabel = FindExactButton(buttons, aliases, false);
            if (exactLabel != null)
                return exactLabel;

            // Fuzzy matching remains a compatibility fallback for legacy
            // scenes whose object names differ from the runtime aliases.
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null || !button.gameObject.scene.IsValid() || !button.gameObject.activeInHierarchy)
                    continue;
                var name = button.gameObject.name;
                var label = ButtonLabel(button);
                for (var j = 0; j < aliases.Length; j++)
                {
                    if (NameMatches(name, aliases[j]) || NameMatches(label, aliases[j]))
                        return button;
                }
            }
            return null;
        }

        private static Button FindExactButton(Button[] buttons, string[] aliases, bool objectName)
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null || !button.gameObject.scene.IsValid() || !button.gameObject.activeInHierarchy)
                    continue;
                var value = objectName ? button.gameObject.name : ButtonLabel(button);
                for (var j = 0; j < aliases.Length; j++)
                {
                    if (string.Equals(Normalize(value), Normalize(aliases[j]), StringComparison.Ordinal))
                        return button;
                }
            }
            return null;
        }

        private static string ButtonLabel(Button button)
        {
            var text = button == null ? null : button.GetComponentInChildren<Text>(true);
            return text == null ? string.Empty : text.text ?? string.Empty;
        }

        private static bool NameMatches(string value, string alias)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(alias))
                return false;
            var normalizedValue = Normalize(value);
            var normalizedAlias = Normalize(alias);
            return normalizedValue == normalizedAlias || normalizedValue.Contains(normalizedAlias) ||
                   normalizedAlias.Contains(normalizedValue);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty)
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private void FinalizeReport()
        {
            if (originalMultipliersCaptured && performance != null)
            {
                performance.SetMotionAmplitudeMultiplier(originalAmplitude);
                performance.SetMotionSpeedMultiplier(originalSpeed);
            }
            report.finishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.parameterRanges = new List<RangeResult>(ranges.Values);
            report.model.parametersInRange = AllRangesInRange();
            report.neutralScreenshotPresent = !string.IsNullOrEmpty(outputDirectory) &&
                                              report.neutralScreenshotRequested &&
                                              File.Exists(Path.Combine(outputDirectory, report.neutralScreenshotFile));
            report.neutralFaceScreenshotPresent = !string.IsNullOrEmpty(outputDirectory) &&
                                                  report.neutralFaceScreenshotRequested &&
                                                  File.Exists(Path.Combine(outputDirectory, report.neutralFaceScreenshotFile));

            var emotionPassed = 0;
            var screenshotPresent = 0;
            var activeScreenshotPresent = 0;
            var faceScreenshotPresent = 0;
            for (var i = 0; i < report.actions.Count; i++)
            {
                var action = report.actions[i];
                action.screenshotPresent = !string.IsNullOrEmpty(outputDirectory) &&
                                           File.Exists(Path.Combine(outputDirectory, action.screenshotFile));
                action.faceScreenshotPresent = !string.IsNullOrEmpty(outputDirectory) &&
                                               action.faceScreenshotRequested &&
                                               File.Exists(Path.Combine(outputDirectory, action.faceScreenshotFile));
                if (action.screenshotPresent)
                    screenshotPresent++;
                if (action.screenshotPresent && action.captureDuringActiveMotion)
                    activeScreenshotPresent++;
                if (action.faceScreenshotPresent)
                    faceScreenshotPresent++;
                if (action.stageScreenshots != null)
                {
                    for (var stageIndex = 0; stageIndex < action.stageScreenshots.Count; stageIndex++)
                    {
                        var stage = action.stageScreenshots[stageIndex];
                        stage.present = !string.IsNullOrEmpty(outputDirectory) &&
                                         stage.requested &&
                                         File.Exists(Path.Combine(outputDirectory, stage.file));
                        if (!stage.present)
                            report.errors.Add("stage_screenshot_file_missing_" + Normalize(stage.stage));
                    }
                }
                action.passed = action.relatedParametersActivated &&
                                !action.invalidatedByExternalPreview &&
                                (!action.headMotionRequired || action.headMoved) &&
                                (!action.bodyMotionRequired || action.bodyMoved) &&
                                action.naturalCompletion && action.faceRecovered && action.mouthRecovered &&
                                action.mouthOpenRecovered &&
                                action.gazeRecovered && action.eyesRecovered && action.expressionNeutral &&
                                action.gazePlayer && action.rangePassed && action.winkPassed &&
                                action.expectedFacePassed && (!action.accentRequired || action.accentActivated) &&
                                action.accentCleared && (!action.outerTearPlacementRequired || action.outerTearPlacementObserved) &&
                                action.captureDuringActiveMotion &&
                                action.screenshotPresent && action.faceScreenshotPresent;
                if (action.passed)
                    emotionPassed++;
                if (!action.screenshotPresent)
                    action.failureReasons.Add("screenshot_file_missing");
                if (!action.faceScreenshotPresent)
                    action.failureReasons.Add("face_screenshot_file_missing");
                if (!action.captureDuringActiveMotion)
                    action.failureReasons.Add("screenshot_not_captured_during_active_motion");
            }

            var sighTransitionScreenshotsPassed = true;
            if (report.transitions.sighScreenshots != null)
            {
                for (var i = 0; i < report.transitions.sighScreenshots.Count; i++)
                {
                    var screenshot = report.transitions.sighScreenshots[i];
                    screenshot.present = !string.IsNullOrEmpty(outputDirectory) &&
                                         screenshot.requested &&
                                         File.Exists(Path.Combine(outputDirectory, screenshot.file));
                    if (!screenshot.present)
                    {
                        sighTransitionScreenshotsPassed = false;
                        report.errors.Add("sigh_transition_screenshot_file_missing_" +
                                          Normalize(screenshot.stage));
                    }
                }
            }
            report.transitions.sighTransitionScreenshotsPassed = sighTransitionScreenshotsPassed;
            if (report.transitions.sighTransitionChecked)
            {
                report.transitions.sighTransitionPassed &= sighTransitionScreenshotsPassed;
                report.transitions.passed = report.transitions.skipped
                    ? report.transitions.sighTransitionPassed
                    : report.transitions.passed && sighTransitionScreenshotsPassed;
            }

            EvaluateLookDirectionPair();

            var expectedActions = runSpecs.Count;
            var uiPassed = expectedActions > 0 && report.uiButtons.Count == expectedActions;
            var uiCount = 0;
            for (var i = 0; i < report.uiButtons.Count; i++)
            {
                if (report.uiButtons[i].passed)
                    uiCount++;
                else
                    uiPassed = false;
            }
            var stageScreenshotRequestedCount = CountStageScreenshots(true);
            var stageScreenshotPresentCount = CountStageScreenshots(false);
            var stageScreenshotsPassed = stageScreenshotPresentCount == stageScreenshotRequestedCount;
            report.summary = new SummaryReport
            {
                actions = expectedActions,
                actionsPassed = emotionPassed,
                screenshotsRequested = report.actions.Count,
                screenshotsPresent = screenshotPresent,
                screenshotsCapturedDuringActiveMotion = activeScreenshotPresent,
                faceScreenshotsRequested = report.actions.Count,
                faceScreenshotsPresent = faceScreenshotPresent,
                stageScreenshotsRequested = stageScreenshotRequestedCount,
                stageScreenshotsPresent = stageScreenshotPresentCount,
                stageScreenshotsPassed = stageScreenshotsPassed,
                uiButtonsFoundAndTriggered = uiCount,
                parameterSamples = report.samples.Count,
                actionsPassedAll = expectedActions > 0 && emotionPassed == expectedActions,
                screenshotsPassed = expectedActions > 0 && activeScreenshotPresent == expectedActions,
                faceScreenshotsPassed = expectedActions > 0 && activeScreenshotPresent == expectedActions &&
                                        faceScreenshotPresent == expectedActions,
                neutralScreenshotRequested = report.neutralScreenshotRequested,
                neutralScreenshotPresent = report.neutralScreenshotPresent,
                neutralFaceScreenshotRequested = report.neutralFaceScreenshotRequested,
                neutralFaceScreenshotPresent = report.neutralFaceScreenshotPresent,
                uiBindingPassed = uiPassed,
                transitionsPassed = report.transitions.passed,
                transitionsSkipped = report.transitions.skipped,
                lookDirectionPairPassed = report.lookDirectionPairPassed,
                partial = focusedMode,
                partialReason = report.partialReason,
                functionalPassed = !focusedMode && report.model.ready && report.model.parametersReady &&
                                   expectedActions > 0 && emotionPassed == expectedActions &&
                                   activeScreenshotPresent == expectedActions && faceScreenshotPresent == expectedActions &&
                                   stageScreenshotsPassed &&
                                   report.model.parametersInRange && report.transitions.passed &&
                                   report.lookDirectionPairPassed,
            };
            report.summary.passed = !focusedMode && report.summary.functionalPassed && report.summary.uiBindingPassed;
            report.uiBindingPassed = uiPassed;
            WriteReport();
        }

        private void PrepareOutputDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var requested = string.IsNullOrWhiteSpace(requestedOutputDirectory)
                ? Environment.GetEnvironmentVariable("Z_EMOTION_QA_OUTPUT")
                : requestedOutputDirectory;
            var root = string.IsNullOrWhiteSpace(requested)
                ? Path.Combine(projectRoot, "verification", "emotion-presets-20261103")
                : Path.GetFullPath(requested);
            runId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            outputDirectory = Path.Combine(root, runId);
            Directory.CreateDirectory(outputDirectory);
        }

        private void WriteReport()
        {
            if (string.IsNullOrEmpty(outputDirectory))
                return;
            var path = Path.Combine(outputDirectory, "emotion-preview-qa.json");
            try
            {
                var json = JsonUtility.ToJson(report, true);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                Debug.Log("[ZEmotionPreviewQa] UTF-8 报告已写入：" + path);
            }
            catch (Exception exception)
            {
                Debug.LogError("[ZEmotionPreviewQa] 无法写入报告：" + exception.GetType().Name);
            }
        }

        private IEnumerator QuitNextFrame()
        {
            yield return null;
            Application.Quit();
        }

        private static float Value(Sample sample, string id)
        {
            if (sample == null)
                return 0f;
            switch (id)
            {
                case "ParamAngleX": return sample.angleX;
                case "ParamAngleY": return sample.angleY;
                case "ParamAngleZ": return sample.angleZ;
                case "ParamBodyAngleX": return sample.bodyAngleX;
                case "ParamBodyAngleY": return sample.bodyAngleY;
                case "ParamBodyAngleZ": return sample.bodyAngleZ;
                case "ParamEyeLOpen": return sample.eyeLOpen;
                case "ParamEyeROpen": return sample.eyeROpen;
                case "ParamEyeSmile": return sample.eyeSmile;
                case "ParamBrowForm": return sample.browForm;
                case "ParamMouthVariant": return sample.mouthVariant;
                case "ParamMouthForm": return sample.mouthForm;
                case "ParamMouthOpenY": return sample.mouthOpenY;
                case "ParamCheek": return sample.cheek;
                case "ParamEyeBallX": return sample.eyeBallX;
                case "ParamEyeBallY": return sample.eyeBallY;
                default: return 0f;
            }
        }

        [Serializable]
        private sealed class EmotionReport
        {
            public string schemaVersion;
            public string startedUtc;
            public string finishedUtc;
            public string outputDirectory;
            public string runId;
            public bool commandLineOptIn;
            public bool inputIsolationRequested;
            public bool inputIsolationActive;
            public bool inputIsolationApplied;
            public bool inputIsolationRestored;
            public int inputIsolationEventSystemCount;
            public int inputIsolationDisabledEventSystemCount;
            public bool focusedMode;
            public string runScope;
            public string requestedEmotionOnly;
            public bool partial;
            public string partialReason;
            public List<string> selectedEmotions;
            public bool uiBindingPassed;
            public float measurementAmplitudeMultiplier;
            public float measurementSpeedMultiplier;
            public string runtimeException;
            public ThresholdReport thresholds;
            public ModelReport model;
            public List<ActionReport> actions;
            public List<Sample> samples;
            public List<RangeResult> parameterRanges;
            public TransitionReport transitions;
            public List<UiButtonReport> uiButtons;
            public SummaryReport summary;
            public List<string> errors;
            public bool lookDirectionPairChecked;
            public bool lookDirectionPairPassed;
            public bool neutralScreenshotRequested;
            public bool neutralScreenshotPresent;
            public string neutralScreenshotFile;
            public bool neutralFaceScreenshotRequested;
            public bool neutralFaceScreenshotPresent;
            public string neutralFaceScreenshotFile;
            public int neutralFaceScreenshotWidth;
            public int neutralFaceScreenshotHeight;
            public int neutralFaceBoundsX;
            public int neutralFaceBoundsY;
        }

        [Serializable]
        private sealed class ThresholdReport
        {
            public float sampleRateHz;
            public float rangeTolerance;
            public float headMotionNormalized;
            public float bodyMotionNormalized;
            public float expressionActivationNormalized;
            public float eyeActivationNormalized;
            public float neutralToleranceNormalized;
            public float winkDifference;
            public float winkOpenEye;
            public float winkResidualDifference;
            public float screenshotActivation;
            public float recoverySeconds;
            public float restartElapsedTolerance;
            public float startleImpactWindowStartSeconds;
            public float startleImpactWindowEndSeconds;
            public float startleImpactThresholdNormalized;
            public float startleScreenshotLatestSeconds;
            public float startleOMouthMinimum;
            public float startleOMouthMaximum;
            public float tiltZThresholdNormalized;
            public float discoverLookRightThresholdNormalized;
            public float discoverPlayerToleranceNormalized;
            public float discoverLookRightScreenshotEarliestSeconds;
            public float discoverLookRightScreenshotLatestSeconds;
            public float discoverReturnScreenshotEarliestSeconds;
            public float sighEyeClosedMaximum;
            public float sighScreenshotEarliestSeconds;
            public float accentResidualTolerance;
            public float sighArtActivationThreshold;
            public int sighArtRequiredInstanceCount;
            public float sighMiddleScreenshotEarliestSeconds;
            public float sighMiddleScreenshotLatestSeconds;
            public float sighRepeatElapsedTolerance;
            public float sighTransitionWaitSeconds;
        }

        [Serializable]
        private sealed class ModelReport
        {
            public bool controllerFound;
            public string controllerObject;
            public bool cubismModelFound;
            public string cubismModelObject;
            public int parameterCount;
            public bool updateControllerFound;
            public bool accentsFound;
            public bool sighFaceArtFound;
            public bool ready;
            public int requiredParameterCount;
            public int availableRequiredParameterCount;
            public bool parametersReady;
            public bool parametersInRange;
            public List<string> missingRequiredParameterIds = new List<string>();
        }

        private enum EmotionExpectation
        {
            None,
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

        [Serializable]
        private sealed class EmotionSpec
        {
            public readonly ZMotionPreset motion;
            public readonly float screenshotEarliestSeconds;
            public readonly string[] expressionIds;
            public readonly string[] gazeIds;
            public readonly float eyeActivationThreshold;
            public readonly EmotionExpectation expectation;
            public readonly string accentName;
            public readonly string[] buttonNames;
            public readonly bool requireHeadMotion;
            public readonly bool requireBodyMotion;
            public readonly int expectedEyeBallXDirection;
            public readonly float screenshotActivationThreshold;

            public EmotionSpec(ZMotionPreset motion, float screenshotEarliestSeconds, string[] expressionIds,
                string[] gazeIds, float eyeActivationThreshold, EmotionExpectation expectation,
                string accentName, string[] buttonNames)
                : this(motion, screenshotEarliestSeconds, expressionIds, gazeIds, eyeActivationThreshold,
                    expectation, accentName, buttonNames, true, true, 0, ScreenshotThreshold)
            {
            }

            public EmotionSpec(ZMotionPreset motion, float screenshotEarliestSeconds, string[] expressionIds,
                string[] gazeIds, float eyeActivationThreshold, EmotionExpectation expectation,
                string accentName, string[] buttonNames, bool requireHeadMotion, bool requireBodyMotion,
                int expectedEyeBallXDirection = 0, float screenshotActivationThreshold = ScreenshotThreshold)
            {
                this.motion = motion;
                this.screenshotEarliestSeconds = screenshotEarliestSeconds;
                this.expressionIds = expressionIds;
                this.gazeIds = gazeIds;
                this.eyeActivationThreshold = eyeActivationThreshold;
                this.expectation = expectation;
                this.accentName = accentName;
                this.buttonNames = buttonNames;
                this.requireHeadMotion = requireHeadMotion;
                this.requireBodyMotion = requireBodyMotion;
                this.expectedEyeBallXDirection = expectedEyeBallXDirection;
                this.screenshotActivationThreshold = screenshotActivationThreshold;
            }

            public float ActivationThreshold(string id)
            {
                return IsEye(id) ? eyeActivationThreshold : ExpressionActivationThreshold;
            }
        }

        [Serializable]
        private sealed class ActionReport
        {
            public string preset;
            public int actionIndex;
            public float nominalDurationSeconds;
            public float screenshotEarliestSeconds;
            public float screenshotActivationThreshold;
            public int triggerSerial;
            public bool invalidatedByExternalPreview;
            public int invalidatingTriggerSerial;
            public float invalidatedAtElapsed;
            public string invalidatedMotion;
            public int sampleStart;
            public int sampleEnd;
            public int sampleCount;
            public float peakScore;
            public float headPeakNormalized;
            public string headPeakParameter;
            public float headPeakValue;
            public float bodyPeakNormalized;
            public string bodyPeakParameter;
            public float bodyPeakValue;
            public float gazePeakNormalized;
            public float eyeBallXPeakValue;
            public float eyeBallXPeakDelta;
            public bool gazeDirectionObserved;
            public bool bodyYObserved;
            public bool headMotionRequired = true;
            public bool bodyMotionRequired = true;
            public bool relatedParametersActivated;
            public bool headMoved;
            public bool bodyMoved;
            public bool naturalCompletion;
            public bool faceRecovered;
            public bool mouthRecovered;
            public bool mouthOpenRecovered;
            public bool gazeRecovered;
            public bool eyesRecovered;
            public bool expressionNeutral;
            public bool gazePlayer;
            public bool rangePassed;
            public bool winkAsymmetryObserved;
            public bool winkPassed;
            public float winkPeakDifference;
            public float winkMinimumEye = 1f;
            public float winkMaximumEye;
            public bool expectedFaceObserved;
            public bool expectedFacePassed;
            public bool specialExpectationPassed;
            public bool tiltZObserved;
            public float tiltZPeakNormalized;
            public bool emphasisHeadBodyObserved;
            public bool earlyImpactObserved;
            public float earlyPeakNormalized;
            public float earlyPeakElapsed;
            public string earlyPeakParameter;
            public float earlyPeakValue;
            public bool earlyAccentActivated;
            public float earlyAccentPeakIntensity;
            public bool startleOMouthObserved;
            public bool discoverLookRightObserved;
            public float discoverLookRightElapsed;
            public float discoverLookRightDelta;
            public float discoverPeakLookRightDelta;
            public bool discoverReturnedToPlayerObserved;
            public float discoverReturnToPlayerElapsed;
            public bool gloomColdFaceObserved;
            public bool sighEyesClosedObserved;
            public bool sighEyesAndLayerSameSampleObserved;
            public bool sighFaceArtFound;
            public bool sighFaceArtReadyObserved;
            public bool sighFaceArtVisibleObserved;
            public bool sighFaceArtAllInstancesObserved;
            public bool sighFaceArtSingleComponentObserved;
            public bool sighFaceArtClearedAfterAction;
            public float sighFaceArtPeakIntensity;
            public int sighFaceArtMaximumInstanceCount;
            public bool accentRequired;
            public bool accentActivated;
            public bool accentCleared;
            public bool accentAnchorsReadyObserved;
            public float accentPeakIntensity;
            public bool outerTearPlacementRequired;
            public bool outerTearPlacementObserved;
            public float finalEyeLOpen;
            public float finalEyeROpen;
            public float finalEyeBallX;
            public float finalEyeBallY;
            public float finalEyeSmile;
            public float finalBrowForm;
            public float finalMouthVariant;
            public float finalMouthForm;
            public float finalMouthOpenY;
            public float finalCheek;
            public bool screenshotRequested;
            public bool screenshotPresent;
            public bool captureDuringActiveMotion;
            public string screenshotFile;
            public string screenshotCaptureReason;
            public float screenshotCaptureTime;
            public bool faceScreenshotRequested;
            public bool faceScreenshotPresent;
            public string faceScreenshotFile;
            public string faceScreenshotCaptureReason;
            public float faceScreenshotCaptureTime;
            public int faceScreenshotWidth;
            public int faceScreenshotHeight;
            public int faceBoundsX;
            public int faceBoundsY;
            public List<StageScreenshotReport> stageScreenshots;
            public bool passed;
            public List<ValueReport> expressions;
            public List<ValueReport> gaze;
            public List<string> failureReasons;
        }

        [Serializable]
        private sealed class StageScreenshotReport
        {
            public string stage;
            public string file;
            public bool requested;
            public bool present;
            public bool captureDuringActiveMotion;
            public float elapsed;
            public float eyeBallX;
            public string currentGaze;
        }

        [Serializable]
        private sealed class ValueReport
        {
            public string id;
            public float baseline;
            public float minimumObserved;
            public float maximumObserved;
            public float peakValue;
            public float peakDeltaNormalized;
            public float activationThresholdNormalized;
            public bool available;
            public bool activated;
            public bool activationRequired = true;

            public bool IsActivated(Dictionary<string, ParameterInfo> metadata)
            {
                available = metadata.ContainsKey(id);
                return available && (!activationRequired || activated);
            }
        }

        [Serializable]
        private sealed class ParameterInfo
        {
            public string id;
            public float minimum;
            public float maximum;
            public float defaultValue;
        }

        [Serializable]
        private sealed class RangeResult
        {
            public string id;
            public float minimum;
            public float maximum;
            public float defaultValue;
            public float minimumObserved;
            public float maximumObserved;
            public float maxBelowRange;
            public float maxAboveRange;
            public int sampleCount;
            public int nonFiniteSamples;
            public bool inRange;
        }

        [Serializable]
        private sealed class TransitionReport
        {
            public bool skipped;
            public string skipReason;
            public bool manualStopWinkObserved;
            public float manualStopDifferenceBefore;
            public float manualStopDifferenceAfter;
            public float manualStopEyeLOpen;
            public float manualStopEyeROpen;
            public bool manualStopPassed;
            public bool cryAccentObservedBeforeStop;
            public bool cryAccentClearedAfterStop;
            public bool accentStopPassed;
            public bool chatProfilePreserved;
            public float winkToSadDifference;
            public bool winkToSadPassed;
            public float winkToNodDifference;
            public bool nodStateObserved;
            public bool nodFinalEyesRecovered;
            public bool winkToSadToNodPassed;
            public int restartSerialBefore;
            public int restartSerialAfter;
            public float restartElapsedBefore;
            public float restartElapsedImmediatelyAfter;
            public float restartElapsedAfterFrame;
            public bool sameMotionRestartPassed;
            public bool newAccentTransitionSkipped;
            public string newAccentTransitionSkipReason;
            public bool newAccentTransitionChecked;
            public string newAccentTransitionFailureReason;
            public bool startleAccentObservedBeforeInterrupt;
            public bool gloomAccentObservedAfterStartle;
            public bool startleClearedOnGloomInterrupt;
            public bool startleToGloomPassed;
            public bool gloomClearedAfterStop;
            public bool gloomFaceRecoveredAfterStop;
            public bool gloomAccentObservedBeforeInterrupt;
            public bool startleAccentObservedAfterGloom;
            public bool gloomClearedOnStartleInterrupt;
            public bool gloomToStartlePassed;
            public bool startleClearedAfterStop;
            public bool startleFaceRecoveredAfterStop;
            public bool newAccentTransitionPassed;
            public Sample startleToGloomSample;
            public Sample gloomToStartleSample;
            public Sample newAccentStopAfterGloom;
            public Sample newAccentStopAfterStartle;
            public bool sighTransitionSkipped;
            public string sighTransitionSkipReason;
            public bool sighTransitionChecked;
            public bool sighMiddleActiveObserved;
            public bool sighRepeatPassed;
            public int sighRepeatSerialBefore;
            public int sighRepeatSerialAfter;
            public float sighRepeatElapsedImmediatelyAfter;
            public float sighRepeatElapsedAfterFrame;
            public bool sighRepeatAllInstances;
            public bool sighToSurprisePassed;
            public bool sighToSurpriseLayerCleared;
            public bool sighToSurpriseStatePreserved;
            public Sample sighToSurpriseSample;
            public bool sighToCryPassed;
            public bool sighToCryLayerCleared;
            public bool sighToCryStatePreserved;
            public Sample sighToCrySample;
            public bool sighStopPassed;
            public bool sighStopLayerCleared;
            public bool sighStopFaceRecovered;
            public Sample sighStopSample;
            public bool sighNaturalEndPassed;
            public bool sighNaturalEndLayerCleared;
            public bool sighNaturalEndMaterialsRestored;
            public Sample sighNaturalEndSample;
            public bool sighTransitionScreenshotsPassed;
            public bool sighTransitionPassed;
            public List<SighTransitionScreenshotReport> sighScreenshots =
                new List<SighTransitionScreenshotReport>();
            public bool passed;
        }

        [Serializable]
        private sealed class SighTransitionScreenshotReport
        {
            public string stage;
            public string file;
            public bool requested;
            public bool present;
        }

        [Serializable]
        private sealed class UiButtonReport
        {
            public string preset;
            public List<string> aliases;
            public bool searched;
            public bool buttonFound;
            public string buttonObject;
            public string buttonLabel;
            public bool invoked;
            public int triggerSerialBefore;
            public int triggerSerialAfter;
            public string observedPreset;
            public bool controllerTriggered;
            public bool passed;
            public string failureReason;
        }

        [Serializable]
        private sealed class SummaryReport
        {
            public int actions;
            public int actionsPassed;
            public int screenshotsRequested;
            public int screenshotsPresent;
            public int screenshotsCapturedDuringActiveMotion;
            public int faceScreenshotsRequested;
            public int faceScreenshotsPresent;
            public int stageScreenshotsRequested;
            public int stageScreenshotsPresent;
            public bool stageScreenshotsPassed;
            public int uiButtonsFoundAndTriggered;
            public int parameterSamples;
            public bool actionsPassedAll;
            public bool screenshotsPassed;
            public bool faceScreenshotsPassed;
            public bool neutralScreenshotRequested;
            public bool neutralScreenshotPresent;
            public bool neutralFaceScreenshotRequested;
            public bool neutralFaceScreenshotPresent;
            public bool uiBindingPassed;
            public bool transitionsPassed;
            public bool transitionsSkipped;
            public bool lookDirectionPairPassed;
            public bool partial;
            public string partialReason;
            public bool functionalPassed;
            public bool passed;
        }

        [Serializable]
        private sealed class Sample
        {
            public float time;
            public int actionIndex;
            public string preset;
            public string phase;
            public float elapsed;
            public string currentMotion;
            public string currentExpression;
            public string currentGaze;
            public float angleX;
            public float angleY;
            public float angleZ;
            public float bodyAngleX;
            public float bodyAngleY;
            public float bodyAngleZ;
            public float eyeLOpen;
            public float eyeROpen;
            public float eyeSmile;
            public float browForm;
            public float mouthVariant;
            public float mouthForm;
            public float mouthOpenY;
            public float cheek;
            public float eyeBallX;
            public float eyeBallY;
            public bool sighFaceActive;
            public float sighEyeArtWeight;
            public float sighMouthArtWeight;
        }
    }
}
