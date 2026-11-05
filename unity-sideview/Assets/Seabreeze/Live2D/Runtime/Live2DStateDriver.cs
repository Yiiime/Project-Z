using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>Applies supplied exp3 data and explicit procedural idle/talk before Cubism physics. No invented motion files.</summary>
    public sealed class Live2DStateDriver : MonoBehaviour, ICubismUpdatable
    {
        readonly Dictionary<string, CubismParameter> parameters = new Dictionary<string, CubismParameter>();
        readonly Dictionary<string, float> targets = new Dictionary<string, float>();
        readonly Dictionary<string, float> currentValues = new Dictionary<string, float>();
        readonly HashSet<string> expressionParameters = new HashSet<string>();
        Live2DModelProfile profile;
        bool talking;
        float phase;
        float mouthValue;
        public bool HasUpdateController { get; set; }
        public int ExecutionOrder => 750;
        public bool NeedsUpdateOnEditing => false;
        public void Configure(Live2DModelProfile profile)
        {
            this.profile = profile;
            var model = GetComponent<CubismModel>();
            foreach (var parameter in model.Parameters) parameters[parameter.Id] = parameter;
            foreach (var binding in profile.expressions)
            {
                if (binding == null || binding.expression == null) continue;
                foreach (var parameter in binding.expression.Parameters) expressionParameters.Add(parameter.Id);
            }
            foreach (var id in expressionParameters)
                if (parameters.TryGetValue(id, out var parameter)) currentValues[id] = parameter.Value;
            SetState(profile.defaultEmotion, false);
        }
        public void SetState(string emotion, bool isTalking)
        {
            talking = isTalking;
            targets.Clear();
            foreach (var id in expressionParameters)
                if (parameters.TryGetValue(id, out var p)) targets[id] = p.DefaultValue;
            var expression = profile.FindExpression(emotion);
            if (expression == null) return;
            foreach (var entry in expression.Parameters)
            {
                if (!parameters.TryGetValue(entry.Id, out var p)) continue;
                float value = entry.Value;
                if (entry.Blend == CubismParameterBlendMode.Additive) value += p.DefaultValue;
                else if (entry.Blend == CubismParameterBlendMode.Multiply) value *= p.DefaultValue;
                targets[entry.Id] = value;
            }
        }
        public void OnLateUpdate()
        {
            if (!enabled || profile == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f); phase += dt;
            float ease = 1 - Mathf.Exp(-dt / Mathf.Max(.02f, profile.expressionFadeSeconds));
            foreach (var pair in targets)
            {
                var p = parameters[pair.Key];
                float current = currentValues.TryGetValue(pair.Key, out var value) ? value : p.DefaultValue;
                current = Mathf.Lerp(current, Mathf.Clamp(pair.Value, p.MinimumValue, p.MaximumValue), ease);
                currentValues[pair.Key] = current;
                p.Value = current;
            }
            if (profile.proceduralIdle)
            {
                Assign(profile.breathParameter, .5f + .42f * Mathf.Sin(phase * 1.65f));
                float blinkPhase = phase % 4.3f;
                float openness = blinkPhase < .16f ? Mathf.Abs(blinkPhase / .08f - 1) : 1;
                Assign(profile.leftEyeParameter, openness); Assign(profile.rightEyeParameter, openness);
                Assign("ParamAngleZ", Mathf.Sin(phase * .73f) * 1.2f);
                Assign("ParamBodyAngleZ", Mathf.Sin(phase * .57f) * .7f);
            }
            if (profile.proceduralTalk)
            {
                bool fixedMouth = targets.TryGetValue("ParamMouthVariant", out var variant) && Mathf.Abs(variant) > .1f;
                float mouth = talking && !fixedMouth ? .10f + .52f * Mathf.Abs(Mathf.Sin(phase * 11.7f) * Mathf.Sin(phase * 5.3f)) : 0;
                mouthValue = Mathf.Lerp(mouthValue, mouth, 1 - Mathf.Exp(-dt * 18));
                if (parameters.TryGetValue(profile.mouthParameter, out var p)) p.Value = mouthValue;
            }
        }
        void Assign(string id, float value)
        {
            if (parameters.TryGetValue(id, out var p)) p.Value = Mathf.Clamp(value, p.MinimumValue, p.MaximumValue);
        }
        void LateUpdate() { if (!HasUpdateController) OnLateUpdate(); }
    }
}
