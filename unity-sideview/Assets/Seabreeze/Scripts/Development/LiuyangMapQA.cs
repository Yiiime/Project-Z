using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Live2D.Cubism.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Seabreeze
{
    /// <summary>
    /// Independent daytime acceptance for Linxu (display name 留洋, captain)'s
    /// real map conversation. The map actor ID is linxu; its portrait asset
    /// intentionally carries the Live2D characterId c01.
    /// It is inert unless -liuyangMapQA &lt;directory&gt; is present.
    ///
    /// The normal mode opens through the live map E route, submits prompts
    /// through the existing dialogue input and requires real MemoryLab replies.
    /// Optional -liuyangQaConfig &lt;json&gt; supplies alternate natural prompts;
    /// otherwise liuyang-qa-prompts.json beside the output is used when present.
    /// -liuyangFixedReplay is a separate offline visual replay using the same
    /// MemoryDialogueView, so it is never reported as a service reply.
    /// </summary>
    public sealed class LiuyangMapQA : MonoBehaviour
    {
        const string QaArgument = "-liuyangMapQA";
        const string FixedArgument = "-liuyangFixedReplay";
        const string FixedAliasArgument = "-qaFixedLiuyang";
        const string ConfigArgument = "-liuyangQaConfig";
        const string CaptainActorId = "linxu";
        const string CaptainLocationId = "dock";
        const float TargetOffset = .12f;
        const float ExpressionThreshold = .16f;
        const float FullSighRelease = .95f;
        const float MouthThreshold = .18f;
        const float MouthClosedTolerance = .10f;
        const int MaxEmotionAttempts = 3;

        [Serializable]
        sealed class Report
        {
            public string timestampUtc;
            public string unityVersion;
            public string outputDirectory;
            public string mode;
            public string actorId;
            public string displayName;
            public string map;
            public string locationId;
            public string entryPath;
            public string profilePath;
            public string expectedProfile;
            public string actualProfile;
            public string otherActorId;
            public string otherActorMap;
            public string otherActorProfile;
            public bool mapEConfirmed;
            public bool mapNameplateConfirmed;
            public bool dialogueTitleConfirmed;
            public bool nameAnswerConfirmed;
            public bool realMemoryReply;
            public bool fixedReplayUsed;
            public bool puzzledObserved;
            public bool sighObserved;
            public bool sighReleaseObserved;
            public bool mouthObserved;
            public bool mouthClosedObserved;
            public bool visibleNeutralBeforeExit;
            public bool interruptionObserved;
            public bool interruptResetObserved;
            public bool textRevealObserved;
            public bool dialogueClosed;
            public bool otherActorOpened;
            public bool otherActorNoLiuyangResidue;
            public float mouthPeak;
            public float puzzledPeak;
            public float sighPeak;
            public float sighReleasePeak;
            public string firstReplyExpression;
            public string secondReplyExpression;
            public string firstReplyText;
            public string secondReplyText;
            public string nameQuestion;
            public string nameAnswer;
            public ReplyRecord[] replyRecords = Array.Empty<ReplyRecord>();
            public string[] missingChecks = Array.Empty<string>();
            public string[] screenshots = Array.Empty<string>();
            public string[] checks = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
            public bool passed;
        }

        sealed class QAFailure : Exception
        {
            public QAFailure(string message) : base(message) { }
        }

        sealed class TurnObservation
        {
            public string expected;
            public string expression;
            public string text;
            public bool replySeen;
            public bool expressionSeen;
            public bool mouthSeen;
            public bool revealSeen;
            public float mouthPeak;
            public float expressionPeak;
            public float releasePeak;
            public int committedBefore;
            public MemoryReply reply;
            public MemoryReply interruptReply;
            public string interruptExpression;
            public string interruptText;
            public bool interruptReplySeen;
            public ReplyRecord replyRecord;
            public ReplyRecord interruptRecord;
        }

        [Serializable]
        sealed class ReplyRecord
        {
            public string phase;
            public string expectedExpression;
            public string actorId;
            public string expression;
            public string normalizedExpression;
            public string text;
            public bool replySeen;
            public bool expressionSeen;
            public bool mouthSeen;
            public bool interrupted;
            public float mouthPeak;
            public float expressionPeak;
            public float releasePeak;
        }

        [Serializable]
        sealed class PromptConfig
        {
            public string namePrompt;
            public string nameInterruptPrompt;
            public string[] puzzledPrompts;
            public string[] puzzledInterruptPrompts;
            public string[] sighPrompts;
            public string[] sighInterruptPrompts;
            public int maxEmotionAttempts;
        }

        static bool bootstrapped;
        readonly List<string> checks = new List<string>();
        readonly List<string> failures = new List<string>();
        readonly List<string> missingChecks = new List<string>();
        readonly List<ReplyRecord> replyRecords = new List<ReplyRecord>();
        readonly List<string> screenshots = new List<string>();
        Report report;
        string outputDirectory;
        string promptConfigPath;
        PromptConfig promptConfig;
        bool fixedReplay;
        Keyboard keyboard;
        bool ownsKeyboard;
        InputSettings originalInputSettings;
        InputSettings qaInputSettings;
        HideFlags originalInputSettingsFlags;
        bool previousRunInBackground;
        string actorId;
        SevenCharacter definition;
        Live2DModelProfile expectedProfile;
        Live2DModelProfile liuyangProfile;
        Live2DCharacterView portrait;
        RectTransform portraitViewport;
        string targetDiagnostic = "";

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
                : Path.Combine(Application.temporaryCachePath, "liuyang-map-qa");
            var root = new GameObject("Liuyang map QA");
            DontDestroyOnLoad(root);
            var qa = root.AddComponent<LiuyangMapQA>();
            qa.outputDirectory = Path.GetFullPath(directory);
            int configIndex = Array.IndexOf(args, ConfigArgument);
            qa.promptConfigPath = configIndex >= 0 && configIndex + 1 < args.Length
                && !args[configIndex + 1].StartsWith("-", StringComparison.Ordinal)
                ? Path.GetFullPath(args[configIndex + 1])
                : Path.Combine(qa.outputDirectory, "liuyang-qa-prompts.json");
            qa.fixedReplay = Array.IndexOf(args, FixedArgument) >= 0
                || Array.IndexOf(args, FixedAliasArgument) >= 0;
        }

        void Start()
        {
            EnableQaInputFocus();
            report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                outputDirectory = outputDirectory,
                mode = fixedReplay ? "fixed-replay" : "real-memory-reply",
                fixedReplayUsed = fixedReplay
            };
            promptConfig = DefaultPromptConfig();
            StartCoroutine(Run());
        }

        // This component is created only by the explicit QA argument. The
        // cloned settings prevent an unfocused desktop window from dropping
        // the real InputSystem events during this acceptance run.
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
            bool passed = failure == null && failures.Count == 0 && report != null
                && report.mapEConfirmed && report.mapNameplateConfirmed && report.dialogueTitleConfirmed
                && (fixedReplay || (report.realMemoryReply && report.nameAnswerConfirmed))
                && report.puzzledObserved && report.sighObserved && report.sighReleaseObserved
                && report.mouthObserved && report.mouthClosedObserved
                && report.interruptionObserved && report.interruptResetObserved
                && report.textRevealObserved && report.dialogueClosed
                && report.otherActorOpened && report.otherActorNoLiuyangResidue;
            Finish(passed);
        }

        IEnumerator RunCore()
        {
            Directory.CreateDirectory(outputDirectory);
            LoadPromptConfig();
            yield return WaitForWorld(90, "world initialization");
            var session = GameSession.Instance;
            var world = WorldDirector.Instance;
            var memory = session == null ? null : session.Memory;
            Require(memory != null && memory.Spawner != null && memory.View != null
                && memory.Conversations != null, "Memory dialogue runtime is not initialized.");

            ResolveActor();
            report.actorId = actorId;
            report.displayName = definition.displayName;
            report.profilePath = definition.portrait;
            expectedProfile = Resources.Load<Live2DModelProfile>(definition.portrait);
            Require(expectedProfile != null, "Liuyang catalog portrait is missing: " + definition.portrait);
            Require(string.Equals(expectedProfile.characterId, "c01", StringComparison.OrdinalIgnoreCase),
                "The linxu portrait must use the c01 Live2D profile, actual=" + expectedProfile.characterId);
            report.expectedProfile = ProfileLabel(expectedProfile);
            liuyangProfile = expectedProfile;

            if (session.CurrentPlace != definition.Place)
            {
                Require(world.TravelTo(definition.Place, null),
                    "Could not travel to Liuyang's catalog map: " + definition.Place);
                yield return WaitForWorld(45, "Liuyang map travel");
            }
            NpcPresence actor = null;
            yield return WaitFor(() =>
            {
                actor = memory.Spawner.Find(actorId);
                return actor != null && actor.IsPresent && actor.Movement != null
                    && actor.Movement.IsReady && actor.gameObject.scene == world.Stage.gameObject.scene;
            }, 20, "Liuyang daytime spawn");
            report.map = session.CurrentPlace.ToString();
            report.locationId = memory.Location(actor);
            Require(actorId == CaptainActorId && session.CurrentPlace == PlaceId.Street,
                "The captain must be the Street map actor linxu, not a portrait-only alias.");
            Require(string.Equals(definition.locationId, CaptainLocationId, StringComparison.Ordinal),
                "The linxu catalog anchor is not dock: " + definition.locationId);
            Require(string.Equals(report.locationId, CaptainLocationId, StringComparison.Ordinal),
                "The live linxu actor is not at the dock anchor: " + report.locationId);
            int captainCount = memory.Spawner.Actors.Count(item => item != null && item.npcId == CaptainActorId
                && item.IsPresent && item.gameObject.scene == world.Stage.gameObject.scene);
            int captainInteractables = world.Interactables.Count(item => item != null && item.npc
                && item.id == CaptainActorId && item.enabled && item.gameObject.activeInHierarchy);
            Require(captainCount == 1 && captainInteractables == 1,
                "The dock must have exactly one active linxu captain. actors=" + captainCount
                + ", interactables=" + captainInteractables);
            string mapPrompt = session.Story.PromptFor(actorId);
            report.mapNameplateConfirmed = mapPrompt.IndexOf(definition.displayName, StringComparison.Ordinal) >= 0;
            Require(report.mapNameplateConfirmed,
                "The live map interaction nameplate does not contain 留洋: " + mapPrompt);
            checks.Add("dock contains exactly one active linxu captain and one matching E interactable");
            yield return CapturePair("liuyang-day-map-before", null);
            checks.Add("captured the actual daytime map before the Liuyang interaction");

            yield return OpenActorWithMapE(world, memory, actor);
            report.mapEConfirmed = true;
            report.entryPath = "map-E / Interactable.Activate / existing MemoryDialogueView";
            checks.Add("map E opened Liuyang through the normal interaction route");

            portrait = FindCharacterView(memory);
            Require(portrait != null, "The dialogue window has no Live2D portrait view.");
            portraitViewport = portrait.transform as RectTransform;
            yield return WaitFor(() => memory.PlayerChatOpen && memory.View.PlayerInputReady
                && portrait.IsVisible && portrait.CurrentProfile != null, 8, "Liuyang portrait visibility");
            Require(portrait.CurrentProfile == expectedProfile
                || ProfileLabel(portrait.CurrentProfile) == ProfileLabel(expectedProfile),
                "Map E opened a different profile. expected=" + ProfileLabel(expectedProfile)
                + ", actual=" + ProfileLabel(portrait.CurrentProfile));
            Require(memory.Conversations.PlayerActor != null
                && memory.Conversations.PlayerActor.npcId == actorId,
                "Map E opened a different player conversation actor.");
            report.dialogueTitleConfirmed = ReadDialogueTitle(memory.View) == definition.displayName;
            Require(report.dialogueTitleConfirmed,
                "The dialogue title is not 留洋: " + ReadDialogueTitle(memory.View));
            report.actualProfile = ProfileLabel(portrait.CurrentProfile);
            yield return CapturePair("liuyang-dialogue-open", portraitViewport);

            if (fixedReplay)
                yield return RunFixedReplay(memory, world);
            else
                yield return RunRealReplies(memory);

            yield return WaitFor(() => portrait != null && portrait.CurrentModel != null
                && !memory.Conversations.Sending && memory.View.DialogueHistory != null
                && memory.View.DialogueHistory.maxVisibleCharacters >= memory.View.DialogueHistory.textInfo.characterCount
                && ReadParameter("ParamMouthOpenY") < .08f && ReadParameter("ParamPuzzled") < .05f
                && ReadParameter("ParamSigh") < .05f && ReadParameter("ParamSighRelease") < .05f,
                25, "visible captain returned to neutral before closing the dialogue");
            report.visibleNeutralBeforeExit = true;
            yield return CapturePair("liuyang-visible-neutral", portraitViewport);
            yield return CloseDialogue(memory);
            report.dialogueClosed = true;
            yield return CapturePair("liuyang-day-map-after", null);
            checks.Add("Esc closed Liuyang and restored the daytime map");

            NpcPresence other = FindOtherPresentActor(memory, actorId, world);
            if (other == null)
            {
                SevenCharacter otherDefinition = SevenCharacterCatalog.Actors.FirstOrDefault(item =>
                    item != null && item.id != actorId && item.Place != definition.Place
                    && !string.IsNullOrWhiteSpace(item.portrait));
                Require(otherDefinition != null, "No other catalog portrait is available for the handoff check.");
                if (session.CurrentPlace != otherDefinition.Place)
                {
                    Require(world.TravelTo(otherDefinition.Place, null),
                        "Could not travel to the handoff actor map: " + otherDefinition.Place);
                    yield return WaitForWorld(45, "handoff map travel");
                }
                yield return WaitFor(() =>
                {
                    other = memory.Spawner.Find(otherDefinition.id);
                    return other != null && other.IsPresent && other.Movement != null
                        && other.Movement.IsReady && other.gameObject.scene == world.Stage.gameObject.scene;
                }, 20, "handoff actor spawn");
            }
            Require(other != null && other.npcId != actorId, "No distinct handoff NPC is present.");
            report.otherActorId = other.npcId;
            report.otherActorMap = session.CurrentPlace.ToString();
            yield return OpenActorWithMapE(world, memory, other);
            var otherPortrait = FindCharacterView(memory);
            var otherExpected = Resources.Load<Live2DModelProfile>(SevenCharacterCatalog.Find(other.npcId)?.portrait);
            Require(otherPortrait != null && otherPortrait.IsVisible && otherExpected != null,
                "The handoff dialogue did not show a valid other portrait.");
            report.otherActorProfile = ProfileLabel(otherPortrait.CurrentProfile);
            report.otherActorOpened = otherPortrait.CurrentProfile == otherExpected
                || ProfileLabel(otherPortrait.CurrentProfile) == ProfileLabel(otherExpected);
            report.otherActorNoLiuyangResidue = otherPortrait.CurrentProfile != liuyangProfile;
            Require(report.otherActorOpened, "The handoff opened the wrong profile for " + other.npcId + ".");
            Require(report.otherActorNoLiuyangResidue, "Liuyang's profile remained after switching actors.");
            yield return CapturePair("liuyang-handoff-other-actor", otherPortrait.transform as RectTransform);
            yield return CloseDialogue(memory);
            checks.Add("closed Liuyang, opened another real map actor and cleared Liuyang residue");
        }

        PromptConfig DefaultPromptConfig()
        {
            return new PromptConfig
            {
                namePrompt = "留洋（船长），我第一次来码头，你的本名是留洋吗？",
                nameInterruptPrompt = "先停一下，下一句接着说：把船靠稳，剩下的交给我。",
                puzzledPrompts = new[]
                {
                    "刚才你说航线没问题，可我听见发动机有异响。你觉得哪里不对？",
                    "我看你一直盯着那条航线，心里是不是还有疑问？说说你真正担心的地方。",
                    "如果现在先不赶路，你会先查哪一处？我想听听你的判断。"
                },
                puzzledInterruptPrompts = new[]
                {
                    "先停一下，下一句接着说：把船靠稳，剩下的交给我。",
                    "先别急着下结论，下一句说说你最担心的地方。",
                    "打断一下，下一句只回答我：现在先查哪一处？"
                },
                sighPrompts = new[]
                {
                    "今天已经折腾很久了。你先不做决定，只回答我：现在更想歇一会儿，还是继续把问题查完？",
                    "事情一件接一件，你是不是也有点累了？现在最想先做什么？",
                    "如果暂时不用照顾所有人的安排，你想先歇一会儿，还是把眼前的问题处理完？"
                },
                sighInterruptPrompts = new[]
                {
                    "请停一下，下一句说说你最担心女儿听见什么。",
                    "先停在这里，下一句只说你现在最想歇什么。",
                    "打断一下，下一句告诉我：你准备先歇一会儿，还是继续查？"
                },
                maxEmotionAttempts = MaxEmotionAttempts
            };
        }

        void LoadPromptConfig()
        {
            if (string.IsNullOrWhiteSpace(promptConfigPath) || !File.Exists(promptConfigPath)) return;
            try
            {
                var loaded = JsonUtility.FromJson<PromptConfig>(File.ReadAllText(promptConfigPath));
                if (loaded == null) return;
                if (!string.IsNullOrWhiteSpace(loaded.namePrompt)) promptConfig.namePrompt = loaded.namePrompt;
                if (!string.IsNullOrWhiteSpace(loaded.nameInterruptPrompt)) promptConfig.nameInterruptPrompt = loaded.nameInterruptPrompt;
                promptConfig.puzzledPrompts = NonEmpty(loaded.puzzledPrompts, promptConfig.puzzledPrompts);
                promptConfig.puzzledInterruptPrompts = NonEmpty(loaded.puzzledInterruptPrompts, promptConfig.puzzledInterruptPrompts);
                promptConfig.sighPrompts = NonEmpty(loaded.sighPrompts, promptConfig.sighPrompts);
                promptConfig.sighInterruptPrompts = NonEmpty(loaded.sighInterruptPrompts, promptConfig.sighInterruptPrompts);
                promptConfig.maxEmotionAttempts = Mathf.Clamp(
                    loaded.maxEmotionAttempts > 0 ? loaded.maxEmotionAttempts : MaxEmotionAttempts, 1, MaxEmotionAttempts);
                checks.Add("loaded optional Liuyang QA prompt config: " + promptConfigPath);
            }
            catch (Exception error)
            {
                missingChecks.Add("prompt config could not be loaded: " + error.Message);
                checks.Add("MISSING: optional Liuyang QA prompt config was ignored");
            }
        }

        static string[] NonEmpty(string[] value, string[] fallback)
        {
            return value == null || value.All(string.IsNullOrWhiteSpace)
                ? fallback : value.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }

        void RecordMissing(string message)
        {
            if (missingChecks.Contains(message)) return;
            missingChecks.Add(message);
            checks.Add("MISSING: " + message);
        }

        void ResolveActor()
        {
            definition = SevenCharacterCatalog.Find(CaptainActorId);
            Require(definition != null, "The live character catalog has no linxu captain entry.");
            Require(!string.IsNullOrWhiteSpace(definition.portrait),
                "Liuyang catalog entry has no portrait path.");
            Require(string.Equals(definition.id, CaptainActorId, StringComparison.Ordinal),
                "Liuyang QA resolved a non-captain actor: " + definition.id);
            Require(string.Equals(definition.displayName, "留洋", StringComparison.Ordinal),
                "The live captain nameplate/title must be 留洋, actual=" + definition.displayName);
            actorId = definition.id;
        }

        IEnumerator OpenActorWithMapE(WorldDirector world, MemoryGameController memory, NpcPresence actor)
        {
            var session = GameSession.Instance;
            var player = world == null ? null : world.Player;
            var controller = player == null ? null : player.GetComponent<PlayerController>();
            var interaction = player == null ? null : player.GetComponent<PlayerInteraction>();
            Require(controller != null && interaction != null, "Player controller or interaction target is missing.");
            controller.Teleport(new Vector2(actor.transform.position.x + TargetOffset, actor.transform.position.y));
            Physics2D.SyncTransforms();
            yield return WaitForTarget(session, world, interaction, actor, 6);
            Require(interaction.Current != null && interaction.Current.id == actor.npcId
                && !interaction.Current.isDoor, "Map E target was not " + actor.npcId + ": " + targetDiagnostic);
            yield return Tap(Key.E);
            yield return WaitFor(() => memory.PlayerChatOpen && memory.View.PlayerInputReady
                && memory.Conversations.PlayerActor != null
                && memory.Conversations.PlayerActor.npcId == actor.npcId, 10,
                "map E conversation for " + actor.npcId);
        }

        IEnumerator WaitForTarget(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor, float timeout)
        {
            int stable = 0;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool matches = session != null && session.IsReady && !session.InputBlocked
                    && !session.NonPlayerInputBlocked && world != null && world.Player != null
                    && world.Stage != null && !world.IsTransitioning && Time.timeScale > 0
                    && interaction != null && interaction.Current != null
                    && interaction.Current.id == actor.npcId && !interaction.Current.isDoor
                    && actor.IsPresent && actor.Movement != null && actor.Movement.IsReady;
                targetDiagnostic = DescribeTarget(session, world, interaction, actor);
                stable = matches ? stable + 1 : 0;
                if (stable >= 2) yield break;
                yield return null;
            }
            throw new QAFailure("Map E target timed out: " + targetDiagnostic);
        }

        IEnumerator RunRealReplies(MemoryGameController memory)
        {
            yield return WaitFor(() => memory.Client != null && memory.Client.CanGenerate, 25,
                "MemoryLab service readiness");
            checks.Add("MemoryLab is reachable before the real Liuyang reply path");

            TurnObservation name = null;
            report.nameQuestion = promptConfig.namePrompt;
            yield return RunNameTurn(memory, value => name = value);
            if (name != null && name.replySeen)
            {
                report.nameAnswer = name.text;
                report.nameAnswerConfirmed = name.text.IndexOf("留洋", StringComparison.Ordinal) >= 0;
                report.realMemoryReply = true;
                if (!report.nameAnswerConfirmed)
                    RecordMissing("real linxu name reply did not contain 留洋: " + name.text);
            }
            else RecordMissing("real linxu name reply was not available");

            TurnObservation puzzled = null;
            yield return RunEmotionTurn(memory, "troubled", promptConfig.puzzledPrompts,
                promptConfig.puzzledInterruptPrompts, value =>
                {
                    if (puzzled == null) puzzled = value;
                    else MergeObservation(puzzled, value);
                });
            StoreEmotionObservation(puzzled, true);

            TurnObservation sigh = null;
            yield return RunEmotionTurn(memory, "sigh", promptConfig.sighPrompts,
                promptConfig.sighInterruptPrompts, value =>
                {
                    if (sigh == null) sigh = value;
                    else MergeObservation(sigh, value);
                });
            StoreEmotionObservation(sigh, false);

            if (!report.puzzledObserved) RecordMissing("troubled expression parameter was not observed after three natural prompts");
            if (!report.sighObserved) RecordMissing("sigh expression parameter was not observed after three natural prompts");
            if (!report.sighReleaseObserved) RecordMissing("sigh release phase was not observed after three natural prompts");
            if (!report.mouthObserved || report.mouthPeak < MouthThreshold)
                RecordMissing("real linxu replies did not visibly open the mouth. peak=" + report.mouthPeak);
            checks.Add("real MemoryLab replies were filtered to linxu and retained actual text/expression records");
        }

        IEnumerator RunNameTurn(MemoryGameController memory, Action<TurnObservation> assign)
        {
            Exception attemptError = null;
            TurnObservation completed = null;
            yield return RunSafely(RunRealTurnAttempt(memory, null, report.nameQuestion,
                promptConfig.nameInterruptPrompt, "name", 1, value => completed = value),
                error => attemptError = error);
            if (attemptError != null) throw attemptError;
            if (completed == null || !completed.replySeen)
                throw new QAFailure("The real linxu name reply was not committed.");
            assign?.Invoke(completed);
        }

        IEnumerator RunEmotionTurn(MemoryGameController memory, string expected,
            string[] prompts, string[] interruptPrompts, Action<TurnObservation> assign)
        {
            int attempts = Mathf.Clamp(promptConfig.maxEmotionAttempts > 0
                ? promptConfig.maxEmotionAttempts : MaxEmotionAttempts, 1, MaxEmotionAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                string prompt = prompts[Mathf.Min(attempt, prompts.Length - 1)];
                string interruptPrompt = interruptPrompts[Mathf.Min(attempt, interruptPrompts.Length - 1)];
                Exception attemptError = null;
                TurnObservation completed = null;
                yield return RunSafely(RunRealTurnAttempt(memory, expected, prompt, interruptPrompt,
                    expected + "-natural-" + (attempt + 1), attempt + 1,
                    value => completed = value), error => attemptError = error);
                if (completed != null) assign?.Invoke(completed);
                if (attemptError == null && completed != null && completed.expressionSeen
                    && (expected != "sigh" || completed.releasePeak >= FullSighRelease))
                {
                    checks.Add(expected + " natural reply observed after " + (attempt + 1) + " prompt(s)");
                    yield break;
                }
                if (attempt + 1 < attempts)
                {
                    checks.Add(expected + " natural context retry " + (attempt + 1) + "/" + attempts
                        + (attemptError == null ? " after missing target expression" : ": " + attemptError.Message));
                    yield return WaitFor(() => memoryReadyForInput(), 20,
                        expected + " retry input readiness");
                }
                else if (attemptError != null)
                {
                    RecordMissing(expected + " natural reply attempt ended without a linxu sample: " + attemptError.Message);
                }
            }
        }

        IEnumerator RunRealTurnAttempt(MemoryGameController memory, string expected,
            string prompt, string interruptPrompt, string phase, int attempt,
            Action<TurnObservation> assign)
        {
            var observation = new TurnObservation { expected = expected };
            observation.committedBefore = memory.Conversations.CommittedNpcLines;
            MemoryReply previousReply = memory.LastReply;
            yield return SubmitPrompt(memory.View, prompt);
            MemoryReply reply = null;
            yield return WaitForLiuyangReply(memory, observation.committedBefore, previousReply,
                phase + " MemoryLab reply", 70, value => reply = value);
            CaptureReply(observation, reply, phase + " reply", false, expected);
            Require(observation.replySeen, "MemoryLab returned an empty linxu line for " + phase + ".");

            float deadline = Time.realtimeSinceStartup + 18f;
            float fallbackInterruptAt = Time.realtimeSinceStartup + (expected == "sigh" ? 5f : 2.5f);
            bool interruptSent = false;
            bool fullExpressionCaptured = false;
            float fullReleaseSince = -1f;
            while (Time.realtimeSinceStartup < deadline)
            {
                SampleExpression(observation);
                if (ReadParameter("ParamSighRelease") >= FullSighRelease)
                {
                    if (fullReleaseSince < 0f) fullReleaseSince = Time.realtimeSinceStartup;
                }
                else fullReleaseSince = -1f;
                var history = memory.View.DialogueHistory;
                bool revealing = history != null && history.textInfo.characterCount > 0
                    && history.maxVisibleCharacters < history.textInfo.characterCount;
                observation.revealSeen |= revealing;
                bool targetReady = string.IsNullOrEmpty(expected) || observation.expressionSeen;
                bool sighComplete = expected != "sigh" || (fullReleaseSince >= 0f
                    && Time.realtimeSinceStartup - fullReleaseSince >= .25f);
                bool fullExpression = observation.mouthSeen && targetReady && sighComplete;
                if (fullExpression && !fullExpressionCaptured)
                {
                    yield return CapturePair("liuyang-" + (string.IsNullOrEmpty(expected) ? "name" : expected)
                        + "-" + attempt + "-full-before-interrupt", portraitViewport);
                    fullExpressionCaptured = true;
                }
                bool interruptWindow = fullExpression || (observation.expression != expected && Time.realtimeSinceStartup >= fallbackInterruptAt);
                if (!interruptSent && revealing && interruptWindow
                    && memory.View.PlayerInputReady
                    && memory.View.DialogueSend != null && memory.View.DialogueSend.interactable
                    && !memory.Conversations.Sending)
                {
                    int interruptBefore = memory.Conversations.CommittedNpcLines;
                    MemoryReply previousInterruptReply = memory.LastReply;
                    yield return SubmitPrompt(memory.View, interruptPrompt);
                    MemoryReply interruptReply = null;
                    yield return WaitForLiuyangReply(memory, interruptBefore, previousInterruptReply,
                        phase + " interrupt reply", 70, value => interruptReply = value);
                    CaptureReply(observation, interruptReply, phase + " interrupt", true, expected);
                    interruptSent = true;
                    report.interruptionObserved = true;
                    yield return null;
                    if (!string.IsNullOrEmpty(expected))
                        report.interruptResetObserved |= IsExpressionCleared(expected);
                    break;
                }
                yield return new WaitForEndOfFrame();
            }
            report.textRevealObserved |= observation.revealSeen;
            if (!interruptSent)
                throw new QAFailure(phase + " ended before a real typewriter interruption window.");

            float settleDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < settleDeadline)
            {
                SampleExpression(observation);
                var history = memory.View.DialogueHistory;
                bool currentLineRevealed = history != null && history.textInfo.characterCount > 0
                    && history.maxVisibleCharacters >= history.textInfo.characterCount;
                if (currentLineRevealed)
                    report.textRevealObserved = true;
                if (currentLineRevealed && !memory.Conversations.Sending) break;
                yield return new WaitForEndOfFrame();
            }
            UpdateReplyRecords(observation);
            assign?.Invoke(observation);
        }

        IEnumerator WaitForLiuyangReply(MemoryGameController memory, int committedBefore,
            MemoryReply previousReply, string label, float timeout, Action<MemoryReply> assign)
        {
            string previousSignature = ReplySignature(previousReply);
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool activeLinxu = memory != null && memory.Conversations != null
                    && memory.Conversations.PlayerActor != null
                    && memory.Conversations.PlayerActor.npcId == actorId;
                var candidate = memory == null ? null : memory.LastReply;
                string signature = ReplySignature(candidate);
                bool changed = candidate != null && (!ReferenceEquals(candidate, previousReply)
                    || !string.Equals(signature, previousSignature, StringComparison.Ordinal));
                bool committed = memory != null && memory.Conversations != null
                    && memory.Conversations.CommittedNpcLines > committedBefore;
                if (activeLinxu && changed && committed && candidate.result != null
                    && string.Equals(candidate.actorId, actorId, StringComparison.Ordinal))
                {
                    assign?.Invoke(candidate);
                    yield break;
                }
                yield return null;
            }
            throw new QAFailure(label + " timed out waiting for linxu; last="
                + ReplyDiagnostic(memory == null ? null : memory.LastReply));
        }

        string ReplySignature(MemoryReply reply)
        {
            if (reply == null) return "";
            return (reply.requestId ?? "") + "|" + reply.responseRevision + "|" + reply.seq + "|"
                + (reply.actorId ?? "") + "|" + RawReplyText(reply);
        }

        string ReplyDiagnostic(MemoryReply reply)
        {
            return reply == null ? "<none>" : "actor=" + (reply.actorId ?? "<empty>")
                + ", expression=" + RawReplyExpression(reply) + ", text=" + RawReplyText(reply);
        }

        string RawReplyExpression(MemoryReply reply)
        {
            return (reply?.result?.expression ?? reply?.expression ?? "").Trim();
        }

        string RawReplyText(MemoryReply reply)
        {
            return (reply?.result?.dialogue ?? reply?.text ?? "").Trim();
        }

        void CaptureReply(TurnObservation observation, MemoryReply reply, string phase,
            bool interrupted, string expected)
        {
            if (reply == null) return;
            string rawExpression = RawReplyExpression(reply);
            string text = RawReplyText(reply);
            var record = new ReplyRecord
            {
                phase = phase,
                expectedExpression = expected ?? "",
                actorId = reply.actorId ?? "",
                expression = rawExpression,
                normalizedExpression = NormalizeExpression(rawExpression),
                text = text,
                replySeen = !string.IsNullOrWhiteSpace(text),
                interrupted = interrupted
            };
            replyRecords.Add(record);
            if (interrupted)
            {
                observation.interruptReply = reply;
                observation.interruptExpression = rawExpression;
                observation.interruptText = text;
                observation.interruptReplySeen = record.replySeen;
                observation.interruptRecord = record;
            }
            else
            {
                observation.reply = reply;
                observation.expression = rawExpression;
                observation.text = text;
                observation.replySeen = record.replySeen;
                observation.replyRecord = record;
            }
        }

        void MergeObservation(TurnObservation target, TurnObservation extra)
        {
            if (target == null || extra == null) return;
            target.replySeen |= extra.replySeen;
            target.expressionSeen |= extra.expressionSeen;
            target.mouthSeen |= extra.mouthSeen;
            target.revealSeen |= extra.revealSeen;
            target.mouthPeak = Mathf.Max(target.mouthPeak, extra.mouthPeak);
            target.expressionPeak = Mathf.Max(target.expressionPeak, extra.expressionPeak);
            target.releasePeak = Mathf.Max(target.releasePeak, extra.releasePeak);
            target.interruptReplySeen |= extra.interruptReplySeen;
            if (target.reply == null) target.reply = extra.reply;
            if (target.interruptReply == null) target.interruptReply = extra.interruptReply;
        }

        void StoreEmotionObservation(TurnObservation observation, bool puzzled)
        {
            if (observation == null) return;
            if (puzzled)
            {
                report.firstReplyExpression = observation.expression;
                report.firstReplyText = observation.text;
                report.puzzledObserved |= observation.expressionSeen;
                report.puzzledPeak = Mathf.Max(report.puzzledPeak, observation.expressionPeak);
            }
            else
            {
                report.secondReplyExpression = observation.expression;
                report.secondReplyText = observation.text;
                report.sighObserved |= observation.expressionSeen;
                report.sighPeak = Mathf.Max(report.sighPeak, observation.expressionPeak);
                report.sighReleasePeak = Mathf.Max(report.sighReleasePeak, observation.releasePeak);
            }
            report.mouthObserved |= observation.mouthSeen;
            report.mouthPeak = Mathf.Max(report.mouthPeak, observation.mouthPeak);
            report.sighReleaseObserved |= !puzzled && observation.releasePeak >= FullSighRelease;
            report.realMemoryReply |= observation.replySeen;
        }

        void UpdateReplyRecords(TurnObservation observation)
        {
            if (observation == null) return;
            if (observation.replyRecord != null)
            {
                observation.replyRecord.replySeen = observation.replySeen;
                observation.replyRecord.expressionSeen = observation.expressionSeen;
                observation.replyRecord.mouthSeen = observation.mouthSeen;
                observation.replyRecord.mouthPeak = observation.mouthPeak;
                observation.replyRecord.expressionPeak = observation.expressionPeak;
                observation.replyRecord.releasePeak = observation.releasePeak;
            }
            if (observation.interruptRecord != null)
            {
                observation.interruptRecord.replySeen = observation.interruptReplySeen;
                observation.interruptRecord.expressionSeen = !string.IsNullOrEmpty(observation.interruptExpression);
            }
        }

        IEnumerator RunFixedReplay(MemoryGameController memory, WorldDirector world)
        {
            report.fixedReplayUsed = true;
            Vector3 position = world.Player.position;
            memory.View.ShowLine(actorId, "船长先看看航线，刚才那声异响不像普通风浪。", position, true, "troubled");
            yield return ObserveFixedExpression("troubled", 2.2f, true);
            memory.View.ShowLine(actorId, "先停一下，换一句：把船靠稳，剩下的交给我。", position, true, "neutral");
            yield return WaitFor(() => IsExpressionCleared("troubled"), 3, "fixed puzzled reset");
            report.interruptionObserved = true;
            report.interruptResetObserved = true;
            memory.View.ShowLine(actorId, "发动机又在这时候出问题了，我得先稳住心。", position, true, "sigh");
            yield return ObserveFixedExpression("sigh", 2.4f, true);
            memory.View.ShowLine(actorId, "现在回到自然说话状态。", position, true, "neutral");
            yield return ObserveFixedExpression("neutral", 2.2f, false);
            report.realMemoryReply = false;
            report.puzzledObserved = report.puzzledObserved || ReadParameter("ParamPuzzled") >= ExpressionThreshold;
            report.sighObserved = report.sighObserved || ReadParameter("ParamSigh") >= ExpressionThreshold;
            report.textRevealObserved = true;
            checks.Add("fixed replay used ShowLine only and was kept separate from real MemoryLab evidence");
        }

        IEnumerator ObserveFixedExpression(string expected, float seconds, bool sampleMouth)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                float mouth = ReadParameter("ParamMouthOpenY");
                float puzzled = ReadParameter("ParamPuzzled");
                float sigh = ReadParameter("ParamSigh");
                float release = ReadParameter("ParamSighRelease");
                report.mouthPeak = Mathf.Max(report.mouthPeak, mouth);
                report.puzzledPeak = Mathf.Max(report.puzzledPeak, puzzled);
                report.sighPeak = Mathf.Max(report.sighPeak, sigh);
                report.sighReleasePeak = Mathf.Max(report.sighReleasePeak, release);
                report.mouthObserved |= sampleMouth && mouth >= MouthThreshold;
                report.puzzledObserved |= expected == "troubled" && puzzled >= ExpressionThreshold;
                report.sighObserved |= expected == "sigh" && sigh >= ExpressionThreshold;
                report.sighReleaseObserved |= expected == "sigh" && release >= ExpressionThreshold;
                report.textRevealObserved = true;
                yield return new WaitForEndOfFrame();
            }
        }

        void SampleExpression(TurnObservation observation)
        {
            float mouth = ReadParameter("ParamMouthOpenY");
            float puzzled = ReadParameter("ParamPuzzled");
            float sigh = ReadParameter("ParamSigh");
            float release = ReadParameter("ParamSighRelease");
            observation.mouthPeak = Mathf.Max(observation.mouthPeak, mouth);
            observation.expressionPeak = Mathf.Max(observation.expressionPeak,
                observation.expected == "troubled" ? puzzled : observation.expected == "sigh" ? sigh : 0f);
            observation.releasePeak = Mathf.Max(observation.releasePeak, release);
            observation.mouthSeen |= mouth >= MouthThreshold;
            observation.expressionSeen |= observation.expected == "troubled"
                ? puzzled >= ExpressionThreshold
                : observation.expected == "sigh" && sigh >= ExpressionThreshold;
            report.sighReleaseObserved |= observation.expected == "sigh" && release >= FullSighRelease;
        }

        bool IsExpressionCleared(string previous)
        {
            float value = previous == "troubled" ? ReadParameter("ParamPuzzled") : ReadParameter("ParamSigh");
            return value < ExpressionThreshold * .65f;
        }

        IEnumerator SubmitPrompt(MemoryDialogueView view, string prompt)
        {
            Require(view != null && view.DialogueInput != null && view.DialogueSend != null,
                "The normal dialogue input controls are unavailable.");
            yield return WaitFor(() => view.PlayerInputReady && view.DialogueSend.interactable
                && memoryReadyForInput(), 20, "dialogue input readiness");
            view.DialogueInput.ActivateInputField();
            view.DialogueInput.text = prompt;
            view.DialogueSend.onClick.Invoke();
            Require(string.IsNullOrEmpty(view.DialogueInput.text), "The prompt did not leave the normal input field.");
        }

        bool memoryReadyForInput()
        {
            var session = GameSession.Instance;
            return session != null && session.Memory != null && session.Memory.Client != null
                && session.Memory.Client.CanGenerate && !session.Memory.Conversations.Sending;
        }

        IEnumerator CloseDialogue(MemoryGameController memory)
        {
            if (!memory.PlayerChatOpen) yield break;
            yield return Tap(Key.Escape);
            yield return WaitFor(() => !memory.PlayerChatOpen && (memory.View == null || !memory.View.Closing),
                8, "dialogue close");
            yield return new WaitForEndOfFrame();
            report.mouthClosedObserved = ReadParameter("ParamMouthOpenY") <= MouthClosedTolerance;
            Require(report.mouthClosedObserved, "Liuyang mouth did not return to its closed idle value.");
        }

        NpcPresence FindOtherPresentActor(MemoryGameController memory, string excluded, WorldDirector world)
        {
            return memory?.Spawner?.Actors?.FirstOrDefault(item => item != null && item.npcId != excluded
                && item.IsPresent && item.Movement != null && item.Movement.IsReady
                && SevenCharacterCatalog.Find(item.npcId)?.Place != definition.Place
                && !string.IsNullOrWhiteSpace(SevenCharacterCatalog.Find(item.npcId)?.portrait)
                && item.gameObject.scene == world.Stage.gameObject.scene);
        }

        Live2DCharacterView FindCharacterView(MemoryGameController memory)
        {
            var nested = memory?.View?.GetComponentsInChildren<Live2DCharacterView>(true).FirstOrDefault();
            if (nested != null) return nested;
            return UnityEngine.Object.FindObjectsByType<Live2DCharacterView>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
        }

        float ReadParameter(string id)
        {
            var model = portrait == null ? null : portrait.CurrentModel;
            if (model == null || model.Parameters == null) return 0;
            foreach (var parameter in model.Parameters)
                if (parameter != null && string.Equals(parameter.Id, id, StringComparison.OrdinalIgnoreCase))
                    return parameter.Value;
            return 0;
        }

        string NormalizeExpression(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            if (key == "troubled" || key == "puzzled" || key == "question" || key == "confused" || key == "疑惑") return "troubled";
            if (key == "sigh" || key == "sigh_before" || key == "sigh_after" || key == "helpless" || key == "叹气") return "sigh";
            return string.IsNullOrEmpty(key) ? "neutral" : key;
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

        IEnumerator WaitForWorld(float timeout, string label)
        {
            yield return WaitFor(() => GameSession.Instance != null && GameSession.Instance.IsReady
                && WorldDirector.Instance != null && WorldDirector.Instance.Stage != null
                && !WorldDirector.Instance.IsTransitioning, timeout, label);
        }

        IEnumerator WaitFor(Func<bool> condition, float timeout, string label)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline) throw new QAFailure(label + " timed out.");
                yield return null;
            }
        }

        string DescribeTarget(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor)
        {
            return "player=" + (world?.Player == null ? "<none>" : world.Player.position.ToString("F3"))
                + ", actor=" + (actor == null ? "<none>" : actor.npcId)
                + ", target=" + (interaction?.Current == null ? "<none>" : interaction.Current.id)
                + ", targetDoor=" + (interaction?.Current != null && interaction.Current.isDoor)
                + ", inputBlocked=" + (session != null && session.InputBlocked)
                + ", transitioning=" + (world != null && world.IsTransitioning)
                + ", appFocused=" + Application.isFocused
                + ", keyboard.enabled=" + (keyboard != null && keyboard.enabled);
        }

        string ProfileLabel(Live2DModelProfile value)
        {
            return value == null ? "<none>" : value.name + " / " + value.characterId;
        }

        string ReadDialogueTitle(MemoryDialogueView view)
        {
            var field = typeof(MemoryDialogueView).GetField("playerTitle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (field?.GetValue(view) as TMP_Text)?.text ?? "";
        }

        IEnumerator CapturePair(string stem, RectTransform viewport)
        {
            yield return new WaitForEndOfFrame();
            Texture2D captured = null;
            try
            {
                captured = ScreenCapture.CaptureScreenshotAsTexture();
                Require(captured != null, "ScreenCapture returned no texture for " + stem + ".");
                string path = Path.Combine(outputDirectory, stem + ".png");
                File.WriteAllBytes(path, captured.EncodeToPNG());
                screenshots.Add(path);
                if (viewport != null)
                {
                    Texture2D close = CropViewport(captured, viewport);
                    if (close != null)
                    {
                        string closePath = Path.Combine(outputDirectory, stem + "-face.png");
                        File.WriteAllBytes(closePath, close.EncodeToPNG());
                        screenshots.Add(closePath);
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
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            float minX = corners.Min(point => point.x);
            float maxX = corners.Max(point => point.x);
            float minY = corners.Min(point => point.y);
            float maxY = corners.Max(point => point.y);
            float sx = source.width / Mathf.Max(1f, Screen.width);
            float sy = source.height / Mathf.Max(1f, Screen.height);
            int x = Mathf.Clamp(Mathf.FloorToInt(minX * sx), 0, source.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(minY * sy), 0, source.height - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(maxX * sx), x + 1, source.width);
            int top = Mathf.Clamp(Mathf.CeilToInt(maxY * sy), y + 1, source.height);
            var crop = new Texture2D(right - x, top - y, TextureFormat.RGBA32, false);
            crop.SetPixels(source.GetPixels(x, y, right - x, top - y));
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
                if (!moved) { stack.Pop(); continue; }
                var nested = yielded as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
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
            report.missingChecks = missingChecks.ToArray();
            report.replyRecords = replyRecords.ToArray();
            report.failures = failures.ToArray();
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "liuyang-map-qa.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception error)
            {
                Debug.LogError("Liuyang map QA could not write its report: " + error);
            }
            string reportPath = Path.Combine(outputDirectory, "liuyang-map-qa.json");
            if (passed) Debug.Log("LIUYANG_MAP_QA_PASS: " + reportPath);
            else Debug.LogError("LIUYANG_MAP_QA_FAIL: " + reportPath);
            RestoreQaInputFocus();
            if (ownsKeyboard && keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (!Application.isEditor) Application.Quit(passed ? 0 : 1);
            else Destroy(gameObject);
        }
    }
}
