using System;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;
using UnityEngine;

namespace ZLive {
public sealed partial class LiveFeatureController {
    Button endLiveButton;
    Label liveSessionLabel;
    bool stopRequested;
    float? viewEnded;
    string SessionPhase => (string)state?["sessionControl"]?["phase"] ?? ((string)state?["phase"]=="ended"?"ended":"live");
    bool SessionClosing => SessionPhase=="closing"||SessionPhase=="farewell";
    bool SessionInputBlocked => stopRequested||SessionPhase=="farewell"||SessionPhase=="ended"||
        (SessionPhase=="closing"&&(string)state?["sessionControl"]?["closeReason"]=="player");

    void BuildSessionControls() {
        endLiveButton=Action("结束直播",RequestEndLive,"end-live");
        endLiveButton.name="end-live-button";
        endLiveButton.tooltip="让 Z 收尾道别，结束本场直播";
        footer.Insert(1,endLiveButton);
        liveSessionLabel=header.Q<Label>(className:"live-label");
    }
    async void RequestEndLive() {
        if(SessionPhase=="ended"){ExitRequested?.Invoke();return;}
        if(stopRequested)return;
        if(transport==null||!connected){Toast("请先连接直播服务");return;}
        stopRequested=true;CancelCompanion();RefreshSessionControls();
        try {
            // Keep the room visible and the executor alive until Z has said goodbye.
            await transport.Control("stop_session");
            ApplyState(await transport.Request(transport.Route()));
        }catch(Exception e){stopRequested=false;Error(e);Toast("结束请求未确认，请重试");}
        finally{if(!disposed)RefreshSessionControls();}
    }
    void RefreshSessionControls() {
        if(endLiveButton==null)return;
        string phase=SessionPhase;
        bool ended=phase=="ended",closing=phase=="closing"||phase=="farewell";
        endLiveButton.text=ended?"返回游戏":stopRequested||phase=="farewell"||(closing&&(string)state?["sessionControl"]?["closeReason"]=="player")?"正在收尾…":closing?"立即收尾":"结束直播";
        endLiveButton.SetEnabled(ended||connected&&!stopRequested&&(!closing||(string)state?["sessionControl"]?["closeReason"]=="host"&&phase=="closing"));
        input?.SetEnabled(!SessionInputBlocked);
        sendButton?.SetEnabled(!sendingInput&&!SessionInputBlocked);
        pause?.SetEnabled(!closing&&!ended&&!stopRequested);
        if(liveSessionLabel!=null)liveSessionLabel.text=ended?"●  直播已结束":closing?"●  正在收尾":"●  LIVE  ·  自主对话";
        if(closing||ended){foreach(var item in flying)item.RemoveFromHierarchy();flying.Clear();replyStatus.text=ended?"✦  本场直播已结束":(string)state?["sessionControl"]?["closeReason"]=="host"?"✦  Z 正在收尾，道别前可挽留一次":"✦  Z 正在和大家道别";}
        if(closing){locallyPaused=false;audioPlayer?.Pause(false);CancelCompanion();}
        if(ended){if(!viewEnded.HasValue)viewEnded=Time.unscaledTime;CancelCompanion();stopRequested=false;PlayerPrefs.DeleteKey(SessionKey);status.text="直播已结束 · 谢谢你的陪伴";}
    }
}
}
