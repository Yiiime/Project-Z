using System;
using System.IO;
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
                    return Path.Combine(VerificationDirectory, "seabreeze-save.json");
                }
#endif
                return Path.Combine(Application.persistentDataPath, "seabreeze-save.json");
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
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
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
    }
}
