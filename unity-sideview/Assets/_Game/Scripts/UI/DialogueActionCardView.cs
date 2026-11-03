using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZFromMoon.Core;
using ZFromMoon.Dialogue;

namespace ZFromMoon.UI
{
    /// <summary>Displays an authored action; buttons raise intent without applying story effects.</summary>
    [DisallowMultipleComponent]
    public sealed class DialogueActionCardView : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text descriptionLabel;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;
        [SerializeField] private TMP_Text statusLabel;

        private bool listening;
        private bool reportedMissingReferences;
        public bool CanAccept => isActiveAndEnabled && acceptButton != null && acceptButton.interactable;
        public bool CanDecline => isActiveAndEnabled && declineButton != null && declineButton.interactable;
        public event Action Accepted;
        public event Action Declined;

        private void OnEnable()
        {
            if (listening || !HasReferences()) return;
            acceptButton.onClick.AddListener(Accept);
            declineButton.onClick.AddListener(Decline);
            listening = true;
        }

        private void OnDisable()
        {
            if (!listening) return;
            acceptButton.onClick.RemoveListener(Accept);
            declineButton.onClick.RemoveListener(Decline);
            listening = false;
        }

        public void Bind(DialogueAction action, GameState state)
        {
            if (!HasReferences()) return;
            titleLabel.richText = false;
            descriptionLabel.richText = false;
            statusLabel.richText = false;
            if (action == null || state == null)
            {
                titleLabel.text = string.Empty;
                descriptionLabel.text = string.Empty;
                statusLabel.text = string.Empty;
                acceptButton.interactable = false;
                declineButton.interactable = false;
                return;
            }

            bool recorded = action.Once && !string.IsNullOrWhiteSpace(action.SetFlag)
                && state.HasFlag(action.SetFlag);
            titleLabel.text = action.Title;
            descriptionLabel.text = action.Description;
            statusLabel.text = recorded ? "已记录" : "不消耗时段";
            acceptButton.interactable = action.CanExecute(state);
            declineButton.interactable = !recorded;
        }

        private void Accept()
        {
            if (CanAccept) Accepted?.Invoke();
        }

        private void Decline()
        {
            if (CanDecline) Declined?.Invoke();
        }

        private bool HasReferences()
        {
            if (titleLabel != null && descriptionLabel != null && acceptButton != null
                && declineButton != null && statusLabel != null) return true;
            if (!reportedMissingReferences)
            {
                Debug.LogError("DialogueActionCardView requires its labels and accept/decline buttons.", this);
                reportedMissingReferences = true;
            }
            return false;
        }
    }
}
