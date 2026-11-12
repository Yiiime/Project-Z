using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seabreeze
{
    public static class SaveRepository
    {
#if UNITY_EDITOR
        public static string VerificationDirectory { get; set; }
#endif
        public static string PathOnDisk
        {
            get
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(VerificationDirectory))
                {
                    if (!Path.IsPathRooted(VerificationDirectory))
                        throw new InvalidOperationException("Verification saves require an absolute directory.");
                    return Path.Combine(VerificationDirectory, MemoryRuntimeConfig.Enabled ? "seven-memory-save.json" : "seabreeze-save.json");
                }
#endif
                string directory = Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
                if (string.IsNullOrEmpty(directory)) directory = Application.persistentDataPath;
                if (!Path.IsPathRooted(directory)) throw new InvalidOperationException("Save directory must be absolute.");
                return Path.Combine(directory, MemoryRuntimeConfig.Enabled ? "seven-memory-save.json" : "seabreeze-save.json");
            }
        }
        public static string LastError { get; private set; }
        public static SaveData Load()
        {
            LastError = "";
            var data = Read(PathOnDisk);
            if (data == null) data = Read(PathOnDisk + ".bak");
            return data ?? new SaveData();
        }
        static SaveData Read(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                if (!json.TrimStart().StartsWith("{")) throw new InvalidDataException("Invalid save JSON.");
                var result = JsonUtility.FromJson<SaveData>(json);
                if (result == null) return null;
                // JsonUtility creates inline class values even when an old save
                // omits them. Only an explicitly saved first day may start it.
                var firstDay = ParseObject(json)[nameof(SaveData.firstDay)];
                if (firstDay == null || firstDay.Type == JTokenType.Null)
                    result.firstDay = null;
                result.Sanitize();
                return result;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning("Seabreeze could not read save: " + e.Message);
                return null;
            }
        }
        public static bool Write(SaveData data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
                var temp = PathOnDisk + ".tmp";
                var json = JsonUtility.ToJson(data, true);
                if (data.firstDay == null)
                {
                    // Preserve the opt-in across subsequent saves: JsonUtility
                    // otherwise serializes null as a new opening-state object.
                    var payload = ParseObject(json);
                    payload.Remove(nameof(SaveData.firstDay));
                    json = payload.ToString(Formatting.Indented);
                }
                File.WriteAllText(temp, json);
                if (File.Exists(PathOnDisk))
                {
                    // File.Replace atomically swaps an existing save and preserves its predecessor.
                    File.Replace(temp, PathOnDisk, PathOnDisk + ".bak");
                }
                else File.Move(temp, PathOnDisk);
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning("Seabreeze save failed: " + e.Message);
                return false;
            }
        }
        static JObject ParseObject(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JObject.Load(reader);
            }
        }
    }
}
