using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Seabreeze.Editor
{
    [InitializeOnLoad] public static class MemoryIntegrationVerification
    {
        const string Key="Seabreeze.SevenMemoryVerification";
        static readonly Stack<IEnumerator> steps=new Stack<IEnumerator>();
        static readonly List<string> checks=new List<string>(),issues=new List<string>();
        static readonly List<string> editorOnlyIssues=new List<string>();
        static readonly List<Frame> frames=new List<Frame>();
        static string output,failure;static bool ending,recording;static int lastFrame,frameIndex;
        static double deadline,nextCapture,recordingStart;
        static Keyboard keyboard;static InputSettings originalSettings,testSettings;static HideFlags oldFlags;
        static RenderTexture render;static Camera camera;
        static int observedNpcLines,observedConversations;
        static GameSession S=>GameSession.Instance;
        static WorldDirector W=>WorldDirector.Instance;
        static MemoryGameController M=>S!=null?S.Memory:null;
        [Serializable] sealed class Frame {public string file;public double time;public string stage;}
        [Serializable] sealed class Report {public bool passed;public string mode,error,unityVersion,savePath;public string[] checks,issues,editorOnlyIssues;public Frame[] frames;public int npcLines,completedConversations;}
        [Serializable] sealed class Checkpoint {public Mem[] memories;public Msg[] messages;public Evt[] events;public int seq;}
        [Serializable] sealed class Mem {public string id,actorId,kind,origin,content,speakerId;public float createdHour;}
        [Serializable] sealed class Msg {public string speakerId,content,origin,conversationId;public string[] participants;}
        [Serializable] sealed class Evt {public string gameKind,gameEventId;}
        static MemoryIntegrationVerification()
        {
            if(SessionState.GetBool(Key,false))SaveRepository.VerificationDirectory=SessionState.GetString(Key+".saves","");
            EditorApplication.playModeStateChanged+=StateChanged;
        }
        [MenuItem("Seabreeze/Verify seven-character memory integration")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run verification from Edit mode.");
            string path=Path.GetFullPath(Arg("-memoryOutput",Path.Combine(Path.GetTempPath(),"seven-memory-"+Guid.NewGuid().ToString("N"))));
            Directory.CreateDirectory(path);Directory.CreateDirectory(Path.Combine(path,"frames"));
            SessionState.SetString(Key+".output",path);SessionState.SetString(Key+".saves",Path.Combine(path,"saves"));
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+".finished",false);
            SaveRepository.VerificationDirectory=Path.Combine(path,"saves");
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity",OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        static void StateChanged(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                output=SessionState.GetString(Key+".output","");ending=recording=false;failure=null;lastFrame=-1;frameIndex=0;observedNpcLines=observedConversations=0;
                checks.Clear();issues.Clear();editorOnlyIssues.Clear();frames.Clear();steps.Clear();deadline=EditorApplication.timeSinceStartup+480;
                steps.Push(Verify());EditorApplication.update+=Tick;Application.logMessageReceived+=Log;
            }
            else if(state==PlayModeStateChange.ExitingPlayMode&&!SessionState.GetBool(Key+".finished",false))Complete("Play mode exited before completion.");
            else if(state==PlayModeStateChange.EnteredEditMode)
            {
                SaveRepository.VerificationDirectory=null;SessionState.SetBool(Key,false);
                if(Application.isBatchMode)EditorApplication.Exit(SessionState.GetInt(Key+".exit",1));
            }
        }
        static void Log(string message,string stack,LogType type)
        {
            if(stack.Contains("UnityEditor.Search.SearchDatabase")&&stack.Contains("SearchInit.IndexationOnStartup")){editorOnlyIssues.Add(message+" [Unity Editor Search startup]");return;}
            if(ending)return;if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){issues.Add(message);failure=message;}
        }
        static void Tick()
        {
            if(ending||!EditorApplication.isPlaying||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                Require(failure==null,failure??"");Require(EditorApplication.timeSinceStartup<deadline,"Verification timed out.");
                if(recording&&EditorApplication.timeSinceStartup>=nextCapture){Capture();nextCapture=EditorApplication.timeSinceStartup+.10;}
                while(steps.Count>0)
                {
                    var step=steps.Peek();if(!step.MoveNext()){steps.Pop();continue;}
                    if(step.Current is IEnumerator child){steps.Push(child);continue;}return;
                }
                Complete(null);
            }catch(Exception e){Complete(e.ToString());}
        }
        static IEnumerator Verify()
        {
            yield return Wait(()=>S!=null&&S.IsReady&&W!=null&&W.Stage!=null&&!W.IsTransitioning&&M!=null&&M.Spawner.Actors.Count==4,45,"Seven-person scene startup");
            Require(MemoryRuntimeConfig.Enabled,"Seven-person mode enabled.");
            Require(SaveRepository.PathOnDisk.StartsWith(Path.Combine(output,"saves")+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Isolated save directory.");
            Require(string.Join(",",SevenCharacterCatalog.Actors.Select(a=>a.displayName))=="留洋,晴子,梨园,伊停,周结,正义,星亮","Exact worldbook identities.");
            Require(M.Spawner.Find("c07")==null,"Doctor begins outside the current scene.");
            checks.Add("Seven worldbook IDs/names, independent player, placeholder figures and isolated game saves");
            originalSettings=InputSystem.settings;oldFlags=originalSettings.hideFlags;originalSettings.hideFlags=HideFlags.DontSave;
            testSettings=Object.Instantiate(originalSettings);testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=testSettings;keyboard=InputSystem.AddDevice<Keyboard>();
            if(Environment.GetCommandLineArgs().Contains("-memoryUnconfigured")||Environment.GetCommandLineArgs().Contains("-memoryOffline"))
            {yield return VerifyUnavailable();yield break;}
            yield return Wait(()=>M.Client.CanGenerate,35,"Configured isolated model connection");
            bool real=M.Client.Mode!="simulated";
            if(!real)yield return VerifyReadback();
            yield return Wait(()=>M.Client.CanGenerate&&M.Spawner.Actors.All(a=>a.Movement!=null&&a.Movement.IsReady),30,"Restored scene ready");
            M.Conversations.Cancel("Verification setup");
            var player=W.Player.GetComponent<PlayerController>();player.Teleport(new Vector2(-2.8f,.1f));W.Follow.Snap();
            SetupCapture();recording=true;recordingStart=EditorApplication.timeSinceStartup;nextCapture=0;
            while(S.State.day<4){S.Clock.Advance();yield return null;}
            yield return Wait(()=>S.State.memory.day4Shown&&M.Client.Synchronized,45,"Day4 notice and seven-person delivery");
            var checkpoint=ReadCheckpoint();
            Require(checkpoint.memories.Count(m=>m.origin=="public_day4")==7,"One public Day4 memory per NPC.");
            Require(checkpoint.memories.Count(m=>m.origin=="staff_day4")==7,"One internal Day4 notification per staff member.");
            Require(S.State.memory.publicDay4KnownBy.Contains("player")&&S.State.memory.publicDay4KnownBy.Contains("z_story"),"Player and story Z receive the public fact.");
            M.EnsureDay4();Require(S.State.memory.outbox.All(e=>e.kind!="day4"),"Repeated evaluation does not enqueue the event again.");
            checks.Add("Game Day4 informs all seven, including absent NPCs; public/player/Z notice and staff-only knowledge remain separate");
            int initialCompleted=M.Conversations.CompletedConversations;
            yield return Wait(()=>M.Conversations.Stage=="交谈",real?150:60,"Autonomous choice and approach");
            var first=M.Conversations.First;var second=M.Conversations.Second;
            Require(first!=null&&second!=null&&first!=second,"Two actual scene participants.");
            Require(Vector2.Distance(first.transform.position,second.transform.position)<1.8f,"Participants walked into conversation range.");
            var pair=new[]{first.npcId,second.npcId};
            var observer=M.Spawner.Actors.First(a=>a!=first&&a!=second);
            player.Teleport(new Vector2((first.transform.position.x+second.transform.position.x)/2-2.4f,.1f));W.Follow.Snap();
            float playerX=player.transform.position.x,otherMin=observer.transform.position.x,otherMax=otherMin;
            float t=Time.unscaledTime;
            while(Time.unscaledTime-t<1)
            {
                Press(UnityEngine.InputSystem.Key.D);otherMin=Mathf.Min(otherMin,observer.transform.position.x);otherMax=Mathf.Max(otherMax,observer.transform.position.x);yield return null;
            }
            Press();Require(player.transform.position.x>playerX+.3f,"Player keeps moving during NPC conversation.");
            Require(!S.InputBlocked&&first.Movement.InteractionHeld&&second.Movement.InteractionHeld,"Only participants hold their movement.");
            double end=EditorApplication.timeSinceStartup+(real?180:70);
            while(M.Conversations.CompletedConversations==initialCompleted&&EditorApplication.timeSinceStartup<end)
            {
                if(observer!=null){otherMin=Mathf.Min(otherMin,observer.transform.position.x);otherMax=Mathf.Max(otherMax,observer.transform.position.x);}
                yield return null;
            }
            Require(M.Conversations.CompletedConversations>initialCompleted,"Conversation ends within its cap.");
            Require(otherMax-otherMin>.08f,"Another NPC continues activities.");
            yield return Wait(()=>M.Client.Synchronized,45,"Conversation memory writes");
            checkpoint=ReadCheckpoint();
            var lines=checkpoint.messages.Where(m=>m.origin=="model"&&pair.Contains(m.speakerId)).ToArray();
            Require(pair.All(id=>lines.Any(m=>m.speakerId==id)),"Both NPCs generated their own utterances.");
            Require(pair.All(id=>checkpoint.memories.Any(m=>m.actorId==id&&m.origin=="dialogue")),"Both participants remember statements or commitments from the exchange.");
            Require(!checkpoint.memories.Any(m=>m.actorId=="c07"&&m.origin=="dialogue"),"Off-scene NPC does not receive private conversation.");
            checks.Add("NPC chooses a partner, approaches, faces, alternates model-generated lines and finishes; player/other NPC movement continues");
            checks.Add("Both participants receive attributed memories; an absent NPC cannot read the conversation");
            var rememberedIds=checkpoint.memories.Where(m=>m.actorId==pair[0]&&m.origin=="dialogue").Select(m=>m.id).ToArray();
            var rememberedActor=M.Spawner.Find(pair[0]);
            player.Teleport(rememberedActor.transform.position+Vector3.left*1.2f);W.Follow.Snap();
            M.BeginPlayerChat(pair[0]);
            int beforeReply=M.Conversations.CommittedNpcLines;
            M.Conversations.SendPlayer("你和"+SevenCharacterCatalog.Name(pair[1])+"刚才聊到了什么？你现在有什么打算？");
            yield return Wait(()=>M.Conversations.CommittedNpcLines>beforeReply,real?90:20,"Recall in a separate player conversation");
            Require(M.Conversations.LastRecalledMemoryIds.Any(id=>rememberedIds.Contains(id)),"The later reply receives memories from the previous NPC exchange.");
            yield return Frames(30);M.Conversations.Cancel("Verification follow-up complete");
            yield return Wait(()=>M.Client.Synchronized,45,"Follow-up memory settled");
            checks.Add("A separate player conversation recalls the preceding NPC exchange through the same memory path");
            observedNpcLines=M.Conversations.CommittedNpcLines;observedConversations=M.Conversations.CompletedConversations;
            recording=false;
            M.Conversations.Cancel("Verification travel");S.Travel(PlaceId.Cafe);
            yield return Wait(()=>!W.IsTransitioning&&W.Stage.place==PlaceId.Cafe&&M.Spawner.Find("c07")!=null,20,"Absent NPC appears in cafe");
            Require(ReadCheckpoint().memories.Any(m=>m.actorId=="c07"&&m.origin=="public_day4"),"Arriving NPC already knows Day4.");
            S.Save();S.LoadSavedGame();
            yield return Wait(()=>!W.IsTransitioning&&M.Client.Synchronized,35,"Post-Day4 load");
            Require(ReadCheckpoint().memories.Count(m=>m.origin=="public_day4")==7,"Loading Day4 does not duplicate memories.");
            Require(S.State.memory.day4Shown&&!M.View.Day4Visible,"Loading does not repeat the shown announcement.");
            checks.Add("Scene travel and loading preserve off-scene Day4 knowledge without repeat announcements or memory duplication");
        }
        static IEnumerator VerifyReadback()
        {
            M.Conversations.Cancel("Save rollback setup");S.Save();string old=File.ReadAllText(SaveRepository.PathOnDisk);
            var npc=M.Spawner.Find("c01");W.Player.GetComponent<PlayerController>().Teleport(npc.transform.position+Vector3.left*1.2f);
            yield return null;Press(UnityEngine.InputSystem.Key.E);yield return Frames(3);Press();
            Require(M.PlayerChatOpen,"Existing E interaction opens NPC memory conversation.");
            M.Conversations.SendPlayer("这是读档后不应存在的未来测试句子。");
            yield return Frames(2);
            M.BeforeStateChange();File.WriteAllText(SaveRepository.PathOnDisk,old);S.LoadSavedGame();
            yield return Wait(()=>!W.IsTransitioning&&M.Client.Synchronized&&M.Spawner.Actors.Count==4,30,"Earlier save restored");
            Require(!M.PlayerChatOpen&&string.IsNullOrEmpty(M.Conversations.ActiveId),"Loading releases the conversation.");
            Require(!ReadCheckpoint().messages.Any(m=>m.content.Contains("未来测试句子")),"Late player/model results do not enter the restored past.");
            checks.Add("Player E interaction uses the memory path; loading cancels participants and prevents future dialogue from entering the older save");
        }
        static IEnumerator VerifyUnavailable()
        {
            bool offline=Environment.GetCommandLineArgs().Contains("-memoryOffline");
            if(!offline)yield return Wait(()=>M.Client.Reachable&&!M.Client.Configured&&M.Client.Synchronized,15,"Unconfigured service");
            else yield return Frames(60);
            var player=W.Player.GetComponent<PlayerController>();float x=player.transform.position.x;float until=Time.unscaledTime+1;
            while(Time.unscaledTime<until){Press(UnityEngine.InputSystem.Key.D);yield return null;}Press();
            Require(player.transform.position.x>x+.3f,"Missing model/service does not block player movement.");
            SetupCapture();recording=true;recordingStart=EditorApplication.timeSinceStartup;nextCapture=0;
            while(S.State.day<4){S.Clock.Advance();yield return null;}
            yield return Wait(()=>S.State.memory.day4Shown,5,"Offline public notification");
            if(!offline){yield return Wait(()=>M.Client.Synchronized,20,"Unconfigured Day4 delivery");Require(ReadCheckpoint().memories.Count(m=>m.origin=="public_day4")==7,"Day4 memory does not require model text.");}
            else Require(S.State.memory.outbox.Any(e=>e.kind=="day4"),"Offline notification remains in the durable outbox.");
            yield return Frames(30);
            Require(M.Conversations.CommittedNpcLines==0&&!M.Client.CanGenerate,"No fixed NPC replies stand in for a missing model.");
            checks.Add(offline?"Disconnected service: playable scene, explicit status, durable Day4 notification and no invented NPC replies":"Unconfigured model: playable scene, seven-person Day4 delivery, explicit status and no invented NPC replies");
            recording=false;
        }
        static Checkpoint ReadCheckpoint(){return JsonUtility.FromJson<Checkpoint>(S.State.memory.checkpoint);}
        static void SetupCapture()
        {
            camera=W.Follow.GetComponent<Camera>();render=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);render.Create();camera.targetTexture=render;
            foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
        }
        static void Capture()
        {
            if(camera==null||render==null)return;var previous=RenderTexture.active;
            var pixels=new Texture2D(render.width,render.height,TextureFormat.RGB24,false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=render});
                RenderTexture.active=render;pixels.ReadPixels(new Rect(0,0,render.width,render.height),0,0);pixels.Apply();
                string file="frame_"+(frameIndex++).ToString("D5")+".png";File.WriteAllBytes(Path.Combine(output,"frames",file),pixels.EncodeToPNG());
                frames.Add(new Frame{file=file,time=EditorApplication.timeSinceStartup-recordingStart,stage=M.Conversations.Stage});
            }finally{RenderTexture.active=previous;Object.Destroy(pixels);}
        }
        static void Complete(string error)
        {
            if(ending)return;ending=true;recording=false;SessionState.SetBool(Key+".finished",true);
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            var report=new Report{passed=error==null,error=error??"",mode=M?.Client.Mode??"unknown",unityVersion=Application.unityVersion,savePath=SaveRepository.PathOnDisk,checks=checks.ToArray(),issues=issues.ToArray(),editorOnlyIssues=editorOnlyIssues.ToArray(),frames=frames.ToArray(),npcLines=Math.Max(observedNpcLines,M?.Conversations.CommittedNpcLines??0),completedConversations=Math.Max(observedConversations,M?.Conversations.CompletedConversations??0)};
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
            if(originalSettings!=null){InputSystem.settings=originalSettings;originalSettings.hideFlags=oldFlags;}
            if(testSettings!=null)Object.Destroy(testSettings);
            if(camera!=null)camera.targetTexture=null;
            if(render!=null){render.Release();Object.Destroy(render);}
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));SessionState.SetInt(Key+".exit",error==null?0:1);
            if(error==null)Debug.Log("[Seven Memory] Verification passed.");else Debug.LogError("[Seven Memory] "+error);
            EditorApplication.ExitPlaymode();
        }
        static void Press(params Key[] keys){keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
        static IEnumerator Frames(int n){for(int i=0;i<n;i++)yield return null;}
        static IEnumerator Wait(Func<bool> condition,float seconds,string label){double end=EditorApplication.timeSinceStartup+seconds;while(!condition()){Require(EditorApplication.timeSinceStartup<end,label+" timed out");yield return null;}}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static string Arg(string name,string fallback){var a=Environment.GetCommandLineArgs();int n=Array.IndexOf(a,name);return n>=0&&n+1<a.Length?a[n+1]:fallback;}
    }
}
