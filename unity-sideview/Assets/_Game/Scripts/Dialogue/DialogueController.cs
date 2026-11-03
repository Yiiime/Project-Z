using System;
using System.Collections.Generic;
using UnityEngine;
using ZFromMoon.Core;
using ZFromMoon.Interaction;
using ZFromMoon.UI;

namespace ZFromMoon.Dialogue
{
    /// <summary>Owns the dialogue mode and applies only explicitly configured action effects.</summary>
    [DisallowMultipleComponent]
    public sealed class DialogueController : MonoBehaviour
    {
        private const int MaxMessages = 60;
        private const int MaxInputLength = 300;
        [SerializeField] private GameSession session;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private DialogueView view;

        private readonly Dictionary<string, List<DialogueMessage>> histories = new Dictionary<string, List<DialogueMessage>>();
        private readonly LocalDialogueResponder responder = new LocalDialogueResponder();
        private DialogueActor actor;
        private DialogueDefinition definition;
        private List<DialogueMessage> history;
        private bool started;
        private bool bound;
        private int openedFrame = -1;
        public bool IsOpen { get; private set; }
        public DialogueActor CurrentActor => actor;
        public event Action<DialogueActor> Opened;
        public event Action Closed;

        private void Start()
        {
            started = true;
            Connect();
        }

        private void OnEnable()
        {
            if (started) Connect();
        }

        private void Connect()
        {
            if (session == null || interactor == null || view == null)
            {
                Debug.LogError("DialogueController requires its session, interactor and view.", this);
                enabled = false;
                return;
            }
            session.Ready += OnSessionReady;
            if (session.IsReady) OnSessionReady();
        }

        private void OnSessionReady()
        {
            if (bound || !session.IsReady) return;
            interactor.InteractionRequested += OnInteraction;
            session.Input.CancelRequested += OnCancelRequested;
            session.Modes.Changed += OnModeChanged;
            session.State.Changed += OnStateChanged;
            view.TextSubmitted += SubmitText;
            view.ActionSelected += SelectAction;
            view.CloseRequested += Close;
            bound = true;
        }

        private void OnInteraction(Interactable target)
        {
            if (!bound || IsOpen || !interactor.CanInteractNow || !view.isActiveAndEnabled || target == null)
                return;
            var candidate = target.GetComponent<DialogueActor>();
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.Definition == null)
                return;
            if (!candidate.Definition.IsConfigured)
            {
                Debug.LogError("DialogueActor needs a configured speaker definition.", candidate);
                return;
            }

            actor = candidate;
            definition = actor.Definition;
            if (!histories.TryGetValue(definition.SpeakerId, out history))
            {
                history = new List<DialogueMessage>();
                histories.Add(definition.SpeakerId, history);
                Append(definition.DisplayName, definition.Greeting, false);
            }
            IsOpen = true;
            openedFrame = Time.frameCount;
            session.Modes.SetMode(GameMode.Dialogue);
            view.Show(definition, history, session.State.Snapshot, session.Location.DisplayName);
            Opened?.Invoke(actor);
        }

        private bool CanContinue => bound && IsOpen && session != null && session.IsReady
            && session.isActiveAndEnabled && session.Modes.Current == GameMode.Dialogue
            && actor != null && actor.isActiveAndEnabled
            && actor.gameObject.scene == session.Location.gameObject.scene;

        private void SubmitText(string text)
        {
            if (!CanContinue || Time.frameCount <= openedFrame || string.IsNullOrWhiteSpace(text)) return;
            string input = text.Trim();
            if (input.Length > MaxInputLength) input = input.Substring(0, MaxInputLength);
            Append("你", input, true);
            // Free text cannot supply flag names or execute story actions.
            Append(definition.DisplayName, responder.Reply(input, definition, session.State.Snapshot), false);
            RefreshView();
        }

        private void SelectAction(int index)
        {
            if (!CanContinue || Time.frameCount <= openedFrame || index < 0 || index >= definition.Actions.Count) return;
            DialogueAction action = definition.Actions[index];
            if (action == null || !action.CanExecute(session.State.Snapshot)) return;
            // Only author-configured action cards reach the state mutation API.
            if (!string.IsNullOrWhiteSpace(action.SetFlag))
                session.State.SetFlag(action.SetFlag, true);
            Append("你", action.Title, true);
            Append(definition.DisplayName, action.Reply, false);
            RefreshView();
        }

        private void Append(string speaker, string text, bool isPlayer)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            history.Add(new DialogueMessage(speaker, text, isPlayer));
            if (history.Count > MaxMessages)
                history.RemoveRange(0, history.Count - MaxMessages);
        }

        private void RefreshView()
        {
            if (CanContinue)
                view.Refresh(definition, history, session.State.Snapshot, session.Location.DisplayName);
        }

        private void OnStateChanged(GameState state)
        {
            RefreshView();
        }

        private void OnModeChanged(GameMode mode)
        {
            if (IsOpen && mode != GameMode.Dialogue)
                CloseInternal(false);
        }

        private void Update()
        {
            if (!bound) return;
            if (IsOpen && (!CanContinue || !view.isActiveAndEnabled || !view.IsVisible))
                Close();
            // A re-enabled App may restore a previous Dialogue mode after its panel was closed.
            // Recover outside the mode event to avoid re-entrant map switching.
            if (!IsOpen && session.IsReady && session.isActiveAndEnabled
                && session.Modes.Current == GameMode.Dialogue)
                session.Modes.SetMode(GameMode.Exploration);
        }

        public void Close()
        {
            CloseInternal(true);
        }

        private void OnCancelRequested()
        {
            if (!CanContinue || Time.frameCount <= openedFrame || view == null
                || !view.isActiveAndEnabled || !view.IsVisible)
                return;

            // The same Escape press may cancel an IME candidate instead of the conversation.
            if (!view.TryHandleCancel())
                Close();
        }

        private void CloseInternal(bool restoreExploration)
        {
            bool wasOpen = IsOpen;
            IsOpen = false;
            actor = null;
            definition = null;
            history = null;
            if (view != null) view.Hide();
            if (restoreExploration && session != null && session.isActiveAndEnabled
                && session.Modes != null && session.Modes.Current == GameMode.Dialogue)
                session.Modes.SetMode(GameMode.Exploration);
            if (wasOpen) Closed?.Invoke();
        }

        private void OnDisable()
        {
            if (started && session != null)
            {
                session.Ready -= OnSessionReady;
                if (bound)
                {
                    if (interactor != null) interactor.InteractionRequested -= OnInteraction;
                    session.Input.CancelRequested -= OnCancelRequested;
                    session.Modes.Changed -= OnModeChanged;
                    session.State.Changed -= OnStateChanged;
                    if (view != null)
                    {
                        view.TextSubmitted -= SubmitText;
                        view.ActionSelected -= SelectAction;
                        view.CloseRequested -= Close;
                    }
                }
            }
            CloseInternal(true);
            bound = false;
        }
    }
}
