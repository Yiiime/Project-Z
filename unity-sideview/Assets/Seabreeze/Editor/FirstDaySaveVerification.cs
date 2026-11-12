using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Seabreeze.Editor
{
    public static class FirstDaySaveVerification
    {
        [MenuItem("Seabreeze/First Day/Verify legacy save compatibility")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run save verification from Edit Mode.");
            string previous = SaveRepository.VerificationDirectory;
            string directory = Path.Combine(Path.GetTempPath(), "project-z-firstday-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                SaveRepository.VerificationDirectory = directory;
                var old = new SaveData { started = true, day = 2, period = 1, place = "Street", playerX = 3.25f };
                old.journal.Add("旧旅程中的记录");
                var json = JObject.Parse(JsonUtility.ToJson(old));
                json.Remove("firstDay");
                File.WriteAllText(SaveRepository.PathOnDisk, json.ToString());

                var loaded = SaveRepository.Load();
                Require(loaded.firstDay == null && loaded.day == 2 && loaded.place == "Street", "Legacy save acquired a first day.");
                Require(SaveRepository.Write(loaded), "Legacy save could not be written.");
                Require(JObject.Parse(File.ReadAllText(SaveRepository.PathOnDisk))["firstDay"] == null, "Legacy save wrote an opening-state object.");
                loaded = SaveRepository.Load();
                Require(loaded.firstDay == null && loaded.playerX == 3.25f && loaded.journal.Contains("旧旅程中的记录"), "Legacy round-trip changed progress.");

                json["firstDay"] = JValue.CreateNull();
                File.WriteAllText(SaveRepository.PathOnDisk, json.ToString());
                Require(SaveRepository.Load().firstDay == null, "Explicit null acquired a first day.");

                loaded.firstDay = new FirstDayState { phase = FirstDayPhase.WalkToSquare, invitationAccepted = true };
                loaded.firstDay.greeted.Add("z");
                Require(SaveRepository.Write(loaded), "First-day checkpoint could not be written.");
                loaded = SaveRepository.Load();
                Require(loaded.firstDay.phase == FirstDayPhase.WalkToSquare && loaded.firstDay.invitationAccepted && loaded.firstDay.greeted.Contains("z"), "Authored checkpoint lost progress.");

                loaded.firstDay.phase = FirstDayPhase.Complete;
                Require(SaveRepository.Write(loaded) && SaveRepository.Load().firstDay.phase == FirstDayPhase.Complete, "Completed first day restarted.");
                Debug.Log("FIRST_DAY_SAVE_VERIFICATION_PASSED: missing field, legacy write/read, explicit null, active checkpoint, completed checkpoint.");
            }
            finally
            {
                SaveRepository.VerificationDirectory = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
