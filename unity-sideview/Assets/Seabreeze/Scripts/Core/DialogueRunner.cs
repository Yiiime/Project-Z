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
        public string SourceInteractionId { get; private set; } = "";
        public string Emotion => node != null ? node.emotion : "neutral";
        public string Motion => node != null ? node.motion : "";
        public string SpeakerId => node != null ? node.speakerId : "";
        public event Action Changed;
        public event Action Closed;
        public void Initialize(GameSession owner) { session = owner; }
        public void StartDialogue(string id, string sourceInteractionId = "")
        {
            if (!session || !session.Database) return;
            if (session.Conversation && session.Conversation.IsActive) return;
            SourceInteractionId = sourceInteractionId ?? "";
            if (!string.IsNullOrEmpty(SourceInteractionId) && session.State.resumeInteractionId == SourceInteractionId
                && !string.IsNullOrEmpty(session.State.resumeDialogue)) id = session.State.resumeDialogue;
            else if (!string.IsNullOrEmpty(SourceInteractionId))
            {
                var bookmark = session.State.dialogueBookmarks.Find(b => b.source == SourceInteractionId);
                if (bookmark != null && bookmark.day == session.State.day && bookmark.period == session.State.period
                    && session.Database.FindDialogue(bookmark.node) != null) id = bookmark.node;
            }
            Enter(id);
        }
        void Enter(string id)
        {
            id = session.Story.ResolveCommittedDialogue(id);
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
            session.State.resumeInteractionId = SourceInteractionId;
            if (!string.IsNullOrEmpty(SourceInteractionId))
            {
                var bookmark = session.State.dialogueBookmarks.Find(b => b.source == SourceInteractionId);
                if (bookmark == null) { bookmark = new DialogueBookmark { source = SourceInteractionId }; session.State.dialogueBookmarks.Add(bookmark); }
                bookmark.node = id;
                bookmark.day = session.State.day; bookmark.period = session.State.period;
            }
            Changed?.Invoke();
        }
        public void Advance()
        {
            if (!IsOpen || visibleChoices.Count > 0 || (session.Conversation && !session.Conversation.CanAdvance)) return;
            var next = node.next;
            session.Story.ApplyEffects(node.effects);
            if (string.IsNullOrEmpty(next)) Finish(); else Enter(next);
            session.Save();
        }
        public void Choose(int index)
        {
            if (!IsOpen || index < 0 || index >= visibleChoices.Count || (session.Conversation && !session.Conversation.CanAdvance)) return;
            var choice = visibleChoices[index];
            var next = choice.next;
            session.Story.ApplyEffects(node.effects);
            session.Story.ApplyEffects(choice.effects);
            if (string.IsNullOrEmpty(next)) Finish(); else Enter(next);
            session.Save();
        }
        void Finish()
        {
            if (session) session.State.dialogueBookmarks.RemoveAll(b => b.source == SourceInteractionId);
            IsOpen = false;
            node = null;
            visibleChoices.Clear();
            if (session) { session.State.resumeDialogue = ""; session.State.resumeInteractionId = ""; }
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
            if (session && session.State != null) { session.State.resumeDialogue = ""; session.State.resumeInteractionId = ""; }
            Changed?.Invoke();
            Closed?.Invoke();
        }
        public void Suspend()
        {
            if (!IsOpen) return;
            // Bookmark remains for returning to this person. Do not reopen it after travelling elsewhere and quitting.
            session.State.resumeDialogue = "";
            session.State.resumeInteractionId = "";
            IsOpen = false;
            node = null;
            visibleChoices.Clear();
            Changed?.Invoke();
            Closed?.Invoke();
            session.Save();
        }
    }
}
