using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>
    /// Keeps the authored question and neutral mouth layers in step with the same
    /// Cubism Core frame that supplied their drawable data. Cubism's URP
    /// renderer can briefly leave the front mesh using the previous visibility
    /// flag while its double buffer swaps; this instance-only guard closes that
    /// one-frame gap without touching eyes, FX, or the other mouth variants.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(2000)]
    public sealed class QingyeR61VisibilityGuard : MonoBehaviour
    {
        // These are the original mouth layers used by the game-facing
        // narrow-mouth path. Do not widen this set to every drawable whose name
        // contains "Mouth": smile, bawl, angry, and the other authored states
        // must keep Cubism's normal renderer path.
        static readonly HashSet<string> TargetIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Mouth_Closed",
            "Mouth_Open",
            "Mouth_Frown",
            "QYR6QuestionSpeak"
        };

        sealed class Target
        {
            public CubismRenderer Renderer;
            public int DrawableIndex;
            public bool Desired;
            public float Opacity;
            public Vector3[] Positions;
            public bool Pending;
        }

        readonly List<Target> targets = new List<Target>();
        CubismModel model;
        CubismParameter questionParameter;
        float guardUntil;
        bool questionSeen;
        bool warnedMissingTargets;

        void Start()
        {
            model = GetComponentInChildren<CubismModel>(true);
            if (model == null) return;

            foreach (var parameter in model.Parameters)
            {
                if (parameter != null && string.Equals(parameter.Id, "ParamQYR6Question", StringComparison.Ordinal))
                {
                    questionParameter = parameter;
                    break;
                }
            }

            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            var drawables = model.Drawables;
            for (var i = 0; i < drawables.Length; i++)
                if (drawables[i] != null) indexById[drawables[i].Id] = i;

            foreach (var renderer in GetComponentsInChildren<CubismRenderer>(true))
            {
                var drawable = renderer != null ? renderer.Drawable : null;
                if (drawable == null || !TargetIds.Contains(drawable.Id)) continue;
                if (!indexById.TryGetValue(drawable.Id, out var drawableIndex)) continue;
                targets.Add(new Target
                {
                    Renderer = renderer,
                    DrawableIndex = drawableIndex,
                    Desired = renderer.MeshRenderer != null && renderer.MeshRenderer.enabled
                });
            }

            if (questionParameter == null)
                Debug.LogWarning("[QingyeR61VisibilityGuard] ParamQYR6Question is missing; guard disabled.", this);
            if (targets.Count != TargetIds.Count && !warnedMissingTargets)
            {
                warnedMissingTargets = true;
                Debug.LogWarning("[QingyeR61VisibilityGuard] Missing authored mouth layers; found " + targets.Count + ".", this);
            }

            model.OnDynamicDrawableData += OnDynamicDrawableData;
        }

        void OnDestroy()
        {
            if (model != null) model.OnDynamicDrawableData -= OnDynamicDrawableData;
        }

        void OnDynamicDrawableData(CubismModel sender, CubismDynamicDrawableData[] data)
        {
            if (sender != model) return;

            var questionActive = questionParameter != null && questionParameter.Value > .0001f;
            if (questionActive)
            {
                questionSeen = true;
                // Keep the outgoing reset covered. The Core update and the
                // renderer consume different ends of the one-frame buffer.
                guardUntil = Time.unscaledTime + 2f;
            }
            else if (questionSeen)
            {
                questionSeen = false;
                guardUntil = Mathf.Max(guardUntil, Time.unscaledTime + .25f);
            }

            if (!questionActive && Time.unscaledTime >= guardUntil) return;
            foreach (var target in targets)
            {
                if (target.DrawableIndex < 0 || target.DrawableIndex >= data.Length) continue;
                var current = data[target.DrawableIndex];
                target.Desired = current.IsVisible && current.Opacity > .0001f;
                target.Opacity = current.Opacity;
                target.Positions = target.Desired ? target.Renderer.Drawable.VertexPositions : null;
                target.Pending = true;
            }
        }

        void LateUpdate()
        {
            var armed = questionSeen || Time.unscaledTime <= guardUntil;
            foreach (var target in targets)
            {
                if (target.Renderer == null || target.Renderer.MeshRenderer == null || !target.Pending)
                    continue;
                if (!armed)
                {
                    target.Pending = false;
                    continue;
                }

                // Apply the current Core positions, vertex colors, and enabled
                // flag to the front mesh after CubismRenderController ran.
                if (target.Desired && target.Positions != null && target.Renderer.Mesh != null
                    && target.Positions.Length == target.Renderer.Mesh.vertexCount)
                {
                    var mesh = target.Renderer.Mesh;
                    mesh.vertices = target.Positions;
                    mesh.RecalculateBounds();
                    var colors = mesh.colors;
                    if (colors == null || colors.Length != mesh.vertexCount)
                        colors = new Color[mesh.vertexCount];
                    var color = target.Renderer.Color;
                    color.a *= target.Opacity;
                    for (var i = 0; i < colors.Length; i++) colors[i] = color;
                    mesh.colors = colors;
                }

                target.Renderer.MeshRenderer.enabled = target.Desired;
                target.Pending = false;
            }
        }
    }
}
