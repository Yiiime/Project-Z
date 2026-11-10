using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Seabreeze
{
    /// <summary>
    /// Offline portrait acceptance for the five non-Zhoubo targets requested by
    /// the current character package. It is inert without -multiPortraitMapQA.
    /// Each actor is opened through Street/map input when possible; the fallback
    /// uses the existing MemoryGameController.BeginPlayerChat entry and is named
    /// explicitly in the JSON report.
    /// </summary>
    public sealed class MultiPortraitMapQA : MonoBehaviour
    {
        const string QaArgument = "-multiPortraitMapQA";
        const float TargetOffset = .12f;
        const float MouthChangeThreshold = .03f;
        const float ExpressionCaptureThreshold = .005f;
        static readonly string[] ActorIds = { "z", "liting", "zhengyi", "xiaoxiaoqing", "liyuan", "linxu", "zhou_bobo", "axing" };

        [Serializable]
        sealed class ActorResult
        {
            public string id;
            public string displayName;
            public string map;
            public string entryPath;
            public bool mapInput;
            public bool beginPlayerChat;
            public string profilePath;
            public string expectedProfile;
            public string actualProfile;
            public string mouthParameter;
            public float mouthBaseline;
            public float mouthMin;
            public float mouthMax;
            public float mouthClosed;
            public string expressionEmotion;
            public string expressionParameter;
            public string expressionStatus;
            public float expressionDelta;
            public string[] screenshots = Array.Empty<string>();
            public bool passed;
            public string error;
        }

        [Serializable]
        sealed class Report
        {
            public string timestampUtc;
            public string unityVersion;
            public string outputDirectory;
            public bool passed;
            public string[] screenshots = Array.Empty<string>();
            public string[] checks = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
            public ActorResult[] actors = Array.Empty<ActorResult>();
        }

        sealed class QAFailure : Exception
        {
            public QAFailure(string message) : base(message) { }
        }

        sealed class ExpressionObservation
        {
            public string id;
            public CubismParameter parameter;
            public float before;
            public float maxDelta;
        }

        static bool bootstrapped;
        readonly List<string> checks = new List<string>();
        readonly List<string> failures = new List<string>();
        readonly List<string> screenshots = new List<string>();
        readonly List<ActorResult> actorResults = new List<ActorResult>();
        Report report;
        string outputDirectory;
        Keyboard keyboard;
        bool ownsKeyboard;
        InputSettings originalInputSettings;
        InputSettings qaInputSettings;
        HideFlags originalInputSettingsFlags;
        bool previousRunInBackground;
        bool targetReady;
        bool openReady;
        string targetDiagnostic = "";
        NpcPresence locatedActor;
        string actorDiagnostic = "";

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
                : Path.Combine(Application.temporaryCachePath, "multi-portrait-map-qa");
            var root = new GameObject("Multi portrait map QA");
            DontDestroyOnLoad(root);
            var qa = root.AddComponent<MultiPortraitMapQA>();
            qa.outputDirectory = Path.GetFullPath(directory);
        }

        void Start()
        {
            EnableQaInputFocus();
            report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                outputDirectory = outputDirectory
            };
            StartCoroutine(Run());
        }

        // This component only exists after -multiPortraitMapQA was supplied.
        // Keep the background-input override local to this QA run.
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
            Finish(failure == null && failures.Count == 0 && actorResults.Count == ActorIds.Length
                && actorResults.All(actor => actor.passed));
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

            checks.Add("multi-portrait QA uses the current catalog IDs: " + string.Join(",", ActorIds));
            foreach (string id in ActorIds)
            {
                var result = new ActorResult { id = id };
                actorResults.Add(result);
                yield return RunActorSafely(id, result, session, world, memory);
            }
        }

        IEnumerator RunActorSafely(string id, ActorResult result, GameSession session,
            WorldDirector world, MemoryGameController memory)
        {
            Exception failure = null;
            yield return RunSafely(RunActor(id, result, session, world, memory), error => failure = error);
            if (failure != null)
            {
                string message = failure is QAFailure ? failure.Message : failure.GetType().Name + ": " + failure.Message;
                result.error = message;
                failures.Add(id + ": " + message);
            }
            if (memory != null && memory.PlayerChatOpen)
            {
                yield return ClosePlayer(memory);
                if (memory.PlayerChatOpen)
                    failures.Add(id + ": failed to close the dialogue during actor cleanup.");
            }
        }

        IEnumerator RunActor(string id, ActorResult result, GameSession session,
            WorldDirector world, MemoryGameController memory)
        {
            var definition = SevenCharacterCatalog.Find(id);
            Require(definition != null, "The catalog has no actor " + id + ".");
            result.displayName = definition.displayName;
            result.map = definition.Place.ToString();
            result.profilePath = definition.portrait;
            Require(!string.IsNullOrWhiteSpace(definition.portrait),
                id + " has no portrait path in the live character catalog.");

            var expectedProfile = Resources.Load<Live2DModelProfile>(definition.portrait);
            Require(expectedProfile != null,
                id + " catalog portrait path does not resolve: " + definition.portrait);
            Require(IsZhouboProfile(expectedProfile) == (id == "zhou_bobo"),
                id + " has an incorrect catalog portrait: " + ProfileLabel(expectedProfile));
            result.expectedProfile = ProfileLabel(expectedProfile);

            if (session.CurrentPlace != definition.Place)
            {
                bool requested = world.TravelTo(definition.Place, null);
                Require(requested, id + " could not request travel to " + definition.Place + ".");
                yield return WaitForWorld(35);
                Require(session.CurrentPlace == definition.Place,
                    id + " arrived at an unexpected map: " + session.CurrentPlace);
                checks.Add(id + " entered its catalog map " + definition.Place + ".");
            }
            else
                checks.Add(id + " remained on its catalog map " + definition.Place + ".");

            yield return WaitForActor(memory, world, id, 15f);
            var actor = locatedActor;
            Require(actor != null,
                id + " is not ready on " + session.CurrentPlace + " after the schedule wait. " + actorDiagnostic);

            var actorShots = new List<string>();
            bool opened = false;
            for (int attempt = 1; attempt <= 3 && !opened; attempt++)
            {
                yield return TryApproachActor(world, memory, actor);
                if (!targetReady)
                {
                    checks.Add(id + " map E attempt " + attempt + " could not confirm its target: " + targetDiagnostic);
                    break;
                }

                result.mapInput = true;
                checks.Add(id + " map E attempt " + attempt + " confirmed target before sending E: " + targetDiagnostic);
                yield return Tap(Key.E);
                yield return WaitForOpen(memory, id, 3f);
                if (openReady)
                {
                    result.entryPath = "map-E / Interactable.Activate";
                    opened = true;
                    checks.Add(id + " opened through the real map E route.");
                }
                else
                {
                    result.mapInput = false;
                    targetDiagnostic = "map E did not open the expected actor: " + DescribeTarget(session, world,
                        world.Player == null ? null : world.Player.GetComponent<PlayerInteraction>(), actor);
                    checks.Add(id + " map E attempt " + attempt + " unavailable; retry diagnostic: " + targetDiagnostic);
                    if (memory.PlayerChatOpen) yield return ClosePlayer(memory);
                }
            }
            if (!opened && !result.mapInput)
                checks.Add(id + " map target unavailable; fallback diagnostic: " + targetDiagnostic);

            if (!opened)
            {
                yield return WaitForActor(memory, world, id, 3f);
                actor = locatedActor;
                Require(actor != null,
                    id + " disappeared before the BeginPlayerChat fallback. " + targetDiagnostic
                    + "; " + actorDiagnostic);
                var playerController = world.Player == null ? null : world.Player.GetComponent<PlayerController>();
                Require(playerController != null, id + " has no PlayerController for the normal entry fallback.");
                if (Vector2.Distance(world.Player.position, actor.transform.position) > 1.75f)
                    playerController.Teleport(new Vector2(actor.transform.position.x + TargetOffset, actor.transform.position.y));
                yield return null;
                bool started = memory.BeginPlayerChat(id, false);
                Require(started, id + " normal BeginPlayerChat fallback returned false.");
                result.beginPlayerChat = true;
                result.entryPath = "MemoryGameController.BeginPlayerChat fallback";
                yield return WaitForOpen(memory, id, 6f);
                Require(openReady, id + " BeginPlayerChat fallback did not open its own actor.");
                checks.Add(id + " opened through the existing BeginPlayerChat entry as a recorded fallback.");
            }

            var character = FindCharacterView(memory);
            Require(character != null, id + " dialogue has no Live2DCharacterView.");
            yield return WaitFor(() => memory.PlayerChatOpen && memory.View.PlayerInputReady
                && character.IsVisible && character.CurrentProfile != null, 5,
                id + " portrait visibility");
            var openedActor = memory.Conversations.PlayerActor;
            Require(openedActor != null && openedActor.npcId == id,
                id + " opened the wrong conversation actor: "
                + (openedActor == null ? "<none>" : openedActor.npcId));
            Require(character.CurrentProfile == expectedProfile,
                id + " opened a profile different from its catalog profile. expected="
                + ProfileLabel(expectedProfile) + ", actual=" + ProfileLabel(character.CurrentProfile));
            result.actualProfile = ProfileLabel(character.CurrentProfile);
            string windowShot = id + "-window.png";
            yield return CaptureScreenshot(windowShot);
            actorShots.Add(windowShot);

            yield return VerifyExpression(id, result, memory.View, character, expectedProfile,
                world.Player.position, actorShots);
            yield return VerifyMouth(id, result, memory.View, character, world.Player.position, actorShots);

            yield return ClosePlayer(memory, character);
            Require(!memory.PlayerChatOpen && !character.IsVisible, id + " dialogue close timed out.");
            Require(memory.Conversations.PlayerActor == null,
                id + " left a player conversation actor after closing the dialogue.");
            string closedShot = id + "-closed.png";
            yield return CaptureScreenshot(closedShot);
            actorShots.Add(closedShot);
            result.screenshots = actorShots.ToArray();
            result.passed = true;
            checks.Add(id + " closed without leaving the previous actor visible before the next switch.");
        }

        IEnumerator WaitForActor(MemoryGameController memory, WorldDirector world, string actorId, float timeout)
        {
            locatedActor = null;
            actorDiagnostic = "actor=" + actorId + ", spawner=<none>";
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                locatedActor = memory?.Spawner?.Actors?.FirstOrDefault(actor => actor != null && actor.npcId == actorId);
                if (IsActorReady(locatedActor, world))
                    yield break;
                actorDiagnostic = DescribeActorRoster(memory, world, actorId);
                yield return null;
            }
            locatedActor = memory?.Spawner?.Actors?.FirstOrDefault(actor => actor != null && actor.npcId == actorId);
            actorDiagnostic = DescribeActorRoster(memory, world, actorId);
            if (!IsActorReady(locatedActor, world)) locatedActor = null;
        }

        bool IsActorReady(NpcPresence actor, WorldDirector world)
        {
            return actor != null && actor.IsPresent && actor.isActiveAndEnabled
                && actor.gameObject.activeInHierarchy && world != null && world.Stage != null
                && actor.gameObject.scene == world.Stage.gameObject.scene
                && actor.Movement != null && actor.Movement.IsReady;
        }

        string DescribeActorRoster(MemoryGameController memory, WorldDirector world, string actorId)
        {
            string roster = memory?.Spawner?.Actors == null ? "<none>" : string.Join(" | ", memory.Spawner.Actors
                .Select(actor => actor == null ? "<null>" : actor.npcId
                    + "@" + actor.transform.position.ToString("F3")
                    + ":present=" + actor.IsPresent
                    + ":active=" + actor.gameObject.activeInHierarchy
                    + ":enabled=" + actor.isActiveAndEnabled
                    + ":scene=" + actor.gameObject.scene.name
                    + ":movementReady=" + (actor.Movement != null && actor.Movement.IsReady)));
            return "requested=" + actorId
                + ", stage=" + (world?.Stage == null ? "<none>" : world.Stage.place.ToString())
                + ", roster=" + roster;
        }

        IEnumerator TryApproachActor(WorldDirector world, MemoryGameController memory, NpcPresence actor)
        {
            targetReady = false;
            targetDiagnostic = actor == null ? "actor=<none>" : "actor=" + actor.npcId;
            var session = GameSession.Instance;
            var player = world == null ? null : world.Player;
            var controller = player == null ? null : player.GetComponent<PlayerController>();
            var interaction = player == null ? null : player.GetComponent<PlayerInteraction>();
            if (session == null || controller == null || interaction == null || actor == null)
                yield break;

            yield return WaitForTarget(session, world, interaction, actor, .75f);
            if (targetReady) yield break;

            float[] offsets = { -TargetOffset, TargetOffset };
            foreach (float offset in offsets)
            {
                controller.Teleport(new Vector2(actor.transform.position.x + offset, actor.transform.position.y));
                Physics2D.SyncTransforms();
                yield return WaitForTarget(session, world, interaction, actor, 3f);
                if (targetReady) yield break;
            }
        }

        IEnumerator WaitForTarget(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor, float timeout)
        {
            targetReady = false;
            int stableFrames = 0;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool matches = IsWorldOperable(session, world, interaction, actor)
                    && interaction.Current != null
                    && interaction.Current.id == actor.npcId
                    && !interaction.Current.isDoor;
                stableFrames = matches ? stableFrames + 1 : 0;
                targetDiagnostic = DescribeTarget(session, world, interaction, actor);
                if (stableFrames >= 2)
                {
                    targetReady = true;
                    yield break;
                }
                yield return null;
            }
        }

        IEnumerator WaitForOpen(MemoryGameController memory, string actorId, float timeout)
        {
            openReady = false;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                var actor = memory?.Conversations?.PlayerActor;
                if (memory != null && memory.PlayerChatOpen && memory.View != null
                    && memory.View.PlayerInputReady && actor != null && actor.npcId == actorId)
                {
                    openReady = true;
                    yield break;
                }
                yield return null;
            }
        }

        bool IsWorldOperable(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor)
        {
            return session != null && session.IsReady && !session.InputBlocked && !session.NonPlayerInputBlocked
                && world != null && world.Player != null && world.Stage != null && !world.IsTransitioning
                && Time.timeScale > 0 && interaction != null && actor != null && actor.IsPresent
                && actor.isActiveAndEnabled && actor.Movement != null && actor.Movement.IsReady
                && actor.gameObject.scene == world.Stage.gameObject.scene;
        }

        string DescribeTarget(GameSession session, WorldDirector world,
            PlayerInteraction interaction, NpcPresence actor)
        {
            var current = interaction == null ? null : interaction.Current;
            return "player=" + (world?.Player == null ? "<none>" : world.Player.position.ToString("F3"))
                + ", actor=" + (actor == null ? "<none>" : actor.npcId)
                + ", actorPosition=" + (actor == null ? "<none>" : actor.transform.position.ToString("F3"))
                + ", target=" + (current == null ? "<none>" : current.id)
                + ", targetDoor=" + (current != null && current.isDoor)
                + ", inputBlocked=" + (session != null && session.InputBlocked)
                + ", nonPlayerInputBlocked=" + (session != null && session.NonPlayerInputBlocked)
                + ", transitioning=" + (world != null && world.IsTransitioning)
                + ", timeScale=" + Time.timeScale
                + ", actorPresent=" + (actor != null && actor.IsPresent)
                + ", movementReady=" + (actor != null && actor.Movement != null && actor.Movement.IsReady)
                + ", appFocused=" + Application.isFocused
                + ", keyboard.enabled=" + (keyboard != null && keyboard.enabled)
                + ", currentKeyboard.enabled=" + (Keyboard.current != null && Keyboard.current.enabled);
        }

        IEnumerator VerifyExpression(string id, ActorResult result, MemoryDialogueView view,
            Live2DCharacterView character, Live2DModelProfile profile, Vector3 position,
            List<string> actorShots)
        {
            var binding = profile.expressions == null ? null : profile.expressions.FirstOrDefault(item =>
                item != null && item.expression != null && !string.IsNullOrEmpty(item.emotion)
                    && !string.Equals(item.emotion, profile.defaultEmotion, StringComparison.OrdinalIgnoreCase));
            if (binding == null)
            {
                result.expressionStatus = "skipped: profile has no non-default expression binding";
                checks.Add(id + " has no non-default expression binding; expression observation was not claimed.");
                yield break;
            }

            var observations = new List<ExpressionObservation>();
            foreach (var entry in binding.expression.Parameters)
            {
                if (IsAutomaticParameter(profile, entry.Id)) continue;
                var parameter = FindParameter(character, entry.Id);
                if (parameter != null)
                    observations.Add(new ExpressionObservation
                    {
                        id = entry.Id,
                        parameter = parameter,
                        before = parameter.Value
                    });
            }
            Require(observations.Count > 0,
                id + " expression " + binding.emotion
                + " has no expression-specific parameters in the instantiated model.");

            // Use the same incoming-line path as a real NPC reply. This verifies
            // MemoryDialogueView's expression selection, preparation, typewriter
            // and mouth handoff instead of calling Live2DCharacterView directly.
            view.ShowLine(id, "这句本地台词正在展示当前角色的表情。", position, true, binding.emotion);
            bool captured = false;
            string expressionShot = id + "-expression-" + binding.emotion + ".png";
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                foreach (var observation in observations)
                    observation.maxDelta = Mathf.Max(observation.maxDelta,
                        Mathf.Abs(observation.parameter.Value - observation.before));
                if (!captured && observations.Max(observation => observation.maxDelta) >= ExpressionCaptureThreshold)
                {
                    yield return CaptureScreenshot(expressionShot);
                    captured = true;
                    actorShots.Add(expressionShot);
                }
            }
            float maxDelta = observations.Max(observation => observation.maxDelta);
            result.expressionEmotion = binding.emotion;
            result.expressionParameter = observations.OrderByDescending(observation => observation.maxDelta)
                .First().id;
            result.expressionDelta = maxDelta;
            Require(maxDelta > ExpressionCaptureThreshold,
                id + " expression " + binding.emotion + " did not change a live Cubism parameter. delta=" + maxDelta);
            result.expressionStatus = "observed: " + binding.emotion + ", maxDelta=" + maxDelta.ToString("F4");
            Require(captured, id + " expression " + binding.emotion
                + " never produced a capturable parameter change.");

            // Replace the expression line through the same dialogue path and wait
            // for its typewriter mouth to return to the closed state. VerifyMouth
            // then starts from this real line handoff, not a direct reset.
            var mouth = FindParameter(character, profile.mouthParameter);
            Require(mouth != null, id + " mouth parameter disappeared during expression recovery.");
            view.ShowLine(id, "现在恢复自然说话状态。", position, true, null);
            int closedFrames = 0;
            float closeDeadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < closeDeadline && closedFrames < 8)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(mouth.Value) < .08f) closedFrames++;
                else closedFrames = 0;
            }
            Require(closedFrames >= 8,
                id + " expression recovery line did not return the mouth to a closed state. value=" + mouth.Value);
            checks.Add(id + " expression " + binding.emotion + " changed a live parameter and was captured.");
            checks.Add(id + " replaced the expression line through ShowLine and waited for mouth close.");
        }

        bool IsAutomaticParameter(Live2DModelProfile profile, string id)
        {
            if (string.IsNullOrEmpty(id)) return true;
            return string.Equals(id, profile.mouthParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, profile.breathParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, profile.leftEyeParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, profile.rightEyeParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "ParamAngleZ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "ParamBodyAngleZ", StringComparison.OrdinalIgnoreCase);
        }

        IEnumerator VerifyMouth(string id, ActorResult result, MemoryDialogueView view,
            Live2DCharacterView character, Vector3 position, List<string> actorShots)
        {
            var profile = character.CurrentProfile;
            Require(profile != null, id + " profile disappeared before the mouth check.");
            string mouthParameter = profile.mouthParameter;
            if (string.Equals(profile.characterId, "xiaoxiaoqing", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(profile.neutralSpeechParameter))
                mouthParameter = profile.neutralSpeechParameter;
            var mouth = FindParameter(character, mouthParameter);
            Require(mouth != null, id + " configured mouth parameter was not found: " + mouthParameter);
            result.mouthParameter = mouthParameter;

            // VerifyExpression may have just replaced an expression line through
            // ShowLine(..., null). Wait for both the typewriter and the actual
            // selected speech channel to settle before sampling the baseline.
            int idleFrames = 0;
            float idleDeadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < idleDeadline && idleFrames < 8)
            {
                yield return new WaitForEndOfFrame();
                var history = view.DialogueHistory;
                bool typewriterFinished = history == null || history.textInfo.characterCount == 0
                    || history.maxVisibleCharacters >= history.textInfo.characterCount;
                bool mouthIdle = Mathf.Abs(mouth.Value - mouth.DefaultValue) < .08f;
                idleFrames = typewriterFinished && mouthIdle ? idleFrames + 1 : 0;
            }
            Require(idleFrames >= 8,
                id + " previous line did not finish typing and return the selected mouth channel to idle. value=" + mouth.Value);

            float baseline = 0;
            int baselineFrames = 0;
            float baselineDeadline = Time.realtimeSinceStartup + .35f;
            while (Time.realtimeSinceStartup < baselineDeadline)
            {
                yield return new WaitForEndOfFrame();
                baseline += mouth.Value;
                baselineFrames++;
            }
            baseline = baselineFrames == 0 ? mouth.Value : baseline / baselineFrames;
            string line = "这是本地固定台词，用来确认当前角色的打字机、口型播放和说完后的自然收口。";
            view.ShowLine(id, line, position, true);
            float min = mouth.Value;
            float max = mouth.Value;
            bool captured = false;
            string mouthShot = id + "-mouth.png";
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                min = Mathf.Min(min, mouth.Value);
                if (mouth.Value > max)
                {
                    max = mouth.Value;
                    if (max > baseline + MouthChangeThreshold)
                    {
                        yield return CaptureScreenshot(mouthShot, false);
                        captured = true;
                    }
                }
            }
            result.mouthBaseline = baseline;
            result.mouthMin = min;
            result.mouthMax = max;
            Require(max - baseline > MouthChangeThreshold,
                id + " fixed line did not change the live mouth parameter. baseline=" + baseline + ", max=" + max);
            Require(captured, id + " fixed line never produced a capturable open-mouth frame.");
            screenshots.Add(Path.Combine(outputDirectory, mouthShot));
            actorShots.Add(mouthShot);

            int closedFrames = 0;
            float closeDeadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < closeDeadline && closedFrames < 8)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(mouth.Value - baseline) < .08f) closedFrames++;
                else closedFrames = 0;
            }
            result.mouthClosed = mouth.Value;
            Require(closedFrames >= 8,
                id + " fixed line did not settle back to a closed mouth. value=" + mouth.Value);
            string closedMouthShot = id + "-mouth-closed.png";
            yield return CaptureScreenshot(closedMouthShot);
            actorShots.Add(closedMouthShot);
            checks.Add(id + " fixed local line changed the mouth and returned it to a closed state.");
        }

        bool IsZhouboProfile(Live2DModelProfile profile)
        {
            if (profile == null) return false;
            return profile.name.IndexOf("zhoubo", StringComparison.OrdinalIgnoreCase) >= 0
                || profile.characterId.IndexOf("zhoubo", StringComparison.OrdinalIgnoreCase) >= 0
                || (profile.prefab != null && profile.prefab.name.IndexOf("zhoubo", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        string ProfileLabel(Live2DModelProfile profile)
        {
            if (profile == null) return "<none>";
            return profile.name + " / " + (profile.prefab == null ? "<no prefab>" : profile.prefab.name)
                + " / " + profile.characterId;
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
            if (model == null) return null;
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

        IEnumerator ClosePlayer(MemoryGameController memory, Live2DCharacterView character = null)
        {
            if (memory == null) yield break;
            float deadline = Time.realtimeSinceStartup + 5f;
            int attempts = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool closed = !memory.PlayerChatOpen && (character == null || !character.IsVisible);
                if (closed) yield break;

                // Closing is already a real UI transition. Do not inject another
                // Escape while it is in flight; wait for it to finish instead.
                if (memory.View != null && memory.View.Closing)
                {
                    yield return null;
                    continue;
                }
                if (!memory.PlayerChatOpen)
                {
                    yield return null;
                    continue;
                }
                if (attempts >= 3)
                {
                    yield return null;
                    continue;
                }
                attempts++;
                yield return Tap(Key.Escape);
                float settleDeadline = Time.realtimeSinceStartup + .8f;
                while (Time.realtimeSinceStartup < settleDeadline
                    && memory.PlayerChatOpen
                    && (memory.View == null || !memory.View.Closing))
                    yield return null;
            }
        }

        IEnumerator Tap(Key key)
        {
            var inputKeyboard = AcquireKeyboard();
            // Release any stale hardware key state before sending the actual tap.
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
                if (ownsKeyboard && keyboard != null)
                    InputSystem.RemoveDevice(keyboard);
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

        IEnumerator CaptureScreenshot(string name, bool record = true)
        {
            string path = Path.Combine(outputDirectory, name);
            yield return new WaitForEndOfFrame();
            Texture2D captured = null;
            try
            {
                captured = ScreenCapture.CaptureScreenshotAsTexture();
                Require(captured != null, "ScreenCapture returned no texture for " + name + ".");
                File.WriteAllBytes(path, captured.EncodeToPNG());
                if (record) screenshots.Add(path);
            }
            catch (Exception error)
            {
                failures.Add("Screenshot " + name + ": " + error.Message);
            }
            if (captured != null) Destroy(captured);
            yield return null;
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
            report.actors = actorResults.ToArray();
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "multi-portrait-map-qa.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception error)
            {
                Debug.LogError("Multi portrait map QA could not write its report: " + error);
            }
            if (passed) Debug.Log("MULTI_PORTRAIT_MAP_QA_PASS: " + Path.Combine(outputDirectory, "multi-portrait-map-qa.json"));
            else Debug.LogError("MULTI_PORTRAIT_MAP_QA_FAIL: " + Path.Combine(outputDirectory, "multi-portrait-map-qa.json"));
            RestoreQaInputFocus();
            if (ownsKeyboard && keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (!Application.isEditor) Application.Quit(passed ? 0 : 1);
            else Destroy(gameObject);
        }
    }
}
