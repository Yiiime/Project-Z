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
        Canvas rootCanvas;
        readonly Vector3[] viewportCorners = new Vector3[4];
        string lastEmotion, lastMotion;
        bool lastTalking;
        int lastWidth, lastHeight;
        Coroutine portraitMotionReset;
        int portraitMotionGeneration;
        string activePortraitMotion;
        ZhouboGloomOverlay zhouboGloom;
        const int MinPortraitTextureDimension = 2;

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
            rootCanvas = viewport.GetComponentInParent<Canvas>(true)?.rootCanvas;
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
                ResetToDefault();
                stage.SetActive(true);
                ResetAnimatorToIdle(next);
                var existingUpdater = modelObject.GetComponent<CubismUpdateController>();
                if (existingUpdater != null)
                {
                    driver.HasUpdateController = true;
                    existingUpdater.Refresh();
                }
                image.enabled = true;
                lastEmotion = null; lastMotion = null; lastTalking = false;
                SetState(next.defaultEmotion, false);
                return;
            }
            ReleaseStage();
            profile = next;
            image.material = next.portraitDisplayMaterial;
            stage = new GameObject("Live2D Portrait Stage - " + next.characterId);
            // Keep model and camera in world units. The canvas scales only the resulting RawImage.
            if (stage.scene != gameObject.scene)
                SceneManager.MoveGameObjectToScene(stage, gameObject.scene);
            // Axing's thin eyelids lose vertex precision in the distant portrait
            // stage. This off-map position keeps the model and camera together.
            stage.transform.position = string.Equals(next.characterId, "axing", StringComparison.OrdinalIgnoreCase)
                ? new Vector3(64f, 64f, 0f)
                : new Vector3(10000f, 10000f, 0f);
            stage.SetActive(false);
            modelObject = Instantiate(next.prefab, stage.transform);
            modelObject.name = next.characterId + " Cubism Model";
            modelObject.transform.localPosition = next.modelPosition;
            modelObject.transform.localScale = Vector3.one * Mathf.Max(.001f, next.modelScale);
            SetLayer(modelObject.transform, next.renderLayer);
            if (string.Equals(next.characterId, "xiaoxiaoqing", StringComparison.OrdinalIgnoreCase))
                modelObject.AddComponent<QingyeR61VisibilityGuard>();
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
            ResetAnimatorToIdle(next);
            var model = modelObject.GetComponent<CubismModel>();
            if (model != null) { model.ForceUpdateNow(); FrameModel(model); }
            // CubismUpdateController owns the single LateUpdate dispatch. Configure
            // the driver after the stage is active, then mark it as controller-owned
            // before Refresh() snapshots ICubismUpdatable components. Otherwise the
            // driver also runs its fallback LateUpdate and can race the motion/fade
            // pass on the following frame.
            driver.Configure(next);
            if (IsZhouboProfile(next))
            {
                zhouboGloom = stage.AddComponent<ZhouboGloomOverlay>();
                zhouboGloom.Configure(model, modelCamera);
            }
            driver.HasUpdateController = updater != null;
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

        void ResetAnimatorToIdle(Live2DModelProfile next)
        {
            if (animator == null || !animator.enabled || next == null) return;

            // Assigning a controller while the temporary stage is inactive does not
            // guarantee that Unity evaluates its default state when the stage opens.
            // Rebind/Play/Update makes the authored Cubism idle clip the source of
            // eye and breath values before the Cubism model schedules its native update.
            animator.Rebind();
            string idle = string.IsNullOrEmpty(next.idleState) ? "Idle" : next.idleState;
            if (!string.IsNullOrEmpty(idle) && animator.HasState(0, Animator.StringToHash(idle)))
                animator.Play(idle, 0, 0f);
            animator.Update(0f);
        }

        public void SetState(string emotion, bool talking, string motion = null)
        {
            if (driver == null || profile == null) return;
            emotion = string.IsNullOrEmpty(emotion) ? profile.defaultEmotion : emotion;
            if (zhouboGloom != null)
                zhouboGloom.SetEmotion(IsZhouboProfile(profile) ? emotion : null);

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
        /// Plays the imported clip for the current character's dialogue expression.
        /// </summary>
        public bool PlayPortraitMotion(string key)
        {
            if (!IsVisible || driver == null || profile == null ||
                !profile.TryGetPortraitMotion(key, out var state, out var seconds)) return false;
            key = state;

            // Each state uses the clip imported from the corresponding motion3 file.
            if (animator == null || !animator.enabled || !animator.HasState(0, Animator.StringToHash(key))) return false;

            CancelPortraitMotion();
            if (!driver.SetPortraitMotion(key)) return false;

            driver.SetTalkingDuringMotion(lastTalking);
            activePortraitMotion = key;
            portraitMotionGeneration++;
            lastMotion = key;
            animator.CrossFadeInFixedTime(key, 0f);
            portraitMotionReset = StartCoroutine(ResetPortraitMotionAfterDelay(portraitMotionGeneration, seconds));
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

        /// <summary>Immediately clears the current line's expression, speech and tears.</summary>
        public void ResetToDefault()
        {
            CancelPortraitMotion();
            if (zhouboGloom != null) zhouboGloom.SetEmotion(null);
            if (driver != null) driver.ResetToDefault();
            lastEmotion = null;
            lastMotion = null;
            lastTalking = false;
            if (animator != null && animator.enabled && profile != null &&
                !string.IsNullOrEmpty(profile.idleState) && animator.HasState(0, Animator.StringToHash(profile.idleState)))
                animator.CrossFadeInFixedTime(profile.idleState, .05f);
        }

        /// <summary>Starts the explicit audio/phoneme speech drive for one line segment.</summary>
        public bool BeginSpeechDrive(string turnId, int segmentIndex)
        {
            return IsVisible && driver != null && driver.BeginSpeechDrive(turnId, segmentIndex);
        }

        /// <summary>Writes a normalized 0..1 phoneme amplitude for the active segment.</summary>
        public bool SetSpeechAmplitude(string turnId, int segmentIndex, float amplitude)
        {
            return IsVisible && driver != null && driver.SetSpeechAmplitude(turnId, segmentIndex, amplitude);
        }

        /// <summary>Ends the explicit speech drive while preserving the current face hold.</summary>
        public bool EndSpeechDrive(string turnId, int segmentIndex)
        {
            return driver != null && driver.EndSpeechDrive(turnId, segmentIndex);
        }

        /// <summary>Cancels a segment and rejects any late amplitude callbacks for it.</summary>
        public bool CancelSpeechDrive(string turnId, int segmentIndex)
        {
            return driver != null && driver.CancelSpeechDrive(turnId, segmentIndex);
        }

        public bool HasSpeechDrive => driver != null && driver.HasSpeechDrive;

        public void Hide()
        {
            CancelPortraitMotion();
            if (zhouboGloom != null) zhouboGloom.SetEmotion(null);
            if (driver != null) driver.ResetToDefault();
            lastEmotion = null; lastMotion = null; lastTalking = false;
            if (image != null) image.enabled = false;
            if (stage != null) stage.SetActive(false);
        }
        void LateUpdate() { if (IsVisible) ResizeTarget(); }
        void ResizeTarget()
        {
            if (viewport == null || modelCamera == null || profile == null) return;
            var pixelSize = GetViewportPixelSize();
            if (pixelSize.x < MinPortraitTextureDimension || pixelSize.y < MinPortraitTextureDimension)
            {
                // The first Show can run before the overlay Canvas has completed
                // its layout. Keep the old profile value as a one-frame fallback;
                // LateUpdate replaces it with the real pixel size once available.
                float fallbackAspect = Mathf.Clamp(viewport.rect.width / Mathf.Max(1f, viewport.rect.height), .25f, 3f);
                int fallbackHeight = Mathf.Max(MinPortraitTextureDimension, profile.textureHeight);
                pixelSize = new Vector2(fallbackHeight * fallbackAspect, fallbackHeight);
            }

            float aspect = Mathf.Clamp(pixelSize.x / Mathf.Max(1f, pixelSize.y), .25f, 3f);
            int width = Mathf.Max(MinPortraitTextureDimension, Mathf.RoundToInt(pixelSize.x));
            int height = Mathf.Max(MinPortraitTextureDimension, Mathf.RoundToInt(pixelSize.y));
            int maxDimension = SystemInfo.maxTextureSize;
            if (maxDimension > 0 && Mathf.Max(width, height) > maxDimension)
            {
                float resize = maxDimension / (float)Mathf.Max(width, height);
                width = Mathf.Max(MinPortraitTextureDimension, Mathf.RoundToInt(width * resize));
                height = Mathf.Max(MinPortraitTextureDimension, Mathf.RoundToInt(height * resize));
            }

            if (target != null && target.IsCreated() && width == lastWidth && height == lastHeight) return;
            modelCamera.targetTexture = null;
            if (target == null)
            {
                // Preserve the atlas values while the model layers composite. The display
                // material converts the finished portrait to the canvas color space.
                var readWrite = profile.portraitDisplayMaterial != null
                    ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default;
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, readWrite)
                { name = "Live2D Portrait Alpha", antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            else
            {
                // Keep the managed RenderTexture object stable while a window is
                // being resized; only its native storage is recreated.
                target.Release();
                target.width = width;
                target.height = height;
            }
            if (!target.IsCreated()) target.Create();
            modelCamera.targetTexture = target; modelCamera.aspect = aspect;
            image.texture = target; lastWidth = width; lastHeight = height;
        }

        Vector2 GetViewportPixelSize()
        {
            // Project the displayed rectangle. Its world corners already include
            // the CanvasScaler transform on a Retina screen-space overlay.
            viewport.GetWorldCorners(viewportCorners);
            Camera projectionCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;
            Vector2 lowerLeft = RectTransformUtility.WorldToScreenPoint(projectionCamera, viewportCorners[0]);
            Vector2 upperRight = RectTransformUtility.WorldToScreenPoint(projectionCamera, viewportCorners[2]);
            Vector2 size = new Vector2(Mathf.Abs(upperRight.x - lowerLeft.x), Mathf.Abs(upperRight.y - lowerLeft.y));

            // MemoryDialogueView scales the portrait RectTransform during its
            // entrance animation. Do not resize the native target for that visual
            // transition; the RawImage still receives the presentation scale.
            Vector3 localScale = viewport.localScale;
            if (Mathf.Abs(localScale.x) > .0001f) size.x /= Mathf.Abs(localScale.x);
            if (Mathf.Abs(localScale.y) > .0001f) size.y /= Mathf.Abs(localScale.y);

            return size;
        }
        static void SetLayer(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            foreach (Transform child in node) SetLayer(child, layer);
        }

        static bool IsZhouboProfile(Live2DModelProfile value)
        {
            return value != null && string.Equals(value.characterId, "zhou_bobo_daily", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool SupportsExternalExpression(Live2DModelProfile value, string key)
        {
            return IsZhouboProfile(value) && ZhouboGloomOverlay.IsGloomExpression(key);
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

        IEnumerator ResetPortraitMotionAfterDelay(int generation, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
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
            stage = null; modelObject = null; modelCamera = null; driver = null; animator = null; zhouboGloom = null; profile = null;
        }
        void Release()
        {
            ReleaseStage();
            if (image != null) Destroy(image.gameObject);
            image = null;
            rootCanvas = null;
        }
        void OnDisable() { Hide(); }
        void OnDestroy() { Release(); }
    }
}
