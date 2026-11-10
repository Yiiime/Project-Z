#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Rendering;
using Live2D.Cubism.Rendering.Masking;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Seabreeze.Editor
{
    /// <summary>
    /// One-shot importer/profile setup for the isolated Liting v02-r5 model.
    /// Run this after syncing the raw 21-file package into Models/Liting.
    /// It deliberately does not touch any other character or scene asset.
    /// </summary>
    public static class LitingR5ImportSetup
    {
        const string ModelRoot = "Assets/Seabreeze/Live2D/Models/Liting";
        const string ModelJsonPath = ModelRoot + "/Model.model3.json";
        const string TexturePath = ModelRoot + "/texture_00.png";
        const string PrefabPath = ModelRoot + "/Model.prefab";
        const string MocPath = ModelRoot + "/Model.asset";
        const string ProfilePath = "Assets/Seabreeze/Resources/Memory/Portraits/Liting.asset";

        static readonly string[] NativeExpressions =
        {
            "neutral", "smile", "surprise", "starry_curious", "curious", "blush", "angry",
            "disappointed", "cry", "bawl_dry", "bawl", "gloom", "sigh_in", "sigh", "wink", "laugh"
        };

        static readonly string[] RequiredParameters =
        {
            "ParamExpression", "ParamExpressionMix", "ParamMouthOpenY", "ParamBreath",
            "ParamEyeLOpen", "ParamEyeROpen", "ParamAngleZ", "ParamMouthVariant"
        };

        static readonly string[] RequiredMaskDrawables = { "Eye_Mask_L", "Eye_Mask_R" };

        [MenuItem("Seabreeze/Configure Liting v02-r5")]
        public static void Configure()
        {
            EnsureEditorOnly();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImport();
            AssetDatabase.ImportAsset(ModelJsonPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // The Cubism model importer may touch the referenced texture while
            // rebuilding the prefab. Re-apply the portrait settings afterwards
            // so the final serialized importer state is the one we validate.
            ConfigureTextureImport();

            var prefab = RequireAsset<GameObject>(PrefabPath, "Cubism model prefab");
            RequireAsset<CubismMoc>(MocPath, "Cubism model asset");
            RequireNativeExpressions();
            ValidateModelAndRenderer(prefab);
            var profile = ConfigureProfile(prefab);
            ValidateProfile(profile, prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("LITING_R5_CONFIGURED: " + ProfilePath);
        }

        [MenuItem("Seabreeze/Validate Liting v02-r5")]
        public static void ValidateMenu()
        {
            EnsureEditorOnly();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var prefab = RequireAsset<GameObject>(PrefabPath, "Cubism model prefab");
            RequireAsset<CubismMoc>(MocPath, "Cubism model asset");
            RequireNativeExpressions();
            ValidateModelAndRenderer(prefab);
            var profile = RequireAsset<Live2DModelProfile>(ProfilePath, "Liting Live2D profile");
            ValidateProfile(profile, prefab);
            Debug.Log("LITING_R5_VALIDATED: prefab=" + PrefabPath + ", profile=" + ProfilePath);
        }

        [MenuItem("Seabreeze/Validate Liting v02-r5 Batch")]
        public static void ValidateBatch()
        {
            ValidateMenu();
            var prefab = RequireAsset<GameObject>(PrefabPath, "Cubism model prefab");
            var profile = RequireAsset<Live2DModelProfile>(ProfilePath, "Liting Live2D profile");
            var bawl = profile.FindExpressionTransition("bawl");
            var sigh = profile.FindExpressionTransition("sigh");
            if (bawl == null || sigh == null)
                throw new InvalidOperationException("Liting batch sequence configuration is incomplete.");

            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var model = instance.GetComponent<CubismModel>();
                if (model == null) throw new InvalidOperationException("Liting batch instance has no CubismModel.");
                model.ForceUpdateNow();
                var driver = instance.AddComponent<Live2DStateDriver>();
                driver.Configure(profile);

                driver.SetState("bawl", false);
                ExpectParameter(model, "ParamExpression", 9f, "bawl intro");
                ExpectParameter(model, "ParamExpressionMix", 1f, "bawl intro mix");
                driver.SetState("bawl", true);
                driver.StepForValidation(Mathf.Max(.01f, bawl.introSeconds * .5f));
                ExpectParameter(model, "ParamExpression", 9f, "bawl intro remains discrete");
                ExpectParameter(model, "ParamExpressionMix", 1f, "bawl intro mix remains discrete");
                driver.SetState("bawl", false); // same emotion: must not restart the intro.
                driver.StepForValidation(Mathf.Max(.01f, bawl.introSeconds * .6f));
                ExpectParameter(model, "ParamExpression", 10f, "bawl final");
                ExpectParameter(model, "ParamExpressionMix", 1f, "bawl final mix");

                driver.SetState("sigh", false);
                ExpectParameter(model, "ParamExpression", 12f, "sigh intro");
                ExpectParameter(model, "ParamExpressionMix", 1f, "sigh intro mix");
                driver.StepForValidation(Mathf.Max(.01f, sigh.introSeconds + .02f));
                ExpectParameter(model, "ParamExpression", 13f, "sigh final");
                ExpectParameter(model, "ParamExpressionMix", 1f, "sigh final mix");

                driver.SetState("bawl", false);
                driver.StepForValidation(Mathf.Max(.01f, bawl.introSeconds * .4f));
                driver.SetState("smile", false); // interruption cancels the pending bawl final.
                ExpectParameter(model, "ParamExpression", 1f, "interruption expression");
                ExpectParameter(model, "ParamExpressionMix", 1f, "interruption mix");
                driver.StepForValidation(Mathf.Max(.01f, bawl.introSeconds + .02f));
                ExpectParameter(model, "ParamExpression", 1f, "interruption stays cancelled");

                driver.SetState("sigh", false);
                driver.SetState("neutral", false); // neutral also clears the pending sigh intro.
                ExpectParameter(model, "ParamExpression", 0f, "neutral reset");
                ExpectParameter(model, "ParamExpressionMix", 0f, "neutral mix reset");
                driver.StepForValidation(Mathf.Max(.01f, sigh.introSeconds + .02f));
                ExpectParameter(model, "ParamExpression", 0f, "neutral reset stays");
                Debug.Log("LITING_R5_BATCH_OK: bawl_dry->bawl, sigh_in->sigh, same-emotion hold, interruption, and neutral reset");
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void ExpectParameter(CubismModel model, string id, float expected, string label)
        {
            foreach (var parameter in model.Parameters)
            {
                if (parameter == null || parameter.Id != id) continue;
                if (Mathf.Abs(parameter.Value - expected) > .001f)
                    throw new InvalidOperationException("Liting batch " + label + " expected " + id + "=" + expected + ", got " + parameter.Value + ".");
                return;
            }
            throw new InvalidOperationException("Liting batch parameter is missing: " + id);
        }

        static void EnsureEditorOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Liting v02-r5 import/configuration must run outside Play mode.");
        }

        static void ConfigureTextureImport()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Liting texture importer is unavailable: " + TexturePath);

            importer.textureType = TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            SetPortraitPlatformSettings(importer, "DefaultTexturePlatform");
            SetPortraitPlatformSettings(importer, "Standalone");
            importer.SaveAndReimport();

            var reloaded = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (reloaded == null)
                throw new InvalidOperationException("Liting texture importer disappeared after reimport: " + TexturePath);
            var defaultPlatform = reloaded.GetPlatformTextureSettings("DefaultTexturePlatform");
            var standalone = reloaded.GetPlatformTextureSettings("Standalone");
            if (reloaded.maxTextureSize != 4096 || reloaded.textureType != TextureImporterType.Default ||
                reloaded.spriteImportMode != SpriteImportMode.None || reloaded.mipmapEnabled ||
                reloaded.filterMode != FilterMode.Bilinear || defaultPlatform.maxTextureSize != 4096 ||
                defaultPlatform.textureCompression != TextureImporterCompression.Uncompressed ||
                standalone.maxTextureSize != 4096 || standalone.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Liting texture import settings did not stick: " + TexturePath);
        }

        static void SetPortraitPlatformSettings(TextureImporter importer, string platform)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.maxTextureSize = 4096;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }

        static void RequireNativeExpressions()
        {
            foreach (var name in NativeExpressions)
            {
                var jsonPath = ModelRoot + "/" + name + ".exp3.json";
                var assetPath = ModelRoot + "/" + name + ".exp3.asset";
                if (!File.Exists(jsonPath))
                    throw new FileNotFoundException("Missing native Liting expression JSON", jsonPath);
                var data = AssetDatabase.LoadAssetAtPath<CubismExpressionData>(assetPath);
                if (data == null)
                    throw new InvalidOperationException("Cubism expression import is incomplete: " + assetPath);
                RequireExpressionParameter(data, "ParamExpression");
                RequireExpressionParameter(data, "ParamExpressionMix");
            }
        }

        static Live2DModelProfile ConfigureProfile(GameObject prefab)
        {
            var profile = AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if (profile == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
                profile = ScriptableObject.CreateInstance<Live2DModelProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.prefab = prefab;
            profile.characterId = "liting";
            // Renderer 1 is the project's Cubism URP renderer; renderer 0 is the scene 2D renderer.
            profile.rendererIndex = 1;
            profile.renderLayer = 30;
            profile.modelPosition = Vector3.zero;
            profile.modelScale = 1f;
            profile.cameraCenter = Vector2.zero;
            profile.orthographicSize = 2f;
            profile.autoFrame = true;
            profile.visibleHeight = .78f;
            profile.verticalFocus = .62f;
            profile.uvRect = new Rect(0f, 0f, 1f, 1f);
            profile.textureHeight = 1200;
            profile.defaultEmotion = "neutral";
            profile.proceduralIdle = true;
            profile.proceduralTalk = true;
            profile.expressionFadeSeconds = .24f;
            profile.clearExpressionOnInterrupt = true;
            profile.discreteExpressionParameters = true;
            profile.expressionIndexParameter = "ParamExpression";
            profile.expressionMixParameter = "ParamExpressionMix";
            profile.expressionTransitions = new[]
            {
                new Live2DModelProfile.ExpressionTransition
                {
                    finalEmotion = "bawl", introEmotion = "bawl_dry", introSeconds = .18f
                },
                new Live2DModelProfile.ExpressionTransition
                {
                    finalEmotion = "sigh", introEmotion = "sigh_in", introSeconds = .18f
                }
            };
            profile.mouthParameter = "ParamMouthOpenY";
            profile.breathParameter = "ParamBreath";
            profile.leftEyeParameter = "ParamEyeLOpen";
            profile.rightEyeParameter = "ParamEyeROpen";
            profile.animatorController = null;
            profile.idleState = "Idle";
            profile.talkState = "Talk";
            profile.expressions = BuildExpressionBindings();
            profile.authoringNotes =
                "李庭 v02-r5 原生正式运行包；表情离散驱动 ParamExpression 与 ParamExpressionMix，" +
                "口型只驱动 ParamMouthOpenY。服务端单键 bawl 自动短播 bawl_dry→bawl，单键 sigh 自动短播 sigh_in→sigh；" +
                "同情绪说话/停嘴不重启，换表情先将旧表情参数归零；" +
                "troubled 兼容 curious 别名。Mask 使用 Cubism 原生 Eye_Mask_L/Eye_Mask_R 与 CubismMaskTextureController，" +
                "rendererIndex=1 指向项目 Cubism URP renderer。";
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static Live2DModelProfile.ExpressionBinding[] BuildExpressionBindings()
        {
            var bindings = new List<Live2DModelProfile.ExpressionBinding>();
            foreach (var name in NativeExpressions)
                bindings.Add(Bind(name, name));

            // Backend compatibility aliases. Keep the native files untouched.
            bindings.Add(Bind("happy", "smile"));
            bindings.Add(Bind("sad", "disappointed"));
            bindings.Add(Bind("shy", "blush"));
            bindings.Add(Bind("troubled", "curious"));
            bindings.Add(Bind("bawling", "bawl"));
            return bindings.ToArray();
        }

        static Live2DModelProfile.ExpressionBinding Bind(string key, string nativeName)
        {
            var expression = AssetDatabase.LoadAssetAtPath<CubismExpressionData>(ModelRoot + "/" + nativeName + ".exp3.asset");
            if (expression == null)
                throw new InvalidOperationException("Cannot bind Liting expression " + key + " -> " + nativeName);
            return new Live2DModelProfile.ExpressionBinding { emotion = key, expression = expression };
        }

        static void ValidateModelAndRenderer(GameObject prefab)
        {
            if (prefab == null)
                throw new InvalidOperationException("Liting Cubism prefab is null.");
            var model = prefab.GetComponent<CubismModel>();
            if (model == null)
                throw new InvalidOperationException("Liting prefab has no CubismModel component.");

            var parameterNames = new HashSet<string>(prefab.GetComponentsInChildren<CubismParameter>(true)
                .Where(p => p != null && !string.IsNullOrEmpty(p.gameObject.name))
                .Select(p => p.gameObject.name), StringComparer.Ordinal);
            foreach (var id in RequiredParameters)
                if (!parameterNames.Contains(id))
                    throw new InvalidOperationException("Liting prefab is missing parameter: " + id);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null || texture.width != 4096 || texture.height != 4096)
                throw new InvalidOperationException("Liting texture is not the expected 4096x4096 asset: " + TexturePath);

            var drawables = prefab.GetComponentsInChildren<CubismDrawable>(true);
            var renderers = prefab.GetComponentsInChildren<CubismRenderer>(true);
            if (drawables.Length == 0 || renderers.Length == 0)
                throw new InvalidOperationException("Liting prefab has no Cubism drawable/renderer set.");
            foreach (var renderer in renderers)
                if (renderer == null || renderer.MainTexture != texture)
                    throw new InvalidOperationException("Liting renderer texture mismatch at " +
                        (renderer == null ? "<null>" : renderer.gameObject.name));

            foreach (var maskName in RequiredMaskDrawables)
            {
                var mask = drawables.FirstOrDefault(d => d != null && d.gameObject.name == maskName);
                if (mask == null || mask.GetComponent<CubismRenderer>() == null)
                    throw new InvalidOperationException("Liting mask drawable is not renderer-bound: " + maskName);
            }

            if (prefab.GetComponent<CubismMaskTextureController>() == null)
                throw new InvalidOperationException("Liting prefab has no CubismMaskTextureController.");
            if (prefab.GetComponent<CubismRenderController>() == null)
                throw new InvalidOperationException("Liting prefab has no CubismRenderController.");
            if (prefab.GetComponent<CubismUpdateController>() == null)
                throw new InvalidOperationException("Liting prefab has no CubismUpdateController.");

            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                var renderer = pipeline.GetRenderer(1);
                if (renderer == null)
                    throw new InvalidOperationException("Project URP renderer index 1 is unavailable for Live2D.");
                Debug.Log("LITING_R5_RENDERER_OK: index=1 type=" + renderer.GetType().FullName);
            }
            else
            {
                Debug.LogWarning("LITING_R5_RENDERER_UNVERIFIED: GraphicsSettings.defaultRenderPipeline is null in this editor session.");
            }

            Debug.Log("LITING_R5_MODEL_OK: parameters=" + parameterNames.Count + ", drawables=" + drawables.Length +
                ", renderers=" + renderers.Length + ", texture=" + texture.width + "x" + texture.height);
        }

        static void ValidateProfile(Live2DModelProfile profile, GameObject prefab)
        {
            if (profile == null || profile.prefab != prefab || profile.characterId != "liting")
                throw new InvalidOperationException("Liting Profile does not point at the expected prefab/character ID.");
            if (profile.rendererIndex != 1 || profile.mouthParameter != "ParamMouthOpenY")
                throw new InvalidOperationException("Liting Profile renderer or mouth parameter is incorrect.");
            if (!profile.clearExpressionOnInterrupt)
                throw new InvalidOperationException("Liting Profile must clear expression parameters on interruption.");
            if (!profile.discreteExpressionParameters || profile.expressionIndexParameter != "ParamExpression" ||
                profile.expressionMixParameter != "ParamExpressionMix")
                throw new InvalidOperationException("Liting Profile must use discrete ParamExpression and ParamExpressionMix.");

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in profile.expressions ?? Array.Empty<Live2DModelProfile.ExpressionBinding>())
            {
                if (binding == null || string.IsNullOrEmpty(binding.emotion) || binding.expression == null)
                    throw new InvalidOperationException("Liting Profile contains an empty expression binding.");
                if (!keys.Add(binding.emotion))
                    throw new InvalidOperationException("Liting Profile contains duplicate expression key: " + binding.emotion);
                RequireExpressionParameter(binding.expression, "ParamExpression");
                RequireExpressionParameter(binding.expression, "ParamExpressionMix");
            }

            foreach (var key in new[] { "bawl_dry", "bawl", "sigh_in", "sigh", "troubled", "curious" })
                if (!keys.Contains(key))
                    throw new InvalidOperationException("Liting Profile is missing backend expression key: " + key);

            ValidateTransition(profile, "bawl", "bawl_dry");
            ValidateTransition(profile, "sigh", "sigh_in");
        }

        static void ValidateTransition(Live2DModelProfile profile, string finalEmotion, string introEmotion)
        {
            var transition = profile.FindExpressionTransition(finalEmotion);
            if (transition == null || transition.introSeconds <= .001f ||
                !string.Equals(transition.introEmotion, introEmotion, StringComparison.OrdinalIgnoreCase) ||
                profile.FindExpression(finalEmotion) == null || profile.FindExpression(introEmotion) == null)
                throw new InvalidOperationException("Liting Profile transition is invalid: " + introEmotion + " -> " + finalEmotion);
        }

        static void RequireExpressionParameter(CubismExpressionData expression, string id)
        {
            if (expression == null || expression.Parameters == null || !expression.Parameters.Any(p => p.Id == id))
                throw new InvalidOperationException("Expression " + (expression == null ? "<null>" : expression.name) +
                    " is missing " + id + ".");
        }

        static T RequireAsset<T>(string path, string label) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException(label + " is unavailable: " + path);
            return asset;
        }
    }
}
#endif
