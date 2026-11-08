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
    /// Explicit end-to-end dialogue check. It drives the same TMP input and
    /// Send button as a player, then samples the instantiated Cubism model.
    /// No branch is enabled during ordinary play.
    ///
    /// -zRealSmileCheck &lt;dir&gt; uses a configured real service.
    /// -zSmileCheck &lt;dir&gt; requires a simulated service.
    /// -zDialogueCapture &lt;dir&gt; runs the shorter fixed-input smoke check.
    /// </summary>
    public sealed class ZDialogueInputCheck : MonoBehaviour
    {
        const string ActorId = "z";
        const float Timeout = 90f;
        const float SpeechThreshold = .2f;
        const float SmileDelta = .2f;
        MemoryGameController memory;
        string output;
        bool real, fixture;
        readonly Dictionary<string, float> runtimeOpacities = new Dictionary<string, float>();
        readonly Dictionary<string, bool> runtimeVisibilities = new Dictionary<string, bool>();
        CubismModel observedModel;

        [Serializable]
        sealed class Report
        {
            public bool passed;
            public string mode, actorId = ActorId, failure, prompt, secondPrompt, transcript, reply, secondReply;
            public bool modeCorrect, inputSubmitted, replyObserved, speakingObserved, replyEnded, smileObserved, smileHeldAtEnd, returnedNeutral;
            public bool smileDrawableChanged, endedDrawableEvidence;
            public bool secondInputSubmitted, secondReplyObserved, secondStayedNeutral;
            public float neutralEyeSmile, maxEyeSmile, finalEyeSmile, maxSpeech, finalSpeech, maxTearSob, maxTearQuiet;
            public int committedReplies;
            public DrawableSample[] neutralDrawables = Array.Empty<DrawableSample>();
            public DrawableSample[] smileDrawables = Array.Empty<DrawableSample>();
            public DrawableSample[] endedDrawables = Array.Empty<DrawableSample>();
            public string[] missingDrawables = Array.Empty<string>();
        }

        [Serializable]
        sealed class DrawableSample
        {
            public string id;
            public bool found, visible;
            public float opacity, width, height, area, centerX, centerY;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int realIndex = Array.IndexOf(args, "-zRealSmileCheck");
            int fixtureIndex = Array.IndexOf(args, "-zSmileCheck");
            int captureIndex = Array.IndexOf(args, "-zDialogueCapture");
            int index = realIndex >= 0 ? realIndex : fixtureIndex >= 0 ? fixtureIndex : captureIndex;
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Z dialogue input check");
            DontDestroyOnLoad(runner);
            var check = runner.AddComponent<ZDialogueInputCheck>();
            check.output = args[index + 1];
            check.real = index == realIndex;
            check.fixture = index == fixtureIndex;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            yield return OpenZDialogue();
            if (memory == null || memory.View == null || !memory.View.PlayerInputReady)
            {
                Finish(new Report { mode = ModeName(), failure = "Z 对话界面未能打开。" });
                yield break;
            }
            if (real || fixture) yield return RunSmileCheck();
            else yield return RunDialogueSmoke();
        }

        string ModeName() => fixture ? "simulated" : real ? "real" : "capture";

        IEnumerator OpenZDialogue()
        {
            float deadline = Time.realtimeSinceStartup + 40;
            while (Time.realtimeSinceStartup < deadline)
            {
                memory = GameSession.Instance?.Memory;
                var world = WorldDirector.Instance;
                var z = memory?.Spawner?.Find(ActorId);
                if (memory != null && world?.Player != null && !world.IsTransitioning && z != null && z.IsPresent &&
                    z.Movement != null && z.Movement.IsReady)
                {
                    if (!memory.PlayerChatOpen)
                    {
                        world.Player.GetComponent<PlayerController>().Teleport(z.transform.position + Vector3.left * 1.2f);
                        world.Follow?.Snap();
                        memory.BeginPlayerChat(ActorId);
                    }
                    if (memory.View != null && memory.View.PlayerInputReady) yield break;
                }
                yield return null;
            }
        }

        IEnumerator WaitForService(bool requireMode, Report report)
        {
            float deadline = Time.realtimeSinceStartup + Timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (memory?.Client != null)
                {
                    report.modeCorrect = !requireMode || (fixture ? ZLive2DProbe.IsSimulated(memory.Client.Mode) : !ZLive2DProbe.IsSimulated(memory.Client.Mode));
                    if (memory.View != null && memory.View.PlayerInputReady && memory.Client.CanGenerate && report.modeCorrect) yield break;
                }
                yield return null;
            }
        }

        IEnumerator RunDialogueSmoke()
        {
            var report = new Report { mode = ModeName(), prompt = "今天你想拍什么？", secondPrompt = "我想再确认一下。" };
            yield return WaitForService(true, report);
            if (!report.modeCorrect)
            {
                report.failure = "固定模式要求记忆服务返回 simulated*，实际为 " + (memory?.Client?.Mode ?? "unknown") + "。";
                Finish(report); yield break;
            }
            var input = FindInput();
            var history = FindHistory();
            var send = FindSend();
            if (!ControlsReady(input, history, send, out var controlFailure))
            {
                report.failure = controlFailure; Finish(report); yield break;
            }
            int before = memory.Conversations.CommittedNpcLines;
            input.text = report.prompt; send.onClick.Invoke();
            report.inputSubmitted = input.text.Length == 0 && history.text.Contains(report.prompt);
            yield return WaitForReply(before, history, Timeout);
            report.replyObserved = memory.Conversations.CommittedNpcLines > before && history.text.Contains("Z：");
            before = memory.Conversations.CommittedNpcLines;
            input.text = report.secondPrompt; input.onSubmit.Invoke(input.text);
            report.secondInputSubmitted = input.text.Length == 0 && history.text.Contains(report.secondPrompt);
            yield return WaitForReply(before, history, Timeout);
            report.secondReplyObserved = memory.Conversations.CommittedNpcLines > before && Count(history.text, "Z：") >= 2;
            report.transcript = history.text;
            report.passed = report.inputSubmitted && report.replyObserved && report.secondInputSubmitted && report.secondReplyObserved;
            if (!report.passed) report.failure = "正常输入、真实 UI 回复或第二回合未完成。";
            memory.Conversations.Cancel("验收结束");
            yield return new WaitForSecondsRealtime(.5f);
            Finish(report);
        }

        IEnumerator RunSmileCheck()
        {
            var report = new Report { mode = ModeName(), prompt = "你笑一下", secondPrompt = "大哭" };
            yield return WaitForService(true, report);
            if (!report.modeCorrect)
            {
                report.failure = "模式不匹配：期望 " + ModeName() + "，实际为 " + (memory?.Client?.Mode ?? "unknown") + "。";
                Finish(report); yield break;
            }
            var portrait = memory.View.GetComponentInChildren<Live2DCharacterView>(true);
            var model = portrait?.CurrentModel;
            var input = FindInput();
            var history = FindHistory();
            var send = FindSend();
            var eye = ZLive2DProbe.Parameter(model, "ParamEyeSmile");
            var speech = ZLive2DProbe.Parameter(model, "ParamZSpeech");
            var tearSob = ZLive2DProbe.Parameter(model, "ParamZTearSob");
            var tearQuiet = ZLive2DProbe.Parameter(model, "ParamZTearQuiet");
            if (portrait == null || model == null || input == null || history == null || send == null || eye == null || speech == null)
            {
                report.failure = "缺少真实 Z 模型、TMP 输入、ParamEyeSmile 或 ParamZSpeech。";
                Finish(report); yield break;
            }
            if (!ControlsReady(input, history, send, out var controlFailure))
            {
                report.failure = controlFailure; Finish(report); yield break;
            }
            portrait.StopPortraitMotion();
            portrait.SetState("neutral", false);
            observedModel = model;
            model.OnDynamicDrawableData += OnDynamicDrawableData;
            model.ForceUpdateNow();
            for (int i = 0; i < 8; i++) yield return null;
            report.neutralEyeSmile = eye.Value;
            report.neutralDrawables = ReadDrawableSamples(model);
            report.missingDrawables = report.neutralDrawables.Where(s => s == null || !s.found).Select(s => s?.id ?? "unknown").ToArray();
            yield return SaveFrame("neutral.png");
            float readyUntil = Time.realtimeSinceStartup + 20;
            while ((!memory.Client.CanGenerate || !send.IsInteractable()) && Time.realtimeSinceStartup < readyUntil)
                yield return null;
            if (!memory.View.PlayerInputReady || !memory.Client.CanGenerate || !send.IsInteractable())
            {
                report.failure = "对话输入尚未就绪。" + memory.Client.Status + " " + memory.Client.LastError;
                Finish(report); yield break;
            }
            int before = memory.Conversations.CommittedNpcLines;
            input.text = report.prompt; send.onClick.Invoke();
            report.inputSubmitted = input.text.Length == 0 && history.text.Contains(report.prompt);
            float maxEye = report.neutralEyeSmile, maxSpeech = 0, finalEye = eye.Value, finalSpeech = speech.Value;
            bool responseSeen = false, ended = false, speechSeen = false, smileSeen = false, speakingFrameSaved = false;
            float deadline = Time.realtimeSinceStartup + Timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                history.ForceMeshUpdate();
                maxEye = Mathf.Max(maxEye, eye.Value); maxSpeech = Mathf.Max(maxSpeech, speech.Value);
                responseSeen |= memory.Conversations.CommittedNpcLines > before && Count(history.text, "Z：") >= 1;
                speechSeen |= responseSeen && speech.Value > SpeechThreshold;
                smileSeen |= responseSeen && eye.Value >= report.neutralEyeSmile + SmileDelta;
                if (!speakingFrameSaved && speechSeen && smileSeen)
                {
                    speakingFrameSaved = true; report.speakingObserved = true;
                    report.smileDrawables = ReadDrawableSamples(model);
                    yield return SaveFrame("speaking.png");
                }
                bool revealed = responseSeen && history.textInfo.characterCount > 0 && history.maxVisibleCharacters >= history.textInfo.characterCount;
                if (revealed && !memory.Conversations.Sending && speech.Value < .08f)
                {
                    ended = true; finalEye = eye.Value; finalSpeech = speech.Value; break;
                }
            }
            report.replyObserved = responseSeen; report.speakingObserved = speechSeen; report.smileObserved = smileSeen;
            report.replyEnded = ended; report.maxEyeSmile = maxEye; report.maxSpeech = maxSpeech;
            report.finalEyeSmile = finalEye; report.finalSpeech = finalSpeech; report.reply = ExtractReply(history.text); report.transcript = history.text; report.committedReplies = memory.Conversations.CommittedNpcLines - before;
            if (ended)
            {
                yield return new WaitForSecondsRealtime(.5f); report.finalEyeSmile = eye.Value; report.finalSpeech = speech.Value;
                report.smileHeldAtEnd = report.finalEyeSmile >= report.neutralEyeSmile + SmileDelta;
                report.endedDrawables = ReadDrawableSamples(model);
                report.smileDrawableChanged = HasDrawableChange(report.neutralDrawables, report.smileDrawables);
                report.endedDrawableEvidence = HasDrawableChange(report.neutralDrawables, report.endedDrawables);
                yield return SaveFrame("smile-held.png");
                float neutralDeadline = Time.realtimeSinceStartup + 7;
                while (Time.realtimeSinceStartup < neutralDeadline)
                {
                    yield return new WaitForEndOfFrame();
                    if (eye.Value <= report.neutralEyeSmile + .1f && speech.Value < .08f &&
                        (tearSob == null || tearSob.Value < .08f) && (tearQuiet == null || tearQuiet.Value < .08f))
                    {
                        report.returnedNeutral = true;
                        break;
                    }
                }
                report.finalEyeSmile = eye.Value; report.finalSpeech = speech.Value;
                yield return SaveFrame("idle-return.png");
            }
            if (fixture && ended)
            {
                before = memory.Conversations.CommittedNpcLines;
                input.text = report.secondPrompt; input.onSubmit.Invoke(input.text);
                report.secondInputSubmitted = input.text.Length == 0 && history.text.Contains(report.secondPrompt);
                float secondDeadline = Time.realtimeSinceStartup + 35; bool secondSeen = false; float maxSob = 0, maxQuiet = 0;
                while (Time.realtimeSinceStartup < secondDeadline)
                {
                    yield return new WaitForEndOfFrame(); history.ForceMeshUpdate();
                    maxSob = Mathf.Max(maxSob, tearSob?.Value ?? 0); maxQuiet = Mathf.Max(maxQuiet, tearQuiet?.Value ?? 0);
                    secondSeen |= memory.Conversations.CommittedNpcLines > before && Count(history.text, "Z：") >= 2;
                    bool revealed = secondSeen && history.textInfo.characterCount > 0 && history.maxVisibleCharacters >= history.textInfo.characterCount;
                    if (revealed && !memory.Conversations.Sending)
                    {
                        report.secondReplyObserved = true;
                        report.secondReply = ExtractReply(history.text);
                        report.secondStayedNeutral = maxSob < .08f && maxQuiet < .08f;
                        break;
                    }
                }
                report.maxTearSob = maxSob; report.maxTearQuiet = maxQuiet;
                yield return new WaitForSecondsRealtime(1.5f);
                report.returnedNeutral = report.returnedNeutral && eye.Value <= report.neutralEyeSmile + .1f && (tearSob == null || tearSob.Value < .08f) && (tearQuiet == null || tearQuiet.Value < .08f);
                yield return SaveFrame("idle-return-second.png");
            }
            report.passed = report.inputSubmitted && report.replyObserved && report.speakingObserved && report.replyEnded && report.smileObserved && report.smileHeldAtEnd &&
                report.returnedNeutral && report.smileDrawableChanged && report.endedDrawableEvidence &&
                (!fixture || (report.secondInputSubmitted && report.secondReplyObserved && report.secondStayedNeutral));
            if (!report.passed) report.failure = SmileFailure(report, fixture);
            memory.Conversations.Cancel("验收结束");
            yield return new WaitForSecondsRealtime(.5f);
            Finish(report);
        }

        TMP_InputField FindInput() => memory?.View?.DialogueInput;
        TextMeshProUGUI FindHistory() => memory?.View?.DialogueHistory;
        Button FindSend() => memory?.View?.DialogueSend;

        static bool ControlsReady(TMP_InputField input, TextMeshProUGUI history, Button send, out string failure)
        {
            string[] oldControls = { "说话", "好奇", "惊讶", "哭泣", "大哭" };
            bool oldVisible = send != null && send.transform.root.GetComponentsInChildren<Button>(true).Any(b => oldControls.Contains(b.name));
            failure = input == null || history == null || send == null ? "缺少正常输入框、对话记录或发送按钮。" : oldVisible ? "发现旧表情控制按钮，未走正常玩家输入。" : null;
            return string.IsNullOrEmpty(failure);
        }

        IEnumerator WaitForReply(int before, TextMeshProUGUI history, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                history?.ForceMeshUpdate();
                if (memory?.Conversations != null && memory.Conversations.CommittedNpcLines > before && !memory.Conversations.Sending) yield break;
                yield return null;
            }
        }

        IEnumerator SaveFrame(string name)
        {
            yield return new WaitForEndOfFrame();
            var frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, name), frame.EncodeToPNG());
            Destroy(frame);
        }

        string SmileFailure(Report r, bool fixedMode)
        {
            var failures = new List<string>();
            if (!r.inputSubmitted) failures.Add("TMP 输入未提交");
            if (!r.replyObserved) failures.Add("没有看到 Z 的真实回复");
            if (!r.speakingObserved) failures.Add("ParamZSpeech 未出现");
            if (!r.replyEnded) failures.Add("回复没有回到待机");
            if (!r.smileObserved) failures.Add("ParamEyeSmile 未出现真实变化");
            if (!r.smileHeldAtEnd) failures.Add("回复结束时表情没有短暂保持");
            if (!r.returnedNeutral) failures.Add("第一句结束后没有自动回到中性待机");
            if (!r.smileDrawableChanged) failures.Add("动态 drawable 没有相对 neutral 基线变化");
            if (!r.endedDrawableEvidence) failures.Add("回复结束时没有保留表情 drawable 证据");
            if (fixedMode && !r.secondInputSubmitted) failures.Add("固定模式第二次输入未提交");
            if (fixedMode && !r.secondReplyObserved) failures.Add("固定模式第二次回复缺失");
            if (fixedMode && !r.secondStayedNeutral) failures.Add("玩家输入“大哭”触发了非请求表情");
            if (fixedMode && !r.returnedNeutral) failures.Add("第二次回复后没有回到中性待机");
            return string.Join("; ", failures);
        }

        static readonly string[] DrawableIds =
        {
            "Eye_Smile_L", "Eye_Smile_R", "Eye_Closed_L", "Eye_Closed_R", "Mouth_Closed", "Mouth_Line",
            "Mouth_Open", "Mouth_O", "ZQuietMouth", "ZSpeechMouth", "ZRoundMouth", "ZSpeech"
        };

        void OnDynamicDrawableData(CubismModel sender, CubismDynamicDrawableData[] data)
        {
            if (sender == null || data == null || sender.Drawables == null) return;
            int count = Mathf.Min(data.Length, sender.Drawables.Length);
            for (int i = 0; i < count; i++)
            {
                var drawable = sender.Drawables[i]; if (drawable == null) continue;
                runtimeOpacities[drawable.Id] = data[i].Opacity; runtimeVisibilities[drawable.Id] = data[i].IsVisible;
            }
        }

        DrawableSample[] ReadDrawableSamples(CubismModel model)
        {
            return DrawableIds.Select(id =>
            {
                var drawable = model?.Drawables?.FirstOrDefault(d => d != null && d.Id == id);
                if (drawable == null) return new DrawableSample { id = id };
                var sample = new DrawableSample { id = id, found = true, opacity = ReadOpacity(model, drawable) };
                var vertices = drawable.VertexPositions;
                if (vertices == null || vertices.Length == 0) return sample;
                var min = vertices[0]; var max = vertices[0]; float area = 0;
                for (int i = 1; i < vertices.Length; i++) { min = Vector3.Min(min, vertices[i]); max = Vector3.Max(max, vertices[i]); }
                var indices = drawable.Indices;
                for (int i = 0; indices != null && i + 2 < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                    area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * .5f;
                }
                sample.width = max.x - min.x; sample.height = max.y - min.y; sample.area = area; sample.centerX = (min.x + max.x) * .5f; sample.centerY = (min.y + max.y) * .5f;
                sample.visible = area > .000001f && (sample.opacity < 0 || sample.opacity > .02f) && (!runtimeVisibilities.TryGetValue(id, out var visible) || visible);
                return sample;
            }).ToArray();
        }

        float ReadOpacity(CubismModel model, CubismDrawable drawable)
        {
            if (runtimeOpacities.TryGetValue(drawable.Id, out var value)) return value;
            int part = drawable.ParentPartIndex;
            return model?.Parts != null && part >= 0 && part < model.Parts.Length && model.Parts[part] != null ? model.Parts[part].Opacity : -1;
        }

        static bool HasDrawableChange(DrawableSample[] before, DrawableSample[] after)
        {
            if (before == null || after == null) return false;
            foreach (var item in after)
            {
                var prior = before.FirstOrDefault(s => s != null && s.id == item.id);
                if (prior == null || !prior.found || !item.found) continue;
                if (item.visible != prior.visible || (item.opacity >= 0 && prior.opacity >= 0 && Mathf.Abs(item.opacity - prior.opacity) > .08f)) return true;
                if (Mathf.Abs(item.width - prior.width) > Mathf.Max(.001f, Mathf.Abs(prior.width) * .03f) ||
                    Mathf.Abs(item.height - prior.height) > Mathf.Max(.001f, Mathf.Abs(prior.height) * .03f) ||
                    Mathf.Abs(item.area - prior.area) > Mathf.Max(.0001f, Mathf.Abs(prior.area) * .03f) ||
                    Vector2.Distance(new Vector2(item.centerX, item.centerY), new Vector2(prior.centerX, prior.centerY)) > .01f) return true;
            }
            return false;
        }

        static int Count(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return 0;
            int count = 0;
            for (int index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
            return count;
        }

        static string ExtractReply(string transcript)
        {
            if (string.IsNullOrEmpty(transcript)) return "";
            int index = transcript.LastIndexOf("Z：", StringComparison.Ordinal);
            return index >= 0 ? transcript.Substring(index + 2).Trim() : transcript.Trim();
        }

        void Finish(Report report)
        {
            if (report == null) report = new Report { mode = ModeName(), failure = "未知验收错误" };
            File.WriteAllText(Path.Combine(output, "smile-report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "dialogue.txt"), (report.passed ? "PASS " : "FAIL ") + (report.failure ?? report.mode + " dialogue"));
            Debug.Log("Z " + report.mode + " dialogue: " + (report.passed ? "PASS" : "FAIL") + " " + report.failure);
            Application.Quit(report.passed ? 0 : 1);
        }

        void OnDestroy()
        {
            if (observedModel != null) observedModel.OnDynamicDrawableData -= OnDynamicDrawableData;
        }
    }
}
