using System;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Seabreeze.Editor
{
    public static class MemoryExtendedVerificationEntry
    {
        public static void Begin()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-memoryExtendedOutput")<0)
                throw new InvalidOperationException("Explicit verification output is required");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            view.Show();view.Focus();EditorApplication.isPlaying=true;
        }
    }
}
