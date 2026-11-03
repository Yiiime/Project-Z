using System;

namespace ZFromMoon.Core
{
    /// <summary>Owns the current interaction mode without depending on Unity objects.</summary>
    public sealed class GameModeController
    {
        public GameMode Current { get; private set; } = GameMode.Transition;

        public event Action<GameMode> Changed;

        public void SetMode(GameMode mode)
        {
            if (!Enum.IsDefined(typeof(GameMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown game mode.");
            }

            if (Current == mode)
            {
                return;
            }

            Current = mode;
            Changed?.Invoke(Current);
        }
    }
}
