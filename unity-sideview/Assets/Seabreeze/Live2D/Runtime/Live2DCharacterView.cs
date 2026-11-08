using System;
using System.Collections;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Owns a real Cubism model, isolated URP camera and alpha render target. No scene-pipeline swaps.</summary>
    public sealed class Live2DCharacterView : MonoBehaviour
    {
        RectTransform viewport;
        RawImage image;
        GameObject stage;
        GameObject modelObject;
        Camera modelCamera;
        RenderTexture target;
        Live2DModelProfile profile;
        Live2DStateDriver driver;
        Animator animator;
        string lastEmotion, lastMotion;
        bool lastTalking;
        int lastWidth, lastHeight;
        Coroutine portraitMotionReset;
        int portraitMotionGeneration;
        string activePortraitMotion;

        const float PortraitMotionDuration = 3f;

        public bool IsVisible => stage != null && stage.activeSelf;
        public Live2DModelProfile CurrentProfile => profile;
        internal Camera PortraitCamera => modelCamera;
        public CubismModel CurrentModel => modelObject != null ? modelObject.GetComponent<CubismModel>() : null;

        public void Initialize(RectTransform viewport)
        {
            if (viewport == null) throw new ArgumentNullException(nameof(viewport));
            if (this.viewport == viewport && image != null) return;
            Release();
            this.viewport = viewport;
            var go = new GameObject("Live2D Render Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(viewport, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            image = go.GetComponent<RawImage>(); image.raycastTarget = false;
            image.enabled = false;
        }

        public void Show(Live2DModelProfile next)
        {
            if (viewport == null || next == null || next.prefab == null) { Hide(); return; }
            if (profile == next && modelObject != null)
            {
                stage.SetActive(true); image.enabled = true; return;
            }
            ReleaseStage();
            profile = next;
            stage = new GameObject("Live2D Portrait Stage - " + next.characterId);
            // Keep model and camera in world units. The canvas scales only the resulting RawImage.
            if (stage.scene != gameObject.scene)
                SceneManager.MoveGameObjectToScene(stage, gameObject.scene);
            stage.transform.position = new Vector3(10000, 10000, 0);
            stage.SetActive(false);
            modelObject = Instantiate(next.prefab, stage.transform);
            modelObject.name = next.characterId + " Cubism Model";
            modelObject.transform.localPosition = next.modelPosition;
            modelObject.transform.localScale = Vector3.one * Mathf.Max(.001f, next.modelScale);
            SetLayer(modelObject.transform, next.renderLayer);
            foreach (var expression in modelObject.GetComponentsInChildren<CubismExpressionController>(true)) expression.enabled = false;
            animator = modelObject.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = next.animatorController;
                animator.enabled = next.animatorController != null;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }
            driver = modelObject.AddComponent<Live2DStateDriver>();
            var updater = modelObject.GetComponent<CubismUpdateController>();
            if (updater == null) updater = modelObject.AddComponent<CubismUpdateController>();
            var cameraGo = new GameObject("Live2D Portrait Camera", typeof(Camera));
            cameraGo.transform.SetParent(stage.transform, false);
            cameraGo.layer = next.renderLayer;
            cameraGo.transform.localPosition = new Vector3(next.cameraCenter.x, next.cameraCenter.y, -10);
            modelCamera = cameraGo.GetComponent<Camera>();
            modelCamera.orthographic = true;
            modelCamera.orthographicSize = Mathf.Max(.01f, next.orthographicSize);
            modelCamera.clearFlags = CameraClearFlags.SolidColor;
            modelCamera.backgroundColor = Color.clear;
            modelCamera.cullingMask = 1 << next.renderLayer;
            modelCamera.nearClipPlane = .1f; modelCamera.farClipPlane = 100;
            modelCamera.allowHDR = false; modelCamera.allowMSAA = false;
            modelCamera.depth = -100;
            var data = modelCamera.GetUniversalAdditionalCameraData();
            data.SetRenderer(next.rendererIndex);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;
            image.uvRect = next.uvRect;
            stage.SetActive(true);
            var model = modelObject.GetComponent<CubismModel>();
            if (model != null) { model.ForceUpdateNow(); FrameModel(model); }
            driver.Configure(next);
            driver.HasUpdateController = true;
            updater.Refresh();
            ResizeTarget();
            image.enabled = true;
            lastEmotion = null; lastMotion = null;
            SetState(next.defaultEmotion, false);
        }

        void FrameModel(CubismModel model)
        {
            if (!profile.autoFrame || model.Drawables == null) return;
            bool found = false;
            var bounds = new Bounds();
            foreach (var drawable in model.Drawables)
            {
                if (drawable == null || drawable.VertexPositions == null) continue;
                foreach (var vertex in drawable.VertexPositions)
                {
                    var world = drawable.transform.TransformPoint(vertex);
                    if (!found) { bounds = new Bounds(world, Vector3.zero); found = true; }
                    else bounds.Encapsulate(world);
                }
            }
            if (!found || bounds.size.y < .001f) return;
            float halfHeight = bounds.size.y * profile.visibleHeight * .5f;
            var center = new Vector3(bounds.center.x, Mathf.Lerp(bounds.min.y, bounds.max.y, profile.verticalFocus), stage.transform.position.z - 10);
            center.x += profile.cameraCenter.x; center.y += profile.cameraCenter.y;
            modelCamera.transform.position = center;
            modelCamera.orthographicSize = halfHeight;
        }

        public void SetState(string emotion, bool talking, string motion = null)
        {
            if (driver == null || profile == null) return;
            emotion = string.IsNullOrEmpty(emotion) ? profile.defaultEmotion : emotion;

            if (!string.IsNullOrEmpty(activePortraitMotion))
            {
                // MemoryDialogueView keeps submitting neutral/talking while text types.
                // Let an explicit portrait clip finish instead of cancelling it next frame.
                if (string.IsNullOrEmpty(motion))
                {
                    lastEmotion = emotion;
                    lastTalking = talking;
                    driver.SetTalkingDuringMotion(talking);
                    return;
                }
                CancelPortraitMotion();
            }
            if (emotion == lastEmotion && talking == lastTalking && motion == lastMotion) return;
            driver.SetState(emotion, talking);
            if (animator != null && animator.enabled)
            {
                string state = !string.IsNullOrEmpty(motion) ? motion : talking ? profile.talkState : profile.idleState;
                if (!string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state)))
                    animator.CrossFadeInFixedTime(state, .18f);
            }
            lastEmotion = emotion; lastTalking = talking; lastMotion = motion;
        }

        /// <summary>
        /// Plays one of Z's authored portrait motions without inferring emotion from dialogue text.
        /// The matching Animator state is expected to use the imported 3-second motion3 clip.
        /// </summary>
        public bool PlayPortraitMotion(string key)
        {
            if (!IsVisible || driver == null || profile == null || !IsZProfile(profile)) return false;
            key = NormalizePortraitMotion(key);
            if (key == null) return false;

            // Each state uses the clip imported from the corresponding motion3 file.
            if (animator == null || !animator.enabled || !animator.HasState(0, Animator.StringToHash(key))) return false;

            CancelPortraitMotion();
            if (!driver.SetPortraitMotion(key)) return false;

            activePortraitMotion = key;
            portraitMotionGeneration++;
            lastMotion = key;
            animator.CrossFadeInFixedTime(key, 0f);
            portraitMotionReset = StartCoroutine(ResetPortraitMotionAfterDelay(portraitMotionGeneration));
            return true;
        }

        /// <summary>Explicitly cancels an active portrait clip and restores the prior dialogue state.</summary>
        public void StopPortraitMotion()
        {
            if (string.IsNullOrEmpty(activePortraitMotion)) return;
            CancelPortraitMotion();
            if (driver == null || profile == null) return;
            driver.SetState(lastEmotion, lastTalking);
            if (animator != null && animator.enabled)
            {
                string state = lastTalking ? profile.talkState : profile.idleState;
                if (!string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state)))
                    animator.CrossFadeInFixedTime(state, .18f);
            }
        }

        public void Hide()
        {
            CancelPortraitMotion();
            if (image != null) image.enabled = false;
            if (stage != null) stage.SetActive(false);
        }
        void LateUpdate() { if (IsVisible) ResizeTarget(); }
        void ResizeTarget()
        {
            if (viewport == null || modelCamera == null || profile == null) return;
            float aspect = Mathf.Clamp(viewport.rect.width / Mathf.Max(1, viewport.rect.height), .25f, 3f);
            int height = Mathf.Clamp(profile.textureHeight, 256, 2048);
            int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 128, 4096);
            if (target != null && target.IsCreated() && width == lastWidth && height == lastHeight) return;
            if (target != null) { modelCamera.targetTexture = null; target.Release(); Destroy(target); }
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            { name = "Live2D Portrait Alpha", antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            target.Create(); modelCamera.targetTexture = target; modelCamera.aspect = aspect;
            image.texture = target; lastWidth = width; lastHeight = height;
        }
        static void SetLayer(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            foreach (Transform child in node) SetLayer(child, layer);
        }

        static bool IsZProfile(Live2DModelProfile value)
        {
            return value != null && string.Equals(value.characterId, "z", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizePortraitMotion(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim().ToLowerInvariant();
            if (key == "cry" || key == "bawl" || key == "surprise" || key == "starry_curious") return key;
            return null;
        }

        void CancelPortraitMotion()
        {
            if (portraitMotionReset != null)
            {
                StopCoroutine(portraitMotionReset);
                portraitMotionReset = null;
            }
            portraitMotionGeneration++;
            activePortraitMotion = null;
            lastMotion = null;
            if (driver != null) driver.ClearPortraitMotion();
        }

        IEnumerator ResetPortraitMotionAfterDelay(int generation)
        {
            yield return new WaitForSecondsRealtime(PortraitMotionDuration);
            if (generation != portraitMotionGeneration || string.IsNullOrEmpty(activePortraitMotion)) yield break;

            activePortraitMotion = null;
            portraitMotionReset = null;
            lastMotion = null;
            if (driver == null || profile == null) yield break;

            driver.ClearPortraitMotion();
            driver.SetState(lastEmotion, lastTalking);
            if (animator != null && animator.enabled)
            {
                string state = lastTalking ? profile.talkState : profile.idleState;
                if (!string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state)))
                    animator.CrossFadeInFixedTime(state, .18f);
            }
        }

        void ReleaseStage()
        {
            CancelPortraitMotion();
            if (modelCamera != null) modelCamera.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); target = null; }
            if (stage != null) { stage.SetActive(false); Destroy(stage); }
            stage = null; modelObject = null; modelCamera = null; driver = null; animator = null; profile = null;
        }
        void Release()
        {
            ReleaseStage();
            if (image != null) Destroy(image.gameObject);
            image = null;
        }
        void OnDisable() { Hide(); }
        void OnDestroy() { Release(); }
    }
}
