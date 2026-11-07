using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;

namespace Seabreeze.Editor {
// Uses real UI, transport and executor against verify-session-ui.mjs.
[InitializeOnLoad]
public static class LiveSessionVerification {
    const string Key="ZLive.SessionVerification";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static LiveFeatureController feature;
    static RenderTexture target;
    static UIDocument document;
    static int stage;
    static double deadline,changedAt;
    static string output;
    static bool returned;
    static LiveSessionVerification(){EditorApplication.update+=Tick;}
    public static void Run(){
        SessionState.SetBool(Key,true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    static T Field<T>(string name)=>(T)typeof(LiveFeatureController).GetField(name,Private).GetValue(feature);
    static void Invoke(string name)=>typeof(LiveFeatureController).GetMethod(name,Private).Invoke(feature,null);
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Click(Button button){
        Require(button.enabledInHierarchy,"Expected actionable button.");
        button.Focus();
        using(var submit=NavigationSubmitEvent.GetPooled())button.SendEvent(submit);
    }
    static void Tick(){
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try{
            if(stage==0){
                output=Path.GetFullPath("Builds/LiveSession/verification");Directory.CreateDirectory(output);
                deadline=EditorApplication.timeSinceStartup+50;
                Application.runInBackground=true;
                feature=new GameObject("Isolated live session verification").AddComponent<LiveFeatureController>();
                feature.Configure("http://127.0.0.1:4188","isolated-ui-verification");
                feature.ExitRequested=()=>returned=true;
                stage=1;return;
            }
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Verification timed out at stage "+stage);
            if(stage==1){
                document=feature.GetComponent<UIDocument>();
                if(document==null||document.panelSettings==null||!Field<bool>("connected"))return;
                target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);target.Create();document.panelSettings.targetTexture=target;
                var button=document.rootVisualElement.Q<Button>("end-live-button");
                Require(button!=null&&button.text=="结束直播","Always-visible stop button missing.");
                typeof(LiveFeatureController).GetField("muted",Private).SetValue(feature,true);
                Field<LiveAudioPlayer>("audioPlayer").Source.volume=0;Invoke("SyncAudioCapabilities");
                Field<TextField>("input").value="测试话题：我今天看了海边的晚霞。";
                Invoke("Send");stage=2;changedAt=EditorApplication.timeSinceStartup;return;
            }
            if(stage==2){
                if(EditorApplication.timeSinceStartup-changedAt<1||Field<bool>("sendingInput"))return;
                Require(Field<JObject>("state")["room"]["messages"].ToString().Contains("晚霞"),"Input did not reach real session.");
                Capture("before-stop.png");
                Click(document.rootVisualElement.Q<Button>("end-live-button"));
                Require(Field<bool>("stopRequested"),"Button submit did not invoke stop action.");
                Require(!Field<TextField>("input").enabledSelf,"P00 must block new input immediately.");
                stage=3;return;
            }
            if(stage==3){
                var state=Field<JObject>("state");if((string)state?["phase"]!="ended")return;
                stage=4;changedAt=EditorApplication.timeSinceStartup;return;
            }
            if(stage==4){
                if(EditorApplication.timeSinceStartup-changedAt<.3)return;
                var button=document.rootVisualElement.Q<Button>("end-live-button");
                Require(button.text=="返回游戏","Ended action did not switch to return.");
                Require(document.rootVisualElement.Q<Label>(className:"live-label").text.Contains("已结束"),"Ended header still advertises LIVE.");
                Require(!Field<Label>("replyStatus").text.Contains("正在整理"),"Ended UI claims another reply is pending.");
                Require(Field<VisualElement>("pausedOverlay").resolvedStyle.display==DisplayStyle.None,"Ended view still offers resume.");
                Require(Field<Label>("caption").text.Contains("晚霞"),"Contextual farewell was not presented.");
                Require(!Field<Button>("sidebarCallButton").enabledSelf,"Ended session permits joining a call.");
                Capture("ended.png");Click(button);Require(returned,"Return action did not reach host.");
                File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: actual stop button submit, authenticated transport, real cancel barrier, contextual text farewell completion, ended UI without resume overlay, disabled input/microphone, return callback.\nProvider: deterministic local fixture; no paid model calls.\n");
                Finish(0);
            }
        }catch(Exception error){Debug.LogException(error);if(output!=null)File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL: "+error);Finish(1);}
    }
    static void Capture(string name){
        var previous=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name),pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(pixels);}
    }
    static void Finish(int code){
        SessionState.SetBool(Key,false);
        if(document!=null)document.panelSettings.targetTexture=null;
        if(feature!=null)UnityEngine.Object.Destroy(feature.gameObject);
        if(target!=null){target.Release();UnityEngine.Object.Destroy(target);}
        Debug.Log("LIVE_SESSION_VERIFICATION_"+(code==0?"PASS":"FAIL"));EditorApplication.Exit(code);
    }
}
}
