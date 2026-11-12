using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Seabreeze
{
    /// <summary>
    /// Standalone verification for Zhoubo's daytime-map portrait.
    /// It is inert unless the player is launched with -zhouboMapQA &lt;directory&gt;.
    /// </summary>
    public sealed class ZhouboMapQA : MonoBehaviour
    {
        const string PreviewArgument = "-zhoubo-final-preview";
        const string LocalPreviewArgument = "-localPortraitPreview";
        const string RequireMemoryReplyArgument = "-qaRequireMemoryReply";
        const string QaArgument = "-zhouboMapQA";
        const string ActorId = "zhou_bobo";
        const float Frame194Seconds = 8.083333f;
        const float TalkCaptureThreshold = .55f;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        sealed class Report
        {
            public string timestampUtc;
            public string unityVersion;
            public string outputDirectory;
            public bool finalPreviewArgument;
            public bool localPortraitPreviewArgument;
            public bool requireMemoryReplyArgument;
            public bool passed;
            public string dialogueMode;
            public string modelProfile;
            public string mouthParameter;
            public float idleMouth;
            public float talkingMouthMin;
            public float talkingMouthMax;
            public float closedMouth;
            public float frame194LeftEye;
            public float frame194RightEye;
            public float frame194ExpectedEye;
            public float frame194SampledLeftEye;
            public float frame194SampledRightEye;
            public float frame194RenderedLeftEye;
            public float frame194RenderedRightEye;
            public int frame194HoldFrames;
            public bool frame194UsedNaturalFallback;
            public float naturalBreathMin;
            public float naturalBreathMax;
            public float naturalLeftEyeMin;
            public float naturalLeftEyeMax;
            public float naturalRightEyeMin;
            public float naturalRightEyeMax;
            public float naturalClosestEye;
            public float naturalClosestSeconds;
            public float naturalClosestAnimatorTime;
            public string animatorState;
            public float animatorNormalizedTime;
            public string animationClip;
            public float animationClipLength;
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
        bool finalPreview;
        bool localPortraitPreview;
        bool requireMemoryReply;
        Keyboard keyboard;
        bool ownsKeyboard;
        InputSettings originalInputSettings;
        InputSettings qaInputSettings;
        HideFlags originalInputSettingsFlags;
        bool previousRunInBackground;
        float idleMouth;
        float talkingMin = float.MaxValue;
        float talkingMax = float.MinValue;
        float closedMouth;
        bool frame194Holding;
        AnimationClip frame194HoldClip;
        Animator frame194HoldAnimator;
        CubismModel frame194HoldModel;
        float frame194HoldTime;
        string dialogueMode = "";
        string modelProfile = "";
        string mouthParameter = "";
        bool targetReady;
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
                : Path.Combine(Application.temporaryCachePath, "zhoubo-map-qa");
            var root = new GameObject("Zhoubo map QA");
            DontDestroyOnLoad(root);
            var qa = root.AddComponent<ZhouboMapQA>();
            qa.outputDirectory = Path.GetFullPath(directory);
            qa.finalPreview = Array.IndexOf(args, PreviewArgument) >= 0;
            qa.localPortraitPreview = Array.IndexOf(args, LocalPreviewArgument) >= 0;
            qa.requireMemoryReply = Array.IndexOf(args, RequireMemoryReplyArgument) >= 0;
        }

        void Start()
        {
            EnableQaInputFocus();
            report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                outputDirectory = outputDirectory,
                finalPreviewArgument = finalPreview,
                localPortraitPreviewArgument = localPortraitPreview,
                requireMemoryReplyArgument = requireMemoryReply
            };
            StartCoroutine(Run());
        }

        // This component only exists after -zhouboMapQA was supplied. Keep the
        // background-input override local to this standalone acceptance run.
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

        void LateUpdate()
        {
            if (!frame194Holding || frame194HoldClip == null || frame194HoldAnimator == null) return;
            frame194HoldClip.SampleAnimation(frame194HoldAnimator.gameObject, frame194HoldTime);
            if (frame194HoldModel != null) frame194HoldModel.ForceUpdateNow();
        }

        IEnumerator Run()
        {
            Exception failure = null;
            yield return RunSafely(RunCore(), error => failure = error);
            if (failure != null)
                failures.Add(failure is QAFailure ? failure.Message : failure.GetType().Name + ": " + failure.Message);
            Finish(failure == null && failures.Count == 0);
        }

        IEnumerator RunCore()
        {
            Directory.CreateDirectory(outputDirectory);
            yield return WaitForWorld(90);
            var session = GameSession.Instance;
            var world = WorldDirector.Instance;
            var memory = session.Memory;
            Require(memory != null, "Memory runtime is not initialized.");

            // The launcher already supplied a fresh temporary save root. Preserve the
            // current daytime state so this check exercises the real map entry path.
            if (session.CurrentPlace != PlaceId.Street)
            {
                world.TravelTo(PlaceId.Street, null);
                yield return WaitForWorld(30);
            }
            Require(session.CurrentPlace == PlaceId.Street, "QA could not reach the Street map.");
            string flagsBefore = string.Join("|", session.State.flags.OrderBy(x => x, StringComparer.Ordinal));
            checks.Add("isolated daytime run from the launcher save root");

            yield return WaitForZhoubo(memory, world, 30);
            var zhoubo = memory.Spawner.Find(ActorId);
            Require(zhoubo != null && zhoubo.IsPresent, "zhou_bobo is not present on the Street map at the current daytime period.");
            checks.Add("zhou_bobo is present at the current scheduled Street position");
            yield return CaptureScreenshot("zhoubo-day-map.png");
            checks.Add("captured the real daytime Street map before opening dialogue");

            yield return EnsureZhouboConversation(world, memory);
            var character = FindCharacterView(memory);
            Require(character != null, "The dialogue UI has no Live2D portrait view.");
            yield return WaitFor(() => character.IsVisible || finalPreview, 4, "Zhoubo portrait visibility");
            if (!finalPreview)
            {
                Require(!memory.ZhouboPortraitFinal, "The normal Zhoubo interaction opened in final mode.");
                RequireDailyProfile(character, "The normal Zhoubo interaction did not show the old daily portrait.");
                yield return CaptureScreenshot("zhoubo-daily-window.png");
                checks.Add("normal E interaction opens the old Zhoubo daily portrait");
                yield return Tap(Key.Escape);
                yield return WaitFor(() => !memory.PlayerChatOpen && !character.IsVisible, 4, "daily dialogue close");
                Require(!session.InputBlocked, "Player input remains blocked after closing the daily Zhoubo dialogue.");
                checks.Add("closed the old Zhoubo daily dialogue before the same-location Shift+E switch");

                yield return EnsureZhouboFinalConversation(world, memory);
                character = FindCharacterView(memory);
                Require(character != null, "The final-preview interaction lost its Live2D portrait view.");
                yield return WaitFor(() => memory.ZhouboPortraitFinal && character.IsVisible && memory.PlayerChatOpen, 4,
                    "same-location Shift+E final portrait");
                yield return CaptureScreenshot("zhoubo-final-window.png");
            }
            else
            {
                yield return WaitFor(() => memory.ZhouboPortraitFinal && character.IsVisible, 4, "final portrait preview");
                yield return CaptureScreenshot("zhoubo-final-window.png");
            }

            Require(memory.ZhouboPortraitFinal, "The Zhoubo final portrait was not selected after the normal/manual switch.");
            Require(character.IsVisible, "The Zhoubo final Live2D portrait is not visible in the dialogue UI.");
            var profile = character.CurrentProfile;
            Require(profile != null && profile.prefab != null, "The Zhoubo portrait profile or prefab is missing.");
            Require(profile.name.IndexOf("ZhouboFinal-v03", StringComparison.OrdinalIgnoreCase) >= 0
                || profile.prefab.name.IndexOf("ZhouboFinal", StringComparison.OrdinalIgnoreCase) >= 0,
                "The visible Live2D profile is not the Zhoubo final v03 asset.");
            modelProfile = profile.name + " / " + profile.prefab.name + " / " + profile.characterId;
            mouthParameter = profile.mouthParameter;
            report.modelProfile = modelProfile;
            report.mouthParameter = mouthParameter;
            checks.Add("dialogue UI shows the real Zhoubo final v03 Cubism profile");

            var mouth = FindParameter(character, mouthParameter);
            Require(mouth != null, "The configured mouth parameter was not found in the instantiated Cubism model.");
            var breath = FindParameter(character, profile.breathParameter);
            var leftEye = FindParameter(character, profile.leftEyeParameter);
            var rightEye = FindParameter(character, profile.rightEyeParameter);
            Require(breath != null, "The configured breath parameter was not found in the instantiated Cubism model.");
            Require(leftEye != null && rightEye != null, "The configured eye parameters were not found in the instantiated Cubism model.");
            yield return SampleIdle(mouth);
            yield return CaptureScreenshot("zhoubo-idle.png");
            yield return SampleNaturalMotion(character, breath, leftEye, rightEye);
            yield return CaptureAtFrame194(character, leftEye, rightEye);

            if (requireMemoryReply)
            {
                dialogueMode = "memory-service";
                yield return WaitForMemoryService(memory.Client, 20);
                checks.Add("MemoryLab became CanGenerate within the optional 20-second service wait");
                int committedBefore = memory.Conversations.CommittedNpcLines;
                SendThroughDialogueInput(memory.View, "周伯伯，今天的光线很好，先聊一句。");
                float deadline = Time.realtimeSinceStartup + 40;
                while (memory.Conversations.CommittedNpcLines <= committedBefore && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (memory.Conversations.CommittedNpcLines <= committedBefore)
                    throw new QAFailure("MemoryLab did not return a Zhoubo NPC reply before timeout. CanGenerate="
                        + memory.Client.CanGenerate + ", Sending=" + memory.Conversations.Sending
                        + ", Status=" + memory.Client.Status + ", LastError=" + memory.Client.LastError);
                checks.Add("MemoryLab returned a committed Zhoubo NPC reply");
            }
            else
            {
                dialogueMode = "fixed-line-fallback";
                // Offline QA deliberately bypasses MemoryLab and uses the existing dialogue
                // typewriter path with a deterministic local line.
                memory.View.ShowLine(ActorId, "周伯伯把相片收好，抬头看了你一眼。", world.Player.position, true);
                checks.Add("offline mode uses a fixed local line through the existing typewriter path");
            }
            report.dialogueMode = dialogueMode;
            yield return SampleTalking(mouth);
            yield return WaitForClosedMouth(mouth);
            checks.Add(requireMemoryReply
                ? "MemoryLab NPC reply drives typewriter mouth and returns to a closed state"
                : "fixed local typewriter line drives mouth and returns to a closed state");

            yield return Tap(Key.Escape);
            yield return WaitFor(() => !memory.PlayerChatOpen && !character.IsVisible, 4, "dialogue close");
            Require(!session.InputBlocked, "Player input remains blocked after closing Zhoubo dialogue.");
            Require(string.Join("|", session.State.flags.OrderBy(x => x, StringComparer.Ordinal)) == flagsBefore,
                "Closing the local preview changed story flags.");
            yield return CaptureScreenshot("zhoubo-closed-window.png");
            checks.Add("captured the real Street map after closing the dialogue window");
            checks.Add("Esc closes the dialogue, hides the model and restores player input");

            yield return VerifyOtherNpcReset(world, memory, character);
            yield return VerifySceneSwitch(session, world, memory);
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

        IEnumerator WaitForWorld(float seconds)
        {
            yield return WaitFor(() => GameSession.Instance != null && GameSession.Instance.IsReady
                && WorldDirector.Instance != null && WorldDirector.Instance.Stage != null
                && !WorldDirector.Instance.IsTransitioning, seconds, "world initialization");
        }

        IEnumerator WaitForZhoubo(MemoryGameController memory, WorldDirector world, float seconds)
        {
            yield return WaitFor(() => world.Stage != null && world.Stage.place == PlaceId.Street
                && memory.Spawner != null && memory.Spawner.Find(ActorId) != null, seconds, "Zhoubo Street spawn");
        }

        IEnumerator EnsureZhouboConversation(WorldDirector world, MemoryGameController memory)
        {
            var character = FindCharacterView(memory);
            if (memory.PlayerChatOpen)
                yield return WaitFor(() => memory.View.PlayerInputReady, 4, "preview dialogue ready");
            else
            {
                yield return OpenActorWithRetry(world, memory, ActorId, "Street E interaction", false,
                    () => memory.PlayerChatOpen && memory.View != null && memory.View.PlayerInputReady
                        && memory.Conversations != null && memory.Conversations.PlayerActor != null
                        && memory.Conversations.PlayerActor.npcId == ActorId);
            }
            character = FindCharacterView(memory);
            Require(character != null, "The map interaction opened without a portrait view.");
        }

        IEnumerator EnsureZhouboFinalConversation(WorldDirector world, MemoryGameController memory)
        {
            yield return OpenActorWithRetry(world, memory, ActorId,
                "same-location Shift+E final interaction", true,
                () => memory.PlayerChatOpen && memory.View != null && memory.View.PlayerInputReady
                    && memory.ZhouboPortraitFinal && memory.Conversations != null
                    && memory.Conversations.PlayerActor != null
                    && memory.Conversations.PlayerActor.npcId == ActorId);
            checks.Add("same-location Shift+E opens Zhoubo's final portrait in the existing dialogue window");
        }

        IEnumerator OpenActorWithRetry(WorldDirector world, MemoryGameController memory, string actorId,
            string label, bool shiftInteract, Func<bool> opened)
        {
            var session = GameSession.Instance;
            var interaction = world?.Player == null ? null : world.Player.GetComponent<PlayerInteraction>();
            string lastDiagnostic = "<no attempt>";
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                yield return ApproachActor(world, memory, actorId, label);
                var actor = memory?.Spawner?.Find(actorId);
                if (!targetReady)
                {
                    lastDiagnostic = DescribeInputState(session, world, memory, interaction, actor);
                    checks.Add(label + " attempt " + attempt + " could not reselect the actor: " + lastDiagnostic);
                    continue;
                }

                lastDiagnostic = DescribeInputState(session, world, memory, interaction, actor);
                checks.Add(label + " attempt " + attempt + " before key: " + lastDiagnostic);
                if (shiftInteract) yield return TapShiftE();
                else yield return Tap(Key.E);

                float deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline && !opened())
                    yield return null;
                if (opened())
                {
                    checks.Add(label + " opened on real keyboard attempt " + attempt + ".");
                    yield break;
                }

                actor = memory?.Spawner?.Find(actorId);
                lastDiagnostic = DescribeInputState(session, world, memory, interaction, actor);
                checks.Add(label + " attempt " + attempt + " timed out after key: " + lastDiagnostic);
                if (memory != null && memory.PlayerChatOpen)
                {
                    yield return Tap(Key.Escape);
                    float closeDeadline = Time.realtimeSinceStartup + 4f;
                    while (memory.PlayerChatOpen && Time.realtimeSinceStartup < closeDeadline)
                        yield return null;
                }
                yield return null;
            }

            Require(false, label + " timed out after real target/key retries. " + lastDiagnostic);
        }

        IEnumerator ApproachActor(WorldDirector world, MemoryGameController memory, string actorId, string label)
        {
            var session = GameSession.Instance;
            var player = world == null ? null : world.Player;
            var playerController = player == null ? null : player.GetComponent<PlayerController>();
            var playerInteraction = player == null ? null : player.GetComponent<PlayerInteraction>();
            var actor = memory?.Spawner?.Find(actorId);
            Require(session != null && playerController != null && playerInteraction != null,
                label + " cannot inspect the player session, controller or interaction target.");
            Require(actor != null && actor.IsPresent, "Cannot locate " + actorId + " before " + label + ".");
            yield return WaitFor(() => IsWorldOperable(session, world, playerInteraction, actor), 8,
                label + " world readiness");

            // If the player is already in range, preserve that location. Otherwise
            // place the player close to the actor and let PlayerInteraction select it.
            yield return WaitForTarget(session, world, playerInteraction, actor, .75f, label);
            if (targetReady) yield break;

            float[] offsets = { -.12f, .12f };
            foreach (float offset in offsets)
            {
                playerController.Teleport(new Vector2(actor.transform.position.x + offset, actor.transform.position.y));
                Physics2D.SyncTransforms();
                yield return WaitForTarget(session, world, playerInteraction, actor, 3f, label);
                if (targetReady) yield break;
            }

            // The caller may re-align and retry. It reports the final target
            // state only after all real input attempts are exhausted.
        }

        IEnumerator WaitForTarget(GameSession session, WorldDirector world, PlayerInteraction playerInteraction,
            NpcPresence actor, float timeout, string label)
        {
            targetReady = false;
            int stableTargetFrames = 0;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool matches = IsWorldOperable(session, world, playerInteraction, actor)
                    && playerInteraction.Current != null
                    && playerInteraction.Current.id == actor.npcId
                    && !playerInteraction.Current.isDoor;
                stableTargetFrames = matches ? stableTargetFrames + 1 : 0;
                targetDiagnostic = label + ": " + DescribeTarget(session, world, playerInteraction, actor);
                if (stableTargetFrames >= 2)
                {
                    targetReady = true;
                    yield break;
                }
                yield return null;
            }
        }

        void RequireDailyProfile(Live2DCharacterView character, string message)
        {
            var expected = Resources.Load<Live2DModelProfile>("Memory/Portraits/ZhouboDaily");
            var profile = character == null ? null : character.CurrentProfile;
            Require(expected != null, "The old ZhouboDaily profile asset is missing.");
            Require(profile != null && (profile == expected
                || profile.name.IndexOf("ZhouboDaily", StringComparison.OrdinalIgnoreCase) >= 0
                || (profile.prefab != null && profile.prefab.name.IndexOf("ZhouboDaily", StringComparison.OrdinalIgnoreCase) >= 0)),
                message);
        }

        IEnumerator SampleIdle(CubismParameter mouth)
        {
            float sum = 0;
            int count = 0;
            float deadline = Time.realtimeSinceStartup + .6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                sum += mouth.Value;
                count++;
            }
            idleMouth = count == 0 ? mouth.Value : sum / count;
            closedMouth = mouth.Value;
            report.idleMouth = idleMouth;
        }

        IEnumerator SampleNaturalMotion(Live2DCharacterView character, CubismParameter breath, CubismParameter leftEye, CubismParameter rightEye)
        {
            float breathMin = float.MaxValue, breathMax = float.MinValue;
            float leftMin = float.MaxValue, leftMax = float.MinValue;
            float rightMin = float.MaxValue, rightMax = float.MinValue;
            float closestError = float.MaxValue, closestEye = 0, closestSeconds = 0, closestAnimatorTime = 0;
            bool capturedNearEye = false;
            var model = character.CurrentModel;
            var animator = model == null ? null : model.GetComponent<Animator>();
            float started = Time.realtimeSinceStartup;
            float deadline = Time.realtimeSinceStartup + 12.15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                breathMin = Mathf.Min(breathMin, breath.Value);
                breathMax = Mathf.Max(breathMax, breath.Value);
                leftMin = Mathf.Min(leftMin, leftEye.Value);
                leftMax = Mathf.Max(leftMax, leftEye.Value);
                rightMin = Mathf.Min(rightMin, rightEye.Value);
                rightMax = Mathf.Max(rightMax, rightEye.Value);
                float eye = (leftEye.Value + rightEye.Value) * .5f;
                float error = Mathf.Abs(eye - .3313f);
                if (error < closestError)
                {
                    closestError = error;
                    closestEye = eye;
                    closestSeconds = Time.realtimeSinceStartup - started;
                    closestAnimatorTime = animator == null ? 0 : animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                }
                if (!capturedNearEye && error < .08f)
                {
                    yield return CaptureScreenshot("zhoubo-natural-eye-near-331.png");
                    capturedNearEye = true;
                }
            }
            report.naturalBreathMin = breathMin;
            report.naturalBreathMax = breathMax;
            report.naturalLeftEyeMin = leftMin;
            report.naturalLeftEyeMax = leftMax;
            report.naturalRightEyeMin = rightMin;
            report.naturalRightEyeMax = rightMax;
            report.naturalClosestEye = closestEye;
            report.naturalClosestSeconds = closestSeconds;
            report.naturalClosestAnimatorTime = closestAnimatorTime;
            Require(breathMax - breathMin > .08f,
                "The 12-second natural idle sample did not move ParamBreath. range=" + (breathMax - breathMin));
            Require(leftMax - leftMin > .25f && rightMax - rightMin > .25f,
                "The 12-second natural idle sample did not show a full blink range. eyes="
                + (leftMax - leftMin) + "/" + (rightMax - rightMin));
            Require(closestError < .12f, "Natural idle playback never reached the expected half-closed eye value near .3313.");
            Require(capturedNearEye, "Natural idle playback found .3313 in parameters but could not capture the rendered near-eye frame.");
            checks.Add("12-second natural Animator playback moves breath, closes both eyes and captures a near-.3313 eye frame");
        }

        IEnumerator SampleTalking(CubismParameter mouth)
        {
            talkingMin = float.MaxValue;
            talkingMax = float.MinValue;
            bool captured = false;
            string capturePath = Path.Combine(outputDirectory, "zhoubo-talk.png");
            float deadline = Time.realtimeSinceStartup + (requireMemoryReply ? 5f : 2.2f);
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                talkingMin = Mathf.Min(talkingMin, mouth.Value);
                talkingMax = Mathf.Max(talkingMax, mouth.Value);
                if (mouth.Value >= TalkCaptureThreshold && (!captured || mouth.Value >= talkingMax - .001f))
                {
                    yield return CaptureScreenshot("zhoubo-talk.png", false);
                    captured = true;
                }
            }
            if (captured) screenshots.Add(capturePath);
            report.talkingMouthMin = talkingMin;
            report.talkingMouthMax = talkingMax;
            Require(captured && talkingMax >= TalkCaptureThreshold,
                "The mouth parameter did not reach the visible open-mouth threshold of .55 during the typewriter line. max="
                + talkingMax);
            checks.Add("captured a real speaking frame while the typewriter line was still revealing");
        }

        IEnumerator WaitForClosedMouth(CubismParameter mouth)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            int closedFrames = 0;
            while (Time.realtimeSinceStartup < deadline && closedFrames < 8)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(mouth.Value - idleMouth) < .08f) closedFrames++;
                else closedFrames = 0;
            }
            closedMouth = mouth.Value;
            report.closedMouth = closedMouth;
            Require(Mathf.Abs(closedMouth - idleMouth) < .08f,
                "The mouth parameter did not settle back to the idle value after speaking.");
            Require(closedFrames >= 8, "The typewriter line did not reach a stable closed-mouth state before timeout.");
        }

        IEnumerator CaptureAtFrame194(Live2DCharacterView character, CubismParameter leftEye, CubismParameter rightEye)
        {
            var currentModel = character.CurrentModel;
            var animator = currentModel == null ? null : currentModel.GetComponent<Animator>();
            Require(animator != null, "The Zhoubo model has no Animator for the fixed idle-frame sample.");
            var controller = animator.runtimeAnimatorController;
            Require(controller != null, "The Zhoubo Animator has no runtime controller for the fixed idle-frame sample.");
            var clip = controller.animationClips
                .Where(candidate => candidate != null && candidate.length >= Frame194Seconds)
                .OrderByDescending(candidate => candidate.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(candidate => candidate.length)
                .FirstOrDefault();
            Require(clip != null, "The Zhoubo Animator controller has no idle clip long enough for 8.083 seconds.");
            report.animationClip = clip.name;
            report.animationClipLength = clip.length;
            Require(Mathf.Abs(clip.length - 12f) < .25f,
                "The Zhoubo idle clip is not the expected 12-second source. length=" + clip.length);

            yield return new WaitForEndOfFrame();
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            Require(current.fullPathHash != 0, "The Zhoubo Animator has no active default state for the fixed idle-frame sample.");
            float oldSpeed = animator.speed;
            float normalizedTime = Frame194Seconds / clip.length;
            yield return CaptureScreenshot("zhoubo-frame-194-before.png");
            animator.speed = 0;
            // Keep the real default state's full path; do not guess a short state hash.
            animator.Play(current.fullPathHash, 0, Mathf.Repeat(normalizedTime, 1));
            animator.Update(0);
            var model = character.CurrentModel;
            var store = model == null ? null : model.GetComponent<CubismParameterStore>();
            bool storeWasEnabled = store != null && store.enabled;
            if (store != null) store.enabled = false;
            frame194HoldClip = clip;
            frame194HoldAnimator = animator;
            frame194HoldModel = model;
            frame194HoldTime = Frame194Seconds;
            frame194Holding = true;
            report.frame194HoldFrames = 4;
            for (int frame = 0; frame < report.frame194HoldFrames; frame++)
            {
                // Reapply the imported motion and force the same live Cubism instance
                // before each render. Disabling only the temporary ParameterStore avoids
                // restoring the pre-sample eye values between the four held frames.
                clip.SampleAnimation(animator.gameObject, Frame194Seconds);
                report.frame194SampledLeftEye = leftEye.Value;
                report.frame194SampledRightEye = rightEye.Value;
                if (model != null) model.ForceUpdateNow();
                yield return new WaitForEndOfFrame();
                report.frame194RenderedLeftEye = leftEye.Value;
                report.frame194RenderedRightEye = rightEye.Value;
            }
            report.frame194LeftEye = report.frame194RenderedLeftEye;
            report.frame194RightEye = report.frame194RenderedRightEye;
            report.frame194ExpectedEye = .3313f;
            report.animatorState = current.fullPathHash.ToString();
            report.animatorNormalizedTime = normalizedTime;
            bool fixedReadbackNearEye = Mathf.Abs(report.frame194LeftEye - report.frame194ExpectedEye) < .16f
                && Mathf.Abs(report.frame194RightEye - report.frame194ExpectedEye) < .16f;
            if (fixedReadbackNearEye)
            {
                yield return CaptureScreenshot("zhoubo-frame-194-8.083s.png");
                checks.Add("captured four held rendered frames at exact 8.083/12 using fullPathHash, clip.SampleAnimation and ForceUpdateNow");
            }
            else
            {
                report.frame194UsedNaturalFallback = true;
                checks.Add("clip.SampleAnimation readback was restored by the Cubism frame; natural near-.3313 screenshot is the accepted frame-194 fallback");
            }
            frame194Holding = false;
            frame194HoldClip = null;
            frame194HoldAnimator = null;
            frame194HoldModel = null;
            animator.speed = oldSpeed;
            if (store != null) store.enabled = storeWasEnabled;
            yield return CaptureScreenshot("zhoubo-frame-194-after.png");
            Require(fixedReadbackNearEye || report.naturalClosestEye > .15f && report.naturalClosestEye < .52f,
                "Neither exact clip sampling nor the natural near-.3313 eye frame produced a visible half-closed result. left="
                + report.frame194LeftEye + ", right=" + report.frame194RightEye);
        }

        IEnumerator VerifyOtherNpcReset(WorldDirector world, MemoryGameController memory, Live2DCharacterView character)
        {
            yield return EnsureZhouboConversation(world, memory);
            character = FindCharacterView(memory);
            Require(character != null && character.IsVisible, "Reopening Zhoubo with E did not show a portrait.");
            Require(!memory.ZhouboPortraitFinal, "A normal E reopen did not reset Zhoubo to daily portrait mode.");
            RequireDailyProfile(character, "A normal E reopen did not restore the old ZhouboDaily profile.");
            yield return CaptureScreenshot("zhoubo-daily-restored.png");
            checks.Add("after final close, normal E reopens Zhoubo with the old daily profile");
            yield return VerifyDailyMouth(memory.View, character, world.Player.position);
            yield return Tap(Key.Escape);
            yield return WaitFor(() => !memory.PlayerChatOpen && !character.IsVisible, 4, "restored Zhoubo close");

            yield return OpenActorWithRetry(world, memory, "z", "Z interaction", false,
                () => memory.PlayerChatOpen && memory.View != null && memory.View.PlayerInputReady
                    && memory.Conversations != null && memory.Conversations.PlayerActor != null
                    && memory.Conversations.PlayerActor.npcId == "z");
            character = FindCharacterView(memory);
            Require(character != null, "Z interaction opened without a portrait view.");
            Require(character.IsVisible && character.CurrentProfile != null,
                "Z dialogue opened without a visible portrait.");
            var expectedZ = Resources.Load<Live2DModelProfile>(SevenCharacterCatalog.Find("z").portrait);
            Require(memory.Conversations != null && memory.Conversations.PlayerActor != null
                && memory.Conversations.PlayerActor.npcId == "z",
                "The interaction target was z, but the opened conversation actor was not z.");
            Require(expectedZ != null && character.CurrentProfile == expectedZ,
                "The actual opened portrait is not Z's configured profile.");
            Require(character.CurrentProfile.name.IndexOf("ZhouboFinal-v03", StringComparison.OrdinalIgnoreCase) < 0,
                "Zhoubo's final portrait leaked into the Z conversation.");
            yield return CaptureScreenshot("z-window.png");
            checks.Add("switched to Z and opened the actual Z portrait profile");
            yield return Tap(Key.Escape);
            yield return WaitFor(() => !memory.PlayerChatOpen && !character.IsVisible, 4, "Z close");
            yield return CaptureScreenshot("z-closed-window.png");
            checks.Add("closed the Z dialogue and returned to the Street map");
        }

        bool IsWorldOperable(GameSession session, WorldDirector world, PlayerInteraction playerInteraction, NpcPresence actor)
        {
            return session != null && session.IsReady && !session.InputBlocked && !session.NonPlayerInputBlocked
                && world != null && world.Player != null && world.Stage != null
                && !world.IsTransitioning && Time.timeScale > 0
                && playerInteraction != null && actor != null && actor.IsPresent
                && actor.isActiveAndEnabled && actor.Movement != null && actor.Movement.IsReady
                && actor.gameObject.scene == world.Stage.gameObject.scene;
        }

        string DescribeTarget(GameSession session, WorldDirector world, PlayerInteraction playerInteraction,
            NpcPresence actor)
        {
            var current = playerInteraction == null ? null : playerInteraction.Current;
            string playerPosition = world?.Player == null ? "<none>" : world.Player.position.ToString("F3");
            string actorPosition = actor == null ? "<none>" : actor.transform.position.ToString("F3");
            return "player=" + playerPosition
                + ", actor=" + (actor == null ? "<none>" : actor.npcId)
                + ", actorPosition=" + actorPosition
                + ", target=" + (current == null ? "<none>" : current.id)
                + ", targetDoor=" + (current != null && current.isDoor)
                + ", inputBlocked=" + (session != null && session.InputBlocked)
                + ", nonPlayerInputBlocked=" + (session != null && session.NonPlayerInputBlocked)
                + ", transitioning=" + (world != null && world.IsTransitioning)
                + ", timeScale=" + Time.timeScale
                + ", actorPresent=" + (actor != null && actor.IsPresent)
                + ", actorMovementReady=" + (actor != null && actor.Movement != null && actor.Movement.IsReady)
                + ", actorMoving=" + (actor != null && actor.Movement != null && actor.Movement.IsMoving)
                + ", appFocused=" + Application.isFocused
                + ", keyboard.enabled=" + (keyboard != null && keyboard.enabled)
                + ", currentKeyboard.enabled=" + (Keyboard.current != null && Keyboard.current.enabled);
        }

        string DescribeInputState(GameSession session, WorldDirector world, MemoryGameController memory,
            PlayerInteraction playerInteraction, NpcPresence actor)
        {
            var openedActor = memory?.Conversations?.PlayerActor;
            var currentKeyboard = Keyboard.current;
            return DescribeTarget(session, world, playerInteraction, actor)
                + ", sessionReady=" + (session != null && session.IsReady)
                + ", playerActor=" + (openedActor == null ? "<none>" : openedActor.npcId)
                + ", playerInputReady=" + (memory?.View != null && memory.View.PlayerInputReady)
                + ", keyboard.current=" + (currentKeyboard == null
                    ? "<none>"
                    : currentKeyboard.name + "#" + currentKeyboard.deviceId)
                + ", qaKeyboard=" + (keyboard == null ? "<none>" : keyboard.name + "#" + keyboard.deviceId)
                + ", qaKeyboardIsCurrent=" + (keyboard != null && currentKeyboard == keyboard);
        }

        IEnumerator VerifyDailyMouth(MemoryDialogueView view, Live2DCharacterView character, Vector3 position)
        {
            var profile = character.CurrentProfile;
            Require(profile != null, "The restored daily Zhoubo profile disappeared before the elderly-line mouth check.");
            var mouth = FindParameter(character, profile.mouthParameter);
            Require(mouth != null, "The restored daily Zhoubo mouth parameter was not found.");
            float idle = 0;
            int idleFrames = 0;
            float idleDeadline = Time.realtimeSinceStartup + .45f;
            while (Time.realtimeSinceStartup < idleDeadline)
            {
                yield return new WaitForEndOfFrame();
                idle += mouth.Value;
                idleFrames++;
            }
            idle = idleFrames == 0 ? mouth.Value : idle / idleFrames;
            view.ShowLine(ActorId, "年轻人，光线正好。你往窗边站一点，让我看看这张老照片。你瞧，这扇窗以前也朝着这条街，那时候我们常在这里拍照。", position, true);
            float max = float.MinValue;
            bool captured = false;
            string capturePath = Path.Combine(outputDirectory, "zhoubo-daily-talk.png");
            float deadline = Time.realtimeSinceStartup + 2.2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                max = Mathf.Max(max, mouth.Value);
                if (mouth.Value >= TalkCaptureThreshold && (!captured || mouth.Value >= max - .001f))
                {
                    yield return CaptureScreenshot("zhoubo-daily-talk.png", false);
                    captured = true;
                }
            }
            if (captured) screenshots.Add(capturePath);
            Require(captured && max >= TalkCaptureThreshold,
                "The elderly Zhoubo line did not reach the visible open-mouth threshold of .55. max=" + max);

            int closedFrames = 0;
            float closeDeadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < closeDeadline && closedFrames < 8)
            {
                yield return new WaitForEndOfFrame();
                if (Mathf.Abs(mouth.Value - idle) < .08f) closedFrames++;
                else closedFrames = 0;
            }
            Require(closedFrames >= 8 && Mathf.Abs(mouth.Value - idle) < .08f,
                "The elderly Zhoubo line did not settle back to a closed mouth.");
            yield return CaptureScreenshot("zhoubo-daily-closed.png");
            checks.Add("elderly Zhoubo fixed line reaches a visible open mouth and a stable closed mouth");
        }

        IEnumerator VerifySceneSwitch(GameSession session, WorldDirector world, MemoryGameController memory)
        {
            Require(!memory.PlayerChatOpen, "Cannot switch maps while a dialogue is open.");
            world.TravelTo(PlaceId.Cafe, null);
            yield return WaitForWorld(30);
            Require(session.CurrentPlace == PlaceId.Cafe, "Cafe scene switch did not complete.");
            world.TravelTo(PlaceId.Street, null);
            yield return WaitForWorld(30);
            Require(session.CurrentPlace == PlaceId.Street, "Return to Street scene switch did not complete.");
            yield return WaitForZhoubo(memory, world, 10);
            Require(memory.Spawner.Find(ActorId) != null, "Zhoubo did not respawn after returning to Street.");
            checks.Add("Cafe to Street switch recreates the scheduled Zhoubo actor");
        }

        void SendThroughDialogueInput(MemoryDialogueView view, string text)
        {
            var input = typeof(MemoryDialogueView).GetField("input", PrivateInstance)?.GetValue(view) as TMP_InputField;
            var send = typeof(MemoryDialogueView).GetMethod("Send", PrivateInstance);
            Require(input != null && send != null, "Dialogue input control is not available for the service-backed check.");
            input.text = text;
            send.Invoke(view, null);
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

        IEnumerator TapShiftE()
        {
            var inputKeyboard = AcquireKeyboard();
            InputSystem.QueueStateEvent(inputKeyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(inputKeyboard, new KeyboardState(Key.LeftShift, Key.E));
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

        IEnumerator WaitForMemoryService(MemoryLabClient client, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!client.CanGenerate)
            {
                if (Time.realtimeSinceStartup >= deadline)
                    throw new QAFailure("MemoryLab service readiness timed out. Status=" + client.Status
                        + ", LastError=" + client.LastError + ", Reachable=" + client.Reachable
                        + ", Configured=" + client.Configured + ", Synchronized=" + client.Synchronized);
                yield return null;
            }
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

        void Require(bool condition, string message)
        {
            if (!condition) throw new QAFailure(message);
        }

        void Finish(bool passed)
        {
            report.passed = passed;
            report.dialogueMode = dialogueMode;
            report.modelProfile = modelProfile;
            report.mouthParameter = mouthParameter;
            report.screenshots = screenshots.ToArray();
            report.checks = checks.ToArray();
            report.failures = failures.ToArray();
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "zhoubo-map-qa.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception error)
            {
                Debug.LogError("Zhoubo map QA could not write its report: " + error);
            }
            if (passed) Debug.Log("ZHOUBO_MAP_QA_PASS: " + Path.Combine(outputDirectory, "zhoubo-map-qa.json"));
            else Debug.LogError("ZHOUBO_MAP_QA_FAIL: " + Path.Combine(outputDirectory, "zhoubo-map-qa.json"));
            RestoreQaInputFocus();
            if (ownsKeyboard && keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (!Application.isEditor) Application.Quit(passed ? 0 : 1);
            else Destroy(gameObject);
        }
    }
}
