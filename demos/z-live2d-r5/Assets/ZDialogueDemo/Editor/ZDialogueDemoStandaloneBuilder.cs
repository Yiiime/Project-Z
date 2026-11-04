#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZDialogueDemo.EditorTools
{
    /// <summary>
    /// Builds the already-saved Demo scene without calling the scene generator.
    ///
    /// This entry point is intentionally separate from
    /// ZDialogueDemoSceneBuilder.BuildMacDemo: the latter creates a fresh UI
    /// scene before building, while this path must preserve the user's saved
    /// scene and root-level Animator assets.
    /// </summary>
    public static class ZDialogueDemoStandaloneBuilder
    {
        private const string ScenePath = "Assets/ZDialogueDemo/Scenes/ZDialogueDemo.unity";
        private const string ModelPrefabPath = "Assets/ZDialogueDemo/Models/Z/Model.prefab";
        private const string ControllerPath = "Assets/Model.controller";
        private const string ReplyClipPath = "Assets/Reply_Nod.anim";

        [MenuItem("Tools/Z Dialogue/Build Saved Scene Mac Demo")]
        public static void BuildMacDemoPreserveScene()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!ValidateInputs(out var validationError))
                throw new InvalidOperationException(validationError);

            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                throw new InvalidOperationException("无法打开已保存 Demo 场景：" + ScenePath);

            var outputPath = Environment.GetEnvironmentVariable("Z_DEMO_BUILD_PATH");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                outputPath = Path.Combine(projectRoot, "Builds", "ZDialogueDemo.app");
            }

            outputPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("保存场景 macOS 构建失败：" + report.summary);

            Debug.Log("[ZDialogueDemo] 已按保存场景完成 macOS 构建：" + outputPath);
        }

        private static bool ValidateInputs(out string report)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                report = "场景不存在：" + ScenePath;
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath) == null)
            {
                report = "模型 Prefab 不存在：" + ModelPrefabPath;
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) == null)
            {
                report = "根 Animator Controller 不存在：" + ControllerPath;
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ReplyClipPath) == null)
            {
                report = "点头动画不存在：" + ReplyClipPath;
                return false;
            }

            report = ScenePath + " + " + ModelPrefabPath + " + " + ControllerPath + " + " + ReplyClipPath;
            return true;
        }
    }
}
#endif
