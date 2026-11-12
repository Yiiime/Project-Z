using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>
    /// Same-run low-volume recorder for -liuyangMapQA. Every sample writes a
    /// full game frame and a face/portrait crop from the same texture, with a
    /// timeline row that can be assembled into a video without re-recording.
    /// </summary>
    public sealed class LiuyangDayTakeRecorder : MonoBehaviour
    {
        const string QaArgument = "-liuyangMapQA";
        const int Fps = 12;
        const int MaxSeconds = 240;
        // Keep a long enough 12 fps take while leaving headroom on the
        // four-gigabyte recording machine. Face JPGs are cropped from the
        // same full-frame texture, so no second game render is recorded.
        const long MaxBytes = 320L * 1024L * 1024L;
        const long GuardFreeBytes = 256L * 1024L * 1024L;

        string directory;
        StreamWriter timeline;
        long writtenBytes;
        bool capturedTransparentPortrait;
        int index;
        Live2DCharacterView portrait;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, QaArgument);
            if (i < 0) return;
            string output = i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                ? Path.GetFullPath(args[i + 1])
                : Path.Combine(Application.temporaryCachePath, "liuyang-map-qa");
            var root = new GameObject("Liuyang daytime take recorder");
            DontDestroyOnLoad(root);
            root.AddComponent<LiuyangDayTakeRecorder>().directory = Path.Combine(output, "take");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);
            timeline = new StreamWriter(Path.Combine(directory, "frames.ndjson"), false) { AutoFlush = true };
            double started = Time.realtimeSinceStartupAsDouble;
            double next = started;
            var endOfFrame = new WaitForEndOfFrame();
            while (Time.realtimeSinceStartupAsDouble - started < MaxSeconds)
            {
                yield return endOfFrame;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < next || Screen.width <= 8 || Screen.height <= 8) continue;
                next = now + 1d / Fps;
                if (!CanWrite()) break;
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) continue;
                try
                {
                    portrait = FindPortrait();
                    string fullName = "full-" + index.ToString("D06") + ".jpg";
                    string faceName = "face-" + index.ToString("D06") + ".jpg";
                    byte[] full = frame.EncodeToJPG(78);
                    if (full == null || !CanWriteBytes(full.Length)) break;
                    File.WriteAllBytes(Path.Combine(directory, fullName), full);
                    writtenBytes += full.Length;
                    string facePath = "";
                    Texture2D face = CropPortrait(frame, portrait);
                    if (face != null)
                    {
                        byte[] faceBytes = face.EncodeToJPG(82);
                        if (faceBytes != null && CanWriteBytes(faceBytes.Length))
                        {
                            File.WriteAllBytes(Path.Combine(directory, faceName), faceBytes);
                            writtenBytes += faceBytes.Length;
                            facePath = faceName;
                        }
                        Destroy(face);
                    }
                    if (!capturedTransparentPortrait && index > 10 && portrait?.CurrentProfile?.characterId == "c01")
                        CaptureTransparentPortrait(portrait, fullName);
                    timeline.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{{\"t\":{0:F5},\"full\":\"{1}\",\"face\":\"{2}\",\"w\":{3},\"h\":{4},\"actor\":\"{5}\"}}",
                        now - started, fullName, facePath, frame.width, frame.height,
                        portrait?.CurrentProfile?.characterId ?? ""));
                    index++;
                }
                finally { Destroy(frame); }
            }
        }

        void CaptureTransparentPortrait(Live2DCharacterView portrait, string frameName)
        {
            var surface = portrait.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            if (surface == null || !(surface.texture is RenderTexture target) || !target.IsCreated()) return;
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(directory, "portrait-alpha-" + Path.GetFileNameWithoutExtension(frameName) + ".png"), texture.EncodeToPNG());
                capturedTransparentPortrait = true;
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(texture);
            }
        }

        bool CanWrite()
        {
            if (writtenBytes >= MaxBytes) return false;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(directory));
                return string.IsNullOrEmpty(root) || new DriveInfo(root).AvailableFreeSpace >= GuardFreeBytes;
            }
            catch { return true; }
        }

        bool CanWriteBytes(int bytes)
        {
            return bytes > 0 && writtenBytes + bytes <= MaxBytes;
        }

        Live2DCharacterView FindPortrait()
        {
            return FindObjectsByType<Live2DCharacterView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item != null && item.IsVisible);
        }

        Texture2D CropPortrait(Texture2D source, Live2DCharacterView view)
        {
            if (source == null || view == null) return null;
            Rect screenRect = FaceScreenRect(view);
            if (screenRect.width <= 1 || screenRect.height <= 1) return null;
            float sx = source.width / Mathf.Max(1f, Screen.width);
            float sy = source.height / Mathf.Max(1f, Screen.height);
            int x = Mathf.Clamp(Mathf.FloorToInt(screenRect.x * sx), 0, source.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(screenRect.y * sy), 0, source.height - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(screenRect.xMax * sx), x + 1, source.width);
            int top = Mathf.Clamp(Mathf.CeilToInt(screenRect.yMax * sy), y + 1, source.height);
            var crop = new Texture2D(right - x, top - y, TextureFormat.RGB24, false);
            crop.SetPixels(source.GetPixels(x, y, right - x, top - y));
            crop.Apply(false, false);
            return crop;
        }

        Rect FaceScreenRect(Live2DCharacterView view)
        {
            RectTransform viewport = view.GetComponent<RectTransform>();
            if (viewport == null) return new Rect();
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            Rect fallback = Rect.MinMaxRect(
                Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x),
                Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y),
                Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x),
                Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y));
            CubismModel model = view.CurrentModel;
            Camera camera = view.PortraitCamera;
            if (model == null || camera == null || model.Drawables == null) return fallback;
            bool found = false;
            Vector2 min = Vector2.one;
            Vector2 max = Vector2.zero;
            foreach (CubismDrawable drawable in model.Drawables)
            {
                if (drawable == null || !IsFaceDrawable(drawable.Id) || drawable.VertexPositions == null) continue;
                foreach (Vector3 vertex in drawable.VertexPositions)
                {
                    Vector3 point = camera.WorldToViewportPoint(drawable.transform.TransformPoint(vertex));
                    if (point.z <= 0 || float.IsNaN(point.x) || float.IsNaN(point.y)
                        || float.IsInfinity(point.x) || float.IsInfinity(point.y)) continue;
                    found = true;
                    min = Vector2.Min(min, new Vector2(point.x, point.y));
                    max = Vector2.Max(max, new Vector2(point.x, point.y));
                }
            }
            if (!found) return fallback;
            Vector2 pad = (max - min) * .32f;
            min = Vector2.Max(Vector2.zero, min - pad);
            max = Vector2.Min(Vector2.one, max + pad);
            return new Rect(
                fallback.x + min.x * fallback.width,
                fallback.y + min.y * fallback.height,
                Mathf.Max(1, (max.x - min.x) * fallback.width),
                Mathf.Max(1, (max.y - min.y) * fallback.height));
        }

        static bool IsFaceDrawable(string id)
        {
            string key = (id ?? "").ToLowerInvariant();
            return key.Contains("face") || key.Contains("eye") || key.Contains("brow")
                || key.Contains("mouth") || key.Contains("tear") || key.Contains("iris")
                || key.Contains("cheek") || key.Contains("sigh") || key.Contains("question");
        }

        void OnDestroy()
        {
            if (timeline != null) timeline.Dispose();
        }
    }
}
