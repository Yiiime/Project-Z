using System;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.MotionFade;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Seabreeze.Editor
{
    /// <summary>
    /// Creates the dialogue portrait profile after Cubism has imported the
    /// Zhoubo final v03 source model. The authored idle motion remains
    /// available on the imported model, while dialogue owns mouth motion.
    /// </summary>
    public static class ZhouboFinalV03PresentationSetup
    {
        const string ModelRoot = "Assets/Seabreeze/Live2D/Models/ZhouboFinal-v03";
        const string ModelJsonPath = ModelRoot + "/Model.model3.json";
        const string ModelPrefabPath = ModelRoot + "/Model.prefab";
        const string ModelControllerPath = ModelRoot + "/Model.controller";
        const string ModelAssetPath = ModelRoot + "/Model.asset";
        const string IdleClipPath = ModelRoot + "/motions/zhoubo_v03_idle.anim";
        const string ProfilePath = "Assets/Seabreeze/Resources/Memory/Portraits/ZhouboFinal-v03.asset";

        [MenuItem("Seabreeze/Configure Zhoubo final v03 portrait")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure the Zhoubo portrait outside Play mode.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ModelJsonPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
            if (prefab == null || prefab.GetComponent<CubismModel>() == null || !File.Exists(ProjectPath(ModelAssetPath)))
                throw new InvalidOperationException("Zhoubo final v03 Cubism import has not completed.");

            var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
            if (idleClip == null)
                throw new InvalidOperationException("Zhoubo final v03 idle AnimationClip was not generated.");

            var controller = PrepareIdleController(idleClip);

            Directory.CreateDirectory(ProjectPath(Path.GetDirectoryName(ProfilePath)));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var profile = AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<Live2DModelProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.prefab = prefab;
            profile.characterId = "zhou_bobo_final_v03";
            profile.rendererIndex = 1;
            profile.renderLayer = 30;
            profile.modelScale = 1;
            profile.autoFrame = true;
            profile.visibleHeight = .78f;
            profile.verticalFocus = .62f;
            profile.textureHeight = 1200;
            profile.defaultEmotion = "neutral";
            profile.expressions = Array.Empty<Live2DModelProfile.ExpressionBinding>();

            // Keep the authored idle state as the controller default. The
            // dialogue view must not cross-fade to a separate Talk state, so
            // the speaking driver only owns ParamMouthOpenY on top of Idle.
            profile.animatorController = controller;
            profile.idleState = "";
            profile.talkState = "";
            profile.proceduralIdle = false;
            profile.proceduralTalk = true;
            profile.expressionFadeSeconds = .24f;
            profile.mouthParameter = "ParamMouthOpenY";
            profile.talkMouthAmplitude = 1.8f;
            profile.breathParameter = "ParamBreath";
            profile.leftEyeParameter = "ParamEyeLOpen";
            profile.rightEyeParameter = "ParamEyeROpen";
            profile.authoringNotes =
                "周伯伯终局真容 v03。模型源路径为 Live2D/Models/ZhouboFinal-v03，待机由 motions/zhoubo_v03_idle.motion3.json 提供。" +
                "已关闭 proceduralIdle，保留 proceduralTalk 通过 ParamMouthOpenY 驱动说话。" +
                "使用角色独立 Animator controller 的默认 Idle 状态，不切换 Talk 状态，避免 Idle 被打断。";

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("Zhoubo final v03 portrait ready: " + ProfilePath);
        }

        static AnimatorController PrepareIdleController(AnimationClip idleClip)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ModelControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ModelControllerPath);

            var stateMachine = controller.layers[0].stateMachine;
            AnimatorState idle = null;
            foreach (var child in stateMachine.states)
                if (child.state != null && child.state.name == "Idle") idle = child.state;
            if (idle == null) idle = stateMachine.AddState("Idle", new Vector3(220, 80, 0));

            bool hasFadeObserver = false;
            foreach (var behaviour in stateMachine.behaviours)
                if (behaviour is CubismFadeStateObserver) hasFadeObserver = true;
            if (!hasFadeObserver) stateMachine.AddStateMachineBehaviour<CubismFadeStateObserver>();

            idle.motion = idleClip;
            stateMachine.defaultState = idle;

            var settings = AnimationUtility.GetAnimationClipSettings(idleClip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(idleClip, settings);
            EditorUtility.SetDirty(idleClip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static string ProjectPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }
    }
}
