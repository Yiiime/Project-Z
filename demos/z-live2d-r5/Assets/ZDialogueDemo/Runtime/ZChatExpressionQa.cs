using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZDialogueDemo
{
    /// <summary>
    /// Opt-in runtime QA for the chat expression path. It deserializes a
    /// synthetic ChatResponse and calls the same public PlaySegments seam as
    /// DialoguePanel, without LLM, network, ACK or database activity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZChatExpressionQa : MonoBehaviour
    {
        private const string VerifyArgument = "--z-chat-face-verify=";
        private const string FixtureArgument = "--z-chat-face-fixture=";
        private const string QuitArgument = "--z-chat-face-quit";
        private const float StableSampleSeconds = 0.70f;
        private const float RecoverySeconds = 0.75f;
        private const float NaturalWaitSeconds = 2.65f;
        private const float SighHoldSeconds = 1.70f;
        private const float SighBodyIdleWaitSeconds = 0.35f;
        private const float FaceToleranceRatio = 0.12f;
        private const float FaceToleranceMinimum = 0.08f;
        private const float PoseTolerance = 0.05f;
        private const float EyeTolerance = 0.12f;
        private const float BodyMotionMinimum = 0.01f;
        private const float AccentVisibleMinimum = 0.25f;
        private const float AccentResidualMaximum = 0.05f;

        private static readonly string[] FaceIds =
        {
            "ParamEyeSmile", "ParamBrowForm", "ParamCheek", "ParamMouthVariant", "ParamMouthForm",
        };

        private static readonly string[] PoseIds =
        {
            "ParamPoseWave", "ParamPoseExplain", "ParamPosePoint",
        };

        private static readonly string[] Gestures = { "none", "wave", "explain", "point" };

        private static readonly string[] HeadIds = { "ParamAngleX", "ParamAngleY", "ParamAngleZ" };
        private static readonly string[] BodyIds = { "ParamBodyAngleX", "ParamBodyAngleY", "ParamBodyAngleZ" };

        private static readonly ExpressionProfile[] Profiles =
        {
            new ExpressionProfile("neutral", ZFaceExpression.Neutral, 1f, string.Empty,
                ZMotionPreset.Idle, false, true, false, false, new FaceValue[0]),
            new ExpressionProfile("smile", ZFaceExpression.Smile, 1f, string.Empty,
                ZMotionPreset.Idle, false, true, false, false, new[]
            {
                new FaceValue("ParamEyeSmile", 1f), new FaceValue("ParamMouthForm", 0.60f),
            }),
            new ExpressionProfile("surprise", ZFaceExpression.Surprise, 1f, "Surprise",
                ZMotionPreset.Idle, false, true, false, false, new[]
            {
                new FaceValue("ParamMouthVariant", 1f), new FaceValue("ParamBrowForm", 0.25f),
            }),
            new ExpressionProfile("angry", ZFaceExpression.Angry, 0.76f, "Angry",
                ZMotionPreset.Idle, false, true, false, false, new[]
            {
                new FaceValue("ParamBrowForm", -1f), new FaceValue("ParamMouthVariant", -1f),
                new FaceValue("ParamMouthForm", -0.40f),
            }),
            new ExpressionProfile("troubled", ZFaceExpression.Troubled, 0.82f, string.Empty,
                ZMotionPreset.Idle, false, true, false, false, new[]
            {
                new FaceValue("ParamBrowForm", 1f), new FaceValue("ParamMouthVariant", -1f),
                new FaceValue("ParamMouthForm", -0.25f),
            }),
            new ExpressionProfile("blush", ZFaceExpression.Blush, 0.90f, "Shy",
                ZMotionPreset.Idle, false, true, false, false, new[]
            {
                new FaceValue("ParamCheek", 1f),
                new FaceValue("ParamMouthForm", 0.35f),
            }),
            new ExpressionProfile("cry", ZFaceExpression.Cry, 0f, "Cry",
                ZMotionPreset.Cry, true, true, true, false, new[]
            {
                new FaceValue("ParamEyeSmile", 0f), new FaceValue("ParamBrowForm", 1f),
                new FaceValue("ParamCheek", 0f), new FaceValue("ParamMouthVariant", 2f),
                new FaceValue("ParamMouthForm", -0.55f),
            }),
            new ExpressionProfile("bawl", ZFaceExpression.Bawl, 0f, "Bawl",
                ZMotionPreset.Bawl, true, true, true, false, new[]
            {
                new FaceValue("ParamEyeSmile", 0f), new FaceValue("ParamBrowForm", 1f),
                new FaceValue("ParamCheek", 0f), new FaceValue("ParamMouthVariant", 2f),
                new FaceValue("ParamMouthForm", -0.72f),
            }),
            new ExpressionProfile("sigh", ZFaceExpression.Sigh, 0f, string.Empty,
                ZMotionPreset.Sigh, true, true, false, true, new[]
            {
                new FaceValue("ParamEyeSmile", 0f), new FaceValue("ParamBrowForm", 0.28f),
                new FaceValue("ParamCheek", 0f), new FaceValue("ParamMouthVariant", 0f),
                new FaceValue("ParamMouthForm", 0f),
            }),
            new ExpressionProfile("gloom", ZFaceExpression.Gloom, 1f, "Gloom",
                ZMotionPreset.Gloom, true, false, false, false, new[]
            {
                new FaceValue("ParamEyeSmile", 0f), new FaceValue("ParamBrowForm", 0f),
                new FaceValue("ParamCheek", 0f), new FaceValue("ParamMouthVariant", -1f),
                new FaceValue("ParamMouthForm", -0.15f),
            }),
        };

        private static readonly string[] NewExpressionNames = { "cry", "bawl", "sigh", "gloom" };

        private ZPerformanceController performance;
        private DialoguePanel panel;
        private ZEmotionAccents accents;
        private ZSighFaceArt sighArt;
        private string outputDirectory;
        private string fixturePath;
        private bool quitWhenDone;
        private bool started;
        private bool inputIsolationAttempted;
        private int checkSerial;
        private readonly List<EventSystemState> eventSystemStates = new List<EventSystemState>();
        private Report report;
        private ExpressionCheck lastCheck;

        private struct EventSystemState
        {
            public EventSystem system;
            public bool enabled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var output = string.Empty;
            var fixture = string.Empty;
            var quit = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? string.Empty;
                if (arg.StartsWith(VerifyArgument, StringComparison.Ordinal))
                    output = arg.Substring(VerifyArgument.Length);
                else if (arg.StartsWith(FixtureArgument, StringComparison.Ordinal))
                    fixture = arg.Substring(FixtureArgument.Length);
                else if (string.Equals(arg, QuitArgument, StringComparison.Ordinal))
                    quit = true;
            }
            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output) ||
                FindObjectOfType<ZChatExpressionQa>() != null)
                return;

            var go = new GameObject("ZChatExpressionQa");
            var qa = go.AddComponent<ZChatExpressionQa>();
            qa.outputDirectory = output;
            qa.fixturePath = fixture;
            qa.quitWhenDone = quit;
        }

        private void Start()
        {
            if (started)
                return;
            started = true;
            report = new Report
            {
                runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture),
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                outputDirectory = outputDirectory ?? string.Empty,
                fixturePath = fixturePath ?? string.Empty,
                noNetworkRequested = true,
                inputIsolationRequested = true,
                expressions = new List<ExpressionCheck>(),
                transitions = new List<TransitionCheck>(),
                cleanups = new List<CleanupCheck>(),
                effectCleanups = new List<EffectCleanupCheck>(),
                sighHolds = new List<SighHoldCheck>(),
                fixtureChecks = new List<ExpressionCheck>(),
                errors = new List<string>(),
            };
            StartCoroutine(RunRoutine());
        }

        private void OnDisable() => RestoreEventSystems();
        private void OnDestroy() => RestoreEventSystems();

        private IEnumerator RunRoutine()
        {
            try
            {
                yield return RunBody();
                if (report != null)
                    report.bodyCompleted = true;
            }
            finally
            {
                RestoreEventSystems();
                FinalizeReport();
                if (quitWhenDone)
                    Application.Quit();
            }
        }

        private IEnumerator RunBody()
        {
            CaptureAndDisableEventSystems();
            ResolveDependencies();
            yield return WaitForDependencies(2f);
            ResolveDependencies();
            report.panelFound = panel != null;
            report.controllerFound = performance != null;
            report.controllerResolvedViaPanel = panel != null && panel.PerformanceController == performance;
            report.accentsFound = accents != null;
            report.sighArtFound = sighArt != null;

            if (performance == null || !performance.IsModelReady)
            {
                AddError("controller_or_model_not_ready");
                yield break;
            }
            if (accents == null)
                AddError("emotion_accents_not_found");

            yield return PrepareNeutral();
            report.baseline = ReadState();

            yield return RunExpressionMatrix();
            // Keep the legacy transition assertions as real adjacent
            // PlaySegments calls; matrix entries are intentionally sampled
            // independently and must not stand in for this sequence.
            yield return RunExpression("smile", "explain", true, false, false);
            var smile = lastCheck;
            yield return RunExpression("angry", "explain", false, false, false);
            var angry = lastCheck;
            yield return RunExpression("neutral", "explain", false, false, false);
            var neutral = lastCheck;
            RecordTransition("Smile->Angry", smile, angry);
            RecordTransition("Angry->Neutral", angry, neutral);

            yield return RunExpression("surprise", "explain", true, false, false);
            var surprise = lastCheck;
            yield return RunExpression("blush", "explain", false, false, false);
            var blush = lastCheck;
            RecordTransition("Surprise->Blush", surprise, blush);

            yield return RunCleanupTest("stop", "surprise", "explain", false);
            yield return RunCleanupTest("cancel", "angry", "explain", true);

            yield return PrepareNeutral();
            var natural = ParseSyntheticResponse("troubled", "explain", "chat-natural-end");
            if (natural != null)
                performance.PlaySegments(natural.turn_id, natural.segments);
            yield return new WaitForSecondsRealtime(NaturalWaitSeconds);
            yield return CheckCleanup("natural-completion");

            yield return RunExtendedExpressionLifecycle();
            yield return RunFixture();
        }

        private IEnumerator RunExpressionMatrix()
        {
            for (var i = 0; i < Profiles.Length; i++)
            {
                yield return PrepareNeutral();
                for (var j = 0; j < Gestures.Length; j++)
                {
                    var gesture = Gestures[j];
                    yield return RunExpression(Profiles[i].name, gesture,
                        false, ShouldCaptureMatrixScreenshots(Profiles[i].name, gesture), true);
                }
            }
        }

        private void ResolveDependencies()
        {
            if (panel == null)
                panel = FindObjectOfType<DialoguePanel>();
            if (performance == null)
                performance = panel != null && panel.PerformanceController != null
                    ? panel.PerformanceController
                    : FindObjectOfType<ZPerformanceController>();
            if (accents == null)
                accents = FindObjectOfType<ZEmotionAccents>();
            if (sighArt == null)
                sighArt = FindObjectOfType<ZSighFaceArt>();
        }

        private IEnumerator WaitForDependencies(float timeoutSeconds)
        {
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                ResolveDependencies();
                if (performance != null && performance.IsModelReady && accents != null)
                    yield break;
                yield return null;
            }
        }

        private IEnumerator PrepareNeutral()
        {
            if (performance == null)
                yield break;
            performance.CancelCurrentPerformance();
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
        }

        private IEnumerator RunExpression(string name, string gesture, bool resetFirst,
            bool captureScreenshots, bool addToMatrixReport)
        {
            if (resetFirst)
                yield return PrepareNeutral();

            var profile = FindProfile(name);
            var check = CreateExpressionCheck("matrix", -1, name, gesture, "player", profile,
                captureScreenshots);
            if (addToMatrixReport)
                report.expressions.Add(check);
            lastCheck = check;

            var response = ParseSyntheticResponse(name, gesture,
                "chat-face-" + Normalize(name) + "-" + Normalize(gesture) + "-" + (++checkSerial));
            check.jsonParsed = IsSingleSyntheticSegment(response, name, gesture);
            if (!check.jsonParsed)
            {
                AddError("synthetic_json_failed_" + Normalize(name) + "_" + Normalize(gesture));
                yield break;
            }

            yield return PlayAndEvaluate(check, response.turn_id, response.segments[0], profile,
                captureScreenshots);
        }

        private IEnumerator PlayAndEvaluate(ExpressionCheck check, string turnId,
            DialogueClient.ChatSegment segment, ExpressionProfile profile, bool captureScreenshots)
        {
            if (check == null || segment == null || performance == null)
                yield break;

            check.turnId = turnId ?? string.Empty;
            performance.PlaySegments(turnId ?? string.Empty, new[] { segment });
            check.played = true;
            var start = Time.unscaledTime;
            while (Time.unscaledTime - start < StableSampleSeconds)
            {
                UpdatePeaks(check);
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            UpdatePeaks(check);
            check.sampleElapsed = Time.unscaledTime - start;
            EvaluateExpression(check, profile);
            if (captureScreenshots)
                CaptureScreenshots(check);
            check.screenshotPassed = !check.screenshotsRequired ||
                                     (check.fullScreenshotRequested && check.faceScreenshotRequested);
            check.passed = check.jsonParsed && check.played && check.statePassed && check.facePassed &&
                           check.eyePassed && check.bodyMotionPassed && check.manualFalse &&
                           check.effectPassed && check.posePassed && check.screenshotPassed;
            if (!check.passed)
                AddError("expression_failed_" + Normalize(check.expression) + "_" + Normalize(check.gesture));
        }

        private DialogueClient.ChatResponse ParseSyntheticResponse(string expression, string gesture, string turnId)
        {
            var json = "{\"turn_id\":\"" + turnId + "\",\"segments\":[{\"text\":\"QA synthetic segment\",\"expression\":\"" +
                       expression + "\",\"gesture\":\"" + gesture + "\",\"gaze\":\"player\"}],\"provider\":\"synthetic\",\"memory_count\":0}";
            try
            {
                return JsonUtility.FromJson<DialogueClient.ChatResponse>(json);
            }
            catch (Exception exception)
            {
                AddError("synthetic_json_exception_" + exception.GetType().Name);
                return null;
            }
        }

        private static bool IsSingleSyntheticSegment(DialogueClient.ChatResponse response,
            string expression, string gesture)
        {
            return response != null && response.segments != null && response.segments.Length == 1 &&
                   response.segments[0] != null &&
                   string.Equals(response.segments[0].expression, expression, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(response.segments[0].gesture, gesture, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(response.segments[0].gaze, "player", StringComparison.OrdinalIgnoreCase);
        }

        private ExpressionCheck CreateExpressionCheck(string source, int fixtureIndex, string expression,
            string gesture, string gaze, ExpressionProfile profile, bool screenshotsRequired)
        {
            var name = Normalize(expression);
            var normalizedGesture = Normalize(gesture);
            var legacyScreenshot = source == "matrix" && !IsNewExpression(name);
            var baseName = source == "matrix"
                ? (legacyScreenshot ? "z-chat-face-" + name : "z-chat-face-" + name + "-" + normalizedGesture)
                : "z-chat-fixture-" + fixtureIndex.ToString("D3", CultureInfo.InvariantCulture) + "-" + name + "-" + normalizedGesture;
            return new ExpressionCheck
            {
                source = source,
                fixtureIndex = fixtureIndex,
                expression = expression ?? string.Empty,
                gesture = gesture ?? string.Empty,
                expectedExpression = profile == null ? string.Empty : profile.expectedExpression.ToString(),
                expectedGesture = GestureToEnumString(gesture),
                expectedGaze = GazeToEnumString(gaze),
                expectedMotion = profile == null ? string.Empty : ExpectedMotion(profile, gesture).ToString(),
                expectedAccent = profile == null ? string.Empty : profile.accent,
                screenshotsRequired = screenshotsRequired,
                fullScreenshotFile = baseName + "-full.png",
                faceScreenshotFile = baseName + "-face.png",
                parameters = new List<ParameterCheck>(),
                poseParameters = new List<ParameterCheck>(),
            };
        }

        private static bool ShouldCaptureMatrixScreenshots(string expression, string gesture)
        {
            if (string.Equals(gesture, "explain", StringComparison.OrdinalIgnoreCase) &&
                !IsNewExpression(expression))
                return true;
            return (string.Equals(expression, "cry", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(gesture, "wave", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(expression, "bawl", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(gesture, "explain", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(expression, "sigh", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(gesture, "explain", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(expression, "gloom", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(gesture, "point", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNewExpression(string expression)
        {
            for (var i = 0; i < NewExpressionNames.Length; i++)
            {
                if (string.Equals(NewExpressionNames[i], expression, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void UpdatePeaks(ExpressionCheck check)
        {
            check.headPeakNormalized = Mathf.Max(check.headPeakNormalized, PeakNormalized(HeadIds));
            check.bodyPeakNormalized = Mathf.Max(check.bodyPeakNormalized, PeakNormalized(BodyIds));
        }

        private float PeakNormalized(string[] ids)
        {
            var peak = 0f;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!performance.TryGetParameterInfo(ids[i], out var minimum, out var maximum, out var defaultValue) ||
                    !performance.TryGetParameterValue(ids[i], out var actual))
                    continue;
                peak = Mathf.Max(peak, Mathf.Abs(actual - defaultValue) /
                    Mathf.Max(0.001f, maximum - minimum));
            }
            return peak;
        }

        private void EvaluateExpression(ExpressionCheck check, ExpressionProfile profile)
        {
            var state = ReadState();
            check.currentExpression = state.expression;
            check.currentGesture = state.gesture;
            check.currentGaze = state.gaze;
            check.currentMotion = state.motion;
            check.motionElapsed = state.motionElapsed;
            check.accentName = state.accentName;
            check.accentIntensity = state.accentIntensity;
            check.accentAnchorsReady = state.accentAnchorsReady;
            check.eyeExpected = profile == null ? 1f : profile.eyeOpen;
            check.eyeLOpen = state.eyeLOpen;
            check.eyeROpen = state.eyeROpen;
            check.manualFalse = !state.previewPresentationActive;
            check.statePassed = profile != null &&
                                string.Equals(state.expression, check.expectedExpression,
                                    StringComparison.Ordinal) &&
                                string.Equals(state.gesture, check.expectedGesture,
                                    StringComparison.Ordinal) &&
                                string.Equals(state.gaze, check.expectedGaze,
                                    StringComparison.Ordinal) &&
                                string.Equals(state.motion, check.expectedMotion,
                                    StringComparison.Ordinal);
            check.eyePassed = Mathf.Abs(state.eyeLOpen - check.eyeExpected) <= EyeTolerance &&
                              Mathf.Abs(state.eyeROpen - check.eyeExpected) <= EyeTolerance;
            // The existing short Shake is damped more strongly than Nod.
            // Check both channels for this motion instead of imposing Nod's
            // torso amplitude on every expression/gesture combination.
            var motionVisible = check.expectedMotion == ZMotionPreset.Shake.ToString()
                ? check.bodyPeakNormalized >= 0.004f && check.headPeakNormalized >= 0.015f
                : check.bodyPeakNormalized >= BodyMotionMinimum;
            check.bodyMotionPassed = profile != null &&
                                     (!profile.bodyMotionRequired || motionVisible);
            check.accentPassed = CheckAccent(check.expectedAccent, state);
            check.facePassed = CheckFaceProfile(check, profile);
            check.posePassed = CheckGesturePose(check);
            check.tearAnchorsPassed = profile != null &&
                                      (!profile.tearAnchorsRequired ||
                                       (state.accentAnchorsReady && state.tearsStartAtOuterCorners));
            check.sighArtPassed = profile != null &&
                                  (!profile.sighArtRequired ||
                                   (state.sighArtReady && state.sighVisible &&
                                    state.sighVisibleInstanceCount >= 3 &&
                                    state.sighMouthArtWeight >= AccentVisibleMinimum));
            check.effectPassed = check.accentPassed && check.tearAnchorsPassed && check.sighArtPassed;
        }

        private bool CheckFaceProfile(ExpressionCheck check, ExpressionProfile profile)
        {
            if (check == null || profile == null || performance == null)
                return false;
            if (check.parameters == null)
                check.parameters = new List<ParameterCheck>();

            var expected = new Dictionary<string, float>(StringComparer.Ordinal);
            for (var i = 0; i < profile.faceValues.Length; i++)
                expected[profile.faceValues[i].id] = profile.faceValues[i].value;

            var passed = true;
            for (var i = 0; i < FaceIds.Length; i++)
            {
                var id = FaceIds[i];
                if (!performance.TryGetParameterInfo(id, out var minimum, out var maximum, out var defaultValue) ||
                    !performance.TryGetParameterValue(id, out var actual))
                {
                    passed = false;
                    check.parameters.Add(new ParameterCheck { parameterId = id, passed = false });
                    continue;
                }
                var expectedValue = expected.TryGetValue(id, out var value) ? value : defaultValue;
                var tolerance = Mathf.Max(FaceToleranceMinimum, (maximum - minimum) * FaceToleranceRatio);
                var parameterPassed = Mathf.Abs(actual - expectedValue) <= tolerance;
                check.parameters.Add(new ParameterCheck
                {
                    parameterId = id,
                    expected = expectedValue,
                    actual = actual,
                    minimum = minimum,
                    maximum = maximum,
                    tolerance = tolerance,
                    passed = parameterPassed,
                });
                passed &= parameterPassed;
            }
            return passed;
        }

        private bool CheckGesturePose(ExpressionCheck check)
        {
            if (check == null || performance == null)
                return false;
            if (check.poseParameters == null)
                check.poseParameters = new List<ParameterCheck>();

            var selectedId = PoseIdForGesture(check.gesture);
            var passed = selectedId != null;
            for (var i = 0; i < PoseIds.Length; i++)
            {
                var id = PoseIds[i];
                if (!performance.TryGetParameterInfo(id, out var minimum, out var maximum, out var defaultValue) ||
                    !performance.TryGetParameterValue(id, out var actual))
                {
                    passed = false;
                    check.poseParameters.Add(new ParameterCheck
                    {
                        parameterId = id,
                        expected = id == selectedId ? 1f : 0f,
                        tolerance = PoseTolerance,
                        passed = false,
                    });
                    continue;
                }

                var expected = id == selectedId ? 1f : 0f;
                var parameterPassed = Mathf.Abs(actual - expected) <= PoseTolerance;
                check.poseParameters.Add(new ParameterCheck
                {
                    parameterId = id,
                    expected = expected,
                    actual = actual,
                    minimum = minimum,
                    maximum = maximum,
                    tolerance = PoseTolerance,
                    passed = parameterPassed,
                });
                passed &= parameterPassed;
            }
            return passed;
        }

        private bool ArePosesNeutral(StateSnapshot state)
        {
            if (state.poseValues == null || state.poseValues.Length != PoseIds.Length ||
                state.poseValuesAvailable == null || state.poseValuesAvailable.Length != PoseIds.Length)
                return false;
            for (var i = 0; i < state.poseValues.Length; i++)
            {
                if (!state.poseValuesAvailable[i])
                    return false;
                if (Mathf.Abs(state.poseValues[i]) > PoseTolerance)
                    return false;
            }
            return true;
        }

        private static string PoseIdForGesture(string gesture)
        {
            if (string.Equals(gesture, "wave", StringComparison.OrdinalIgnoreCase))
                return "ParamPoseWave";
            if (string.Equals(gesture, "explain", StringComparison.OrdinalIgnoreCase))
                return "ParamPoseExplain";
            if (string.Equals(gesture, "point", StringComparison.OrdinalIgnoreCase))
                return "ParamPosePoint";
            if (string.Equals(gesture, "none", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return null;
        }

        private static string GestureToEnumString(string gesture)
        {
            if (string.Equals(gesture, "wave", StringComparison.OrdinalIgnoreCase))
                return ZGesture.Wave.ToString();
            if (string.Equals(gesture, "explain", StringComparison.OrdinalIgnoreCase))
                return ZGesture.Explain.ToString();
            if (string.Equals(gesture, "point", StringComparison.OrdinalIgnoreCase))
                return ZGesture.Point.ToString();
            return ZGesture.None.ToString();
        }

        private static string GazeToEnumString(string gaze)
        {
            return string.Equals(gaze, "away", StringComparison.OrdinalIgnoreCase)
                ? ZGaze.Away.ToString()
                : ZGaze.Player.ToString();
        }

        private static ZMotionPreset ExpectedMotion(ExpressionProfile profile, string gesture)
        {
            if (profile.bodyOverride)
                return profile.bodyMotion;
            if (string.Equals(gesture, "wave", StringComparison.OrdinalIgnoreCase))
                return ZMotionPreset.Happy;
            if (string.Equals(gesture, "explain", StringComparison.OrdinalIgnoreCase))
                return ZMotionPreset.Nod;
            if (string.Equals(gesture, "point", StringComparison.OrdinalIgnoreCase))
                return ZMotionPreset.Listen;
            if (string.Equals(profile.name, "troubled", StringComparison.OrdinalIgnoreCase))
                return ZMotionPreset.Think;
            if (string.Equals(profile.name, "angry", StringComparison.OrdinalIgnoreCase))
                return ZMotionPreset.Shake;
            if (string.Equals(profile.name, "smile", StringComparison.OrdinalIgnoreCase))
                return ZMotionPreset.Happy;
            return ZMotionPreset.Nod;
        }

        private bool CheckAccent(string expected, StateSnapshot state)
        {
            if (string.IsNullOrEmpty(expected))
                return string.Equals(state.accentName, "None", StringComparison.OrdinalIgnoreCase) &&
                       state.accentIntensity <= AccentResidualMaximum;
            return string.Equals(state.accentName, expected, StringComparison.OrdinalIgnoreCase) &&
                   state.accentIntensity >= AccentVisibleMinimum && state.accentAnchorsReady;
        }

        private void CaptureScreenshots(ExpressionCheck check)
        {
            if (check == null || string.IsNullOrEmpty(outputDirectory))
                return;
            try
            {
                Directory.CreateDirectory(outputDirectory);
                check.fullScreenshotRequested = true;
                CaptureScreenRect(Path.Combine(outputDirectory, check.fullScreenshotFile),
                    new RectInt(0, 0, Screen.width, Screen.height));
                check.faceScreenshotRequested = true;
                CaptureScreenRect(Path.Combine(outputDirectory, check.faceScreenshotFile), GetFaceRect());
            }
            catch (Exception exception)
            {
                check.screenshotError = exception.GetType().Name + ": " + exception.Message;
                AddError("screenshot_failed_" + check.expression);
            }
        }

        private void CaptureScreenRect(string path, RectInt rect)
        {
            if (rect.width <= 0 || rect.height <= 0)
                throw new InvalidOperationException("face_rect_empty");
            var texture = new Texture2D(rect.width, rect.height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0, false);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Destroy(texture);
            }
        }

        private RectInt GetFaceRect()
        {
            var fallback = new RectInt(
                Mathf.RoundToInt(Screen.width * 0.25f), Mathf.RoundToInt(Screen.height * 0.20f),
                Mathf.Max(1, Mathf.RoundToInt(Screen.width * 0.50f)),
                Mathf.Max(1, Mathf.RoundToInt(Screen.height * 0.60f)));
            var camera = Camera.main;
            if (camera == null || performance == null || performance.CubismModel == null)
                return ClampRect(fallback);

            var renderers = performance.CubismModel.GetComponentsInChildren<Renderer>(true);
            Renderer face = null;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].name.IndexOf("Face_Skin", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    face = renderers[i];
                    break;
                }
            }
            if (face == null)
                return ClampRect(fallback);

            var bounds = face.bounds;
            var points = new[]
            {
                camera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.min.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.max.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.min.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.max.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.min.y, bounds.min.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.min.y, bounds.max.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.min.z)),
                camera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.max.z)),
            };
            var minX = points[0].x;
            var maxX = points[0].x;
            var minY = points[0].y;
            var maxY = points[0].y;
            for (var i = 1; i < points.Length; i++)
            {
                minX = Mathf.Min(minX, points[i].x);
                maxX = Mathf.Max(maxX, points[i].x);
                minY = Mathf.Min(minY, points[i].y);
                maxY = Mathf.Max(maxY, points[i].y);
            }
            var padX = Mathf.Max(8f, (maxX - minX) * 0.35f);
            var padY = Mathf.Max(8f, (maxY - minY) * 0.45f);
            return ClampRect(new RectInt(
                Mathf.FloorToInt(minX - padX), Mathf.FloorToInt(minY - padY),
                Mathf.CeilToInt(maxX - minX + padX * 2f),
                Mathf.CeilToInt(maxY - minY + padY * 2f)));
        }

        private static RectInt ClampRect(RectInt rect)
        {
            var x = Mathf.Clamp(rect.x, 0, Mathf.Max(0, Screen.width - 1));
            var y = Mathf.Clamp(rect.y, 0, Mathf.Max(0, Screen.height - 1));
            var right = Mathf.Clamp(rect.xMax, x + 1, Screen.width);
            var top = Mathf.Clamp(rect.yMax, y + 1, Screen.height);
            return new RectInt(x, y, Mathf.Max(1, right - x), Mathf.Max(1, top - y));
        }

        private IEnumerator RunExtendedExpressionLifecycle()
        {
            // Each new face must release its auxiliary effect and all three
            // pose parameters when the next neutral segment takes ownership.
            for (var i = 0; i < NewExpressionNames.Length; i++)
            {
                var expression = NewExpressionNames[i];
                yield return PrepareNeutral();
                yield return RunExpression(expression, GestureForNewExpression(expression),
                    false, false, false);
                yield return RunExpression("neutral", "none", false, false, false);
                yield return CheckEffectCleanup(expression + "-to-neutral");
            }

            // Switch through all four new faces without a reset. The final
            // cancel proves the previous accents/materials and hand pose did
            // not survive the ownership hand-off chain.
            yield return PrepareNeutral();
            for (var i = 0; i < NewExpressionNames.Length; i++)
            {
                var expression = NewExpressionNames[i];
                yield return RunExpression(expression, GestureForNewExpression(expression),
                    false, false, false);
            }
            performance.CancelCurrentPerformance();
            yield return CheckCleanup("new-mutual-switch");

            // Cancel and natural completion are both explicit exit paths for
            // every new chat expression.
            for (var i = 0; i < NewExpressionNames.Length; i++)
            {
                var expression = NewExpressionNames[i];
                yield return PrepareNeutral();
                yield return RunExpression(expression, GestureForNewExpression(expression),
                    false, false, false);
                yield return new WaitForSecondsRealtime(0.35f);
                performance.CancelCurrentPerformance();
                yield return CheckCleanup("new-" + expression + "-cancel");
            }

            for (var i = 0; i < NewExpressionNames.Length; i++)
            {
                var expression = NewExpressionNames[i];
                yield return PrepareNeutral();
                yield return RunExpression(expression, GestureForNewExpression(expression),
                    false, false, false);
                yield return new WaitForSecondsRealtime(NaturalWaitSeconds);
                yield return CheckCleanup("new-" + expression + "-natural");
            }

            yield return RunSighHoldCheck();
        }

        private IEnumerator RunSighHoldCheck()
        {
            yield return PrepareNeutral();
            yield return RunExpression("sigh", "explain", false, false, false);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, SighHoldSeconds - StableSampleSeconds));
            yield return new WaitForEndOfFrame();

            var beforeBodyIdle = ReadState();
            var hold = new SighHoldCheck
            {
                sampleElapsed = SighHoldSeconds,
                motionBeforeBodyIdle = beforeBodyIdle.motion,
                mouthVisibleAfter144Seconds = beforeBodyIdle.sighMouthArtWeight >= AccentVisibleMinimum,
                sighVisibleInstanceCount = beforeBodyIdle.sighVisibleInstanceCount,
                sighVisibleBeforeBodyIdle = beforeBodyIdle.sighVisible &&
                                            beforeBodyIdle.sighVisibleInstanceCount >= 3,
            };

            performance.SetMotionPreset(ZMotionPreset.Idle);
            yield return new WaitForSecondsRealtime(SighBodyIdleWaitSeconds);
            yield return new WaitForEndOfFrame();
            var afterBodyIdle = ReadState();
            var profile = FindProfile("sigh");
            var faceProbe = CreateExpressionCheck("sigh-hold", -1, "sigh", "explain",
                "player", profile, false);
            hold.bodyEndedAtIdle = string.Equals(afterBodyIdle.motion, ZMotionPreset.Idle.ToString(),
                StringComparison.Ordinal);
            hold.faceRetainedAfterBodyIdle = string.Equals(afterBodyIdle.expression,
                ZFaceExpression.Sigh.ToString(), StringComparison.Ordinal) &&
                CheckFaceProfile(faceProbe, profile);
            hold.posePassed = CheckGesturePose(faceProbe);
            hold.sighVisibleAfterBodyIdle = afterBodyIdle.sighVisible &&
                                            afterBodyIdle.sighVisibleInstanceCount >= 3 &&
                                            afterBodyIdle.sighMouthArtWeight >= AccentVisibleMinimum;
            hold.passed = hold.mouthVisibleAfter144Seconds && hold.bodyEndedAtIdle &&
                          hold.faceRetainedAfterBodyIdle && hold.posePassed &&
                          hold.sighVisibleBeforeBodyIdle && hold.sighVisibleAfterBodyIdle;
            report.sighHolds.Add(hold);
            if (!hold.passed)
                AddError("sigh_hold_failed");

            performance.CancelCurrentPerformance();
            yield return CheckCleanup("sigh-hold-cancel");
        }

        private IEnumerator CheckEffectCleanup(string operation)
        {
            yield return new WaitForEndOfFrame();
            var state = ReadState();
            var cleanup = new EffectCleanupCheck
            {
                operation = operation,
                currentExpression = state.expression,
                currentGesture = state.gesture,
                currentMotion = state.motion,
                manualPreview = state.previewPresentationActive,
                accentName = state.accentName,
                accentIntensity = state.accentIntensity,
                accentAnchorsReady = state.accentAnchorsReady,
                faceRecovered = IsNeutralFace(state),
                poseCleared = ArePosesNeutral(state),
                accentCleared = string.Equals(state.accentName, "None", StringComparison.OrdinalIgnoreCase) &&
                                state.accentIntensity <= AccentResidualMaximum && !state.accentAnchorsReady,
                sighMaterialsRestored = state.sighMaterialsRestored,
            };
            cleanup.effectsCleared = cleanup.accentCleared && cleanup.sighMaterialsRestored;
            cleanup.passed = cleanup.currentExpression == ZFaceExpression.Neutral.ToString() &&
                             !cleanup.manualPreview && cleanup.faceRecovered && cleanup.poseCleared &&
                             cleanup.effectsCleared;
            report.effectCleanups.Add(cleanup);
            if (!cleanup.passed)
                AddError("effect_cleanup_failed_" + Normalize(operation));
        }

        private IEnumerator RunFixture()
        {
            report.fixtureRequested = !string.IsNullOrWhiteSpace(fixturePath);
            if (!report.fixtureRequested)
            {
                report.fixturePassed = true;
                yield break;
            }
            if (!Path.IsPathRooted(fixturePath))
            {
                AddError("fixture_path_not_absolute");
                yield break;
            }

            string json;
            try
            {
                json = File.ReadAllText(fixturePath);
            }
            catch (Exception exception)
            {
                AddError("fixture_read_failed_" + exception.GetType().Name);
                yield break;
            }

            DialogueClient.ChatResponse response;
            try
            {
                response = JsonUtility.FromJson<DialogueClient.ChatResponse>(json);
            }
            catch (Exception exception)
            {
                AddError("fixture_json_failed_" + exception.GetType().Name);
                yield break;
            }

            if (response == null || response.segments == null)
            {
                AddError("fixture_segments_missing");
                yield break;
            }
            report.fixtureLoaded = true;
            report.fixtureExpectedCount = response.segments.Length;
            for (var i = 0; i < response.segments.Length; i++)
            {
                var segment = response.segments[i];
                var profile = segment == null ? null : FindProfile(segment.expression);
                var check = CreateExpressionCheck("fixture", i,
                    segment == null ? string.Empty : segment.expression,
                    segment == null ? string.Empty : segment.gesture,
                    segment == null ? string.Empty : segment.gaze,
                    profile, true);
                check.jsonParsed = segment != null &&
                                   !string.IsNullOrEmpty(segment.expression) &&
                                   !string.IsNullOrEmpty(segment.gesture);
                report.fixtureChecks.Add(check);
                if (!check.jsonParsed)
                {
                    AddError("fixture_segment_invalid_" + i.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                yield return PrepareNeutral();
                var turnId = (string.IsNullOrEmpty(response.turn_id)
                    ? "synthetic-provider-cases" : response.turn_id) + "-fixture-" + i;
                yield return PlayAndEvaluate(check, turnId, segment, profile, true);
            }
            report.fixturePassed = report.fixtureChecks.Count == report.fixtureExpectedCount;
            for (var i = 0; i < report.fixtureChecks.Count; i++)
                report.fixturePassed &= report.fixtureChecks[i].passed;
            if (!report.fixturePassed)
                AddError("fixture_cases_failed");
        }

        private static string GestureForNewExpression(string expression)
        {
            if (string.Equals(expression, "cry", StringComparison.OrdinalIgnoreCase))
                return "wave";
            if (string.Equals(expression, "gloom", StringComparison.OrdinalIgnoreCase))
                return "point";
            return "explain";
        }

        private IEnumerator RunCleanupTest(string operation, string expression, string gesture,
            bool usePanelCancel)
        {
            yield return PrepareNeutral();
            var response = ParseSyntheticResponse(expression, gesture, "chat-" + operation);
            if (response != null)
                performance.PlaySegments(response.turn_id, response.segments);
            yield return new WaitForSecondsRealtime(0.35f);
            if (usePanelCancel && panel != null)
                panel.CancelCurrentTurn();
            else
                performance.CancelCurrentPerformance();
            yield return CheckCleanup(operation);
        }

        private IEnumerator CheckCleanup(string operation)
        {
            yield return new WaitForSecondsRealtime(RecoverySeconds);
            yield return new WaitForEndOfFrame();
            var state = ReadState();
            var cleanup = new CleanupCheck
            {
                operation = operation,
                currentExpression = state.expression,
                currentMotion = state.motion,
                manualPreview = state.previewPresentationActive,
                accentName = state.accentName,
                accentIntensity = state.accentIntensity,
                accentAnchorsReady = state.accentAnchorsReady,
                faceRecovered = IsNeutralFace(state),
                accentCleared = string.Equals(state.accentName, "None", StringComparison.OrdinalIgnoreCase) &&
                                state.accentIntensity <= AccentResidualMaximum && !state.accentAnchorsReady,
                poseCleared = ArePosesNeutral(state),
                sighMaterialsRestored = state.sighMaterialsRestored,
            };
            cleanup.effectsCleared = cleanup.accentCleared && cleanup.sighMaterialsRestored;
            cleanup.passed = cleanup.currentExpression == ZFaceExpression.Neutral.ToString() &&
                             cleanup.currentMotion == ZMotionPreset.Idle.ToString() &&
                             !cleanup.manualPreview && cleanup.faceRecovered && cleanup.accentCleared &&
                             cleanup.poseCleared && cleanup.effectsCleared;
            report.cleanups.Add(cleanup);
            if (!cleanup.passed)
                AddError("cleanup_failed_" + operation);
        }

        private StateSnapshot ReadState()
        {
            var state = new StateSnapshot
            {
                expression = performance == null ? string.Empty : performance.CurrentExpression.ToString(),
                gesture = performance == null ? string.Empty : performance.CurrentGesture.ToString(),
                gaze = performance == null ? string.Empty : performance.CurrentGaze.ToString(),
                motion = performance == null ? string.Empty : performance.CurrentMotion.ToString(),
                motionElapsed = performance == null ? 0f : performance.MotionElapsed,
                previewPresentationActive = performance != null && performance.IsPreviewPresentationActive,
                accentName = accents == null ? "None" : accents.ActiveAccent,
                accentIntensity = accents == null ? 0f : accents.VisibleIntensity,
                accentAnchorsReady = accents != null && accents.AnchorsReady,
                tearsStartAtOuterCorners = accents != null && accents.TearsStartAtOuterCorners,
                sighArtFound = sighArt != null,
                sighArtReady = sighArt != null && sighArt.IsSighReady,
                sighFaceActive = sighArt != null && sighArt.IsSighReady && performance != null &&
                                 performance.SighFaceActive,
                sighMouthArtWeight = performance == null ? 0f : performance.SighMouthArtWeight,
                sighVisible = sighArt != null && sighArt.IsSighVisible,
                sighVisibleInstanceCount = sighArt == null ? 0 : sighArt.SighVisibleInstanceCount,
                sighMaterialsRestored = sighArt == null || sighArt.MaterialsRestored,
                poseValues = new float[PoseIds.Length],
                poseValuesAvailable = new bool[PoseIds.Length],
            };
            if (performance != null)
            {
                performance.TryGetParameterValue("ParamEyeLOpen", out state.eyeLOpen);
                performance.TryGetParameterValue("ParamEyeROpen", out state.eyeROpen);
                for (var i = 0; i < PoseIds.Length; i++)
                    state.poseValuesAvailable[i] = performance.TryGetParameterValue(PoseIds[i],
                        out state.poseValues[i]);
            }
            return state;
        }

        private bool IsNeutralFace(StateSnapshot state)
        {
            for (var i = 0; i < FaceIds.Length; i++)
            {
                if (!performance.TryGetParameterInfo(FaceIds[i], out var minimum, out var maximum, out var defaultValue) ||
                    !performance.TryGetParameterValue(FaceIds[i], out var actual))
                    return false;
                var tolerance = Mathf.Max(FaceToleranceMinimum, (maximum - minimum) * FaceToleranceRatio);
                if (Mathf.Abs(actual - defaultValue) > tolerance)
                    return false;
            }
            return state.eyeLOpen >= 1f - EyeTolerance && state.eyeROpen >= 1f - EyeTolerance;
        }

        private void RecordTransition(string name, ExpressionCheck from, ExpressionCheck to)
        {
            var transition = new TransitionCheck
            {
                name = name,
                fromExpression = from == null ? string.Empty : from.expression,
                toExpression = to == null ? string.Empty : to.expression,
                fromPassed = from != null && from.passed,
                toPassed = to != null && to.passed,
                previewFalse = to != null && to.manualFalse,
                faceChanged = FacesDiffer(from, to),
                accentPassed = to != null && to.accentPassed,
            };
            transition.passed = transition.fromPassed && transition.toPassed && transition.previewFalse &&
                                transition.faceChanged && transition.accentPassed;
            report.transitions.Add(transition);
            if (!transition.passed)
                AddError("transition_failed_" + Normalize(name));
        }

        private static bool FacesDiffer(ExpressionCheck from, ExpressionCheck to)
        {
            if (from == null || to == null || from.parameters == null || to.parameters == null)
                return false;
            for (var i = 0; i < from.parameters.Count; i++)
            {
                var left = from.parameters[i];
                for (var j = 0; j < to.parameters.Count; j++)
                {
                    var right = to.parameters[j];
                    if (left.parameterId == right.parameterId && Mathf.Abs(left.actual - right.actual) > left.tolerance)
                        return true;
                }
            }
            return false;
        }

        private void CaptureAndDisableEventSystems()
        {
            if (inputIsolationAttempted)
                return;
            inputIsolationAttempted = true;
            report.inputIsolationApplied = true;
            try
            {
                var seen = new HashSet<EventSystem>();
                var systems = Resources.FindObjectsOfTypeAll<EventSystem>();
                for (var i = 0; i < systems.Length; i++)
                {
                    var system = systems[i];
                    if (system == null || !system.gameObject.scene.IsValid() || !seen.Add(system))
                        continue;
                    eventSystemStates.Add(new EventSystemState { system = system, enabled = system.enabled });
                    if (system.enabled)
                        system.enabled = false;
                }
                report.inputIsolationActive = eventSystemStates.Count > 0;
                report.inputIsolationEventSystemCount = eventSystemStates.Count;
            }
            catch (Exception exception)
            {
                AddError("input_isolation_exception_" + exception.GetType().Name);
                RestoreEventSystems();
            }
        }

        private void RestoreEventSystems()
        {
            for (var i = eventSystemStates.Count - 1; i >= 0; i--)
            {
                var state = eventSystemStates[i];
                if (state.system == null)
                    continue;
                try
                {
                    state.system.enabled = state.enabled;
                }
                catch (Exception exception)
                {
                    AddError("input_restore_exception_" + exception.GetType().Name);
                }
            }
            eventSystemStates.Clear();
            if (report != null)
            {
                report.inputIsolationActive = false;
                report.inputIsolationRestored = inputIsolationAttempted;
            }
        }

        private void CaptureScreenshotsPresence()
        {
            if (string.IsNullOrEmpty(outputDirectory))
                return;
            MarkScreenshotPresence(report.expressions);
            MarkScreenshotPresence(report.fixtureChecks);
        }

        private void MarkScreenshotPresence(List<ExpressionCheck> checks)
        {
            if (checks == null)
                return;
            for (var i = 0; i < checks.Count; i++)
            {
                var check = checks[i];
                check.fullScreenshotPresent = check.fullScreenshotRequested &&
                    File.Exists(Path.Combine(outputDirectory, check.fullScreenshotFile));
                check.faceScreenshotPresent = check.faceScreenshotRequested &&
                    File.Exists(Path.Combine(outputDirectory, check.faceScreenshotFile));
            }
        }

        private void FinalizeReport()
        {
            if (report == null || report.reportWritten)
                return;
            report.reportWritten = true;
            try
            {
                if (!report.bodyCompleted)
                    AddError("runtime_exception_or_abort");
                report.finishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                CaptureScreenshotsPresence();
                report.expressionsPassed = report.expressions.Count == Profiles.Length * Gestures.Length;
                for (var i = 0; i < report.expressions.Count; i++)
                    report.expressionsPassed &= report.expressions[i].passed &&
                                               (!report.expressions[i].screenshotsRequired ||
                                                (report.expressions[i].fullScreenshotPresent &&
                                                 report.expressions[i].faceScreenshotPresent));
                report.transitionsPassed = report.transitions.Count == 3;
                for (var i = 0; i < report.transitions.Count; i++)
                    report.transitionsPassed &= report.transitions[i].passed;
                report.cleanupsPassed = report.cleanups.Count >= 3;
                for (var i = 0; i < report.cleanups.Count; i++)
                    report.cleanupsPassed &= report.cleanups[i].passed;
                report.effectCleanupsPassed = report.effectCleanups.Count == NewExpressionNames.Length;
                for (var i = 0; i < report.effectCleanups.Count; i++)
                    report.effectCleanupsPassed &= report.effectCleanups[i].passed;
                report.sighHoldsPassed = report.sighHolds.Count == 1;
                for (var i = 0; i < report.sighHolds.Count; i++)
                    report.sighHoldsPassed &= report.sighHolds[i].passed;
                if (report.fixtureRequested)
                {
                    report.fixturePassed = report.fixtureLoaded &&
                                           report.fixtureChecks.Count == report.fixtureExpectedCount;
                    for (var i = 0; i < report.fixtureChecks.Count; i++)
                        report.fixturePassed &= report.fixtureChecks[i].passed &&
                                                report.fixtureChecks[i].fullScreenshotPresent &&
                                                report.fixtureChecks[i].faceScreenshotPresent;
                }
                else
                    report.fixturePassed = true;
                report.passed = report.bodyCompleted && report.panelFound && report.controllerFound &&
                                report.controllerResolvedViaPanel && report.accentsFound &&
                                report.inputIsolationRestored && report.expressionsPassed &&
                                report.transitionsPassed && report.cleanupsPassed &&
                                report.effectCleanupsPassed && report.sighHoldsPassed &&
                                report.fixturePassed && report.errors.Count == 0;
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "z-chat-face-qa.json"),
                    JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    File.WriteAllText(Path.Combine(outputDirectory, "z-chat-face-qa.failed.txt"),
                        exception.GetType().Name + ": " + exception.Message);
                }
                catch (Exception)
                {
                    // Keep the original failure visible in the Unity log.
                }
            }
        }

        private ExpressionProfile FindProfile(string name)
        {
            for (var i = 0; i < Profiles.Length; i++)
            {
                if (string.Equals(Profiles[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return Profiles[i];
            }
            return null;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static string NameToEnumString(string expression)
        {
            switch ((expression ?? string.Empty).ToLowerInvariant())
            {
                case "smile": return ZFaceExpression.Smile.ToString();
                case "surprise": return ZFaceExpression.Surprise.ToString();
                case "angry": return ZFaceExpression.Angry.ToString();
                case "troubled": return ZFaceExpression.Troubled.ToString();
                case "blush": return ZFaceExpression.Blush.ToString();
                case "cry": return ZFaceExpression.Cry.ToString();
                case "bawl": return ZFaceExpression.Bawl.ToString();
                case "sigh": return ZFaceExpression.Sigh.ToString();
                case "gloom": return ZFaceExpression.Gloom.ToString();
                default: return ZFaceExpression.Neutral.ToString();
            }
        }

        private void AddError(string error)
        {
            if (report == null || string.IsNullOrEmpty(error) || report.errors.Contains(error))
                return;
            report.errors.Add(error);
        }

        [Serializable]
        private sealed class ExpressionProfile
        {
            public readonly string name;
            public readonly ZFaceExpression expectedExpression;
            public readonly float eyeOpen;
            public readonly string accent;
            public readonly ZMotionPreset bodyMotion;
            public readonly bool bodyOverride;
            public readonly bool bodyMotionRequired;
            public readonly bool tearAnchorsRequired;
            public readonly bool sighArtRequired;
            public readonly FaceValue[] faceValues;

            public ExpressionProfile(string name, ZFaceExpression expectedExpression, float eyeOpen,
                string accent, ZMotionPreset bodyMotion, bool bodyOverride, bool bodyMotionRequired,
                bool tearAnchorsRequired, bool sighArtRequired, FaceValue[] faceValues)
            {
                this.name = name;
                this.expectedExpression = expectedExpression;
                this.eyeOpen = eyeOpen;
                this.accent = accent;
                this.bodyMotion = bodyMotion;
                this.bodyOverride = bodyOverride;
                this.bodyMotionRequired = bodyMotionRequired;
                this.tearAnchorsRequired = tearAnchorsRequired;
                this.sighArtRequired = sighArtRequired;
                this.faceValues = faceValues;
            }
        }

        [Serializable]
        private struct FaceValue
        {
            public readonly string id;
            public readonly float value;

            public FaceValue(string id, float value)
            {
                this.id = id;
                this.value = value;
            }
        }

        [Serializable]
        private sealed class Report
        {
            public string runId;
            public string startedUtc;
            public string finishedUtc;
            public string outputDirectory;
            public bool noNetworkRequested;
            public bool bodyCompleted;
            public bool reportWritten;
            public bool passed;
            public bool panelFound;
            public bool controllerFound;
            public bool controllerResolvedViaPanel;
            public bool accentsFound;
            public bool sighArtFound;
            public bool inputIsolationRequested;
            public bool inputIsolationApplied;
            public bool inputIsolationActive;
            public bool inputIsolationRestored;
            public int inputIsolationEventSystemCount;
            public bool expressionsPassed;
            public bool transitionsPassed;
            public bool cleanupsPassed;
            public bool effectCleanupsPassed;
            public bool sighHoldsPassed;
            public bool fixtureRequested;
            public bool fixtureLoaded;
            public bool fixturePassed;
            public string fixturePath;
            public int fixtureExpectedCount;
            public StateSnapshot baseline;
            public List<ExpressionCheck> expressions;
            public List<TransitionCheck> transitions;
            public List<CleanupCheck> cleanups;
            public List<EffectCleanupCheck> effectCleanups;
            public List<SighHoldCheck> sighHolds;
            public List<ExpressionCheck> fixtureChecks;
            public List<string> errors;
        }

        [Serializable]
        private sealed class ExpressionCheck
        {
            public string source;
            public int fixtureIndex;
            public string turnId;
            public string expression;
            public string gesture;
            public string expectedExpression;
            public string expectedGesture;
            public string expectedGaze;
            public string expectedMotion;
            public string currentExpression;
            public string currentGesture;
            public string currentGaze;
            public string currentMotion;
            public string expectedAccent;
            public string accentName;
            public float accentIntensity;
            public bool accentAnchorsReady;
            public bool jsonParsed;
            public bool played;
            public bool statePassed;
            public bool facePassed;
            public bool eyePassed;
            public bool bodyMotionPassed;
            public bool manualFalse;
            public bool accentPassed;
            public bool tearAnchorsPassed;
            public bool sighArtPassed;
            public bool effectPassed;
            public bool posePassed;
            public bool screenshotPassed;
            public bool screenshotsRequired;
            public bool passed;
            public float sampleElapsed;
            public float motionElapsed;
            public float eyeExpected;
            public float eyeLOpen;
            public float eyeROpen;
            public float headPeakNormalized;
            public float bodyPeakNormalized;
            public string fullScreenshotFile;
            public string faceScreenshotFile;
            public bool fullScreenshotRequested;
            public bool fullScreenshotPresent;
            public bool faceScreenshotRequested;
            public bool faceScreenshotPresent;
            public string screenshotError;
            public List<ParameterCheck> parameters;
            public List<ParameterCheck> poseParameters;
        }

        [Serializable]
        private sealed class ParameterCheck
        {
            public string parameterId;
            public float expected;
            public float actual;
            public float minimum;
            public float maximum;
            public float tolerance;
            public bool passed;
        }

        [Serializable]
        private sealed class TransitionCheck
        {
            public string name;
            public string fromExpression;
            public string toExpression;
            public bool fromPassed;
            public bool toPassed;
            public bool previewFalse;
            public bool faceChanged;
            public bool accentPassed;
            public bool passed;
        }

        [Serializable]
        private sealed class CleanupCheck
        {
            public string operation;
            public string currentExpression;
            public string currentMotion;
            public bool manualPreview;
            public string accentName;
            public float accentIntensity;
            public bool accentAnchorsReady;
            public bool faceRecovered;
            public bool accentCleared;
            public bool poseCleared;
            public bool sighMaterialsRestored;
            public bool effectsCleared;
            public bool passed;
        }

        [Serializable]
        private sealed class EffectCleanupCheck
        {
            public string operation;
            public string currentExpression;
            public string currentGesture;
            public string currentMotion;
            public bool manualPreview;
            public string accentName;
            public float accentIntensity;
            public bool accentAnchorsReady;
            public bool faceRecovered;
            public bool accentCleared;
            public bool sighMaterialsRestored;
            public bool effectsCleared;
            public bool poseCleared;
            public bool passed;
        }

        [Serializable]
        private sealed class SighHoldCheck
        {
            public float sampleElapsed;
            public string motionBeforeBodyIdle;
            public int sighVisibleInstanceCount;
            public bool mouthVisibleAfter144Seconds;
            public bool sighVisibleBeforeBodyIdle;
            public bool bodyEndedAtIdle;
            public bool faceRetainedAfterBodyIdle;
            public bool posePassed;
            public bool sighVisibleAfterBodyIdle;
            public bool passed;
        }

        [Serializable]
        private struct StateSnapshot
        {
            public string expression;
            public string gesture;
            public string gaze;
            public string motion;
            public float motionElapsed;
            public bool previewPresentationActive;
            public float eyeLOpen;
            public float eyeROpen;
            public string accentName;
            public float accentIntensity;
            public bool accentAnchorsReady;
            public bool tearsStartAtOuterCorners;
            public bool sighArtFound;
            public bool sighArtReady;
            public bool sighFaceActive;
            public float sighMouthArtWeight;
            public bool sighVisible;
            public int sighVisibleInstanceCount;
            public bool sighMaterialsRestored;
            public float[] poseValues;
            public bool[] poseValuesAvailable;
        }
    }
}
