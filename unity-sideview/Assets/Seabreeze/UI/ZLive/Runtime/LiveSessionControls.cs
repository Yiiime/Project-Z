using System;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;
using UnityEngine;

namespace ZLive {
public sealed partial class LiveFeatureController {
    Button endLiveButton;
    Label liveSessionLabel,sessionProgressDetail,sessionProgressHint,sessionTiming;
    readonly Label[] sessionSteps=new Label[5];
    double sessionSnapshotAt;
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
        var progress=Box("session-progress");progress.name="session-progress";
        progress.Add(Text("本场进度","session-progress-title"));
        var steps=Box("session-steps");progress.Add(steps);
        string[] names={"开场","互动","收尾","道别","结束"};
        for(int i=0;i<names.Length;i++){sessionSteps[i]=Text(names[i],"session-step");steps.Add(sessionSteps[i]);}
        sessionProgressDetail=Text("正在连接直播…","session-progress-detail");progress.Add(sessionProgressDetail);
        sessionProgressHint=Text("","session-progress-hint");progress.Add(sessionProgressHint);
        sessionTiming=Text("","session-progress-hint");progress.Add(sessionTiming);
        sidebar.Insert(0,progress);
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
    string RemainingTime(JToken milliseconds) {
        double remaining=Math.Max(0,((double?)milliseconds??0)-(Time.realtimeSinceStartupAsDouble-sessionSnapshotAt)*1000);
        int seconds=(int)Math.Ceiling(remaining/1000);
        return $"{seconds/60:00}:{seconds%60:00}";
    }
    void RefreshSessionProgress() {
        if(sessionProgressDetail==null)return;
        var control=state?["sessionControl"];
        string phase=(string)control?["phase"];
        int current=Array.IndexOf(new[]{"opening","live","closing","farewell","ended"},phase);
        for(int i=0;i<sessionSteps.Length;i++){
            sessionSteps[i].EnableInClassList("current",i==current);
            sessionSteps[i].EnableInClassList("past",current>=0&&i<current);
        }
        var timing=control?["timing"];
        sessionTiming.text=timing!=null&&phase!="ended"&&connected?$"计划收尾还剩 {RemainingTime(timing["targetRemainingMs"])} · 整场最多还剩 {RemainingTime(timing["hardRemainingMs"])}":"";
        string detail,hint;
        if(control==null){detail="正在连接直播…";hint="连接后显示本场实际阶段";}
        else if(!connected&&phase!="ended"){detail="连接中断，正在恢复…";hint="当前阶段待服务确认";}
        else if(phase=="ended"){detail="本场直播已结束";hint="点击“返回游戏”离开小屋";}
        else if(stopRequested&&(phase=="opening"||phase=="live")){detail="正在请求收尾…";hint="等待 Z 接到结束请求";}
        else if(phase=="farewell"){detail="Z 正在道别 · 说完这段就结束";hint="本场不再接受挽留";}
        else if(phase=="closing"){
            bool player=(string)control["closeReason"]=="player";
            detail=player?"已收到结束请求 · 正在收尾":"Z 准备收播 · 即将道别";
            hint=player?"当前内容停止后，会向大家简短道别":(int?)control["extensionUsed"]==0?"道别开始前，可在输入框挽留一次":"本场挽留已用完，接下来会道别";
        }
        else if((bool?)control?["extensionActive"]==true){
            double remaining=Math.Max(0,((double?)control["remainingMs"]??0)-(Time.realtimeSinceStartupAsDouble-sessionSnapshotAt)*1000);
            int seconds=(int)Math.Ceiling(remaining/1000);
            detail=seconds>0?$"额外交流 · 最多还剩 {seconds/60:00}:{seconds%60:00}":"额外交流已到时 · 正在收尾";
            string scope=(string)control["extensionScope"]??"约定的一小段内容";
            hint="挽留已用 1/1 · "+scope+"；聊完可提前结束";
        }
        else if(timing!=null){
            detail=(string)timing["beatLabel"]??"互动中";
            hint=timing["nextBeatInMs"]?.Type==JTokenType.Null?"正在准备收尾；你可随时结束":$"下一推进节点 {RemainingTime(timing["nextBeatInMs"])} · 可提前自然收尾";
        }
        else{detail=phase=="opening"?"正在开场 · 尚未确定结束时间":"互动中 · 尚未确定结束时间";hint="Z 会根据交流进展收尾；你可随时结束";}
        sessionProgressDetail.text=detail;sessionProgressHint.text=hint;
        sessionProgressHint.tooltip=hint;
    }
    void RefreshSessionControls() {
        if(endLiveButton==null)return;
        RefreshSessionProgress();
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
