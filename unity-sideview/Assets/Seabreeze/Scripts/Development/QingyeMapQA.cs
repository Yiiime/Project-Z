using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>
    /// Isolated daytime-map acceptance for Qingye's real dialogue portrait.
    ///
    /// This component is inert unless the player is launched with
    /// -qingyeMapQA [outputDirectory]. The normal run must receive a real
    /// MemoryLab reply; it never injects a local line. -qaFixedSmile is a
    /// separate, explicitly labelled fixed replay for the authored smile.
    /// Both modes use the real map E interaction and the existing dialogue UI.
    /// </summary>
    public sealed class QingyeMapQA : MonoBehaviour
    {
        const string QaArgument = "-qingyeMapQA";
        const string FixedSmileArgument = "-qaFixedSmile";
        const string LocalPreviewArgument = "-localPortraitPreview";
        const string ActorId = "xiaoxiaoqing";
        const string ProfilePath = "Memory/Portraits/Qingye";
        const float TargetOffset = .12f;
        const float MouthCaptureThreshold = .20f;
        const float NativeSpeechDeltaThreshold = .18f;
        const float EyeOpenThreshold = .82f;
        const float IdleMouthTolerance = .08f;
        const float LegacyMouthDriftTolerance = .08f;
        const int StableFrames = 8;

        [Serializable]
        sealed class Report
        {
            public string timestampUtc;
            public string unityVersion;
            public string outputDirectory;
            public string actorId;
            public string map;
            public string locationId;
            public string entryPath;
            public string dialogueMode;
            public bool localPortraitPreviewArgument;
            public bool fixedSmileArgument;
            public bool passed;
            public bool mapEConfirmed;
            public bool realMemoryReply;
            public bool realReplyExpressionNeutral;
            public bool neutralEyesOpen;
            public bool neutralMouthTalk;
            public bool neutralSpeechParameterPresent;
            public bool neutralSpeechParameterMoved;
            public bool neutralSpeechReturnedToIdle;
            public bool neutralLegacyMouthStayedAtBaseline;
            public bool textRevealCompleted;
            public bool returnedToIdle;
            public bool fixedNeutralSpeechTalk;
            public bool fixedNeutralTextRevealCompleted;
            public bool fixedNeutralReturnedToIdle;
            public bool fixedNeutralLegacyMouthStayedAtBaseline;
            public bool fixedInterruptedFirstLineTalk;
            public bool fixedInterruptedSecondLineNativeSpeechTalk;
            public bool fixedInterruptedLegacyMouthStayedAtBaseline;
            public bool fixedInterruptedTextRevealCompleted;
            public bool fixedInterruptedReturnedToIdle;
            public bool fixedEscClosed;
            public bool fixedOtherActorOpened;
            public bool fixedOtherActorNoQingyeSpeechResidue;
            public bool fixedSmileChangedLiveParameter;
            public bool fixedSmileMouthTalk;
            public bool fixedSmileReturnedToIdle;
            public bool fixedSmileExpressionReturnedToIdle;
            public string question;
            public string replyText;
            public string replyExpression;
            public string modelProfile;
            public string mouthParameter;
            public string neutralSpeechParameter;
            public string leftEyeParameter;
            public string rightEyeParameter;
            public float neutralIdleMouth;
            public float neutralIdleSpeech;
            public float neutralTalkMouth;
            public float neutralTalkSpeech;
            public float neutralTalkSpeechMaxDelta;
            public float neutralLegacyMouthMaxDelta;
            public float neutralTalkLeftEye;
            public float neutralTalkRightEye;
            public float fixedNeutralTalkSpeech;
            public float fixedNeutralTalkSpeechMaxDelta;
            public float fixedNeutralLegacyMouthMaxDelta;
            public float fixedNeutralIdleSpeech;
            public float fixedNeutralIdleMouth;
            public float fixedInterruptedFirstLinePeak;
            public float fixedInterruptedSecondLineSpeechPeak;
            public float fixedInterruptedSecondLineLegacyMaxDelta;
            public string fixedOtherActorProfile;
            public float fixedSmileParameterBefore;
            public float fixedSmileParameterMaxDelta;
            public float fixedSmileIdleMouth;
            public int fullWidth;
            public int fullHeight;
            public int closeupWidth;
            public int closeupHeight;
            public string[] screenshots = Array.Empty<string>();
            public string[] checks = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
        }

        sealed class QAFailure : Exception
        {
            public QAFailure(string message) : base(message) { }
        }

        static bool bootstrapped;
        readonly List<string> checks = new List<string>();
        readonly List<string> failures = new List<string>();
        readonly List<string> screenshots = new List<string>();
        Report report;
        string outputDirectory;
        bool fixedSmile;
        bool localPortraitPreview;
        Keyboard keyboard;
        bool ownsKeyboard;
        InputSettings originalInputSettings;
        InputSettings qaInputSettings;
        HideFlags originalInputSettingsFlags;
        bool previousRunInBackground;
        bool targetReady;
        string targetDiagnostic = "";
        Live2DCharacterView character;
        RectTransform portraitViewport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (bootstrapped) return;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, QaArgument);
            if (index < 0) return;
            bootstrapped = true;
            string directory = index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal)
                ? args[index + 1]
                : Path.Combine(Application.temporaryCachePath, "qingye-map-qa");
            var root = new GameObject("Qingye map QA");
            DontDestroyOnLoad(root);
            var qa = root.AddComponent<QingyeMapQA>();
            qa.outputDirectory = Path.GetFullPath(directory);
            qa.fixedSmile = Array.IndexOf(args, FixedSmileArgument) >= 0;
            qa.localPortraitPreview = Array.IndexOf(args, LocalPreviewArgument) >= 0;
        }

        void Start()
        {
            EnableQaInputFocus();
            report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                outputDirectory = outputDirectory,
                actorId = ActorId,
                dialogueMode = fixedSmile
                    ? "fixed-neutral-interrupt-smile-esc-street-z"
                    : "real-memory-reply",
                fixedSmileArgument = fixedSmile,
                localPortraitPreviewArgument = localPortraitPreview
            };
            StartCoroutine(Run());
        }

        void EnableQaInputFocus()
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            originalInputSettings = InputSystem.settings;
            if (originalInputSettings == null) return;
            originalInputSettingsFlags = originalInputSettings.hideFlags;
            originalInputSettings.hideFlags = HideFlags.DontSave;
            qaInputSettings = Instantiate(originalInputSettings);
            qaInputSettings.hideFlags = HideFlags.DontSave;
            qaInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = qaInputSettings;
        }

        void RestoreQaInputFocus()
        {
            if (originalInputSettings != null)
            {
                if (qaInputSettings != null && InputSystem.settings == qaInputSettings)
                    InputSystem.settings = originalInputSettings;
                originalInputSettings.hideFlags = originalInputSettingsFlags;
            }
            if (qaInputSettings != null) Destroy(qaInputSettings);
            Application.runInBackground = previousRunInBackground;
        }

        IEnumerator Run()
        {
            Exception failure = null;
            yield return RunSafely(RunCore(), error => failure = error);
            if (failure != null)
                failures.Add(failure is QAFailure ? failure.Message : failure.GetType().Name + ": " + failure.Message);
            bool passed = failure == null && failures.Count == 0 && report != null &&
                (fixedSmile
                    ? report.mapEConfirmed && report.fixedNeutralSpeechTalk
                        && report.fixedNeutralTextRevealCompleted && report.fixedNeutralReturnedToIdle
                        && report.fixedNeutralLegacyMouthStayedAtBaseline
                        && report.fixedInterruptedFirstLineTalk && report.fixedInterruptedSecondLineNativeSpeechTalk
                        && report.fixedInterruptedLegacyMouthStayedAtBaseline
                        && report.fixedInterruptedTextRevealCompleted && report.fixedInterruptedReturnedToIdle
                        && report.fixedEscClosed && report.fixedOtherActorOpened
                        && report.fixedOtherActorNoQingyeSpeechResidue
                        && report.fixedSmileChangedLiveParameter && report.fixedSmileMouthTalk
                        && report.fixedSmileReturnedToIdle && report.fixedSmileExpressionReturnedToIdle
                    : report.mapEConfirmed && report.realMemoryReply && report.realReplyExpressionNeutral
                        && report.neutralEyesOpen && report.neutralMouthTalk
                        && report.neutralSpeechParameterPresent && report.neutralSpeechParameterMoved
                        && report.neutralSpeechReturnedToIdle && report.neutralLegacyMouthStayedAtBaseline
                        && report.textRevealCompleted && report.returnedToIdle);
            Finish(passed);
        }

        IEnumerator RunCore()
        {
            Directory.CreateDirectory(outputDirectory);
            yield return WaitForWorld(90);
            var session = GameSession.Instance;
            var world = WorldDirector.Instance;
            var memory = session == null ? null : session.Memory;
            Require(memory != null, "Memory runtime is not initialized.");
            Require(memory.Spawner != null && memory.View != null && memory.Conversations != null,
                "Memory dialogue runtime is missing its spawner, view or conversation coordinator.");
            Require(session.CurrentPlace == PlaceId.Street,
                "Qingye daytime acceptance expects the Street map; this is the flower-shop outdoor anchor, not the FlowerShop interior.");
            var definition = SevenCharacterCatalog.Find(ActorId);
            Require(definition != null && definition.Place == PlaceId.Street,
                "The live catalog does not place xiaoxiaoqing at the Street flower-shop anchor.");
            NpcPresence actor = null;
            yield return WaitFor(() =>
            {
                actor = memory.Spawner.Find(ActorId);
                return actor != null && actor.IsPresent && actor.Movement != null && actor.Movement.IsReady;
            }, 20, "Qingye daytime spawn");
            report.map = session.CurrentPlace.ToString();
            report.locationId = memory.Location(actor);
            Require(string.Equals(report.locationId, "flower_shop", StringComparison.Ordinal),
                "Qingye is not at the flower_shop anchor: " + report.locationId);
            yield return CapturePair("qingye-day-map", null);
            checks.Add("captured the real Street daytime map at the flower_shop anchor before dialogue");

            yield return OpenQingyeWithMapE(world, memory, actor);
            report.mapEConfirmed = true;
            report.entryPath = "map-E / Interactable.Activate / existing MemoryDialogueView";
            checks.Add("map E opened the existing Qingye dialogue route");

            character = FindCharacterView(memory);
            Require(character != null, "The dialogue UI has no Live2D portrait view.");
            portraitViewport = character.transform as RectTransform;
            Require(portraitViewport != null, "The Live2D portrait view has no RectTransform viewport.");
            yield return WaitFor(() => memory.PlayerChatOpen && memory.View.PlayerInputReady
                && character.IsVisible && character.CurrentProfile != null, 8, "Qingye portrait visibility");
            var expected = Resources.Load<Live2DModelProfile>(ProfilePath);
            Require(expected != null, "Qingye profile asset is missing: " + ProfilePath);
            Require(character.CurrentProfile == expected,
                "The live E conversation opened a different profile. expected=" + ProfileLabel(expected)
                + ", actual=" + ProfileLabel(character.CurrentProfile));
            Require(memory.Conversations.PlayerActor != null && memory.Conversations.PlayerActor.npcId == ActorId,
                "The map E conversation opened a different actor.");
            report.modelProfile = ProfileLabel(character.CurrentProfile);
            report.mouthParameter = expected.mouthParameter;
            report.neutralSpeechParameter = expected.neutralSpeechParameter;
            report.leftEyeParameter = expected.leftEyeParameter;
            report.rightEyeParameter = expected.rightEyeParameter;

            var mouth = FindParameter(character, expected.mouthParameter);
            var neutralSpeech = FindParameter(character, expected.neutralSpeechParameter);
            var leftEye = FindParameter(character, expected.leftEyeParameter);
            var rightEye = FindParameter(character, expected.rightEyeParameter);
            Require(mouth != null && leftEye != null && rightEye != null,
                "Qingye mouth/eye parameters are missing from the instantiated model.");
            Require(!string.IsNullOrWhiteSpace(expected.neutralSpeechParameter),
                "Qingye profile has no neutralSpeechParameter binding.");
            Require(neutralSpeech != null,
                "The final Qingye model is missing the native neutral speech parameter: "
                + expected.neutralSpeechParameter);
            float idleMouth = 0;
            float idleSpeech = 0;
            int idleFrames = 0;
            float idleDeadline = Time.realtimeSinceStartup + .6f;
            while (Time.realtimeSinceStartup < idleDeadline)
            {
                yield return new WaitForEndOfFrame();
                idleMouth += mouth.Value;
                idleSpeech += neutralSpeech.Value;
                idleFrames++;
            }
            idleMouth = idleFrames == 0 ? mouth.Value : idleMouth / idleFrames;
            idleSpeech = idleFrames == 0 ? neutralSpeech.Value : idleSpeech / idleFrames;
            report.neutralIdleMouth = idleMouth;
            report.neutralIdleSpeech = idleSpeech;
            report.neutralSpeechParameterPresent = true;
            yield return CapturePair("qingye-neutral-idle-before", portraitViewport);
            checks.Add("captured the neutral open-eye portrait before sending a line");

            if (fixedSmile)
            {
                yield return RunFixedNeutral(memory, expected, neutralSpeech, mouth, leftEye, rightEye,
                    idleSpeech, idleMouth, portraitViewport);
                yield return RunFixedInterrupt(memory, expected, neutralSpeech, mouth, leftEye, rightEye,
                    idleSpeech, idleMouth, portraitViewport);
                yield return RunFixedSmile(memory, expected, mouth, portraitViewport);
                yield return RunFixedOtherActor(world, memory, expected, portraitViewport);
            }
            else
                yield return RunRealNeutralReply(memory, expected, neutralSpeech, mouth, leftEye, rightEye,
                    idleSpeech, idleMouth, portraitViewport);

            yield return Tap(Key.Escape);
            yield return WaitFor(() => !memory.PlayerChatOpen && !character.IsVisible, 6, "Qingye dialogue close");
            Require(!session.InputBlocked, "Player input remains blocked after closing Qingye dialogue.");
            yield return CapturePair("qingye-day-map-after", null);
            checks.Add("Esc closed the dialogue and restored the real daytime map");
        }

        IEnumerator OpenQingyeWithMapE(WorldDirector world, MemoryGameController memory, NpcPresence actor)
        {
            var session = GameSession.Instance;
            var player = world == null ? null : world.Player;
            var controller = player == null ? null : player.GetComponent<PlayerController>();
            var interaction = player == null ? null : player.GetComponent<PlayerInteraction>();
            Require(controller != null && interaction != null, "Player controller or interaction target is missing.");
            Require(actor != null && actor.IsPresent, "Qingye is not present at the daytime flower-shop anchor.");
            controller.Teleport(new Vector2(actor.transform.position.x + TargetOffset, actor.transform.position.y));
            Physics2D.SyncTransforms();
            yield return WaitForTarget(session, world, interaction, actor, 5);
            Require(targetReady, "Map E did not select xiaoxiaoqing: " + targetDiagnostic);
            yield return Tap(Key.E);
            yield return WaitFor(() => memory.PlayerChatOpen && memory.View.PlayerInputReady
                && memory.Conversations.PlayerActor != null
                && memory.Conversations.PlayerActor.npcId == ActorId, 8, "Qingye map E conversation");
        }

        IEnumerator WaitForTarget(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor, float timeout)
        {
            targetReady = false;
            int stable = 0;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool matches = session != null && session.IsReady && !session.InputBlocked
                    && !session.NonPlayerInputBlocked && world != null && world.Player != null
                    && world.Stage != null && !world.IsTransitioning && Time.timeScale > 0
                    && interaction != null && interaction.Current != null
                    && interaction.Current.id == actor.npcId && !interaction.Current.isDoor;
                stable = matches ? stable + 1 : 0;
                targetDiagnostic = DescribeTarget(session, world, interaction, actor);
                if (stable >= 2)
                {
                    targetReady = true;
                    yield break;
                }
                yield return null;
            }
        }

        IEnumerator RunRealNeutralReply(MemoryGameController memory, Live2DModelProfile profile,
            CubismParameter neutralSpeech, CubismParameter legacyMouth,
            CubismParameter leftEye, CubismParameter rightEye,
            float idleSpeech, float idleMouth, RectTransform viewport)
        {
            yield return WaitForMemoryService(memory.Client, 30);
            checks.Add("MemoryLab is reachable and configured before the real flower-shop question");
            string question = Environment.GetEnvironmentVariable("QINGYE_QA_QUESTION");
            if (string.IsNullOrWhiteSpace(question)) question = "花店今天营业到几点？";
            string[] expressionDirectives = { "笑", "中性", "请演", "表情", "smile", "emotion" };
            Require(!expressionDirectives.Any(word => question.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0),
                "QINGYE_QA_QUESTION contains an expression directive; use an ordinary flower-shop question.");
            report.question = question;
            int committedBefore = memory.Conversations.CommittedNpcLines;
            SendThroughExistingDialogueInput(memory.View, question);
            yield return WaitFor(() => memory.Conversations.CommittedNpcLines > committedBefore,
                45, "real Qingye reply");
            var reply = memory.LastReply;
            Require(reply != null && reply.result != null, "The committed Qingye turn has no MemoryReply result.");
            Require(string.Equals(reply.actorId, ActorId, StringComparison.Ordinal),
                "The committed MemoryLab reply belongs to actor " + reply.actorId + ", not " + ActorId + ".");
            string text = reply.result.dialogue ?? reply.text ?? "";
            string expression = (reply.result.expression ?? reply.expression ?? "").Trim();
            report.replyText = text;
            report.replyExpression = string.IsNullOrEmpty(expression) ? "neutral (omitted)" : expression;
            report.realMemoryReply = !string.IsNullOrWhiteSpace(text);
            report.realReplyExpressionNeutral = string.IsNullOrEmpty(expression)
                || string.Equals(expression, "neutral", StringComparison.OrdinalIgnoreCase);
            Require(report.realMemoryReply, "MemoryLab returned an empty Qingye dialogue line.");
            Require(report.realReplyExpressionNeutral,
                "The ordinary flower-shop question returned a non-neutral expression: " + expression);
            checks.Add("MemoryLab returned a real Qingye reply with expression=" + report.replyExpression);

            bool captured = false;
            float speechPeak = neutralSpeech.Value;
            float speechMaxDelta = 0;
            float legacyMaxDelta = 0;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                speechPeak = Mathf.Max(speechPeak, neutralSpeech.Value);
                speechMaxDelta = Mathf.Max(speechMaxDelta, Mathf.Abs(neutralSpeech.Value - idleSpeech));
                legacyMaxDelta = Mathf.Max(legacyMaxDelta, Mathf.Abs(legacyMouth.Value - idleMouth));
                report.textRevealCompleted |= IsTextRevealComplete(memory.View);
                if (!captured && neutralSpeech.Value >= MouthCaptureThreshold
                    && neutralSpeech.Value - idleSpeech >= NativeSpeechDeltaThreshold
                    && leftEye.Value >= EyeOpenThreshold
                    && rightEye.Value >= EyeOpenThreshold)
                {
                    report.neutralTalkSpeech = neutralSpeech.Value;
                    report.neutralTalkMouth = legacyMouth.Value;
                    report.neutralTalkLeftEye = leftEye.Value;
                    report.neutralTalkRightEye = rightEye.Value;
                    report.neutralEyesOpen = true;
                    report.neutralMouthTalk = true;
                    yield return CapturePair("qingye-natural-neutral-talk", viewport);
                    captured = true;
                }
            }
            report.neutralTalkSpeech = Mathf.Max(report.neutralTalkSpeech, speechPeak);
            report.neutralTalkSpeechMaxDelta = speechMaxDelta;
            report.neutralLegacyMouthMaxDelta = legacyMaxDelta;
            report.neutralSpeechParameterMoved = speechPeak >= MouthCaptureThreshold
                && speechMaxDelta >= NativeSpeechDeltaThreshold;
            report.neutralLegacyMouthStayedAtBaseline = legacyMaxDelta <= LegacyMouthDriftTolerance;
            report.textRevealCompleted |= IsTextRevealComplete(memory.View);
            Require(report.neutralSpeechParameterMoved,
                "The real neutral reply did not move the native parameter " + profile.neutralSpeechParameter + ".");
            Require(report.neutralLegacyMouthStayedAtBaseline,
                "The real neutral reply moved legacy " + profile.mouthParameter
                + " instead of using " + profile.neutralSpeechParameter + ".");
            Require(captured, "The real neutral reply never produced a captured frame with open eyes and native speech mouth.");
            Require(report.textRevealCompleted, "The real neutral reply did not finish revealing its dialogue text.");
            checks.Add("captured a real neutral reply with both eyes open and native speech mouth active");

            int stableClosed = 0;
            float closeDeadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < closeDeadline && stableClosed < StableFrames)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(neutralSpeech.Value - idleSpeech) < IdleMouthTolerance
                    && Mathf.Abs(legacyMouth.Value - idleMouth) < IdleMouthTolerance) stableClosed++;
                else stableClosed = 0;
            }
            report.neutralSpeechReturnedToIdle = stableClosed >= StableFrames;
            report.returnedToIdle = stableClosed >= StableFrames;
            Require(report.neutralSpeechReturnedToIdle,
                "The real neutral reply did not return both speech channels to the idle baseline.");
            yield return CapturePair("qingye-natural-neutral-idle-after", viewport);
            checks.Add("real neutral reply finished revealing text and returned to the native idle baseline");
        }

        IEnumerator RunFixedNeutral(MemoryGameController memory, Live2DModelProfile profile,
            CubismParameter neutralSpeech, CubismParameter legacyMouth,
            CubismParameter leftEye, CubismParameter rightEye,
            float idleSpeech, float idleMouth, RectTransform viewport)
        {
            const string line = "花店今天营业到晚上七点，临近打烊前也可以来取花。";
            memory.View.ShowLine(ActorId, line, WorldDirector.Instance.Player.position, true, "neutral");
            bool captured = false;
            float speechPeak = neutralSpeech.Value;
            float speechMaxDelta = 0;
            float legacyMaxDelta = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                speechPeak = Mathf.Max(speechPeak, neutralSpeech.Value);
                speechMaxDelta = Mathf.Max(speechMaxDelta, Mathf.Abs(neutralSpeech.Value - idleSpeech));
                legacyMaxDelta = Mathf.Max(legacyMaxDelta, Mathf.Abs(legacyMouth.Value - idleMouth));
                report.fixedNeutralTextRevealCompleted |= IsTextRevealComplete(memory.View);
                if (!captured && neutralSpeech.Value >= MouthCaptureThreshold
                    && neutralSpeech.Value - idleSpeech >= NativeSpeechDeltaThreshold
                    && leftEye.Value >= EyeOpenThreshold && rightEye.Value >= EyeOpenThreshold)
                {
                    report.fixedNeutralTalkSpeech = neutralSpeech.Value;
                    yield return CapturePair("qingye-fixed-neutral-talk", viewport);
                    captured = true;
                }
            }
            report.fixedNeutralTalkSpeech = Mathf.Max(report.fixedNeutralTalkSpeech, speechPeak);
            report.fixedNeutralTalkSpeechMaxDelta = speechMaxDelta;
            report.fixedNeutralLegacyMouthMaxDelta = legacyMaxDelta;
            report.fixedNeutralSpeechTalk = speechPeak >= MouthCaptureThreshold
                && speechMaxDelta >= NativeSpeechDeltaThreshold && captured;
            report.fixedNeutralLegacyMouthStayedAtBaseline = legacyMaxDelta <= LegacyMouthDriftTolerance;
            report.fixedNeutralTextRevealCompleted |= IsTextRevealComplete(memory.View);
            Require(report.fixedNeutralSpeechTalk,
                "Fixed neutral replay did not move the native parameter " + profile.neutralSpeechParameter + ".");
            Require(report.fixedNeutralLegacyMouthStayedAtBaseline,
                "Fixed neutral replay moved legacy " + profile.mouthParameter + ".");
            Require(report.fixedNeutralTextRevealCompleted,
                "Fixed neutral replay did not finish revealing its dialogue text.");
            checks.Add("fixed neutral replay reproduced native speech and completed text reveal");

            int stableClosed = 0;
            float closeDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < closeDeadline && stableClosed < StableFrames)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(neutralSpeech.Value - idleSpeech) < IdleMouthTolerance
                    && Mathf.Abs(legacyMouth.Value - idleMouth) < IdleMouthTolerance) stableClosed++;
                else stableClosed = 0;
            }
            report.fixedNeutralIdleSpeech = neutralSpeech.Value;
            report.fixedNeutralIdleMouth = legacyMouth.Value;
            report.fixedNeutralReturnedToIdle = stableClosed >= StableFrames;
            Require(report.fixedNeutralReturnedToIdle,
                "Fixed neutral replay did not return both speech channels to idle.");
            yield return CapturePair("qingye-fixed-neutral-idle-after", viewport);
            checks.Add("fixed neutral replay returned to idle before the smile replay");
        }

        IEnumerator RunFixedInterrupt(MemoryGameController memory, Live2DModelProfile profile,
            CubismParameter neutralSpeech, CubismParameter legacyMouth,
            CubismParameter leftEye, CubismParameter rightEye,
            float idleSpeech, float idleMouth, RectTransform viewport)
        {
            const string firstLine = "这束花我先替你包好，颜色很适合放在窗边。";
            const string secondLine = "如果还想再看一束，我可以马上拿出来。";
            memory.View.ShowLine(ActorId, firstLine, WorldDirector.Instance.Player.position, true, "smile");
            float firstPeak = legacyMouth.Value;
            for (int frame = 0; frame < 30; frame++)
            {
                yield return new WaitForEndOfFrame();
                firstPeak = Mathf.Max(firstPeak, legacyMouth.Value);
            }
            report.fixedInterruptedFirstLinePeak = firstPeak;
            report.fixedInterruptedFirstLineTalk = firstPeak >= .10f;
            Require(report.fixedInterruptedFirstLineTalk,
                "Fixed interruption setup did not enter the first speaking line.");

            memory.View.ShowLine(ActorId, secondLine, WorldDirector.Instance.Player.position, true, "neutral");
            bool captured = false;
            float speechPeak = neutralSpeech.Value;
            float legacyMaxDelta = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                speechPeak = Mathf.Max(speechPeak, neutralSpeech.Value);
                legacyMaxDelta = Mathf.Max(legacyMaxDelta, Mathf.Abs(legacyMouth.Value - idleMouth));
                report.fixedInterruptedTextRevealCompleted |= IsTextRevealComplete(memory.View);
                if (!captured && neutralSpeech.Value >= MouthCaptureThreshold
                    && neutralSpeech.Value - idleSpeech >= NativeSpeechDeltaThreshold
                    && leftEye.Value >= EyeOpenThreshold && rightEye.Value >= EyeOpenThreshold)
                {
                    yield return CapturePair("qingye-fixed-interrupt-second-neutral-talk", viewport);
                    captured = true;
                }
            }
            report.fixedInterruptedSecondLineSpeechPeak = speechPeak;
            report.fixedInterruptedSecondLineLegacyMaxDelta = legacyMaxDelta;
            report.fixedInterruptedSecondLineNativeSpeechTalk = captured
                && speechPeak >= MouthCaptureThreshold
                && speechPeak - idleSpeech >= NativeSpeechDeltaThreshold;
            report.fixedInterruptedLegacyMouthStayedAtBaseline = legacyMaxDelta <= LegacyMouthDriftTolerance;
            report.fixedInterruptedTextRevealCompleted |= IsTextRevealComplete(memory.View);
            Require(report.fixedInterruptedSecondLineNativeSpeechTalk,
                "The second fixed line did not use the native neutral speech parameter.");
            Require(report.fixedInterruptedLegacyMouthStayedAtBaseline,
                "The first line's legacy mouth channel remained active after the second neutral line began.");
            Require(report.fixedInterruptedTextRevealCompleted,
                "The second fixed line did not finish revealing its dialogue text.");
            checks.Add("fixed replay interrupted a smile line with a second neutral line and cleared the legacy mouth channel");

            int stableClosed = 0;
            float closeDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < closeDeadline && stableClosed < StableFrames)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(neutralSpeech.Value - idleSpeech) < IdleMouthTolerance
                    && Mathf.Abs(legacyMouth.Value - idleMouth) < IdleMouthTolerance) stableClosed++;
                else stableClosed = 0;
            }
            report.fixedInterruptedReturnedToIdle = stableClosed >= StableFrames;
            Require(report.fixedInterruptedReturnedToIdle,
                "The interrupted second line did not return both speech channels to idle.");
            yield return CapturePair("qingye-fixed-interrupt-second-neutral-idle-after", viewport);
            checks.Add("fixed interrupted line returned to idle before the smile replay");
        }

        IEnumerator RunFixedSmile(MemoryGameController memory, Live2DModelProfile profile,
            CubismParameter mouth, RectTransform viewport)
        {
            var expression = profile.FindExpression("smile");
            Require(expression != null, "Qingye profile has no smile expression binding.");
            var expressionParameters = new List<CubismParameter>();
            if (expression.Parameters != null) foreach (var entry in expression.Parameters)
            {
                var parameter = FindParameter(character, entry.Id);
                if (parameter != null && !string.Equals(entry.Id, profile.mouthParameter, StringComparison.OrdinalIgnoreCase))
                    expressionParameters.Add(parameter);
            }
            Require(expressionParameters.Count > 0, "Qingye smile has no live Cubism parameter in the instantiated model.");
            var beforeValues = expressionParameters.ToDictionary(parameter => parameter.Id, parameter => parameter.Value);
            float before = beforeValues.Values.Max();
            float maxDelta = 0;
            float idle = mouth.Value;
            string line = "花店的花束已经包好了，放在窗边给你看。";
            memory.View.ShowLine(ActorId, line, WorldDirector.Instance.Player.position, true, "smile");
            bool captured = false;
            float talkMouth = mouth.Value;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                maxDelta = Mathf.Max(maxDelta, expressionParameters.Max(parameter =>
                    Mathf.Abs(parameter.Value - beforeValues[parameter.Id])));
                talkMouth = Mathf.Max(talkMouth, mouth.Value);
                if (!captured && maxDelta > .20f && mouth.Value >= MouthCaptureThreshold)
                {
                    yield return CapturePair("qingye-fixed-smile-talk", viewport);
                    captured = true;
                }
            }
            report.fixedSmileParameterBefore = before;
            report.fixedSmileParameterMaxDelta = maxDelta;
            report.fixedSmileMouthTalk = talkMouth >= MouthCaptureThreshold;
            report.fixedSmileChangedLiveParameter = maxDelta > .20f;
            Require(report.fixedSmileChangedLiveParameter, "Fixed smile did not change a live Cubism parameter.");
            Require(captured, "Fixed smile never produced a same-segment mouth-speaking capture.");
            Require(IsTextRevealComplete(memory.View), "Fixed smile replay did not finish revealing its dialogue text.");
            checks.Add("fixed smile replay changed a live expression parameter and was captured separately from real dialogue");

            int stableClosed = 0;
            int stableExpression = 0;
            float closeDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < closeDeadline && stableClosed < StableFrames)
            {
                yield return new WaitForEndOfFrame();
                bool mouthIdle = Mathf.Abs(mouth.Value - idle) < IdleMouthTolerance;
                bool expressionIdle = expressionParameters.All(parameter =>
                    Mathf.Abs(parameter.Value - beforeValues[parameter.Id]) < IdleMouthTolerance);
                if (mouthIdle) stableClosed++; else stableClosed = 0;
                if (expressionIdle) stableExpression++; else stableExpression = 0;
            }
            report.fixedSmileIdleMouth = mouth.Value;
            report.fixedSmileExpressionReturnedToIdle = stableExpression >= StableFrames;
            report.fixedSmileReturnedToIdle = stableClosed >= StableFrames
                && report.fixedSmileExpressionReturnedToIdle;
            Require(report.fixedSmileReturnedToIdle,
                "Fixed smile replay did not return its mouth and expression parameters to idle.");
            yield return CapturePair("qingye-fixed-smile-idle-after", viewport);
            checks.Add("fixed smile replay returned to idle");
        }

        IEnumerator RunFixedOtherActor(WorldDirector world, MemoryGameController memory,
            Live2DModelProfile qingyeProfile, RectTransform viewport)
        {
            yield return Tap(Key.Escape);
            yield return WaitFor(() => !memory.PlayerChatOpen && !character.IsVisible,
                6, "Qingye dialogue close before actor switch");
            report.fixedEscClosed = !memory.PlayerChatOpen && !character.IsVisible;
            Require(report.fixedEscClosed, "Esc did not close Qingye before the actor switch.");

            const string otherActorId = "z";
            var otherDefinition = SevenCharacterCatalog.Find(otherActorId);
            Require(otherDefinition != null && otherDefinition.Place == PlaceId.Street,
                "The Street handoff actor is missing from the live catalog.");
            NpcPresence otherActor = null;
            yield return WaitFor(() =>
            {
                otherActor = memory.Spawner.Find(otherActorId);
                return otherActor != null && otherActor.IsPresent
                    && otherActor.Movement != null && otherActor.Movement.IsReady;
            }, 20, "Street handoff actor spawn");
            var player = world == null ? null : world.Player;
            var controller = player == null ? null : player.GetComponent<PlayerController>();
            var interaction = player == null ? null : player.GetComponent<PlayerInteraction>();
            Require(controller != null && interaction != null,
                "Player controller or interaction target is missing for the Street handoff.");
            controller.Teleport(new Vector2(otherActor.transform.position.x + TargetOffset, otherActor.transform.position.y));
            Physics2D.SyncTransforms();
            yield return WaitForTarget(GameSession.Instance, world, interaction, otherActor, 5);
            Require(targetReady, "Map E did not select the Street handoff actor: " + targetDiagnostic);
            yield return Tap(Key.E);
            yield return WaitFor(() => memory.PlayerChatOpen && memory.View.PlayerInputReady
                && memory.Conversations.PlayerActor != null
                && memory.Conversations.PlayerActor.npcId == otherActorId,
                8, "Street handoff conversation");
            var otherProfile = Resources.Load<Live2DModelProfile>(otherDefinition.portrait);
            Require(otherProfile != null, "The Street handoff actor has no portrait profile: " + otherDefinition.portrait);
            yield return WaitFor(() => character.IsVisible && character.CurrentProfile == otherProfile,
                8, "Street handoff portrait");
            report.fixedOtherActorProfile = ProfileLabel(character.CurrentProfile);
            report.fixedOtherActorOpened = character.CurrentProfile == otherProfile
                && character.CurrentProfile != qingyeProfile;
            var staleNative = FindParameter(character, qingyeProfile.neutralSpeechParameter);
            report.fixedOtherActorNoQingyeSpeechResidue = staleNative == null;
            Require(report.fixedOtherActorOpened,
                "Esc did not open the separate Street handoff portrait for " + otherActorId + ".");
            Require(report.fixedOtherActorNoQingyeSpeechResidue,
                "The Street handoff portrait still contains Qingye's native speech parameter.");
            yield return CapturePair("qingye-fixed-other-street-z", viewport);
            checks.Add("Esc closed Qingye and map E opened Street actor Z without Qingye speech residue");
        }

        void SendThroughExistingDialogueInput(MemoryDialogueView view, string text)
        {
            Require(view != null && view.DialogueInput != null && view.DialogueSend != null,
                "The existing dialogue input control is not available.");
            view.DialogueInput.text = text;
            view.DialogueSend.onClick.Invoke();
        }

        bool IsTextRevealComplete(MemoryDialogueView view)
        {
            var history = view == null ? null : view.DialogueHistory;
            return history != null && history.textInfo.characterCount > 0
                && history.maxVisibleCharacters >= history.textInfo.characterCount;
        }

        Live2DCharacterView FindCharacterView(MemoryGameController memory)
        {
            var nested = memory?.View?.GetComponentsInChildren<Live2DCharacterView>(true).FirstOrDefault();
            if (nested != null) return nested;
            return UnityEngine.Object.FindObjectsByType<Live2DCharacterView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(view => view != null && view.gameObject.scene.IsValid());
        }

        CubismParameter FindParameter(Live2DCharacterView view, string id)
        {
            var model = view == null ? null : view.CurrentModel;
            if (model == null || string.IsNullOrEmpty(id)) return null;
            try
            {
                foreach (var parameter in model.Parameters)
                    if (parameter != null && parameter.Id == id) return parameter;
            }
            catch (Exception) { }
            return null;
        }

        IEnumerator WaitForWorld(float timeout)
        {
            yield return WaitFor(() => GameSession.Instance != null && GameSession.Instance.IsReady
                && WorldDirector.Instance != null && WorldDirector.Instance.Stage != null
                && !WorldDirector.Instance.IsTransitioning, timeout, "world initialization");
        }

        IEnumerator WaitForMemoryService(MemoryLabClient client, float timeout)
        {
            yield return WaitFor(() => client != null && client.CanGenerate, timeout,
                "MemoryLab service readiness");
        }

        IEnumerator WaitFor(Func<bool> condition, float timeout, string label)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline)
                    throw new QAFailure(label + " timed out.");
                yield return null;
            }
        }

        IEnumerator Tap(Key key)
        {
            var inputKeyboard = AcquireKeyboard();
            InputSystem.QueueStateEvent(inputKeyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(inputKeyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(inputKeyboard, new KeyboardState());
            yield return null;
        }

        Keyboard AcquireKeyboard()
        {
            var current = Keyboard.current;
            if (current != null && (keyboard == null || keyboard != current))
            {
                if (ownsKeyboard && keyboard != null) InputSystem.RemoveDevice(keyboard);
                keyboard = current;
                ownsKeyboard = false;
            }
            if (keyboard == null)
            {
                keyboard = InputSystem.AddDevice<Keyboard>();
                ownsKeyboard = true;
            }
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            keyboard.MakeCurrent();
            return keyboard;
        }

        string DescribeTarget(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor)
        {
            return "player=" + (world?.Player == null ? "<none>" : world.Player.position.ToString("F3"))
                + ", actor=" + (actor == null ? "<none>" : actor.npcId)
                + ", actorPosition=" + (actor == null ? "<none>" : actor.transform.position.ToString("F3"))
                + ", target=" + (interaction?.Current == null ? "<none>" : interaction.Current.id)
                + ", targetDoor=" + (interaction?.Current != null && interaction.Current.isDoor)
                + ", inputBlocked=" + (session != null && session.InputBlocked)
                + ", nonPlayerInputBlocked=" + (session != null && session.NonPlayerInputBlocked)
                + ", transitioning=" + (world != null && world.IsTransitioning)
                + ", timeScale=" + Time.timeScale
                + ", appFocused=" + Application.isFocused;
        }

        string ProfileLabel(Live2DModelProfile profile)
        {
            return profile == null ? "<none>" : profile.name + " / "
                + (profile.prefab == null ? "<no prefab>" : profile.prefab.name) + " / " + profile.characterId;
        }

        IEnumerator CapturePair(string stem, RectTransform viewport)
        {
            yield return new WaitForEndOfFrame();
            Texture2D captured = null;
            try
            {
                captured = ScreenCapture.CaptureScreenshotAsTexture();
                Require(captured != null, "ScreenCapture returned no texture for " + stem + ".");
                string fullPath = Path.Combine(outputDirectory, stem + ".png");
                File.WriteAllBytes(fullPath, captured.EncodeToPNG());
                screenshots.Add(fullPath);
                report.fullWidth = captured.width;
                report.fullHeight = captured.height;
                if (viewport != null)
                {
                    Texture2D close = CropViewport(captured, viewport);
                    if (close != null)
                    {
                        string closePath = Path.Combine(outputDirectory, stem + "-closeup.png");
                        File.WriteAllBytes(closePath, close.EncodeToPNG());
                        screenshots.Add(closePath);
                        report.closeupWidth = close.width;
                        report.closeupHeight = close.height;
                        Destroy(close);
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add("Screenshot " + stem + ": " + error.Message);
            }
            if (captured != null) Destroy(captured);
            yield return null;
        }

        Texture2D CropViewport(Texture2D source, RectTransform viewport)
        {
            if (source == null || viewport == null) return null;
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            float minX = corners.Min(point => point.x);
            float maxX = corners.Max(point => point.x);
            float minY = corners.Min(point => point.y);
            float maxY = corners.Max(point => point.y);
            float scaleX = source.width / Mathf.Max(1f, Screen.width);
            float scaleY = source.height / Mathf.Max(1f, Screen.height);
            float padX = (maxX - minX) * .08f;
            float padY = (maxY - minY) * .08f;
            int x = Mathf.Clamp(Mathf.FloorToInt((minX - padX) * scaleX), 0, source.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((minY - padY) * scaleY), 0, source.height - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt((maxX + padX) * scaleX), x + 1, source.width);
            int top = Mathf.Clamp(Mathf.CeilToInt((maxY + padY) * scaleY), y + 1, source.height);
            int width = right - x;
            int height = top - y;
            var crop = new Texture2D(width, height, TextureFormat.RGBA32, false);
            crop.SetPixels(source.GetPixels(x, y, width, height));
            crop.Apply(false, false);
            return crop;
        }

        IEnumerator RunSafely(IEnumerator inner, Action<Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                bool moved;
                object yielded;
                try
                {
                    moved = current.MoveNext();
                    yielded = moved ? current.Current : null;
                }
                catch (Exception error)
                {
                    onError(error);
                    yield break;
                }
                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                var nested = yielded as IEnumerator;
                if (nested != null)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return yielded;
            }
        }

        void Require(bool condition, string message)
        {
            if (!condition) throw new QAFailure(message);
        }

        void Finish(bool passed)
        {
            report.passed = passed;
            report.screenshots = screenshots.ToArray();
            report.checks = checks.ToArray();
            report.failures = failures.ToArray();
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "qingye-map-qa.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception error)
            {
                Debug.LogError("Qingye map QA could not write its report: " + error);
            }
            if (passed) Debug.Log("QINGYE_MAP_QA_PASS: " + Path.Combine(outputDirectory, "qingye-map-qa.json"));
            else Debug.LogError("QINGYE_MAP_QA_FAIL: " + Path.Combine(outputDirectory, "qingye-map-qa.json"));
            RestoreQaInputFocus();
            if (ownsKeyboard && keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (!Application.isEditor) Application.Quit(passed ? 0 : 1);
            else Destroy(gameObject);
        }
    }
}
