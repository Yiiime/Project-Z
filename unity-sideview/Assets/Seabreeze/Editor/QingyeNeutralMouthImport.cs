#if UNITY_EDITOR
using System;
using System.Linq;
using Live2D.Cubism.Core;
using UnityEditor;
using UnityEngine;

namespace Seabreeze.Editor
{
    public static class QingyeNeutralMouthImport
    {
        const string ModelRoot = "Assets/Seabreeze/Live2D/Models/Qingye/";

        public static void RefreshModel()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ModelRoot + "Model.model3.json",
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot + "Model.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var model = instance.GetComponent<CubismModel>();
                if (!model || !model.Parameters.Any(p => p.Id == "ParamQYNeutralSpeech"))
                    throw new InvalidOperationException("Missing Qingye neutral speech binding after import.");
                Debug.Log("QINGYE_DAYTIME_NEUTRAL_IMPORTED");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
#endif
