using System;
using System.Collections.Generic;

namespace Seabreeze
{
    public enum FirstDayPhase { None, Live, Arrival, Harbor, WalkToSquare, MeetIslanders, Fireworks, Home, Bedtime, Complete }

    [Serializable]
    public sealed class FirstDayState
    {
        public FirstDayPhase phase = FirstDayPhase.Live;
        public List<string> greeted = new List<string>();
        public bool invitationAccepted;
        public void Sanitize()
        {
            if (greeted == null) greeted = new List<string>();
            if (!Enum.IsDefined(typeof(FirstDayPhase), phase)) phase = FirstDayPhase.Live;
        }
    }

    [Serializable] public sealed class FirstDayLine { public string speaker, text; }
    [Serializable] public sealed class FirstDayConversation { public string id, actor; public FirstDayLine[] lines; }
    [Serializable] public sealed class FirstDayContent { public FirstDayConversation[] conversations; }
}
