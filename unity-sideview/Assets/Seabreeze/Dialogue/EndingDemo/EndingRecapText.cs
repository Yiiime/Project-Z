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
            public Variant[] variants;
        }

        [Serializable]
        private class Variant
        {
            public string fingerprint;
            public EndingDemoView.RecapPage[] recapPages;
        }

        // Also read by the memory service. Older saves use the same reviewed copy offline.
        internal static EndingDemoView.RecapPage[] Approved(params string[] fingerprints)
        {
            if (fingerprints == null || fingerprints.Length == 0)
                return Array.Empty<EndingDemoView.RecapPage>();
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, "Ending", "approved-recap.json");
                var file = JsonUtility.FromJson<RecapFile>(File.ReadAllText(path));
                if (file == null)
                    return Array.Empty<EndingDemoView.RecapPage>();
                foreach (var fingerprint in fingerprints)
                {
                    if (string.IsNullOrEmpty(fingerprint))
                        continue;
                    if (file.fingerprint == fingerprint && IsReviewedThreePageCopy(file.recapPages))
                        return file.recapPages;
                    if (file.variants == null)
                        continue;
                    foreach (var variant in file.variants)
                    {
                        if (variant != null && variant.fingerprint == fingerprint && IsReviewedThreePageCopy(variant.recapPages))
                            return variant.recapPages;
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning("无法读取三页回忆稿：" + e.Message);
            }
            return Array.Empty<EndingDemoView.RecapPage>();
        }

        static bool IsReviewedThreePageCopy(EndingDemoView.RecapPage[] pages)
        {
            if (pages == null || pages.Length != 3)
                return false;
            return HasRange(pages[0], 1, 1) && HasRange(pages[1], 2, 3) && HasRange(pages[2], 4, 7);
        }

        static bool HasRange(EndingDemoView.RecapPage page, int startDay, int endDay)
        {
            return page != null && page.startDay == startDay && page.endDay == endDay &&
                   !string.IsNullOrWhiteSpace(page.title) && !string.IsNullOrWhiteSpace(page.text);
        }
    }
}
