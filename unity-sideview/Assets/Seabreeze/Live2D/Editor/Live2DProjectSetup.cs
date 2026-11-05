using System;
using System.Collections.Generic;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.Json;
using Live2D.Cubism.Rendering.URP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Seabreeze.Editor
{
    public static class Live2DProjectSetup
    {
        public const string ModelRoot = "Assets/Seabreeze/Live2D/Models/Z";
        public const string ProfilePath = "Assets/Seabreeze/Live2D/Profiles/Z.asset";
        const string RendererPath = "Assets/Live2D/Cubism/Rendering/URP/CubismURPRenderer.asset";

        [MenuItem("Seabreeze/Live2D/Configure Z Portrait")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure the Live2D renderer outside Play mode.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (renderer == null) throw new InvalidOperationException("Official Cubism URP renderer is missing.");
            // Preserve the existing 2D renderer as index zero and as the default.
            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("This project must retain its URP pipeline.");
            var pipelineObject = new SerializedObject(pipeline);
            var renderers = pipelineObject.FindProperty("m_RendererDataList");
            int rendererIndex = -1;
            for (int i = 0; i < renderers.arraySize; i++)
                if (renderers.GetArrayElementAtIndex(i).objectReferenceValue == renderer) rendererIndex = i;
            if (rendererIndex < 0)
            {
                rendererIndex = renderers.arraySize;
                renderers.InsertArrayElementAtIndex(rendererIndex);
                renderers.GetArrayElementAtIndex(rendererIndex).objectReferenceValue = renderer;
            }
            pipelineObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            // This portrait uses its own transparent RT, keep the official conventional render path.
            foreach (var feature in renderer.rendererFeatures)
                if (feature is CubismRenderPassFeature cubism) { cubism.EnableDirectRendering = false; EditorUtility.SetDirty(cubism); }
            int layer = ConfigureLayer();
            string modelFile = ModelRoot + "/Model.model3.json";
            AssetDatabase.ImportAsset(modelFile, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot + "/Model.prefab");
            if (prefab == null || prefab.GetComponent<CubismModel>() == null)
                throw new InvalidOperationException("Cubism importer has not produced Model.prefab yet. Wait for import, then run Configure Z Portrait again.");
            Directory.CreateDirectory("Assets/Seabreeze/Live2D/Profiles");
            var profile = AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            bool created = profile == null;
            if (created) { profile = ScriptableObject.CreateInstance<Live2DModelProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            profile.prefab = prefab;
            profile.characterId = "z";
            profile.rendererIndex = rendererIndex;
            profile.renderLayer = layer;
            if (created)
            {
                profile.autoFrame = true;
                profile.visibleHeight = .78f;
                profile.verticalFocus = .62f;
                profile.textureHeight = 1200;
                profile.modelScale = 1;
                profile.proceduralIdle = true;
                profile.proceduralTalk = true;
            }
            profile.expressions = new[]
            {
                Bind("neutral", null),
                Bind("shy", "z_shy"),
                Bind("sad", "z_aggrieved"),
                Bind("aggrieved", "z_aggrieved"),
                Bind("serious", "z_serious"),
                Bind("happy_wave", "z_happy_wave"),
                Bind("happy", "smile"),
                Bind("smile", "smile"),
                Bind("surprise", "surprise"),
                Bind("angry", "angry"),
                Bind("troubled", "troubled"),
                Bind("wave", "wave"),
                Bind("explain", "explain"),
                Bind("point", "point"),
                Bind("blush", "blush")
            };
            profile.authoringNotes = "User-supplied Z model. Cubism 5 SDK for Unity URP R6_1 + matching official Core. No motion3 files were supplied. Idle breath/blink and text-speaking mouth are explicitly procedural; optional Animator states may replace them. Neutral returns expression parameters to model defaults. Only Z should reference this profile.";
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("Live2D Z configured: " + ProfilePath + "; URP renderer index=" + rendererIndex + "; layer=" + layer + ". Default world 2D renderer preserved.");
        }

        static Live2DModelProfile.ExpressionBinding Bind(string key, string file)
        {
            CubismExpressionData expression = null;
            if (file != null)
            {
                string path = ModelRoot + "/" + file + ".exp3.asset";
                expression = AssetDatabase.LoadAssetAtPath<CubismExpressionData>(path);
                if (expression == null)
                {
                    var json = CubismExp3Json.LoadFrom(File.ReadAllText(ModelRoot + "/" + file + ".exp3.json"));
                    expression = CubismExpressionData.CreateInstance(json);
                    AssetDatabase.CreateAsset(expression, path);
                }
            }
            return new Live2DModelProfile.ExpressionBinding { emotion = key, expression = expression };
        }
        static int ConfigureLayer()
        {
            int existing = LayerMask.NameToLayer("Live2DPortrait");
            if (existing >= 8) return existing;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int index = 30; index >= 8; index--)
            {
                if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(index).stringValue)) continue;
                layers.GetArrayElementAtIndex(index).stringValue = "Live2DPortrait";
                tags.ApplyModifiedPropertiesWithoutUndo();
                return index;
            }
            throw new InvalidOperationException("No free user layer for isolated Live2D portrait rendering.");
        }
    }
}
