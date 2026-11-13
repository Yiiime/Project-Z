using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Seabreeze
{
    // Local authored conversation uses the same glass, bubbles and Live2D view as free chat.
    // It neither generates AI responses nor fabricates backend conversation receipts.
    public sealed partial class MemoryDialogueView
    {
        FirstDayLine[] storyLines;
        int storyIndex, storyFrame, storyGeneration;
        Action<bool> storyFinished;
        SaveData storyOwner;
        RectTransform storyControls;
        Button storyNext;
        TextMeshProUGUI storyHint;
        public bool ScriptedOpen { get; private set; }

        public void OpenStory(string actor, FirstDayLine[] lines, Action<bool> finished)
        {
            if (ScriptedOpen || storyFinished != null || Closing || memory == null || playerPanel == null
                || lines == null || lines.Length == 0 || Array.Exists(lines, line => line == null))
            {
                finished?.Invoke(false);
                return;
            }
            memory.Conversations.CancelAll("剧情交谈");
            ScriptedOpen = true;
            storyGeneration++;
            storyOwner = session.State;
            storyFinished = finished; storyLines = lines; storyIndex = 0;
            // Authored dialogue reuses the presentation without free-chat task cards.
            ClearTaskCards();
            portrait.anchorMin = new Vector2(.015f,.025f); portrait.anchorMax = new Vector2(.445f,.84f);
            portrait.offsetMin = portrait.offsetMax = Vector2.zero;
            column.anchorMin = new Vector2(.455f,.12f); column.anchorMax = new Vector2(.95f,.87f);
            column.offsetMin = column.offsetMax = Vector2.zero;
            OpenPlayer(actor);
            input.DeactivateInputField(); replySurface.gameObject.SetActive(false);
            if (inputHelp != null) inputHelp.gameObject.SetActive(false);
            if (storyControls == null)
            {
                storyControls = UIPrimitives.Rect("Story continuation", column, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0,72));
                storyControls.anchorMax = Vector2.right;
                storyNext = UIPrimitives.Button("Continue story", storyControls, Vector2.one, Vector2.one, Vector2.zero, new Vector2(260,64), "继续 →", AdvanceStory, true, 23);
                storyHint = UIPrimitives.Label("Story keys", storyControls, Vector2.zero, Vector2.zero, new Vector2(0,12), new Vector2(460,48), "E / 空格 继续 · Esc 暂停交谈", 17, new Color(.92f,.95f,1));
            }
            storyControls.gameObject.SetActive(true);
            playerState.text = "";
            PresentStoryLine();
        }
        void PresentStoryLine()
        {
            storyFrame = Time.frameCount;
            var line = storyLines[storyIndex];
            ShowLine(line.speaker == "player" ? "player" : selectedActor, line.text, Vector3.zero, true);
            storyNext.GetComponentInChildren<TextMeshProUGUI>().text = storyIndex == storyLines.Length - 1 ? "听完，回到小岛 →" : "继续 →";
            storyHint.text = (storyIndex + 1) + " / " + storyLines.Length + "    E / 空格 继续 · Esc 稍后再聊";
        }
        void AdvanceStory()
        {
            if (!ScriptedOpen || !PlayerInputReady || Time.frameCount <= storyFrame) return;
            if (typing) { FinishTyping(); return; }
            if (++storyIndex < storyLines.Length) PresentStoryLine();
            else CloseStory(true);
        }
        void CloseStory(bool completed)
        {
            if (!ScriptedOpen) return;
            ScriptedOpen = false;
            storyControls.gameObject.SetActive(false);
            HidePlayer();
            StartCoroutine(FinishStoryClose(storyGeneration, completed));
        }
        IEnumerator FinishStoryClose(int generation, bool completed)
        {
            while (Closing) yield return null;
            if (generation != storyGeneration) yield break;
            bool sameTimeline = storyOwner == session.State;
            var callback = ResetStoryState();
            callback?.Invoke(completed && sameTimeline);
        }
        Action<bool> ResetStoryState()
        {
            var callback = storyFinished;
            storyFinished = null; storyOwner = null; storyLines = null; storyIndex = 0;
            storyGeneration++; ScriptedOpen = false;
            if (storyControls != null) storyControls.gameObject.SetActive(false);
            if (replySurface != null) replySurface.gameObject.SetActive(true);
            if (inputHelp != null) inputHelp.gameObject.SetActive(true);
            return callback;
        }
        void TickStory()
        {
            if (!ScriptedOpen) return;
            playerState.text = "第一天 · 小岛来信";
            if (PlayerInputReady && GameInput.Pressed(Key.Escape)) { CloseStory(false); return; }
            if (PlayerInputReady && (GameInput.Pressed(Key.E) || GameInput.Pressed(Key.Space) || GameInput.Pressed(Key.Enter))) AdvanceStory();
            if (typing && PlayerInputReady)
            {
                revealed += Time.unscaledDeltaTime * UIPrimitives.Style.TypewriterSpeed;
                historyText.maxVisibleCharacters = Mathf.FloorToInt(revealed);
                if (historyText.maxVisibleCharacters >= historyText.textInfo.characterCount) FinishTyping();
                else character.SetState("neutral", lastSpeaker == selectedActor);
            }
            RefreshTypingLayout();
        }
        void CloseConversationView()
        {
            if (ScriptedOpen) CloseStory(false);
            else if (!LineFullyVisible)
            {
                // First close reveals the full line; a second close exits. Never
                // silently discard an introduction because its voice is slower.
                memory.Client.StopSpeech(); FinishTyping();
            }
            else memory.Conversations.Cancel("玩家结束交谈");
        }
    }
}
