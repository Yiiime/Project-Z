using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

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
        const string QingyeCharacterId = "xiaoxiaoqing";
        const float LiyuanTearRampSeconds = .25f;
        const float LiyuanBawlEyeCloseStartSeconds = .55f;
        const float LiyuanBawlEyeClosedSeconds = .70f;
        const float LiyuanBawlExpressionStartSeconds = .85f;
        const float LiyuanBawlExpressionHoldSeconds = 1.05f;
        const float LiyuanMouthOpenThreshold = .42f;
        const float LiyuanMouthCloseThreshold = .18f;
        const float LiyuanSpeechBeatSeconds = .72f;

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
        CubismExpressionData activeExpression;
        CubismExpressionData appliedExpression;
        Live2DModelProfile.ExpressionTransition activeTransition;
        float transitionElapsed;
        bool transitionRunning;
        bool talking;
        bool portraitMotionActive;
        string activeEmotion;
        float stateElapsed;
        bool tearApplied;
        string externalSpeechToken;
        float externalSpeechAmplitude;
        float phase;
        float mouthValue;
        float liyuanSpeechClock;
        bool liyuanMouthOpen;
        float liuyangSpeechClock;
        CubismRenderer[] zhengyiEffectRenderers = Array.Empty<CubismRenderer>();
        int effectGateFrames;

        public bool HasUpdateController { get; set; }
        public int ExecutionOrder => 750;
        public bool NeedsUpdateOnEditing => false;

        public void Configure(Live2DModelProfile profile)
        {
            this.profile = profile;
            phase = 0f;
            mouthValue = 0f;
            parameters.Clear();
            targets.Clear();
            currentValues.Clear();
            expressionParameters.Clear();
            activeExpression = null;
            appliedExpression = null;
            activeTransition = null;
            transitionElapsed = 0f;
            transitionRunning = false;
            activeEmotion = null;
            stateElapsed = 0f;
            tearApplied = false;
            externalSpeechToken = null;
            externalSpeechAmplitude = 0f;
            talking = false;
            portraitMotionActive = false;
            zhengyiEffectRenderers = Array.Empty<CubismRenderer>();
            effectGateFrames = 0;
            liuyangSpeechClock = 0f;
            liyuanSpeechClock = 0f;
            liyuanMouthOpen = false;

            var model = GetComponent<CubismModel>();
            if (model == null || profile == null) return;
            CacheZhengyiEffectRenderers();
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
            ResetToDefault();
            SetState(profile.defaultEmotion, false);
        }

        public void SetState(string emotion, bool isTalking)
        {
            if (profile == null) return;
            portraitMotionActive = false;
            if (!isTalking && !string.IsNullOrEmpty(externalSpeechToken)) CancelExternalSpeechDrive();
            bool wasTalking = talking;
            talking = isTalking;
            if (!isTalking || (!wasTalking && (IsLiyuanProfile() || IsLiuyangProfile())))
            {
                liyuanSpeechClock = 0f;
                liyuanMouthOpen = false;
                liuyangSpeechClock = 0f;
            }
            // Liting's profile maps the server's single bawl/sigh key to the
            // short authored intro bawl_dry/sigh_in before the final state.
            emotion = string.IsNullOrEmpty(emotion) ? profile.defaultEmotion : emotion;
            var expression = profile.FindExpression(emotion);
            // MemoryDialogueView submits the same emotion repeatedly while text types.
            // Do not restart a timed intro just because only the talking flag changed.
            if (activeExpression == expression && string.Equals(activeEmotion, emotion, StringComparison.OrdinalIgnoreCase)) return;

            if ((activeExpression != null || appliedExpression != null) && (IsZhengyiProfile() || IsLiuyangProfile()))
                ResetModelParametersImmediate();
            else if ((activeExpression != null || appliedExpression != null) && IsLiyuanProfile())
                ResetLiyuanTransientParametersImmediate();
            else if (profile.clearExpressionOnInterrupt && (activeExpression != null || appliedExpression != null))
                ClearExpressionParametersImmediate();
            CancelExternalSpeechDrive();
            ResetQingyeSpeechChannelsForState(emotion);
            activeExpression = expression;
            activeEmotion = emotion;
            stateElapsed = 0f;
            tearApplied = false;
            BeginEffectGate();
            ResetNativeMotionParameters(false);
            activeTransition = null;
            transitionElapsed = 0f;
            transitionRunning = false;

            if (expression != null)
            {
                var transition = profile.FindExpressionTransition(emotion);
                var intro = transition == null ? null : profile.FindExpression(transition.introEmotion);
                if (intro != null && intro != expression && transition.introSeconds > .001f)
                {
                    activeTransition = transition;
                    transitionRunning = true;
                    appliedExpression = intro;
                    ApplyExpression(intro);
                    ApplyCharacterExpressionPose();
                    return;
                }
            }

            appliedExpression = expression;
            ApplyExpression(expression);
            ApplyCharacterExpressionPose();
        }

        /// <summary>
        /// Clears every model parameter immediately. This is intentionally broader
        /// than the expression parameter set: c06 owns eyes, tears, mouth shape,
        /// gaze and the integer face selector on one line, so a hidden or
        /// interrupted portrait must not carry any of them into the next line.
        /// </summary>
        public void ResetToDefault()
        {
            if (profile == null) return;
            CancelExternalSpeechDrive();
            portraitMotionActive = false;
            talking = false;
            activeExpression = null;
            appliedExpression = null;
            activeEmotion = null;
            activeTransition = null;
            transitionElapsed = 0f;
            transitionRunning = false;
            stateElapsed = 0f;
            tearApplied = false;
            targets.Clear();
            BeginEffectGate();
            ResetModelParametersImmediate();
            activeEmotion = profile.defaultEmotion;
            activeExpression = profile.FindExpression(profile.defaultEmotion);
            appliedExpression = activeExpression;
            ApplyExpression(activeExpression);
            liuyangSpeechClock = 0f;
            liyuanSpeechClock = 0f;
            liyuanMouthOpen = false;
            if (HasConfiguredQingyeNeutralSpeech())
                mouthValue = DefaultValue(ResolveSpeechParameter(profile.defaultEmotion));
            ApplyCharacterExpressionPose();
        }

        /// <summary>Starts the explicit audio/phoneme speech drive for one line segment.</summary>
        public bool BeginSpeechDrive(string turnId, int segmentIndex)
        {
            string token = SpeechToken(turnId, segmentIndex);
            if (profile == null || !HasParameter(ResolveSpeechParameter()) || token == null) return false;
            if (!string.IsNullOrEmpty(externalSpeechToken) && externalSpeechToken != token)
                CancelExternalSpeechDrive();
            externalSpeechToken = token;
            externalSpeechAmplitude = 0f;
            talking = true;
            return true;
        }

        /// <summary>Writes a normalized 0..1 phoneme amplitude for the active segment.</summary>
        public bool SetSpeechAmplitude(string turnId, int segmentIndex, float amplitude)
        {
            string token = SpeechToken(turnId, segmentIndex);
            if (token == null || token != externalSpeechToken) return false;
            externalSpeechAmplitude = Mathf.Clamp01(amplitude);
            talking = true;
            return true;
        }

        /// <summary>Ends the explicit speech drive while preserving the current face hold.</summary>
        public bool EndSpeechDrive(string turnId, int segmentIndex)
        {
            string token = SpeechToken(turnId, segmentIndex);
            if (token == null || token != externalSpeechToken) return false;
            CancelExternalSpeechDrive();
            talking = false;
            return true;
        }

        /// <summary>Cancels a segment and rejects any late amplitude callbacks for it.</summary>
        public bool CancelSpeechDrive(string turnId, int segmentIndex)
        {
            string token = SpeechToken(turnId, segmentIndex);
            if (token == null || token != externalSpeechToken) return false;
            CancelExternalSpeechDrive();
            talking = false;
            return true;
        }

        public bool HasSpeechDrive => !string.IsNullOrEmpty(externalSpeechToken);

        static string SpeechToken(string turnId, int segmentIndex)
        {
            if (string.IsNullOrWhiteSpace(turnId) || segmentIndex < 0) return null;
            return turnId.Trim() + ":" + segmentIndex;
        }

        void CancelExternalSpeechDrive()
        {
            externalSpeechToken = null;
            externalSpeechAmplitude = 0f;
        }

        void ResetModelParametersImmediate()
        {
            foreach (var pair in parameters)
            {
                if (pair.Value == null) continue;
                float value = Mathf.Clamp(pair.Value.DefaultValue, pair.Value.MinimumValue, pair.Value.MaximumValue);
                currentValues[pair.Key] = value;
                pair.Value.Value = value;
            }
            targets.Clear();
            mouthValue = DefaultValue(profile != null ? profile.mouthParameter : null);
        }

        bool IsZhengyiProfile()
        {
            return profile != null && string.Equals(profile.characterId, "zhengyi", StringComparison.OrdinalIgnoreCase);
        }

        bool IsLiyuanProfile()
        {
            return profile != null && string.Equals(profile.characterId, "liyuan", StringComparison.OrdinalIgnoreCase);
        }

        bool IsLiuyangProfile()
        {
            return profile != null && string.Equals(profile.characterId, "c01", StringComparison.OrdinalIgnoreCase);
        }

        static string EmotionKey(string emotion)
        {
            return (emotion ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        }

        static bool IsLiuyangBawlEmotion(string emotion)
        {
            switch (EmotionKey(emotion))
            {
                case "bawl":
                case "bawl_dry":
                case "bawl_tear":
                case "bawl_wet":
                case "bawling":
                case "cry":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsLiyuanBawlEmotion(string emotion)
        {
            switch (EmotionKey(emotion))
            {
                case "bawl":
                case "bawl_dry":
                case "bawl_wet":
                case "bawl_tears_preview":
                case "bawling":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsLiyuanWetEmotion(string emotion)
        {
            switch (EmotionKey(emotion))
            {
                case "bawl_wet":
                case "bawl_tears_preview":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsLiyuanBawlSequenceEmotion(string emotion)
        {
            switch (EmotionKey(emotion))
            {
                case "bawl":
                case "bawl_dry":
                case "bawling":
                    return true;
                default:
                    return false;
            }
        }

        void ResetLiyuanTransientParametersImmediate()
        {
            if (!IsLiyuanProfile()) return;
            SetImmediate("ParamDialogueSmile", DefaultValue("ParamDialogueSmile"));
            SetImmediate("ParamBawl", DefaultValue("ParamBawl"));
            SetImmediate("ParamBawlTears", DefaultValue("ParamBawlTears"));
            SetImmediate(profile.leftEyeParameter, DefaultValue(profile.leftEyeParameter));
            SetImmediate(profile.rightEyeParameter, DefaultValue(profile.rightEyeParameter));
            SetImmediate(profile.mouthParameter, DefaultValue(profile.mouthParameter));
            mouthValue = DefaultValue(profile.mouthParameter);
            liyuanSpeechClock = 0f;
            liyuanMouthOpen = false;
            tearApplied = false;
        }

        void ApplyCharacterExpressionPose()
        {
            if (IsLiuyangProfile() && IsLiuyangBawlEmotion(activeEmotion))
            {
                targets[profile.mouthParameter] = DefaultValue(profile.mouthParameter);
                string key = EmotionKey(activeEmotion);
                targets["ParamTear"] = key == "bawl_tear" || key == "bawl_wet" ? 1f : 0f;
            }
            if (!IsLiyuanProfile()) return;
            float leftEye = DefaultValue(profile.leftEyeParameter);
            float rightEye = DefaultValue(profile.rightEyeParameter);
            if (IsLiyuanBawlSequenceEmotion(activeEmotion))
            {
                // The approved take does not enter the crying mouth with the
                // expression itself. The sequence below owns eye close first,
                // then ParamBawl at 0.85..1.05 s.
                targets["ParamBawl"] = 0f;
                targets["ParamBawlTears"] = 0f;
            }
            else if (IsLiyuanWetEmotion(activeEmotion))
            {
                // A direct wet preview is still a closed-eye pose. The normal
                // dialogue tag is bawl/bawling and follows the timed sequence.
                leftEye = .05f;
                rightEye = .05f;
            }
            targets[profile.leftEyeParameter] = leftEye;
            targets[profile.rightEyeParameter] = rightEye;
        }

        void ApplyLiyuanBawlSequence()
        {
            if (!IsLiyuanProfile() || !IsLiyuanBawlSequenceEmotion(activeEmotion)) return;

            // Match the approved independent take: eyes begin closing at .55 s,
            // are fully closed at .70 s, and only then does the crying mouth
            // enter from .85 to 1.05 s.
            float eyeOpen;
            if (stateElapsed <= LiyuanBawlEyeCloseStartSeconds) eyeOpen = DefaultValue(profile.leftEyeParameter);
            else if (stateElapsed < LiyuanBawlEyeClosedSeconds)
            {
                float t = Mathf.InverseLerp(LiyuanBawlEyeCloseStartSeconds, LiyuanBawlEyeClosedSeconds, stateElapsed);
                eyeOpen = Mathf.Lerp(DefaultValue(profile.leftEyeParameter), .05f, t);
            }
            else eyeOpen = .05f;

            float bawl;
            if (stateElapsed <= LiyuanBawlExpressionStartSeconds) bawl = 0f;
            else if (stateElapsed < LiyuanBawlExpressionHoldSeconds)
                bawl = Mathf.InverseLerp(LiyuanBawlExpressionStartSeconds, LiyuanBawlExpressionHoldSeconds, stateElapsed);
            else bawl = 1f;

            SetImmediate(profile.leftEyeParameter, eyeOpen);
            SetImmediate(profile.rightEyeParameter, eyeOpen);
            SetImmediate("ParamBawl", bawl);
            SetImmediate("ParamBawlTears", 0f);
        }

        void ApplyLiyuanTearProgress()
        {
            if (!IsLiyuanProfile() || !IsLiyuanBawlEmotion(activeEmotion) || IsLiyuanWetEmotion(activeEmotion)) return;
            float start = Mathf.Max(0f, profile.tearStartSeconds);
            float progress = stateElapsed <= start ? 0f : Mathf.Clamp01((stateElapsed - start) / LiyuanTearRampSeconds);
            SetImmediate("ParamBawlTears", progress);
            tearApplied = progress >= .999f;
        }

        /// <summary>
        /// Starts an authored portrait motion. Animator owns the motion3 curves;
        /// the view refuses to trigger this unless the matching state is installed.
        /// </summary>
        public bool SetPortraitMotion(string key)
        {
            if (profile == null || parameters.Count == 0 ||
                !profile.TryGetPortraitMotion(key, out _, out _)) return false;

            CancelExternalSpeechDrive();
            portraitMotionActive = true;
            talking = false;
            activeExpression = null;
            appliedExpression = null;
            activeEmotion = null;
            stateElapsed = 0f;
            tearApplied = false;
            targets.Clear();
            activeTransition = null;
            transitionElapsed = 0f;
            transitionRunning = false;
            ResetNativeMotionParameters(true);
            BeginEffectGate();
            return true;
        }

        /// <summary>Stops a direct portrait motion and restores authored parameter defaults.</summary>
        public void ClearPortraitMotion()
        {
            CancelExternalSpeechDrive();
            portraitMotionActive = false;
            activeExpression = null;
            appliedExpression = null;
            activeEmotion = null;
            stateElapsed = 0f;
            tearApplied = false;
            ResetNativeMotionParameters(true);
            BeginEffectGate();
        }

        public void OnLateUpdate()
        {
            StepInternal(Time.unscaledDeltaTime, true);
        }

        /// <summary>
        /// Advances the driver with an explicit unscaled delta. The Editor batch
        /// validator uses this to exercise timed expression transitions without
        /// entering Play mode; runtime uses OnLateUpdate above.
        /// </summary>
        public void StepForValidation(float unscaledDeltaSeconds)
        {
            StepInternal(unscaledDeltaSeconds, false);
        }

        void StepInternal(float unscaledDeltaSeconds, bool capDelta)
        {
            if (!enabled || profile == null) return;
            float dt = Mathf.Max(0f, unscaledDeltaSeconds);
            if (capDelta) dt = Mathf.Min(dt, .1f);
            phase += dt;
            if (activeExpression != null) stateElapsed += dt;

            if (transitionRunning && activeTransition != null)
            {
                transitionElapsed += dt;
                if (transitionElapsed >= activeTransition.introSeconds)
                {
                    transitionRunning = false;
                    activeTransition = null;
                    transitionElapsed = 0f;
                    appliedExpression = activeExpression;
                    ApplyExpression(activeExpression);
                    if (IsLiuyangProfile()) ApplyCharacterExpressionPose();
                }
            }

            if (!IsLiyuanProfile() && !portraitMotionActive && !tearApplied && activeExpression != null &&
                (string.Equals(EmotionKey(activeEmotion), "bawl", StringComparison.OrdinalIgnoreCase) ||
                 (IsLiuyangProfile() && IsLiuyangBawlEmotion(activeEmotion))) &&
                profile.tearStartSeconds > .001f && stateElapsed >= profile.tearStartSeconds)
                ApplyTearExpression();

            if (!portraitMotionActive)
            {
                float ease = 1 - Mathf.Exp(-dt / Mathf.Max(.02f, profile.expressionFadeSeconds));
                foreach (var pair in targets)
                {
                    if (!parameters.TryGetValue(pair.Key, out var p)) continue;
                    if (IsDiscreteExpressionParameter(pair.Key))
                    {
                        // Discrete face selectors must be written after Cubism's
                        // async parameter restore as well. Skipping them here lets
                        // the previous enum value rebound for one or more frames.
                        SetDiscreteExpressionParameter(pair.Key, pair.Value);
                        continue;
                    }
                    float current = currentValues.TryGetValue(pair.Key, out var value) ? value : p.DefaultValue;
                    // Qingye's authored face selector swaps eye/mouth layers.
                    // Complete that discrete-looking change before the separate
                    // tear channel enters, while preserving the normal fade for
                    // every other model in the shared island host.
                    float weight = profile.characterId == "xiaoxiaoqing" && pair.Key == "ParamQingyeEmotion"
                        ? 1f - Mathf.Exp(-dt / .08f) : ease;
                    current = Mathf.Lerp(current, Mathf.Clamp(pair.Value, p.MinimumValue, p.MaximumValue), weight);
                    currentValues[pair.Key] = current;
                    p.Value = current;
                }

                if (profile.proceduralIdle)
                {
                    // Do not overwrite an authored expression parameter while it is held.
                    // Liting's curious state owns ParamAngleZ=22; the other native
                    // expressions may resume the gentle idle tilt at their default value.
                    AssignIdle(profile.breathParameter, .5f + .42f * Mathf.Sin(phase * 1.65f));
                    bool holdLiuyangCryEyes = IsLiuyangProfile() && IsLiuyangBawlEmotion(activeEmotion);
                    if (!holdLiuyangCryEyes && (!IsLiyuanProfile() || (!IsLiyuanBawlEmotion(activeEmotion) && !IsLiyuanWetEmotion(activeEmotion))))
                    {
                        float blinkPhase = phase % 4.3f;
                        float openness = blinkPhase < .16f ? Mathf.Abs(blinkPhase / .08f - 1) : 1;
                        AssignIdle(profile.leftEyeParameter, openness);
                        AssignIdle(profile.rightEyeParameter, openness);
                    }
                    AssignIdle("ParamAngleZ", Mathf.Sin(phase * .73f) * 1.2f);
                    AssignIdle("ParamBodyAngleZ", Mathf.Sin(phase * .57f) * .7f);
                }

                if (IsLiyuanProfile() && activeExpression != null && IsLiyuanBawlEmotion(activeEmotion))
                {
                    ApplyLiyuanBawlSequence();
                    ApplyLiyuanTearProgress();
                }

            }
            // The native speech layer can articulate Z's lines over the authored expression.
            if (profile.proceduralTalk && (!portraitMotionActive || talking))
                ApplySpeech(dt);

            if (effectGateFrames > 0)
            {
                HideZhengyiEffectRenderers();
                effectGateFrames--;
            }
        }

        void ApplyExpression(CubismExpressionData expression)
        {
            targets.Clear();
            foreach (var id in expressionParameters)
            {
                if (parameters.TryGetValue(id, out var parameter))
                    targets[id] = parameter.DefaultValue;
            }

            if (expression == null) return;
            foreach (var entry in expression.Parameters)
            {
                if (!parameters.TryGetValue(entry.Id, out var parameter)) continue;
                float value = entry.Value;
                if (entry.Blend == CubismParameterBlendMode.Additive) value += parameter.DefaultValue;
                else if (entry.Blend == CubismParameterBlendMode.Multiply) value *= parameter.DefaultValue;

                if (IsDiscreteExpressionParameter(entry.Id))
                    SetDiscreteExpressionParameter(entry.Id, value);
                targets[entry.Id] = value;
            }
        }

        void ApplyTearExpression()
        {
            tearApplied = true;
            if (profile == null) return;
            string tearKey = profile.tearExpression;
            if (string.IsNullOrEmpty(tearKey) && IsZhengyiProfile()) tearKey = "bawl_tears_preview";
            if (string.IsNullOrEmpty(tearKey)) return;
            var tear = profile.FindExpression(tearKey);
            if (tear == null) return;
            foreach (var entry in tear.Parameters)
            {
                if (!parameters.TryGetValue(entry.Id, out var parameter)) continue;
                float value = entry.Value;
                if (entry.Blend == CubismParameterBlendMode.Additive) value += parameter.DefaultValue;
                else if (entry.Blend == CubismParameterBlendMode.Multiply) value *= parameter.DefaultValue;
                if (IsDiscreteExpressionParameter(entry.Id))
                    SetDiscreteExpressionParameter(entry.Id, value);
                targets[entry.Id] = value;
            }
            BeginEffectGate();
        }

        bool IsDiscreteExpressionParameter(string id)
        {
            if (profile == null || !profile.discreteExpressionParameters || string.IsNullOrEmpty(id)) return false;
            return string.Equals(id, profile.expressionIndexParameter, StringComparison.Ordinal) ||
                string.Equals(id, profile.expressionMixParameter, StringComparison.Ordinal);
        }

        void SetDiscreteExpressionParameter(string id, float value)
        {
            if (!parameters.TryGetValue(id, out var parameter)) return;
            if (string.Equals(id, profile.expressionIndexParameter, StringComparison.Ordinal))
                value = Mathf.Round(value);
            else if (string.Equals(id, profile.expressionMixParameter, StringComparison.Ordinal))
                value = Mathf.Clamp01(value);
            value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
            currentValues[id] = value;
            parameter.Value = value;
        }

        public void SetTalkingDuringMotion(bool value)
        {
            talking = value;
        }

        void ApplySpeech(float dt)
        {
            if (IsLiyuanProfile())
            {
                ApplyLiyuanSpeech(dt);
                return;
            }
            if (IsLiuyangProfile())
            {
                ApplyLiuyangSpeech(dt);
                return;
            }
            string normalizedEmotion = EmotionKey(activeEmotion);
            string speechParameter = ResolveSpeechParameter(normalizedEmotion, out var qingyeNeutral);
            if (string.IsNullOrEmpty(speechParameter) || !HasParameter(speechParameter)) return;
            bool native = HasParameter(NativeSpeechParameter) && speechParameter == NativeSpeechParameter;
            // Legacy expressions with a non-zero mouth variant own their
            // fixed shape. R5's native ParamZSpeech path intentionally keeps
            // its existing layered speech behavior.
            bool fixedMouth = !native && !profile.allowSpeechWithMouthVariant &&
                targets.TryGetValue("ParamMouthVariant", out var variant) && Mathf.Abs(variant) > .1f;
            float authoredMouth = targets.TryGetValue(speechParameter, out var hold)
                ? hold
                : DefaultValue(speechParameter);
            float target;
            var speechExpression = profile.FindSpeechExpression(normalizedEmotion);
            if (speechExpression != null || profile.speechAddsToExpressionTarget || IsZhengyiProfile())
            {
                float peak = speechExpression != null ? Mathf.Clamp01(speechExpression.peak) : ResolveSpeechPeak(authoredMouth);
                float amplitude = externalSpeechToken != null
                    ? externalSpeechAmplitude
                    : talking ? Mathf.Abs(Mathf.Sin(phase * 11.7f) * Mathf.Sin(phase * 5.3f)) : 0f;
                target = fixedMouth ? authoredMouth : Mathf.Lerp(authoredMouth, Mathf.Max(authoredMouth, peak), amplitude);
            }
            else
            {
                float amplitude = externalSpeechToken != null
                    ? externalSpeechAmplitude
                    : talking && !fixedMouth ? Mathf.Abs(Mathf.Sin(phase * 11.7f) * Mathf.Sin(phase * 5.3f)) : 0f;
                float peak = profile.speechPeak > .001f ? profile.speechPeak : .52f;
                bool speaking = talking || externalSpeechToken != null;
                float rest = qingyeNeutral ? 0f : profile.speechRest;
                target = fixedMouth || !speaking ? authoredMouth : (rest + peak * amplitude) * profile.talkMouthAmplitude;
            }
            // 周伯伯日常说话时保留轻启口型，只收住重音的最大开口。
            if (profile.characterId == "zhou_bobo_daily" && !fixedMouth &&
                (talking || externalSpeechToken != null) && speechParameter == LegacyMouthParameter && target > .55f)
            {
                float opening = Mathf.Min(1f, target);
                float high = Mathf.InverseLerp(.55f, 1f, opening);
                target = opening - .18f * high * high;
            }
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
                Assign(speechParameter, mouthValue);
                ResetInactiveQingyeSpeechChannel(speechParameter);
            }
        }

        string ResolveSpeechParameter(string emotion = null)
        {
            return ResolveSpeechParameter(EmotionKey(emotion ?? activeEmotion), out _);
        }

        string ResolveSpeechParameter(string normalizedEmotion, out bool qingyeNeutral)
        {
            qingyeNeutral = false;
            if (profile == null) return null;
            if (HasConfiguredQingyeNeutralSpeech() && IsNeutralEmotion(normalizedEmotion) &&
                HasParameter(profile.neutralSpeechParameter))
            {
                qingyeNeutral = true;
                return profile.neutralSpeechParameter;
            }
            return profile.mouthParameter;
        }

        bool IsQingyeProfile()
        {
            return profile != null && string.Equals(profile.characterId, QingyeCharacterId, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsNeutralEmotion(string normalizedEmotion)
        {
            return string.Equals(normalizedEmotion, "neutral", StringComparison.Ordinal);
        }

        bool HasConfiguredQingyeNeutralSpeech()
        {
            return IsQingyeProfile() && !string.IsNullOrEmpty(profile.neutralSpeechParameter);
        }

        void ResetQingyeSpeechChannelsForState(string nextEmotion)
        {
            if (!HasConfiguredQingyeNeutralSpeech()) return;
            SetImmediate(profile.neutralSpeechParameter, DefaultValue(profile.neutralSpeechParameter));
            string nextParameter = ResolveSpeechParameter(nextEmotion);
            if (!string.Equals(nextParameter, profile.neutralSpeechParameter, StringComparison.OrdinalIgnoreCase))
                SetImmediate(profile.mouthParameter, DefaultValue(profile.mouthParameter));
            mouthValue = DefaultValue(nextParameter);
        }

        void ResetInactiveQingyeSpeechChannel(string activeParameter)
        {
            if (!HasConfiguredQingyeNeutralSpeech()) return;
            if (!string.Equals(activeParameter, profile.neutralSpeechParameter, StringComparison.OrdinalIgnoreCase))
                SetImmediate(profile.neutralSpeechParameter, DefaultValue(profile.neutralSpeechParameter));
            else if (!string.Equals(profile.mouthParameter, profile.neutralSpeechParameter, StringComparison.OrdinalIgnoreCase))
                SetImmediate(profile.mouthParameter, DefaultValue(profile.mouthParameter));
        }

        void ApplyLiyuanSpeech(float dt)
        {
            float peak = IsLiyuanBawlEmotion(activeEmotion) ? .65f : .85f;
            bool fixedMouth = targets.TryGetValue("ParamMouthVariant", out var variant) && Mathf.Abs(variant) > .1f;
            bool bawlReady = !IsLiyuanBawlSequenceEmotion(activeEmotion) || stateElapsed >= LiyuanBawlExpressionHoldSeconds;

            if (!talking || fixedMouth || !bawlReady)
            {
                liyuanSpeechClock = 0f;
                liyuanMouthOpen = false;
            }
            else if (externalSpeechToken != null)
            {
                // The explicit phoneme path uses hysteresis so small amplitude
                // noise cannot chatter between the two approved mouth shapes.
                if (liyuanMouthOpen)
                {
                    if (externalSpeechAmplitude <= LiyuanMouthCloseThreshold) liyuanMouthOpen = false;
                }
                else if (externalSpeechAmplitude >= LiyuanMouthOpenThreshold)
                {
                    liyuanMouthOpen = true;
                }
            }
            else
            {
                // MemoryDialogueView supplies a talking flag rather than a
                // phoneme stream. Keep this path as two discrete rhythmic beats
                // per .72 s instead of a continuous sinusoidal mouth.
                liyuanSpeechClock += dt;
                float beat = Mathf.Repeat(liyuanSpeechClock, LiyuanSpeechBeatSeconds);
                bool rhythmicOpen = (beat >= .10f && beat < .32f) || (beat >= .46f && beat < .68f);
                liyuanMouthOpen = rhythmicOpen;
            }

            // v04-r2 approved two-state mouth: no interpolation through 0.05,
            // 0.27, etc. Each frame owns exactly the closed or peak shape.
            float target = liyuanMouthOpen ? peak : 0f;
            mouthValue = target;
            Assign(profile.mouthParameter, target);
        }

        void ApplyLiuyangSpeech(float dt)
        {
            // c01's approved crying states use the confirmed closed mouth even
            // while the dialogue line is still revealing. Puzzled and neutral
            // use the source contract's ~.44 peak; sigh_after holds .24 and
            // layers speech over it up to the same peak.
            if (IsLiuyangBawlEmotion(activeEmotion))
            {
                liuyangSpeechClock = 0f;
                mouthValue = 0f;
                Assign(profile.mouthParameter, 0f);
                return;
            }

            bool fixedMouth = !profile.allowSpeechWithMouthVariant &&
                targets.TryGetValue("ParamMouthVariant", out var variant) && Mathf.Abs(variant) > .1f;
            float authoredMouth = targets.TryGetValue(profile.mouthParameter, out var hold)
                ? hold
                : DefaultValue(profile.mouthParameter);
            float peak = Mathf.Clamp01(profile.FindSpeechPeak(activeEmotion, .44f));
            float amplitude;
            if (!talking || fixedMouth)
            {
                liuyangSpeechClock = 0f;
                amplitude = 0f;
            }
            else if (externalSpeechToken != null)
            {
                amplitude = externalSpeechAmplitude;
            }
            else
            {
                // MemoryDialogueView supplies a talking flag rather than a
                // phoneme stream. Keep the mouth rhythmic while preserving the
                // authored sigh hold as the closed/rest shape.
                liuyangSpeechClock += dt;
                amplitude = Mathf.Abs(Mathf.Sin(liuyangSpeechClock * 11.7f) *
                    Mathf.Sin(liuyangSpeechClock * 5.3f));
            }

            float target = fixedMouth ? authoredMouth :
                Mathf.Lerp(authoredMouth, Mathf.Max(authoredMouth, peak), amplitude);
            mouthValue = Mathf.Lerp(mouthValue, target, 1 - Mathf.Exp(-dt * 18f));
            Assign(profile.mouthParameter, mouthValue);
        }

        float ResolveSpeechPeak(float authoredMouth)
        {
            float fallback;
            if (string.Equals(profile.characterId, "zhengyi", StringComparison.OrdinalIgnoreCase))
            {
                switch ((activeEmotion ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_'))
                {
                    case "smile": fallback = .40f; break;
                    case "surprise": fallback = .36f; break;
                    case "shock": fallback = .40f; break;
                    case "bawl": fallback = .24f; break;
                    default: fallback = .32f; break;
                }
            }
            else fallback = Mathf.Max(.52f, authoredMouth);
            return Mathf.Clamp01(profile.FindSpeechPeak(activeEmotion, fallback));
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

        void ClearExpressionParametersImmediate()
        {
            foreach (var id in expressionParameters)
            {
                if (!parameters.TryGetValue(id, out var parameter)) continue;
                float value = parameter.DefaultValue;
                currentValues[id] = value;
                parameter.Value = value;
            }
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

        void CacheZhengyiEffectRenderers()
        {
            if (!IsZhengyiProfile()) return;
            var renderers = GetComponentsInChildren<CubismRenderer>(true);
            var found = new List<CubismRenderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                string name = renderer.gameObject.name ?? string.Empty;
                if (name.StartsWith("Fx", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Tear_L", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Tear_R", StringComparison.OrdinalIgnoreCase))
                    found.Add(renderer);
            }
            zhengyiEffectRenderers = found.ToArray();
        }

        void BeginEffectGate()
        {
            if (!IsZhengyiProfile() || zhengyiEffectRenderers.Length == 0) return;
            // Cubism's native drawable data and the URP portrait camera do not
            // publish on the same phase. Keep old symbol/tear drawables hidden
            // through the next two runtime frames, then let the new opacity win.
            effectGateFrames = Mathf.Max(effectGateFrames, 2);
            HideZhengyiEffectRenderers();
        }

        void HideZhengyiEffectRenderers()
        {
            ApplyZhengyiEffectVisibility(false);
        }

        void ShowZhengyiEffectRenderers()
        {
            ApplyZhengyiEffectVisibility(true);
        }

        void ApplyZhengyiEffectVisibility(bool allowCurrentState)
        {
            foreach (var renderer in zhengyiEffectRenderers)
                if (renderer != null && renderer.MeshRenderer != null)
                    renderer.MeshRenderer.enabled = allowCurrentState && IsEffectAllowed(renderer.gameObject.name);
        }

        bool IsEffectAllowed(string name)
        {
            string key = (activeEmotion ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
            if (key == "starry" || key == "starry_curious") key = "curious";
            if (key == "troubled" || key == "question") key = "puzzled";
            if (key == "blush") key = "shy";

            if (key == "shock") return string.Equals(name, "FxShock", StringComparison.OrdinalIgnoreCase);
            if (key == "surprise") return string.Equals(name, "FxSurprise", StringComparison.OrdinalIgnoreCase);
            if (key == "puzzled") return string.Equals(name, "FxQuestion", StringComparison.OrdinalIgnoreCase);
            if (key == "angry") return string.Equals(name, "FxAnger", StringComparison.OrdinalIgnoreCase);
            if (key == "curious")
                return name.StartsWith("FxStarIris", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("FxSpark", StringComparison.OrdinalIgnoreCase);
            if (key == "bawl" && tearApplied)
                return string.Equals(name, "Tear_L", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Tear_R", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!isActiveAndEnabled || !IsZhengyiProfile()) return;
            // The project uses a custom Cubism URP pass. It checks
            // MeshRenderer.enabled, so this gate is applied immediately before
            // camera culling instead of relying on the earlier LateUpdate write.
            if (effectGateFrames > 0) HideZhengyiEffectRenderers();
            else ShowZhengyiEffectRenderers();
        }

        void AssignIdle(string id, float value)
        {
            if (!HasParameter(id)) return;
            // An expression target different from the parameter default is authored
            // pose data. Let it win over procedural idle until SetState clears it.
            if (targets.TryGetValue(id, out var target) &&
                !Mathf.Approximately(target, parameters[id].DefaultValue)) return;
            Assign(id, value);
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering; }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            effectGateFrames = 0;
        }

        void LateUpdate() { if (!HasUpdateController) OnLateUpdate(); }
    }
}
