using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;

namespace Seabreeze.Editor
{
    // Ensures editable voice transcripts retain text before the player confirms them.
    public static class LiveDraftVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Seabreeze/Verify Live Input Drafts")]
        public static void Run()
        {
            var root = new GameObject("Live draft verification") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var controller = root.AddComponent<LiveFeatureController>();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                window.Show();
                var input = new TextField { maxLength = 360 };
                window.rootVisualElement.Add(input);
                Field(controller, "input", input);
                foreach (int limit in new[] { 120, 360 })
                {
                    string transcript = new string('声', limit + 80);
                    Invoke(controller, "SetVoiceDraft", transcript, limit);
                    Require(input.value == transcript, "Long transcript was truncated before review.");
                    Invoke(controller, "SetVoiceDraft", "下一句", limit);
                    Require(input.maxLength == limit && input.value == "下一句", "Next draft did not restore its input limit.");
                }
                Debug.Log("LIVE_DRAFT_VERIFICATION_PASSED: long transcripts retain text and restore normal limits");
            }
            finally
            {
                window.Close();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void Field(object target, string name, object value)
            => target.GetType().GetField(name, Private).SetValue(target, value);
        static void Invoke(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, Private).Invoke(target, args);
        static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}
