using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Seabreeze
{
    // Only created during the explicit daytime Qingye acceptance run.
    public sealed class QingyeDayTakeRecorder : MonoBehaviour
    {
        string directory;
        StreamWriter timeline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartTake()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-qingyeMapQA");
            if (i < 0 || i + 1 >= args.Length || args[i + 1].StartsWith("-")) return;
            var root = new GameObject("Qingye daytime game-window take");
            DontDestroyOnLoad(root);
            root.AddComponent<QingyeDayTakeRecorder>().directory = Path.Combine(args[i + 1], "take");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);
            timeline = new StreamWriter(Path.Combine(directory, "frames.ndjson"), false) { AutoFlush = true };
            double startTime = Time.realtimeSinceStartupAsDouble;
            double next = startTime;
            int index = 0;
            var end = new WaitForEndOfFrame();
            while (Time.realtimeSinceStartupAsDouble - startTime < 200)
            {
                yield return end;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < next || Screen.width <= 8 || Screen.height <= 8) continue;
                next = now + 1d / 12;
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) continue;
                string name = (++index).ToString("D5") + ".jpg";
                try
                {
                    File.WriteAllBytes(Path.Combine(directory, name), frame.EncodeToJPG(84));
                    timeline.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{{\"t\":{0:F5},\"file\":\"{1}\",\"w\":{2},\"h\":{3}}}",
                        now - startTime, name, frame.width, frame.height));
                }
                finally { Destroy(frame); }
            }
        }

        void OnDestroy() { if (timeline != null) timeline.Dispose(); }
    }
}
