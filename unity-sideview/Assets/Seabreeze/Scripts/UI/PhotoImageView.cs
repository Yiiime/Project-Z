using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Owns a decoded album image for the lifetime of a single UI view.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class PhotoImageView : MonoBehaviour
    {
        Texture2D texture;
        Sprite sprite;

        public bool Load(string path)
        {
            Release();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                // A damaged/external file should not allocate an unbounded image in the phone.
                if (new FileInfo(path).Length > 40 * 1024 * 1024) return false;
                byte[] bytes = File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes, true)) { Release(); return false; }
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f);
                var image = GetComponent<Image>();
                image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false; image.color = Color.white;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Album image could not be loaded: " + exception.Message);
                Release(); return false;
            }
        }

        void Release()
        {
            var image = GetComponent<Image>();
            if (image != null) image.sprite = null;
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
            sprite = null; texture = null;
        }
        void OnDestroy() { Release(); }
    }
}
