namespace ZFromMoon.Dialogue
{
    public sealed class DialogueMessage
    {
        public string Speaker { get; }
        public string Text { get; }
        public bool IsPlayer { get; }

        public DialogueMessage(string speaker, string text, bool isPlayer)
        {
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            IsPlayer = isPlayer;
        }
    }
}
