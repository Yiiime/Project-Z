using System;
using System.IO;
using System.Linq;
using ProjectZ.Core;
using ProjectZ.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectZ.Editor
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/ProjectZ/Scenes/Boot.unity";
        private const string PipelinePath = "Assets/ProjectZ/Settings/Z-URP.asset";
        static ProjectSetup()
        {
            EditorApplication.delayCall += () => {
                if (!Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode &&
                    AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath) == null)
                    Prepare();
            };
        }

        [MenuItem("Project Z/Prepare Project")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/ProjectZ/Settings");
            Directory.CreateDirectory("Assets/ProjectZ/Scenes");
            AssetDatabase.Refresh();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                const string rendererPath = "Assets/ProjectZ/Settings/Z-Renderer.asset";
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    renderer.name = "Z Renderer";
                    AssetDatabase.CreateAsset(renderer, rendererPath);
                }
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Z URP";
                pipeline.msaaSampleCount = 4;
                pipeline.shadowDistance = 45;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.companyName = "ProjectZ";
            PlayerSettings.productName = "Project Z";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; // Loopback bridge only.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 0; settings.ApplyModifiedPropertiesWithoutUndo(); }
            if (!File.Exists(ScenePath))
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Project Z").AddComponent<ProjectZGame>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            // Keep runtime-created materials' shader in standalone builds.
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new BuildFailedException("URP Lit shader is unavailable.");
            var included = false;
            for (var i = 0; i < shaders.arraySize; i++) included |= shaders.GetArrayElementAtIndex(i).objectReferenceValue == lit;
            if (!included) { shaders.InsertArrayElementAtIndex(shaders.arraySize); shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = lit; }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("Project Z prepared. Open Assets/ProjectZ/Scenes/Boot.unity and press Play.");
        }

        [MenuItem("Project Z/Verify Project")]
        public static void Verify()
        {
            Prepare();
            CoastalArtSetup.Ensure();
            var catalog = JsonUtility.FromJson<EpisodeCatalog>(File.ReadAllText("Assets/ProjectZ/Resources/Content/episodes.json"));
            ContentRules.Validate(catalog);
            var font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
            if (font == null || !font.HasCharacter('你') || !font.HasCharacter('Z'))
                throw new BuildFailedException("The embedded Chinese font is missing required glyphs.");
            var session = new GameSession(catalog);
            foreach (var episode in catalog.episodes)
            {
                var request = session.Choose(episode.id, episode.choices[0].id);
                if (!session.Accept(request, ResponseRules.Authored(request), out var error)) throw new BuildFailedException(error);
            }
            session.NextDay();
            foreach (var episode in catalog.episodes)
            {
                var request = session.BeginFollowup(episode.id);
                if (!session.Accept(request, ResponseRules.Authored(request), out var error)) throw new BuildFailedException(error);
            }
            var restored = new UnityStateCodec().Decode(new UnityStateCodec().Encode(session.Snapshot()));
            GameSession.ValidateState(restored, catalog);
            if (!restored.episodes.All(p => p.followupSeen)) throw new BuildFailedException("Continuation verification failed.");
            Debug.Log("PROJECT_Z_VERIFY_PASSED: Unity JSON round-trip, events, continuations and URP preparation.");
        }

        [MenuItem("Project Z/Build Mac")]
        public static void BuildMac()
        {
            Verify();
            Directory.CreateDirectory("Builds");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "Builds/Project Z.app", target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development });
            if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException(result.summary.result.ToString());
            Debug.Log("PROJECT_Z_BUILD_PASSED: " + result.summary.outputPath);
        }
    }
}
