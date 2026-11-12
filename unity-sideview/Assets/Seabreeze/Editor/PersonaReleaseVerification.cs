using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;

namespace Seabreeze.Editor
{
    // Exercises current scene, real player dialogue, and the native live executor.
    // Live UI uses the isolated verify-session-ui.mjs fixture; model tests are separate.
    [InitializeOnLoad]
    public static class PersonaReleaseVerification
    {
        const string Key="Z.PersonaReleaseVerification";
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static readonly string[] Ids={"z","xiaoxiaoqing","zhou_bobo"};
        static readonly string[] Questions={"花店是谁开的？码头船长叫什么？我想认识他们。","你和留洋熟吗？压花册是你发明的吗，能直接把Z送出去吗？","你在岛上做什么工作？Z跟你很熟吗？"};
        static int phase,index;static double deadline,changed;static string output;
        static JObject report;static JArray dialogue;static LiveFeatureController live;
        static HashSet<string> viewers=new HashSet<string>();
        static PersonaReleaseVerification(){EditorApplication.update+=Tick;}
        public static void Begin()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-personaVerificationOutput");
            if(at<0||at+1>=args.Length)throw new Exception("Pass -personaVerificationOutput with a temporary output path");
            string path=Path.GetFullPath(args[at+1]);
            string save=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
            if(!Temporary(path)||!Temporary(save))throw new Exception("Verification requires isolated temporary output and save paths");
            SessionState.SetString(Key+".Output",path);SessionState.SetBool(Key,true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Show();
            EditorApplication.EnterPlaymode();
        }
        static bool Temporary(string p)=>p.StartsWith("/private/tmp/",StringComparison.Ordinal)||p.StartsWith(Path.GetTempPath(),StringComparison.Ordinal);
        static T Field<T>(string name)=>(T)typeof(LiveFeatureController).GetField(name,Private).GetValue(live);
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try
            {
                if(report==null){output=SessionState.GetString(Key+".Output","");report=new JObject();dialogue=new JArray();deadline=EditorApplication.timeSinceStartup+240;Application.runInBackground=true;}
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Persona verification timeout at phase "+phase);
                var game=GameSession.Instance;var memory=game?.Memory;
                if(memory==null||!game.IsReady||!memory.Client.Synchronized)return;
                if(phase==0)
                {
                    Require(SevenCharacterCatalog.Actors.Length==8,"Expected eight stable actors");
                    Require(SevenCharacterCatalog.Name("linxu")=="留洋"&&SevenCharacterCatalog.Name("xiaoxiaoqing")=="晴野","New display names not loaded");
                    report["roster"]=JArray.FromObject(SevenCharacterCatalog.Actors.Select(a=>new {a.id,a.displayName,a.occupation}));
                    var cp=JObject.Parse(game.State.memory.checkpoint);var rel=cp["world"]["actors"]["xiaoxiaoqing"]["peer_relations"];
                    Require((int)rel["linxu"]["trust"]>(int)rel["liyuan"]["trust"],"Story relationship baselines not available in Unity checkpoint");
                    report["relationshipBaseline"]=rel;report["model"]=memory.Client.Model;report["visibleSceneActors"]=memory.Spawner.Actors.Count;
                    phase=1;
                }
                if(phase==1)
                {
                    var actor=memory.Spawner.Find(Ids[index]);if(actor==null||!actor.Movement.IsReady)return;
                    WorldDirector.Instance.Player.position=actor.transform.position+Vector3.left;
                    memory.BeginPlayerChat(Ids[index]);phase=2;return;
                }
                if(phase==2&&memory.Client.CanGenerate&&!memory.Conversations.Sending)
                {memory.Conversations.SendPlayer(Questions[index]);phase=3;return;}
                if(phase==3&&memory.LastReply?.actorId==Ids[index]&&!memory.Conversations.Sending)
                {
                    var result=memory.LastReply.result;Require(result!=null&&!string.IsNullOrEmpty(result.dialogue),"No actual dialogue");
                    Require(result.action!=null&&result.info!=null,"Internal action/info missing");
                    Require(memory.Conversations.PlayerActor?.npcId==Ids[index]&&memory.Spawner.Find(Ids[index]).Movement.InteractionHeld,"Player focus missing");
                    if(index==0)Require(result.dialogue.Contains("晴野")&&result.dialogue.Contains("留洋"),"Z did not name current friends");
                    Require(!memory.View.GetComponentsInChildren<RectTransform>().Any(r=>r.name=="Action与Info"||r.name=="行动与数值"),"Internal state displayed in player UI");
                    dialogue.Add(new JObject{["actor"]=Ids[index],["question"]=Questions[index],["dialogue"]=result.dialogue,["actionInfoRetained"]=true});
                    Canvas.ForceUpdateCanvases();ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,"island-"+index+".png"));changed=EditorApplication.timeSinceStartup;phase=4;return;
                }
                if(phase==4&&EditorApplication.timeSinceStartup-changed>2)
                {memory.Conversations.Cancel("Persona verification next actor");phase=5;changed=EditorApplication.timeSinceStartup;return;}
                if(phase==5&&EditorApplication.timeSinceStartup-changed>1)
                {
                    Require(memory.Conversations.PlayerActor==null,"Player focus did not release");
                    index++;if(index<Ids.Length){phase=1;return;}
                    report["dialogues"]=dialogue;report["playerFocusAndInternalState"]=true;
                    live=new GameObject("Isolated persona live verification").AddComponent<LiveFeatureController>();
                    live.Configure("http://127.0.0.1:4188","isolated-ui-verification");phase=6;return;
                }
                if(phase==6)
                {
                    if(!Field<bool>("connected"))return;
                    var state=Field<JObject>("state");
                    foreach(var m in state?["publicEvents"]??new JArray())
                    {string id=(string)m["speakerId"];if((string)m["type"]=="npc.message"&&!string.IsNullOrEmpty(id))viewers.Add(id);}
                    if(viewers.Count<7)return;
                    string[] expected=SevenCharacterCatalog.Actors.Select(a=>a.id).Where(id=>id!="z").ToArray();
                    Require(viewers.SetEquals(expected),"Live viewer IDs differ from the island actors");
                    var shown=live.GetComponent<UIDocument>().rootVisualElement.Query<Label>(className:"nickname").ToList().Select(l=>l.text).ToArray();
                    Require(expected.All(id=>shown.Contains(SevenCharacterCatalog.Name(id))),"Actual live nickname labels differ from the island roster");
                    Require(live.GetComponent<UIDocument>().rootVisualElement.Query<Label>(className:"viewer-initial").ToList().Count>=5,"Existing avatar styles missing");
                    report["liveViewers"]=JArray.FromObject(viewers);report["liveUiFixture"]=true;
                    report["liveRoom"]=state["room"];ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,"live.png"));
                    changed=EditorApplication.timeSinceStartup;phase=7;return;
                }
                if(phase==7&&EditorApplication.timeSinceStartup-changed>2){report["passed"]=true;Finish(0);}
            }
            catch(Exception e){if(report==null)report=new JObject();report["passed"]=false;report["error"]=e.Message;report["phase"]=phase;Finish(1);}
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Key,false);if(live!=null)UnityEngine.Object.Destroy(live.gameObject);
            if(!string.IsNullOrEmpty(output))File.WriteAllText(output,report.ToString(Formatting.Indented));
            EditorApplication.Exit(code);
        }
    }
}
