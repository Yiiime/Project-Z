using UnityEngine;

namespace Seabreeze
{
    // Design coordinates stay readable in pixels, independent of the world's canvas scaler.
    [DisallowMultipleComponent]
    public sealed class ModalViewportFit : MonoBehaviour
    {
        public float margin = 24;
        RectTransform rect;
        Canvas canvas;
        void Awake() { rect = (RectTransform)transform; canvas = GetComponentInParent<Canvas>(); }
        void OnEnable() { Fit(); }
        void LateUpdate() { Fit(); }
        public void Fit()
        {
            if (rect == null || canvas == null || rect.rect.width <= 0 || rect.rect.height <= 0) return;
            var parent = rect.parent as RectTransform;
            float pixels = Mathf.Max(.01f, canvas.scaleFactor);
            float width = parent != null ? parent.rect.width * pixels : Screen.width;
            float height = parent != null ? parent.rect.height * pixels : Screen.height;
            if (width < 1 || height < 1) return;
            float fit = Mathf.Min(1, Mathf.Min(Mathf.Max(1,width-margin*2)/rect.rect.width, Mathf.Max(1,height-margin*2)/rect.rect.height));
            rect.localScale = Vector3.one * (fit / pixels);
        }
    }
}
