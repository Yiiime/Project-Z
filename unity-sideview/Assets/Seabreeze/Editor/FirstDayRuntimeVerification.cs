#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Seabreeze.Editor
{
    public static class FirstDayRuntimeVerification
    {
        [MenuItem("Seabreeze/First Day/Verify onboarding and daily cycle")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run verification outside the user's live game.");
            string previous = SaveRepository.VerificationDirectory;
            string directory = Path.Combine(Path.GetTempPath(), "z-day01-verification-" + Guid.NewGuid().ToString("N"));
            GameObject root = null; int checks = 0;
            void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; }
            try
            {
                SaveRepository.VerificationDirectory = directory;
                root = new GameObject("Isolated first-day verification");
                var session = root.AddComponent<GameSession>(); session.Initialize(); session.Dialogue.Cancel();
                var firstDay = root.AddComponent<FirstDayDirector>(); firstDay.Initialize(session, null);
                typeof(GameSession).GetProperty(nameof(GameSession.FirstDay)).SetValue(session, firstDay);
                session.State = new SaveData { day = 1, playerName = "小海", firstDay = FirstDayState.NewJourney() };
                session.Clock.Refresh();
                string opening = Guid.NewGuid().ToString();
                Require(!session.Clock.CompleteLiveSession(opening) && session.State.day == 1, "Opening room must not change the day");
                session.Clock.Advance();
                Require(session.State.restActions == 0, "Onboarding must not spend ordinary rest slots");
                session.State.firstDay.phase = FirstDayPhase.EveningLive;
                string firstEvening = Guid.NewGuid().ToString();
                Require(session.Clock.CompleteLiveSession(firstEvening), "First evening completion must be accepted");
                Require(session.State.day == 2 && session.State.restActions == 0 && session.State.firstDay.phase == FirstDayPhase.Complete, "First evening hands off to day 2 with four unused slots");
                Require(!session.Clock.CompleteLiveSession(firstEvening) && session.State.day == 2, "Duplicate room completion must not advance twice");
                Require(!session.Clock.CompleteLiveSession(Guid.NewGuid().ToString()), "A daily room cannot finish before the four rests");
                for (int rest = 1; rest <= 4; rest++)
                {
                    session.Clock.Advance();
                    Require(session.State.day == 2 && session.Clock.RestActions == rest && session.Clock.LiveReady == (rest == 4), "Rest " + rest + " keeps the day and controls live eligibility");
                }
                session.Clock.Advance(); Require(session.Clock.RestActions == 4 && session.State.day == 2, "Additional rest does not bypass the room");
                Require(session.Save(), "Save completed rest slots");
                var loaded = SaveRepository.Load();
                Require(loaded.day == 2 && loaded.restActions == 4 && loaded.playerName == "小海" && loaded.firstDay.phase == FirstDayPhase.Complete, "Reload preserves identity and daily handoff");
                session.State = loaded;
                Require(session.Clock.CompleteLiveSession(Guid.NewGuid().ToString()) && session.State.day == 3 && session.Clock.RestActions == 0, "Daily completion resets slots on day 3");
                session.State.firstDay = FirstDayState.NewJourney(); session.State.firstDay.phase = FirstDayPhase.MeetIslanders;
                session.State.place = nameof(PlaceId.Square);
                Require(!firstDay.CanTravel(PlaceId.Street), "Incomplete introductions keep the square boundary");
                session.State.firstDay.greeted.Add("z");
                session.State.firstDay.greeted.Add("z"); session.State.firstDay.Sanitize();
                Require(!firstDay.CanTravel(PlaceId.Street), "Duplicate greeting cannot advance progress");
                session.State.firstDay.greeted.Add("linxu");
                Require(!firstDay.CanTravel(PlaceId.Street), "Two different greetings are insufficient");
                session.State.firstDay.greeted.Add("liting");
                Require(firstDay.CanTravel(PlaceId.Street), "Three different introductions release the boundary");
                Require(PlayerIdentity.Normalize(" <小海>\n") == "小海", "Player name does not inject rich text");
                File.WriteAllText(Path.Combine(directory, "result.json"), "{\"passed\":true,\"checks\":" + checks + "}");
                Debug.Log("FIRST_DAY_RUNTIME_VERIFICATION_PASSED checks=" + checks + " evidence=" + directory);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                SaveRepository.VerificationDirectory = previous;
            }
        }
    }
}
#endif
