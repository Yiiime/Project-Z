using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;

namespace Seabreeze.Editor {
// Real GameUI -> NativeLiveHost -> HTTP -> farewell -> GameClock -> save integration.
[InitializeOnLoad]
public static class LiveDayVerification {
    const string Key="ZLive.DayVerification";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static GameObject root;
    static GameSession game;
    static GameUI ui;
    static LiveFeatureController feature;
    static int stage,scenario,beforeDay,callbacks;
    static bool sawFarewell,sawExtension,extensionAccepted;
    static double deadline,changedAt;
    static string directory,saveDirectory,sessionId;
    static readonly JArray checks=new JArray();
    static LiveDayVerification(){EditorApplication.update+=Tick;}
    public static void Run(){SessionState.SetBool(Key,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();}
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
    static void Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Send(string text){Field<TextField>(feature,"input").value=text;Invoke(feature,"Send");}
    static void Open(){
        for(int rest=0;rest<4&&!game.Clock.LiveReady;rest++)game.Clock.Advance();
        Require(game.Clock.LiveReady,"Four rests must precede a daily broadcast.");
        beforeDay=game.State.day;sawFarewell=sawExtension=extensionAccepted=false;callbacks=0;
        Invoke(ui,"OpenOverlay","live");
        var host=Field<NativeLiveHost>(ui,"nativeLive");Require(host!=null,"Native live room was not opened.");
        feature=host.GetComponent<LiveFeatureController>();feature.Configure("http://127.0.0.1:4188","isolated-ui-verification");
        var original=feature.SessionEnded;
        feature.SessionEnded=id=>{callbacks++;sessionId=id;original(id);};
        stage=1;
    }
    static void Tick(){
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try{
            if(stage==0){
                directory=Path.GetFullPath("Builds/LiveSession/day-verification");Directory.CreateDirectory(directory);
                saveDirectory=Path.Combine(directory,"saves-"+Guid.NewGuid().ToString("N"));SaveRepository.VerificationDirectory=saveDirectory;
                Require(SaveRepository.Write(new SaveData{started=true,day=1,period=3}),"Could not seed isolated save.");
                root=new GameObject("Live next-day integration");game=root.AddComponent<GameSession>();game.Initialize();game.Dialogue.Cancel();
                ui=root.AddComponent<GameUI>();ui.Initialize();deadline=EditorApplication.timeSinceStartup+120;
                Open();return;
            }
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timed out at scenario "+scenario+", stage "+stage);
            if(stage==1){
                if(!Field<bool>(feature,"connected"))return;
                Require(game.State.day==beforeDay&&ui.HasOverlay&&Time.timeScale==0,"Opening advanced the day or failed to pause the world.");
                Send(scenario==0?"验收:玩家结束，刚才的晚霞很好看。":"验收:自主收播，刚才的晚霞已经聊完了。");
                stage=2;changedAt=EditorApplication.timeSinceStartup;return;
            }
            if(stage==2){
                if(EditorApplication.timeSinceStartup-changedAt<.8||Field<bool>(feature,"sendingInput"))return;
                if(scenario==0){
                    Require(feature.HandleBack(),"Return did not open confirmation.");
                    Require(!Field<bool>(feature,"stopRequested") && !Field<bool>(feature,"locallyPaused"),"Opening confirmation interrupted conversation.");
                    var button=feature.GetComponent<UIDocument>().rootVisualElement.Q<Button>("end-live-button");
                    button.Focus();using(var evt=NavigationSubmitEvent.GetPooled())button.SendEvent(evt);
                    Require(Field<bool>(feature,"stopRequested"),"Stop button was not submitted.");
                }
                stage=3;return;
            }
            if(stage==3){
                if(callbacks==0){
                    var state=Field<JObject>(feature,"state");string phase=(string)state?["sessionControl"]?["phase"];
                    if(phase=="farewell")sawFarewell=true;
                    Require(game.State.day==beforeDay,"Day advanced before live completion.");
                    if(scenario==2&&phase=="closing"&&!sawExtension){Send("验收:挽留，把晚霞的颜色说完好吗？");sawExtension=true;}
                    if(scenario==2&&phase=="live"&&(bool?)state?["sessionControl"]?["extensionActive"]==true&&!extensionAccepted){extensionAccepted=true;checks.Add("extension keeps current day");}
                    return;
                }
                Require(scenario!=2||extensionAccepted,"Extension was not actually accepted.");
                Require(callbacks==1&&sawFarewell,"Missing real farewell or duplicate auto-return callback.");
                Require(game.State.day==beforeDay+1&&game.Clock.Period==TimePeriod.Morning,"Not the next morning.");
                Require(!ui.HasOverlay&&Time.timeScale==1&&!game.InputBlocked&&!AudioListener.pause,"Main game was not restored.");
                Require(Field<Canvas>(ui,"canvas").enabled,"Main game canvas is hidden.");
                Require(Field<TextMeshProUGUI>(ui,"dateText").text.Contains((beforeDay+1).ToString()),"HUD date is stale.");
                Require(SaveRepository.Load().day==beforeDay+1,"Next day was not saved.");
                Require(!game.Clock.CompleteLiveSession(sessionId)&&game.State.day==beforeDay+1,"Duplicate receipt advanced twice.");
                game.State=SaveRepository.Load();game.Clock.Refresh();
                Require(!game.Clock.CompleteLiveSession(sessionId)&&game.State.day==beforeDay+1,"Reload replay advanced twice.");
                checks.Add(scenario==0?"player stop -> next morning once":scenario==1?"host decision -> next morning once":"one extension -> next morning once");
                ScreenCapture.CaptureScreenshot(Path.Combine(directory,"next-day-"+scenario+".png"));
                changedAt=EditorApplication.timeSinceStartup;stage=4;return;
            }
            if(stage==4){
                if(EditorApplication.timeSinceStartup-changedAt<1)return;
                scenario++;if(scenario<3){Open();return;}
                Open();stage=5;return;
            }
            if(stage==5){
                if(!Field<bool>(feature,"connected"))return;
                ui.CloseOverlay();changedAt=EditorApplication.timeSinceStartup;stage=6;return;
            }
            if(stage==6){
                if(EditorApplication.timeSinceStartup-changedAt<1)return;
                Require(game.State.day==beforeDay&&callbacks==0,"Ordinary return unexpectedly advanced a day.");
                checks.Add("ordinary return does not settle a day");Finish(0);
            }
        }catch(Exception e){checks.Add(e.ToString());Finish(1);}
    }
    static void Finish(int code){
        SessionState.SetBool(Key,false);
        if(directory!=null)File.WriteAllText(Path.Combine(directory,"report.json"),new JObject{{"passed",code==0},{"checks",checks},{"provider","local deterministic fixture; no remote model"}}.ToString());
        Time.timeScale=1; // Keep the isolated save directory through OnApplicationQuit.
        Debug.Log("LIVE_DAY_VERIFICATION_"+(code==0?"PASS":"FAIL"));EditorApplication.Exit(code);
    }
}
}
