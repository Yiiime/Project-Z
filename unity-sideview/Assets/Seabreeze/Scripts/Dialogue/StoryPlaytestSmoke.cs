using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;
namespace Seabreeze
{
    // Explicit standalone verification entry; never enabled by the normal launcher.
    public sealed class StoryPlaytestSmoke : MonoBehaviour
    {
        string output;readonly List<string> checks=new List<string>();GameSession S=>GameSession.Instance;StoryPlaytestRuntime R=>StoryPlaytestRuntime.Instance;WorldDirector W=>WorldDirector.Instance;
        StoryPlaytestQuest template;bool finished;float deadline;Stack<IEnumerator> steps=new Stack<IEnumerator>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-storySmokeOutput");
            if(index<0||index+1>=args.Length||!StoryPlaytest.Enabled)return;
            string root=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
            if(!root.Contains("story-smoke")||!Path.IsPathRooted(root))throw new InvalidOperationException("Story smoke requires an explicitly isolated story-smoke save directory.");
            var obj=new GameObject("Story progression verification");DontDestroyOnLoad(obj);var runner=obj.AddComponent<StoryPlaytestSmoke>();runner.output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(runner.output);
            int fixture=Array.IndexOf(args,"-storySmokeFixture");var data=JObject.Parse(File.ReadAllText(args[fixture+1]));runner.template=data["task"].ToObject<StoryPlaytestQuest>();
            runner.deadline=Time.unscaledTime+240;runner.steps.Push(runner.Run());
            Debug.Log("STORY_SMOKE initialized: "+runner.output);
        }
        void Update()
        {
            if(finished||output==null)return;
            try{
                Require(Time.unscaledTime<deadline,"Verification completed within 240 seconds");
                while(steps.Count>0){var item=steps.Peek();if(!item.MoveNext()){steps.Pop();continue;}if(item.Current is IEnumerator nested){steps.Push(nested);continue;}return;}
                Finish(null);
            }catch(Exception e){Finish(e.ToString());}
        }
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        IEnumerator Wait(Func<bool> condition,float timeout,string label){float until=Time.unscaledTime+timeout;while(!condition()){Require(Time.unscaledTime<until,label);yield return null;}}
        IEnumerator Film(string expected)
        {
            yield return Wait(()=>R.MovieOpen,15,"Film opens: "+expected);Require(R.CurrentClip==expected,"Expected clip: "+expected);
            yield return Wait(()=>FindAnyObjectByType<VideoPlayer>()?.frame>0,30,"Actual decoded video frames: "+expected);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,expected+".png"));yield return null;R.FinishMovie();yield return null;
        }
        IEnumerator Quest(int index)
        {
            var q=JsonUtility.FromJson<StoryPlaytestQuest>(JsonUtility.ToJson(template));q.id="fixture-"+index;q.actor="linxu";q.ready=true;q.accepted=false;q.picked=false;q.completed=false;q.error="";q.scene=S.CurrentPlace.ToString();q.itemX=W.Player.position.x+1;q.targetX=W.Player.position.x+4;
            S.State.storyProgress.quest=q;S.Save();yield return null;
            int score=S.State.storyProgress.Person("linxu").points,period=S.State.period;
            R.PickUp(q.id);Require(!q.picked,"Cannot pick up before accepting");R.AcceptQuest();R.Deposit(q.id);Require(!q.completed,"Cannot deposit before picking up");
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(q.itemX,.2f));yield return null;R.PickUp(q.id);Require(q.picked,"Actual pickup near item");
            R.Deposit(q.id);Require(!q.completed,"Cannot deliver from too far away");W.Player.GetComponent<PlayerController>().Teleport(new Vector2(q.targetX,.2f));yield return null;
            R.Deposit(q.id);Require(q.completed,"Delivery completes the whole task");Require(S.State.storyProgress.Person("linxu").points==Math.Min(100,score+10),"Task awards ten points once");
            R.Deposit(q.id);Require(S.State.storyProgress.Person("linxu").points==Math.Min(100,score+10),"Repeated delivery cannot score twice");
            if(period<3)Require(S.State.period==period+1,"Task advances exactly one period");
            checks.Add("task-"+index+": accept/pickup/delivery/distance/idempotence");yield return null;
        }
        IEnumerator Run()
        {
            yield return Wait(()=>S!=null&&S.IsReady&&R!=null&&W?.Player!=null&&S.Memory?.Spawner!=null&&!W.IsTransitioning,50,"Playable scene initializes");
            Require(SaveRepository.PathOnDisk.Contains("story-smoke"),"Isolated save path");
            yield return Wait(()=>S.Memory.Client.CanGenerate,40,"Real model service connected");
            var speaker=S.Memory.Spawner.Find("linxu");Require(speaker!=null,"Linxu is present");
            W.Player.GetComponent<PlayerController>().Teleport(new Vector2(speaker.transform.position.x,.2f));yield return null;
            S.Memory.BeginPlayerChat("linxu");yield return Wait(()=>S.Memory.PlayerChatOpen&&S.Memory.Client.CanGenerate,15,"Player conversation opens");
            int priorLines=S.Memory.Conversations.CommittedNpcLines;
            S.Memory.Conversations.SendPlayer("你好，今天码头有什么小事需要留意？");
            yield return Wait(()=>S.Memory.Conversations.CommittedNpcLines>priorLines&&!S.Memory.Conversations.Sending,45,"Real NPC reply presented");
            float shownUntil=Time.unscaledTime+1;yield return Wait(()=>Time.unscaledTime>=shownUntil,3,"Dialogue layout settled");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"dialogue-layout.png"));yield return null;
            S.Memory.Conversations.Cancel();yield return Wait(()=>!S.Memory.PlayerChatOpen,5,"Conversation closes");
            Require(S.State.storyProgress.Person("linxu").points==2,"A real complete conversation earns two points");checks.Add("real model conversation / unobstructed dialogue layout / completion credit");
            S.NewGame();yield return Wait(()=>!W.IsTransitioning&&W.Player!=null,25,"New game ready");
            int initial=S.State.period;S.Clock.Advance();Require(S.State.period==initial,"Rest cannot advance story clock");checks.Add("rest does not advance time");
            for(int i=1;i<=4;i++){
                yield return Quest(i);
                if(i==3)yield return Film("linxu_1");
                if(i==4){Require(S.State.day==1&&S.State.period==3,"Fourth task holds night until livestream");yield return Film("night_live");Require(S.State.day==2&&S.State.period==0,"Livestream enters next morning once");}
            }
            checks.Add("four tasks => night livestream => next morning");
            R.RecordChat("linxu","chat-1");R.RecordChat("linxu","chat-2");Require(S.State.storyProgress.Person("linxu").points==42,"Chat counts once per NPC/period");R.RecordChat("z","chat-z");Require(S.State.storyProgress.Person("z").points==2,"Independent NPC progress");
            S.Save();S.LoadSavedGame();yield return Wait(()=>!W.IsTransitioning&&W.Player!=null,25,"Save restored");Require(S.State.storyProgress.Person("linxu").points==42,"Score survives save/load");R.RecordChat("linxu","chat-3");Require(S.State.storyProgress.Person("linxu").points==42,"Chat cap survives reload");checks.Add("save/load preserves progress and chat cap");
            for(int i=5;i<=10;i++){
                yield return Quest(i);
                while(S.State.storyProgress.queuedClips.Count>0)yield return Film(S.State.storyProgress.queuedClips[0]);
                if(S.State.storyProgress.livePending)yield return Film("night_live");
            }
            Require(S.State.storyProgress.winner=="linxu"&&S.State.storyProgress.endingSeen,"Correct ending actually plays");
            Require(S.State.storyProgress.Person("z").points==2,"Other person's progress remains independent");Require(!S.State.storyProgress.CreditTask("z","late",StoryPlaytest.Settings),"Route cannot change after ending");checks.Add("correct ending / prior chapters / route lock");
            S.Save();ScreenCapture.CaptureScreenshot(Path.Combine(output,"ending-state.png"));yield return null;
        }
        [Serializable]sealed class Report{public bool passed;public string error,save;public string[] checks;}
        void Finish(string error){finished=true;File.WriteAllText(Path.Combine(output,"runtime-result.json"),JsonUtility.ToJson(new Report{passed=error==null,error=error,checks=checks.ToArray(),save=SaveRepository.PathOnDisk},true));Debug.Log("STORY_SMOKE "+(error??"PASS"));Application.Quit(error==null?0:1);}
    }
}
