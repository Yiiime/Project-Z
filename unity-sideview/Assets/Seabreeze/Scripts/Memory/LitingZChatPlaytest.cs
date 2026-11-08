using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>
    /// Isolated playtest entry for the two-person memory chat flow.
    ///
    /// This component is installed only when one of its explicit command-line
    /// arguments is present. Ordinary play never creates the panel or changes
    /// the normal MemoryDialogueView.
    ///
    /// -litingZPortraitPreview             opens Li Ting and shows switch buttons.
    /// -litingZChatCheck &lt;directory&gt;        drives Li Ting -> Z -> Li Ting using
    ///                                      the normal input field and send button.
    /// </summary>
    public sealed class LitingZChatPlaytest : MonoBehaviour
    {
        const string LitingId = "liting";
        const string ZId = "z";
        const float OpenTimeout = 45f;
        const float ServiceTimeout = 90f;
        const float ReplyTimeout = 120f;
        const float SpeechThreshold = .08f;
        const float IdleSpeechThreshold = .08f;
        const float ExpressionDeltaThreshold = .08f;
        const float IdleExpressionThreshold = .08f;
        const string ReportName = "liting-z-chat-report.json";
        const string ResultName = "result.txt";

        static readonly string[] ZParameterIds =
        {
            "ParamZSpeech", "ParamMouthOpenY", "ParamMouthForm", "ParamEyeSmile",
            "ParamZRoundMouth", "ParamZStarry", "ParamZTearQuiet", "ParamZTearSob"
        };
        static readonly string[] LitingParameterIds =
        {
            "ParamExpression", "ParamExpressionMix", "ParamMouthOpenY"
        };
        static readonly string[] ZExpressionParameterIds =
        {
            "ParamMouthForm", "ParamEyeSmile", "ParamZRoundMouth", "ParamZStarry",
            "ParamZTearQuiet", "ParamZTearSob"
        };
        static readonly string[] LitingExpressionParameterIds =
        {
            "ParamExpression", "ParamExpressionMix"
        };

        GameSession session;
        MemoryGameController memory;
        WorldDirector world;
        Canvas controlsCanvas;
        RectTransform controlsPanel;
        Button litingButton;
        Button zButton;
        string output;
        string activeActor;
        string setupFailure;
        bool checkMode;
        bool switching;
        bool finished;

        [Serializable]
        sealed class ParameterSample
        {
            public string id;
            public string role;
            public bool found;
            public float neutral;
            public float minimum;
            public float maximum;
            public float final;
            public float maxDelta;
            public bool changed;
        }

        [Serializable]
        sealed class ActorReport
        {
            public string actorId;
            public string displayName;
            public string place;
            public string prompt;
            public string reply;
            public string transcript;
            public string expression;
            public string latestPlayer;
            public string profileCharacterId;
            public string mouthParameterId;
            public string inputMethod;
            public string openScreenshot;
            public string replyScreenshot;
            public string speakingScreenshot;
            public string idleScreenshot;
            public string worldId;
            public string runId;
            public bool npcFound;
            public bool viewOpened;
            public bool inputReady;
            public bool requiredParameters;
            public bool inputSubmitted;
            public bool replyObserved;
            public bool replyMatchesPrompt;
            public bool unexpectedPlayerInput;
            public bool replyEnded;
            public bool returnedIdle;
            public bool mouthReturnedIdle;
            public bool expressionReturnedIdle;
            public bool modelExpected;
            public bool modelVisible;
            public bool profileMatches;
            public bool speechObserved;
            public bool mouthObserved;
            public bool expressionObserved;
            public float maxSpeech;
            public float finalSpeech;
            public float maxMouth;
            public float finalMouth;
            public ParameterSample[] parameters = Array.Empty<ParameterSample>();
            public string failure;
        }

        [Serializable]
        sealed class SwitchReport
        {
            public string fromActor;
            public string toActor;
            public string fromPlace;
            public string toPlace;
            public bool previousChatClosed;
            public bool placeChanged;
            public bool targetReady;
            public bool sameWorldId;
            public bool sameRunId;
        }

        [Serializable]
        sealed class CheckReport
        {
            public string schema = "liting-z-chat-check-v1";
            public bool passed;
            public string mode;
            public string serviceStatus;
            public string failure;
            public string worldId;
            public string runId;
            public bool modeCorrect;
            public bool sameWorldId;
            public bool sameRunId;
            public bool legacyZEntryPresent;
            public bool normalInputPath;
            public bool switchControlsAvoidClose;
            public string[] actorOrder = Array.Empty<string>();
            public string[] screenshots = Array.Empty<string>();
            public ActorReport[] actors = Array.Empty<ActorReport>();
            public SwitchReport[] switches = Array.Empty<SwitchReport>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int checkIndex = Array.IndexOf(args, "-litingZChatCheck");
            int previewIndex = Array.IndexOf(args, "-litingZPortraitPreview");
            bool check = checkIndex >= 0 && checkIndex + 1 < args.Length;
            bool preview = previewIndex >= 0;
            if (!check && !preview) return;

            var runner = new GameObject("Li Ting and Z chat playtest");
            DontDestroyOnLoad(runner);
            var playtest = runner.AddComponent<LitingZChatPlaytest>();
            playtest.checkMode = check;
            if (check) playtest.output = Path.GetFullPath(args[checkIndex + 1]);
        }

        IEnumerator Start()
        {
            yield return WaitForGame();
            if (!string.IsNullOrEmpty(setupFailure))
            {
                Debug.LogError(setupFailure);
                if (checkMode) FinishFailure(setupFailure);
                yield break;
            }
            BuildControls();
            yield return OpenActor(LitingId);
            if (!string.IsNullOrEmpty(setupFailure))
            {
                Debug.LogError(setupFailure);
                if (checkMode) FinishFailure(setupFailure);
                yield break;
            }
            if (checkMode) yield return RunCheck();
        }

        IEnumerator WaitForGame()
        {
            float deadline = Time.realtimeSinceStartup + OpenTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                session = GameSession.Instance;
                memory = session?.Memory;
                world = WorldDirector.Instance;
                if (session != null && session.IsReady && memory != null && memory.Client != null && memory.View != null &&
                    world != null && world.Player != null)
                    yield break;
                yield return null;
            }
            setupFailure = "游戏正常入口或记忆聊天界面未就绪。";
        }

        void BuildControls()
        {
            if (controlsCanvas != null) return;
            var canvasObject = new GameObject("Li Ting and Z playtest switch controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            controlsCanvas = canvasObject.GetComponent<Canvas>();
            controlsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            controlsCanvas.sortingOrder = 260;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UIPrimitives.Style.ReferenceResolution;
            scaler.matchWidthOrHeight = .5f;

            var root = controlsCanvas.GetComponent<RectTransform>();
            var panel = UIPrimitives.Panel("试玩切换面板", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -18), new Vector2(230, 48), UIPrimitives.Style.HudFill, true);
            controlsPanel = panel.rectTransform;
            // Panel children use the panel's bottom-left origin. Keep both
            // buttons inside the visible panel; the old negative Y positions
            // rendered them below an empty blue frame.
            litingButton = UIPrimitives.Button("试玩-李庭", panel.transform, Vector2.zero, Vector2.zero, new Vector2(12, 6), new Vector2(96, 36), "李庭", () => RequestSwitch(LitingId), false, 17);
            zButton = UIPrimitives.Button("试玩-Z", panel.transform, Vector2.zero, Vector2.zero, new Vector2(122, 6), new Vector2(96, 36), "Z", () => RequestSwitch(ZId), false, 17);
        }

        void Update()
        {
            if (controlsCanvas == null) return;
            bool enabled = !switching && !finished && memory != null && memory.View != null;
            if (litingButton != null) litingButton.interactable = enabled;
            if (zButton != null) zButton.interactable = enabled;
        }

        void RequestSwitch(string actorId)
        {
            if (switching || finished) return;
            StartCoroutine(OpenActor(actorId));
        }

        IEnumerator OpenActor(string actorId)
        {
            setupFailure = null;
            var definition = SevenCharacterCatalog.Find(actorId);
            if (definition == null)
            {
                setupFailure = "找不到试玩角色：" + actorId;
                yield break;
            }
            if (activeActor == actorId && memory != null && memory.View != null && memory.View.PlayerInputReady)
                yield break;

            switching = true;
            try
            {
                if (memory.PlayerChatOpen)
                {
                    memory.Conversations.Cancel("试玩切换角色");
                    yield return WaitForChatClosed();
                    if (memory.PlayerChatOpen)
                    {
                        setupFailure = "上一段对话未能关闭，切换已取消。";
                        yield break;
                    }
                }

                if (session.CurrentPlace != definition.Place)
                {
                    session.Travel(definition.Place);
                    yield return WaitForPlace(definition.Place);
                    if (!string.IsNullOrEmpty(setupFailure)) yield break;
                }
                var npc = memory.Spawner.Find(actorId);
                float deadline = Time.realtimeSinceStartup + OpenTimeout;
                while (npc == null || !npc.IsPresent || npc.Movement == null || !npc.Movement.IsReady)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        setupFailure = "角色未出现在场景：" + actorId;
                        yield break;
                    }
                    npc = memory.Spawner.Find(actorId);
                    yield return null;
                }

                var controller = world.Player.GetComponent<PlayerController>();
                if (controller == null)
                {
                    setupFailure = "正常玩家入口缺少 PlayerController。";
                    yield break;
                }
                controller.Teleport(npc.transform.position + Vector3.left * 1.2f);
                world.Follow?.Snap();
                memory.BeginPlayerChat(actorId);
                yield return WaitForPlayerInput();
                if (!string.IsNullOrEmpty(setupFailure)) yield break;
                activeActor = actorId;
                litingButton.GetComponentInChildren<TextMeshProUGUI>().color = actorId == LitingId ? UIPrimitives.Accent : UIPrimitives.Paper;
                zButton.GetComponentInChildren<TextMeshProUGUI>().color = actorId == ZId ? UIPrimitives.Accent : UIPrimitives.Paper;
            }
            finally
            {
                switching = false;
            }
        }

        IEnumerator WaitForChatClosed()
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (memory != null && memory.PlayerChatOpen && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        IEnumerator WaitForPlace(PlaceId place)
        {
            float deadline = Time.realtimeSinceStartup + OpenTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!world.IsTransitioning && world.Stage != null && world.Stage.place == place)
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            setupFailure = "场景切换超时：" + place;
        }

        IEnumerator WaitForPlayerInput()
        {
            float deadline = Time.realtimeSinceStartup + OpenTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (memory.View != null && memory.View.PlayerInputReady) yield break;
                yield return null;
            }
            setupFailure = "正常对话输入框未打开。";
        }

        IEnumerator RunCheck()
        {
            Directory.CreateDirectory(output);
            var report = new CheckReport
            {
                mode = memory.Client.Mode,
                serviceStatus = memory.Client.Status,
                worldId = session.State.memory.worldId,
                runId = memory.Client.RunId,
                actorOrder = new[] { LitingId, ZId, LitingId },
                legacyZEntryPresent = typeof(ZPortraitPlaytest) != null,
                normalInputPath = ControlsAreNormal(memory.View.DialogueInput, memory.View.DialogueSend),
                switchControlsAvoidClose = ControlsAvoidClose()
            };
            var actors = new List<ActorReport>();
            var switches = new List<SwitchReport>();

            yield return WaitForService(report);
            if (!report.modeCorrect)
            {
                report.failure = "需要真实记忆服务，当前为 " + (memory.Client.Mode ?? "unknown") + "。";
                report.actors = actors.ToArray();
                report.switches = switches.ToArray();
                report.passed = false;
                Finish(report);
                yield break;
            }

            yield return RunActorChat(LitingId, "李庭，今天的咖啡和海风怎么样？像刚才那样对我笑一下好吗？请记住我来过这里。", true, "liting-open.png", "liting-reply.png", "liting-speaking.png", "liting-idle.png", actors);
            yield return SwitchTo(ZId, switches);
            yield return RunActorChat(ZId, "Z，和你聊完李庭的咖啡，我想听听你今天看到的海。你也像刚才那样对我笑一下好吗？", true, "z-open.png", "z-reply.png", "z-speaking.png", "z-idle.png", actors);
            yield return SwitchTo(LitingId, switches);
            yield return RunActorChat(LitingId, "我还想再回来聊聊。像刚才那样对我笑一下好吗？", false, "liting-return-open.png", "liting-return-reply.png", "liting-return-speaking.png", "liting-return-idle.png", actors);

            memory.Conversations.Cancel("试玩验收结束");
            yield return WaitForChatClosed();
            report.serviceStatus = memory.Client.Status;
            report.sameWorldId = actors.Count > 0 && actors.All(a => a.worldId == report.worldId && session.State.memory.worldId == report.worldId);
            report.sameRunId = actors.Count > 0 && actors.All(a => a.runId == report.runId && memory.Client.RunId == report.runId);
            report.actors = actors.ToArray();
            report.switches = switches.ToArray();
            report.screenshots = actors.SelectMany(a => new[] { a.openScreenshot, a.replyScreenshot, a.speakingScreenshot, a.idleScreenshot }).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToArray();
            var litingActors = actors.Where(a => a.actorId == LitingId).ToArray();
            var zActors = actors.Where(a => a.actorId == ZId).ToArray();
            report.passed = report.modeCorrect && report.normalInputPath && report.sameWorldId && report.sameRunId &&
                report.switchControlsAvoidClose &&
                switches.Count == 2 && switches.All(s => s.previousChatClosed && s.targetReady && s.sameWorldId && s.sameRunId) &&
                actors.Count == 3 && actors.All(a => a.npcFound && a.viewOpened && a.inputReady && a.requiredParameters && a.inputSubmitted && a.replyObserved && a.replyMatchesPrompt && a.replyEnded && a.returnedIdle &&
                    a.modelExpected && a.modelVisible && a.profileMatches && a.mouthObserved && a.mouthReturnedIdle && a.expressionReturnedIdle) &&
                litingActors.Any(a => a.replyMatchesPrompt && a.expressionObserved && a.expressionReturnedIdle) &&
                zActors.Any(a => a.replyMatchesPrompt && a.expressionObserved && a.speechObserved && a.mouthObserved);
            if (!report.passed) report.failure = BuildFailure(report);
            Finish(report);
        }

        IEnumerator WaitForService(CheckReport report)
        {
            float deadline = Time.realtimeSinceStartup + ServiceTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                report.serviceStatus = memory.Client.Status;
                report.mode = memory.Client.Mode;
                report.modeCorrect = string.Equals(memory.Client.Mode, "real", StringComparison.OrdinalIgnoreCase) && memory.Client.CanGenerate;
                if (report.modeCorrect) yield break;
                yield return null;
            }
        }

        IEnumerator SwitchTo(string actorId, List<SwitchReport> switches)
        {
            string from = activeActor;
            string worldId = session.State.memory.worldId;
            string runId = memory.Client.RunId;
            PlaceId fromPlace = session.CurrentPlace;
            bool wasOpen = memory.PlayerChatOpen;
            bool closedBeforeOpen = !wasOpen;
            if (wasOpen)
            {
                memory.Conversations.Cancel("试玩切换角色");
                yield return WaitForChatClosed();
                closedBeforeOpen = !memory.PlayerChatOpen;
            }
            yield return OpenActor(actorId);
            var result = new SwitchReport
            {
                fromActor = from,
                toActor = actorId,
                fromPlace = fromPlace.ToString(),
                toPlace = session.CurrentPlace.ToString(),
                previousChatClosed = closedBeforeOpen,
                placeChanged = fromPlace != session.CurrentPlace,
                targetReady = activeActor == actorId && memory.View.PlayerInputReady,
                sameWorldId = session.State.memory.worldId == worldId,
                sameRunId = memory.Client.RunId == runId
            };
            switches.Add(result);
        }

        IEnumerator RunActorChat(string actorId, string prompt, bool useButton, string openScreenshot, string replyScreenshot, string speakingScreenshot, string idleScreenshot, List<ActorReport> actors)
        {
            yield return OpenActor(actorId);
            var definition = SevenCharacterCatalog.Find(actorId);
            var actor = new ActorReport
            {
                actorId = actorId,
                displayName = SevenCharacterCatalog.Name(actorId),
                place = definition?.Place.ToString(),
                prompt = prompt,
                inputMethod = useButton ? "Button.onClick.Invoke" : "TMP_InputField.onSubmit.Invoke",
                openScreenshot = openScreenshot,
                replyScreenshot = replyScreenshot,
                idleScreenshot = idleScreenshot,
                worldId = session.State.memory.worldId,
                runId = memory.Client.RunId,
                npcFound = memory.Spawner.Find(actorId) != null,
                viewOpened = memory.View != null,
                inputReady = memory.View != null && memory.View.PlayerInputReady,
                modelExpected = true,
                mouthParameterId = MouthParameterFor(actorId)
            };
            var samples = CreateParameterSamples(actorId);
            actor.parameters = samples;
            actor.requiredParameters = samples.Length > 0 && samples.All(sample => sample.found);
            yield return SaveFrame(openScreenshot);

            if (!actor.inputReady || !actor.npcFound)
            {
                actor.failure = "角色或正常对话输入框未就绪。";
                actors.Add(actor);
                yield break;
            }
            float serviceDeadline = Time.realtimeSinceStartup + ServiceTimeout;
            while (!memory.Client.CanGenerate && Time.realtimeSinceStartup < serviceDeadline) yield return null;
            if (!memory.Client.CanGenerate)
            {
                actor.failure = "记忆服务未就绪：" + memory.Client.Status;
                actors.Add(actor);
                yield break;
            }

            var input = memory.View.DialogueInput;
            var send = memory.View.DialogueSend;
            var history = memory.View.DialogueHistory;
            float controlsDeadline = Time.realtimeSinceStartup + 8;
            while ((!memory.Client.CanGenerate || send == null || !send.IsInteractable()) && Time.realtimeSinceStartup < controlsDeadline)
                yield return null;
            if (input == null || send == null || history == null || !memory.Client.CanGenerate || !send.IsInteractable())
            {
                actor.failure = "正常输入框或发送按钮未进入可用状态。";
                actors.Add(actor);
                yield break;
            }
            int playerLinesBefore = CountSpeakerLines(history.text, "玩家");
            int speakerLinesBefore = CountSpeakerLines(history.text, actor.displayName);
            input.ActivateInputField();
            input.text = prompt;
            if (useButton) send.onClick.Invoke();
            else input.onSubmit.Invoke(prompt);
            actor.inputSubmitted = input.text.Length == 0 && history != null && history.text.IndexOf(prompt, StringComparison.Ordinal) >= 0;

            var portrait = memory.View.GetComponentInChildren<Live2DCharacterView>(true);
            var model = portrait?.CurrentModel;
            actor.modelVisible = portrait != null && portrait.IsVisible && model != null;
            actor.profileCharacterId = portrait?.CurrentProfile?.characterId;
            actor.profileMatches = portrait?.CurrentProfile != null && string.Equals(portrait.CurrentProfile.characterId, actorId, StringComparison.OrdinalIgnoreCase);
            bool replySeen = false;
            bool speakingFrameSaved = false;
            float deadline = Time.realtimeSinceStartup + ReplyTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (history != null) history.ForceMeshUpdate();
                SampleParameters(samples, model);
                if (!replySeen && CountSpeakerLines(history?.text, actor.displayName) > speakerLinesBefore)
                {
                    replySeen = true;
                    actor.latestPlayer = ExtractLatestLine(history?.text, "玩家");
                    actor.unexpectedPlayerInput = CountSpeakerLines(history?.text, "玩家") != playerLinesBefore + 1 ||
                        !string.Equals(actor.latestPlayer, prompt, StringComparison.Ordinal);
                    actor.replyMatchesPrompt = !actor.unexpectedPlayerInput;
                    if (actor.replyMatchesPrompt)
                    {
                        actor.replyObserved = true;
                        actor.reply = ExtractReply(history?.text, actor.displayName);
                        actor.expression = memory.LastReply?.result?.expression ?? memory.LastReply?.expression ?? "";
                    }
                    else actor.failure = "检测到额外玩家输入，忽略这条非本题回复。";
                }
                if (replySeen && actor.replyMatchesPrompt)
                {
                    actor.speechObserved |= HasMeaningfulChange(samples, MouthParameterFor(actorId) == "ParamZSpeech" ? new[] { "ParamZSpeech" } : Array.Empty<string>(), SpeechThreshold);
                    actor.mouthObserved |= HasMeaningfulChange(samples, new[] { MouthParameterFor(actorId) }, SpeechThreshold);
                    actor.expressionObserved |= actorId == LitingId
                        ? HasAllMeaningfulChange(samples, ExpressionParametersFor(actorId), ExpressionDeltaThreshold)
                        : HasMeaningfulChange(samples, ExpressionParametersFor(actorId), ExpressionDeltaThreshold);
                }
                if (actor.modelExpected && !speakingFrameSaved && actor.replyObserved && (actor.mouthObserved || actor.expressionObserved))
                {
                    speakingFrameSaved = true;
                    actor.speakingScreenshot = speakingScreenshot;
                    yield return SaveFrame(actor.speakingScreenshot);
                }
                if (replySeen && !memory.Conversations.Sending && history != null && history.maxVisibleCharacters >= history.textInfo.characterCount)
                {
                    actor.replyEnded = true;
                    break;
                }
                yield return new WaitForEndOfFrame();
            }
            if (actor.modelExpected && !speakingFrameSaved && actor.replyObserved)
            {
                actor.speakingScreenshot = speakingScreenshot;
                yield return SaveFrame(actor.speakingScreenshot);
            }
            actor.transcript = history?.text ?? "";
            actor.latestPlayer = ExtractLatestLine(actor.transcript, "玩家");
            if (CountSpeakerLines(actor.transcript, "玩家") != playerLinesBefore + 1 ||
                !string.Equals(actor.latestPlayer, prompt, StringComparison.Ordinal))
            {
                actor.unexpectedPlayerInput = true;
                actor.replyMatchesPrompt = false;
                actor.replyObserved = false;
                actor.expressionObserved = false;
                actor.mouthObserved = false;
                actor.failure = "检测到额外玩家输入，忽略这条非本题回复。";
            }
            yield return SaveFrame(replyScreenshot);

            if (actor.replyObserved)
            {
                float idleDeadline = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < idleDeadline)
                {
                    SampleParameters(samples, model);
                    yield return new WaitForEndOfFrame();
                }
            }
            FinalizeParameters(samples);
            actor.maxSpeech = samples.FirstOrDefault(s => s.id == "ParamZSpeech")?.maximum ?? 0;
            actor.finalSpeech = samples.FirstOrDefault(s => s.id == "ParamZSpeech")?.final ?? 0;
            actor.maxMouth = samples.FirstOrDefault(s => s.id == actor.mouthParameterId)?.maximum ?? 0;
            actor.finalMouth = samples.FirstOrDefault(s => s.id == actor.mouthParameterId)?.final ?? 0;
            actor.mouthReturnedIdle = HasReturnedToNeutral(samples, new[] { actor.mouthParameterId }, IdleSpeechThreshold);
            actor.expressionReturnedIdle = HasReturnedToNeutral(samples, ExpressionParametersFor(actorId), IdleExpressionThreshold);
            actor.returnedIdle = actor.mouthReturnedIdle && actor.expressionReturnedIdle;
            yield return SaveFrame(idleScreenshot);
            if (!actor.inputSubmitted) actor.failure = "真实输入控件未提交。";
            else if (!actor.replyObserved) actor.failure = "未看到 " + actor.displayName + " 的真实回复。";
            else if (!actor.replyEnded) actor.failure = "回复没有完成并回到可输入状态。";
            actors.Add(actor);
        }

        ParameterSample[] CreateParameterSamples(string actorId)
        {
            var model = memory.View.GetComponentInChildren<Live2DCharacterView>(true)?.CurrentModel;
            var expressionIds = ExpressionParametersFor(actorId);
            return ParameterIdsFor(actorId).Select(id =>
            {
                var parameter = ZLive2DProbe.Parameter(model, id);
                float value = parameter?.Value ?? 0;
                return new ParameterSample
                {
                    id = id,
                    role = expressionIds.Contains(id) ? "expression" : id == MouthParameterFor(actorId) ? "mouth" : "other",
                    found = parameter != null,
                    neutral = value,
                    minimum = value,
                    maximum = value,
                    final = value
                };
            }).ToArray();
        }

        static string[] ParameterIdsFor(string actorId) => actorId == ZId ? ZParameterIds : LitingParameterIds;
        static string MouthParameterFor(string actorId) => actorId == ZId ? "ParamZSpeech" : "ParamMouthOpenY";
        static string[] ExpressionParametersFor(string actorId) => actorId == ZId ? ZExpressionParameterIds : LitingExpressionParameterIds;

        static bool HasMeaningfulChange(ParameterSample[] samples, string[] ids, float threshold)
        {
            return samples != null && ids != null && ids.Length > 0 && samples.Any(sample => sample.found && ids.Contains(sample.id) && sample.maxDelta > threshold);
        }

        static bool HasAllMeaningfulChange(ParameterSample[] samples, string[] ids, float threshold)
        {
            return samples != null && ids != null && ids.Length > 0 && ids.All(id =>
            {
                var sample = samples.FirstOrDefault(item => item.id == id);
                return sample != null && sample.found && sample.maxDelta > threshold;
            });
        }

        static bool HasReturnedToNeutral(ParameterSample[] samples, string[] ids, float threshold)
        {
            return samples != null && ids != null && ids.Length > 0 && ids.All(id =>
            {
                var sample = samples.FirstOrDefault(item => item.id == id);
                return sample != null && sample.found && Mathf.Abs(sample.final - sample.neutral) <= threshold;
            });
        }

        static void SampleParameters(ParameterSample[] samples, CubismModel model)
        {
            if (samples == null || model == null) return;
            foreach (var sample in samples)
            {
                var parameter = ZLive2DProbe.Parameter(model, sample.id);
                if (parameter == null) continue;
                sample.found = true;
                sample.minimum = Mathf.Min(sample.minimum, parameter.Value);
                sample.maximum = Mathf.Max(sample.maximum, parameter.Value);
                sample.final = parameter.Value;
                sample.maxDelta = Mathf.Max(sample.maxDelta, Mathf.Abs(parameter.Value - sample.neutral));
                sample.changed |= sample.maxDelta > ExpressionDeltaThreshold;
            }
        }

        static void FinalizeParameters(ParameterSample[] samples)
        {
            if (samples == null) return;
            foreach (var sample in samples)
            {
                sample.maxDelta = Mathf.Max(sample.maxDelta, Mathf.Abs(sample.final - sample.neutral));
                sample.changed |= sample.maxDelta > ExpressionDeltaThreshold;
            }
        }

        static bool ControlsAreNormal(TMP_InputField input, Button send)
        {
            if (input == null || send == null) return false;
            string[] oldControls = { "说话", "好奇", "惊讶", "哭泣", "大哭" };
            return !send.transform.root.GetComponentsInChildren<Button>(true).Any(button => oldControls.Contains(button.name));
        }

        bool ControlsAvoidClose()
        {
            if (controlsPanel == null || memory?.View == null) return false;
            var close = memory.View.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == "结束交谈");
            if (close == null) return false;
            Canvas.ForceUpdateCanvases();
            var panelRect = ScreenRect(controlsPanel);
            var closeRect = ScreenRect(close.GetComponent<RectTransform>());
            return panelRect.width > 0 && panelRect.height > 0 && closeRect.width > 0 && closeRect.height > 0 && !panelRect.Overlaps(closeRect);
        }

        static Rect ScreenRect(RectTransform rect)
        {
            if (rect == null) return default;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        IEnumerator SaveFrame(string name)
        {
            if (string.IsNullOrEmpty(output)) yield break;
            yield return new WaitForEndOfFrame();
            var frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame != null)
            {
                File.WriteAllBytes(Path.Combine(output, name), frame.EncodeToPNG());
                Destroy(frame);
            }
        }

        static string ExtractReply(string transcript, string speaker)
        {
            if (string.IsNullOrEmpty(transcript) || string.IsNullOrEmpty(speaker)) return "";
            string prefix = speaker + "：";
            int index = transcript.LastIndexOf(prefix, StringComparison.Ordinal);
            return index >= 0 ? transcript.Substring(index + prefix.Length).Trim() : "";
        }

        static int CountSpeakerLines(string transcript, string speaker)
        {
            if (string.IsNullOrEmpty(transcript) || string.IsNullOrEmpty(speaker)) return 0;
            string prefix = speaker + "：\n";
            int count = 0;
            for (int index = 0; (index = transcript.IndexOf(prefix, index, StringComparison.Ordinal)) >= 0; index += prefix.Length) count++;
            return count;
        }

        static string ExtractLatestLine(string transcript, string speaker)
        {
            if (string.IsNullOrEmpty(transcript) || string.IsNullOrEmpty(speaker)) return "";
            string prefix = speaker + "：\n";
            int index = transcript.LastIndexOf(prefix, StringComparison.Ordinal);
            if (index < 0) return "";
            string value = transcript.Substring(index + prefix.Length);
            int separator = value.IndexOf("\n\n", StringComparison.Ordinal);
            return (separator >= 0 ? value.Substring(0, separator) : value).Trim();
        }

        string BuildFailure(CheckReport report)
        {
            var failures = new List<string>();
            if (!report.modeCorrect) failures.Add("记忆服务不是已配置的真实模式");
            if (!report.normalInputPath) failures.Add("未使用正常输入框/发送按钮路径");
            if (!report.switchControlsAvoidClose) failures.Add("试玩切换按钮遮挡原生关闭按钮或未找到关闭按钮");
            if (!report.sameWorldId) failures.Add("切换时 worldId 发生变化");
            if (!report.sameRunId) failures.Add("切换时 runId 发生变化");
            foreach (var item in report.switches.Where(s => !s.previousChatClosed || !s.targetReady || !s.sameWorldId || !s.sameRunId))
                failures.Add("切换 " + item.fromActor + " -> " + item.toActor + " 未完成");
            foreach (var item in report.actors.Where(a => !a.inputSubmitted || !a.replyObserved || !a.replyMatchesPrompt || !a.replyEnded || !a.returnedIdle ||
                !a.modelExpected || !a.modelVisible || !a.profileMatches || !a.requiredParameters || !a.mouthObserved || !a.mouthReturnedIdle || !a.expressionReturnedIdle))
                failures.Add(item.displayName + " 对话未完整完成" + (string.IsNullOrEmpty(item.failure) ? "" : "（" + item.failure + "）"));
            var z = report.actors.FirstOrDefault(a => a.actorId == ZId);
            if (z != null && (!z.modelVisible || !z.profileMatches || !z.speechObserved || !z.mouthObserved || !z.expressionObserved)) failures.Add("Z 运行时模型、表情或口型数据未通过");
            var liting = report.actors.Where(a => a.actorId == LitingId).ToArray();
            if (liting.Length > 0 && !liting.Any(a => a.replyMatchesPrompt && a.expressionObserved && a.expressionReturnedIdle)) failures.Add("李庭没有一条与测试输入匹配的非 neutral 表情并完成复位");
            return string.Join("；", failures.Distinct());
        }

        void FinishFailure(string failure)
        {
            if (finished) return;
            Directory.CreateDirectory(output);
            Finish(new CheckReport
            {
                mode = memory?.Client?.Mode ?? "unknown",
                serviceStatus = memory?.Client?.Status ?? "unknown",
                worldId = session?.State?.memory?.worldId,
                runId = memory?.Client?.RunId,
                modeCorrect = false,
                failure = failure,
                legacyZEntryPresent = typeof(ZPortraitPlaytest) != null
            });
        }

        void Finish(CheckReport report)
        {
            if (finished) return;
            finished = true;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, ReportName), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, ResultName), report.passed ? "PASS\n" : "FAIL\n");
            Debug.Log("Li Ting/Z chat playtest: " + (report.passed ? "PASS" : "FAIL") + " " + report.failure);
            Application.Quit(report.passed ? 0 : 1);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(report.passed ? 0 : 1);
#endif
        }
    }
}
