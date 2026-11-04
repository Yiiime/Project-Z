#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ZDialogueDemo.R5Migration
{
    /// <summary>R4.2 Built-in -> Cubism 5-r.5 URP migration for the isolated project.</summary>
    public static class ZR5Migration
    {
        private const string ScenePath = "Assets/ZDialogueDemo/Scenes/ZDialogueDemo.unity";
        private const string ModelJsonPath = "Assets/ZDialogueDemo/Models/Z/Model.model3.json";
        private const string ModelPrefabPath = "Assets/ZDialogueDemo/Models/Z/Model.prefab";
        private const string RendererPath = "Assets/Live2D/Cubism/Rendering/URP/CubismURPRenderer.asset";
        private const string PipelinePath = "Assets/ZR5Migration/Settings/ZDialogueDemoURP.asset";
        private const string ReportPath = "Assets/ZR5Migration/r5-migration-report.json";

        [Serializable]
        private sealed class MigrationReport
        {
            public string version;
            public string sdkVersion;
            public string pipeline;
            public string renderer;
            public string rendererGuid;
            public string colorSpace;
            public bool hdr;
            public bool cameraAllowHdr;
            public bool cameraAllowHdrOutput;
            public bool sceneSaved;
            public string scene;
            public string modelPrefab;
            public string modelPrefabGuid;
            public int missingScriptsBefore;
            public int missingScriptsAfter;
            public int modelCount;
            public int oldMaskComponentsRemoved;
            public bool panelRebound;
            public bool animatorControllerRebound;
            public string[] qualityPipelines;
            public string[] warnings;
        }

        private sealed class ObjectMap
        {
            public readonly Dictionary<UnityEngine.Object, UnityEngine.Object> values =
                new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        }

        [MenuItem("Tools/Z Dialogue/R5 URP/Migrate isolated scene")]
        public static void Migrate()
        {
            var report = MigrateInternal();
            Debug.Log("[ZR5Migration] report=" + ReportPath + " modelCount=" + report.modelCount +
                      " missingScripts=" + report.missingScriptsAfter);
        }

        // Batchmode entry: -executeMethod ZDialogueDemo.R5Migration.ZR5Migration.Migrate
        private static MigrationReport MigrateInternal()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var pipeline = ConfigureUrp();
            ForceReimportModel();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                throw new InvalidOperationException("无法打开R5隔离场景：" + ScenePath);

            var report = new MigrationReport
            {
                version = Application.unityVersion,
                sdkVersion = ReadSdkVersion(),
                pipeline = AssetDatabase.GetAssetPath(pipeline),
                renderer = RendererPath,
                rendererGuid = AssetDatabase.AssetPathToGUID(RendererPath),
                colorSpace = PlayerSettings.colorSpace.ToString(),
                scene = ScenePath,
                modelPrefab = ModelPrefabPath,
                modelPrefabGuid = AssetDatabase.AssetPathToGUID(ModelPrefabPath),
                warnings = new string[0],
            };
            ConfigureSceneCamera(scene, pipeline, report);
            ReplaceSceneModel(scene, report);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("R5隔离场景保存失败：" + ScenePath);
            report.sceneSaved = true;
            report.missingScriptsAfter = CountMissingScripts(scene);
            WriteReport(report);
            return report;
        }

        private static UniversalRenderPipelineAsset ConfigureUrp()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null)
                throw new InvalidOperationException("找不到SDK R5 URP renderer asset：" + RendererPath);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                EnsureFolder("Assets/ZR5Migration");
                EnsureFolder("Assets/ZR5Migration/Settings");
                // Public URP 17.6 factory; renderer list is pinned below by
                // the real serialized field name used by this package.
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "ZDialogueDemoURP";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            var serialized = new SerializedObject(pipeline);
            var rendererList = serialized.FindProperty("m_RendererDataList");
            if (rendererList == null || !rendererList.isArray)
                throw new InvalidOperationException("URP PipelineAsset 缺少 m_RendererDataList。 ");
            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            var defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (defaultIndex != null)
                defaultIndex.intValue = 0;
            var legacyRenderer = serialized.FindProperty("m_RendererData");
            if (legacyRenderer != null)
                legacyRenderer.objectReferenceValue = rendererData;
            var supportsHdr = serialized.FindProperty("m_SupportsHDR");
            if (supportsHdr != null)
                supportsHdr.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            pipeline.supportsHDR = false;
            pipeline.colorGradingMode = ColorGradingMode.LowDynamicRange;
            EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline = pipeline;

            var qualityNames = QualitySettings.names;
            var originalQualityLevel = QualitySettings.GetQualityLevel();
            for (var index = 0; index < qualityNames.Length; index++)
            {
                QualitySettings.SetQualityLevel(index, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalQualityLevel, false);
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return pipeline;
        }

        private static void ForceReimportModel()
        {
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(ModelJsonPath) == null)
                throw new InvalidOperationException("模型model3.json不存在：" + ModelJsonPath);
            RemoveMissingScriptsFromPrefabAsset();
            AssetDatabase.ImportAsset(ModelJsonPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath) == null)
                throw new InvalidOperationException("R5 importer未生成模型Prefab：" + ModelPrefabPath);
        }

        private static void RemoveMissingScriptsFromPrefabAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (prefab == null)
                return;
            var contents = PrefabUtility.LoadPrefabContents(ModelPrefabPath);
            try
            {
                var transforms = contents.GetComponentsInChildren<Transform>(true);
                var changed = false;
                for (var index = 0; index < transforms.Length; index++)
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[index].gameObject) <= 0)
                        continue;
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[index].gameObject);
                    changed = true;
                }
                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(contents, ModelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.ImportAsset(ModelPrefabPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureSceneCamera(
            Scene scene, UniversalRenderPipelineAsset pipeline, MigrationReport report)
        {
            var cameras = FindComponents<Camera>(scene);
            if (cameras.Count == 0)
                throw new InvalidOperationException("场景缺少Camera，无法配置URP renderer。");
            var camera = cameras[0];
            for (var index = 0; index < cameras.Count; index++)
            {
                if (cameras[index].CompareTag("MainCamera"))
                {
                    camera = cameras[index];
                    break;
                }
            }
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
                cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.SetRenderer(0);
            cameraData.allowHDROutput = false;
            camera.allowHDR = false;
            report.cameraAllowHdr = camera.allowHDR;
            report.cameraAllowHdrOutput = cameraData.allowHDROutput;
            report.hdr = pipeline.supportsHDR || camera.allowHDR || cameraData.allowHDROutput;
            EditorUtility.SetDirty(cameraData);
            EditorUtility.SetDirty(camera);
        }

        private static void ReplaceSceneModel(Scene scene, MigrationReport report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            var stage = FindGameObject(scene, "Z Model Stage");
            if (prefab == null || stage == null)
                throw new InvalidOperationException("R5场景缺少模型Prefab或Z Model Stage。");

            var oldInstance = FindModelRoot(stage.transform);
            var oldTransform = oldInstance != null ? oldInstance.transform : null;
            var oldAnimator = oldInstance != null ? oldInstance.GetComponent<Animator>() : null;
            var oldController = oldAnimator != null ? oldAnimator.runtimeAnimatorController : null;
            var oldPerformance = oldInstance != null
                ? oldInstance.GetComponent<ZDialogueDemo.ZPerformanceController>() : null;
            var oldLayout = oldInstance != null
                ? oldInstance.GetComponent<ZDialogueDemo.ZDemoModelLayout>() : null;
            report.missingScriptsBefore = oldInstance == null ? 0 : CountMissingScripts(oldInstance);
            report.oldMaskComponentsRemoved = report.missingScriptsBefore;

            var position = oldTransform != null ? oldTransform.localPosition : Vector3.zero;
            var rotation = oldTransform != null ? oldTransform.localRotation : Quaternion.identity;
            var scale = oldTransform != null ? oldTransform.localScale : Vector3.one;
            var instanceName = oldInstance != null ? oldInstance.name : "Z Model";
            var newInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (newInstance == null)
                throw new InvalidOperationException("R5模型Prefab实例化失败：" + ModelPrefabPath);
            newInstance.name = instanceName;
            newInstance.transform.SetParent(stage.transform, false);
            newInstance.transform.localPosition = position;
            newInstance.transform.localRotation = rotation;
            newInstance.transform.localScale = scale;

            var map = BuildObjectMap(oldInstance, newInstance);
            var newAnimator = newInstance.GetComponent<Animator>();
            if (oldAnimator != null && newAnimator != null)
            {
                EditorUtility.CopySerialized(oldAnimator, newAnimator);
                newAnimator.runtimeAnimatorController = oldController;
                report.animatorControllerRebound = newAnimator.runtimeAnimatorController == oldController;
            }
            var newPerformance = CopyUserComponent(oldPerformance, newInstance);
            var newLayout = CopyUserComponent(oldLayout, newInstance);
            RebindPerformance(newPerformance, newInstance);
            if (oldPerformance != null && newPerformance != null)
                map.values[oldPerformance] = newPerformance;
            if (oldLayout != null && newLayout != null)
                map.values[oldLayout] = newLayout;
            RemapSceneReferences(scene, map);

            var panels = FindComponents<ZDialogueDemo.DialoguePanel>(scene);
            for (var index = 0; index < panels.Count; index++)
                SetObjectReference(panels[index], "performanceController", newPerformance);
            report.panelRebound = newPerformance == null || panels.Count == 0 ||
                panels.Count > 0 && HasObjectReference(panels[0], "performanceController", newPerformance);

            if (oldInstance != null)
                UnityEngine.Object.DestroyImmediate(oldInstance);
            RemoveMissingScripts(newInstance);
            RemoveR5MaskComponents(newInstance);
            report.modelCount = newInstance.GetComponentsInChildren<CubismModel>(true).Length;
            report.missingScriptsAfter = CountMissingScripts(scene);
            EditorUtility.SetDirty(newInstance);
        }

        private static GameObject FindModelRoot(Transform stage)
        {
            for (var index = 0; index < stage.childCount; index++)
            {
                var child = stage.GetChild(index).gameObject;
                if (child.GetComponentInChildren<CubismModel>(true) != null)
                    return child;
            }
            return stage.childCount > 0 ? stage.GetChild(0).gameObject : null;
        }

        private static T CopyUserComponent<T>(T source, GameObject destination) where T : Component
        {
            if (source == null)
                return null;
            var target = destination.GetComponent<T>();
            if (target == null)
                target = destination.AddComponent<T>();
            EditorUtility.CopySerialized(source, target);
            return target;
        }

        private static void RebindPerformance(
            ZDialogueDemo.ZPerformanceController performance, GameObject modelRoot)
        {
            if (performance == null)
                return;
            SetObjectReference(performance, "cubismModel", modelRoot.GetComponent<CubismModel>());
            SetObjectReference(performance, "cubismUpdateController",
                modelRoot.GetComponent<CubismUpdateController>());
        }

        private static ObjectMap BuildObjectMap(GameObject oldRoot, GameObject newRoot)
        {
            var map = new ObjectMap();
            if (oldRoot == null)
                return map;
            var oldTransforms = oldRoot.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < oldTransforms.Length; index++)
            {
                var oldTransform = oldTransforms[index];
                var path = RelativePath(oldRoot.transform, oldTransform);
                var newTransform = string.IsNullOrEmpty(path)
                    ? newRoot.transform : newRoot.transform.Find(path);
                if (newTransform == null)
                    continue;
                map.values[oldTransform] = newTransform;
                map.values[oldTransform.gameObject] = newTransform.gameObject;
                MapComponents(oldTransform.gameObject, newTransform.gameObject, map.values);
            }
            return map;
        }

        private static void MapComponents(
            GameObject oldObject,
            GameObject newObject,
            Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var oldComponents = oldObject.GetComponents<Component>();
            var newComponents = newObject.GetComponents<Component>();
            for (var oldIndex = 0; oldIndex < oldComponents.Length; oldIndex++)
            {
                var oldComponent = oldComponents[oldIndex];
                if (oldComponent == null)
                    continue;
                var occurrence = 0;
                for (var i = 0; i < oldIndex; i++)
                {
                    if (oldComponents[i] != null && oldComponents[i].GetType() == oldComponent.GetType())
                        occurrence++;
                }
                var seen = 0;
                for (var newIndex = 0; newIndex < newComponents.Length; newIndex++)
                {
                    var newComponent = newComponents[newIndex];
                    if (newComponent == null || newComponent.GetType() != oldComponent.GetType())
                        continue;
                    if (seen == occurrence)
                    {
                        map[oldComponent] = newComponent;
                        break;
                    }
                    seen++;
                }
            }
        }

        private static void RemapSceneReferences(Scene scene, ObjectMap map)
        {
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var components = roots[rootIndex].GetComponentsInChildren<Component>(true);
                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var component = components[componentIndex];
                    if (component == null)
                        continue;
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    var enterChildren = true;
                    var changed = false;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        var oldReference = property.objectReferenceValue;
                        if (oldReference == null || !map.values.TryGetValue(oldReference, out var replacement))
                            continue;
                        property.objectReferenceValue = replacement;
                        changed = true;
                    }
                    if (changed)
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static bool SetObjectReference(
            Component component, string propertyName, UnityEngine.Object value)
        {
            if (component == null)
                return false;
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool HasObjectReference(
            Component component, string propertyName, UnityEngine.Object expected)
        {
            if (component == null)
                return false;
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(propertyName);
            return property != null && property.objectReferenceValue == expected;
        }

        private static int CountMissingScripts(Scene scene)
        {
            var count = 0;
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
                count += CountMissingScripts(roots[index]);
            return count;
        }

        private static int CountMissingScripts(GameObject root)
        {
            var count = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[index].gameObject);
            return count;
        }

        private static void RemoveMissingScripts(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[index].gameObject);
        }

        private static void RemoveR5MaskComponents(GameObject root)
        {
            // R5 renders masks in CubismRenderPassFeature.  A fresh R5
            // prefab must not retain an R4 CubismMaskController/renderer.
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                var components = transforms[index].GetComponents<Component>();
                for (var componentIndex = components.Length - 1; componentIndex >= 0; componentIndex--)
                {
                    var component = components[componentIndex];
                    if (component == null)
                        continue;
                    var fullName = component.GetType().FullName ?? component.GetType().Name;
                    if (fullName.IndexOf("CubismMask", StringComparison.OrdinalIgnoreCase) >= 0)
                        UnityEngine.Object.DestroyImmediate(component, true);
                }
            }
        }

        private static List<T> FindComponents<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                result.AddRange(roots[rootIndex].GetComponentsInChildren<T>(true));
            return result;
        }

        private static GameObject FindGameObject(Scene scene, string name)
        {
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == name)
                    return roots[index];
                var nested = FindChildByName(roots[index].transform, name);
                if (nested != null)
                    return nested.gameObject;
            }
            return null;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child.name == name)
                    return child;
                var nested = FindChildByName(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (root == target)
                return string.Empty;
            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            if (current != root)
                return string.Empty;
            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var slash = path.LastIndexOf('/');
            var parent = slash > 0 ? path.Substring(0, slash) : "Assets";
            var name = slash > 0 ? path.Substring(slash + 1) : path;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string ReadSdkVersion()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var path = Path.Combine(projectRoot, "Assets/Live2D/Cubism/cubism-info.yml");
            if (!File.Exists(path))
                return "missing";
            var text = File.ReadAllText(path);
            var match = Regex.Match(text, @"(?m)^version:\s*(.+?)\s*$");
            return match.Success ? match.Groups[1].Value : "unknown";
        }

        private static void WriteReport(MigrationReport report)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var path = Path.Combine(projectRoot, ReportPath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + "\n");
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
#endif
