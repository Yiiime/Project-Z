#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.Json;
using Live2D.Cubism.Framework.MotionFade;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Seabreeze.Editor
{
    /// <summary>
    /// Imports the approved 阿星 v13 runtime and creates the game-facing
    /// dialogue portrait profile. The source model owns the six authored
    /// action clips, while the profile exposes the stable dialogue keys used
    /// by MemoryLab.
    /// </summary>
    public static class AxingImportSetup
    {
        const string ModelRoot = "Assets/Seabreeze/Live2D/Models/Axing";
        const string ModelJsonPath = ModelRoot + "/Model.model3.json";
        const string ModelAssetPath = ModelRoot + "/Model.asset";
        const string PrefabPath = ModelRoot + "/Model.prefab";
        const string ControllerPath = ModelRoot + "/Model.controller";
        const string TexturePath = ModelRoot + "/texture_00.png";
        const string ProfilePath = "Assets/Seabreeze/Resources/Memory/Portraits/Axing.asset";
        const int AtlasTextureSize = 4096;
        const string DisplayMaterialPath = ModelRoot + "/AxingPortraitComposite.mat";

        // These are the authored motion3 clips, not their static exp3 endpoint
        // poses. The service-facing keys deliberately stay stable even if the
        // source file names change in a future approved runtime package.
        static readonly MotionSpec[] AuthoredMotions =
        {
            new MotionSpec("repair", "axing_focus_repair"),
            new MotionSpec("puzzled", "axing_curious_side"),
            new MotionSpec("angry", "axing_angry_stop"),
            new MotionSpec("surprise", "axing_alert_micro_open"),
            new MotionSpec("sigh", "axing_sigh"),
            new MotionSpec("smile", "axing_relieved_smile")
        };

        struct MotionSpec
        {
            public readonly string Emotion;
            public readonly string ClipName;

            public MotionSpec(string emotion, string clipName)
            {
                Emotion = emotion;
                ClipName = clipName;
            }
        }

        static readonly Dictionary<string, string> RequiredHashes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Model.model3.json", "4b52e973f09f3fe00181fe677efc09b850682f7172453e1516b2b99c2beb8c62" },
                { "Model.moc3", "adfa5c9af1e75a84cb7c328073e295dd330008b19e491dccc4803f67f39784bf" },
                { "texture_00.png", "7572a8609e161a71bb6f1c051992762d8fd14e9cd2404a7908e23a5f4d3c7004" }
            };

        [MenuItem("Seabreeze/Configure Axing v13 dialogue portrait")]
        public static void Configure()
        {
            EnsureEditMode();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateRawSource();
            ConfigureTextureImport();

            AssetDatabase.ImportAsset(ModelJsonPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateTextureImport();

            var prefab = RequireAsset<GameObject>(PrefabPath, "阿星 Cubism model prefab");
            RequireAsset<CubismMoc>(ModelAssetPath, "阿星 Cubism model asset");
            ValidateParameters(prefab);
            ValidateAuthoredMotionSources();
            var controller = PrepareMotionController();

            var profile = AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<Live2DModelProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.prefab = prefab;
            profile.portraitDisplayMaterial = RequireAsset<Material>(DisplayMaterialPath, "阿星 portrait display material");
            profile.characterId = "axing";
            profile.rendererIndex = 1;
            profile.renderLayer = 30;
            profile.modelScale = 1f;
            profile.autoFrame = true;
            profile.visibleHeight = .78f;
            profile.verticalFocus = .62f;
            profile.textureHeight = 1200;
            profile.defaultEmotion = "neutral";
            profile.proceduralTalk = true;
            profile.expressionFadeSeconds = .20f;
            profile.clearExpressionOnInterrupt = true;
            profile.mouthParameter = "ParamMouthOpenY";
            profile.neutralSpeechParameter = "ParamMouthOpenY";
            profile.talkMouthAmplitude = 1f;
            profile.speechPeak = .78f;
            profile.speechRest = .08f;
            // Keep the authored idle clip running while speech drives only the
            // mouth. Returning to Idle after an interrupted action is explicit:
            // the profile's talk state is also Idle, rather than an empty state.
            profile.animatorController = controller;
            profile.idleState = "Idle";
            profile.talkState = "Idle";
            profile.proceduralIdle = false;
            profile.portraitMotions = BuildPortraitMotionBindings();
            profile.expressions = new[]
            {
                Bind("neutral", null),
                Bind("repair", "focus_repair"),
                Bind("puzzled", "curious_side"),
                Bind("angry", "angry_stop"),
                Bind("surprise", "alert_micro_open"),
                Bind("sigh", "sigh"),
                Bind("smile", "relieved_smile"),
                Bind("happy", "relieved_smile"),
                Bind("troubled", "troubled"),
                Bind("blush", "blush"),
                Bind("wave", "wave"),
                Bind("explain", "explain"),
                Bind("point", "point"),
                Bind("inspect", "inspect")
            };
            profile.authoringNotes =
                "阿星 v13 眉间修正版。repair/puzzled/angry/surprise/sigh/smile are the stable dialogue keys. " +
                "These six keys bind the authored motion3 curves through Model.controller and portraitMotions; " +
                "the matching exp3 files remain as endpoint compatibility faces only. " +
                "The model's ParamMouthOpenY drives speech, and every interrupted action returns to Idle. " +
                "Source runtime: live2d-halfbody/角色/阿星_v13_眉间修正/runtime.";

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateProfile(profile, prefab, controller);
            Debug.Log("AXING_V13_CONFIGURED: prefab=" + PrefabPath + ", profile=" + ProfilePath);
        }

        [MenuItem("Seabreeze/Validate Axing v13 dialogue portrait")]
        public static void Validate()
        {
            EnsureEditMode();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateRawSource();
            ValidateTextureImport();
            var prefab = RequireAsset<GameObject>(PrefabPath, "阿星 Cubism model prefab");
            RequireAsset<CubismMoc>(ModelAssetPath, "阿星 Cubism model asset");
            ValidateParameters(prefab);
            ValidateAuthoredMotionSources();
            var controller = RequireAsset<AnimatorController>(ControllerPath, "阿星 Animator controller");
            ValidateMotionController(controller);
            var profile = RequireAsset<Live2DModelProfile>(ProfilePath, "阿星 Live2D profile");
            ValidateProfile(profile, prefab, controller);
            Debug.Log("AXING_V13_VALIDATED: prefab=" + PrefabPath + ", profile=" + ProfilePath);
        }

        static Live2DModelProfile.PortraitMotionBinding[] BuildPortraitMotionBindings()
        {
            var bindings = new Live2DModelProfile.PortraitMotionBinding[AuthoredMotions.Length];
            for (var i = 0; i < AuthoredMotions.Length; i++)
            {
                var spec = AuthoredMotions[i];
                // Cubism's motion importer normally creates these clips, but a
                // model import can finish before the generated .anim is written.
                // Rehydrate from the approved motion3 source so the profile never
                // silently falls back to a missing or empty generated asset.
                var clip = EnsureMotionClip(spec.ClipName, false);
                bindings[i] = new Live2DModelProfile.PortraitMotionBinding
                {
                    emotion = spec.Emotion,
                    clip = clip
                };
            }
            return bindings;
        }

        static void ConfigureTextureImport()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("阿星 atlas texture importer is unavailable: " + TexturePath);

            importer.textureType = TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.None;
            importer.alphaIsTransparency = true;
            // Preserve the color values the artist used for semi-transparent facial layers.
            // The display material converts the finished composite for the linear Canvas.
            importer.sRGBTexture = false;
            importer.maxTextureSize = AtlasTextureSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            SetPortraitPlatformSettings(importer, "DefaultTexturePlatform");
            SetPortraitPlatformSettings(importer, "Standalone");
            importer.SaveAndReimport();
            ValidateTextureImport();
        }

        static void SetPortraitPlatformSettings(TextureImporter importer, string platform)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.maxTextureSize = AtlasTextureSize;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }

        static void ValidateTextureImport()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("阿星 atlas texture importer is unavailable: " + TexturePath);

            var defaultPlatform = importer.GetPlatformTextureSettings("DefaultTexturePlatform");
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            if (importer.sRGBTexture || importer.maxTextureSize != AtlasTextureSize ||
                importer.textureType != TextureImporterType.Default ||
                importer.spriteImportMode != SpriteImportMode.None ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                defaultPlatform.maxTextureSize != AtlasTextureSize ||
                defaultPlatform.textureCompression != TextureImporterCompression.Uncompressed ||
                standalone.maxTextureSize != AtlasTextureSize ||
                standalone.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidDataException("阿星 atlas texture import settings did not stick: " + TexturePath);
        }

        static AnimatorController PrepareMotionController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            if (controller.layers == null || controller.layers.Length == 0)
                throw new InvalidDataException("阿星 Animator controller has no Base Layer.");

            var stateMachine = controller.layers[0].stateMachine;
            EnsureFadeObserver(stateMachine);

            // Keep idle as an authored motion3 clip. This is intentionally
            // generated here as well as by Cubism's importer because model3
            // imports may leave idle.motion3.json without idle.anim.
            var idleClip = EnsureMotionClip("idle", true);
            var idle = EnsureState(stateMachine, "Idle", new Vector3(220, 40, 0));
            idle.motion = idleClip;
            SetLoop(idleClip, true);

            for (var i = 0; i < AuthoredMotions.Length; i++)
            {
                var spec = AuthoredMotions[i];
                var clip = EnsureMotionClip(spec.ClipName, false);
                var state = EnsureState(stateMachine, spec.Emotion,
                    new Vector3(260 + i * 40, 100 + i * 55, 0));
                state.motion = clip;
                SetLoop(clip, false);
            }

            stateMachine.defaultState = idle;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            ValidateMotionController(controller);
            return controller;
        }

        static AnimationClip EnsureMotionClip(string clipName, bool loopExpected)
        {
            var motionPath = ModelRoot + "/" + clipName + ".motion3.json";
            var clipPath = ModelRoot + "/" + clipName + ".anim";
            var filePath = ProjectFilePath(motionPath);
            if (!File.Exists(filePath))
                throw new FileNotFoundException("阿星 authored motion source is missing", filePath);

            var motion = CubismMotion3Json.LoadFrom(File.ReadAllText(filePath), true);
            if (motion == null || motion.Meta.Duration <= .01f)
                throw new InvalidDataException("阿星 authored motion source is invalid: " + clipName);
            if (motion.Meta.Loop != loopExpected)
                throw new InvalidDataException("阿星 authored motion loop flag is unexpected: " + clipName);

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = motion.ToAnimationClip(false, true, true);
                if (clip == null)
                    throw new InvalidDataException("阿星 authored motion could not be converted: " + clipName);
                clip.name = clipName;
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            else
            {
                // Existing .anim files are generated outputs, not an alternate
                // source of truth. Refresh their curves in place to preserve the
                // asset GUID while correcting incomplete Cubism imports.
                if (motion.ToAnimationClip(clip, false, true, true) == null)
                    throw new InvalidDataException("阿星 authored motion could not be refreshed: " + clipName);
                clip.name = clipName;
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loopExpected;
            settings.stopTime = motion.Meta.Duration;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static AnimatorState EnsureState(AnimatorStateMachine stateMachine, string name, Vector3 position)
        {
            foreach (var child in stateMachine.states)
                if (child.state != null && string.Equals(child.state.name, name, StringComparison.Ordinal))
                    return child.state;
            return stateMachine.AddState(name, position);
        }

        static void EnsureFadeObserver(AnimatorStateMachine stateMachine)
        {
            foreach (var behaviour in stateMachine.behaviours)
                if (behaviour is CubismFadeStateObserver) return;
            stateMachine.AddStateMachineBehaviour<CubismFadeStateObserver>();
        }

        static void SetLoop(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }

        static void ValidateMotionController(AnimatorController controller)
        {
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                throw new InvalidDataException("阿星 Animator controller is missing its Base Layer.");
            var states = controller.layers[0].stateMachine.states
                .Where(child => child.state != null)
                .ToDictionary(child => child.state.name, child => child.state,
                    StringComparer.Ordinal);
            if (!states.TryGetValue("Idle", out var idle) || idle.motion == null)
                throw new InvalidDataException("阿星 Animator controller has no Idle clip.");
            var expectedIdle = RequireAsset<AnimationClip>(ModelRoot + "/idle.anim", "阿星 authored idle clip");
            if (idle.motion != expectedIdle)
                throw new InvalidDataException("阿星 Idle state is not bound to idle.anim");
            if (expectedIdle.length <= .01f)
                throw new InvalidDataException("阿星 authored idle clip is empty.");
            ValidateImportedCurves(expectedIdle, "idle");
            foreach (var spec in AuthoredMotions)
            {
                if (!states.TryGetValue(spec.Emotion, out var state) || state.motion == null)
                    throw new InvalidDataException("阿星 Animator controller is missing authored state " + spec.Emotion);
                var expected = RequireAsset<AnimationClip>(ModelRoot + "/" + spec.ClipName + ".anim",
                    "阿星 authored motion clip " + spec.Emotion);
                if (state.motion != expected)
                    throw new InvalidDataException("阿星 state " + spec.Emotion + " is not bound to " + spec.ClipName + ".anim");
                if (expected.length <= .01f)
                    throw new InvalidDataException("阿星 authored motion clip is empty: " + spec.ClipName);
                ValidateImportedCurves(expected, spec.Emotion);
            }
        }

        static void ValidateImportedCurves(AnimationClip clip, string emotion)
        {
            var paths = new HashSet<string>(AnimationUtility.GetCurveBindings(clip)
                .Select(binding => binding.path), StringComparer.Ordinal);
            foreach (var id in RequiredMotionParameters(emotion))
                if (!paths.Contains("Parameters/" + id))
                    throw new InvalidDataException("阿星 imported clip " + emotion + " is missing curve Parameters/" + id);
            if (paths.Count == 0)
                throw new InvalidDataException("阿星 imported clip has no parameter curves: " + emotion);
        }

        static void ValidateAuthoredMotionSources()
        {
            var idlePath = ProjectFilePath(ModelRoot + "/idle.motion3.json");
            if (!File.Exists(idlePath)) throw new FileNotFoundException("阿星 authored idle source is missing", idlePath);
            var idleData = JsonUtility.FromJson<MotionJson>(File.ReadAllText(idlePath));
            if (idleData == null || idleData.Meta == null || idleData.Curves == null || idleData.Curves.Length == 0 ||
                !idleData.Meta.Loop || idleData.Meta.Duration <= .01f)
                throw new InvalidDataException("阿星 authored idle source is not a valid looping motion3.");
            var idleIds = new HashSet<string>(idleData.Curves.Where(curve => curve != null && !string.IsNullOrEmpty(curve.Id))
                .Select(curve => curve.Id), StringComparer.Ordinal);
            foreach (var id in new[] { "ParamBreath", "ParamAngleX", "ParamAngleY", "ParamAngleZ", "ParamBodyAngleX", "ParamBodyAngleY", "ParamBodyAngleZ" })
                if (!idleIds.Contains(id))
                    throw new InvalidDataException("阿星 idle motion is missing curve " + id);

            foreach (var spec in AuthoredMotions)
            {
                var path = ProjectFilePath(ModelRoot + "/" + spec.ClipName + ".motion3.json");
                if (!File.Exists(path)) throw new FileNotFoundException("阿星 authored motion source is missing", path);
                var data = JsonUtility.FromJson<MotionJson>(File.ReadAllText(path));
                if (data == null || data.Meta == null || data.Curves == null || data.Curves.Length == 0)
                    throw new InvalidDataException("阿星 authored motion source is not a valid motion3: " + spec.ClipName);
                if (data.Meta.Loop || data.Meta.Duration <= .01f)
                    throw new InvalidDataException("阿星 authored motion must be a finite clip: " + spec.ClipName);

                var ids = new HashSet<string>(data.Curves.Where(curve => curve != null && !string.IsNullOrEmpty(curve.Id))
                    .Select(curve => curve.Id), StringComparer.Ordinal);
                foreach (var id in RequiredMotionParameters(spec.Emotion))
                    if (!ids.Contains(id))
                        throw new InvalidDataException("阿星 motion " + spec.Emotion + " is missing curve " + id);

                foreach (var id in new[] { "ParamFxQuestion", "ParamFxExclamation", "ParamFxAnger" })
                {
                    var curve = data.Curves.FirstOrDefault(item => item != null && item.Id == id);
                    if (curve == null || curve.Segments == null || curve.Segments.Length < 2 ||
                        Mathf.Abs(curve.Segments[curve.Segments.Length - 1]) > .001f)
                        throw new InvalidDataException("阿星 motion " + spec.Emotion + " does not reset " + id);
                }
            }
        }

        static IEnumerable<string> RequiredMotionParameters(string emotion)
        {
            switch (emotion)
            {
                case "idle": return new[] { "ParamBreath", "ParamAngleX", "ParamAngleY", "ParamAngleZ", "ParamBodyAngleX", "ParamBodyAngleY", "ParamBodyAngleZ" };
                case "repair": return new[] { "ParamPose" };
                case "puzzled": return new[] { "ParamEyeBallX", "ParamMouthVariant" };
                case "angry": return new[] { "ParamAngleY", "ParamMouthVariant" };
                case "surprise": return new[] { "ParamBrowForm", "ParamEyeBallY", "ParamMouthOpenY" };
                case "sigh": return new[] { "ParamBodyAngleY", "ParamMouthVariant", "ParamMouthOpenY" };
                case "smile": return new[] { "ParamRelievedSmile", "ParamMouthVariant" };
                default: throw new InvalidDataException("Unknown 阿星 authored motion key: " + emotion);
            }
        }

        [Serializable]
        sealed class MotionJson
        {
            public MotionMeta Meta;
            public MotionCurve[] Curves;
        }

        [Serializable]
        sealed class MotionMeta
        {
            public float Duration;
            public bool Loop;
        }

        [Serializable]
        sealed class MotionCurve
        {
            public string Id;
            public float[] Segments;
        }

        static Live2DModelProfile.ExpressionBinding Bind(string emotion, string file)
        {
            return new Live2DModelProfile.ExpressionBinding
            {
                emotion = emotion,
                expression = string.IsNullOrEmpty(file)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<CubismExpressionData>(
                        ModelRoot + "/" + file + ".exp3.asset")
            };
        }

        static void ValidateRawSource()
        {
            foreach (var item in RequiredHashes)
            {
                string path = ProjectFilePath(ModelRoot + "/" + item.Key);
                if (!File.Exists(path))
                    throw new FileNotFoundException("阿星 source asset is missing", path);
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                        .Replace("-", string.Empty).ToLowerInvariant();
                    if (!string.Equals(actual, item.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("阿星 source hash mismatch: " + item.Key);
                }
            }
        }

        static void ValidateParameters(GameObject prefab)
        {
            var model = prefab.GetComponent<CubismModel>();
            if (model == null) throw new InvalidDataException("阿星 prefab has no CubismModel.");
            var ids = new HashSet<string>(model.Parameters.Where(p => p != null).Select(p => p.Id));
            string[] required = { "ParamEyeLOpen", "ParamEyeROpen", "ParamMouthOpenY", "ParamMouthVariant", "ParamMouthForm" };
            foreach (var id in required)
                if (!ids.Contains(id)) throw new InvalidDataException("阿星 model is missing parameter " + id);
        }

        static void ValidateProfile(Live2DModelProfile profile, GameObject prefab, AnimatorController controller)
        {
            if (profile == null || profile.prefab != prefab || profile.characterId != "axing")
                throw new InvalidDataException("阿星 profile does not point to the imported prefab.");
            if (profile.animatorController != controller || profile.idleState != "Idle" || profile.talkState != "Idle" ||
                profile.proceduralIdle)
                throw new InvalidDataException("阿星 profile must keep authored Idle for action return and speech.");
            if (profile.portraitDisplayMaterial != RequireAsset<Material>(DisplayMaterialPath, "阿星 portrait display material"))
                throw new InvalidDataException("阿星 portrait display material must match its atlas color space.");
            string[] keys = { "repair", "puzzled", "angry", "surprise", "sigh", "smile" };
            foreach (var key in keys)
                if (profile.FindExpression(key) == null)
                    throw new InvalidDataException("阿星 profile is missing expression key " + key);
            if (profile.portraitMotions == null || profile.portraitMotions.Length != AuthoredMotions.Length)
                throw new InvalidDataException("阿星 profile does not contain all six authored portrait motions.");
            foreach (var spec in AuthoredMotions)
            {
                var binding = profile.portraitMotions.FirstOrDefault(item => item != null && item.emotion == spec.Emotion);
                var expected = AssetDatabase.LoadAssetAtPath<AnimationClip>(ModelRoot + "/" + spec.ClipName + ".anim");
                if (binding == null || binding.clip != expected)
                    throw new InvalidDataException("阿星 profile motion binding is invalid: " + spec.Emotion);
            }
            if (profile.mouthParameter != "ParamMouthOpenY")
                throw new InvalidDataException("阿星 profile mouthParameter must be ParamMouthOpenY.");
        }

        static T RequireAsset<T>(string path, string label) where T : UnityEngine.Object
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidDataException(label + " is missing: " + path);
            return value;
        }

        static string ProjectFilePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        static void EnsureEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("阿星 Live2D import must run outside Play mode.");
        }
    }
}
#endif
