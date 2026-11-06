using System;
using System.IO;
using Live2D.Cubism.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZLive.EditorTools
{
    // The large Cubism generated cache is rebuilt locally, never committed.
    [InitializeOnLoad]
    public sealed class ZLiveModelSetup : IPreprocessBuildWithReport
    {
        const string Root = "Assets/Seabreeze/UI/ZLive/";
        const string JsonPath = Root + "Art/Z/Model.model3.json";
        const string ModelPath = Root + "Art/Z/Model.prefab";
        const string ResourcePath = Root + "Resources/ZLiveModel.prefab";
        public int callbackOrder => 0;

        static ZLiveModelSetup() { EditorApplication.delayCall += EnsureReady; }
        static void EnsureReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            bool missing = !File.Exists(Root + "Art/Z/Model.asset") || !File.Exists(ResourcePath);
            if (!missing)
            {
                var generatedAt = File.GetLastWriteTimeUtc(ResourcePath);
                foreach (var source in Directory.GetFiles(Root + "Art/Z"))
                    if ((source.EndsWith(".json") || source.EndsWith(".moc3") || source.EndsWith(".png")) &&
                        File.GetLastWriteTimeUtc(source) > generatedAt) { missing = true; break; }
            }
            if (missing) Prepare();
        }

        [MenuItem("Seabreeze/Prepare Live Z Model")]
        public static void Prepare()
        {
            AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null || source.GetComponent<CubismModel>()?.Moc == null)
                throw new InvalidOperationException("Cubism 未能导入 Z 模型。");
            var model = UnityEngine.Object.Instantiate(source);
            try
            {
                model.name = "ZLiveModel";
                PrefabUtility.SaveAsPrefabAsset(model, ResourcePath);
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
            AssetDatabase.SaveAssets();
            Debug.Log("[ZLive] Z 模型导入完成。");
        }

        public void OnPreprocessBuild(BuildReport report) { EnsureReady(); }
    }
}
