using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>
    /// Applies supplied exp3 data and explicit procedural idle/talk before Cubism physics.
    /// Native Z speech is preferred when present; older models keep the legacy mouth fallback.
    /// </summary>
    public sealed class Live2DStateDriver : MonoBehaviour, ICubismUpdatable
    {
        const string NativeSpeechParameter = "ParamZSpeech";
        const string LegacyMouthParameter = "ParamMouthOpenY";

        static readonly string[] NativeMotionParameters =
        {
            "ParamZQuietMouth", "ParamZSobMouth", "ParamZSobSighMouth",
            "ParamZRoundMouth", "ParamZStarry", "ParamZTearQuiet", "ParamZTearSob"
        };

        readonly Dictionary<string, CubismParameter> parameters = new Dictionary<string, CubismParameter>();
        readonly Dictionary<string, float> targets = new Dictionary<string, float>();
        readonly Dictionary<string, float> currentValues = new Dictionary<string, float>();
        readonly HashSet<string> expressionParameters = new HashSet<string>();
        Live2DModelProfile profile;
        bool talking;
        bool portraitMotionActive;
        float phase;
        float mouthValue;

        public bool HasUpdateController { get; set; }
        public int ExecutionOrder => 750;
        public bool NeedsUpdateOnEditing => false;

        public void Configure(Live2DModelProfile profile)
        {
            this.profile = profile;
            parameters.Clear();
            targets.Clear();
            currentValues.Clear();
            expressionParameters.Clear();

            var model = GetComponent<CubismModel>();
            if (model == null || profile == null) return;
            foreach (var parameter in model.Parameters)
            {
                if (parameter != null && !string.IsNullOrEmpty(parameter.Id)) parameters[parameter.Id] = parameter;
            }
            if (profile.expressions != null)
            {
                foreach (var binding in profile.expressions)
                {
                    if (binding == null || binding.expression == null || binding.expression.Parameters == null) continue;
                    foreach (var parameter in binding.expression.Parameters)
                    {
                        if (!string.IsNullOrEmpty(parameter.Id)) expressionParameters.Add(parameter.Id);
                    }
                }
            }
            foreach (var id in expressionParameters)
            {
                if (parameters.TryGetValue(id, out var parameter)) currentValues[id] = parameter.Value;
            }
            SetState(profile.defaultEmotion, false);
        }

        public void SetState(string emotion, bool isTalking)
        {
            if (profile == null) return;
            portraitMotionActive = false;
            talking = isTalking;
            targets.Clear();
            // Keep the current authored face as the smoothing source when only
            // the speaking flag changes. Resetting ParamMouthForm here would
            // flash a smile back to neutral for one frame before its target
            // returns to the same expression.
            ResetNativeMotionParameters(false);
            foreach (var id in expressionParameters)
            {
                if (parameters.TryGetValue(id, out var p)) targets[id] = p.DefaultValue;
            }

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

        /// <summary>
        /// Starts one of Z's four authored portrait motions. Animator owns the motion3 curves;
        /// the view refuses to trigger this unless the matching state is installed.
        /// </summary>
        public bool SetPortraitMotion(string key)
        {
            key = NormalizePortraitMotion(key);
            if (key == null || profile == null || parameters.Count == 0) return false;

            portraitMotionActive = true;
            talking = false;
            targets.Clear();
            ResetNativeMotionParameters(true);
            return true;
        }

        /// <summary>Stops a direct portrait motion and restores authored parameter defaults.</summary>
        public void ClearPortraitMotion()
        {
            portraitMotionActive = false;
            ResetNativeMotionParameters(true);
        }

        public void OnLateUpdate()
        {
            if (!enabled || profile == null) return;
            float dt = Mathf.Min(Mathf.Max(0f, Time.unscaledDeltaTime), .1f);
            phase += dt;

            if (!portraitMotionActive)
            {
                float ease = 1 - Mathf.Exp(-dt / Mathf.Max(.02f, profile.expressionFadeSeconds));
                foreach (var pair in targets)
                {
                    if (!parameters.TryGetValue(pair.Key, out var p)) continue;
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
                    Assign(profile.leftEyeParameter, openness);
                    Assign(profile.rightEyeParameter, openness);
                    Assign("ParamAngleZ", Mathf.Sin(phase * .73f) * 1.2f);
                    Assign("ParamBodyAngleZ", Mathf.Sin(phase * .57f) * .7f);
                }

            }
            // The native speech layer can articulate Z's lines over the authored expression.
            if (profile.proceduralTalk && (!portraitMotionActive || talking))
                ApplySpeech(dt);
        }

        public void SetTalkingDuringMotion(bool value)
        {
            talking = value;
        }

        void ApplySpeech(float dt)
        {
            bool native = HasParameter(NativeSpeechParameter) && profile.mouthParameter == NativeSpeechParameter;
            // Legacy expressions with a non-zero mouth variant own their
            // fixed shape. R5's native ParamZSpeech path intentionally keeps
            // its existing layered speech behavior.
            bool fixedMouth = !native && targets.TryGetValue("ParamMouthVariant", out var variant) && Mathf.Abs(variant) > .1f;
            float target = talking && !fixedMouth
                ? .10f + .52f * Mathf.Abs(Mathf.Sin(phase * 11.7f) * Mathf.Sin(phase * 5.3f))
                : 0f;
            mouthValue = Mathf.Lerp(mouthValue, target, 1 - Mathf.Exp(-dt * (native ? 28f : 18f)));

            if (native)
            {
                // R5's native speech layer owns the mouth. Keep the old mouth channel at baseline
                // so ParamMouthOpenY cannot stack a second, incorrect shape on top of it.
                Assign(NativeSpeechParameter, mouthValue);
                Assign(LegacyMouthParameter, DefaultValue(LegacyMouthParameter));
            }
            else
            {
                Assign(profile.mouthParameter, mouthValue);
            }
        }

        void ResetNativeMotionParameters(bool resetAuthoredMouth)
        {
            if (profile == null || profile.mouthParameter != NativeSpeechParameter || !HasParameter(NativeSpeechParameter)) return;
            foreach (var id in NativeMotionParameters) SetImmediate(id, DefaultValue(id));
            SetImmediate(NativeSpeechParameter, DefaultValue(NativeSpeechParameter));
            SetImmediate(LegacyMouthParameter, DefaultValue(LegacyMouthParameter));
            if (profile != null && !string.Equals(profile.mouthParameter, LegacyMouthParameter, StringComparison.OrdinalIgnoreCase))
                SetImmediate(profile.mouthParameter, DefaultValue(profile.mouthParameter));
            if (resetAuthoredMouth)
            {
                SetImmediate("ParamMouthVariant", DefaultValue("ParamMouthVariant"));
                SetImmediate("ParamMouthForm", DefaultValue("ParamMouthForm"));
            }
            mouthValue = DefaultValue(NativeSpeechParameter);
        }

        void SetImmediate(string id, float value)
        {
            if (!HasParameter(id)) return;
            var p = parameters[id];
            value = Mathf.Clamp(value, p.MinimumValue, p.MaximumValue);
            currentValues[id] = value;
            p.Value = value;
        }

        float DefaultValue(string id)
        {
            return HasParameter(id) ? parameters[id].DefaultValue : 0f;
        }

        bool HasParameter(string id)
        {
            return !string.IsNullOrEmpty(id) && parameters.ContainsKey(id);
        }

        void Assign(string id, float value)
        {
            if (!HasParameter(id)) return;
            var p = parameters[id];
            p.Value = Mathf.Clamp(value, p.MinimumValue, p.MaximumValue);
        }

        static string NormalizePortraitMotion(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = key.Trim().ToLowerInvariant();
            if (key == "cry" || key == "bawl" || key == "surprise" || key == "starry_curious") return key;
            return null;
        }

        void LateUpdate() { if (!HasUpdateController) OnLateUpdate(); }
    }
}
