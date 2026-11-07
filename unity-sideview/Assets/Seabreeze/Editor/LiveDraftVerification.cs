using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

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
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                window.Show();
                var input = new TextField { maxLength = 360 };
                window.rootVisualElement.Add(input);
                Field(controller, "input", input);
                Invoke(controller, "ConfigureChatInput");
                foreach (int limit in new[] { 120, 360 })
                {
                    string transcript = new string('声', limit + 80);
                    Invoke(controller, "SetVoiceDraft", transcript, limit);
                    Require(input.value == transcript, "Long transcript was truncated before review.");
                    Invoke(controller, "SetVoiceDraft", "下一句", limit);
                    Require(input.maxLength == limit && input.value == "下一句", "Next draft did not restore its input limit.");
                }
                input.value="这是一条较长的旧消息";
                input.SelectRange(input.value.Length,input.value.Length);
                Invoke(controller,"ReplaceChatDraft","",0);
                Require(input.value=="" && input.cursorIndex==0 && input.selectIndex==0,"Clearing a draft left stale selection indices.");
                int revision=(int)controller.GetType().GetField("inputRevision",Private).GetValue(controller);
                Invoke(controller,"RestoreFailedDraft","发送失败的草稿",revision);
                Require(input.value=="发送失败的草稿","Failed send did not restore an untouched draft.");
                Invoke(controller,"ReplaceChatDraft","",0);
                revision=(int)controller.GetType().GetField("inputRevision",Private).GetValue(controller);
                input.value="新的草稿";
                Invoke(controller,"RestoreFailedDraft","旧草稿",revision);
                Require(input.value=="新的草稿","Failed send overwrote the next draft.");
                Invoke(controller,"ReplaceChatDraft",new string('字',119),120);
                Invoke(controller,"AppendChatEmoji","🌙");
                Require(input.value.Length==119&&!char.IsHighSurrogate(input.value[input.value.Length-1]),"Emoji was split at the input limit.");
                VerifyImePreview(controller,input);
                Invoke(controller,"ReplaceChatDraft","正在输入",120);
                keyboard.OnIMECompositionChanged(new IMECompositionString("nihao"));
                Require((bool)controller.GetType().GetProperty("ChatInputComposing",Private).GetValue(controller),"New Input System IME composition was not detected.");
                Invoke(controller,"ReplaceChatDraft","不应覆盖候选词",120);
                Require(input.value=="正在输入","Composition was overwritten by a programmatic draft change.");
                input.value="正在输入你好";
                keyboard.OnIMECompositionChanged(new IMECompositionString(""));
                Require((bool)controller.GetType().GetProperty("ChatInputComposing",Private).GetValue(controller),"IME commit frame can accidentally submit the message.");
                Invoke(controller,"ReleaseInputKeyboard");
                Debug.Log("LIVE_DRAFT_VERIFICATION_PASSED: transcripts, cursor reset, failed-send restoration, next draft and emoji boundaries");
            }
            finally
            {
                Invoke(controller,"ReleaseInputKeyboard");
                InputSystem.RemoveDevice(keyboard);
                window.Close();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void VerifyImePreview(object controller,TextField input)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var text=(TextElement)input.textEdition;
            object editing=typeof(TextElement).GetProperty("editingManipulator",flags).GetValue(text);
            var utilities=editing.GetType().GetProperty("editingUtilities",flags)?.GetValue(editing)
                ?? editing.GetType().GetField("editingUtilities",flags)?.GetValue(editing);
            if(utilities==null)throw new Exception("IME verification could not access the engine editing state.");
            var active=utilities.GetType().GetField("isCompositionActive",flags);
            var preview=utilities.GetType().GetMethod("GeneratePreviewString",flags);
            Invoke(controller,"ReplaceChatDraft","中文输入预览",120);
            active.SetValue(utilities,true);
            try
            {
                Invoke(controller,"ReplaceChatDraft","",120);
                Require((string)preview.Invoke(utilities,new object[]{false})=="","IME preview did not follow a cleared draft.");
                Invoke(controller,"ReplaceChatDraft","下一句",120);
                Require((string)preview.Invoke(utilities,new object[]{false})=="下一句","IME preview did not follow the new draft.");
            }
            finally{active.SetValue(utilities,false);}
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
