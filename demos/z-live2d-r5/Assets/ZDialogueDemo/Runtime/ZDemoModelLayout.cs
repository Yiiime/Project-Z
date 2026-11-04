using System.Collections;
using UnityEngine;

namespace ZDialogueDemo
{
    /// <summary>
    /// 在运行时按实际 Renderer.bounds 把 Z 放入相机左侧区域。
    /// 编辑器导入时 Cubism 网格的 bounds 可能还未就绪，所以首帧不做布局。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZDemoModelLayout : MonoBehaviour
    {
        private const float LeftRegionFraction = 0.35f;
        private const float TargetWidthFraction = 0.88f;
        private const float TargetHeightFraction = 0.86f;

        private int lastScreenWidth;
        private int lastScreenHeight;
        private bool startupFitFinished;

        public bool HasFitted { get; private set; }
        public Vector3 LastAppliedScale { get; private set; } = Vector3.one;
        public Bounds LastBounds { get; private set; }

        private void Start()
        {
            StartCoroutine(FitAfterCubismStartup());
        }

        private IEnumerator FitAfterCubismStartup()
        {
            // Cubism 的运行时网格和 Renderer.bounds 要到模型初始化后才可靠。
            yield return null;
            yield return null;
            startupFitFinished = true;
            FitNow();
        }

        private void Update()
        {
            if (!startupFitFinished)
                return;

            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
                return;

            FitNow();
        }

        /// <summary>
        /// 重新按当前分辨率布局。每次都会先恢复基准 scale/position，避免累计缩放和位移。
        /// </summary>
        public bool FitNow()
        {
            var camera = Camera.main;
            if (camera == null)
                return false;

            if (Screen.width <= 0 || Screen.height <= 0)
                return false;

            camera.ResetAspect();

            // 场景构建器可能保存了编辑器阶段的临时偏移；运行时始终从基准状态计算。
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;

            if (!TryGetRendererBounds(out var bounds) || bounds.size.x <= 0.0001f || bounds.size.y <= 0.0001f)
            {
                HasFitted = false;
                return false;
            }

            var visibleHeight = camera.orthographicSize * 2f;
            var visibleWidth = visibleHeight * camera.aspect;
            var leftWidth = visibleWidth * LeftRegionFraction;
            var targetWidth = leftWidth * TargetWidthFraction;
            var targetHeight = visibleHeight * TargetHeightFraction;
            var scale = Mathf.Min(targetWidth / bounds.size.x, targetHeight / bounds.size.y);
            scale = Mathf.Clamp(scale, 0.01f, 100f);

            transform.localScale = Vector3.one * scale;
            if (!TryGetRendererBounds(out bounds))
            {
                HasFitted = false;
                transform.localScale = Vector3.one;
                return false;
            }

            var targetCenter = new Vector3(
                camera.transform.position.x + visibleWidth * (-0.5f + LeftRegionFraction * 0.5f),
                camera.transform.position.y,
                bounds.center.z);
            transform.position += targetCenter - bounds.center;
            TryGetRendererBounds(out bounds);

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            LastAppliedScale = transform.localScale;
            LastBounds = bounds;
            HasFitted = true;
            return true;
        }

        private bool TryGetRendererBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            bounds = default;
            var found = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }
    }
}
