using System;
using System.Collections.Generic;

namespace ZFromMoon.Core
{
    /// <summary>Applies explicit game-rule changes and publishes immutable snapshots.</summary>
    public sealed class GameStateService
    {
        private const int SlotsPerDay = 4;

        public GameState Snapshot { get; private set; }

        public event Action<GameState> Changed;

        public GameStateService(int day, TimeSlot timeSlot, string locationId, string entryId)
        {
            Snapshot = new GameState(day, timeSlot, locationId, entryId);
        }

        public void SetLocation(string locationId, string entryId)
        {
            GameState.ValidateId(locationId, nameof(locationId));
            GameState.ValidateId(entryId, nameof(entryId));

            if (string.Equals(Snapshot.LocationId, locationId, StringComparison.Ordinal)
                && string.Equals(Snapshot.EntryId, entryId, StringComparison.Ordinal))
            {
                return;
            }

            Publish(new GameState(Snapshot.Day, Snapshot.TimeSlot, locationId, entryId, Snapshot.Flags));
        }

        public void SetFlag(string flag, bool value)
        {
            GameState.ValidateId(flag, nameof(flag));
            if (Snapshot.HasFlag(flag) == value)
            {
                return;
            }

            var flags = new HashSet<string>(Snapshot.Flags, StringComparer.Ordinal);
            if (value)
            {
                flags.Add(flag);
            }
            else
            {
                flags.Remove(flag);
            }

            Publish(new GameState(Snapshot.Day, Snapshot.TimeSlot, Snapshot.LocationId, Snapshot.EntryId, flags));
        }

        /// <summary>Advances only when an accepted game action explicitly requests it.</summary>
        public void AdvanceTime(int slots)
        {
            if (slots < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slots), slots, "Time cannot advance by a negative number of slots.");
            }

            if (slots == 0)
            {
                return;
            }

            long totalSlots = ((long)Snapshot.Day - 1) * SlotsPerDay + (int)Snapshot.TimeSlot + slots;
            long nextDay = totalSlots / SlotsPerDay + 1;
            if (nextDay > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(slots), slots, "The resulting day exceeds the supported range.");
            }

            var nextSlot = (TimeSlot)(totalSlots % SlotsPerDay);
            Publish(new GameState((int)nextDay, nextSlot, Snapshot.LocationId, Snapshot.EntryId, Snapshot.Flags));
        }

        private void Publish(GameState next)
        {
            Snapshot = next;
            Changed?.Invoke(next);
        }
    }
}
