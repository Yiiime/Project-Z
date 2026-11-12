using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class NpcRuntimeVerification
    {
        const string Key="Z.NpcRuntimeQA";
        static readonly List<TaskCompletionSource<bool>> frames=new List<TaskCompletionSource<bool>>();
        static readonly JArray checks=new JArray();
        static bool running;static string output;
        static NpcRuntimeVerification(){EditorApplication.update+=Tick;}
        public static void Begin()
        {
            output=Environment.GetEnvironmentVariable("Z_NPC_RUNTIME_QA_DIR");
            string saves=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
            if(string.IsNullOrEmpty(output)||!output.StartsWith("/private/tmp/")||string.IsNullOrEmpty(saves)||!saves.StartsWith(output+"/"))
                throw new Exception("NPC runtime verification requires isolated temporary saves.");
            Directory.CreateDirectory(saves);
            var saved=Path.Combine(saves,"seven-memory-save.json");
            if(!File.Exists(saved)){var state=new SaveData{started=true,firstDay=null,place="Street",day=1,period=0,memory=new MemorySaveState()};state.Sanitize();if(!SaveRepository.Write(state))throw new Exception("Could not create isolated sandbox save");}
            SessionState.SetString(Key+".Output",output);SessionState.SetBool(Key,true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            foreach(var frame in frames.ToArray()){frames.Remove(frame);frame.TrySetResult(true);}
            if(!running&&SessionState.GetBool(Key,false)&&EditorApplication.isPlaying&&!EditorApplication.isCompiling)
            {running=true;output=SessionState.GetString(Key+".Output","");_ = Verify();}
        }
        static Task Frame(){var frame=new TaskCompletionSource<bool>();frames.Add(frame);return frame.Task;}
        static async Task Until(Func<bool> test,string name,float timeout=30)
        {float end=Time.realtimeSinceStartup+timeout;while(!test()){if(Time.realtimeSinceStartup>end)throw new Exception("Timeout: "+name);await Frame();}}
        static void Check(string name,bool passed)
        {checks.Add(new JObject{{"name",name},{"passed",passed}});if(!passed)throw new Exception(name);}
        static async Task<JObject> Inspect(MemoryGameController memory,string actor)
        {
            using(var client=new HttpClient(new HttpClientHandler{UseProxy=false}))
            {
                var request=memory.Client.RequestBase();request.actorId=actor;
                var message=new HttpRequestMessage(HttpMethod.Post,MemoryRuntimeConfig.Settings.serviceUrl+"/api/game/runtime");
                message.Headers.Add("X-Z-Memory","1");message.Content=new StringContent(JsonConvert.SerializeObject(request),Encoding.UTF8,"application/json");
                using(message)using(var response=await client.SendAsync(message))
                {string text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new Exception(text);return JObject.Parse(text);}
            }
        }
        static async Task Verify()
        {
            bool passed=false;Directory.CreateDirectory(output);
            try
            {
                await Until(()=>GameSession.Instance?.IsReady==true,"bootstrap ready",90);
                var title=UnityEngine.Object.FindFirstObjectByType<StartPageView>();
                if(title!=null){await Frame();var button=title.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Continue journey");button.onClick.Invoke();}
                await Until(()=>GameSession.Instance?.Memory?.Client?.Synchronized==true&&WorldDirector.Instance?.Stage!=null&&!WorldDirector.Instance.IsTransitioning&&!GameSession.Instance.IsAtTitle&&!GameSession.Instance.NonPlayerInputBlocked&&Time.timeScale>0,"world ready after title dismissal",90);
                var session=GameSession.Instance;var memory=session.Memory;var world=WorldDirector.Instance;
                session.SetOverlay(false);memory.Conversations.CancelAll();memory.Conversations.enabled=false;
                await Until(()=>memory.Spawner.Actors.Count(a=>a.IsPresent&&a.Movement?.IsReady==true)>=2,"two actual NPCs");
                foreach(var npc in memory.Spawner.Actors)if(npc.Movement!=null)npc.Movement.SimulateDecisions=false;
                var actor=memory.Spawner.Actors.First(a=>a.IsPresent&&a.Movement.IsReady);
                var other=memory.Spawner.Actors.First(a=>a!=actor&&a.IsPresent&&a.Movement.IsReady);
                await Until(()=>memory.Client.CanGenerateFor(actor.npcId)&&memory.Client.RuntimeSynchronized(other.npcId)&&session.State.memory.RuntimeCursor(actor.npcId).acknowledgedSeq>0&&session.State.memory.RuntimeCursor(other.npcId).acknowledgedSeq>0,"initial actor reports");
                var initial=await Inspect(memory,actor.npcId);
                Check("initial_environment_has_actual_coordinates",Mathf.Abs((float)initial["current"]["environment"]["x"]-actor.transform.position.x)<.05f);
                float start=actor.transform.position.x;
                var point=new[]{new Vector2(start+1.3f,actor.transform.position.y),new Vector2(start-1.3f,actor.transform.position.y)}.First(p=>actor.Movement.CanReachConversationPoint(p));
                Check("actual_move_started",actor.Movement.MoveForConversation(point,point.x>start?1:-1));
                await Until(()=>!actor.Movement.IsMoving&&memory.Client.RuntimeSynchronized(actor.npcId),"physical arrival and own receipt");
                Check("npc_really_walked",Mathf.Abs(actor.transform.position.x-start)>.5f&&actor.Movement.LastResult.Outcome==NpcMoveOutcome.Arrived);
                var moved=await Inspect(memory,actor.npcId);
                Check("movement_start_and_arrival_recorded",moved["entries"].Any(e=>(string)e["type"]=="environment.movement_started")&&moved["entries"].Any(e=>(string)e["type"]=="environment.movement_arrived"));
                var player=world.Player.GetComponent<PlayerController>();player.Teleport((Vector2)actor.transform.position+Vector2.right*.7f);
                Check("open_real_dialogue_window",memory.BeginPlayerChat(actor.npcId,false));
                await Until(()=>memory.Client.CanGenerateFor(actor.npcId),"dialogue environment acknowledged");
                memory.Runtimes.Report(other,"qa_other_actor");
                Check("other_npc_queue_does_not_block_dialogue",memory.Client.CanGenerateFor(actor.npcId));
                memory.Conversations.SendPlayer("你现在在哪里？");
                await Until(()=>memory.LastReply?.actorId==actor.npcId&&!memory.Conversations.Sending&&memory.Client.Synchronized,"actual dialogue and commit");
                var chatted=await Inspect(memory,actor.npcId);
                Check("only_own_entries",chatted["entries"].All(e=>(string)e["actor"]==actor.npcId));
                Check("dialogue_environment_and_decision_saved",chatted["entries"].Any(e=>(string)e["type"]=="environment.conversation_opened")&&chatted["entries"].Any(e=>(string)e["type"]=="decision.requested")&&chatted["entries"].Any(e=>(string)e["type"]=="proposal.committed"));
                Check("dialogue_has_actual_location",memory.LastReply.result.dialogue.Contains((string)chatted["current"]["environment"]["locationName"]??"这里")||memory.LastReply.result.dialogue.Contains("这里"));
                memory.Conversations.Cancel();await Until(()=>memory.Client.RuntimeSynchronized(actor.npcId)&&memory.Client.CanGenerate,"window close acknowledged");
                Check("conversation_closed_recorded",(await Inspect(memory,actor.npcId))["entries"].Any(e=>(string)e["type"]=="environment.conversation_closed"));
                session.Save();string saved=File.ReadAllText(SaveRepository.PathOnDisk);
                memory.Runtimes.Report(actor,"qa_future");await Until(()=>memory.Client.RuntimeSynchronized(actor.npcId),"future report");
                Check("future_record_exists",(await Inspect(memory,actor.npcId))["entries"].Any(e=>(string)e["type"]=="environment.qa_future"));
                string run=memory.Client.RunId,actorId=actor.npcId;File.WriteAllText(SaveRepository.PathOnDisk,saved);session.LoadSavedGame();
                await Until(()=>memory.Client.RunId!=run&&memory.Client.Synchronized&&!world.IsTransitioning&&memory.Spawner.Find(actorId)?.Movement?.IsReady==true,"reload earlier save",60);
                await Until(()=>memory.Client.RuntimeSynchronized(actorId),"restored actor input sequence");
                Check("reload_drops_future_runtime_record",!(await Inspect(memory,actorId))["entries"].Any(e=>(string)e["type"]=="environment.qa_future"));
                passed=true;
            }
            catch(Exception error){checks.Add(new JObject{{"name","exception"},{"passed",false},{"detail",error.ToString()+" | "+GameSession.Instance?.Memory?.Client?.LastError+" | "+GameSession.Instance?.Memory?.Client?.BackgroundError}});}
            finally
            {
                SessionState.SetBool(Key,false);
                File.WriteAllText(Path.Combine(output,"report.json"),new JObject{{"passed",passed},{"source","Unity Play Mode, actual locomotion, dialogue window, per-NPC HTTP queues and save reload; simulated model"},{"checks",checks}}.ToString());
                Debug.Log("NPC_RUNTIME_"+(passed?"PASSED":"FAILED"));EditorApplication.Exit(passed?0:1);
            }
        }
    }
}
