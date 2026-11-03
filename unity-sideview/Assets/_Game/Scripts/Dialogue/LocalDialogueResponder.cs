using ZFromMoon.Core;

namespace ZFromMoon.Dialogue
{
    /// <summary>Offline placeholder. A future responder receives only a read-only state snapshot.</summary>
    public sealed class LocalDialogueResponder
    {
        public string Reply(string input, DialogueDefinition definition, GameState snapshot)
        {
            if (string.IsNullOrWhiteSpace(input) || definition == null || snapshot == null)
                return string.Empty;
            // No model or language understanding is implied by this configured acknowledgement.
            return definition.FreeInputReply;
        }
    }
}
