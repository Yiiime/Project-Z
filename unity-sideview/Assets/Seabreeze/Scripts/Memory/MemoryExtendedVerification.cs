using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    // Explicit opt-in verification entry for both Editor and standalone; never runs in normal play.
    public sealed class MemoryExtendedVerification : MonoBehaviour
    {
        readonly List<object> checks = new List<object>();
        string output;
        float started;
        int failures;
        GameSession Game => GameSession.Instance;
        MemoryGameController Memory => Game?.Memory;
        static string Arg(string name,string fallback="") { var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);return i>=0&&i+1<a.Length?a[i+1]:fallback; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(string.IsNullOrEmpty(Arg("-memoryExtendedOutput")))return;
            string save=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
            if(!save.StartsWith("/private/tmp/",StringComparison.Ordinal))throw new InvalidOperationException("Use an isolated temporary save root");
            var go=new GameObject("Extended verification");DontDestroyOnLoad(go);go.AddComponent<MemoryExtendedVerification>();
        }
        void Record(string name,bool passed,object detail=null)
        {
            if(!passed)failures++;
            checks.Add(new {name,passed,detail,seconds=Math.Round(Time.realtimeSinceStartup-started,1)});
            File.WriteAllText(output,JsonConvert.SerializeObject(new {complete=false,failures,checks},Formatting.Indented));
        }
        IEnumerator Ready(float timeout=40)
        {
            float end=Time.realtimeSinceStartup+timeout;
            while((Memory==null||!Memory.Client.Synchronized||WorldDirector.Instance.IsTransitioning)&&Time.realtimeSinceStartup<end)yield return null;
        }
        IEnumerator Chat(string actor,string message)
        {
            yield return Ready();
            var npc=Memory.Spawner.Find(actor);
            if(npc==null){Record("chat:"+actor,false,"Actor not present");yield break;}
            WorldDirector.Instance.Player.GetComponent<PlayerController>().Teleport(new Vector2(npc.transform.position.x-.9f,npc.transform.position.y));
            Memory.BeginPlayerChat(actor);
            yield return new WaitForSecondsRealtime(1);
            yield return Ready();
            float readyUntil=Time.realtimeSinceStartup+35;
            while(!Memory.Client.CanGenerate&&Time.realtimeSinceStartup<readyUntil)yield return null;
            if(!Memory.Client.CanGenerate){Record("chat:"+actor,false,new {message,error=Memory.Client.LastError});yield break;}
            var previous=Memory.LastReply;
            Memory.Conversations.SendPlayer(message);
            float until=Time.realtimeSinceStartup+25;
            while(Memory.Conversations.Sending&&Time.realtimeSinceStartup<until)yield return null;
            yield return Ready();
            bool ok=Memory.LastReply!=null&&Memory.LastReply!=previous&&Memory.LastReply.actorId==actor;
            Record("chat:"+actor,ok,new {message,dialogue=ok?Memory.LastReply.result?.dialogue:null,status=Memory.Client.Status,error=ok?null:Memory.Client.LastError});
        }
        JObject Checkpoint(){try{return JObject.Parse(Game.State.memory.checkpoint);}catch{return new JObject();}}
        string TaskState(string task)=>(string)Checkpoint()["tasks"]?["state"]?.FirstOrDefault(t=>(string)t["task"]==task)?["status"];
        IEnumerator Button(string name)
        {
            yield return new WaitForSecondsRealtime(1);
            float readyUntil=Time.realtimeSinceStartup+35;
            while(!Memory.Client.CanGenerate&&Time.realtimeSinceStartup<readyUntil)yield return null;
            var button=Memory.View.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name==name);
            Record("button:"+name,button!=null&&button.interactable);
            if(button==null||!button.interactable)yield break;
            button.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1);
            float until=Time.realtimeSinceStartup+25;
            while((Memory.Conversations.Sending||!Memory.Client.Synchronized)&&Time.realtimeSinceStartup<until)yield return null;
        }
        IEnumerator Travel(PlaceId place)
        {
            Memory.Conversations.CancelAll();Game.Travel(place);
            yield return new WaitForSecondsRealtime(2);yield return Ready();
            Record("travel:"+place,Game.CurrentPlace==place&&WorldDirector.Instance.Stage.place==place,new {actors=Memory.Spawner.Actors.Select(a=>a.npcId).ToArray()});
        }
        IEnumerator Start()
        {
            output=Arg("-memoryExtendedOutput");started=Time.realtimeSinceStartup;
            yield return Ready(90);
            if(Memory==null||!Memory.Client.Synchronized){Record("connected",false);Finish();yield break;}
            Record("connected",Memory.Client.Mode=="real",new {Memory.Client.Mode,unity=Application.unityVersion,standalone=!Application.isEditor});
            yield return Chat("z","你认识岛上的咖啡店老板吗？他是谁？");
            Record("player-focus",Memory.Conversations.PlayerActor?.npcId=="z"&&Memory.Spawner.Find("z").Movement.InteractionHeld);
            Record("internal-output-hidden",!Memory.View.GetComponentsInChildren<RectTransform>(true).Any(t=>t.name=="Action与Info"||t.name=="行动与数值"));
            foreach(var size in new[]{new[]{1280,720},new[]{1920,1080},new[]{1024,768}})
            {
                Screen.SetResolution(size[0],size[1],FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(2);
                string image=Path.ChangeExtension(output,null)+"-"+size[0]+"x"+size[1]+".png";
                ScreenCapture.CaptureScreenshot(image);yield return new WaitForSecondsRealtime(1);
                Record("render:"+size[0]+"x"+size[1],File.Exists(image),new {actualWidth=Screen.width,actualHeight=Screen.height,image});
            }
            Memory.Conversations.CancelAll();yield return Ready();
            yield return Chat("linxu","码头有哪些需要我帮忙的？可以把整理码头物资和清点用品这项委托交给我。");
            if(TaskState("dock_sort")=="offered")
            {
                yield return Button("暂不接受");Record("task-refusal",TaskState("dock_sort")=="declined");
                yield return Chat("linxu","我刚才有别的事情，现在已经处理完，接下来时间充足，可以重新接下整理码头物资和清点用品的委托了。");
                if(TaskState("dock_sort")=="offered") {yield return Button("接受任务");Record("task-accept",TaskState("dock_sort")=="active");}
                else Record("task-reoffer-choice",true,new {state=TaskState("dock_sort"),note="NPC may choose not to reoffer"});
            }
            else Record("task-offer-path",false,new {state=TaskState("dock_sort"),note="Model did not offer the requested task"});
            Memory.Conversations.CancelAll();yield return Travel(PlaceId.Cafe);
            yield return Chat("liting","你平时在这里做什么？Z和你是什么关系？");
            Memory.Conversations.CancelAll();yield return Travel(PlaceId.Lighthouse);
            yield return Chat("liyuan","我想和Z一起去看看没标进地图的地方。可以把地图上的空白这个探索委托交给我吗？");
            if(TaskState("map_blank")=="offered")
            {
                yield return Button("接受任务");Record("cooperation-task-active",TaskState("map_blank")=="active");
                yield return Button("邀请当前NPC协作");
                Record("npc-cooperation-choice",Memory.LastReply?.result?.info?["task_participation"]!=null,Memory.LastReply?.result?.info?["task_participation"]);
            }
            else Record("cooperation-offer-path",false,TaskState("map_blank"));
            Memory.Conversations.CancelAll();yield return Chat("axing","你平时做什么？想去码头应该怎么走？");
            Memory.Conversations.CancelAll();yield return Travel(PlaceId.Street);
            for(int day=2;day<=7;day++)
            {
                while(Game.State.day<day)Game.Clock.Advance();yield return Ready();
                if(day==4)
                {
                    string notice=null;Action<string> capture=message=>notice=message;
                    Game.Story.Toast+=capture;Game.Story.Interact("noticeboard");Game.Story.Toast-=capture;
                    Record("no-fixed-day4-announcement",!Game.Story.Objective.Contains("毁灭")&&notice!=null&&!notice.Contains("毁灭"));
                    Memory.PublishStoryEvent("verify_private_day4","仅林序接收的船务测试通知，核对代号：琥珀海燕492。",new[]{"linxu"},true,new[]{"linxu"});yield return Ready();
                    var secret=Checkpoint()["records"]?["event"]?.FirstOrDefault(e=>(string)e["id"]==Memory.StoryEventId("verify_private_day4"));
                    Record("day4-directed-event",secret!=null&&!secret["visible"].Values<string>().Contains("z"),secret?["visible"]);
                    int seq=Game.State.memory.nextSeq;
                    Memory.PublishStoryEvent("verify_private_day4","仅林序接收的船务测试通知，核对代号：琥珀海燕492。",new[]{"linxu"},true,new[]{"linxu"});
                    Record("story-replay-idempotent",Game.State.memory.nextSeq==seq);
                }
                Record("day:"+day,Game.State.day==day);
            }
            string worldId=Game.State.memory.worldId;Game.Save();Game.LoadSavedGame();yield return new WaitForSecondsRealtime(2);yield return Ready();
            Record("save-reload",Game.State.day==7&&Game.State.memory.worldId==worldId&&Memory.Client.Synchronized);
            int before=Memory.Conversations.CommittedNpcLines,completed=Memory.Conversations.CompletedConversations;
            float soak=float.Parse(Arg("-memorySoakSeconds","600"),System.Globalization.CultureInfo.InvariantCulture);
            float end=Time.realtimeSinceStartup+soak;int samples=0;
            while(Time.realtimeSinceStartup<end)
            {
                yield return new WaitForSecondsRealtime(30);samples++;
                Record("soak-sample",Memory.Client.Reachable,new {samples,lines=Memory.Conversations.CommittedNpcLines-before,conversations=Memory.Conversations.CompletedConversations-completed,status=Memory.Client.Status});
            }
            if(soak>0)Record("real-a2a-delivered",Memory.Conversations.CommittedNpcLines>before,new {seconds=soak,lines=Memory.Conversations.CommittedNpcLines-before,conversations=Memory.Conversations.CompletedConversations-completed});
            Finish();
        }
        void Finish()
        {
            File.WriteAllText(output,JsonConvert.SerializeObject(new {complete=true,failures,checks},Formatting.Indented));
            Application.Quit(failures==0?0:1);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failures==0?0:1);
#endif
        }
    }
}
