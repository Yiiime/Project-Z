using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seabreeze.EndingDemo
{
    public static class EndingDemoAuthoring
    {
        const string ScenePath = "Assets/Seabreeze/Dialogue/EndingDemo/EndingDemo.unity";
        [MenuItem("Seabreeze/Ending Demo/Open independent seventh-night scene")]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateScene();
        }
        [MenuItem("Seabreeze/Ending Demo/Edit this run's memories")]
        public static void EditMemories() { Application.OpenURL("http://127.0.0.1:4319/editor"); }
        public static void CreateScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Ending Camera", typeof(Camera)); camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(.04f, .07f, .11f);
            new GameObject("Seventh Night", typeof(EndingDemoView));
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();
        }
        public static void BuildMacPreview()
        {
            CreateScene();
            var path = Path.GetFullPath("Builds/EndingDemo/ProjectZ-Ending.app");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            string originalProduct = PlayerSettings.productName;
            string originalIdentifier = PlayerSettings.GetApplicationIdentifier(target);
            try {
                PlayerSettings.productName = "Project Z - Seventh Night";
                PlayerSettings.SetApplicationIdentifier(target, "com.projectz.endingdemo");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path,
                    target = BuildTarget.StandaloneOSX, options = BuildOptions.Development });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("Ending demo build failed: " + report.summary.result);
            } finally {
                PlayerSettings.productName = originalProduct;
                PlayerSettings.SetApplicationIdentifier(target, originalIdentifier);
            }
            Debug.Log("ENDING_DEMO_BUILD_OK " + path);
        }
    }
}
