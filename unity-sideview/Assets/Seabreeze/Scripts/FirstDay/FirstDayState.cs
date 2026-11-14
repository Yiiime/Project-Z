using System;
using System.Collections.Generic;

namespace Seabreeze
{
    // Persisted values 0-9 belong to the original release. Never reorder them.
    public enum FirstDayPhase { None, Live, Arrival, Harbor, WalkToSquare, MeetIslanders, Fireworks, Home, Bedtime, Complete, WelcomeTask, EveningReady, EveningLive }

    [Serializable]
    public sealed class FirstDayState
    {
        public int revision;
        public int stageVersion;
        public FirstDayPhase phase = FirstDayPhase.Live;
        public List<string> greeted = new List<string>();
        public List<string> taskPhotoIds = new List<string>();
        public bool introSeen, arrivalFilmSeen;
        public string preparedIntroduction = "";
        public bool invitationAccepted;
        public bool announced, openingStarted, harborOpened, harborReady;
        public string openingSession = "", eveningSession = "", taskId = "", agentRequest = "";
        public string objective = "", invitationTitle = "", invitationDescription = "";
        public string escortPlace = "Harbor";
        public float escortX = -7.8f;
        public static FirstDayState NewJourney() => new FirstDayState { revision = 3 };
        public void Sanitize()
        {
            if (revision < 3) { introSeen = true; arrivalFilmSeen = true; }
            if (greeted == null) greeted = new List<string>();
            if (taskPhotoIds == null) taskPhotoIds = new List<string>();
            if (!Enum.IsDefined(typeof(FirstDayPhase), phase)) phase = FirstDayPhase.Live;
            greeted.RemoveAll(id => id == "liyuan");
            greeted = new List<string>(new HashSet<string>(greeted));
            if (revision < 2)
            {
                // Keep completed/legacy free-play saves out of the new onboarding.
                if (phase == FirstDayPhase.Home || phase == FirstDayPhase.Bedtime) phase = FirstDayPhase.WelcomeTask;
                revision = 2;
            }
            revision = 3;
        }
    }

    [Serializable] public sealed class FirstDayLine { public string speaker, text; }
    [Serializable] public sealed class FirstDayConversation { public string id, actor; public FirstDayLine[] lines; }
    [Serializable] public sealed class FirstDayContent { public FirstDayConversation[] conversations; }
}
