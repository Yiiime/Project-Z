using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seabreeze
{
    public sealed class DialogueRunner : MonoBehaviour
    {
        GameSession session;
        DialogueNode node;
        readonly List<DialogueChoice> visibleChoices = new List<DialogueChoice>();
        public bool IsOpen { get; private set; }
        public string Speaker => node != null ? node.speaker : "";
        public string Text => node != null ? node.text : "";
        public IReadOnlyList<DialogueChoice> Choices => visibleChoices;
        public string CurrentNodeId => node != null ? node.id : "";
        public event Action Changed;
        public event Action Closed;
        public void Initialize(GameSession owner) { session = owner; }
        public void StartDialogue(string id)
        {
            if (!session || !session.Database) return;
            Enter(id);
        }
        void Enter(string id)
        {
            node = session.Database.FindDialogue(id);
            if (node == null)
            {
                if (!string.IsNullOrEmpty(id)) Debug.LogError("Unknown Seabreeze dialogue node: " + id);
                Finish();
                return;
            }
            visibleChoices.Clear();
            if (node.choices != null)
                foreach (var choice in node.choices)
                    if (string.IsNullOrEmpty(choice.requiredFlag) || session.HasFlag(choice.requiredFlag)) visibleChoices.Add(choice);
            IsOpen = true;
            session.State.resumeDialogue = id;
            Changed?.Invoke();
        }
        public void Advance()
        {
            if (!IsOpen || visibleChoices.Count > 0) return;
            var next = node.next;
            session.Story.ApplyEffects(node.effects);
            if (string.IsNullOrEmpty(next)) Finish(); else Enter(next);
            session.Save();
        }
        public void Choose(int index)
        {
            if (!IsOpen || index < 0 || index >= visibleChoices.Count) return;
            var choice = visibleChoices[index];
            var next = choice.next;
            session.Story.ApplyEffects(node.effects);
            session.Story.ApplyEffects(choice.effects);
            if (string.IsNullOrEmpty(next)) Finish(); else Enter(next);
            session.Save();
        }
        void Finish()
        {
            IsOpen = false;
            node = null;
            visibleChoices.Clear();
            if (session) session.State.resumeDialogue = "";
            Changed?.Invoke();
            Closed?.Invoke();
            if (session)
            {
                session.Story?.Evaluate();
                session.NotifyChanged();
                session.Save();
            }
        }
        public void Cancel()
        {
            IsOpen = false;
            node = null;
            visibleChoices.Clear();
            if (session && session.State != null) session.State.resumeDialogue = "";
            Changed?.Invoke();
            Closed?.Invoke();
        }
    }
}
