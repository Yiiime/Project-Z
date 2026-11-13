using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class ConversationActionVerification
    {
        const string Key="Z.ConversationActionQA";
        static readonly List<TaskCompletionSource<bool>> frames=new List<TaskCompletionSource<bool>>();
        static readonly JArray checks=new JArray();static bool running;static string output;
        static JObject backendCheckpoint;
        static ConversationActionVerification(){EditorApplication.update+=Tick;}
        [MenuItem("Seabreeze/Verify Conversation Actions")]
        public static void Begin()
        {
            output=Environment.GetEnvironmentVariable("Z_CONVERSATION_QA_DIR");
            string saves=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
            if(string.IsNullOrEmpty(output)||!output.StartsWith("/private/tmp/")||string.IsNullOrEmpty(saves)||!saves.StartsWith(output+"/"))throw new Exception("Use isolated QA and save directories");
            SessionState.SetString(Key+".Output",output);SessionState.SetBool(Key,true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Show();
            EditorApplication.EnterPlaymode();
        }
        static void Tick(){foreach(var f in frames.ToArray()){frames.Remove(f);f.TrySetResult(true);}if(!running&&SessionState.GetBool(Key,false)&&EditorApplication.isPlaying&&!EditorApplication.isCompiling){running=true;output=SessionState.GetString(Key+".Output","");_ = Verify();}}
        static Task Frame(){var t=new TaskCompletionSource<bool>();frames.Add(t);return t.Task;}
        static async Task Until(Func<bool> predicate,string name,float timeout=25){float until=Time.realtimeSinceStartup+timeout;while(!predicate()){if(Time.realtimeSinceStartup>until)throw new Exception("Timeout: "+name);await Frame();}}
        static async Task Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await Frame();}
        static void NewSandbox(GameSession session)
        {
            // This verifies free NPC locomotion. First-day story checkpoints
            // are exercised separately and must not be silently skipped in play.
            var state=new SaveData{started=true,firstDay=null,place="Street",day=1,period=0,memory=new MemorySaveState()};
            state.Sanitize();if(!SaveRepository.Write(state))throw new Exception("Could not create isolated sandbox save");session.LoadSavedGame();
        }
        static void Check(string name,bool ok,string detail=""){checks.Add(new JObject{{"name",name},{"passed",ok},{"detail",detail}});if(!ok)throw new Exception(name+": "+detail);}
        static JToken Action(MemoryGameController m,string id)=>JObject.Parse(GameSession.Instance.State.memory.checkpoint)["records"]["action"].FirstOrDefault(a=>(string)a["id"]==id)??backendCheckpoint?["records"]?["action"]?.FirstOrDefault(a=>(string)a["id"]==id);
        static async Task UntilAction(MemoryGameController memory,string id,string status,string name,float timeout)
        {
            float until=Time.realtimeSinceStartup+timeout;
            while(true){backendCheckpoint=await MemoryVerificationSnapshot.Read(memory.Client);var action=backendCheckpoint["records"]?["action"]?.FirstOrDefault(a=>(string)a["id"]==id);if((string)action?["status"]==status)return;if(Time.realtimeSinceStartup>until)throw new Exception("Timeout: "+name+" | action="+action);await Wait(.15f);}
        }
        static async Task<string> Invite(MemoryGameController m,NpcPresence z,PlayerController player)
        {
            player.Teleport((Vector2)z.transform.position+Vector2.right*.8f);await Wait(.15f);
            Check("open_player_dialogue",m.BeginPlayerChat("z",false));
            await Until(()=>m.Client.CanGenerate,"ready for player input");
            string old=m.LastReply?.proposalId;m.Conversations.SendPlayer("去码头吧一起");
            await Until(()=>m.LastReply?.proposalId!=null&&m.LastReply.proposalId!=old&&!m.Conversations.Sending&&m.Client.Synchronized,"committed escort");
            var backend=await MemoryVerificationSnapshot.Read(m.Client);
            var selected=backend["records"]?["proposal"]?.First(p=>(string)p["id"]==m.LastReply.proposalId)?["output"]?["action"];
            Check("dialogue_selected_full_destination",(string)selected?["type"]=="escort_player"&&(string)selected?["target"]=="dock",m.LastReply.result.dialogue);
            string id="action_"+m.LastReply.proposalId;
            Check("pending_durable",Action(m,id)?["status"]?.ToString()=="pending");return id;
        }
        static async Task Verify()
        {
            bool passed=false;Directory.CreateDirectory(output);
            try
            {
                await Until(()=>GameSession.Instance?.Memory?.Client?.Synchronized==true&&WorldDirector.Instance?.Stage!=null&&!WorldDirector.Instance.IsTransitioning,"world",90);
                var session=GameSession.Instance;var m=session.Memory;var world=WorldDirector.Instance;
                NewSandbox(session);await Until(()=>!world.IsTransitioning&&world.Stage.place==PlaceId.Street&&m.Client.Synchronized,"new world");
                session.SetOverlay(false);Application.runInBackground=true;
                await Until(()=>m.Spawner.Find("z")?.Movement?.IsReady==true,"Z spawn");
                foreach(var a in m.Spawner.Actors)a.Movement.SimulateDecisions=false;
                m.Conversations.CancelAll();m.Conversations.enabled=false;
                var z=m.Spawner.Find("z");var player=world.Player.GetComponent<PlayerController>();
                var id=await Invite(m,z,player);float start=z.transform.position.x;await Wait(.7f);
                Check("chat_holds_movement",Mathf.Abs(z.transform.position.x-start)<.03f&&m.Actions.HasPending("z"));
                m.Conversations.Cancel();await Until(()=>m.Client.Synchronized&&!z.Movement.InteractionHeld,"release dialogue");await Wait(.7f);
                float dock=SevenCharacterCatalog.Anchor("dock").x;player.Teleport(new Vector2(-10,z.transform.position.y));
                await Until(()=>Mathf.Abs(z.transform.position.x-start)>.15f,"physical movement started");
                Check("world_position_changes",z.Movement.IsMoving);
                player.Teleport((Vector2)z.transform.position+Vector2.right*.8f);
                Check("reopen_during_escort",m.BeginPlayerChat("z",false));await Wait(.5f);float held=z.transform.position.x;await Wait(.6f);
                Check("midway_chat_pauses_without_failing",Mathf.Abs(z.transform.position.x-held)<.03f&&(string)Action(m,id)["status"]=="pending");
                m.Conversations.Cancel();await Wait(.8f);
                var exit=SevenCharacterCatalog.SceneRoute(PlaceId.Street,PlaceId.Harbor)[0];
                // Lead the actor physically through the authored boundary. Keep
                // within walking distance; never teleport the NPC to the target.
                float deadline=Time.realtimeSinceStartup+90;float lastMove=Time.time;
                while(world.Stage.place==PlaceId.Street&&Time.realtimeSinceStartup<deadline)
                {
                    float elapsed=Time.time-lastMove;lastMove=Time.time;
                    if(!world.IsTransitioning&&elapsed>0)player.Teleport(new Vector2(Mathf.MoveTowards(player.transform.position.x,exit.x,player.speed*elapsed),z.transform.position.y));
                    await Frame();
                }
                await Until(()=>world.Stage.place==PlaceId.Harbor&&!world.IsTransitioning&&m.Spawner.Find("z")?.Movement?.IsReady==true,"companion crosses actual Harbor portal");
                z=m.Spawner.Find("z");
                Check("actual_harbor_scene",session.CurrentPlace==PlaceId.Harbor);
                await Until(()=>m.Client.Synchronized,"transition receipt");
                Check("portal_is_not_final_arrival",(string)Action(m,id)["status"]=="pending"&&(string)Action(m,id)["physical_position"]?["place"]=="Harbor");
                foreach(var a in m.Spawner.Actors)a.Movement.SimulateDecisions=false;
                player.Teleport(new Vector2(dock,z.transform.position.y));
                await UntilAction(m,id,"succeeded","actual dock arrival",90);
                Check("at_dock",Mathf.Abs(z.transform.position.x-dock)<.15f);Check("route_progress_saved",(int)Action(m,id)["progress_index"]==1);
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"dock-arrival.png"));await Wait(.5f);
                session.LoadSavedGame();await Until(()=>!world.IsTransitioning&&m.Client.Synchronized&&m.Spawner.Find("z")?.Movement?.IsReady==true,"restore arrived companion");
                z=m.Spawner.Find("z");Check("saved_companion_stays_in_harbor",world.Stage.place==PlaceId.Harbor&&Mathf.Abs(z.transform.position.x-dock)<.2f);
                NewSandbox(session);await Until(()=>!world.IsTransitioning&&world.Stage.place==PlaceId.Street&&m.Client.Synchronized&&m.Spawner.Find("z")?.Movement?.IsReady==true,"fresh blocked test");
                z=m.Spawner.Find("z");foreach(var a in m.Spawner.Actors)a.Movement.SimulateDecisions=false;
                id=await Invite(m,z,player);float blockerX=z.transform.position.x-2;var wall=new GameObject("QA physical blocker");wall.transform.position=new Vector3(blockerX,z.transform.position.y+1.3f,0);wall.AddComponent<BoxCollider2D>().size=new Vector2(.3f,2.5f);Physics2D.SyncTransforms();
                m.Conversations.Cancel();await Wait(.8f);player.Teleport(new Vector2(blockerX-2,z.transform.position.y));
                await UntilAction(m,id,"failed","blocked receipt",12);
                Check("failure_has_reason",((string)Action(m,id)["receiptPayload"]).Contains("obstructed:QA physical blocker"));
                Check("failure_does_not_teleport",Mathf.Abs(z.transform.position.x-dock)>.5f);UnityEngine.Object.Destroy(wall);
                passed=true;
            }
            catch(Exception e){checks.Add(new JObject{{"name","exception"},{"passed",false},{"detail",e.ToString()+" | scene="+WorldDirector.Instance?.Stage?.place+" state="+GameSession.Instance?.CurrentPlace+" error="+GameSession.Instance?.Memory?.Client?.LastError}});}
            finally{SessionState.SetBool(Key,false);File.WriteAllText(Path.Combine(output,"report.json"),new JObject{{"passed",passed},{"source","Unity Play Mode, real NPC locomotion and HTTP action receipts; deterministic decision fixture"},{"checks",checks}}.ToString());Debug.Log("CONVERSATION_ACTION_"+(passed?"PASSED":"FAILED"));EditorApplication.Exit(passed?0:1);}
        }
    }
}
