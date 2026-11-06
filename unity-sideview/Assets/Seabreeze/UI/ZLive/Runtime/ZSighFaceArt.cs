using System;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace ZLive.ModelPerformance
{
    /// <summary>
    /// Runtime-only Sigh art. The B eye and mouth PNG stays on the model's
    /// original Cubism meshes; only a per-renderer material clone is swapped
    /// in while Sigh is active. Cubism continues to own vertex positions,
    /// opacity and sorting, so the feature cannot drift away from the face.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1250)]
    public sealed class ZSighFaceArt : MonoBehaviour
    {
        private const string ObjectName = "Z Sigh Face Art";
        private const string FeatureTextureResource = "ZSighBFeatures";
        private const string ShaderResource = "ZSighFace";
        private const string ShaderName = "ZDialogueDemo/SighFace";
        private const string WeightProperty = "_SighWeight";
        private const int FeatureTextureSize = 1254;
        private const float FadeSpeed = 12f;
        private const float ClearThreshold = 0.002f;

        // The asset uses top-left pixel coordinates. Four pixels of transparent
        // padding keep bilinear sampling away from the neighboring feature.
        private static readonly FeatureRect LeftEyeRect = new FeatureRect(171, 287, 330, 117);
        private static readonly FeatureRect RightEyeRect = new FeatureRect(755, 289, 328, 116);
        private static readonly FeatureRect MouthRect = new FeatureRect(414, 843, 425, 204);

        private ZPerformanceController controller;
        private Texture2D featureTexture;
        private Shader sighShader;
        private SighBinding leftEye;
        private SighBinding rightEye;
        private SighBinding mouth;
        private bool prepared;
        private bool materialsRestored = true;
        private bool warnedMissingTexture;
        private bool warnedMissingShader;
        private bool warnedMissingDrawable;
        private float visibleIntensity;
        private float targetIntensity;

        /// <summary>所有三块原始 Drawable 与资源均已准备好。</summary>
        public bool IsSighReady => prepared && IsBindingReady(leftEye) &&
                                    IsBindingReady(rightEye) && IsBindingReady(mouth);

        /// <summary>Sigh 图层已进入可见淡入状态。</summary>
        public bool IsSighVisible => visibleIntensity > ClearThreshold &&
                                     SighVisibleInstanceCount > 0;

        /// <summary>三块 Sigh 图层的实际淡入强度。</summary>
        public float SighVisibleIntensity => visibleIntensity;

        /// <summary>当前仍挂着 Sigh 材质 clone 的原始 Drawable 数量。</summary>
        public int SighVisibleInstanceCount
        {
            get
            {
                var count = 0;
                if (IsActiveBinding(leftEye)) ++count;
                if (IsActiveBinding(rightEye)) ++count;
                if (IsActiveBinding(mouth)) ++count;
                return count;
            }
        }

        /// <summary>停止、打断、自然结束或禁用后是否已恢复全部原材质。</summary>
        public bool MaterialsRestored => materialsRestored && SighVisibleInstanceCount == 0;



        private void Awake()
        {
            ResolveController();
            LoadResources();
        }

        private void OnEnable()
        {
            ResolveController();
            LoadResources();
        }

        private void OnDisable()
        {
            RestoreMaterials();
            visibleIntensity = 0f;
            targetIntensity = 0f;
        }

        private void OnDestroy()
        {
            RestoreMaterials();
        }

        private void LateUpdate()
        {
            ResolveController();
            if (controller != null && controller.Paused) return;
            LoadResources();
            if (controller == null || !TryPrepareBindings())
            {
                targetIntensity = 0f;
                visibleIntensity = 0f;
                RestoreMaterials();
                return;
            }

            var sighActive = controller.SighFaceActive;
            if (!sighActive)
            {
                // The controller has already returned Cubism parameters to
                // neutral. Do not retain a fading material on a model whose
                // original opacity/mesh is active again.
                targetIntensity = 0f;
                visibleIntensity = 0f;
                RestoreMaterials();
                return;
            }

            targetIntensity = sighActive ? 1f : 0f;
            ActivateMaterials();

            visibleIntensity = Mathf.MoveTowards(
                visibleIntensity,
                targetIntensity,
                Time.unscaledDeltaTime * FadeSpeed);

            if (IsSighReady && SighVisibleInstanceCount > 0)
            {
                // Core controls eye Drawable opacity during the hand-off. The
                // art itself is fully swapped on the first Sigh frame so the
                // old and new closed-eye lines never cross-fade into a double
                // eyelid. Mouth keeps the controller's authored entry/hold/
                // exit curve exactly.
                SetWeight(leftEye, Mathf.Clamp01(controller.SighEyeArtWeight));
                SetWeight(rightEye, Mathf.Clamp01(controller.SighEyeArtWeight));
                SetWeight(mouth, Mathf.Clamp01(controller.SighMouthArtWeight));
            }

            if (!sighActive && visibleIntensity <= ClearThreshold)
                RestoreMaterials();
        }

        private void ResolveController()
        {
            if (controller == null)
                controller = GetComponentInParent<ZPerformanceController>();
        }

        private void LoadResources()
        {
            if (featureTexture == null)
                featureTexture = Resources.Load<Texture2D>(FeatureTextureResource);
            if (featureTexture == null && !warnedMissingTexture)
            {
                warnedMissingTexture = true;
                Debug.LogWarning("[ZSighFaceArt] 找不到 Resources/" + FeatureTextureResource + ".png。", this);
            }

            if (sighShader == null)
            {
                sighShader = Shader.Find(ShaderName);
                if (sighShader == null)
                    sighShader = Resources.Load<Shader>(ShaderResource);
            }
            if (sighShader == null && !warnedMissingShader)
            {
                warnedMissingShader = true;
                Debug.LogWarning("[ZSighFaceArt] 找不到 " + ShaderName + " shader。", this);
            }
        }

        private bool TryPrepareBindings()
        {
            if (featureTexture == null || sighShader == null || controller == null)
                return false;

            var model = controller.CubismModel;
            if (model == null || model.Drawables == null || model.Drawables.Length == 0)
                return false;

            var left = FindDrawable(model.Drawables, "Eye_Closed_L");
            var right = FindDrawable(model.Drawables, "Eye_Closed_R");
            // The imported model uses Mouth_Closed. Mouth_Main is accepted as
            // a forward-compatible alias without changing this model asset.
            var mainMouth = FindDrawable(model.Drawables, "Mouth_Main");
            if (mainMouth == null)
                mainMouth = FindDrawable(model.Drawables, "Mouth_Closed");

            var valid = TryCaptureBinding(ref leftEye, left, LeftEyeRect, "Sigh Eye L", false) &&
                        TryCaptureBinding(ref rightEye, right, RightEyeRect, "Sigh Eye R", false) &&
                        TryCaptureBinding(ref mouth, mainMouth, MouthRect, "Sigh Mouth", true);
            if (!valid && !warnedMissingDrawable)
            {
                warnedMissingDrawable = true;
                Debug.LogWarning(
                    "[ZSighFaceArt] 需要 Eye_Closed_L、Eye_Closed_R、Mouth_Closed Drawable。",
                    this);
            }

            prepared = valid;
            return valid;
        }

        private bool TryCaptureBinding(
            ref SighBinding binding,
            CubismDrawable drawable,
            FeatureRect featureRect,
            string bindingName,
            bool stretchVertical)
        {
            if (drawable == null)
                return false;

            var meshRenderer = drawable.GetComponent<MeshRenderer>();
            var cubismRenderer = drawable.GetComponent<CubismRenderer>();
            if (meshRenderer == null && cubismRenderer == null)
                return false;

            if (binding != null && binding.Drawable == drawable && binding.MeshRenderer == meshRenderer &&
                binding.CubismRenderer == cubismRenderer &&
                (binding.OriginalMaterial != null || binding.OriginalDrawMaterial != null))
                return true;

            // R5's URP command-buffer path draws with DrawMaterial when one is
            // supplied. Keep both sources so the overlay works with the stock
            // fallback (MeshRenderer material) and with a custom render pass.
            var originalMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : null;
            var originalDrawMaterial = cubismRenderer != null ? cubismRenderer.DrawMaterial : null;
            if (originalMaterial == null && originalDrawMaterial == null)
                return false;

            var originalTexture = cubismRenderer != null ? cubismRenderer.MainTexture : null;
            if (originalTexture == null)
            {
                var textureMaterial = originalDrawMaterial ?? originalMaterial;
                if (textureMaterial != null)
                    originalTexture = textureMaterial.mainTexture as Texture2D;
            }
            if (originalTexture == null)
                return false;

            Vector2 uvMin;
            Vector2 uvMax;
            if (!TryGetUvBounds(drawable, out uvMin, out uvMax))
                return false;

            if (binding != null)
                binding.DisposeRuntimeMaterial();

            binding = new SighBinding
            {
                Drawable = drawable,
                MeshRenderer = meshRenderer,
                CubismRenderer = cubismRenderer,
                OriginalMaterial = originalMaterial,
                OriginalDrawMaterial = originalDrawMaterial,
                OriginalTexture = originalTexture,
                OriginalUvRect = new Vector4(uvMin.x, uvMin.y, uvMax.x, uvMax.y),
                SighUvRect = featureRect.ToUvRect(),
                Name = bindingName,
                StretchVertical = stretchVertical,
            };
            return true;
        }

        private void ActivateMaterials()
        {
            if (!IsSighReady)
                return;

            var activated = ActivateBinding(leftEye) &&
                            ActivateBinding(rightEye) &&
                            ActivateBinding(mouth);
            materialsRestored = !activated;
        }

        private bool ActivateBinding(SighBinding binding)
        {
            if (binding == null || (binding.MeshRenderer == null && binding.CubismRenderer == null) ||
                (binding.OriginalMaterial == null && binding.OriginalDrawMaterial == null))
                return false;

            if (binding.RuntimeMaterial == null)
            {
                var sourceMaterial = binding.OriginalDrawMaterial ?? binding.OriginalMaterial;
                var material = new Material(sourceMaterial)
                {
                    name = binding.Name + " (Runtime Sigh)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                material.shader = sighShader;
                material.SetTexture("_MainTex", binding.OriginalTexture);
                material.SetTexture("_SighTex", featureTexture);
                material.SetVector("_OriginalUvRect", binding.OriginalUvRect);
                material.SetVector("_SighUvRect", binding.SighUvRect);
                material.SetFloat("_SighVerticalScale", binding.StretchVertical ? 1.55f : 1f);
                material.SetFloat(WeightProperty, 0f);
                binding.RuntimeMaterial = material;
            }

            // Reassert both slots on every active frame. The R5 URP pass uses
            // CubismRenderer.DrawMaterial for CommandBuffer.DrawMesh; writing
            // only MeshRenderer.sharedMaterial can therefore leave the Sigh
            // clone invisible. This also repairs an external pass that swaps
            // DrawMaterial between frames.
            if (binding.MeshRenderer != null && binding.MeshRenderer.sharedMaterial != binding.RuntimeMaterial)
                binding.MeshRenderer.sharedMaterial = binding.RuntimeMaterial;
            if (binding.CubismRenderer != null && binding.CubismRenderer.DrawMaterial != binding.RuntimeMaterial)
                binding.CubismRenderer.DrawMaterial = binding.RuntimeMaterial;

            return IsActiveBinding(binding);
        }

        private static void SetWeight(SighBinding binding, float weight)
        {
            if (binding == null || binding.RuntimeMaterial == null)
                return;
            if (binding.StretchVertical)
                UpdateVerticalCenter(binding);
            binding.RuntimeMaterial.SetFloat(WeightProperty, Mathf.Clamp01(weight));
        }

        private static void UpdateVerticalCenter(SighBinding binding)
        {
            Vector3[] vertices;
            try
            {
                vertices = binding.Drawable.VertexPositions;
            }
            catch (Exception)
            {
                return;
            }

            if (vertices == null || vertices.Length == 0)
                return;

            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            for (var i = 0; i < vertices.Length; i++)
            {
                minY = Mathf.Min(minY, vertices[i].y);
                maxY = Mathf.Max(maxY, vertices[i].y);
            }
            if (float.IsInfinity(minY) || float.IsInfinity(maxY))
                return;
            binding.RuntimeMaterial.SetFloat("_SighVerticalCenter", (minY + maxY) * 0.5f);
        }

        private void RestoreMaterials()
        {
            RestoreBinding(leftEye);
            RestoreBinding(rightEye);
            RestoreBinding(mouth);
            materialsRestored = true;
        }

        private static void RestoreBinding(SighBinding binding)
        {
            if (binding == null)
                return;

            if (binding.RuntimeMaterial != null)
            {
                if (binding.MeshRenderer != null && binding.MeshRenderer.sharedMaterial == binding.RuntimeMaterial)
                    binding.MeshRenderer.sharedMaterial = binding.OriginalMaterial;
                if (binding.CubismRenderer != null && binding.CubismRenderer.DrawMaterial == binding.RuntimeMaterial)
                    binding.CubismRenderer.DrawMaterial = binding.OriginalDrawMaterial;
            }
            binding.DisposeRuntimeMaterial();
        }

        private static bool IsBindingReady(SighBinding binding)
        {
            return binding != null && binding.Drawable != null &&
                   (binding.MeshRenderer != null || binding.CubismRenderer != null) &&
                   (binding.OriginalMaterial != null || binding.OriginalDrawMaterial != null) &&
                   binding.OriginalTexture != null;
        }

        private static bool IsActiveBinding(SighBinding binding)
        {
            if (binding == null || binding.RuntimeMaterial == null)
                return false;

            // R5's renderer owns the command-buffer material. When it exists,
            // only DrawMaterial proves that the custom pass will draw the
            // Sigh clone; a shared-material match alone is not sufficient.
            if (binding.CubismRenderer != null)
                return binding.CubismRenderer.DrawMaterial == binding.RuntimeMaterial;

            return binding.MeshRenderer != null &&
                   binding.MeshRenderer.sharedMaterial == binding.RuntimeMaterial;
        }

        private static bool TryGetUvBounds(
            CubismDrawable drawable,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = Vector2.zero;
            maximum = Vector2.zero;
            Vector2[] uvs;
            try
            {
                uvs = drawable.VertexUvs;
            }
            catch (Exception)
            {
                return false;
            }

            if (uvs == null || uvs.Length == 0)
                return false;

            var minX = float.PositiveInfinity;
            var minY = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var maxY = float.NegativeInfinity;
            for (var i = 0; i < uvs.Length; i++)
            {
                minX = Mathf.Min(minX, uvs[i].x);
                minY = Mathf.Min(minY, uvs[i].y);
                maxX = Mathf.Max(maxX, uvs[i].x);
                maxY = Mathf.Max(maxY, uvs[i].y);
            }

            if (float.IsInfinity(minX) || maxX - minX < 0.000001f || maxY - minY < 0.000001f)
                return false;

            minimum = new Vector2(minX, minY);
            maximum = new Vector2(maxX, maxY);
            return true;
        }

        private static CubismDrawable FindDrawable(CubismDrawable[] drawables, string wantedId)
        {
            if (drawables == null || string.IsNullOrEmpty(wantedId))
                return null;

            for (var i = 0; i < drawables.Length; i++)
            {
                var drawable = drawables[i];
                if (drawable == null)
                    continue;
                try
                {
                    if (string.Equals(drawable.Id, wantedId, StringComparison.OrdinalIgnoreCase))
                        return drawable;
                }
                catch (Exception)
                {
                    // The unmanaged model may still be initializing; retry next frame.
                }
            }

            return null;
        }

        private struct FeatureRect
        {
            private readonly int x;
            private readonly int y;
            private readonly int width;
            private readonly int height;

            public FeatureRect(int x, int y, int width, int height)
            {
                this.x = x;
                this.y = y;
                this.width = width;
                this.height = height;
            }

            public Vector4 ToUvRect()
            {
                var scale = 1f / FeatureTextureSize;
                return new Vector4(
                    x * scale,
                    (FeatureTextureSize - y - height) * scale,
                    (x + width) * scale,
                    (FeatureTextureSize - y) * scale);
            }
        }

        private sealed class SighBinding
        {
            public CubismDrawable Drawable;
            public MeshRenderer MeshRenderer;
            public CubismRenderer CubismRenderer;
            public Material OriginalMaterial;
            public Material OriginalDrawMaterial;
            public Texture2D OriginalTexture;
            public Vector4 OriginalUvRect;
            public Vector4 SighUvRect;
            public string Name;
            public bool StretchVertical;
            public Material RuntimeMaterial;

            public void DisposeRuntimeMaterial()
            {
                if (RuntimeMaterial == null)
                    return;

                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(RuntimeMaterial);
                else
                    UnityEngine.Object.DestroyImmediate(RuntimeMaterial);
                RuntimeMaterial = null;
            }
        }
    }
}
