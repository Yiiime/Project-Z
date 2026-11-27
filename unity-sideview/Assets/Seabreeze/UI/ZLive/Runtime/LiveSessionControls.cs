using System;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace ZLive {
public sealed partial class LiveFeatureController {
    bool stopRequested, retryingHost;
    bool CanRetryHost => !disposed && !leaving && connected && !locallyPaused && !SessionInputBlocked &&
        (bool?)state?["agent"]?["canRetryReply"] == true;

    async void RetryHostReply(){
        if(retryingHost || !CanRetryHost)return;
        retryingHost=true;
        retryHost.SetEnabled(false);
        try{
            await transport.Control("retry_reply", onlyIfExhausted:true);
            var updated=await transport.Request(transport.Route());
            if(!disposed && !leaving)ApplyState(updated);
        }catch(Exception e){
            if(!disposed && !leaving)Error(e);
        }finally{
            retryingHost=false;
            if(!disposed && !leaving)RefreshSessionControls();
        }
    }

    string SessionPhase => (string)state?["sessionControl"]?["phase"] ?? ((string)state?["phase"]=="ended"?"ended":"live");
    bool SessionClosing => SessionPhase=="closing"||SessionPhase=="farewell";
    bool SessionInputBlocked => stopRequested||SessionPhase=="farewell"||SessionPhase=="ended"||
        (SessionPhase=="closing"&&(string)state?["sessionControl"]?["closeReason"]=="player");
    async void RequestEndLive(){
        if(stopRequested||SessionPhase=="farewell")return;
        if(state==null && !connected){
            // Never opened a session. There is no farewell or day settlement to acknowledge.
            ExitRequested?.Invoke();return;
        }
        if(!connected){Toast("连接中断，恢复后可结束本场直播");return;}
        stopRequested=true;
        CancelCompanion();
        RefreshSessionControls();
        try{
            await transport.Control("stop_session");
            if(!disposed&&!leaving)ApplyState(await transport.Request(transport.Route()));
        }catch(Exception e){
            if(!disposed){stopRequested=false;Error(e);Toast("结束请求暂未确认");}
        }finally{if(!disposed)RefreshSessionControls();}
    }
    void RefreshSessionControls(){
        if(sendButton==null)return;
        if(SessionClosing&&locallyPaused){
            locallyPaused=false;
            audioPlayer.Pause(false);
        }
        bool blocked=!connected||SessionInputBlocked;
        retryHost.style.display=CanRetryHost?DisplayStyle.Flex:DisplayStyle.None;
        retryHost.SetEnabled(!retryingHost && CanRetryHost);
        input.SetEnabled(!blocked);
        sendButton.SetEnabled(!blocked&&!sendingInput&&!string.IsNullOrWhiteSpace(input.value));
        liveSessionLabel.text=SessionPhase=="ended"?"已结束":!connected?"连接中":SessionClosing||stopRequested?"收尾中":"• LIVE";
        status.style.display=string.IsNullOrEmpty(status.text)?DisplayStyle.None:DisplayStyle.Flex;
    }
}
}
