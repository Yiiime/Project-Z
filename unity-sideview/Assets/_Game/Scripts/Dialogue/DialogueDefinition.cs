using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZFromMoon.Dialogue
{
    [CreateAssetMenu(fileName = "DialogueDefinition", menuName = "Z From Moon/Dialogue Definition")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        [SerializeField] private string speakerId;
        [SerializeField] private string displayName;
        [SerializeField] private string subtitle;
        [SerializeField, TextArea] private string greeting;
        [SerializeField, TextArea] private string freeInputReply;
        [SerializeField] private Sprite portrait;
        [SerializeField] private DialogueAction[] actions = Array.Empty<DialogueAction>();

        public string SpeakerId => speakerId;
        public string DisplayName => displayName;
        public string Subtitle => subtitle;
        public string Greeting => greeting;
        public string FreeInputReply => freeInputReply;
        public Sprite Portrait => portrait;
        public IReadOnlyList<DialogueAction> Actions => actions ?? Array.Empty<DialogueAction>();
        public bool IsConfigured => !string.IsNullOrWhiteSpace(speakerId)
            && !string.IsNullOrWhiteSpace(displayName);
    }
}
