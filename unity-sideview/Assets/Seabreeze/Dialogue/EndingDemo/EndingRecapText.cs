using System;
using System.IO;
using UnityEngine;

namespace Seabreeze.EndingDemo
{
    internal static class EndingRecapText
    {
        [Serializable]
        private class RecapFile
        {
            public string fingerprint;
            public EndingDemoView.RecapPage[] recapPages;
        }

        // Also read by the memory service. Older saves use the same reviewed copy offline.
        internal static EndingDemoView.RecapPage[] Approved(string fingerprint)
        {
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, "Ending", "approved-recap.json");
                var file = JsonUtility.FromJson<RecapFile>(File.ReadAllText(path));
                if (file != null && file.fingerprint == fingerprint && file.recapPages?.Length == 2)
                    return file.recapPages;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning("无法读取两页回忆稿：" + e.Message);
            }
            return Array.Empty<EndingDemoView.RecapPage>();
        }
    }
}
