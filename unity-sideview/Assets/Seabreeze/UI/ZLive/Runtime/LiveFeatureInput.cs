using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ZLive {
public sealed partial class LiveFeatureController {
    bool sendingInput;
    int inputRevision;
    Keyboard inputKeyboard;
    bool imeComposing;
    int imeCommitFrame=-1;
    bool ChatInputComposing => imeComposing||imeCommitFrame==Time.frameCount;

    void BindInputKeyboard() {
        if(inputKeyboard==Keyboard.current)return;
        ReleaseInputKeyboard();inputKeyboard=Keyboard.current;
        if(inputKeyboard!=null)inputKeyboard.onIMECompositionChange+=OnChatComposition;
    }
    void OnChatComposition(IMECompositionString composition) {
        bool active=composition.Count>0;
        if(imeComposing&&!active)imeCommitFrame=Time.frameCount;
        imeComposing=active;
    }
    void ReleaseInputKeyboard() {
        if(inputKeyboard!=null)inputKeyboard.onIMECompositionChange-=OnChatComposition;
        inputKeyboard=null;imeComposing=false;imeCommitFrame=-1;
    }

    void ConfigureChatInput() {
        BindInputKeyboard();
        // Drafts are plain text. Rich-text indices must not affect an IME cursor.
        input.Query<TextElement>().ForEach(text=>text.enableRichText=false);
        input.RegisterValueChangedCallback(_=>inputRevision++);
        input.RegisterCallback<KeyDownEvent>(e=>{
            if(e.keyCode!=KeyCode.Return&&e.keyCode!=KeyCode.KeypadEnter)return;
            if(ChatInputComposing)return; // Enter belongs to the IME while choosing a character.
            e.StopImmediatePropagation();Send();
        },TrickleDown.TrickleDown);
        input.RegisterCallback<FocusOutEvent>(_=>{if(imeComposing){imeComposing=false;imeCommitFrame=Time.frameCount;}});
        input.schedule.Execute(()=>{BindInputKeyboard();UpdateChatInputLimit();}).Every(100);
    }

    void UpdateChatInputLimit() {
        if(input==null||ChatInputComposing)return;
        int limit=(bool?)state?["call"]?["active"]==true?360:120;
        int length=Math.Max(limit,input.value?.Length??0);
        if(input.maxLength!=length)input.maxLength=length;
    }

    void ReplaceChatDraft(string text,int limit=0) {
        text=text??"";
        // Voice recognition can finish while the player is composing another word.
        // Wait for commitment and keep any intervening edit instead of overwriting it.
        if(ChatInputComposing){
            int revision=inputRevision;
            IVisualElementScheduledItem pending=null;
            pending=input.schedule.Execute(()=>{
                if(disposed||revision!=inputRevision){pending.Pause();return;}
                if(ChatInputComposing)return;
                pending.Pause();ReplaceChatDraft(text,limit);
            }).Every(50);
            return;
        }
        if(limit==0)limit=(bool?)state?["call"]?["active"]==true?360:120;
        input.SelectRange(0,0);
        input.maxLength=Math.Max(limit,text.Length);
        input.value=text;
        input.SelectRange(text.Length,text.Length);
    }

    void RestoreFailedDraft(string text,int clearedRevision) {
        if(inputRevision==clearedRevision&&string.IsNullOrEmpty(input.value)&&!ChatInputComposing)
            ReplaceChatDraft(text);
        // If another draft exists, keep it; the failed send is reported separately.
    }

    void AppendChatEmoji(string emoji) {
        if(ChatInputComposing)return;
        string draft=input.value+emoji;
        int end=Math.Min(input.maxLength,draft.Length);
        if(end>0&&end<draft.Length&&char.IsHighSurrogate(draft[end-1]))end--;
        ReplaceChatDraft(draft.Substring(0,end));
    }
}
}
