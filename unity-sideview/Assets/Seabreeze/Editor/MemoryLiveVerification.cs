using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;
namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class MemoryLiveVerification
    {
        const string Key="Moonlife.LocalLiveVerification",Message="大家好，这次见面的暗号叫蓝色风筝。想听听大家平时做什么。";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static int phase;static double deadline,changed;static string output;static Task<JObject> snapshot;
        static LiveFeatureController feature;static JObject report=new JObject();static GameUI ui;
        static MemoryLiveVerification(){EditorApplication.update+=Tick;}
        public static void Begin()
        {
            if(!(Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"").StartsWith("/private/tmp/",StringComparison.Ordinal))throw new Exception("Isolated save required");
            var connection=Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE");
            if(string.IsNullOrEmpty(connection)||!connection.StartsWith("/private/tmp/",StringComparison.Ordinal)||!File.Exists(connection))throw new Exception("Isolated live connection required");
            SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();EditorApplication.EnterPlaymode();
        }
        static T Field<T>(string name)=>(T)typeof(LiveFeatureController).GetField(name,Private).GetValue(feature);
        static void Require(bool condition,string text){if(!condition)throw new Exception(text);}
        static void Capture(string suffix){ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,null)+suffix+".png");}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try
            {
                if(output==null){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"-memoryLiveOutput");output=i>=0?a[i+1]:"/private/tmp/z-memory-live.json";Require(output.StartsWith("/private/tmp/"),"Temporary output required");deadline=EditorApplication.timeSinceStartup+260;Application.runInBackground=true;}
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timeout at phase "+phase);
                var game=GameSession.Instance;var memory=game?.Memory;if(memory==null||!game.IsReady||!memory.Client.Synchronized)return;
                if(phase==0){if(!memory.Client.CanGenerate)return;var z=memory.Spawner.Find("z");if(z==null||!z.Movement.IsReady)return;ui=UnityEngine.Object.FindFirstObjectByType<GameUI>();WorldDirector.Instance.Player.GetComponent<PlayerController>().Teleport(new Vector2(z.transform.position.x-.9f,z.transform.position.y));memory.BeginPlayerChat("z");phase=1;return;}
                if(phase==1){if(!memory.Client.CanGenerate||memory.Conversations.Sending)return;memory.Conversations.SendPlayer("你认识这个岛上的咖啡店老板吗？他是谁？");phase=2;return;}
                if(phase==2){if(memory.LastReply?.actorId!="z"||memory.Conversations.Sending)return;Require(memory.LastReply.result.dialogue.Contains("李庭"),"Z failed identity recall");Require(memory.LastReply.result.action==null&&memory.LastReply.result.info==null,"Private model fields exposed");report["islandDialogue"]=memory.LastReply.result.dialogue;report["privateFieldsHidden"]=true;snapshot=MemoryVerificationSnapshot.Read(memory.Client);phase=3;return;}
                if(phase==3){if(!snapshot.IsCompleted)return;var cp=snapshot.GetAwaiter().GetResult();var proposal=cp["records"]["proposal"].FirstOrDefault(p=>(string)p["id"]==memory.LastReply.proposalId);Require(proposal?["output"]?["action"]!=null&&proposal?["output"]?["info"]!=null,"Backend output missing");report["backendPartsRetained"]=true;Capture("-island");memory.Conversations.CancelAll();changed=EditorApplication.timeSinceStartup;phase=4;return;}
                if(phase==4){if(EditorApplication.timeSinceStartup-changed<2||memory.PlayerChatOpen||!memory.Client.CanGenerate)return;typeof(GameUI).GetMethod("OpenOverlay",Private).Invoke(ui,new object[]{"live"});phase=5;return;}
                if(phase==5){feature=UnityEngine.Object.FindFirstObjectByType<LiveFeatureController>();if(feature==null||!Field<bool>("connected"))return;var connection=JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE")));Require(Field<LiveTransport>("transport").BaseUrl==(string)connection["baseUrl"],"Live verification used another service");var state=Field<JObject>("state");if(!(state["publicEvents"]??new JArray()).Any(e=>(string)e["type"]=="speech.completed"&&(string)e["speakerId"]=="Z"))return;Require(state["room"]["participants"].Count()==8,"Room must contain eight roles");Require(memory.Client.LiveBinding!=null&&!memory.Client.CanGenerate,"Island generation not suspended");report["participants"]=state["room"]["participants"].DeepClone();typeof(LiveFeatureController).GetField("muted",Private).SetValue(feature,true);Field<LiveAudioPlayer>("audioPlayer").Source.volume=0;typeof(LiveFeatureController).GetMethod("SyncAudioCapabilities",Private).Invoke(feature,null);Field<TextField>("input").value=Message;typeof(LiveFeatureController).GetMethod("Send",Private).Invoke(feature,null);changed=EditorApplication.timeSinceStartup;phase=6;return;}
                if(phase==6){if(EditorApplication.timeSinceStartup-changed<65)return;var state=Field<JObject>("state");report["liveMessages"]=state["room"]["messages"].DeepClone();Require(state["room"]["messages"].ToString().Contains("蓝色风筝"),"Player danmaku not visible");Capture("-live");phase=13;changed=EditorApplication.timeSinceStartup;return;}
                if(phase==13){if(EditorApplication.timeSinceStartup-changed<1)return;ui.CloseOverlay();phase=7;return;}
                if(phase==7){if(memory.Client.LiveBinding!=null||UnityEngine.Object.FindFirstObjectByType<NativeLiveHost>()!=null||!memory.Client.CanGenerate)return;snapshot=MemoryVerificationSnapshot.Read(memory.Client);phase=8;return;}
                if(phase==8){if(!snapshot.IsCompleted)return;var cp=snapshot.GetAwaiter().GetResult();var delivered=cp["records"]["event"].Where(e=>(string)e["channel"]=="livestream").ToArray();var message=delivered.FirstOrDefault(e=>(string)e["text"]==Message);Require(message!=null&&message["visible"].Count()==9,"Danmaku did not reach all eight actors");Require(delivered.Any(e=>(string)e["speaker"]=="z"),"No Z playback confirmed");Require(delivered.Any(e=>(string)e["speaker"]!="z"&&(string)e["speaker"]!="player"&&(string)e["speaker"]!="engine"),"No NPC danmaku confirmed");report["deliveredLiveEvents"]=JArray.FromObject(delivered.Select(e=>new {id=(string)e["id"],speaker=(string)e["speaker"],text=(string)e["text"]}));report["memoryRestoredAfterExit"]=true;var z=memory.Spawner.Find("z");WorldDirector.Instance.Player.GetComponent<PlayerController>().Teleport(new Vector2(z.transform.position.x-.9f,z.transform.position.y));memory.BeginPlayerChat("z");phase=9;return;}
                if(phase==9){if(!memory.Client.CanGenerate||memory.Conversations.Sending)return;memory.Conversations.SendPlayer("刚才直播里，我说见面的暗号叫什么？");phase=10;return;}
                if(phase==10){if(memory.Conversations.Sending||memory.LastReply==null)return;report["afterLiveDialogue"]=memory.LastReply.result.dialogue;Require(memory.LastReply.result.dialogue.Contains("蓝色风筝"),"Z failed cross-channel recall");Capture("-return");report["passed"]=true;phase=11;changed=EditorApplication.timeSinceStartup;return;}
                if(phase==11&&EditorApplication.timeSinceStartup-changed>3){Capture("-return");phase=12;changed=EditorApplication.timeSinceStartup;return;}
                if(phase==12&&EditorApplication.timeSinceStartup-changed>1)Finish(0);
            }
            catch(Exception e){report["passed"]=false;report["error"]=e.ToString();Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);report["phase"]=phase;File.WriteAllText(output,report.ToString());EditorApplication.isPlaying=false;EditorApplication.Exit(code);}
    }
}
