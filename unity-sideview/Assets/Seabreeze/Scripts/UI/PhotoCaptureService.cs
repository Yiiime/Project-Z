using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Captures the actual displayed frame, including the current conversation or phone.</summary>
    public sealed class PhotoCaptureService : MonoBehaviour
    {
        GameSession session;
        bool capturing;
        float nextCapture;
        public bool IsCapturing => capturing;

        public void Initialize(GameSession owner) { session = owner; }

        void Update()
        {
            if (GameInput.Pressed(Key.P) && !TypingInField()) Capture();
        }

        static bool TypingInField()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null)
            {
                var text = selected.GetComponentInParent<TMP_InputField>();
                if (text != null && text.isFocused) return true;
                var legacy = selected.GetComponentInParent<InputField>();
                if (legacy != null && legacy.isFocused) return true;
            }
            // The native live room uses UI Toolkit rather than an EventSystem input field.
            // Inspect documents only on the hotkey press, never as a per-frame scene search.
            foreach (var document in FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var focused = document.rootVisualElement?.focusController?.focusedElement as UnityEngine.UIElements.VisualElement;
                for (var element = focused; element != null; element = element.parent)
                    if (element is UnityEngine.UIElements.TextField) return true;
            }
            return false;
        }

        public bool Capture()
        {
            if (session == null || session.State == null || session.IsAtTitle || capturing || Time.unscaledTime < nextCapture)
                return false;
            capturing = true;
            nextCapture = Time.unscaledTime + .8f;
            StartCoroutine(CaptureFrame(session.State));
            return true;
        }

        IEnumerator CaptureFrame(SaveData owner)
        {
            // Works while an overlay has paused scaled game time. No flash/toast contaminates the photo.
            yield return new WaitForEndOfFrame();
            Texture2D frame = null;
            Texture2D thumbnail = null;
            string path = null, thumbPath = null;
            bool recorded = false;
            try
            {
                if (session == null || session.State != owner || session.IsAtTitle) yield break;
                if (owner.phone == null) owner.phone = new PhoneSaveState();
                owner.phone.Sanitize();
                if (!Guid.TryParseExact(owner.phone.albumId, "N", out _)) owner.phone.albumId = Guid.NewGuid().ToString("N");
                string id = "photo_" + Guid.NewGuid().ToString("N");
                string name = id + ".png", thumbName = id + "_thumb.png";
                path = ResolvePath(owner.phone, name);
                thumbPath = ResolvePath(owner.phone, thumbName);
                frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null || frame.width < 1 || frame.height < 1) throw new IOException("The game frame is not ready.");
                thumbnail = MakeThumbnail(frame);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                WriteAtomically(path, frame.EncodeToPNG());
                WriteAtomically(thumbPath, thumbnail.EncodeToPNG());
                owner.phone.photos.Add(new PhoneSavedPhoto
                {
                    id = id, fileName = name, thumbnailFileName = thumbName,
                    title = GameUI.PlaceName(session.CurrentPlace) + " · 留住这一刻",
                    place = owner.place, day = owner.day, period = owner.period,
                    capturedUtc = DateTime.UtcNow.ToString("o"), width = frame.width, height = frame.height
                });
                recorded = true;
                bool saved = session.Save();
                session.NotifyChanged();
                session.Story?.Notify(saved ? "咔嚓。照片已放进手机相册。" : "照片已拍下；存档暂未写入，请稍后手动保存。" );
            }
            catch (Exception exception)
            {
                if (!recorded) { TryDelete(path); TryDelete(thumbPath); }
                Debug.LogWarning("Photo capture failed: " + exception.Message);
                session?.Story?.Notify("这张照片暂时没能保存，请稍后再试。");
            }
            finally
            {
                if (frame != null) Destroy(frame);
                if (thumbnail != null) Destroy(thumbnail);
                capturing = false;
            }
        }

        public static string ResolvePath(PhoneSaveState phone, string fileName)
        {
            if (phone == null || !Guid.TryParseExact(phone.albumId, "N", out _) || string.IsNullOrEmpty(fileName)
                || fileName != Path.GetFileName(fileName) || fileName.Contains("/") || fileName.Contains("\\")
                || !fileName.StartsWith("photo_", StringComparison.Ordinal) || !fileName.EndsWith(".png", StringComparison.Ordinal)) return "";
            return Path.Combine(Path.GetDirectoryName(SaveRepository.PathOnDisk), "Photos", phone.albumId, fileName);
        }

        static Texture2D MakeThumbnail(Texture source)
        {
            int width = Mathf.Min(512, source.width);
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * (width / (float)source.width)));
            // ScreenCapture's texture already contains display-ready pixels. A default
            // sRGB render target in a Linear project would encode those values a second
            // time, making only the resized thumbnail look washed out.
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply(false, false);
                return result;
            }
            finally { GL.sRGBWrite = previousSrgbWrite; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        static void WriteAtomically(string path, byte[] bytes)
        {
            string temporary = path + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path); }
            finally { TryDelete(temporary); }
        }
        static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        void OnDisable() { StopAllCoroutines(); capturing = false; }
    }
}
