using System;
using UnityEngine;
using Live2D.Cubism.Framework.Expression;

namespace Seabreeze
{
    [CreateAssetMenu(menuName = "Seabreeze/Live2D Model Profile")]
    public sealed class Live2DModelProfile : ScriptableObject
    {
        public GameObject prefab;
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
        public RuntimeAnimatorController animatorController;
        public string idleState = "Idle";
        public string talkState = "Talk";
        public bool proceduralIdle = true;
        public bool proceduralTalk = true;
        public float expressionFadeSeconds = .24f;
        public string mouthParameter = "ParamMouthOpenY";
        public string breathParameter = "ParamBreath";
        public string leftEyeParameter = "ParamEyeLOpen";
        public string rightEyeParameter = "ParamEyeROpen";
        [TextArea] public string authoringNotes;

        [Serializable]
        public sealed class ExpressionBinding
        {
            public string emotion;
            public CubismExpressionData expression;
        }
        public CubismExpressionData FindExpression(string emotion)
        {
            foreach (var binding in expressions)
                if (binding != null && string.Equals(binding.emotion, emotion, StringComparison.OrdinalIgnoreCase))
                    return binding.expression;
            return null;
        }
    }
}
