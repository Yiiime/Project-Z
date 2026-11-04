#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZDialogueDemo.EditorTools
{
    /// <summary>
    /// Opens the already-generated Demo scene and enters Play Mode so the
    /// opt-in runtime verifier can consume the command-line QA arguments.
    /// This entry point never rebuilds or rewrites the Demo scene.
    /// </summary>
    public static class ZDialogueDemoQaRunner
    {
        private const string ScenePath = "Assets/ZDialogueDemo/Scenes/ZDialogueDemo.unity";
        private const string MotionQaPendingKey = "ZDialogueDemo.MotionQa.Pending";

        [InitializeOnLoadMethod]
        private static void InstallPlayModeRecovery()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem("Tools/Z Dialogue/Open Demo Scene")]
        public static void Open()
        {
            OpenDemoScene(enterPlayMode: false);
        }

        /// <summary>
        /// 进入现有 Demo 场景，临时记录连续动作的真实参数轨迹和截图。
        /// 不重建、不保存场景；QA 结束后自动退出 Play Mode。
        /// </summary>
        [MenuItem("Tools/Z Dialogue/Run Motion Trajectory QA")]
        public static void RunMotionTrajectoryQa()
        {
            if (!OpenDemoScene(enterPlayMode: false))
                return;

            // SessionState survives the editor domain reload that can happen while
            // entering Play Mode, so the request cannot be lost between delayCall
            // and the first runtime frame.
            SessionState.SetBool(MotionQaPendingKey, true);
            EditorApplication.delayCall -= EnterPlayMode;
            EditorApplication.delayCall += EnterPlayMode;
            EditorApplication.delayCall -= StartMotionTrajectoryQa;
            EditorApplication.delayCall += StartMotionTrajectoryQa;
        }

        /// <summary>
        /// Command-line entry point:
        /// -executeMethod ZDialogueDemo.EditorTools.ZDialogueDemoQaRunner.Run
        ///
        /// The runtime ZDemoVerification component remains opt-in. It reads
        /// --z-demo-verify, --z-demo-chat and --z-demo-quit from the existing
        /// Unity process command line after Play Mode starts.
        /// </summary>
        public static void Run()
        {
            if (!OpenDemoScene(enterPlayMode: true))
                return;

            EditorApplication.delayCall -= EnterPlayMode;
            EditorApplication.delayCall += EnterPlayMode;
        }

        private static bool OpenDemoScene(bool enterPlayMode)
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[ZDialogueDemo QA] 已取消打开 Demo 场景。");
                return false;
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError("[ZDialogueDemo QA] 找不到已生成场景：" + ScenePath);
                return false;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[ZDialogueDemo QA] 无法打开场景：" + ScenePath);
                return false;
            }

            Debug.Log("[ZDialogueDemo QA] 已打开现有场景：" + ScenePath +
                      (enterPlayMode ? "，等待进入 Play Mode。" : "。"));
            return true;
        }

        private static void EnterPlayMode()
        {
            EditorApplication.delayCall -= EnterPlayMode;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Debug.Log("[ZDialogueDemo QA] 进入 Play Mode；运行时 QA 由命令行参数选择性启动。");
            EditorApplication.isPlaying = true;
        }

        private static void StartMotionTrajectoryQa()
        {
            EditorApplication.delayCall -= StartMotionTrajectoryQa;
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.delayCall += StartMotionTrajectoryQa;
                    return;
                }

                EditorApplication.isPlaying = true;
                EditorApplication.delayCall += StartMotionTrajectoryQa;
                return;
            }

            var qa = Object.FindObjectOfType<ZMotionTrajectoryQa>();
            if (qa == null)
            {
                var gameObject = new GameObject("Z Motion Trajectory QA (temporary)");
                qa = gameObject.AddComponent<ZMotionTrajectoryQa>();
                qa.Begin();
            }
            SessionState.EraseBool(MotionQaPendingKey);

            EditorApplication.update -= PollMotionTrajectoryQa;
            EditorApplication.update += PollMotionTrajectoryQa;
        }

        private static void PollMotionTrajectoryQa()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= PollMotionTrajectoryQa;
                return;
            }

            var qa = Object.FindObjectOfType<ZMotionTrajectoryQa>();
            if (qa != null && qa.IsFinished)
            {
                EditorApplication.update -= PollMotionTrajectoryQa;
                EditorApplication.isPlaying = false;
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool(MotionQaPendingKey, false))
            {
                EditorApplication.delayCall -= StartMotionTrajectoryQa;
                EditorApplication.delayCall += StartMotionTrajectoryQa;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // A user-cancelled Play Mode should not leave a stale request
                // that fires the next time an unrelated scene is played.
                SessionState.EraseBool(MotionQaPendingKey);
                EditorApplication.update -= PollMotionTrajectoryQa;
            }
        }
    }
}
#endif
