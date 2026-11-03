#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Seabreeze.Editor
{
    // Runtime services are deliberately rebuilt at startup; an assembly reload is not a new startup.
    // Save and end the current editor session before reloading changed scripts.
    [InitializeOnLoad]
    public static class PlayModeCompileGuard
    {
        static PlayModeCompileGuard()
        {
            CompilationPipeline.compilationStarted += BeforeCompilation;
        }

        static void BeforeCompilation(object context)
        {
            if (!EditorApplication.isPlaying || !GameSession.Instance) return;
            var session = GameSession.Instance;
            if (WorldDirector.Instance && WorldDirector.Instance.Player && session.State != null)
                session.State.playerX = WorldDirector.Instance.Player.position.x;
            session.Save();
            Debug.Log("Seabreeze: saved and stopped Play Mode before script recompilation. Press Play to resume.");
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
