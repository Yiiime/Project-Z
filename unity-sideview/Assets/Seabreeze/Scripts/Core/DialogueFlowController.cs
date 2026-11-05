using System;
using UnityEngine;

namespace Seabreeze
{
    // Presentation lifetime is deliberately separate from the authored dialogue graph.
    public sealed class DialogueFlowController : MonoBehaviour
    {
        public enum Phase { Inactive, Entering, Reading, Exiting }
        public Phase State { get; private set; }
        public bool IsActive => State != Phase.Inactive;
        public bool IsTransitioning => State == Phase.Entering || State == Phase.Exiting;
        public bool CanAdvance => State == Phase.Reading;
        public bool IsNpcConversation => ActiveNpc != null;
        public NpcData ActiveNpc { get; private set; }
        public int Sequence { get; private set; }
        public event Action Changed;
        GameSession session;

        public void Initialize(GameSession owner)
        {
            session = owner;
            owner.Dialogue.Changed += OnLineChanged;
            owner.Dialogue.Closed += OnClosed;
        }
        void OnLineChanged()
        {
            if (!session.Dialogue.IsOpen || State == Phase.Reading || State == Phase.Entering) return;
            Sequence++;
            ActiveNpc = session.Database.FindNpc(session.Dialogue.SourceInteractionId);
            State = Phase.Entering;
            SetPlayerLock(true);
            Changed?.Invoke();
        }
        void OnClosed()
        {
            if (!IsActive) return;
            State = Phase.Exiting;
            Changed?.Invoke();
        }
        public void CompleteEnter(int sequence)
        {
            if (sequence != Sequence || State != Phase.Entering) return;
            State = Phase.Reading;
            Changed?.Invoke();
        }
        public void CompleteExit(int sequence)
        {
            if (sequence != Sequence || State != Phase.Exiting) return;
            State = Phase.Inactive;
            ActiveNpc = null;
            SetPlayerLock(false);
            Changed?.Invoke();
            session.NotifyChanged(); // Apply deferred world presence/lighting after the reverse transition.
        }
        public void RequestReturn()
        {
            if (!CanAdvance) return;
            session.Dialogue.Suspend(); // Unread line and choices are not committed by leaving the UI.
        }
        public void ResetImmediately()
        {
            Sequence++;
            State = Phase.Inactive;
            ActiveNpc = null;
            SetPlayerLock(false);
            Changed?.Invoke();
        }
        void SetPlayerLock(bool value)
        {
            var player = WorldDirector.Instance ? WorldDirector.Instance.Player : null;
            if (player) player.GetComponent<PlayerController>()?.SetConversationLocked(value);
        }
        void OnDestroy()
        {
            if (session && session.Dialogue)
            {
                session.Dialogue.Changed -= OnLineChanged;
                session.Dialogue.Closed -= OnClosed;
            }
            SetPlayerLock(false);
        }
    }
}
