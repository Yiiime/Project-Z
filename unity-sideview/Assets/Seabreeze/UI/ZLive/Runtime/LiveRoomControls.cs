using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    void BuildDialog(VisualElement root){
        dialogLayer=Box("dialog-layer");
        dialogLayer.style.display=DisplayStyle.None;
        root.Add(dialogLayer);
        dialogContent=Box("dialog-paper");
        dialogLayer.Add(dialogContent);
    }
    void OpenDialog(string title){
        dialogContent.RemoveFromClassList("exit-paper");
        dialogContent.Clear();
        dialogContent.Add(Text(title,"dialog-title"));
        dialogLayer.style.display=DisplayStyle.Flex;
        dismissToast?.Pause();
        toast.style.display=DisplayStyle.None;
        // Tab navigation must stay in the modal, while speech and session timers continue.
        roomContent.AddToClassList("blurred");
        sidebar.SetEnabled(false);
        reopen.SetEnabled(false);
        document.rootVisualElement.Q<Button>("return-game").SetEnabled(false);
    }
    void CloseDialog(){
        dialogContent.Clear();
        dialogLayer.style.display=DisplayStyle.None;
        roomContent.RemoveFromClassList("blurred");
        sidebar.SetEnabled(true);
        reopen.SetEnabled(true);
        document.rootVisualElement.Q<Button>("return-game").SetEnabled(true);
    }
    public bool HandleBack(){
        if(dialogLayer==null)return true;
        if(dialogLayer.style.display!=DisplayStyle.None)CloseDialog();
        else ShowExitDialog();
        return true;
    }
    void ShowExitDialog(){
        if(SessionPhase=="ended"){ExitRequested?.Invoke();return;}
        if(stopRequested||SessionPhase=="farewell")return;
        OpenDialog("退出直播，返回游戏？");
        dialogContent.AddToClassList("exit-paper");
        var buttons=Box("dialog-actions");
        dialogContent.Add(buttons);
        var stay=Action("继续观看",CloseDialog,"secondary");
        stay.name="continue-watching";

        var confirm=Action("退出直播",()=>{CloseDialog();RequestEndLive();},"primary");
        confirm.name="end-live-button";
        buttons.Add(confirm);
        buttons.Add(stay);
        stay.Focus();
    }
    async Task SyncAudioCapabilities(){
        if(transport==null||!connected||locallyPaused||SessionPhase=="ended")return;
        try{await transport.Input("client.capabilities_changed",new JObject{{"audioEnabled",!muted&&audioPlayer.Source.volume>0}});}
        catch(Exception e){Error(e);}
    }
    static string VoiceErrorText(string code)=>"可继续文字聊天";
}
}
