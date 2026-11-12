using System;
using System.IO;
using Live2D.Cubism.Core;
using UnityEditor;
using UnityEngine;

namespace Seabreeze.Editor
{
    /// <summary>
    /// Creates the daily Zhoubo portrait profile after the confirmed V05 model
    /// has been imported by Cubism. The daily package intentionally contains no
    /// unconfirmed expression assets; idle and speech remain parameter-driven.
    /// </summary>
    public static class ZhouboDailyPresentationSetup
    {
        const string ModelRoot = "Assets/Seabreeze/Live2D/Models/ZhouboDaily";
        const string ModelJsonPath = ModelRoot + "/Model.model3.json";
        const string ModelPrefabPath = ModelRoot + "/Model.prefab";
        const string ModelAssetPath = ModelRoot + "/Model.asset";
        const string ProfilePath = "Assets/Seabreeze/Resources/Memory/Portraits/ZhouboDaily.asset";

        [MenuItem("Seabreeze/Configure Zhoubo daily portrait")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure the Zhoubo daily portrait outside Play mode.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(
                ModelJsonPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (prefab == null || prefab.GetComponent<CubismModel>() == null || !File.Exists(ProjectPath(ModelAssetPath)))
                throw new InvalidOperationException("Zhoubo daily V05 Cubism import has not completed.");

            Directory.CreateDirectory(ProjectPath(Path.GetDirectoryName(ProfilePath)));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var profile = AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<Live2DModelProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.prefab = prefab;
            profile.characterId = "zhou_bobo_daily";
            profile.rendererIndex = 1;
            profile.renderLayer = 30;
            profile.modelScale = 1;
            profile.autoFrame = true;
            profile.visibleHeight = .78f;
            profile.verticalFocus = .62f;
            profile.textureHeight = 1200;
            profile.defaultEmotion = "neutral";

            // V05 expressions were confirmed only in VTS. Keep this daily
            // profile neutral until those additional states are separately
            // approved and bound for Unity.
            profile.expressions = Array.Empty<Live2DModelProfile.ExpressionBinding>();
            profile.animatorController = null;
            profile.idleState = "";
            profile.talkState = "";
            profile.proceduralIdle = true;
            profile.proceduralTalk = true;
            profile.expressionFadeSeconds = .24f;
            profile.mouthParameter = "ParamMouthOpenY";
            profile.talkMouthAmplitude = 1.8f;
            profile.breathParameter = "ParamBreath";
            profile.leftEyeParameter = "ParamEyeLOpen";
            profile.rightEyeParameter = "ParamEyeROpen";
            profile.authoringNotes =
                "周伯伯日常老年形象，使用用户在 VTube Studio 确认的 V05 宽袖、加宽说话口型基础包。" +
                "Unity 适配不带未确认表情文件，保留原模型参数；使用 proceduralIdle 做呼吸、眨眼、轻微摆动，" +
                "使用 proceduralTalk 通过 ParamMouthOpenY 驱动说话。源包名为 Zhoubo_V05_TalkWide_Mouth，" +
                "导入路径为 Live2D/Models/ZhouboDaily，Model.moc3 SHA-256 为 " +
                "7862879a5c8b5fa5578095529c22150e553a1ca0624fab7c846e5acb920a4670。";

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("Zhoubo daily portrait ready: " + ProfilePath);
        }

        static string ProjectPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }
    }
}
