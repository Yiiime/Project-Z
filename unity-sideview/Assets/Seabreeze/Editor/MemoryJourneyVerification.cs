using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class MemoryJourneyVerification
    {
        const string Key="Moonlife.JourneyVerification";
        static bool cancelTest;static int phase;static double deadline,changed;static float startX,pauseX;
        static string output,actionId;static Task<JObject> snapshot;static JObject report=new JObject();
        static MemoryJourneyVerification(){EditorApplication.update+=Tick;}
        public static void Begin()
        {
            if(!(Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"").StartsWith("/private/tmp/",StringComparison.Ordinal))throw new Exception("Isolated save required");
            cancelTest=Array.IndexOf(Environment.GetCommandLineArgs(),"-memoryJourneyCancel")>=0;SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();EditorApplication.EnterPlaymode();
        }
        static void Require(bool condition,string text){if(!condition)throw new Exception(text);}
        static void Approach(MemoryGameController memory,NpcPresence z){WorldDirector.Instance.Player.GetComponent<PlayerController>().Teleport(new Vector2(z.transform.position.x-.9f,z.transform.position.y));memory.BeginPlayerChat("z");}
        static void Next(int value){phase=value;changed=EditorApplication.timeSinceStartup;}
        static void Capture(string suffix){ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,null)+suffix+".png");}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try
            {
                if(output==null){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"-memoryJourneyOutput");output=i>=0?a[i+1]:"/private/tmp/z-journey.json";Require(output.StartsWith("/private/tmp/"),"Temporary output required");deadline=EditorApplication.timeSinceStartup+180;Application.runInBackground=true;}
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timeout at phase "+phase);
                var game=GameSession.Instance;var memory=game?.Memory;var z=memory?.Spawner.Find("z");
                if(memory==null||!game.IsReady||z==null||!z.Movement.IsReady)return;
                if(phase==0){if(!memory.Client.CanGenerate)return;startX=z.transform.position.x;report["startX"]=startX;Approach(memory,z);Next(1);return;}
                if(phase==1){if(!memory.Client.CanGenerate||memory.Conversations.Sending)return;memory.Conversations.SendPlayer("Z，现在有空吗？我们一起去小小晴的花店看看花吧。");Next(2);return;}
                if(phase==2){if(memory.LastReply?.actorId!="z"||memory.Conversations.Sending||!memory.Client.Synchronized)return;report["dialogue"]=memory.LastReply.result.dialogue;Require(memory.LastReply.result.action==null&&memory.LastReply.result.info==null,"Private fields exposed");snapshot=MemoryVerificationSnapshot.Read(memory.Client);Next(3);return;}
                if(phase==3){if(!snapshot.IsCompleted)return;var cp=snapshot.GetAwaiter().GetResult();var proposal=cp["records"]["proposal"].First(p=>(string)p["id"]==memory.LastReply.proposalId);report["selectedAction"]=proposal["output"]["action"].DeepClone();Require((string)proposal["output"]["action"]["target"]=="flower_shop","Model did not choose flower shop; inspect its autonomous answer");actionId="action_"+memory.LastReply.proposalId;Require(memory.Actions.HasPendingMove("z"),"Missing committed trip");pauseX=z.transform.position.x;Capture("-promise");Next(4);return;}
                if(phase==4){if(EditorApplication.timeSinceStartup-changed<2)return;Require(Mathf.Abs(z.transform.position.x-pauseX)<.1f,"Moved during conversation");report["pausedWhileTalking"]=true;memory.Conversations.CancelAll();Next(5);return;}
                if(phase==5){if(Mathf.Abs(z.transform.position.x-startX)<.8f)return;report["walkingX"]=z.transform.position.x;Approach(memory,z);pauseX=z.transform.position.x;Next(6);return;}
                if(phase==6){if(EditorApplication.timeSinceStartup-changed<2||!memory.Client.CanGenerate)return;Require(Mathf.Abs(z.transform.position.x-pauseX)<.1f,"Failed to pause mid-journey");Require(memory.Actions.HasPendingMove("z"),"Conversation lost trip");report["midJourneyPause"]=true;memory.Conversations.SendPlayer(cancelTest?"我突然有事，今天不去花店了。我们先停下，下次再约好吗？":"那我们继续去花店吧，到了再聊。");Next(7);return;}
                if(phase==7){if(memory.Conversations.Sending||!memory.Client.Synchronized)return;report["continueDialogue"]=memory.LastReply.result.dialogue;memory.Conversations.CancelAll();Next(8);return;}
                if(phase==8){if(memory.Actions.HasPendingMove("z")||!memory.Client.Synchronized)return;Require(cancelTest?Mathf.Abs(z.transform.position.x-SevenCharacterCatalog.Anchor("flower_shop").x)>.5f:Mathf.Abs(z.transform.position.x-SevenCharacterCatalog.Anchor("flower_shop").x)<.15f,"Unexpected physical arrival state: "+z.Movement.LastResult?.Reason);snapshot=MemoryVerificationSnapshot.Read(memory.Client);Next(9);return;}
                if(phase==9){if(!snapshot.IsCompleted)return;var cp=snapshot.GetAwaiter().GetResult();var action=cp["records"]["action"].First(a=>(string)a["id"]==actionId);Require((string)action["status"]==(cancelTest?"cancelled":"succeeded"),"Unexpected backend action state");if(cancelTest){Require(z.Movement.SimulateDecisions,"Cancellation left NPC frozen");report["cancelledAndReleased"]=true;}report["actionReceipt"]=action.DeepClone();report["finalX"]=z.transform.position.x;if(!cancelTest)report["arrivalEvent"]=cp["records"]["event"].Last(e=>((string)e["text"]??"").Contains("已实际走到目的地"));report["passed"]=true;Approach(memory,z);Capture("-arrival");Next(10);return;}
                if(phase==10&&EditorApplication.timeSinceStartup-changed>2){memory.Conversations.CancelAll();memory.Conversations.enabled=false;Next(11);return;}
                if(phase==11&&EditorApplication.timeSinceStartup-changed>2)Finish(0);
            }
            catch(Exception e){report["passed"]=false;report["error"]=e.ToString();Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);report["phase"]=phase;File.WriteAllText(output,report.ToString());EditorApplication.isPlaying=false;EditorApplication.Exit(code);}
    }
}
