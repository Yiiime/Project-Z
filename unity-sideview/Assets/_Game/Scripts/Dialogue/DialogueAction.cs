using System;
using UnityEngine;
using ZFromMoon.Core;

namespace ZFromMoon.Dialogue
{
    /// <summary>Describes an authored action; eligibility reads state without changing it.</summary>
    [Serializable]
    public sealed class DialogueAction
    {
        [SerializeField] private string title;
        [SerializeField, TextArea] private string description;
        [SerializeField, TextArea] private string reply;
        [SerializeField] private string requiredFlag;
        [SerializeField] private string setFlag;
        [SerializeField] private bool once;

        public string Title => title;
        public string Description => description;
        public string Reply => reply;
        public string RequiredFlag => requiredFlag;
        public string SetFlag => setFlag;
        public bool Once => once;

        public bool CanExecute(GameState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredFlag) && !state.HasFlag(requiredFlag))
            {
                return false;
            }

            return !once || string.IsNullOrWhiteSpace(setFlag) || !state.HasFlag(setFlag);
        }
    }
}
