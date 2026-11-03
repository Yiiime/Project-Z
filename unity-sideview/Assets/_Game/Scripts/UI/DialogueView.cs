using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using ZFromMoon.Core;
using ZFromMoon.Dialogue;

namespace ZFromMoon.UI
{
    /// <summary>Renders dialogue and raises user intent; never changes game mode or story state.</summary>
    [DisallowMultipleComponent]
    public sealed class DialogueView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Image portraitImage;
        [SerializeField] private TMP_Text speakerName;
        [SerializeField] private TMP_Text subtitleLabel;
        [SerializeField] private TMP_Text contextLabel;
        [SerializeField] private ScrollRect messageScroll;
        [SerializeField] private RectTransform messageContent;
        [SerializeField] private DialogueMessageView messageTemplate;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform actionContent;
        [SerializeField] private DialogueActionCardView actionTemplate;
        [SerializeField] private ScrollRect actionsScroll;

        private readonly List<DialogueMessageView> messageRows = new List<DialogueMessageView>();
        private readonly List<ActionRow> actionRows = new List<ActionRow>();
        private readonly HashSet<int> declinedActions = new HashSet<int>();
        private bool configured;
        private bool listening;
        private bool focusPending;
        private bool scrollMessagesPending;
        private bool resetActionsPending;
        private Coroutine layoutRoutine;
        private float layoutWidth = -1f;
        private float retainedMessageScroll;
        private float retainedActionsScroll;
        private int renderedMessageCount;
        private DialogueMessage lastRenderedMessage;
        private Keyboard imeKeyboard;
        private bool imeCompositionActive;
        private int compositionEndedFrame = -1;
        private int compositionObservedFrame = -1;

        public bool IsVisible => panel != null && panel.activeSelf;
        public event Action<string> TextSubmitted;
        public event Action<int> ActionSelected;
        public event Action<int> ActionDeclined;
        public event Action CloseRequested;

        private sealed class ActionRow
        {
            public DialogueActionCardView View;
            public Action Accept;
            public Action Decline;
        }

        private void Awake()
        {
            if (panel == null || portraitImage == null || speakerName == null
                || subtitleLabel == null || contextLabel == null || messageScroll == null
                || messageContent == null || messageTemplate == null || inputField == null
                || sendButton == null || closeButton == null || actionContent == null
                || actionTemplate == null || actionsScroll == null)
            {
                Debug.LogError("DialogueView requires its panel, portrait, labels, input, buttons and row templates.", this);
                enabled = false;
                return;
            }

            if (transform.IsChildOf(panel.transform)
                || messageTemplate.transform.parent != messageContent
                || actionTemplate.transform.parent != actionContent)
            {
                Debug.LogError("DialogueView must remain outside its hidden panel; row templates must be direct children of their content containers.", this);
                enabled = false;
                return;
            }

            configured = true;
            portraitImage.preserveAspect = true;
            inputField.restoreOriginalTextOnEscape = false;
            inputField.onFocusSelectAll = false;
            messageTemplate.gameObject.SetActive(false);
            actionTemplate.gameObject.SetActive(false);
            Hide();
        }

        private void OnEnable()
        {
            if (!configured || listening) return;
            listening = true;
            sendButton.onClick.AddListener(SubmitText);
            closeButton.onClick.AddListener(RequestClose);
            inputField.onValueChanged.AddListener(UpdateSendAvailability);
            foreach (ActionRow row in actionRows)
            {
                row.View.Accepted += row.Accept;
                row.View.Declined += row.Decline;
            }
            SyncImeKeyboard();
            UpdateSendAvailability(inputField.text);
        }

        private void OnDisable()
        {
            if (!configured) return;
            if (listening)
            {
                sendButton.onClick.RemoveListener(SubmitText);
                closeButton.onClick.RemoveListener(RequestClose);
                inputField.onValueChanged.RemoveListener(UpdateSendAvailability);
                foreach (ActionRow row in actionRows)
                {
                    row.View.Accepted -= row.Accept;
                    row.View.Declined -= row.Decline;
                }
            }
            listening = false;
            SetImeKeyboard(null);
            Hide();
        }

        public void Show(DialogueDefinition definition, IReadOnlyList<DialogueMessage> messages,
            GameState state, string locationName)
        {
            if (!configured) return;
            declinedActions.Clear();
            ClearImeState();
            renderedMessageCount = 0;
            lastRenderedMessage = null;
            panel.SetActive(true);
            inputField.SetTextWithoutNotify(string.Empty);
            Render(definition, messages, state, locationName);
            UpdateSendAvailability(inputField.text);
            ScheduleLayout(true, true, true);
        }

        public void Refresh(DialogueDefinition definition, IReadOnlyList<DialogueMessage> messages,
            GameState state, string locationName)
        {
            if (!configured || !IsVisible) return;
            bool historyChanged = Render(definition, messages, state, locationName);
            UpdateSendAvailability(inputField.text);
            ScheduleLayout(false, historyChanged, false);
        }

        public void Hide()
        {
            if (!configured) return;
            if (layoutRoutine != null) StopCoroutine(layoutRoutine);
            layoutRoutine = null;
            focusPending = false;
            scrollMessagesPending = false;
            resetActionsPending = false;
            declinedActions.Clear();
            ClearImeState();
            inputField.DeactivateInputField();
            inputField.SetTextWithoutNotify(string.Empty);
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(panel.transform))
                EventSystem.current.SetSelectedGameObject(null);
            panel.SetActive(false);
            sendButton.interactable = false;
        }

        private bool Render(DialogueDefinition definition, IReadOnlyList<DialogueMessage> messages,
            GameState state, string locationName)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (state == null) throw new ArgumentNullException(nameof(state));

            speakerName.text = definition.DisplayName;
            subtitleLabel.text = definition.Subtitle;
            contextLabel.text = $"{locationName} · {TimeSlotLabel(state.TimeSlot)} · 第 {state.Day} 天";
            portraitImage.sprite = definition.Portrait;
            portraitImage.enabled = definition.Portrait != null;
            if (definition.Portrait != null)
            {
                var fitter = portraitImage.GetComponent<AspectRatioFitter>();
                if (fitter != null && definition.Portrait.rect.height > 0f)
                    fitter.aspectRatio = definition.Portrait.rect.width / definition.Portrait.rect.height;
            }

            DialogueMessage lastMessage = messages.Count > 0 ? messages[messages.Count - 1] : null;
            bool historyChanged = renderedMessageCount != messages.Count || !ReferenceEquals(lastRenderedMessage, lastMessage);
            renderedMessageCount = messages.Count;
            lastRenderedMessage = lastMessage;
            float availableWidth = AvailableMessageWidth();
            for (int i = 0; i < messages.Count; i++)
            {
                if (i == messageRows.Count)
                {
                    DialogueMessageView row = Instantiate(messageTemplate, messageContent, false);
                    row.name = "Message";
                    messageRows.Add(row);
                }
                messageRows[i].Bind(messages[i], availableWidth);
                messageRows[i].gameObject.SetActive(true);
            }
            for (int i = messages.Count; i < messageRows.Count; i++)
                messageRows[i].gameObject.SetActive(false);

            IReadOnlyList<DialogueAction> actions = definition.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                if (i == actionRows.Count) CreateActionRow(i);
                DialogueAction action = actions[i];
                ActionRow row = actionRows[i];
                if (action == null || declinedActions.Contains(i))
                {
                    row.View.gameObject.SetActive(false);
                    continue;
                }
                row.View.Bind(action, state);
                row.View.gameObject.SetActive(true);
            }
            for (int i = actions.Count; i < actionRows.Count; i++)
                actionRows[i].View.gameObject.SetActive(false);
            return historyChanged;
        }

        private void CreateActionRow(int index)
        {
            DialogueActionCardView card = Instantiate(actionTemplate, actionContent, false);
            card.name = "ActionCard";
            Action accept = () => SelectAction(index);
            Action decline = () => DeclineAction(index);
            if (listening)
            {
                card.Accepted += accept;
                card.Declined += decline;
            }
            actionRows.Add(new ActionRow { View = card, Accept = accept, Decline = decline });
        }

        private void SubmitText()
        {
            if (!IsVisible || !inputField.interactable) return;
            string text = inputField.text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            inputField.SetTextWithoutNotify(string.Empty);
            UpdateSendAvailability(string.Empty);
            TextSubmitted?.Invoke(text);
            // A deliberate send click returns focus to writing; ordinary Refresh never does.
            if (IsVisible && isActiveAndEnabled) FocusInput();
        }

        private void SelectAction(int index)
        {
            if (index < 0 || index >= actionRows.Count) return;
            if (IsVisible && !declinedActions.Contains(index) && actionRows[index].View.CanAccept)
                ActionSelected?.Invoke(index);
        }

        private void DeclineAction(int index)
        {
            if (!IsVisible || index < 0 || index >= actionRows.Count || !actionRows[index].View.CanDecline) return;
            declinedActions.Add(index);
            actionRows[index].View.gameObject.SetActive(false);
            ScheduleLayout(false, false, false);
            ActionDeclined?.Invoke(index);
        }

        private void RequestClose()
        {
            if (IsVisible) CloseRequested?.Invoke();
        }

        private void UpdateSendAvailability(string value)
        {
            sendButton.interactable = IsVisible && !string.IsNullOrWhiteSpace(value);
        }

        private void LateUpdate()
        {
            if (!configured || !IsVisible) return;
            SyncImeKeyboard();
            if (HasTmpComposition()) compositionObservedFrame = Time.frameCount;
            float width = AvailableMessageWidth();
            if (layoutRoutine == null && Mathf.Abs(width - layoutWidth) > 0.5f)
                ScheduleLayout(false, false, false);
        }

        private float AvailableMessageWidth()
        {
            float width = messageContent.rect.width;
            var group = messageContent.GetComponent<VerticalLayoutGroup>();
            if (group != null) width -= group.padding.horizontal;
            return width > 1f ? width : 600f;
        }

        private void ScheduleLayout(bool focusInput, bool scrollMessages, bool resetActions)
        {
            focusPending |= focusInput;
            scrollMessagesPending |= scrollMessages;
            resetActionsPending |= resetActions;
            if (layoutRoutine == null)
            {
                retainedMessageScroll = messageScroll.verticalNormalizedPosition;
                retainedActionsScroll = actionsScroll.verticalNormalizedPosition;
            }
            if (layoutRoutine != null) StopCoroutine(layoutRoutine);
            if (isActiveAndEnabled) layoutRoutine = StartCoroutine(FinishLayout());
        }

        private IEnumerator FinishLayout()
        {
            yield return null;
            layoutRoutine = null;
            if (!IsVisible) yield break;
            Canvas.ForceUpdateCanvases();
            layoutWidth = AvailableMessageWidth();
            foreach (DialogueMessageView row in messageRows)
                if (row.gameObject.activeSelf) row.Relayout(layoutWidth);
            LayoutRebuilder.ForceRebuildLayoutImmediate(messageContent);
            LayoutRebuilder.ForceRebuildLayoutImmediate(actionContent);
            Canvas.ForceUpdateCanvases();
            if (scrollMessagesPending) messageScroll.StopMovement();
            if (resetActionsPending) actionsScroll.StopMovement();
            messageScroll.verticalNormalizedPosition = scrollMessagesPending ? 0f : retainedMessageScroll;
            actionsScroll.verticalNormalizedPosition = resetActionsPending ? 1f : retainedActionsScroll;
            bool shouldFocus = focusPending;
            focusPending = false;
            scrollMessagesPending = false;
            resetActionsPending = false;
            if (shouldFocus) FocusInput();
        }

        /// <summary>True consumes this cancel for IME; false lets the controller close the dialogue.</summary>
        public bool TryHandleCancel()
        {
            if (!configured || !IsVisible) return false;
            SyncImeKeyboard();
            bool editing = inputField.isFocused || (EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == inputField.gameObject);
            if (!editing) return false;
            // IME may publish its empty composition before the same Escape input action arrives.
            bool consumed = HasTmpComposition() || imeCompositionActive
                || compositionEndedFrame == Time.frameCount || compositionObservedFrame == Time.frameCount;
            if (consumed) ScheduleLayout(true, false, false);
            return consumed;
        }

        private bool HasTmpComposition()
        {
            if (!inputField.isFocused) return false;
            var module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
            return module != null && module.input != null
                && !string.IsNullOrEmpty(module.input.compositionString);
        }

        private void SyncImeKeyboard()
        {
            if (imeKeyboard != Keyboard.current) SetImeKeyboard(Keyboard.current);
        }

        private void SetImeKeyboard(Keyboard keyboard)
        {
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange -= OnImeComposition;
            imeKeyboard = keyboard;
            ClearImeState();
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange += OnImeComposition;
        }

        private void OnImeComposition(IMECompositionString composition)
        {
            if (!IsVisible) return;
            bool active = composition.Count > 0;
            if (active) compositionObservedFrame = Time.frameCount;
            else if (imeCompositionActive) compositionEndedFrame = Time.frameCount;
            imeCompositionActive = active;
        }

        private void ClearImeState()
        {
            imeCompositionActive = false;
            compositionEndedFrame = -1;
            compositionObservedFrame = -1;
        }

        private void FocusInput()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            inputField.ActivateInputField();
        }

        private static string TimeSlotLabel(TimeSlot slot)
        {
            switch (slot)
            {
                case TimeSlot.Morning: return "清晨";
                case TimeSlot.Afternoon: return "午后";
                case TimeSlot.Evening: return "傍晚";
                case TimeSlot.Night: return "夜晚";
                default: return string.Empty;
            }
        }
    }
}
