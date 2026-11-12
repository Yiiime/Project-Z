#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Seabreeze.Editor
{
    /// <summary>
    /// Imports the approved 留洋 c01 v05-r03 puzzled/sigh runtime into the
    /// isolated Project-Z copy and creates the game-facing portrait profile.
    /// This editor step only imports assets and writes the c01 profile; Unity
    /// compilation, play mode and the real dialogue capture remain separate.
    /// </summary>
    public static class LiuyangV05ImportSetup
    {
        const string ModelRoot = "Assets/Seabreeze/Live2D/Models/Liuyang";
        const string ModelStem = "LiuyangV05PocketWatchExprPuzzledSighV02";
        const string ModelJsonPath = ModelRoot + "/" + ModelStem + ".model3.json";
        const string PrefabPath = ModelRoot + "/" + ModelStem + ".prefab";
        const string MocPath = ModelRoot + "/" + ModelStem + ".asset";
        const string ProfilePath = "Assets/Seabreeze/Resources/Memory/Portraits/Liuyang.asset";
        const string ExpectedModelJsonSha256 = "8cb8d1ec7532576889bd6e3deb4d86d0ee03188b0e46378c966d423de87394ca";
        const string ExpectedMocSha256 = "c4f865ea293c84eeb12b2a0969fd0aa900837d88c0d85402f3ca5cd5ca591999";
        const string ExpectedTexture00Sha256 = "dcbe7f249160a322da9e0a3acf6533f724fb8bbd762bae2c95e858ab3a896c11";
        const string ExpectedTexture01Sha256 = "9967e740ae16dbd1267f842e5121397bd615d7026a330f2f2602bff50bc4d652";

        static readonly string[] ExpressionFiles =
        {
            "clear", "smile", "smile_talk", "bawl_dry", "bawl_tear",
            "puzzled", "sigh_before", "sigh_after"
        };

        static readonly string[] RequiredParameters =
        {
            "ParamAngleX", "ParamAngleY", "ParamAngleZ", "ParamBodyAngleZ",
            "ParamBreath", "ParamEyeLOpen", "ParamEyeROpen", "ParamEyeSmile",
            "ParamEyeBallX", "ParamEyeBallY", "ParamBrowForm", "ParamMouthOpenY",
            "ParamMouthVariant", "ParamMouthForm", "ParamSmile", "ParamBawl",
            "ParamTear", "ParamPuzzled", "ParamSigh", "ParamSighRelease"
        };

        [MenuItem("Seabreeze/Configure Liuyang c01 v05-r03 puzzled-sigh-v02")]
        public static void Configure()
        {
            EnsureEditorOnly();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSourceHashes();
            ImportRawAssets();
            ConfigureTextures();
            AssetDatabase.ImportAsset(ModelJsonPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Cubism may reimport the referenced atlases while rebuilding the
            // prefab. Reapply the portrait settings after the model import.
            ConfigureTextures();

            var prefab = RequireAsset<GameObject>(PrefabPath, "留洋 c01 Cubism model prefab");
            RequireAsset<CubismMoc>(MocPath, "留洋 c01 Cubism model asset");
            RequireExpressions();
            ValidateModelAndRenderer(prefab);
            // The approved r02 package already contains the corrected atlas.
            // Keep the profile on the imported Cubism prefab directly; no
            // second mask/derived atlas is needed in the daytime map.
            var profile = ConfigureProfile(prefab);
            ValidateProfile(profile, prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("LIUYANG_C01_V05_R03_CONFIGURED: prefab=" + PrefabPath + ", profile=" + ProfilePath);
        }

        [MenuItem("Seabreeze/Validate Liuyang c01 v05-r03 puzzled-sigh-v02")]
        public static void ValidateMenu()
        {
            EnsureEditorOnly();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSourceHashes();
            var prefab = RequireAsset<GameObject>(PrefabPath, "留洋 c01 Cubism model prefab");
            RequireAsset<CubismMoc>(MocPath, "留洋 c01 Cubism model asset");
            RequireExpressions();
            ValidateModelAndRenderer(prefab);
            var profile = RequireAsset<Live2DModelProfile>(ProfilePath, "留洋 c01 Live2D profile");
            ValidateProfile(profile, prefab);
            Debug.Log("LIUYANG_C01_V05_R03_VALIDATED: prefab=" + PrefabPath + ", profile=" + ProfilePath);
        }

        static void EnsureEditorOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("留洋 c01 import/configuration must run outside Play mode.");
        }

        static void ValidateSourceHashes()
        {
            RequireHash(ModelJsonPath, ExpectedModelJsonSha256);
            RequireHash(ModelRoot + "/" + ModelStem + ".moc3", ExpectedMocSha256);
            RequireHash(ModelRoot + "/texture_00.png", ExpectedTexture00Sha256);
            RequireHash(ModelRoot + "/texture_01.png", ExpectedTexture01Sha256);
        }

        static void RequireHash(string assetPath, string expected)
        {
            string fullPath = ProjectFilePath(assetPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("留洋 c01 source asset is missing", fullPath);
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(fullPath)))
                    .Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("留洋 c01 source hash mismatch: " + assetPath + " expected=" + expected + " actual=" + actual);
            }
        }

        static void ImportRawAssets()
        {
            string root = ProjectFilePath(ModelRoot);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var fullPath in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                string assetPath = ToAssetPath(fullPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
        }

        static void ConfigureTextures()
        {
            foreach (var file in new[] { "texture_00.png", "texture_01.png" })
            {
                string path = ModelRoot + "/" + file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("留洋 c01 texture importer is unavailable: " + path);
                importer.textureType = TextureImporterType.Default;
                importer.spriteImportMode = SpriteImportMode.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 4096;
                importer.filterMode = FilterMode.Bilinear;
                // The Cubism atlas contains opaque matte pixels around the cutout.
                // Clamp prevents bilinear samples from wrapping into neighboring
                // white atlas pixels at the mesh/UV boundary. This is an isolated
                // Unity importer guard; the approved Runtime PNG remains unchanged.
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.ToNearest;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                SetPortraitPlatformSettings(importer, "DefaultTexturePlatform");
                SetPortraitPlatformSettings(importer, "Standalone");
                importer.SaveAndReimport();
            }
        }

        static void SetPortraitPlatformSettings(TextureImporter importer, string platform)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.maxTextureSize = 4096;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }

        static void RequireExpressions()
        {
            foreach (var name in ExpressionFiles)
            {
                string jsonPath = ModelRoot + "/expressions/" + name + ".exp3.json";
                string assetPath = ModelRoot + "/expressions/" + name + ".exp3.asset";
                if (!File.Exists(ProjectFilePath(jsonPath)))
                    throw new FileNotFoundException("Missing 留洋 c01 expression JSON", ProjectFilePath(jsonPath));
                var expression = AssetDatabase.LoadAssetAtPath<CubismExpressionData>(assetPath);
                if (expression == null || expression.Parameters == null)
                    throw new InvalidOperationException("Cubism expression import is incomplete: " + assetPath);
            }
        }

        static Live2DModelProfile ConfigureProfile(GameObject prefab)
        {
            var profile = AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if (profile == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProjectFilePath(ProfilePath)));
                profile = ScriptableObject.CreateInstance<Live2DModelProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.prefab = prefab;
            profile.characterId = "c01";
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
            profile.discreteExpressionParameters = false;
            profile.expressionIndexParameter = string.Empty;
            profile.expressionMixParameter = string.Empty;
            profile.expressionTransitions = new[]
            {
                new Live2DModelProfile.ExpressionTransition
                {
                    finalEmotion = "sigh", introEmotion = "sigh_before", introSeconds = 1.20f
                }
            };
            profile.mouthParameter = "ParamMouthOpenY";
            profile.neutralSpeechParameter = string.Empty;
            profile.talkMouthAmplitude = 1f;
            profile.allowSpeechWithMouthVariant = true;
            profile.speechAddsToExpressionTarget = true;
            profile.speechPeak = .44f;
            profile.speechRest = 0f;
            profile.speechExpressions = BuildSpeechExpressions();
            profile.tearExpression = "bawl_tear";
            profile.tearStartSeconds = 1.10f;
            profile.breathParameter = "ParamBreath";
            profile.leftEyeParameter = "ParamEyeLOpen";
            profile.rightEyeParameter = "ParamEyeROpen";
            profile.animatorController = null;
            profile.idleState = "Idle";
            profile.talkState = "Talk";
            profile.expressions = BuildExpressionBindings();
            profile.authoringNotes =
                "留洋 c01 怀表 v05-r03 puzzled-sigh-v02，Project Z 独立模型接入。" +
                "服务端只输出 troubled 或 sigh；troubled 在 Unity 侧绑定 puzzled，sigh 按 sigh_before→sigh_after 1.20 秒渐入。" +
                "ParamEyeLOpen/ParamEyeROpen、ParamMouthOpenY 为游戏可读取通道；基础对白峰值约 0.44，叹气末态眼开度 0.62、口型 0.24。" +
                "大哭沿用 bawl_dry→bawl_tear，嘴保持闭合；换句、打断和退场清理 ParamSmile、ParamBawl、ParamTear、ParamPuzzled、ParamSigh、ParamSighRelease。";
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static Live2DModelProfile.ExpressionBinding[] BuildExpressionBindings()
        {
            var bindings = new List<Live2DModelProfile.ExpressionBinding>
            {
                Bind("neutral", "clear"),
                Bind("smile", "smile"),
                Bind("smile_talk", "smile_talk"),
                Bind("bawl", "bawl_dry"),
                Bind("bawl_dry", "bawl_dry"),
                Bind("bawl_tear", "bawl_tear"),
                Bind("cry", "bawl_dry"),
                Bind("bawling", "bawl_dry"),
                Bind("puzzled", "puzzled"),
                Bind("troubled", "puzzled"),
                Bind("question", "puzzled"),
                Bind("sigh", "sigh_after"),
                Bind("sigh_before", "sigh_before"),
                Bind("sigh_after", "sigh_after")
            };
            return bindings.ToArray();
        }

        static Live2DModelProfile.SpeechExpression[] BuildSpeechExpressions()
        {
            return new[]
            {
                new Live2DModelProfile.SpeechExpression { emotion = "neutral", peak = .44f },
                new Live2DModelProfile.SpeechExpression { emotion = "smile", peak = .40f },
                new Live2DModelProfile.SpeechExpression { emotion = "smile_talk", peak = .40f },
                new Live2DModelProfile.SpeechExpression { emotion = "puzzled", peak = .44f },
                new Live2DModelProfile.SpeechExpression { emotion = "troubled", peak = .44f },
                new Live2DModelProfile.SpeechExpression { emotion = "question", peak = .44f },
                new Live2DModelProfile.SpeechExpression { emotion = "sigh", peak = .44f },
                new Live2DModelProfile.SpeechExpression { emotion = "bawl", peak = 0f },
                new Live2DModelProfile.SpeechExpression { emotion = "bawl_dry", peak = 0f },
                new Live2DModelProfile.SpeechExpression { emotion = "bawl_tear", peak = 0f },
                new Live2DModelProfile.SpeechExpression { emotion = "cry", peak = 0f }
            };
        }

        static Live2DModelProfile.ExpressionBinding Bind(string key, string nativeName)
        {
            string path = ModelRoot + "/expressions/" + nativeName + ".exp3.asset";
            var expression = AssetDatabase.LoadAssetAtPath<CubismExpressionData>(path);
            if (expression == null)
                throw new InvalidOperationException("Cannot bind 留洋 c01 expression " + key + " -> " + nativeName);
            return new Live2DModelProfile.ExpressionBinding { emotion = key, expression = expression };
        }

        static void ValidateModelAndRenderer(GameObject prefab)
        {
            if (prefab == null) throw new InvalidOperationException("留洋 c01 Cubism prefab is null.");
            var model = prefab.GetComponent<CubismModel>();
            if (model == null) throw new InvalidOperationException("留洋 c01 prefab has no CubismModel component.");

            var parameterNames = new HashSet<string>(prefab.GetComponentsInChildren<CubismParameter>(true)
                .Where(p => p != null && !string.IsNullOrEmpty(p.Id)).Select(p => p.Id), StringComparer.Ordinal);
            foreach (var id in RequiredParameters)
                if (!parameterNames.Contains(id)) throw new InvalidOperationException("留洋 c01 prefab is missing parameter: " + id);

            foreach (var file in new[] { "texture_00.png", "texture_01.png" })
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelRoot + "/" + file);
                if (texture == null || texture.width != 4096 || texture.height != 4096)
                    throw new InvalidOperationException("留洋 c01 texture is not 4096x4096: " + file);
            }

            var drawables = prefab.GetComponentsInChildren<CubismDrawable>(true);
            var renderers = prefab.GetComponentsInChildren<CubismRenderer>(true);
            if (drawables.Length == 0 || renderers.Length == 0)
                throw new InvalidOperationException("留洋 c01 prefab has no Cubism drawable/renderer set.");
            if (prefab.GetComponent<CubismRenderController>() == null ||
                prefab.GetComponent<CubismUpdateController>() == null)
                throw new InvalidOperationException("留洋 c01 prefab has no Cubism renderer or update component.");

            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null && pipeline.GetRenderer(1) == null)
                throw new InvalidOperationException("Project URP renderer index 1 is unavailable for 留洋 c01.");
            Debug.Log("LIUYANG_C01_V05_R03_MODEL_OK: parameters=" + parameterNames.Count +
                ", drawables=" + drawables.Length + ", renderers=" + renderers.Length + ", textures=4096x4096x2");
        }

        static void ValidateProfile(Live2DModelProfile profile, GameObject prefab)
        {
            if (profile == null || profile.prefab != prefab || profile.characterId != "c01")
                throw new InvalidOperationException("留洋 c01 Profile does not point at the expected prefab/actor ID.");
            if (profile.rendererIndex != 1 || profile.mouthParameter != "ParamMouthOpenY" ||
                profile.leftEyeParameter != "ParamEyeLOpen" || profile.rightEyeParameter != "ParamEyeROpen")
                throw new InvalidOperationException("留洋 c01 Profile renderer/eye/mouth configuration is incorrect.");
            if (!profile.clearExpressionOnInterrupt || !profile.speechAddsToExpressionTarget)
                throw new InvalidOperationException("留洋 c01 Profile must clear expressions and layer speech over authored holds.");
            if (!Mathf.Approximately(profile.speechPeak, .44f) || !Mathf.Approximately(profile.tearStartSeconds, 1.10f))
                throw new InvalidOperationException("留洋 c01 Profile speech/tear configuration is incorrect.");

            foreach (var key in new[] { "neutral", "smile", "smile_talk", "bawl", "bawl_dry", "bawl_tear",
                "cry", "bawling", "puzzled", "troubled", "question", "sigh", "sigh_before", "sigh_after" })
                if (profile.FindExpression(key) == null)
                    throw new InvalidOperationException("留洋 c01 Profile is missing expression key: " + key);
            var transition = profile.FindExpressionTransition("sigh");
            if (transition == null || !string.Equals(transition.introEmotion, "sigh_before", StringComparison.OrdinalIgnoreCase) ||
                !Mathf.Approximately(transition.introSeconds, 1.20f))
                throw new InvalidOperationException("留洋 c01 sigh_before -> sigh_after transition is incomplete.");
            Debug.Log("LIUYANG_C01_V05_R03_PROFILE_OK: expressionKeys=" + profile.expressions.Length +
                ", speechPeak=.44, tearStart=1.10, sighIntro=1.20");
        }

        static string ProjectFilePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        static string ToAssetPath(string fullPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string relative = fullPath.Substring(projectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
            return relative;
        }

        static T RequireAsset<T>(string path, string label) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException(label + " is unavailable: " + path);
            return asset;
        }
    }
}
#endif
