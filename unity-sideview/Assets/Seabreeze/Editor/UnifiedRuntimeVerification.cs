using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class UnifiedRuntimeVerification
    {
        static Task<JObject> backend;
        static string output;static double deadline,changedAt;static int phase;static Result result;
        [Serializable] sealed class Result {public bool passed,sceneReady,connected,playerFocusIsolated,focusReleased,internalStateHidden,backendResultRetained,screenshotCaptured;public int rosterCount,visibleActors;public string identities,dialogue,error,mode,screenshot;}
        public static void Begin()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-moonlifeVerificationOutput");if(index<0||index+1>=args.Length)throw new Exception("Verification output argument required");output=args[index+1];
            if(!output.StartsWith("/private/tmp/",StringComparison.Ordinal)&&!output.StartsWith(Path.GetTempPath(),StringComparison.Ordinal))throw new Exception("Use an isolated verification path");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            if(!Application.isBatchMode){var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();}
            result=new Result{rosterCount=SevenCharacterCatalog.Actors.Length,identities=string.Join(",",SevenCharacterCatalog.Actors.Select(a=>a.id))};phase=0;deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Unity verification wait expired");
                var game=GameSession.Instance;var memory=game?.Memory;if(memory==null||!game.IsReady||!memory.Client.Synchronized)return;
                if(phase==0)
                {
                    result.sceneReady=true;result.connected=memory.Client.Reachable;result.visibleActors=memory.Spawner.Actors.Count;result.mode=memory.Client.Mode;
                    var z=memory.Spawner.Find("z");if(z==null||!z.Movement.IsReady)return;
                    WorldDirector.Instance.Player.position=z.transform.position+Vector3.left;
                    memory.BeginPlayerChat("z");phase=1;return;
                }
                if(phase==1&&memory.Client.CanGenerate&&!memory.Conversations.Sending){memory.Conversations.SendPlayer("你认识这个岛上的咖啡店老板吗？他是谁？");phase=2;return;}
                if(phase==2&&memory.LastReply?.actorId=="z"&&!memory.Conversations.Sending)
                {
                    result.dialogue=memory.LastReply.result?.dialogue??"";
                    result.playerFocusIsolated=memory.Conversations.PlayerActor?.npcId=="z"&&memory.Spawner.Actors.Any(a=>a.npcId!="z"&&!a.Movement.InteractionHeld);
                    result.passed=result.rosterCount==8&&result.connected&&result.dialogue.Contains("李庭")&&result.playerFocusIsolated;
                    changedAt=EditorApplication.timeSinceStartup;phase=3;return;
                }
                if(phase==3&&EditorApplication.timeSinceStartup-changedAt>3)
                {
                    if(backend==null){backend=MemoryVerificationSnapshot.Read(memory.Client);return;}
                    if(!backend.IsCompleted)return;
                    if(backend.IsFaulted)throw backend.Exception;
                    Canvas.ForceUpdateCanvases();
                    var rects=memory.View.GetComponentsInChildren<RectTransform>();
                    result.internalStateHidden=!rects.Any(r=>r.name=="Action与Info"||r.name=="行动与数值");
                    var proposal=backend.Result["records"]?["proposal"]?.FirstOrDefault(p=>(string)p["id"]==memory.LastReply.proposalId);
                    result.backendResultRetained=proposal?["output"]?["action"]!=null&&proposal?["output"]?["info"]!=null&&memory.LastReply.result.action==null&&memory.LastReply.result.info==null;
                    result.passed=result.passed&&result.internalStateHidden&&result.backendResultRetained;
                    result.screenshot=Path.ChangeExtension(output,"png");ScreenCapture.CaptureScreenshot(result.screenshot);
                    changedAt=EditorApplication.timeSinceStartup;phase=4;return;
                }
                if(phase==4&&EditorApplication.timeSinceStartup-changedAt>1)
                {
                    result.screenshotCaptured=File.Exists(result.screenshot);if(!result.screenshotCaptured)result.screenshot=null;
                    if(!Application.isBatchMode)result.passed=result.passed&&result.screenshotCaptured;
                    memory.Conversations.Cancel("Verification completed");changedAt=EditorApplication.timeSinceStartup;phase=5;return;
                }
                if(phase==5&&EditorApplication.timeSinceStartup-changedAt>1)
                {
                    result.focusReleased=memory.Conversations.PlayerActor==null&&!memory.Spawner.Find("z").Movement.InteractionHeld;
                    result.passed=result.passed&&result.focusReleased;Finish();
                }
            }
            catch(Exception e){result.error=e.Message;result.passed=false;Finish();}
        }
        static void Finish(){EditorApplication.update-=Tick;File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));EditorApplication.isPlaying=false;EditorApplication.Exit(result.passed?0:1);}
    }
}
