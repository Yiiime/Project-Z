using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ZDialogueDemo
{
    /// <summary>
    /// 真实 Unity 运行时动作 QA。它记录每帧头、身体参数，并为每个预设保存一张截图。
    /// 组件只由 Editor 菜单临时创建，普通启动不会自动运行。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class ZMotionTrajectoryQa : MonoBehaviour
    {
        private const float ModelTimeoutSeconds = 20f;
        private const float ScreenshotLeadSeconds = 0.25f;
        private const float SampleIntervalSeconds = 1f / 30f;
        private const float MaxNormalizedSpeedPerSecond = 3.5f;
        private const string VerifyArgument = "--z-motion-verify=";
        private const string QuitArgument = "--z-motion-quit";
        private static readonly string[] MotionParameterIds =
        {
            "ParamAngleX",
            "ParamAngleY",
            "ParamAngleZ",
            "ParamBodyAngleX",
            "ParamBodyAngleY",
            "ParamBodyAngleZ",
        };
        private static readonly string[] ExpressionParameterIds =
        {
            "ParamEyeSmile",
            "ParamBrowForm",
            "ParamEyeBallX",
        };

        private readonly List<MotionSample> samples = new List<MotionSample>();
        private readonly List<MotionScreenshot> screenshots = new List<MotionScreenshot>();
        private readonly List<MotionParameterMetadata> parameterMetadata = new List<MotionParameterMetadata>();
        private readonly List<MotionTransitionCheck> transitionChecks = new List<MotionTransitionCheck>();
        private readonly List<PreviewExpressionCheck> previewExpressionChecks =
            new List<PreviewExpressionCheck>();
        private bool running;
        private float startedAt;
        private float nextSampleAt;
        private int stepIndex = -1;
        private ZPerformanceController performance;
        private string outputDirectory;
        private string runId;
        private string requestedOutputDirectory;
        private bool autoStartedByCommandLine;
        private bool quitWhenDone;
        private MotionQaChecks checks = new MotionQaChecks();

        public bool IsFinished { get; private set; }
        public string OutputDirectory => outputDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var output = string.Empty;
            var quit = false;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(VerifyArgument, StringComparison.Ordinal))
                    output = args[i].Substring(VerifyArgument.Length);
                else if (args[i] == QuitArgument)
                    quit = true;
            }

            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output) ||
                FindObjectOfType<ZMotionTrajectoryQa>() != null)
                return;

            var gameObject = new GameObject("Z Motion Trajectory QA (command line)");
            DontDestroyOnLoad(gameObject);
            var qa = gameObject.AddComponent<ZMotionTrajectoryQa>();
            qa.requestedOutputDirectory = output;
            qa.quitWhenDone = quit;
            qa.autoStartedByCommandLine = true;
        }

        private void Start()
        {
            if (autoStartedByCommandLine)
                Begin();
        }

        public void Begin()
        {
            if (running || IsFinished)
                return;
            StartCoroutine(Run());
        }

        private void LateUpdate()
        {
            if (!running || performance == null || Time.unscaledTime < nextSampleAt)
                return;

            nextSampleAt = Time.unscaledTime + SampleIntervalSeconds;
            samples.Add(CaptureSample());
        }

        private IEnumerator Run()
        {
            running = true;
            IsFinished = false;
            performance = FindObjectOfType<ZPerformanceController>();
            var deadline = Time.realtimeSinceStartup + ModelTimeoutSeconds;
            while ((performance == null || !performance.IsModelReady) && Time.realtimeSinceStartup < deadline)
            {
                if (performance == null)
                    performance = FindObjectOfType<ZPerformanceController>();
                yield return null;
            }

            if (performance == null || !performance.IsModelReady)
            {
                Debug.LogError("[ZMotionTrajectoryQa] ZPerformanceController 未就绪。");
                Finish();
                yield break;
            }

            PrepareOutputDirectory();
            startedAt = Time.unscaledTime;
            nextSampleAt = startedAt;
            samples.Clear();
            screenshots.Clear();
            parameterMetadata.Clear();
            transitionChecks.Clear();
            previewExpressionChecks.Clear();
            checks = new MotionQaChecks();
            CaptureParameterMetadata();

            // Let the model/controller settle before measuring a preview. This
            // avoids a stale dialogue segment or Animator write contaminating
            // the first restart/face sample.
            performance.ResetPerformance();
            yield return new WaitForEndOfFrame();

            yield return RunTransitionChecks();

            var steps = new[]
            {
                new MotionStep(ZMotionPreset.Idle, 1.2f),
                new MotionStep(ZMotionPreset.Nod, 1.6f),
                new MotionStep(ZMotionPreset.Idle, 0.5f),
                new MotionStep(ZMotionPreset.Shake, 1.7f),
                new MotionStep(ZMotionPreset.Think, 2.6f),
                new MotionStep(ZMotionPreset.Listen, 3.0f),
                new MotionStep(ZMotionPreset.Happy, 2.0f),
                new MotionStep(ZMotionPreset.Idle, 1.2f),
            };

            for (var i = 0; i < steps.Length; i++)
            {
                stepIndex = i;
                // Use the same public preview entry point as DialoguePanel so
                // this QA catches motion-only previews that leave the face neutral.
                performance.PreviewMotion(steps[i].motion);
                yield return new WaitForSecondsRealtime(ScreenshotLeadSeconds);
                yield return new WaitForEndOfFrame();
                CaptureStepScreenshot(i, steps[i].motion);
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, steps[i].duration - ScreenshotLeadSeconds));
            }

            performance.PreviewMotion(ZMotionPreset.Idle);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForEndOfFrame();
            yield return null;
            EvaluateChecks();
            WriteReport(steps);
            Finish();
        }

        private IEnumerator RunTransitionChecks()
        {
            // 同一动作重启：重新 SetMotionPreset 后，单一动作时钟应回到起点。
            var restartStart = samples.Count;
            performance.PreviewMotion(ZMotionPreset.Nod);
            var firstTriggerSerial = performance.MotionTriggerSerial;
            yield return new WaitForSecondsRealtime(0.35f);
            var elapsedBeforeRestart = performance.MotionElapsed;
            checks.sameMotionBeforeTime = Time.unscaledTime - startedAt;
            checks.sameMotionBeforeState = performance.CurrentMotion.ToString();
            checks.sameMotionBeforeElapsed = elapsedBeforeRestart;
            checks.sameMotionBeforeTriggerSerial = performance.MotionTriggerSerial;
            performance.PreviewMotion(ZMotionPreset.Nod);
            checks.sameMotionAfterTime = Time.unscaledTime - startedAt;
            checks.sameMotionAfterState = performance.CurrentMotion.ToString();
            checks.sameMotionAfterElapsed = performance.MotionElapsed;
            checks.sameMotionAfterTriggerSerial = performance.MotionTriggerSerial;
            yield return new WaitForSecondsRealtime(0.25f);
            var elapsedAfterRestart = performance.MotionElapsed;
            checks.sameMotionProgressedElapsed = elapsedAfterRestart;
            checks.sameMotionRestartPassed = firstTriggerSerial > 0 &&
                                             checks.sameMotionAfterTriggerSerial > checks.sameMotionBeforeTriggerSerial &&
                                             checks.sameMotionAfterState == ZMotionPreset.Nod.ToString() &&
                                             checks.sameMotionAfterElapsed <= 0.001f &&
                                             (elapsedAfterRestart > checks.sameMotionAfterElapsed ||
                                              elapsedBeforeRestart >= 1.35f);
            transitionChecks.Add(EvaluateTransition("same-motion-restart", restartStart, samples.Count));

            // 中断：点头进行中切换到摇头，下一采样不能继续保留旧动作状态。
            var interruptStart = samples.Count;
            performance.PreviewMotion(ZMotionPreset.Nod);
            yield return new WaitForSecondsRealtime(0.2f);
            performance.PreviewMotion(ZMotionPreset.Shake);
            yield return new WaitForSecondsRealtime(0.3f);
            checks.interruptPassed = performance.CurrentMotion == ZMotionPreset.Shake &&
                                     performance.MotionElapsed > 0f;
            transitionChecks.Add(EvaluateTransition("interrupt-nod-to-shake", interruptStart, samples.Count));

            // 停止：停止入口只回到 Idle，不依赖额外 coroutine 收尾。
            var stopStart = samples.Count;
            performance.PreviewMotion(ZMotionPreset.Happy);
            yield return new WaitForSecondsRealtime(0.2f);
            checks.stopExpressionBefore = performance.CurrentExpression.ToString();
            yield return new WaitForEndOfFrame();
            checks.stopEyeSmileBefore = Read("ParamEyeSmile");
            checks.stopBrowFormBefore = Read("ParamBrowForm");
            performance.StopMotionPreview();
            // Wait for the face blend to settle, then inspect the actual
            // parameters. Checking only CurrentMotion == Idle would miss a
            // stopped motion whose smile/troubled face remains on screen.
            yield return new WaitForSecondsRealtime(0.6f);
            checks.stopExpressionAfter = performance.CurrentExpression.ToString();
            yield return new WaitForEndOfFrame();
            checks.stopEyeSmileAfter = Read("ParamEyeSmile");
            checks.stopBrowFormAfter = Read("ParamBrowForm");
            checks.stopPassed = performance.CurrentMotion == ZMotionPreset.Idle &&
                                checks.stopExpressionBefore != ZFaceExpression.Neutral.ToString() &&
                                checks.stopEyeSmileBefore > 0.2f &&
                                checks.stopExpressionAfter == ZFaceExpression.Neutral.ToString() &&
                                Mathf.Abs(checks.stopEyeSmileAfter) <= 0.25f &&
                                Mathf.Abs(checks.stopBrowFormAfter) <= 0.25f;
            transitionChecks.Add(EvaluateTransition("stop-happy-to-idle", stopStart, samples.Count));
            performance.PreviewMotion(ZMotionPreset.Idle);
            stepIndex = -1;
        }

        private void CaptureParameterMetadata()
        {
            for (var i = 0; i < MotionParameterIds.Length; i++)
            {
                if (!performance.TryGetParameterInfo(MotionParameterIds[i], out var minimum, out var maximum,
                        out var defaultValue))
                    continue;
                parameterMetadata.Add(new MotionParameterMetadata
                {
                    id = MotionParameterIds[i],
                    minimum = minimum,
                    maximum = maximum,
                    defaultValue = defaultValue,
                });
            }
        }

        private MotionTransitionCheck EvaluateTransition(string name, int startIndex, int endIndex)
        {
            var check = new MotionTransitionCheck
            {
                name = name,
                sampleStart = startIndex,
                sampleEnd = Mathf.Max(startIndex, endIndex - 1),
                passed = true,
            };
            var firstDelta = true;
            for (var i = Mathf.Max(1, startIndex); i < endIndex && i < samples.Count; i++)
            {
                var previous = samples[i - 1];
                var current = samples[i];
                var deltaTime = current.time - previous.time;
                if (deltaTime <= 0f)
                    continue;

                var previousValues = GetMotionValues(previous);
                var currentValues = GetMotionValues(current);
                var normalizedSpeed = 0f;
                for (var parameterIndex = 0; parameterIndex < parameterMetadata.Count; parameterIndex++)
                {
                    var metadata = parameterMetadata[parameterIndex];
                    var parameterSlot = Array.IndexOf(MotionParameterIds, metadata.id);
                    if (parameterSlot < 0)
                        continue;
                    var range = Mathf.Max(0.001f, metadata.maximum - metadata.minimum);
                    var normalizedStep = Mathf.Abs(currentValues[parameterSlot] - previousValues[parameterSlot]) / range;
                    normalizedSpeed = Mathf.Max(normalizedSpeed, normalizedStep / deltaTime);
                }

                check.maxNormalizedSpeed = Mathf.Max(check.maxNormalizedSpeed, normalizedSpeed);
                if (firstDelta)
                {
                    check.firstNormalizedSpeed = normalizedSpeed;
                    firstDelta = false;
                }
                if (normalizedSpeed > MaxNormalizedSpeedPerSecond)
                    check.passed = false;
            }
            return check;
        }

        private static float[] GetMotionValues(MotionSample sample)
        {
            return new[]
            {
                sample.angleX,
                sample.angleY,
                sample.angleZ,
                sample.bodyAngleX,
                sample.bodyAngleY,
                sample.bodyAngleZ,
            };
        }

        private void EvaluateChecks()
        {
            checks.samplesTotal = samples.Count;
            checks.samplesInRange = 0;
            checks.rangeAssertionsPassed = true;
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var values = new[]
                {
                    sample.angleX,
                    sample.angleY,
                    sample.angleZ,
                    sample.bodyAngleX,
                    sample.bodyAngleY,
                    sample.bodyAngleZ,
                };
                var sampleInRange = true;
                for (var parameterIndex = 0; parameterIndex < MotionParameterIds.Length; parameterIndex++)
                {
                    if (!performance.TryGetParameterRange(MotionParameterIds[parameterIndex], out var minimum,
                            out var maximum))
                    {
                        sampleInRange = false;
                        continue;
                    }

                    var value = values[parameterIndex];
                    if (float.IsNaN(value) || value < minimum - 0.001f || value > maximum + 0.001f)
                        sampleInRange = false;
                }

                if (sampleInRange)
                    checks.samplesInRange++;
                else
                    checks.rangeAssertionsPassed = false;
            }

            EvaluatePreviewExpressions();
            EvaluateMotionAmplitude();

            checks.screenshotsRequested = screenshots.Count;
            checks.screenshotsPresent = 0;
            for (var i = 0; i < screenshots.Count; i++)
            {
                if (File.Exists(Path.Combine(outputDirectory, screenshots[i].file)))
                    checks.screenshotsPresent++;
            }
            checks.screenshotAssertionsPassed = checks.screenshotsPresent == checks.screenshotsRequested;
            checks.transitionVelocityAssertionsPassed = true;
            for (var i = 0; i < transitionChecks.Count; i++)
            {
                if (!transitionChecks[i].passed)
                    checks.transitionVelocityAssertionsPassed = false;
            }
            checks.passCount = 0;
            if (checks.sameMotionRestartPassed) checks.passCount++;
            if (checks.interruptPassed) checks.passCount++;
            if (checks.stopPassed) checks.passCount++;
            if (checks.rangeAssertionsPassed) checks.passCount++;
            if (checks.screenshotAssertionsPassed) checks.passCount++;
            if (checks.transitionVelocityAssertionsPassed) checks.passCount++;
            if (checks.previewExpressionAssertionsPassed) checks.passCount++;
            if (checks.motionAmplitudeAssertionsPassed) checks.passCount++;
            checks.totalChecks = 8;
            checks.passed = checks.sameMotionRestartPassed && checks.interruptPassed &&
                            checks.stopPassed && checks.rangeAssertionsPassed && checks.screenshotAssertionsPassed &&
                            checks.transitionVelocityAssertionsPassed && checks.previewExpressionAssertionsPassed &&
                            checks.motionAmplitudeAssertionsPassed &&
                            checks.samplesTotal > 0;
        }

        private void EvaluatePreviewExpressions()
        {
            previewExpressionChecks.Clear();
            var motions = new[]
            {
                ZMotionPreset.Nod,
                ZMotionPreset.Shake,
                ZMotionPreset.Think,
                ZMotionPreset.Listen,
                ZMotionPreset.Happy,
            };
            for (var i = 0; i < motions.Length; i++)
            {
                var motion = motions[i];
                var step = motion == ZMotionPreset.Nod ? 1 :
                    motion == ZMotionPreset.Shake ? 3 :
                    motion == ZMotionPreset.Think ? 4 :
                    motion == ZMotionPreset.Listen ? 5 : 6;
                var expected = ExpectedPreviewExpression(motion);
                var check = new PreviewExpressionCheck
                {
                    motion = motion.ToString(),
                    expectedExpression = expected,
                    observedExpression = string.Empty,
                    passed = false,
                };
                for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
                {
                    var sample = samples[sampleIndex];
                    if (sample.step != step || !string.Equals(sample.expression, expected,
                            StringComparison.Ordinal))
                        continue;

                    check.observedExpression = sample.expression;
                    check.eyeSmile = Mathf.Max(check.eyeSmile, Mathf.Abs(sample.eyeSmile));
                    check.browForm = Mathf.Max(check.browForm, Mathf.Abs(sample.browForm));
                    check.gazeX = Mathf.Min(check.gazeX, sample.eyeBallX);
                }

                if (expected == ZFaceExpression.Smile.ToString())
                    check.passed = check.eyeSmile >= 0.2f;
                else if (expected == ZFaceExpression.Troubled.ToString())
                    check.passed = check.browForm >= 0.2f;
                else
                    // Listen is intentionally a neutral face. Its gaze may
                    // stay on the player, so expression QA must not require a
                    // directional eye target.
                    check.passed = check.observedExpression == expected;
                previewExpressionChecks.Add(check);
            }

            var neutralSamples = 0;
            var neutralPass = false;
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (sample.step != 7 || sample.expression != ZFaceExpression.Neutral.ToString())
                    continue;
                neutralSamples++;
                neutralPass |= Mathf.Abs(sample.eyeSmile) <= 0.25f && Mathf.Abs(sample.browForm) <= 0.25f;
            }

            checks.previewExpressionCount = previewExpressionChecks.Count;
            checks.previewExpressionsObserved = 0;
            for (var i = 0; i < previewExpressionChecks.Count; i++)
            {
                if (previewExpressionChecks[i].passed)
                    checks.previewExpressionsObserved++;
            }
            checks.previewExpressionReturnedToNeutral = neutralSamples > 0 && neutralPass;
            checks.previewExpressionAssertionsPassed =
                checks.previewExpressionsObserved == checks.previewExpressionCount &&
                checks.previewExpressionReturnedToNeutral;
        }

        private void EvaluateMotionAmplitude()
        {
            var peak = 0f;
            var nodY = 0f;
            var shakeX = 0f;
            var thinkZ = 0f;
            var listenZ = 0f;
            var happyY = 0f;
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (sample.step < 1 || sample.step > 6 || sample.preset == ZMotionPreset.Idle.ToString())
                    continue;
                var values = GetMotionValues(sample);
                for (var parameterIndex = 0; parameterIndex < parameterMetadata.Count; parameterIndex++)
                {
                    var metadata = parameterMetadata[parameterIndex];
                    var parameterSlot = Array.IndexOf(MotionParameterIds, metadata.id);
                    if (parameterSlot < 0)
                        continue;
                    var range = Mathf.Max(0.001f, metadata.maximum - metadata.minimum);
                    var displacement = Mathf.Abs(values[parameterSlot] - metadata.defaultValue);
                    peak = Mathf.Max(peak, displacement / range);
                    if (sample.preset == ZMotionPreset.Nod.ToString() && metadata.id == "ParamAngleY")
                        nodY = Mathf.Max(nodY, displacement);
                    else if (sample.preset == ZMotionPreset.Shake.ToString() && metadata.id == "ParamAngleX")
                        shakeX = Mathf.Max(shakeX, displacement);
                    else if (sample.preset == ZMotionPreset.Think.ToString() && metadata.id == "ParamAngleZ")
                        thinkZ = Mathf.Max(thinkZ, displacement);
                    else if (sample.preset == ZMotionPreset.Listen.ToString() && metadata.id == "ParamAngleZ")
                        listenZ = Mathf.Max(listenZ, displacement);
                    else if (sample.preset == ZMotionPreset.Happy.ToString() && metadata.id == "ParamAngleY")
                        happyY = Mathf.Max(happyY, displacement);
                }
            }

            checks.motionPeakNormalizedDisplacement = peak;
            checks.nodYPeakDegrees = nodY;
            checks.shakeXPeakDegrees = shakeX;
            checks.thinkZPeakDegrees = thinkZ;
            checks.listenZPeakDegrees = listenZ;
            checks.happyYPeakDegrees = happyY;
            // The old preview was visibly small (Nod Y peaked below 4 degrees
            // in the saved baseline). These per-motion thresholds make the QA
            // red when one preset remains tiny while another is large.
            checks.motionAmplitudeAssertionsPassed = nodY >= 8f && shakeX >= 6f &&
                                                      thinkZ >= 6f && listenZ >= 4.5f &&
                                                      happyY >= 4.5f;
        }

        private static string ExpectedPreviewExpression(ZMotionPreset motion)
        {
            switch (motion)
            {
                case ZMotionPreset.Nod:
                case ZMotionPreset.Happy:
                    return ZFaceExpression.Smile.ToString();
                case ZMotionPreset.Shake:
                case ZMotionPreset.Think:
                    return ZFaceExpression.Troubled.ToString();
                default:
                    return ZFaceExpression.Neutral.ToString();
            }
        }

        private MotionSample CaptureSample()
        {
            return new MotionSample
            {
                time = Time.unscaledTime - startedAt,
                step = stepIndex,
                preset = performance.CurrentMotion.ToString(),
                expression = performance.CurrentExpression.ToString(),
                angleX = Read("ParamAngleX"),
                angleY = Read("ParamAngleY"),
                angleZ = Read("ParamAngleZ"),
                bodyAngleX = Read("ParamBodyAngleX"),
                bodyAngleY = Read("ParamBodyAngleY"),
                bodyAngleZ = Read("ParamBodyAngleZ"),
                eyeSmile = Read("ParamEyeSmile"),
                browForm = Read("ParamBrowForm"),
                eyeBallX = Read("ParamEyeBallX"),
            };
        }

        private float Read(string id)
        {
            return performance != null && performance.TryGetParameterValue(id, out var value) ? value : float.NaN;
        }

        private void CaptureStepScreenshot(int index, ZMotionPreset motion)
        {
            var fileName = string.Format(CultureInfo.InvariantCulture, "motion-{0:00}-{1}.png", index + 1,
                motion.ToString().ToLowerInvariant());
            var path = Path.Combine(outputDirectory, fileName);
            ScreenCapture.CaptureScreenshot(path);
            screenshots.Add(new MotionScreenshot
            {
                step = index,
                preset = motion.ToString(),
                file = fileName,
            });
        }

        private void PrepareOutputDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var requested = string.IsNullOrWhiteSpace(requestedOutputDirectory)
                ? Environment.GetEnvironmentVariable("Z_MOTION_QA_OUTPUT")
                : requestedOutputDirectory;
            var baseDirectory = string.IsNullOrWhiteSpace(requested)
                ? Path.Combine(projectRoot, "verification", "motion-presets-20261103")
                : Path.GetFullPath(requested);
            runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            outputDirectory = Path.Combine(baseDirectory, runId);
            Directory.CreateDirectory(outputDirectory);
        }

        private void WriteReport(MotionStep[] steps)
        {
            var report = new MotionTrajectoryReport
            {
                schemaVersion = "z-motion-trajectory-v2",
                generatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                outputDirectory = outputDirectory,
                runId = runId,
                steps = new List<MotionStep>(steps),
                screenshots = screenshots,
                samples = samples,
                checks = checks,
                parameterMetadata = parameterMetadata,
                transitionChecks = transitionChecks,
                previewExpressionChecks = previewExpressionChecks,
            };
            var path = Path.Combine(outputDirectory, "motion-trajectory.json");
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("[ZMotionTrajectoryQa] 轨迹已写入：" + path);
            }
            catch (Exception exception)
            {
                Debug.LogError("[ZMotionTrajectoryQa] 无法写入轨迹：" + exception.GetType().Name);
            }
        }

        private void Finish()
        {
            running = false;
            IsFinished = true;
            if (quitWhenDone)
                StartCoroutine(QuitNextFrame());
        }

        private IEnumerator QuitNextFrame()
        {
            yield return null;
            Application.Quit();
        }

        [Serializable]
        private sealed class MotionTrajectoryReport
        {
            public string schemaVersion;
            public string generatedAt;
            public string outputDirectory;
            public string runId;
            public List<MotionStep> steps;
            public List<MotionScreenshot> screenshots;
            public List<MotionSample> samples;
            public MotionQaChecks checks;
            public List<MotionParameterMetadata> parameterMetadata;
            public List<MotionTransitionCheck> transitionChecks;
            public List<PreviewExpressionCheck> previewExpressionChecks;
        }

        [Serializable]
        private sealed class MotionParameterMetadata
        {
            public string id;
            public float minimum;
            public float maximum;
            public float defaultValue;
        }

        [Serializable]
        private sealed class MotionTransitionCheck
        {
            public string name;
            public int sampleStart;
            public int sampleEnd;
            public float firstNormalizedSpeed;
            public float maxNormalizedSpeed;
            public bool passed;
        }

        [Serializable]
        private sealed class MotionQaChecks
        {
            public bool sameMotionRestartPassed;
            public bool interruptPassed;
            public bool stopPassed;
            public bool rangeAssertionsPassed;
            public bool screenshotAssertionsPassed;
            public bool transitionVelocityAssertionsPassed;
            public bool previewExpressionAssertionsPassed;
            public bool previewExpressionReturnedToNeutral;
            public bool motionAmplitudeAssertionsPassed;
            public int previewExpressionCount;
            public int previewExpressionsObserved;
            public int samplesTotal;
            public int samplesInRange;
            public int screenshotsRequested;
            public int screenshotsPresent;
            public int passCount;
            public int totalChecks;
            public float maxNormalizedSpeedPerSecond = MaxNormalizedSpeedPerSecond;
            public float motionPeakNormalizedDisplacement;
            public float nodYPeakDegrees;
            public float shakeXPeakDegrees;
            public float thinkZPeakDegrees;
            public float listenZPeakDegrees;
            public float happyYPeakDegrees;
            public float sameMotionBeforeTime;
            public float sameMotionAfterTime;
            public string sameMotionBeforeState;
            public string sameMotionAfterState;
            public float sameMotionBeforeElapsed;
            public float sameMotionAfterElapsed;
            public float sameMotionProgressedElapsed;
            public int sameMotionBeforeTriggerSerial;
            public int sameMotionAfterTriggerSerial;
            public string stopExpressionBefore;
            public string stopExpressionAfter;
            public float stopEyeSmileBefore;
            public float stopEyeSmileAfter;
            public float stopBrowFormBefore;
            public float stopBrowFormAfter;
            public bool passed;
        }

        [Serializable]
        private sealed class PreviewExpressionCheck
        {
            public string motion;
            public string expectedExpression;
            public string observedExpression;
            public float eyeSmile;
            public float browForm;
            public float gazeX = float.PositiveInfinity;
            public bool passed;
        }

        [Serializable]
        private sealed class MotionStep
        {
            public string preset;
            public float duration;
            [NonSerialized] public ZMotionPreset motion;

            public MotionStep(ZMotionPreset motion, float duration)
            {
                this.motion = motion;
                preset = motion.ToString();
                this.duration = duration;
            }
        }

        [Serializable]
        private sealed class MotionScreenshot
        {
            public int step;
            public string preset;
            public string file;
        }

        [Serializable]
        private sealed class MotionSample
        {
            public float time;
            public int step;
            public string preset;
            public string expression;
            public float angleX;
            public float angleY;
            public float angleZ;
            public float bodyAngleX;
            public float bodyAngleY;
            public float bodyAngleZ;
            public float eyeSmile;
            public float browForm;
            public float eyeBallX;
        }
    }
}
