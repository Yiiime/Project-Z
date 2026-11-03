using System;
using System.Collections.Generic;

namespace ZFromMoon.Core
{
    /// <summary>A detached, immutable view of the current game state.</summary>
    public sealed class GameState
    {
        private readonly HashSet<string> flags;

        public int Day { get; }
        public TimeSlot TimeSlot { get; }
        public string LocationId { get; }
        public string EntryId { get; }
        public IReadOnlyCollection<string> Flags { get; }

        public GameState(
            int day,
            TimeSlot timeSlot,
            string locationId,
            string entryId,
            IEnumerable<string> flags = null)
        {
            if (day < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(day), day, "Day must be at least 1.");
            }

            if (!Enum.IsDefined(typeof(TimeSlot), timeSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(timeSlot), timeSlot, "Unknown time slot.");
            }

            ValidateId(locationId, nameof(locationId));
            ValidateId(entryId, nameof(entryId));

            this.flags = new HashSet<string>(StringComparer.Ordinal);
            if (flags != null)
            {
                foreach (string flag in flags)
                {
                    ValidateId(flag, nameof(flags));
                    this.flags.Add(flag);
                }
            }

            // Neither the lookup set nor a mutable backing collection escapes the snapshot.
            var orderedFlags = new List<string>(this.flags);
            orderedFlags.Sort(StringComparer.Ordinal);
            Flags = orderedFlags.AsReadOnly();

            Day = day;
            TimeSlot = timeSlot;
            LocationId = locationId;
            EntryId = entryId;
        }

        public bool HasFlag(string flag)
        {
            ValidateId(flag, nameof(flag));
            return flags.Contains(flag);
        }

        internal static void ValidateId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("An ID must contain a non-whitespace character.", parameterName);
            }
        }
    }
}
