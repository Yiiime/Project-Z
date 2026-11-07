using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class UnifiedRuntimeVerification
    {
        static string output;static double deadline;static int phase;static Result result;
        [Serializable] sealed class Result {public bool passed,sceneReady,connected,playerFocusIsolated;public int rosterCount,visibleActors;public string identities,dialogue,error,mode;}
        public static void Begin()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-moonlifeVerificationOutput");if(index<0||index+1>=args.Length)throw new Exception("Verification output argument required");output=args[index+1];
            if(!output.StartsWith("/private/tmp/",StringComparison.Ordinal)&&!output.StartsWith(Path.GetTempPath(),StringComparison.Ordinal))throw new Exception("Use an isolated verification path");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");result=new Result{rosterCount=SevenCharacterCatalog.Actors.Length,identities=string.Join(",",SevenCharacterCatalog.Actors.Select(a=>a.id))};phase=0;deadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
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
                    Finish();
                }
            }
            catch(Exception e){result.error=e.Message;result.passed=false;Finish();}
        }
        static void Finish(){EditorApplication.update-=Tick;File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));EditorApplication.isPlaying=false;EditorApplication.Exit(result.passed?0:1);}
    }
}
