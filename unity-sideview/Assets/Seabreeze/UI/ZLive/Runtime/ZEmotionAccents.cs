using System;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace ZLive.ModelPerformance
{
    /// <summary>
    /// Runtime-only face accents. The object is created beside the model in a
    /// portrait-camera canvas, owned by the live host. Coordinates follow
    /// the model mesh and remain inside the character RenderTexture.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1200)]
    public sealed class ZEmotionAccents : MonoBehaviour
    {
        private const string CanvasName = "Z Emotion Accents Canvas";
        private const string AccentsName = "Z Emotion Accents";
        private const float FadeSpeed = 9f;
        private const float ClearThreshold = 0.002f;
        private const float EyeClosedSwap = 0.12f;
        private const float EyeOpenSwap = 0.15f;

        // These points are calibrated against texture_00.png (4096x4096).
        // The UVs land on the visible lower-lid pixels rather than on the
        // transparent padding around each eye sprite. Each point is resolved
        // to a static UV triangle once, then follows that triangle's dynamic
        // VertexPositions at runtime.
        private static readonly EyeUvAnchors LeftOpenUv = new EyeUvAnchors(
            new Vector2(0.6653f, 0.7488f),
            new Vector2(0.6616f, 0.7454f),
            new Vector2(0.6580f, 0.7446f));
        private static readonly EyeUvAnchors RightOpenUv = new EyeUvAnchors(
            new Vector2(0.6952f, 0.7495f),
            new Vector2(0.7002f, 0.7451f),
            new Vector2(0.7051f, 0.7444f));
        private static readonly EyeUvAnchors LeftClosedUv = new EyeUvAnchors(
            new Vector2(0.9126f, 0.7510f),
            new Vector2(0.9102f, 0.7512f),
            new Vector2(0.9077f, 0.7515f));
        private static readonly EyeUvAnchors RightClosedUv = new EyeUvAnchors(
            new Vector2(0.9829f, 0.7510f),
            new Vector2(0.9854f, 0.7512f),
            new Vector2(0.9878f, 0.7515f));

        // Independent outer-eye anchors for Cry/Bawl. These are calibrated
        // from the visible outer-corner pixels in texture_00.png; the
        // existing Corner/Glint points above stay dedicated to Sorrow.
        private static readonly Vector2 LeftOuterOpenUv = new Vector2(0.6466f, 0.7487f);
        private static readonly Vector2 RightOuterOpenUv = new Vector2(0.7143f, 0.7499f);
        private static readonly Vector2 LeftOuterClosedUv = new Vector2(0.8979f, 0.7507f);
        private static readonly Vector2 RightOuterClosedUv = new Vector2(0.9983f, 0.7529f);

        private ZPerformanceController controller;
        private Canvas canvas;
        private RectTransform canvasRect;
        private ZEmotionAccentGraphic graphic;
        private ZEmotionAccentKind renderedKind;
        private float visibleIntensity;
        private float targetIntensity;
        private bool anchorsReady;
        private bool fallbackWarningLogged;
        private bool tearBindingWarningLogged;
        private FaceProjection projection;
        private EyeMeshBinding leftOpenBinding;
        private EyeMeshBinding rightOpenBinding;
        private EyeMeshBinding leftClosedBinding;
        private EyeMeshBinding rightClosedBinding;
        private UvTriangleBinding leftOuterOpenBinding;
        private UvTriangleBinding rightOuterOpenBinding;
        private UvTriangleBinding leftOuterClosedBinding;
        private UvTriangleBinding rightOuterClosedBinding;

        /// <summary>当前正在显示的辅助图形名；无图形时为 None。</summary>
        public string ActiveAccent => renderedKind == ZEmotionAccentKind.None ? "None" : renderedKind.ToString();

        /// <summary>经过淡入淡出的实际可见强度，范围为 0 到 1。</summary>
        public float VisibleIntensity => visibleIntensity;

        /// <summary>当前模型是否成功生成了当前辅助图形所需的锚点。</summary>
        public bool AnchorsReady
        {
            get
            {
                if (!anchorsReady)
                    return false;
                if (renderedKind == ZEmotionAccentKind.Sorrow)
                    return projection.LeftTearReady && projection.RightTearReady;
                if (renderedKind == ZEmotionAccentKind.Cry || renderedKind == ZEmotionAccentKind.Bawl)
                    return projection.LeftOuterTearReady && projection.RightOuterTearReady;
                if (renderedKind == ZEmotionAccentKind.Gloom)
                    return projection.FaceShadowReady && graphic != null && graphic.FaceShadowMaterialReady;
                return true;
            }
        }

        /// <summary>
        /// Whether both Cry/Bawl streams use projected points outside their
        /// respective eye centers, away from the nose. This is calculated
        /// from the actual outer anchors used by the renderer.
        /// </summary>
        public bool TearsStartAtOuterCorners
        {
            get
            {
                if (!projection.IsValid || !projection.LeftOuterTearReady || !projection.RightOuterTearReady)
                    return false;

                var leftOffset = Vector2.Dot(projection.LeftOuterTear - projection.LeftEye, projection.Right);
                var rightOffset = Vector2.Dot(projection.RightOuterTear - projection.RightEye, projection.Right);
                return leftOffset < 0f && rightOffset > 0f;
            }
        }



        public static ZEmotionAccents Create(ZPerformanceController performance, Camera camera, Transform parent)
        {
            var canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.layer = 30;
            var overlay = canvasObject.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.WorldSpace;
            overlay.worldCamera = camera;
            overlay.planeDistance = 1;
            overlay.sortingOrder = 500;
            var accentsObject = new GameObject(AccentsName, typeof(RectTransform), typeof(CanvasRenderer));
            accentsObject.transform.SetParent(canvasObject.transform, false);
            accentsObject.layer = 30;
            var rect = (RectTransform)accentsObject.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var accents = accentsObject.AddComponent<ZEmotionAccents>();
            accents.controller = performance;
            accents.canvas = overlay;
            accents.canvasRect = (RectTransform)canvasObject.transform;
            accents.EnsureGraphic();
            return accents;
        }

        private void Awake()
        {
            if (canvas == null)
                canvas = GetComponentInParent<Canvas>();
            if (canvasRect == null && canvas != null)
                canvasRect = canvas.GetComponent<RectTransform>();
            EnsureGraphic();
        }

        private void OnEnable()
        {
            EnsureGraphic();
        }

        private void OnDisable()
        {
            ClearVisuals();
        }

        private void OnDestroy()
        {
            ClearVisuals();
        }

        private void LateUpdate()
        {
            ResolveController();
            if (controller != null && controller.Paused) return;

            // Draw inside the portrait camera's transparent RenderTexture. A world
            // canvas avoids screen-space UI being composited onto the main display.
            var portraitCamera = canvas != null ? canvas.worldCamera : null;
            if (portraitCamera != null && portraitCamera.targetTexture != null)
            {
                var texture = portraitCamera.targetTexture;
                canvasRect.sizeDelta = new Vector2(texture.width, texture.height);
                canvasRect.position = portraitCamera.transform.position + portraitCamera.transform.forward * 2;
                canvasRect.rotation = portraitCamera.transform.rotation;
                canvasRect.localScale = Vector3.one * (2 * portraitCamera.orthographicSize / texture.height);
            }
            var requestedKind = ZEmotionAccentKind.None;
            var projectionReadyThisFrame = false;
            if (controller != null)
            {
                // Manual preview owns the accent route. Chat segments do not
                // set previewPresentationActive, so explicit chat face
                // expressions can request an auxiliary graphic.
                requestedKind = controller.IsPreviewPresentationActive
                    ? AccentForMotion(controller.CurrentMotion.ToString())
                    : AccentForExpression(controller.CurrentExpression.ToString());
            }

            if (requestedKind != ZEmotionAccentKind.None &&
                TryBuildProjection(out var nextProjection, requestedKind == ZEmotionAccentKind.Gloom))
            {
                if (requestedKind == ZEmotionAccentKind.Gloom && nextProjection.FaceShadowReady)
                {
                    EnsureGraphic();
                    if (graphic != null)
                        graphic.PrepareFaceShadow(nextProjection.FaceShadowTexture);
                }
                projection = nextProjection;
                anchorsReady = nextProjection.IsReadyFor(requestedKind) &&
                    (requestedKind != ZEmotionAccentKind.Gloom ||
                     (graphic != null && graphic.FaceShadowMaterialReady));
                projectionReadyThisFrame = anchorsReady;
            }
            else if (requestedKind != ZEmotionAccentKind.None)
            {
                anchorsReady = false;
            }
            else
            {
                // Neutral/Smile/Troubled intentionally clear the accent. Keep
                // the last valid frame only for the short fade-out.
                anchorsReady = projection.IsValid;
                projectionReadyThisFrame = projection.IsValid;
            }

            if (requestedKind != renderedKind && requestedKind != ZEmotionAccentKind.None)
            {
                renderedKind = requestedKind;
                // Switching from one icon to another must not expose the new
                // shape at the old icon's full opacity for one frame.
                visibleIntensity = 0f;
            }

            targetIntensity = requestedKind == ZEmotionAccentKind.None || !projectionReadyThisFrame ? 0f : 1f;
            var speedMultiplier = controller == null
                ? 1f
                : Mathf.Clamp(controller.MotionSpeedMultiplier, 0.25f, 2f);
            var fade = FadeSpeed * speedMultiplier;
            visibleIntensity = Mathf.MoveTowards(visibleIntensity, targetIntensity, Time.unscaledDeltaTime * fade);

            if (visibleIntensity <= ClearThreshold && targetIntensity <= 0f)
            {
                visibleIntensity = 0f;
                renderedKind = ZEmotionAccentKind.None;
                anchorsReady = false;
                projection = default(FaceProjection);
            }

            if (graphic == null)
                EnsureGraphic();
            if (graphic == null)
                return;

            graphic.raycastTarget = false;
            graphic.enabled = visibleIntensity > ClearThreshold && renderedKind != ZEmotionAccentKind.None;
            graphic.SetAccent(renderedKind, visibleIntensity, projection, controller != null ? controller.Elapsed : 0);
        }

        private void ClearVisuals()
        {
            visibleIntensity = 0f;
            targetIntensity = 0f;
            renderedKind = ZEmotionAccentKind.None;
            anchorsReady = false;
            projection = default(FaceProjection);
            if (graphic != null)
            {
                graphic.SetAccent(ZEmotionAccentKind.None, 0f, projection);
                graphic.raycastTarget = false;
                graphic.enabled = false;
            }
        }

        private void EnsureGraphic()
        {
            if (GetComponent<CanvasRenderer>() == null)
                gameObject.AddComponent<CanvasRenderer>();
            if (graphic == null)
                graphic = GetComponent<ZEmotionAccentGraphic>();
            if (graphic == null)
                graphic = gameObject.AddComponent<ZEmotionAccentGraphic>();
            graphic.raycastTarget = false;
            graphic.color = Color.white;
        }

        private void ResolveController()
        {
            if (controller != null)
                return;

            controller = GetComponentInParent<ZPerformanceController>();
            if (controller == null)
                return;
        }

        private static ZEmotionAccentKind AccentForMotion(string motionName)
        {
            if (string.Equals(motionName, "Shy", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Shy;
            if (string.Equals(motionName, "Angry", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Angry;
            if (string.Equals(motionName, "Curious", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Curious;
            if (string.Equals(motionName, "Sorrow", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Sorrow;
            if (string.Equals(motionName, "Cry", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Cry;
            if (string.Equals(motionName, "Bawl", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Bawl;
            if (string.Equals(motionName, "Surprise", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Surprise;
            if (string.Equals(motionName, "Startle", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Startle;
            if (string.Equals(motionName, "Gloom", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Gloom;
            // Sad is deliberately an empty, tear-free accent.
            return ZEmotionAccentKind.None;
        }

        private static ZEmotionAccentKind AccentForExpression(string expressionName)
        {
            if (string.Equals(expressionName, "Surprise", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Surprise;
            if (string.Equals(expressionName, "Angry", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Angry;
            if (string.Equals(expressionName, "Blush", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Shy;
            if (string.Equals(expressionName, "Cry", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Cry;
            if (string.Equals(expressionName, "Bawl", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Bawl;
            if (string.Equals(expressionName, "Gloom", StringComparison.OrdinalIgnoreCase))
                return ZEmotionAccentKind.Gloom;
            // Neutral, Smile and Troubled stay model-only in chat. In
            // particular, Troubled must not be turned into a question mark;
            // Sigh has no ZEmotionAccents layer because ZSighFaceArt owns its
            // approved eye/mouth material.
            return ZEmotionAccentKind.None;
        }

        private bool TryBuildProjection(out FaceProjection result, bool buildFaceShadow)
        {
            result = default(FaceProjection);
            if (controller == null || canvasRect == null)
                return false;

            var model = controller.CubismModel;
            if (model == null || model.Drawables == null || model.Drawables.Length == 0)
                return false;

            var drawables = model.Drawables;
            var face = FindDrawable(drawables, "Face_Skin");
            Vector2 localMin;
            Vector2 localMax;
            Transform faceTransform;
            if (face == null)
            {
                WarnFallback("找不到 Face_Skin，暂不显示脸部辅助图形。");
                return false;
            }
            if (!TryGetLocalBounds(face, out localMin, out localMax))
                return false;
            faceTransform = face.transform;

            var localCenter = new Vector3(
                (localMin.x + localMax.x) * 0.5f,
                (localMin.y + localMax.y) * 0.5f,
                0f);
            var centerWorld = faceTransform.TransformPoint(localCenter);
            var rawRight = faceTransform.TransformVector(Vector3.right);
            var rawUp = faceTransform.TransformVector(Vector3.up);
            if (rawRight.sqrMagnitude < 0.000001f || rawUp.sqrMagnitude < 0.000001f)
                return false;

            var worldRight = rawRight.normalized;
            var worldUp = rawUp.normalized;
            var widthWorld = Mathf.Max(0.0001f, (localMax.x - localMin.x) * rawRight.magnitude);
            var heightWorld = Mathf.Max(0.0001f, (localMax.y - localMin.y) * rawUp.magnitude);

            var leftEye = FindDrawable(drawables, "Eye_Open_L");
            var rightEye = FindDrawable(drawables, "Eye_Open_R");
            var mouth = FindDrawable(drawables, "Mouth_Closed");
            var leftEyeWorld = GetDrawableCenterOrFallback(
                leftEye,
                centerWorld - worldRight * widthWorld * 0.24f + worldUp * heightWorld * 0.16f,
                out var leftEyeFallback);
            var rightEyeWorld = GetDrawableCenterOrFallback(
                rightEye,
                centerWorld + worldRight * widthWorld * 0.24f + worldUp * heightWorld * 0.16f,
                out var rightEyeFallback);
            var mouthWorld = GetDrawableCenterOrFallback(
                mouth,
                centerWorld - worldUp * heightWorld * 0.20f,
                out var mouthFallback);

            var leftEyeClosed = FindDrawable(drawables, "Eye_Closed_L");
            var rightEyeClosed = FindDrawable(drawables, "Eye_Closed_R");
            var leftTearWorldReady = TryBuildEyeAnchors(
                leftEye,
                leftEyeClosed,
                "ParamEyeLOpen",
                LeftOpenUv,
                LeftClosedUv,
                ref leftOpenBinding,
                ref leftClosedBinding,
                out var leftTearWorld);
            var rightTearWorldReady = TryBuildEyeAnchors(
                rightEye,
                rightEyeClosed,
                "ParamEyeROpen",
                RightOpenUv,
                RightClosedUv,
                ref rightOpenBinding,
                ref rightClosedBinding,
                out var rightTearWorld);
            var leftOuterWorldReady = TryBuildEyePoint(
                leftEye,
                leftEyeClosed,
                "ParamEyeLOpen",
                LeftOuterOpenUv,
                LeftOuterClosedUv,
                ref leftOuterOpenBinding,
                ref leftOuterClosedBinding,
                out var leftOuterWorld);
            var rightOuterWorldReady = TryBuildEyePoint(
                rightEye,
                rightEyeClosed,
                "ParamEyeROpen",
                RightOuterOpenUv,
                RightOuterClosedUv,
                ref rightOuterOpenBinding,
                ref rightOuterClosedBinding,
                out var rightOuterWorld);

            if (leftEyeFallback || rightEyeFallback || mouthFallback)
            {
                WarnFallback("部分眼/嘴网格不可用，通用脸部图形使用 Face_Skin bbox；泪效不使用该回退。");
            }

            if (!leftTearWorldReady || !rightTearWorldReady || !leftOuterWorldReady || !rightOuterWorldReady)
            {
                WarnTearBinding("眼睛泪效 UV 锚点不可用，已隐藏对应泪效，不使用 Face_Skin bbox 伪造位置。");
            }

            if (!TryWorldToCanvas(centerWorld, out var center) ||
                !TryWorldToCanvas(leftEyeWorld, out var leftEyePoint) ||
                !TryWorldToCanvas(rightEyeWorld, out var rightEyePoint) ||
                !TryWorldToCanvas(mouthWorld, out var mouthPoint))
            {
                return false;
            }

            Vector2 leftTear = Vector2.zero;
            Vector2 leftGlintA = Vector2.zero;
            Vector2 leftGlintB = Vector2.zero;
            Vector2 rightTear = Vector2.zero;
            Vector2 rightGlintA = Vector2.zero;
            Vector2 rightGlintB = Vector2.zero;
            var leftTearReady = leftTearWorldReady &&
                TryWorldToCanvas(leftTearWorld.Corner, out leftTear) &&
                TryWorldToCanvas(leftTearWorld.GlintA, out leftGlintA) &&
                TryWorldToCanvas(leftTearWorld.GlintB, out leftGlintB);
            var rightTearReady = rightTearWorldReady &&
                TryWorldToCanvas(rightTearWorld.Corner, out rightTear) &&
                TryWorldToCanvas(rightTearWorld.GlintA, out rightGlintA) &&
                TryWorldToCanvas(rightTearWorld.GlintB, out rightGlintB);
            Vector2 leftOuterTear = Vector2.zero;
            Vector2 rightOuterTear = Vector2.zero;
            var leftOuterTearReady = leftOuterWorldReady &&
                TryWorldToCanvas(leftOuterWorld, out leftOuterTear);
            var rightOuterTearReady = rightOuterWorldReady &&
                TryWorldToCanvas(rightOuterWorld, out rightOuterTear);

            if (!TryWorldToCanvas(centerWorld + worldRight * widthWorld, out var rightEdge) ||
                !TryWorldToCanvas(centerWorld + worldUp * heightWorld, out var topEdge))
            {
                return false;
            }

            var rightDelta = rightEdge - center;
            var upDelta = topEdge - center;
            var width = rightDelta.magnitude;
            var height = upDelta.magnitude;
            if (width < 0.5f || height < 0.5f)
                return false;

            Texture faceShadowTexture = null;
            Vector2[] faceShadowVertices = null;
            Vector2[] faceShadowUvs = null;
            int[] faceShadowIndices = null;
            var faceShadowReady = !buildFaceShadow || TryBuildFaceShadowMesh(
                face,
                out faceShadowTexture,
                out faceShadowVertices,
                out faceShadowUvs,
                out faceShadowIndices);

            result = new FaceProjection
            {
                IsValid = true,
                FaceCenter = center,
                LeftEye = leftEyePoint,
                RightEye = rightEyePoint,
                Mouth = mouthPoint,
                LeftTearReady = leftTearReady,
                RightTearReady = rightTearReady,
                LeftTear = leftTear,
                LeftGlintA = leftGlintA,
                LeftGlintB = leftGlintB,
                RightTear = rightTear,
                RightGlintA = rightGlintA,
                RightGlintB = rightGlintB,
                LeftOuterTearReady = leftOuterTearReady,
                RightOuterTearReady = rightOuterTearReady,
                LeftOuterTear = leftOuterTear,
                RightOuterTear = rightOuterTear,
                Right = rightDelta / width,
                Up = upDelta / height,
                Width = width,
                Height = height,
                FaceShadowReady = faceShadowReady,
                FaceShadowTexture = faceShadowTexture,
                FaceShadowVertices = faceShadowVertices,
                FaceShadowUvs = faceShadowUvs,
                FaceShadowIndices = faceShadowIndices,
            };
            return true;
        }

        private bool TryBuildFaceShadowMesh(
            CubismDrawable face,
            out Texture texture,
            out Vector2[] projectedVertices,
            out Vector2[] uvs,
            out int[] indices)
        {
            texture = null;
            projectedVertices = null;
            uvs = null;
            indices = null;
            if (face == null)
                return false;

            var renderer = face.GetComponent<CubismRenderer>();
            if (renderer == null || renderer.MainTexture == null)
                return false;

            Vector3[] sourceVertices;
            Vector2[] sourceUvs;
            int[] sourceIndices;
            try
            {
                sourceVertices = face.VertexPositions;
                sourceUvs = face.VertexUvs;
                sourceIndices = face.Indices;
            }
            catch (Exception)
            {
                return false;
            }

            if (sourceVertices == null || sourceVertices.Length == 0 ||
                sourceUvs == null || sourceUvs.Length != sourceVertices.Length ||
                sourceIndices == null || sourceIndices.Length < 3 || sourceIndices.Length % 3 != 0)
            {
                return false;
            }

            projectedVertices = new Vector2[sourceVertices.Length];
            for (var i = 0; i < sourceVertices.Length; i++)
            {
                var world = face.transform.TransformPoint(sourceVertices[i]);
                if (!TryWorldToCanvas(world, out projectedVertices[i]))
                {
                    projectedVertices = null;
                    return false;
                }
            }

            uvs = new Vector2[sourceUvs.Length];
            Array.Copy(sourceUvs, uvs, sourceUvs.Length);
            indices = new int[sourceIndices.Length];
            Array.Copy(sourceIndices, indices, sourceIndices.Length);
            texture = renderer.MainTexture;
            return texture != null;
        }

        private Vector3 GetDrawableCenterOrFallback(
            CubismDrawable drawable,
            Vector3 fallback,
            out bool usedFallback)
        {
            usedFallback = true;
            if (drawable == null || !TryGetLocalBounds(drawable, out var min, out var max))
                return fallback;

            var localCenter = new Vector3(
                (min.x + max.x) * 0.5f,
                (min.y + max.y) * 0.5f,
                0f);
            usedFallback = false;
            return drawable.transform.TransformPoint(localCenter);
        }

        private bool TryBuildEyeAnchors(
            CubismDrawable openDrawable,
            CubismDrawable closedDrawable,
            string openingParameterId,
            EyeUvAnchors openUv,
            EyeUvAnchors closedUv,
            ref EyeMeshBinding openBinding,
            ref EyeMeshBinding closedBinding,
            out EyeWorldAnchors anchors)
        {
            anchors = default(EyeWorldAnchors);
            var hasOpenBinding = TryGetEyeMeshBinding(openDrawable, openUv, ref openBinding);
            var hasClosedBinding = TryGetEyeMeshBinding(closedDrawable, closedUv, ref closedBinding);
            if (!hasOpenBinding && !hasClosedBinding)
                return false;

            var openingValue = 1f;
            var hasOpening = controller != null &&
                controller.TryGetParameterValue(openingParameterId, out openingValue);
            var opening = hasOpening
                ? Mathf.Clamp01(openingValue)
                : (hasOpenBinding ? 1f : 0f);
            var openWeight = Mathf.InverseLerp(EyeClosedSwap, EyeOpenSwap, opening);

            // Do not replace a missing active mesh with the other eye state.
            // During the swap both bindings are required; at either endpoint
            // only the mesh active for that endpoint matters.
            if ((openWeight <= 0f && !hasClosedBinding) ||
                (openWeight >= 1f && !hasOpenBinding) ||
                (openWeight > 0f && openWeight < 1f && (!hasOpenBinding || !hasClosedBinding)))
            {
                return false;
            }

            EyeWorldAnchors openAnchors = default(EyeWorldAnchors);
            EyeWorldAnchors closedAnchors = default(EyeWorldAnchors);
            var hasOpenAnchors = hasOpenBinding && TryEvaluateEyeAnchors(openBinding, out openAnchors);
            var hasClosedAnchors = hasClosedBinding && TryEvaluateEyeAnchors(closedBinding, out closedAnchors);
            if ((openWeight <= 0f && !hasClosedAnchors) ||
                (openWeight >= 1f && !hasOpenAnchors) ||
                (openWeight > 0f && openWeight < 1f && (!hasOpenAnchors || !hasClosedAnchors)))
            {
                return false;
            }

            anchors = hasOpenAnchors && hasClosedAnchors
                ? EyeWorldAnchors.Lerp(closedAnchors, openAnchors, openWeight)
                : (hasOpenAnchors ? openAnchors : closedAnchors);
            return true;
        }

        private bool TryBuildEyePoint(
            CubismDrawable openDrawable,
            CubismDrawable closedDrawable,
            string openingParameterId,
            Vector2 openUv,
            Vector2 closedUv,
            ref UvTriangleBinding openBinding,
            ref UvTriangleBinding closedBinding,
            out Vector3 point)
        {
            point = Vector3.zero;
            var hasOpenBinding = TryGetEyePointBinding(openDrawable, openUv, ref openBinding);
            var hasClosedBinding = TryGetEyePointBinding(closedDrawable, closedUv, ref closedBinding);
            if (!hasOpenBinding && !hasClosedBinding)
                return false;

            var openingValue = 1f;
            var hasOpening = controller != null &&
                controller.TryGetParameterValue(openingParameterId, out openingValue);
            var opening = hasOpening
                ? Mathf.Clamp01(openingValue)
                : (hasOpenBinding ? 1f : 0f);
            var openWeight = Mathf.InverseLerp(EyeClosedSwap, EyeOpenSwap, opening);

            if ((openWeight <= 0f && !hasClosedBinding) ||
                (openWeight >= 1f && !hasOpenBinding) ||
                (openWeight > 0f && openWeight < 1f && (!hasOpenBinding || !hasClosedBinding)))
            {
                return false;
            }

            var openPoint = Vector3.zero;
            var closedPoint = Vector3.zero;
            var hasOpenPoint = hasOpenBinding && TryEvaluateEyePoint(openBinding, out openPoint);
            var hasClosedPoint = hasClosedBinding && TryEvaluateEyePoint(closedBinding, out closedPoint);
            if ((openWeight <= 0f && !hasClosedPoint) ||
                (openWeight >= 1f && !hasOpenPoint) ||
                (openWeight > 0f && openWeight < 1f && (!hasOpenPoint || !hasClosedPoint)))
            {
                return false;
            }

            point = hasOpenPoint && hasClosedPoint
                ? Vector3.Lerp(closedPoint, openPoint, openWeight)
                : (hasOpenPoint ? openPoint : closedPoint);
            return true;
        }

        private bool TryGetEyeMeshBinding(
            CubismDrawable drawable,
            EyeUvAnchors uvAnchors,
            ref EyeMeshBinding cache)
        {
            if (drawable == null)
            {
                cache = default(EyeMeshBinding);
                return false;
            }

            if (cache.Drawable == drawable && cache.IsValid)
                return true;

            cache = new EyeMeshBinding { Drawable = drawable };
            Vector2[] uvs;
            int[] indices;
            try
            {
                uvs = drawable.VertexUvs;
                indices = drawable.Indices;
            }
            catch (Exception)
            {
                return false;
            }

            if (uvs == null || indices == null || uvs.Length == 0 || indices.Length < 3)
                return false;

            return TryCreateUvTriangle(drawable, uvs, indices, uvAnchors.Corner, out cache.Corner) &&
                TryCreateUvTriangle(drawable, uvs, indices, uvAnchors.GlintA, out cache.GlintA) &&
                TryCreateUvTriangle(drawable, uvs, indices, uvAnchors.GlintB, out cache.GlintB);
        }

        private static bool TryGetEyePointBinding(
            CubismDrawable drawable,
            Vector2 uv,
            ref UvTriangleBinding cache)
        {
            if (drawable == null)
            {
                cache = default(UvTriangleBinding);
                return false;
            }

            if (cache.Drawable == drawable && cache.IsValid)
                return true;

            cache = new UvTriangleBinding { Drawable = drawable };
            Vector2[] uvs;
            int[] indices;
            try
            {
                uvs = drawable.VertexUvs;
                indices = drawable.Indices;
            }
            catch (Exception)
            {
                return false;
            }

            if (uvs == null || indices == null || uvs.Length == 0 || indices.Length < 3)
                return false;

            return TryCreateUvTriangle(drawable, uvs, indices, uv, out cache);
        }

        private static bool TryCreateUvTriangle(
            CubismDrawable drawable,
            Vector2[] uvs,
            int[] indices,
            Vector2 targetUv,
            out UvTriangleBinding binding)
        {
            binding = default(UvTriangleBinding);
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = indices[i];
                var b = indices[i + 1];
                var c = indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                    continue;

                if (!TryGetBarycentric(targetUv, uvs[a], uvs[b], uvs[c], out var weights))
                    continue;
                if (weights.x < -0.001f || weights.y < -0.001f || weights.z < -0.001f)
                    continue;

                binding = new UvTriangleBinding
                {
                    Drawable = drawable,
                    A = a,
                    B = b,
                    C = c,
                    Weights = weights,
                };
                return true;
            }

            return false;
        }

        private static bool TryGetBarycentric(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out Vector3 weights)
        {
            weights = Vector3.zero;
            var denominator = (b.y - c.y) * (a.x - c.x) +
                (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < 0.0000001f)
                return false;

            var wa = ((b.y - c.y) * (point.x - c.x) +
                (c.x - b.x) * (point.y - c.y)) / denominator;
            var wb = ((c.y - a.y) * (point.x - c.x) +
                (a.x - c.x) * (point.y - c.y)) / denominator;
            weights = new Vector3(wa, wb, 1f - wa - wb);
            return true;
        }

        private static bool TryEvaluateEyeAnchors(
            EyeMeshBinding binding,
            out EyeWorldAnchors anchors)
        {
            anchors = default(EyeWorldAnchors);
            if (!binding.IsValid)
                return false;

            Vector3[] vertices;
            try
            {
                // One native-to-managed vertex copy per eye mesh per frame;
                // UVs and indices remain in the cached static bindings.
                vertices = binding.Drawable.VertexPositions;
            }
            catch (Exception)
            {
                return false;
            }

            if (!TryEvaluateTriangle(vertices, binding.Corner, out var cornerLocal) ||
                !TryEvaluateTriangle(vertices, binding.GlintA, out var glintALocal) ||
                !TryEvaluateTriangle(vertices, binding.GlintB, out var glintBLocal))
            {
                return false;
            }

            var transform = binding.Drawable.transform;
            anchors.Corner = transform.TransformPoint(cornerLocal);
            anchors.GlintA = transform.TransformPoint(glintALocal);
            anchors.GlintB = transform.TransformPoint(glintBLocal);
            return true;
        }

        private static bool TryEvaluateEyePoint(
            UvTriangleBinding binding,
            out Vector3 point)
        {
            point = Vector3.zero;
            if (!binding.IsValid)
                return false;

            Vector3[] vertices;
            try
            {
                vertices = binding.Drawable.VertexPositions;
            }
            catch (Exception)
            {
                return false;
            }

            if (!TryEvaluateTriangle(vertices, binding, out var local))
                return false;

            point = binding.Drawable.transform.TransformPoint(local);
            return true;
        }

        private static bool TryEvaluateTriangle(
            Vector3[] vertices,
            UvTriangleBinding binding,
            out Vector3 local)
        {
            local = Vector3.zero;
            if (vertices == null || !binding.IsValid ||
                binding.A >= vertices.Length || binding.B >= vertices.Length || binding.C >= vertices.Length)
            {
                return false;
            }

            local = vertices[binding.A] * binding.Weights.x +
                vertices[binding.B] * binding.Weights.y +
                vertices[binding.C] * binding.Weights.z;
            return true;
        }

        private bool TryWorldToCanvas(Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            if (canvasRect == null)
                return false;

            var camera = canvas != null ? canvas.worldCamera : null;
            if (camera == null)
                return false;

            var viewport = camera.WorldToViewportPoint(world);
            if (viewport.z <= 0f) return false;
            local = new Vector2((viewport.x - .5f) * canvasRect.rect.width,
                                (viewport.y - .5f) * canvasRect.rect.height);
            return true;
        }

        private void WarnFallback(string reason)
        {
            if (fallbackWarningLogged)
                return;

            fallbackWarningLogged = true;
            Debug.LogWarning("[ZEmotionAccents] " + reason, this);
        }

        private void WarnTearBinding(string reason)
        {
            if (tearBindingWarningLogged)
                return;

            tearBindingWarningLogged = true;
            Debug.LogWarning("[ZEmotionAccents] " + reason, this);
        }

        private static CubismDrawable FindDrawable(CubismDrawable[] drawables, string wantedId)
        {
            if (drawables == null || string.IsNullOrEmpty(wantedId))
                return null;

            // Exact IDs win because imported models may contain both a named
            // drawable and a similarly named mask/helper drawable.
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
                    // Cubism can expose a proxy before its unmanaged model is
                    // ready. It is safe to inspect again on the next frame.
                }
            }

            for (var i = 0; i < drawables.Length; i++)
            {
                var drawable = drawables[i];
                if (drawable == null)
                    continue;
                try
                {
                    if (!string.IsNullOrEmpty(drawable.Id) &&
                        drawable.Id.IndexOf(wantedId, StringComparison.OrdinalIgnoreCase) >= 0)
                        return drawable;
                }
                catch (Exception)
                {
                    // See the exact-match pass above.
                }
            }

            return null;
        }

        private static bool TryGetLocalBounds(
            CubismDrawable drawable,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = Vector2.zero;
            maximum = Vector2.zero;
            if (drawable == null)
                return false;

            Vector3[] vertices;
            try
            {
                vertices = drawable.VertexPositions;
            }
            catch (Exception)
            {
                return false;
            }

            if (vertices == null || vertices.Length == 0)
                return false;

            var minX = float.PositiveInfinity;
            var minY = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var maxY = float.NegativeInfinity;
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                minX = Mathf.Min(minX, vertex.x);
                minY = Mathf.Min(minY, vertex.y);
                maxX = Mathf.Max(maxX, vertex.x);
                maxY = Mathf.Max(maxY, vertex.y);
            }

            if (float.IsInfinity(minX) || maxX - minX < 0.000001f || maxY - minY < 0.000001f)
                return false;

            minimum = new Vector2(minX, minY);
            maximum = new Vector2(maxX, maxY);
            return true;
        }

    }

    internal enum ZEmotionAccentKind
    {
        None,
        Shy,
        Angry,
        Curious,
        Sorrow,
        Cry,
        Bawl,
        Surprise,
        Startle,
        Gloom,
    }

    internal struct EyeUvAnchors
    {
        public Vector2 Corner;
        public Vector2 GlintA;
        public Vector2 GlintB;

        public EyeUvAnchors(Vector2 corner, Vector2 glintA, Vector2 glintB)
        {
            Corner = corner;
            GlintA = glintA;
            GlintB = glintB;
        }
    }

    internal struct UvTriangleBinding
    {
        public CubismDrawable Drawable;
        public int A;
        public int B;
        public int C;
        public Vector3 Weights;

        public bool IsValid
        {
            get { return Drawable != null && A >= 0 && B >= 0 && C >= 0; }
        }

    }

    internal struct EyeMeshBinding
    {
        public CubismDrawable Drawable;
        public UvTriangleBinding Corner;
        public UvTriangleBinding GlintA;
        public UvTriangleBinding GlintB;

        public bool IsValid
        {
            get { return Drawable != null && Corner.IsValid && GlintA.IsValid && GlintB.IsValid; }
        }
    }

    internal struct EyeWorldAnchors
    {
        public Vector3 Corner;
        public Vector3 GlintA;
        public Vector3 GlintB;

        public static EyeWorldAnchors Lerp(EyeWorldAnchors from, EyeWorldAnchors to, float amount)
        {
            return new EyeWorldAnchors
            {
                Corner = Vector3.Lerp(from.Corner, to.Corner, amount),
                GlintA = Vector3.Lerp(from.GlintA, to.GlintA, amount),
                GlintB = Vector3.Lerp(from.GlintB, to.GlintB, amount),
            };
        }
    }

    internal struct FaceProjection
    {
        public bool IsValid;
        public Vector2 FaceCenter;
        public Vector2 LeftEye;
        public Vector2 RightEye;
        public Vector2 Mouth;
        public bool LeftTearReady;
        public bool RightTearReady;
        public Vector2 LeftTear;
        public Vector2 LeftGlintA;
        public Vector2 LeftGlintB;
        public Vector2 RightTear;
        public Vector2 RightGlintA;
        public Vector2 RightGlintB;
        public bool LeftOuterTearReady;
        public bool RightOuterTearReady;
        public Vector2 LeftOuterTear;
        public Vector2 RightOuterTear;
        public Vector2 Right;
        public Vector2 Up;
        public float Width;
        public float Height;
        public bool FaceShadowReady;
        public Texture FaceShadowTexture;
        public Vector2[] FaceShadowVertices;
        public Vector2[] FaceShadowUvs;
        public int[] FaceShadowIndices;

        public bool IsReadyFor(ZEmotionAccentKind kind)
        {
            return kind != ZEmotionAccentKind.Gloom || FaceShadowReady;
        }

        public Vector2 FacePoint(float rightFraction, float upFraction)
        {
            return FaceCenter + Right * (Width * rightFraction) + Up * (Height * upFraction);
        }

        public Vector2 Along(Vector2 anchor, float rightFraction, float upFraction)
        {
            return anchor + Right * (Width * rightFraction) + Up * (Height * upFraction);
        }
    }

    /// <summary>
    /// Runtime uGUI renderer. Icon geometry is made from vertices, triangles
    /// and vertex colors; Gloom reuses only the Face_Skin atlas alpha through
    /// a separate material. Raycasts are explicitly disabled.
    /// </summary>
    internal sealed class ZEmotionAccentGraphic : Graphic
    {
        private const string FaceShadowShaderName = "UI/ZFaceShadow";
        private static readonly Color ShyPink = new Color32(247, 135, 164, 190);
        private static readonly Color ShyHighlight = new Color32(255, 224, 232, 170);
        private static readonly Color AngryBrick = new Color32(210, 65, 58, 235);
        private static readonly Color AngryShadow = new Color32(132, 42, 48, 210);
        private static readonly Color CuriousPurple = new Color32(145, 94, 195, 235);
        private static readonly Color CuriousHighlight = new Color32(225, 196, 246, 120);
        private static readonly Color SurpriseGold = new Color32(255, 194, 62, 245);
        private static readonly Color SurpriseHighlight = new Color32(255, 244, 168, 235);
        private static readonly Color StartleGold = new Color32(255, 205, 62, 245);
        private static readonly Color StartleHighlight = new Color32(255, 248, 177, 235);
        private static readonly Color GloomBlueGray = new Color32(45, 48, 65, 165);
        private static readonly Color TearCyan = new Color32(169, 225, 235, 220);
        private static readonly Color TearHighlight = new Color32(238, 255, 255, 200);
        private static readonly Color BawlCyan = new Color32(73, 198, 229, 215);
        private static readonly Color BawlHighlight = new Color32(220, 255, 255, 205);

        private ZEmotionAccentKind kind;
        private float intensity;
        private float animationTime;
        private FaceProjection projection;
        private Material faceShadowMaterial;
        private Texture faceShadowTexture;
        private bool faceShadowShaderWarningLogged;

        internal bool FaceShadowMaterialReady => faceShadowMaterial != null;

        // Unity's built-in white texture is not an imported asset; it lets
        // the standard UI shader preserve the vertex colors without adding a
        // project texture dependency.
        public override Texture mainTexture => kind == ZEmotionAccentKind.Gloom && faceShadowTexture != null
            ? faceShadowTexture
            : Texture2D.whiteTexture;

        internal void SetAccent(ZEmotionAccentKind nextKind, float nextIntensity, FaceProjection nextProjection, float time = 0)
        {
            animationTime = time;
            kind = nextKind;
            intensity = Mathf.Clamp01(nextIntensity);
            projection = nextProjection;
            if (kind == ZEmotionAccentKind.Gloom && projection.FaceShadowReady)
            {
                ConfigureFaceShadow(projection.FaceShadowTexture);
                material = faceShadowMaterial;
            }
            else
            {
                material = null;
            }
            SetVerticesDirty();
        }

        internal bool PrepareFaceShadow(Texture texture)
        {
            ConfigureFaceShadow(texture);
            return FaceShadowMaterialReady;
        }

        private void ConfigureFaceShadow(Texture texture)
        {
            if (texture == null)
                return;

            if (faceShadowMaterial == null)
            {
                var shader = Shader.Find(FaceShadowShaderName);
                if (shader == null)
                    shader = Resources.Load<Shader>("ZFaceShadow");
                if (shader == null)
                {
                    if (!faceShadowShaderWarningLogged)
                    {
                        faceShadowShaderWarningLogged = true;
                        Debug.LogWarning("[ZEmotionAccents] 找不到 ZFaceShadow shader，暂不显示 Gloom 脸部渐变。", this);
                    }
                    return;
                }

                faceShadowMaterial = new Material(shader)
                {
                    name = "Z Face Shadow (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                faceShadowMaterial.SetColor("_Color", Color.white);
            }

            if (faceShadowTexture == texture)
                return;

            faceShadowTexture = texture;
            SetMaterialDirty();
        }

        protected override void OnDestroy()
        {
            if (faceShadowMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(faceShadowMaterial);
                else
                    DestroyImmediate(faceShadowMaterial);
                faceShadowMaterial = null;
            }
            base.OnDestroy();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (kind == ZEmotionAccentKind.None || intensity <= 0.0001f || !projection.IsValid)
                return;

            switch (kind)
            {
                case ZEmotionAccentKind.Shy:
                    DrawShy(vertexHelper);
                    break;
                case ZEmotionAccentKind.Angry:
                    DrawAngry(vertexHelper);
                    break;
                case ZEmotionAccentKind.Curious:
                    DrawCurious(vertexHelper);
                    break;
                case ZEmotionAccentKind.Surprise:
                    DrawSurprise(vertexHelper);
                    break;
                case ZEmotionAccentKind.Startle:
                    DrawStartle(vertexHelper);
                    break;
                case ZEmotionAccentKind.Gloom:
                    DrawGloom(vertexHelper);
                    break;
                case ZEmotionAccentKind.Sorrow:
                    DrawSorrow(vertexHelper);
                    break;
                case ZEmotionAccentKind.Cry:
                    DrawCry(vertexHelper);
                    break;
                case ZEmotionAccentKind.Bawl:
                    DrawBawl(vertexHelper);
                    break;
            }
        }

        private void DrawShy(VertexHelper vh)
        {
            // Keep blush close to the lower eye line. The compact radii avoid
            // turning the cheeks into large flat disks over the face.
            var left = projection.Along(projection.LeftEye, -0.005f, -0.070f);
            var right = projection.Along(projection.RightEye, 0.005f, -0.070f);
            AddSoftEllipse(vh, left, projection.Right, projection.Up,
                projection.Width * 0.120f, projection.Height * 0.065f, ShyPink);
            AddSoftEllipse(vh, right, projection.Right, projection.Up,
                projection.Width * 0.120f, projection.Height * 0.065f, ShyPink);

            var slashWidth = projection.Width * 0.012f;
            AddRibbon(vh,
                left + projection.Right * (projection.Width * -0.050f) + projection.Up * (projection.Height * 0.003f),
                left + projection.Right * (projection.Width * -0.020f) + projection.Up * (projection.Height * 0.017f),
                slashWidth,
                slashWidth * 0.72f,
                ShyHighlight);
            AddRibbon(vh,
                right + projection.Right * (projection.Width * 0.020f) + projection.Up * (projection.Height * 0.017f),
                right + projection.Right * (projection.Width * 0.050f) + projection.Up * (projection.Height * 0.003f),
                slashWidth * 0.72f,
                slashWidth,
                ShyHighlight);
        }

        private void DrawAngry(VertexHelper vh)
        {
            // Four independent curved L/bracket strokes read as a comic anger
            // mark while leaving the middle empty. It sits at the left
            // forehead corner so it does not compete with the moon hair clip.
            var center = projection.FacePoint(-0.38f, 0.27f);
            var x = projection.Width * 0.095f;
            var y = projection.Height * 0.095f;
            var thick = projection.Height * 0.022f;
            AddAngryArc(vh,
                center + projection.Right * -x + projection.Up * (y * 0.20f),
                center + projection.Right * (-x * 0.32f) + projection.Up * (y * 0.20f),
                center + projection.Right * (-x * 0.32f) + projection.Up * y,
                thick);
            AddAngryArc(vh,
                center + projection.Right * (x * 0.32f) + projection.Up * y,
                center + projection.Right * (x * 0.32f) + projection.Up * (y * 0.20f),
                center + projection.Right * x + projection.Up * (y * 0.20f),
                thick);
            AddAngryArc(vh,
                center + projection.Right * x + projection.Up * (y * -0.20f),
                center + projection.Right * (x * 0.32f) + projection.Up * (y * -0.20f),
                center + projection.Right * (x * 0.32f) + projection.Up * -y,
                thick);
            AddAngryArc(vh,
                center + projection.Right * (-x * 0.32f) + projection.Up * -y,
                center + projection.Right * (-x * 0.32f) + projection.Up * (y * -0.20f),
                center + projection.Right * -x + projection.Up * (y * -0.20f),
                thick);
        }

        private void DrawCurious(VertexHelper vh)
        {
            // One open standard '?' contour: upper half-round, right bend,
            // short middle stem, then a separate dot. It never closes back
            // into the upper loop and does not depend on a font fallback.
            var center = projection.FacePoint(0.95f, 0.35f);
            var points = new Vector2[17];
            var p0 = projection.Along(center, -0.075f, 0.075f);
            var p1 = projection.Along(center, -0.015f, 0.145f);
            var p2 = projection.Along(center, 0.070f, 0.100f);
            var p3 = projection.Along(center, 0.035f, 0.005f);
            var p4 = projection.Along(center, 0.000f, -0.070f);
            Cubic(p0, projection.Along(center, -0.075f, 0.120f),
                projection.Along(center, -0.050f, 0.145f), p1, points, 0);
            Cubic(p1, projection.Along(center, 0.035f, 0.150f),
                projection.Along(center, 0.080f, 0.140f), p2, points, 4);
            Cubic(p2, projection.Along(center, 0.085f, 0.040f),
                projection.Along(center, 0.020f, 0.045f), p3, points, 8);
            Cubic(p3, projection.Along(center, 0.020f, -0.015f),
                projection.Along(center, 0.000f, -0.035f), p4, points, 12);

            var stroke = projection.Height * 0.021f;
            for (var i = 0; i < points.Length - 1; i++)
            {
                AddRibbon(vh, points[i], points[i + 1], stroke, stroke * 0.92f, CuriousPurple);
            }

            var dot = projection.Along(center, 0.000f, -0.145f);
            AddCircle(vh, dot, projection.Height * 0.022f, projection.Right, projection.Up, CuriousPurple);
            AddCircle(vh, dot + projection.Up * (projection.Height * 0.006f),
                projection.Height * 0.010f, projection.Right, projection.Up, CuriousHighlight);
        }

        private void DrawSurprise(VertexHelper vh)
        {
            // Keep the mark outside the character's screen-left silhouette.
            // This leaves the moon hair ornament, eyes and UI clear.
            const float scale = 1.5f;
            var pulse = 0.96f + Mathf.Sin(animationTime * 8.0f) * 0.04f;
            var center = projection.FacePoint(-0.80f, 0.40f);
            var stemTop = projection.Along(center, 0f, 0.115f * scale * pulse);
            var stemBottom = projection.Along(center, 0f, -0.035f * scale * pulse);
            var stemWidth = projection.Height * 0.026f * scale;
            AddRibbon(vh, stemTop, stemBottom, stemWidth * 1.25f, stemWidth * 1.05f, SurpriseGold);
            AddRibbon(vh, stemTop + projection.Right * (projection.Width * 0.006f),
                stemBottom + projection.Right * (projection.Width * 0.006f),
                stemWidth * 0.30f, stemWidth * 0.24f, SurpriseHighlight);
            AddCircle(vh,
                projection.Along(center, 0f, -0.090f * scale * pulse),
                projection.Height * 0.024f * scale,
                projection.Right,
                projection.Up,
                SurpriseGold);
            AddCircle(vh,
                projection.Along(center, 0.004f, -0.084f * scale * pulse),
                projection.Height * 0.009f * scale,
                projection.Right,
                projection.Up,
                SurpriseHighlight);
        }

        private void DrawStartle(VertexHelper vh)
        {
            // A short yellow zig-zag sits outside the upper-left silhouette;
            // it reads as a quick jolt instead of a lightning bolt on the face.
            const float scale = 1.5f;
            var pulse = 0.94f + Mathf.Sin(animationTime * 13.0f) * 0.06f;
            var center = projection.FacePoint(-0.72f, 0.50f);
            var points = new[]
            {
                projection.Along(center, -0.090f * scale, 0.105f * scale * pulse),
                projection.Along(center, -0.030f * scale, 0.040f * scale * pulse),
                projection.Along(center, -0.095f * scale, 0.025f * scale * pulse),
                projection.Along(center, -0.018f * scale, -0.055f * scale * pulse),
                projection.Along(center, -0.070f * scale, -0.125f * scale * pulse),
            };
            var stroke = projection.Height * 0.0275f;
            var shadowStroke = stroke * 1.22f;
            for (var i = 0; i < points.Length - 1; i++)
            {
                AddRibbon(vh, points[i], points[i + 1], shadowStroke, shadowStroke * 0.95f, StartleGold);
                AddRibbon(vh, points[i] + projection.Right * (projection.Width * 0.005f),
                    points[i + 1] + projection.Right * (projection.Width * 0.005f),
                    stroke * 0.42f, stroke * 0.32f, StartleHighlight);
            }
        }

        private void DrawGloom(VertexHelper vh)
        {
            if (!projection.FaceShadowReady || !FaceShadowMaterialReady ||
                projection.FaceShadowVertices == null || projection.FaceShadowUvs == null ||
                projection.FaceShadowIndices == null ||
                projection.FaceShadowVertices.Length != projection.FaceShadowUvs.Length)
            {
                return;
            }

            var vertexOffset = vh.currentVertCount;
            for (var i = 0; i < projection.FaceShadowVertices.Length; i++)
            {
                var color = GloomBlueGray;
                color.a *= intensity * GetGloomVertexAlpha(projection.FaceShadowVertices[i]);
                AddTexturedVertex(vh, projection.FaceShadowVertices[i], color, projection.FaceShadowUvs[i]);
            }

            for (var i = 0; i + 2 < projection.FaceShadowIndices.Length; i += 3)
            {
                var a = projection.FaceShadowIndices[i];
                var b = projection.FaceShadowIndices[i + 1];
                var c = projection.FaceShadowIndices[i + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= projection.FaceShadowVertices.Length ||
                    b >= projection.FaceShadowVertices.Length ||
                    c >= projection.FaceShadowVertices.Length)
                {
                    continue;
                }

                vh.AddTriangle(vertexOffset + a, vertexOffset + b, vertexOffset + c);
            }
        }

        private float GetGloomVertexAlpha(Vector2 position)
        {
            // Anchor the band to the eyes, not to the Face_Skin bbox center.
            // This keeps the overlay below the bangs and makes the lower edge
            // disappear before the nose instead of forming a blue cap.
            var eyeCenter = (projection.LeftEye + projection.RightEye) * 0.5f;
            var relative = position - eyeCenter;
            var y = Vector2.Dot(relative, projection.Up) / Mathf.Max(0.0001f, projection.Height);
            var lowerFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.16f, -0.01f, y));
            var upperFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.07f, 0.19f, y));
            var browFocus = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                0.015f, 0.10f, Mathf.Abs(y - 0.025f)));
            var alpha = lowerFade * upperFade * Mathf.Lerp(0.78f, 1f, browFocus);

            alpha *= ClearFeature(position, projection.Mouth,
                projection.Width * 0.17f, projection.Height * 0.075f);
            return Mathf.Clamp01(alpha);
        }

        private float ClearFeature(Vector2 position, Vector2 center, float radiusX, float radiusY)
        {
            var delta = position - center;
            var x = Vector2.Dot(delta, projection.Right) / Mathf.Max(0.0001f, radiusX);
            var y = Vector2.Dot(delta, projection.Up) / Mathf.Max(0.0001f, radiusY);
            var ellipseDistance = Mathf.Sqrt(x * x + y * y);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1.16f, ellipseDistance));
        }

        private void DrawSorrow(VertexHelper vh)
        {
            // Sorrow is only a thin, short wet glint at the actual lower-lid
            // UV anchors. It has no drop and no cheek trail, unlike Cry.
            if (projection.LeftTearReady)
                DrawWetGlint(vh, projection.LeftTear, projection.LeftGlintA, projection.LeftGlintB);
            if (projection.RightTearReady)
                DrawWetGlint(vh, projection.RightTear, projection.RightGlintA, projection.RightGlintB);
        }

        private void DrawWetGlint(VertexHelper vh, Vector2 first, Vector2 second, Vector2 third)
        {
            var length = Vector2.Distance(first, third);
            var width = Mathf.Clamp(length * 0.20f, projection.Height * 0.0035f, projection.Height * 0.010f);
            AddRibbon(vh, first, second, width, width * 0.82f, TearCyan);
            AddRibbon(vh, second, third, width * 0.82f, width * 0.55f, TearCyan);
        }

        private void DrawCry(VertexHelper vh)
        {
            DrawCrySide(vh, projection.LeftOuterTear, projection.LeftOuterTearReady, -1f);
            DrawCrySide(vh, projection.RightOuterTear, projection.RightOuterTearReady, 1f);
        }

        private void DrawCrySide(VertexHelper vh, Vector2 anchor, bool ready, float horizontalDirection)
        {
            if (!ready)
                return;

            var dropWidth = projection.Width * 0.035f;
            var dropHeight = projection.Height * 0.065f;
            // AddTearDrop's top tip is at center + up * height * .52. Move
            // its center so that the tip touches the UV-derived eye anchor.
            AddTearDrop(vh, anchor - projection.Up * (dropHeight * 0.52f),
                projection.Right, projection.Up, dropWidth, dropHeight, TearCyan);

            var end = anchor + projection.Right * (projection.Width * 0.030f * horizontalDirection) +
                projection.Up * (projection.Height * -0.250f);
            AddRibbon(vh, anchor, end,
                projection.Height * 0.014f, projection.Height * 0.006f, TearCyan);

            var highlightStart = anchor + projection.Right * (projection.Width * 0.004f) +
                projection.Up * (projection.Height * -0.010f);
            var highlightEnd = anchor + projection.Right * (projection.Width * 0.004f) +
                projection.Up * (projection.Height * -0.050f);
            AddRibbon(vh, highlightStart, highlightEnd,
                projection.Height * 0.004f, projection.Height * 0.002f, TearHighlight);

            var endDropWidth = projection.Width * 0.026f;
            var endDropHeight = projection.Height * 0.045f;
            AddTearDrop(vh, end - projection.Up * (endDropHeight * 0.52f),
                projection.Right, projection.Up, endDropWidth, endDropHeight, TearCyan);
        }

        private void DrawBawl(VertexHelper vh)
        {
            DrawBawlSide(vh, projection.LeftOuterTear, projection.LeftOuterTearReady, -1f);
            DrawBawlSide(vh, projection.RightOuterTear, projection.RightOuterTearReady, 1f);
        }

        private void DrawBawlSide(VertexHelper vh, Vector2 anchor, bool ready, float horizontalDirection)
        {
            if (!ready)
                return;

            // The stream bows strongly away from the nose while staying on
            // the outer cheek. Keep the first control almost level with the
            // eye so the flow leaves the outer corner before dropping.
            var p0 = anchor;
            var p1 = anchor + projection.Right * (projection.Width * 0.20f * horizontalDirection) +
                projection.Up * (projection.Height * -0.06f);
            var p2 = anchor + projection.Right * (projection.Width * 0.27f * horizontalDirection) +
                projection.Up * (projection.Height * -0.35f);
            var p3 = anchor + projection.Right * (projection.Width * 0.27f * horizontalDirection) +
                projection.Up * (projection.Height * -0.60f);

            AddBezierRibbon(vh, p0, p1, p2, p3,
                projection.Width * 0.015f,
                projection.Width * 0.096f,
                projection.Width * 0.088f,
                BawlCyan);

            var highlightOffset = projection.Right * (projection.Width * -0.018f * horizontalDirection) +
                projection.Up * (projection.Height * 0.008f);
            AddBezierRibbon(vh,
                p0,
                p1 + highlightOffset,
                p2 + highlightOffset,
                p3 + highlightOffset,
                projection.Width * 0.004f,
                projection.Width * 0.010f,
                projection.Width * 0.003f,
                BawlHighlight);

            var pulse = 0.92f + Mathf.Sin(animationTime * 5.5f + horizontalDirection) * 0.08f;
            var puddleCenter = p3 - projection.Up * (projection.Height * 0.030f);
            AddSoftEllipse(vh,
                puddleCenter,
                projection.Right,
                projection.Up,
                projection.Width * 0.070f,
                projection.Height * 0.030f,
                BawlCyan);

            // A few round droplets make the bottom read as a small splash,
            // rather than a second pointed tear or a diamond cap.
            AddCircle(vh,
                puddleCenter + projection.Right * (projection.Width * 0.085f * horizontalDirection * pulse) +
                projection.Up * (projection.Height * 0.040f),
                projection.Width * 0.014f,
                projection.Right,
                projection.Up,
                BawlHighlight);
            AddCircle(vh,
                puddleCenter + projection.Right * (projection.Width * 0.040f * horizontalDirection) +
                projection.Up * (projection.Height * 0.060f * pulse),
                projection.Width * 0.010f,
                projection.Right,
                projection.Up,
                BawlCyan);
            AddCircle(vh,
                puddleCenter + projection.Right * (projection.Width * 0.075f * horizontalDirection * pulse) -
                projection.Up * (projection.Height * 0.038f),
                projection.Width * 0.012f,
                projection.Right,
                projection.Up,
                BawlCyan);
            AddCircle(vh,
                puddleCenter - projection.Right * (projection.Width * 0.030f * horizontalDirection) +
                projection.Up * (projection.Height * 0.020f),
                projection.Width * 0.008f,
                projection.Right,
                projection.Up,
                BawlHighlight);
        }

        private Color Faded(Color color)
        {
            color.a *= intensity;
            return color;
        }

        private void AddSoftEllipse(
            VertexHelper vh,
            Vector2 center,
            Vector2 right,
            Vector2 up,
            float radiusX,
            float radiusY,
            Color color)
        {
            const int segments = 24;
            var centerColor = Faded(color);
            centerColor.a *= 0.85f;
            var centerIndex = vh.currentVertCount;
            AddVertex(vh, center, centerColor);
            var outerStart = vh.currentVertCount;
            var outerColor = Faded(color);
            outerColor.a = 0f;
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var point = center + right * (Mathf.Cos(angle) * radiusX) + up * (Mathf.Sin(angle) * radiusY);
                AddVertex(vh, point, outerColor);
            }

            for (var i = 0; i < segments; i++)
            {
                var next = outerStart + ((i + 1) % segments);
                vh.AddTriangle(centerIndex, outerStart + i, next);
            }
        }

        private void AddCircle(
            VertexHelper vh,
            Vector2 center,
            float radius,
            Vector2 right,
            Vector2 up,
            Color color)
        {
            const int segments = 16;
            var centerIndex = vh.currentVertCount;
            AddVertex(vh, center, Faded(color));
            var outerStart = vh.currentVertCount;
            var outerColor = Faded(color);
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var point = center + right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius);
                AddVertex(vh, point, outerColor);
            }

            for (var i = 0; i < segments; i++)
                vh.AddTriangle(centerIndex, outerStart + i, outerStart + ((i + 1) % segments));
        }

        private void AddTearDrop(
            VertexHelper vh,
            Vector2 center,
            Vector2 right,
            Vector2 up,
            float width,
            float height,
            Color color)
        {
            var top = center + up * (height * 0.52f);
            var bottom = center - up * (height * 0.58f);
            var left = center - right * (width * 0.50f) + up * (height * 0.04f);
            var rightPoint = center + right * (width * 0.50f) + up * (height * 0.04f);
            var index = vh.currentVertCount;
            AddVertex(vh, center + up * (height * 0.02f), Faded(color));
            AddVertex(vh, top, Faded(color));
            AddVertex(vh, rightPoint, Faded(color));
            AddVertex(vh, bottom, Faded(color));
            AddVertex(vh, left, Faded(color));
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
            vh.AddTriangle(index, index + 3, index + 4);
            vh.AddTriangle(index, index + 4, index + 1);
        }

        private void AddAngryArc(
            VertexHelper vh,
            Vector2 start,
            Vector2 bend,
            Vector2 end,
            float width)
        {
            var shadowWidth = width * 1.15f;
            const int steps = 8;
            var previous = start;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                var inverse = 1f - t;
                var point = inverse * inverse * start +
                            2f * inverse * t * bend +
                            t * t * end;
                AddRibbon(vh, previous, point, shadowWidth, shadowWidth, AngryShadow);
                previous = point;
            }

            previous = start;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                var inverse = 1f - t;
                var point = inverse * inverse * start +
                            2f * inverse * t * bend +
                            t * t * end;
                AddRibbon(vh, previous, point, width, width, AngryBrick);
                previous = point;
            }
        }

        private void AddBezierRibbon(
            VertexHelper vh,
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            float startWidth,
            float peakWidth,
            float endWidth,
            Color color)
        {
            const int steps = 10;
            var points = new Vector2[steps + 1];
            var widths = new float[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var inverse = 1f - t;
                points[i] = inverse * inverse * inverse * p0 +
                    3f * inverse * inverse * t * p1 +
                    3f * inverse * t * t * p2 +
                    t * t * t * p3;
                widths[i] = t <= 0.5f
                    ? Mathf.Lerp(startWidth, peakWidth, t * 2f)
                    : Mathf.Lerp(peakWidth, endWidth, (t - 0.5f) * 2f);
            }

            var left = new int[steps + 1];
            var right = new int[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var previous = points[Mathf.Max(0, i - 1)];
                var next = points[Mathf.Min(steps, i + 1)];
                var tangent = next - previous;
                if (tangent.sqrMagnitude < 0.000001f)
                    tangent = Vector2.right;
                var normal = new Vector2(-tangent.y, tangent.x).normalized;
                var normalized = i / (float)steps;
                var alpha = normalized <= 0.5f
                    ? Mathf.Lerp(0.92f, 1f, normalized * 2f)
                    : Mathf.Lerp(1f, 0.86f, (normalized - 0.5f) * 2f);
                var vertexColor = Faded(color);
                vertexColor.a *= alpha;
                var halfWidth = widths[i] * 0.5f;
                left[i] = vh.currentVertCount;
                AddVertex(vh, points[i] + normal * halfWidth, vertexColor);
                right[i] = vh.currentVertCount;
                AddVertex(vh, points[i] - normal * halfWidth, vertexColor);
            }

            // Reuse the two vertices at every join. This avoids the repeated
            // per-segment alpha drop that made the old ribbon look striped.
            for (var i = 0; i < steps; i++)
            {
                vh.AddTriangle(left[i], right[i], right[i + 1]);
                vh.AddTriangle(left[i], right[i + 1], left[i + 1]);
            }
        }

        private void AddRibbon(
            VertexHelper vh,
            Vector2 start,
            Vector2 end,
            float startWidth,
            float endWidth,
            Color color)
        {
            var delta = end - start;
            if (delta.sqrMagnitude < 0.000001f)
                return;
            var normal = new Vector2(-delta.y, delta.x).normalized;
            var index = vh.currentVertCount;
            var startColor = Faded(color);
            var endColor = startColor;
            endColor.a *= 0.72f;
            AddVertex(vh, start + normal * (startWidth * 0.5f), startColor);
            AddVertex(vh, start - normal * (startWidth * 0.5f), startColor);
            AddVertex(vh, end - normal * (endWidth * 0.5f), endColor);
            AddVertex(vh, end + normal * (endWidth * 0.5f), endColor);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private void AddVertex(VertexHelper vh, Vector2 position, Color color)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = Vector2.zero;
            vh.AddVert(vertex);
        }

        private void AddTexturedVertex(VertexHelper vh, Vector2 position, Color color, Vector2 uv)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = uv;
            vh.AddVert(vertex);
        }

        private static void Cubic(
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            Vector2[] output,
            int startIndex)
        {
            const int segmentCount = 4;
            for (var i = 0; i <= segmentCount; i++)
            {
                var t = i / (float)segmentCount;
                var oneMinus = 1f - t;
                output[startIndex + i] =
                    oneMinus * oneMinus * oneMinus * p0 +
                    3f * oneMinus * oneMinus * t * p1 +
                    3f * oneMinus * t * t * p2 +
                    t * t * t * p3;
            }
        }
    }
}
