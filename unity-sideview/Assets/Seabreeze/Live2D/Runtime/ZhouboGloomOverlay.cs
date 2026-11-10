using System;
using System.Collections.Generic;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>
    /// Gloom expression for the ordinary ZhouboDaily portrait.
    ///
    /// The approved gradient is baked offline into a copy of ZhouboDaily's
    /// complete 4096 atlas. At runtime the atlas is switched only on the
    /// named skin/face/foreground drawables. Metal glasses and back hair keep
    /// their original texture, so the normal Cubism draw order remains intact.
    /// The renderer bindings and original textures are cached at configuration;
    /// the update loop performs no mesh or pixel-array allocation.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1200)]
    public sealed class ZhouboGloomOverlay : MonoBehaviour
    {
        const string FaceDrawableName = "Face_Skin";
        const string StreamingTextureRelativePath = "ZhouboRevisions/gloom.png";
        const string ResourcesTexturePath = "Memory/Portraits/ZhouboGloomDailyTexture";
        const float GloomMouthForm = -0.82f;

        CubismModel model;
        CubismParameter mouthForm;
        RendererBinding[] gloomRenderers = new RendererBinding[0];
        Texture2D gloomFaceTexture;
        float defaultMouthForm;
        string emotion;
        bool configured;
        bool mouthFormWritten;
        bool textureLoadAttempted;
        bool ownsGloomTexture;

        public static bool IsGloomExpression(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            value = value.Trim();
            return string.Equals(value, "gloom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "black", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "blackened", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "gloom_face", StringComparison.OrdinalIgnoreCase) ||
                value == "沉脸" || value == "黑化";
        }

        public void Configure(CubismModel nextModel, Camera unusedCamera)
        {
            model = nextModel;
            configured = model != null;
            ResolveBindings();
            LoadGloomTexture();
            ApplyFaceTexture();
        }

        public void SetEmotion(string value)
        {
            emotion = IsGloomExpression(value) ? "gloom" : null;
            ApplyFaceTexture();
        }

        void Awake()
        {
            // Live2DCharacterView normally calls Configure after adding this
            // component. Keep this safe when the prefab is inspected alone.
            configured = false;
        }

        void LateUpdate()
        {
            if (!configured || model == null || gloomRenderers.Length == 0)
            {
                RestoreMouthForm();
                return;
            }

            ApplyFaceTexture();
            if (emotion == "gloom") ApplyGloomMouthForm();
            else RestoreMouthForm();
        }

        void OnDisable()
        {
            RestoreFaceTexture();
            RestoreMouthForm();
        }

        void OnDestroy()
        {
            RestoreFaceTexture();
            RestoreMouthForm();
            if (ownsGloomTexture && gloomFaceTexture != null)
            {
                Destroy(gloomFaceTexture);
                gloomFaceTexture = null;
            }
        }

        void ResolveBindings()
        {
            var bindings = new List<RendererBinding>();
            var seen = new HashSet<CubismRenderer>();
            var sceneRenderers = model != null
                ? model.GetComponentsInChildren<CubismRenderer>(true)
                : new CubismRenderer[0];

            if (model != null && model.Drawables != null)
            {
                foreach (var drawable in model.Drawables)
                {
                    if (drawable == null || !IsGloomAtlasDrawable(drawable.gameObject.name)) continue;
                    var renderer = FindRenderer(drawable, sceneRenderers);
                    if (renderer == null || !seen.Add(renderer)) continue;
                    bindings.Add(new RendererBinding(renderer));
                }
            }

            gloomRenderers = bindings.ToArray();
            mouthForm = FindParameter("ParamMouthForm");
            if (mouthForm != null) defaultMouthForm = mouthForm.Value;
        }

        static bool IsGloomAtlasDrawable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // The exact Glasses drawable is the metal frame. Its companion
            // shadow/repair layers are skin-side atlas pixels and do switch.
            if (string.Equals(name, "Glasses", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(name, "Hair_Back", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Hair_Back_", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(name, FaceDrawableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Nose", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Repair_Glasses", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Glasses_Shadow", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("Brow_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Eye_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Iris_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Cheek_Flush_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Ear_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Forehead_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Glasses_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Hair_", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        CubismRenderer FindRenderer(CubismDrawable drawable, CubismRenderer[] sceneRenderers)
        {
            // Drawable is the authoritative scene binding. This handles a
            // renderer living on a parallel node instead of Face_Skin itself.
            foreach (var candidate in sceneRenderers)
                if (candidate != null && candidate.Drawable == drawable) return candidate;

            // The shipped ZhouboDaily prefab currently binds this component on
            // the drawable GameObject. Keep a narrow fallback for older scenes.
            var sameNode = drawable.GetComponent<CubismRenderer>();
            if (sameNode != null) return sameNode;
            foreach (var candidate in drawable.GetComponentsInChildren<CubismRenderer>(true))
                if (candidate != null) return candidate;
            return null;
        }

        CubismParameter FindParameter(string id)
        {
            if (model == null || model.Parameters == null) return null;
            foreach (var parameter in model.Parameters)
                if (parameter != null && string.Equals(parameter.Id, id, StringComparison.OrdinalIgnoreCase))
                    return parameter;
            return null;
        }

        void LoadGloomTexture()
        {
            if (textureLoadAttempted) return;
            textureLoadAttempted = true;

            var streamingPath = Path.Combine(Application.streamingAssetsPath, StreamingTextureRelativePath);
            if (File.Exists(streamingPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(streamingPath);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                    {
                        name = "Zhoubo Gloom Daily (StreamingAssets)",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        anisoLevel = 0,
                    };
                    if (texture.LoadImage(bytes, true))
                    {
                        gloomFaceTexture = texture;
                        ownsGloomTexture = true;
                        return;
                    }
                    Destroy(texture);
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"[PR47] Zhoubo gloom atlas load failed: {error.Message}");
                }
            }

            // Editor/Resources fallback keeps the same source path available
            // for local inspection. The standalone .app uses StreamingAssets.
            gloomFaceTexture = Resources.Load<Texture2D>(ResourcesTexturePath);
            ownsGloomTexture = false;
            if (gloomFaceTexture == null)
                Debug.LogWarning("[PR47] Zhoubo gloom atlas missing; gloom keeps its mouth pose only.");
        }

        void ApplyFaceTexture()
        {
            if (gloomRenderers == null || gloomRenderers.Length == 0) return;
            LoadGloomTexture();
            var useGloom = emotion == "gloom" && gloomFaceTexture != null;
            foreach (var binding in gloomRenderers)
            {
                var target = useGloom ? gloomFaceTexture : binding.OriginalTexture;
                if (target != null && binding.Renderer.MainTexture != target)
                    binding.Renderer.MainTexture = target;
            }
        }

        void RestoreFaceTexture()
        {
            if (gloomRenderers == null) return;
            foreach (var binding in gloomRenderers)
            {
                if (binding.OriginalTexture != null && binding.Renderer.MainTexture != binding.OriginalTexture)
                    binding.Renderer.MainTexture = binding.OriginalTexture;
            }
        }

        void ApplyGloomMouthForm()
        {
            if (mouthForm == null) return;
            mouthForm.Value = GloomMouthForm;
            mouthFormWritten = true;
        }

        void RestoreMouthForm()
        {
            if (!mouthFormWritten || mouthForm == null) return;
            mouthForm.Value = defaultMouthForm;
            mouthFormWritten = false;
        }

        sealed class RendererBinding
        {
            public readonly CubismRenderer Renderer;
            public readonly Texture2D OriginalTexture;

            public RendererBinding(CubismRenderer renderer)
            {
                Renderer = renderer;
                OriginalTexture = renderer.MainTexture;
            }
        }
    }
}
