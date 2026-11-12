using System;
using UnityEngine;
using Live2D.Cubism.Framework.Expression;

namespace Seabreeze
{
    [CreateAssetMenu(menuName = "Seabreeze/Live2D Model Profile")]
    public sealed class Live2DModelProfile : ScriptableObject
    {
        public GameObject prefab;
        [Tooltip("Optional UI material for the finished portrait. Leave empty for standard Canvas display.")]
        public Material portraitDisplayMaterial;
        public string characterId = "z";
        [Tooltip("Index appended to the current URP renderer list by Live2DProjectSetup.")]
        public int rendererIndex = 1;
        [Range(8, 31)] public int renderLayer = 30;
        public Vector3 modelPosition = Vector3.zero;
        public float modelScale = 1;
        public Vector2 cameraCenter = Vector2.zero;
        public float orthographicSize = 2;
        public bool autoFrame = true;
        [Range(.2f, 1.2f)] public float visibleHeight = .78f;
        [Range(0, 1)] public float verticalFocus = .62f;
        public Rect uvRect = new Rect(0, 0, 1, 1);
        [Range(256, 2048)] public int textureHeight = 1200;
        public string defaultEmotion = "neutral";
        public ExpressionBinding[] expressions = Array.Empty<ExpressionBinding>();
        public PortraitMotionBinding[] portraitMotions = Array.Empty<PortraitMotionBinding>();
        public RuntimeAnimatorController animatorController;
        public string idleState = "Idle";
        public string talkState = "Talk";
        public bool proceduralIdle = true;
        public bool proceduralTalk = true;
        public float expressionFadeSeconds = .24f;
        [Tooltip("Clear authored expression parameters to their model defaults when this profile changes expression state.")]
        public bool clearExpressionOnInterrupt;
        [Tooltip("Write the expression index and mix parameters directly instead of interpolating through other faces.")]
        public bool discreteExpressionParameters;
        public string expressionIndexParameter = "ParamExpression";
        public string expressionMixParameter = "ParamExpressionMix";
        public ExpressionTransition[] expressionTransitions = Array.Empty<ExpressionTransition>();
        public string mouthParameter = "ParamMouthOpenY";
        [Tooltip("Optional character-specific speech parameter used only while this profile is in its neutral dialogue state. Falls back to mouthParameter when the imported model does not contain it.")]
        public string neutralSpeechParameter = string.Empty;
        [Min(0f)] public float talkMouthAmplitude = 1f;
        [Tooltip("Allow speech to animate the model's authored mouth shape while an expression sets ParamMouthVariant.")]
        public bool allowSpeechWithMouthVariant;
        [Tooltip("When enabled, speech amplitudes move from the expression's authored mouth hold to a configured peak instead of replacing the expression mouth target.")]
        public bool speechAddsToExpressionTarget;
        [Range(0, 1)] public float speechPeak;
        [Range(0, 1)] public float speechRest = .10f;
        [Tooltip("Optional per-expression speech peaks. A zero value falls back to speechPeak.")]
        public SpeechExpression[] speechExpressions = Array.Empty<SpeechExpression>();
        [Tooltip("Expression to apply after the dry crying face has held for tearStartSeconds. The transition is controller-owned.")]
        public string tearExpression;
        [Min(0)] public float tearStartSeconds = 1.10f;
        public string breathParameter = "ParamBreath";
        public string leftEyeParameter = "ParamEyeLOpen";
        public string rightEyeParameter = "ParamEyeROpen";
        [TextArea] public string authoringNotes;

        [Serializable]
        public sealed class SpeechExpression
        {
            public string emotion;
            [Range(0, 1)] public float peak;
        }

        [Serializable]
        public sealed class PortraitMotionBinding
        {
            public string emotion;
            public AnimationClip clip;
        }

        public bool TryGetPortraitMotion(string key, out string state, out float seconds)
        {
            state = key?.Trim().ToLowerInvariant();
            seconds = 0f;
            if (string.IsNullOrEmpty(state)) return false;
            if (portraitMotions != null)
                foreach (var binding in portraitMotions)
                    if (binding?.clip != null && string.Equals(binding.emotion, state, StringComparison.OrdinalIgnoreCase))
                    {
                        state = binding.emotion;
                        seconds = binding.clip.length;
                        return seconds > 0f;
                    }

            // Older Z profiles store these states directly in their Animator controller.
            if (characterId != "z") return false;
            seconds = 3f;
            return state == "cry" || state == "bawl" || state == "surprise" || state == "starry_curious";
        }

        [Serializable]
        public sealed class ExpressionBinding
        {
            public string emotion;
            public CubismExpressionData expression;
        }

        [Serializable]
        public sealed class ExpressionTransition
        {
            public string finalEmotion;
            public string introEmotion;
            [Min(.01f)] public float introSeconds = .18f;
        }

        public CubismExpressionData FindExpression(string emotion)
        {
            string normalized = NormalizeKey(emotion);
            if (string.IsNullOrEmpty(normalized)) return null;
            foreach (var binding in expressions ?? Array.Empty<ExpressionBinding>())
                if (binding != null && string.Equals(RuntimeKey(binding.emotion), RuntimeKey(normalized), StringComparison.OrdinalIgnoreCase))
                    return binding.expression;
            return null;
        }

        public SpeechExpression FindSpeechExpression(string emotion)
        {
            if (speechExpressions == null || speechExpressions.Length == 0 || string.IsNullOrWhiteSpace(emotion)) return null;
            string key = RuntimeKey(emotion);
            foreach (var binding in speechExpressions)
                if (binding != null && binding.peak > .001f && string.Equals(RuntimeKey(binding.emotion), key, StringComparison.OrdinalIgnoreCase))
                    return binding;
            return null;
        }

        public float FindSpeechPeak(string emotion, float fallback)
        {
            if (string.IsNullOrEmpty(NormalizeKey(emotion))) return fallback;
            var binding = FindSpeechExpression(emotion);
            if (binding != null) return binding.peak;
            return speechPeak > .001f ? speechPeak : fallback;
        }

        string RuntimeKey(string value)
        {
            string normalized = NormalizeKey(value);
            if (!string.Equals(characterId, "zhengyi", StringComparison.OrdinalIgnoreCase)) return normalized;
            if (normalized.EndsWith("_six", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 4);
            switch (normalized)
            {
                case "question": return "puzzled";
                case "troubled": return "puzzled";
                case "starry":
                case "starry_curious": return "curious";
                case "blush": return "shy";
                case "cry":
                    return "bawl";
                default: return normalized;
            }
        }

        static string NormalizeKey(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace('-', '_');
        }

        public ExpressionTransition FindExpressionTransition(string finalEmotion)
        {
            foreach (var transition in expressionTransitions ?? Array.Empty<ExpressionTransition>())
                if (transition != null && string.Equals(transition.finalEmotion, finalEmotion, StringComparison.OrdinalIgnoreCase))
                    return transition;
            return null;
        }
    }
}
