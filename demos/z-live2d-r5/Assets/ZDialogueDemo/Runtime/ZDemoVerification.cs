using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace ZDialogueDemo
{
    /// <summary>
    /// Opt-in runtime QA for the Unity dialogue demo.
    ///
    /// Normal player runs do nothing. Enable explicitly with:
    /// --z-demo-verify=/absolute/output/directory
    /// Optional dialogue check:
    /// --z-demo-chat=测试文字
    /// Optional editor/build shutdown:
    /// --z-demo-quit
    /// </summary>
    public sealed class ZDemoVerification : MonoBehaviour
    {
        private const string VerifyArgument = "--z-demo-verify=";
        private const string ChatArgument = "--z-demo-chat=";
        private const string QuitArgument = "--z-demo-quit";
        private const string QaSaveId = "qa-runtime";
        private const float ComponentSearchTimeoutSeconds = 30f;
        private const float ModelSearchTimeoutSeconds = 30f;
        private const float SessionTimeoutSeconds = 30f;
        private const float ChatTimeoutSeconds = 150f;
        private const float TransitionSeconds = 0.8f;
        private const float ChatMotionObservationSeconds = 0.3f;
        private const float ParameterTolerance = 0.25f;
        private const int ExpectedParameterCount = 32;
        private static readonly string[] ChatMotionParameterIds =
        {
            "ParamAngleX",
            "ParamAngleY",
            "ParamAngleZ",
            "ParamBodyAngleX",
            "ParamBodyAngleY",
            "ParamBodyAngleZ",
        };

        private readonly List<string> errors = new List<string>();

        private string outputDirectory;
        private string chatText;
        private bool quitWhenDone;
        private bool enabledByCommandLine;
        private string originalSaveId;
        private bool originalSaveIdWasPersisted;
        private bool saveIdChanged;

        private DialogueClient client;
        private DialoguePanel panel;
        private ZPerformanceController performance;

        private bool waitingForSession;
        private bool sessionLoaded;
        private string sessionError;
        private DialogueClient.SessionResponse firstSession;

        private bool waitingForChat;
        private bool chatAcknowledged;
        private bool chatTimedOut;
        private string chatError;
        private string acknowledgedTurnId;
        private DialogueClient.ChatResponse observedResponse;
        private DialogueClient.AckResponse observedAck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!TryReadLaunchOptions(out var options))
                return;

            if (FindObjectOfType<ZDemoVerification>() != null)
                return;

            var gameObject = new GameObject("Z Demo Verification (opt-in)");
            DontDestroyOnLoad(gameObject);
            var verifier = gameObject.AddComponent<ZDemoVerification>();
            verifier.outputDirectory = options.outputDirectory;
            verifier.chatText = options.chatText;
            verifier.quitWhenDone = options.quitWhenDone;
            verifier.enabledByCommandLine = true;
        }

        private void Start()
        {
            if (!enabledByCommandLine)
                return;
            StartCoroutine(RunVerification());
        }

        private void OnDestroy()
        {
            UnsubscribeClientEvents();
        }

        private IEnumerator RunVerification()
        {
            var report = CreateReport();
            var verification = RunVerificationBody(report);
            Exception bodyException = null;

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = verification.MoveNext();
                }
                catch (Exception exception)
                {
                    bodyException = exception;
                    break;
                }

                if (!hasNext)
                    break;
                yield return verification.Current;
            }

            if (bodyException != null)
                AddError(report, "runtime_exception_" + bodyException.GetType().Name);

            FinalizeReport(report);

            if (quitWhenDone)
            {
                yield return null;
                Application.Quit();
            }
        }

        private VerificationReport CreateReport()
        {
            var report = new VerificationReport
            {
                schemaVersion = "z-demo-verification-v1",
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                outputDirectory = outputDirectory,
                session = new SessionCheck
                {
                    requestedSaveId = QaSaveId,
                    required = !string.IsNullOrEmpty(chatText),
                },
                chat = new ChatCheck { requested = !string.IsNullOrEmpty(chatText) },
                screenshots = new ScreenshotCheck(),
                parameters = new ParameterCheck(),
                model = new ModelCheck(),
                chatMotion = new ChatMotionCheck(),
            };
            if (!report.chat.requested)
            {
                report.chat.skipped = true;
                report.chat.skipReason = "not_requested";
            }
            return report;
        }

        private IEnumerator RunVerificationBody(VerificationReport report)
        {
            PrepareOutputDirectory();
            yield return FindSceneComponents();
            SubscribeClientEvents();

            if (client == null)
                AddError(report, "missing_dialogue_client");
            if (panel == null)
                AddError(report, "missing_dialogue_panel");
            if (performance == null)
                AddError(report, "missing_z_performance_controller");

            yield return VerifyModelAndParameters(report);
            yield return VerifyPerformance(report);
            yield return VerifySession(report);

            if (report.chat.requested)
                yield return VerifyChat(report);
        }

        private void FinalizeReport(VerificationReport report)
        {
            UnsubscribeClientEvents();
            RestoreSaveId();
            RefreshScreenshotPresence(report.screenshots);
            report.finishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.modelPassed = CalculateModelPass(report);
            report.chatPassed = CalculateChatPass(report);
            report.passed = report.modelPassed && report.chatPassed;
            WriteReport(report);
        }

        private IEnumerator FindSceneComponents()
        {
            yield return null;
            var deadline = Time.realtimeSinceStartup + ComponentSearchTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (client == null)
                    client = FindObjectOfType<DialogueClient>();
                if (panel == null)
                    panel = FindObjectOfType<DialoguePanel>();
                if (performance == null && panel != null)
                    performance = panel.PerformanceController;
                if (performance == null)
                    performance = FindObjectOfType<ZPerformanceController>();

                if (client == null && panel != null)
                    client = panel.Client;
                if (client != null && panel != null && performance != null)
                    yield break;

                yield return null;
            }
        }

        private IEnumerator VerifyModelAndParameters(VerificationReport report)
        {
            if (performance == null)
                yield break;

            var modelDeadline = Time.realtimeSinceStartup + ModelSearchTimeoutSeconds;
            while (!performance.IsModelReady && Time.realtimeSinceStartup < modelDeadline)
                yield return null;

            report.model.ready = performance.IsModelReady;
            report.model.hasUpdateController = performance.HasCubismUpdateController;
            if (!report.model.ready)
                AddError(report, "cubism_model_not_ready");

            var descriptors = ZPerformanceController.ParameterWhitelist;
            report.parameters.expectedCount = ExpectedParameterCount;
            report.parameters.whitelistCount = descriptors == null ? 0 : descriptors.Count;
            if (descriptors == null)
            {
                AddError(report, "parameter_whitelist_missing");
                report.parameters.passed = false;
                yield break;
            }

            for (var i = 0; i < descriptors.Count; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor == null || string.IsNullOrEmpty(descriptor.id) || !performance.IsParameterAvailable(descriptor.id))
                {
                    report.parameters.missingIds.Add(descriptor == null ? "<null>" : descriptor.id);
                }
            }

            report.parameters.availableCount = report.parameters.whitelistCount - report.parameters.missingIds.Count;
            report.parameters.passed = report.model.ready &&
                                        report.parameters.whitelistCount == ExpectedParameterCount &&
                                        report.parameters.missingIds.Count == 0;
            if (!report.parameters.passed)
                AddError(report, "parameter_whitelist_incomplete");
        }

        private IEnumerator VerifyPerformance(VerificationReport report)
        {
            if (performance == null || !performance.IsModelReady)
                yield break;

            performance.SetExpression("neutral");
            performance.SetGesture("none");
            yield return new WaitForSecondsRealtime(TransitionSeconds);
            yield return new WaitForEndOfFrame();
            CaptureScreenshot(report.screenshots, "neutral.png", true);

            var checks = new[]
            {
                new PerformanceCase("smile", "wave", "ParamEyeSmile", "ParamPoseWave", 1f, 1f),
                new PerformanceCase("angry", "explain", "ParamBrowForm", "ParamPoseExplain", -1f, 1f),
                new PerformanceCase("blush", "point", "ParamCheek", "ParamPosePoint", 1f, 1f),
            };

            for (var i = 0; i < checks.Length; i++)
            {
                var check = checks[i];
                performance.SetExpression(check.expression);
                performance.SetGesture(check.gesture);
                yield return new WaitForSecondsRealtime(TransitionSeconds);
                yield return new WaitForEndOfFrame();

                var expressionFound = performance.TryGetParameterValue(check.expressionParameter, out var expressionValue);
                var gestureFound = performance.TryGetParameterValue(check.gestureParameter, out var gestureValue);
                var result = new PerformanceCheck
                {
                    expression = check.expression,
                    gesture = check.gesture,
                    expressionParameter = check.expressionParameter,
                    gestureParameter = check.gestureParameter,
                    expressionTarget = check.expressionTarget,
                    gestureTarget = check.gestureTarget,
                    expressionFound = expressionFound,
                    gestureFound = gestureFound,
                    expressionValue = expressionValue,
                    gestureValue = gestureValue,
                };
                result.passed = result.expressionFound && result.gestureFound &&
                                Mathf.Abs(result.expressionValue - result.expressionTarget) <= ParameterTolerance &&
                                Mathf.Abs(result.gestureValue - result.gestureTarget) <= ParameterTolerance;
                report.performance.Add(result);
                if (!result.passed)
                    AddError(report, "performance_check_failed_" + check.expression + "_" + check.gesture);

                if (i == 0)
                {
                    yield return new WaitForEndOfFrame();
                    CaptureScreenshot(report.screenshots, "smile-wave.png", true);
                }
            }

            performance.SetExpression("neutral");
            performance.SetGesture("none");
        }

        private IEnumerator VerifySession(VerificationReport report)
        {
            if (client == null)
            {
                report.session.error = "missing_dialogue_client";
                yield break;
            }

            waitingForSession = true;
            sessionLoaded = false;
            sessionError = null;
            originalSaveId = client.SaveId;
            originalSaveIdWasPersisted = PlayerPrefs.HasKey("ZDialogueDemo.SaveId");
            saveIdChanged = !string.Equals(originalSaveId, QaSaveId, StringComparison.Ordinal);
            client.SetSaveId(QaSaveId);
            client.LoadSession();

            var deadline = Time.realtimeSinceStartup + SessionTimeoutSeconds;
            while (!sessionLoaded && string.IsNullOrEmpty(sessionError) && Time.realtimeSinceStartup < deadline)
                yield return null;
            waitingForSession = false;

            report.session.loaded = sessionLoaded;
            report.session.saveId = client.SaveId;
            report.session.responseSaveId = firstSession == null ? string.Empty : firstSession.save_id;
            report.session.memoryCount = firstSession == null || firstSession.memories == null ? 0 : firstSession.memories.Length;
            report.session.error = sessionError;
            if (!sessionLoaded)
            {
                if (string.IsNullOrEmpty(sessionError))
                {
                    report.session.error = "session_timeout";
                    AddError(report, "session_timeout");
                }
                else
                {
                    AddError(report, "session_load_failed");
                }
            }
        }

        private IEnumerator VerifyChat(VerificationReport report)
        {
            if (client == null || panel == null || panel.InputField == null)
            {
                report.chat.error = "missing_chat_panel_or_input";
                report.chat.skipped = true;
                report.chat.skipReason = "missing_chat_panel_or_input";
                AddError(report, "missing_chat_panel_or_input");
                yield break;
            }
            if (!report.session.loaded)
            {
                report.chat.error = "session_not_loaded";
                report.chat.skipped = true;
                report.chat.skipReason = "session_not_loaded";
                AddError(report, "chat_skipped_session_not_loaded");
                yield break;
            }

            waitingForChat = true;
            chatAcknowledged = false;
            chatTimedOut = false;
            chatError = null;
            acknowledgedTurnId = null;
            observedResponse = null;
            observedAck = null;

            panel.InputField.text = chatText;
            panel.SendFromInput();
            report.chat.attempted = true;

            var deadline = Time.realtimeSinceStartup + ChatTimeoutSeconds;
            while (!chatAcknowledged && string.IsNullOrEmpty(chatError) && Time.realtimeSinceStartup < deadline)
                yield return null;
            waitingForChat = false;

            if (!chatAcknowledged && string.IsNullOrEmpty(chatError))
            {
                chatTimedOut = true;
                chatError = "chat_timeout";
                client.CancelCurrentTurn();
            }

            report.chat.completed = chatAcknowledged;
            report.chat.timedOut = chatTimedOut;
            report.chat.turnId = acknowledgedTurnId;
            report.chat.memoryCount = observedAck == null ? client.LastMemoryCount : observedAck.memory_count;
            report.chat.saveId = client.SaveId;
            report.chat.lastResponse = SnapshotResponse(observedResponse ?? client.LastResponse);
            report.chat.error = chatError;
            if (!report.chat.completed)
                AddError(report, "chat_failed");
            else
            {
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                yield return VerifyChatMotion(report.chatMotion);
                if (!report.chatMotion.passed)
                    AddError(report, "chat_motion_failed");
                CaptureScreenshot(report.screenshots, "chat.png", true);
            }
        }

        private IEnumerator VerifyChatMotion(ChatMotionCheck result)
        {
            if (result == null)
                yield break;

            result.requested = true;
            if (performance == null || observedResponse == null)
            {
                result.error = "chat_response_or_performance_missing";
                yield break;
            }

            // ChatTurnReady 已先经过 DialoguePanel.OnChatTurnReady；此时再取值，
            // 避免在事件顺序上误判 PlaySegments 尚未调用。
            result.motionAtTrigger = performance.CurrentMotion.ToString();
            result.triggeredAtSeconds = Time.unscaledTime;
            CaptureChatMotionValues(result.before, performance);
            yield return new WaitForSecondsRealtime(ChatMotionObservationSeconds);
            yield return new WaitForEndOfFrame();
            result.motionAfterDelay = performance.CurrentMotion.ToString();
            result.observedAfterSeconds = Time.unscaledTime;
            CaptureChatMotionValues(result.after, performance);

            result.nonIdle = !string.Equals(result.motionAtTrigger, ZMotionPreset.Idle.ToString(),
                StringComparison.Ordinal) &&
                             !string.Equals(result.motionAfterDelay, ZMotionPreset.Idle.ToString(),
                                 StringComparison.Ordinal);
            result.allInRange = true;
            result.continuousParameterChanged = false;
            for (var i = 0; i < result.after.Count; i++)
            {
                var after = result.after[i];
                if (!after.inRange)
                    result.allInRange = false;
                if (Mathf.Abs(after.value - result.before[i].value) > 0.001f)
                    result.continuousParameterChanged = true;
            }

            result.passed = result.nonIdle && result.allInRange && result.continuousParameterChanged;
            if (!result.passed)
                AddError("chat_motion_failed");
        }

        private static void CaptureChatMotionValues(List<ChatMotionParameterValue> destination,
            ZPerformanceController controller)
        {
            destination.Clear();
            for (var i = 0; i < ChatMotionParameterIds.Length; i++)
            {
                var valueFound = controller.TryGetParameterValue(ChatMotionParameterIds[i], out var value);
                var infoFound = controller.TryGetParameterInfo(ChatMotionParameterIds[i], out var minimum,
                    out var maximum, out var defaultValue);
                destination.Add(new ChatMotionParameterValue
                {
                    id = ChatMotionParameterIds[i],
                    value = valueFound ? value : 0f,
                    minimum = infoFound ? minimum : 0f,
                    maximum = infoFound ? maximum : 0f,
                    defaultValue = infoFound ? defaultValue : 0f,
                    inRange = valueFound && infoFound && value >= minimum - 0.001f && value <= maximum + 0.001f,
                });
            }
        }

        private void CaptureScreenshot(ScreenshotCheck screenshots, string fileName, bool requested)
        {
            if (screenshots == null || !requested)
                return;

            var path = Path.Combine(outputDirectory, fileName);
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                if (fileName == "neutral.png")
                {
                    screenshots.neutralRequested = true;
                    screenshots.neutralFile = fileName;
                    screenshots.neutralExists = File.Exists(path);
                }
                else if (fileName == "smile-wave.png")
                {
                    screenshots.smileWaveRequested = true;
                    screenshots.smileWaveFile = fileName;
                    screenshots.smileWaveExists = File.Exists(path);
                }
                else if (fileName == "chat.png")
                {
                    screenshots.chatRequested = true;
                    screenshots.chatFile = fileName;
                    screenshots.chatExists = File.Exists(path);
                }
            }
            catch (Exception exception)
            {
                AddError("screenshot_failed_" + exception.GetType().Name);
            }
        }

        private void RefreshScreenshotPresence(ScreenshotCheck screenshots)
        {
            if (screenshots == null)
                return;
            if (screenshots.neutralRequested)
                screenshots.neutralExists = File.Exists(Path.Combine(outputDirectory, screenshots.neutralFile));
            if (screenshots.smileWaveRequested)
                screenshots.smileWaveExists = File.Exists(Path.Combine(outputDirectory, screenshots.smileWaveFile));
            if (screenshots.chatRequested)
                screenshots.chatExists = File.Exists(Path.Combine(outputDirectory, screenshots.chatFile));
        }

        private void RestoreSaveId()
        {
            if (!saveIdChanged || client == null || string.IsNullOrEmpty(originalSaveId))
                return;

            client.SetSaveId(originalSaveId);
            if (!originalSaveIdWasPersisted)
            {
                PlayerPrefs.DeleteKey("ZDialogueDemo.SaveId");
                PlayerPrefs.Save();
            }
            saveIdChanged = false;
        }

        private void PrepareOutputDirectory()
        {
            Directory.CreateDirectory(outputDirectory);
        }

        private void WriteReport(VerificationReport report)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var path = Path.Combine(outputDirectory, "z-demo-verification.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("[ZDemoVerification] runtime QA report written.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ZDemoVerification] runtime QA report could not be written: " + exception.GetType().Name);
            }
        }

        private bool CalculateModelPass(VerificationReport report)
        {
            if (report == null || report.model == null || !report.model.ready)
                return false;
            if (report.parameters == null || !report.parameters.passed)
                return false;
            if (report.screenshots == null || !report.screenshots.neutralRequested || !report.screenshots.neutralExists ||
                !report.screenshots.smileWaveRequested || !report.screenshots.smileWaveExists)
                return false;
            if (report.performance == null || report.performance.Count != 3)
                return false;
            for (var i = 0; i < report.performance.Count; i++)
            {
                if (!report.performance[i].passed)
                    return false;
            }
            return true;
        }

        private bool CalculateChatPass(VerificationReport report)
        {
            if (report == null || report.chat == null || !report.chat.requested)
                return true;
            return report.session != null && report.session.loaded && report.chat.completed &&
                   report.chatMotion != null && report.chatMotion.passed;
        }

        private void SubscribeClientEvents()
        {
            if (client == null)
                return;
            client.SessionLoaded += OnSessionLoaded;
            client.ChatTurnReady += OnChatTurnReady;
            client.TurnAcknowledged += OnTurnAcknowledged;
            client.RequestFailed += OnRequestFailed;
        }

        private void UnsubscribeClientEvents()
        {
            if (client == null)
                return;
            client.SessionLoaded -= OnSessionLoaded;
            client.ChatTurnReady -= OnChatTurnReady;
            client.TurnAcknowledged -= OnTurnAcknowledged;
            client.RequestFailed -= OnRequestFailed;
        }

        private void OnSessionLoaded(DialogueClient.SessionResponse session)
        {
            if (!waitingForSession || session == null)
                return;
            firstSession = session;
            sessionLoaded = true;
        }

        private void OnChatTurnReady(DialogueClient.ChatResponse response)
        {
            if (waitingForChat && response != null)
                observedResponse = response;
        }

        private void OnTurnAcknowledged(string turnId, DialogueClient.AckResponse ack)
        {
            if (!waitingForChat)
                return;
            acknowledgedTurnId = turnId;
            observedAck = ack;
            chatAcknowledged = true;
        }

        private void OnRequestFailed(string turnId, string error)
        {
            if (waitingForSession)
            {
                sessionError = SafeError(error);
                return;
            }
            if (waitingForChat)
                chatError = SafeError(error);
        }

        private void AddError(VerificationReport report, string code)
        {
            AddError(code);
            if (report != null && report.errors.IndexOf(code) < 0)
                report.errors.Add(code);
        }

        private void AddError(string code)
        {
            if (!string.IsNullOrEmpty(code) && errors.IndexOf(code) < 0)
                errors.Add(code);
        }

        private static string SafeError(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "request_failed";
            var lower = value.ToLowerInvariant();
            if (lower.Contains("http://") || lower.Contains("https://") || lower.Contains("authorization") ||
                lower.Contains("bearer") || lower.Contains("api_key") || lower.Contains("token") ||
                lower.Contains("password"))
                return "request_failed_sensitive_detail_redacted";
            return value.Length > 240 ? value.Substring(0, 240) : value;
        }

        private static ResponseSnapshot SnapshotResponse(DialogueClient.ChatResponse response)
        {
            if (response == null)
                return null;

            var snapshot = new ResponseSnapshot
            {
                turnId = response.turn_id,
                provider = response.provider,
                memoryCount = response.memory_count,
            };
            if (response.segments != null)
            {
                for (var i = 0; i < response.segments.Length; i++)
                {
                    var segment = response.segments[i];
                    if (segment == null)
                        continue;
                    snapshot.segments.Add(new SegmentSnapshot
                    {
                        text = segment.text,
                        expression = segment.expression,
                        gesture = segment.gesture,
                        gaze = segment.gaze,
                    });
                }
            }
            return snapshot;
        }

        private static bool TryReadLaunchOptions(out LaunchOptions options)
        {
            options = null;
            var args = Environment.GetCommandLineArgs();
            string output = null;
            string chat = null;
            var quit = false;
            for (var i = 0; i < args.Length; i++)
            {
                var argument = args[i] ?? string.Empty;
                if (argument.StartsWith(VerifyArgument, StringComparison.Ordinal))
                    output = argument.Substring(VerifyArgument.Length);
                else if (argument.StartsWith(ChatArgument, StringComparison.Ordinal))
                    chat = argument.Substring(ChatArgument.Length);
                else if (string.Equals(argument, QuitArgument, StringComparison.Ordinal))
                    quit = true;
            }

            if (string.IsNullOrWhiteSpace(output))
                return false;

            output = output.Trim();
            if (!Path.IsPathRooted(output))
            {
                Debug.LogWarning("[ZDemoVerification] --z-demo-verify requires an absolute directory.");
                return false;
            }

            try
            {
                output = Path.GetFullPath(output);
            }
            catch (Exception)
            {
                Debug.LogWarning("[ZDemoVerification] --z-demo-verify path is invalid.");
                return false;
            }

            options = new LaunchOptions
            {
                outputDirectory = output,
                chatText = chat,
                quitWhenDone = quit,
            };
            return true;
        }

        private sealed class LaunchOptions
        {
            public string outputDirectory;
            public string chatText;
            public bool quitWhenDone;
        }

        private sealed class PerformanceCase
        {
            public readonly string expression;
            public readonly string gesture;
            public readonly string expressionParameter;
            public readonly string gestureParameter;
            public readonly float expressionTarget;
            public readonly float gestureTarget;

            public PerformanceCase(string expression, string gesture, string expressionParameter,
                string gestureParameter, float expressionTarget, float gestureTarget)
            {
                this.expression = expression;
                this.gesture = gesture;
                this.expressionParameter = expressionParameter;
                this.gestureParameter = gestureParameter;
                this.expressionTarget = expressionTarget;
                this.gestureTarget = gestureTarget;
            }
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public string schemaVersion;
            public string startedUtc;
            public string finishedUtc;
            public string outputDirectory;
            public bool modelPassed;
            public bool chatPassed;
            public bool passed;
            public ModelCheck model;
            public ParameterCheck parameters;
            public List<PerformanceCheck> performance = new List<PerformanceCheck>();
            public ScreenshotCheck screenshots;
            public SessionCheck session;
            public ChatCheck chat;
            public ChatMotionCheck chatMotion;
            public List<string> errors = new List<string>();
        }

        [Serializable]
        private sealed class ModelCheck
        {
            public bool ready;
            public bool hasUpdateController;
        }

        [Serializable]
        private sealed class ParameterCheck
        {
            public int expectedCount;
            public int whitelistCount;
            public int availableCount;
            public bool passed;
            public List<string> missingIds = new List<string>();
        }

        [Serializable]
        private sealed class PerformanceCheck
        {
            public string expression;
            public string gesture;
            public string expressionParameter;
            public string gestureParameter;
            public float expressionTarget;
            public float gestureTarget;
            public bool expressionFound;
            public bool gestureFound;
            public float expressionValue;
            public float gestureValue;
            public bool passed;
        }

        [Serializable]
        private sealed class ScreenshotCheck
        {
            public bool neutralRequested;
            public bool neutralExists;
            public string neutralFile;
            public bool smileWaveRequested;
            public bool smileWaveExists;
            public string smileWaveFile;
            public bool chatRequested;
            public bool chatExists;
            public string chatFile;
        }

        [Serializable]
        private sealed class SessionCheck
        {
            public string requestedSaveId;
            public bool required;
            public bool loaded;
            public string saveId;
            public string responseSaveId;
            public int memoryCount;
            public string error;
        }

        [Serializable]
        private sealed class ChatCheck
        {
            public bool requested;
            public bool skipped;
            public string skipReason;
            public bool attempted;
            public bool completed;
            public bool timedOut;
            public string turnId;
            public int memoryCount;
            public string saveId;
            public string error;
            public ResponseSnapshot lastResponse;
        }

        [Serializable]
        private sealed class ChatMotionCheck
        {
            public bool requested;
            public string motionAtTrigger;
            public string motionAfterDelay;
            public float triggeredAtSeconds;
            public float observedAfterSeconds;
            public bool nonIdle;
            public bool allInRange;
            public bool continuousParameterChanged;
            public bool passed;
            public string error;
            public List<ChatMotionParameterValue> before = new List<ChatMotionParameterValue>();
            public List<ChatMotionParameterValue> after = new List<ChatMotionParameterValue>();
        }

        [Serializable]
        private sealed class ChatMotionParameterValue
        {
            public string id;
            public float value;
            public float minimum;
            public float maximum;
            public float defaultValue;
            public bool inRange;
        }

        [Serializable]
        private sealed class ResponseSnapshot
        {
            public string turnId;
            public string provider;
            public int memoryCount;
            public List<SegmentSnapshot> segments = new List<SegmentSnapshot>();
        }

        [Serializable]
        private sealed class SegmentSnapshot
        {
            public string text;
            public string expression;
            public string gesture;
            public string gaze;
        }
    }
}
