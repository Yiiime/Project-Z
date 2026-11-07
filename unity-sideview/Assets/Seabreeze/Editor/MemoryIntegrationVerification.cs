using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
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
            yield return Wait(()=>S!=null&&S.IsReady&&W!=null&&W.Stage!=null&&!W.IsTransitioning&&M!=null&&M.Spawner.Actors.Count==6,45,"Seven-person scene startup");
            Require(MemoryRuntimeConfig.Enabled,"Seven-person mode enabled.");
            Require(SaveRepository.PathOnDisk.StartsWith(Path.Combine(output,"saves")+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Isolated save directory.");
            Require(string.Join(",",SevenCharacterCatalog.Actors.Select(a=>a.displayName))=="留洋,晴子,梨园,伊停,周结,正义,星亮,Z,阿遥,陈伯","Exact worldbook identities.");
            Require(M.Spawner.Find("c07")==null,"Doctor begins outside the current scene.");
            checks.Add("Ten islander IDs/names, independent player and isolated game saves");
            originalSettings=InputSystem.settings;oldFlags=originalSettings.hideFlags;originalSettings.hideFlags=HideFlags.DontSave;
            testSettings=Object.Instantiate(originalSettings);testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=testSettings;keyboard=InputSystem.AddDevice<Keyboard>();
            if(Environment.GetCommandLineArgs().Contains("-memoryUnconfigured")||Environment.GetCommandLineArgs().Contains("-memoryOffline"))
            {yield return VerifyUnavailable();yield break;}
            yield return Wait(()=>M.Client.CanGenerate,35,"Configured isolated model connection");
            bool real=M.Client.Mode!="simulated";
            if(!real){yield return VerifyPresentation();yield return VerifyInput();yield return VerifyReadback();yield return VerifyNoJump();}
            yield return Wait(()=>M.Client.CanGenerate&&M.Spawner.Actors.All(a=>a.Movement!=null&&a.Movement.IsReady),30,"Restored scene ready");
            M.Conversations.Cancel("Verification setup");
            var player=W.Player.GetComponent<PlayerController>();player.Teleport(new Vector2(-2.8f,.1f));W.Follow.Snap();
            SetupCapture();recording=true;recordingStart=EditorApplication.timeSinceStartup;nextCapture=0;
            while(S.State.day<4){S.Clock.Advance();yield return null;}
            yield return Wait(()=>S.State.memory.day4Shown&&M.Client.Synchronized,45,"Day4 notice and ten-person delivery");
            var checkpoint=ReadCheckpoint();
            Require(checkpoint.memories.Count(m=>m.origin=="public_day4")==10,"One public Day4 memory per NPC.");
            Require(checkpoint.memories.Count(m=>m.origin=="staff_day4")==0,"No retired internal plot memory.");
            Require(S.State.memory.publicDay4KnownBy.Contains("player")&&S.State.memory.publicDay4KnownBy.Contains("z_story"),"Player and story Z receive the public fact.");
            M.EnsureDay4();Require(S.State.memory.outbox.All(e=>e.kind!="day4"),"Repeated evaluation does not enqueue the event again.");
            checks.Add("Game Day4 informs all ten, including absent NPCs; no retired staff or AI-identity setup");
            int initialCompleted=M.Conversations.CompletedConversations;
            yield return Wait(()=>M.Conversations.Stage=="交谈",real?150:60,"Autonomous choice and approach");
            var first=M.Conversations.First;var second=M.Conversations.Second;
            Require(first!=null&&second!=null&&first!=second,"Two actual scene participants.");
            Require(Vector2.Distance(first.transform.position,second.transform.position)<1.8f,"Participants walked into conversation range.");
            var pair=new[]{first.npcId,second.npcId};
            var observer=M.Spawner.Actors.First(a=>a!=first&&a!=second);
            player.Teleport(new Vector2((first.transform.position.x+second.transform.position.x)/2-2.4f,.1f));W.Follow.Snap();
            yield return Wait(()=>M.View.SpeechBubble.Visible,real?60:20,"Overhead NPC speech appears without player interaction");
            Require(!M.PlayerChatOpen&&pair.Contains(M.View.SpeechBubble.SpeakerId),"NPC-to-NPC speech uses a head bubble, not the player dialogue panel.");
            var nearby=player.transform.position;
            float middle=(first.transform.position.x+second.transform.position.x)*.5f;
            player.Teleport(new Vector2(middle+(middle>0?-7.5f:7.5f),.1f));W.Follow.Snap();yield return Frames(3);
            Require(!M.PlayerCanHear(first.transform.position)&&!M.PlayerCanHear(second.transform.position),"Player is beyond hearing range of both speakers.");
            Require(M.View.SpeechBubble.Visible,"Visible NPCs keep their bubble beyond hearing range.");
            int beforeFarLine=M.Conversations.CommittedNpcLines;
            yield return Wait(()=>M.Conversations.CommittedNpcLines>beforeFarLine,real?75:25,"New NPC speech while player is far away");
            Require(M.View.SpeechBubble.Visible&&!M.PlayerChatOpen,"A new far-away utterance appears automatically in a bubble.");
            string bubbleActor=M.View.SpeechBubble.SpeakerId;
            var queued=S.State.memory.outbox.LastOrDefault(e=>e.kind=="utterance"&&e.conversationId==M.Conversations.ActiveId&&e.speakerId==bubbleActor);
            var heard=queued!=null?queued.observers:ReadCheckpoint().messages.Last(m=>m.conversationId==M.Conversations.ActiveId&&m.speakerId==bubbleActor).participants;
            Require(!heard.Contains("player"),"Displaying a distant bubble does not change its actual witness list.");
            Capture();File.Copy(Path.Combine(output,"frames",frames.Last().file),Path.Combine(output,"overhead-bubble.png"),true);
            var viewCamera=W.Follow.GetComponent<Camera>();var cameraPosition=viewCamera.transform.position;
            W.Follow.enabled=false;
            try{viewCamera.transform.position+=Vector3.right*100;yield return Frames(2);Require(!M.View.SpeechBubble.Visible,"Off-screen speakers do not show bubbles.");}
            finally{viewCamera.transform.position=cameraPosition;W.Follow.enabled=true;}
            player.Teleport(nearby);W.Follow.Snap();yield return Frames(2);
            checks.Add("On-screen NPC speech ignores player distance, including newly spoken lines; off-screen hides it while memory witnesses stay unchanged");
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
            yield return Wait(()=>M.Conversations.OpeningComplete&&!M.Conversations.Sending&&M.Client.CanGenerate,real?90:25,"NPC opening before follow-up");
            var openingLines=ReadCheckpoint().messages.Where(m=>m.conversationId==M.Conversations.ActiveId).ToArray();
            Require(openingLines.Length==1&&openingLines[0].speakerId==pair[0]&&openingLines[0].origin=="model","NPC generated the first line; no synthetic player prompt was recorded.");
            Capture();File.Copy(Path.Combine(output,"frames",frames.Last().file),Path.Combine(output,"model-opening.png"),true);
            checks.Add("Player meeting begins with a model-generated, memory-contextual opening before the player speaks");
            int beforeReply=M.Conversations.CommittedNpcLines;
            M.Conversations.SendPlayer("你和"+SevenCharacterCatalog.Name(pair[1])+"刚才聊到了什么？你现在有什么打算？");
            yield return Wait(()=>M.Conversations.CommittedNpcLines>beforeReply,real?90:20,"Recall in a separate player conversation");
            Require(M.Conversations.LastRecalledMemoryIds.Any(id=>rememberedIds.Contains(id)),"The later reply receives memories from the previous NPC exchange.");
            yield return Frames(30);M.Conversations.Cancel("Verification follow-up complete");recording=false;
            yield return Wait(()=>M.Client.Synchronized,real?90:45,"Follow-up memory settled");
            checks.Add("A separate player conversation recalls the preceding NPC exchange through the same memory path");
            observedNpcLines=M.Conversations.CommittedNpcLines;observedConversations=M.Conversations.CompletedConversations;
            recording=false;
            M.Conversations.Cancel("Verification travel");S.Travel(PlaceId.Cafe);
            yield return Wait(()=>!W.IsTransitioning&&W.Stage.place==PlaceId.Cafe&&M.Spawner.Find("c07")!=null,20,"Absent NPC appears in cafe");
            Require(ReadCheckpoint().memories.Any(m=>m.actorId=="c07"&&m.origin=="public_day4"),"Arriving NPC already knows Day4.");
            S.Save();S.LoadSavedGame();
            yield return Wait(()=>!W.IsTransitioning&&M.Client.Synchronized,35,"Post-Day4 load");
            Require(ReadCheckpoint().memories.Count(m=>m.origin=="public_day4")==10,"Loading Day4 does not duplicate memories.");
            Require(S.State.memory.day4Shown&&!M.View.Day4Visible,"Loading does not repeat the shown announcement.");
            checks.Add("Scene travel and loading preserve off-scene Day4 knowledge without repeat announcements or memory duplication");
        }
        static IEnumerator VerifyReadback()
        {
            M.Conversations.Cancel("Save rollback setup");S.Save();string old=File.ReadAllText(SaveRepository.PathOnDisk);
            var npc=M.Spawner.Find("c01");W.Player.GetComponent<PlayerController>().Teleport(npc.transform.position+Vector3.left*1.2f);
            yield return null;Press(UnityEngine.InputSystem.Key.E);yield return Frames(3);Press();
            Require(M.PlayerChatOpen,"Existing E interaction opens NPC memory conversation.");
            yield return Wait(()=>M.Conversations.OpeningComplete&&!M.Conversations.Sending&&M.Client.CanGenerate,25,"Opening before rollback probe");
            M.Conversations.SendPlayer("这是读档后不应存在的未来测试句子。");
            yield return Frames(2);
            M.BeforeStateChange();File.WriteAllText(SaveRepository.PathOnDisk,old);S.LoadSavedGame();
            yield return Wait(()=>!W.IsTransitioning&&M.Client.Synchronized&&M.Spawner.Actors.Count==6,30,"Earlier save restored");
            Require(!M.PlayerChatOpen&&string.IsNullOrEmpty(M.Conversations.ActiveId),"Loading releases the conversation.");
            Require(!ReadCheckpoint().messages.Any(m=>m.content.Contains("未来测试句子")),"Late player/model results do not enter the restored past.");
            checks.Add("Player E interaction uses the memory path; loading cancels participants and prevents future dialogue from entering the older save");
        }
        static IEnumerator VerifyPresentation()
        {
            M.Conversations.Cancel("Presentation verification setup");
            yield return Wait(()=>!M.PlayerChatOpen,5,"Previous view closed");
            var z=M.Spawner.Find("z_story");Require(z!=null,"Z is present alongside the other islanders.");
            W.Player.GetComponent<PlayerController>().Teleport(z.transform.position+Vector3.left*1.2f);W.Follow.Snap();
            M.BeginPlayerChat("z_story");yield return Wait(()=>M.View.PlayerInputReady,8,"PR32 Z view entering");
            string openingId=M.Conversations.ActiveId;
            yield return Wait(()=>M.Conversations.OpeningComplete&&!M.Conversations.Sending&&M.Client.Synchronized,25,"NPC speaks before the player types");
            var openingMessages=ReadCheckpoint().messages.Where(m=>m.conversationId==openingId).ToArray();
            Require(openingMessages.Length==1&&openingMessages[0].speakerId=="z_story","Opening is a real NPC event without a fabricated player message.");
            Press(UnityEngine.InputSystem.Key.E);yield return Frames(3);Press();yield return Frames(8);
            Require(M.Conversations.ActiveId==openingId&&ReadCheckpoint().messages.Count(m=>m.conversationId==openingId)==1,"Repeated E does not issue another opening.");
            checks.Add("E entry generates exactly one memory-based NPC opening before any player line; repeated E does not duplicate it");
            var portrait=M.View.GetComponentInChildren<Live2DCharacterView>(true);
            Require(portrait!=null&&portrait.IsVisible&&portrait.CurrentProfile.characterId=="z_story","Only Z has the configured Live2D portrait.");
            var image=portrait.GetComponentInChildren<UnityEngine.UI.RawImage>();
            Require(image!=null&&image.texture!=null,"Z is rendered into the dialogue portrait surface.");
            SetupCapture();recordingStart=EditorApplication.timeSinceStartup;Capture();
            File.Copy(Path.Combine(output,"frames",frames.Last().file),Path.Combine(output,"z-dialogue.png"),true);frames.Clear();frameIndex=0;
            Press(UnityEngine.InputSystem.Key.Escape);yield return Frames(2);Press();
            yield return Wait(()=>!M.PlayerChatOpen,8,"PR32 reverse transition releases player input");
            Require(!S.InputBlocked,"Player input resumes after dialogue close.");
            Press(UnityEngine.InputSystem.Key.M);yield return Frames(2);Press();
            yield return Wait(()=>GameUI.Instance.HasOverlay&&!GameUI.Instance.IsOverlayTransitioning,5,"Map fade in");
            Capture();File.Copy(Path.Combine(output,"frames",frames.Last().file),Path.Combine(output,"map.png"),true);frames.Clear();frameIndex=0;
            Press(UnityEngine.InputSystem.Key.Escape);yield return Frames(2);Press();
            yield return Wait(()=>!GameUI.Instance.HasOverlay&&!S.InputBlocked,5,"Map fade out");
            Press(UnityEngine.InputSystem.Key.Tab);yield return Frames(2);Press();
            yield return Wait(()=>GameUI.Instance.HasOverlay&&!GameUI.Instance.IsOverlayTransitioning,5,"Phone fade in");
            Require(GameUI.Instance.GetComponentsInChildren<UnityEngine.UI.Button>().Any(b=>b.name=="Open Z Live"),"Existing live-room entry remains in the phone.");
            Capture();File.Copy(Path.Combine(output,"frames",frames.Last().file),Path.Combine(output,"phone.png"),true);frames.Clear();frameIndex=0;
            yield return VerifyLiveShell();
            Press(UnityEngine.InputSystem.Key.Escape);yield return Frames(2);Press();
            yield return Wait(()=>!GameUI.Instance.HasOverlay&&!S.InputBlocked,5,"Phone closes");
            checks.Add("PR32 layout and transitions: Z portrait, player unlock, map/phone fades and retained live-room entry");
        }
        static IEnumerator VerifyLiveShell()
        {
            const string sessionKey="ZLive.LocalSession";
            bool hadSession=PlayerPrefs.HasKey(sessionKey);string savedSession=PlayerPrefs.GetString(sessionKey,"");
            NativeLiveHost host=null;RenderTexture liveTarget=null;UnityEngine.UIElements.PanelSettings settings=null;
            try
            {
                PlayerPrefs.DeleteKey(sessionKey);
                host=NativeLiveHost.Open(GameUI.Instance.transform,()=>{});
                var feature=host.GetComponent<ZLive.LiveFeatureController>();
                // Configure synchronously before Start, so no production connection file is read.
                feature.Configure(MemoryRuntimeConfig.Settings.serviceUrl,"isolated-ui-check");
                yield return Frames(12);
                var document=host.GetComponent<UnityEngine.UIElements.UIDocument>();
                Require(document!=null&&document.rootVisualElement!=null,"Existing live-room UI builds with its retained behavior.");
                settings=document.panelSettings;
                liveTarget=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);liveTarget.Create();settings.targetTexture=liveTarget;
                yield return Frames(12);
                var previous=RenderTexture.active;var pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
                try{RenderTexture.active=liveTarget;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,"live-shell.png"),pixels.EncodeToPNG());}
                finally{RenderTexture.active=previous;Object.Destroy(pixels);}
            }
            finally
            {
                if(settings!=null)settings.targetTexture=null;
                if(host!=null)host.Close();
                if(liveTarget!=null){liveTarget.Release();Object.Destroy(liveTarget);}
                if(hadSession)PlayerPrefs.SetString(sessionKey,savedSession);else PlayerPrefs.DeleteKey(sessionKey);PlayerPrefs.Save();
            }
            yield return Wait(()=>Object.FindFirstObjectByType<NativeLiveHost>()==null,8,"Live shell releases its renderer/audio state");
            checks.Add("Live-room shell renders and closes against an isolated unavailable endpoint; no real speech/singing request");
        }
        static IEnumerator VerifyInput()
        {
            M.Conversations.Cancel("IME verification setup");
            var npc=M.Spawner.Find("c01");W.Player.GetComponent<PlayerController>().Teleport(npc.transform.position+Vector3.left*1.2f);
            M.BeginPlayerChat("c01");yield return Wait(()=>M.View.PlayerInputReady&&M.Conversations.OpeningComplete&&!M.Conversations.Sending&&M.Client.CanGenerate,25,"Input view and NPC opening ready");
            var field=M.View.GetComponentInChildren<TMPro.TMP_InputField>();
            Require(field!=null&&M.PlayerChatOpen,"Player dialogue has an editable field.");
            var module=EventSystem.current.currentInputModule;var previous=module.inputOverride;
            var ime=module.gameObject.AddComponent<MemoryImeVerificationInput>();
            string clipboard=GUIUtility.systemCopyBuffer;
            try
            {
                module.inputOverride=ime;
                field.text="嗯嗯嗯";field.MoveTextEnd(false);ime.Composition="e'e'e";field.ForceLabelUpdate();
                Require(field.textComponent.text.Contains("e'e'e")&&!field.textComponent.text.Contains("<u>"),"IME composition renders without literal underline tags.");
                Require(field.text=="嗯嗯嗯","Uncommitted composition is not stored as player text.");
                SetupCapture();recordingStart=EditorApplication.timeSinceStartup;Capture();
                File.Copy(Path.Combine(output,"frames",frames.Last().file),Path.Combine(output,"ime-composition.png"),true);
                frames.Clear();frameIndex=0;
                int before=S.State.memory.nextSeq;field.onSubmit.Invoke(field.text);yield return Frames(2);
                Require(S.State.memory.nextSeq==before&&!M.Conversations.Sending,"IME candidate confirmation does not send a player message.");
                ime.Composition="";field.text="测试ABC";field.MoveTextEnd(false);field.ForceLabelUpdate();
                field.ProcessEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Backspace});
                Require(field.text=="测试AB","Backspace preserves mixed Chinese and English input.");
                field.text="";GUIUtility.systemCopyBuffer="原样文本 <u>hello</u> 中文";
                field.ProcessEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.V,modifiers=EventModifiers.Command});field.ForceLabelUpdate();
                Require(field.text==GUIUtility.systemCopyBuffer&&field.textComponent.text.Contains("<u>hello</u>"),"User-pasted markup remains literal plain text.");
                field.text="中文选字完成，可以发送。";field.onSubmit.Invoke(field.text);
                yield return Wait(()=>ReadCheckpoint().messages.Any(m=>m.speakerId=="player"&&m.content=="中文选字完成，可以发送。"),20,"Committed plain-text player message");
                Require(!ReadCheckpoint().messages.Any(m=>m.content.Contains("e'e'e")),"IME draft never enters dialogue memory.");
            }
            finally {module.inputOverride=previous;GUIUtility.systemCopyBuffer=clipboard;Object.Destroy(ime);M.Conversations.Cancel("IME verification complete");}
            yield return Wait(()=>M.Client.Synchronized&&!M.PlayerChatOpen,30,"IME test memory settled");
            checks.Add("Simulated Chinese IME composition, candidate-submit guard, mixed-text backspace and literal paste; only committed text enters memory");
        }
        static IEnumerator VerifyNoJump()
        {
            M.Conversations.Cancel("No-jump check");yield return Wait(()=>!M.PlayerChatOpen,5,"Conversation closed before movement check");
            var player=W.Player.GetComponent<PlayerController>();player.Teleport(new Vector2(-12,.1f));yield return Frames(12);
            float ground=player.transform.position.y,peak=ground;
            foreach(var key in new[]{UnityEngine.InputSystem.Key.Space,UnityEngine.InputSystem.Key.W,UnityEngine.InputSystem.Key.UpArrow})
            {
                for(int i=0;i<12;i++){Press(key);yield return null;peak=Mathf.Max(peak,player.transform.position.y);}Press();yield return Frames(4);
            }
            Require(peak-ground<.15f&&player.Grounded,"Space, W and Up cannot launch the player.");
            player.Teleport(new Vector2(-12,1.1f));yield return Wait(()=>player.Grounded&&player.transform.position.y<.2f,4,"Gravity and landing still work");
            Require(player.transform.position.y<.2f,"Natural falling remains after jump removal.");
            checks.Add("Jump bindings removed while horizontal movement, gravity and landing remain");
        }
        static IEnumerator VerifyUnavailable()
        {
            bool offline=Environment.GetCommandLineArgs().Contains("-memoryOffline");
            if(!offline)yield return Wait(()=>M.Client.Reachable&&!M.Client.Configured&&M.Client.Synchronized,15,"Unconfigured service");
            else yield return Frames(60);
            if(!offline)
            {
                var npc=M.Spawner.Find("c01");W.Player.GetComponent<PlayerController>().Teleport(npc.transform.position+Vector3.left*1.2f);
                M.BeginPlayerChat("c01");yield return Wait(()=>M.View.PlayerInputReady&&!M.Conversations.Opening,8,"Unconfigured opening returns to waiting");
                Require(!M.Conversations.OpeningComplete&&M.Conversations.CanRetryOpening&&M.Conversations.CommittedNpcLines==0,"Missing model never invents a greeting.");
                M.Conversations.Cancel("Unavailable opening checked");yield return Wait(()=>!M.PlayerChatOpen,5,"Unavailable opening can close");
                checks.Add("Unconfigured automatic opening shows waiting/retry state without a fake greeting and releases movement on exit");
            }
            var player=W.Player.GetComponent<PlayerController>();float x=player.transform.position.x;float until=Time.unscaledTime+1;
            while(Time.unscaledTime<until){Press(UnityEngine.InputSystem.Key.D);yield return null;}Press();
            Require(player.transform.position.x>x+.3f,"Missing model/service does not block player movement.");
            SetupCapture();recording=true;recordingStart=EditorApplication.timeSinceStartup;nextCapture=0;
            while(S.State.day<4){S.Clock.Advance();yield return null;}
            yield return Wait(()=>S.State.memory.day4Shown,5,"Offline public notification");
            if(!offline){yield return Wait(()=>M.Client.Synchronized,20,"Unconfigured Day4 delivery");Require(ReadCheckpoint().memories.Count(m=>m.origin=="public_day4")==10,"Day4 memory does not require model text.");}
            else Require(S.State.memory.outbox.Any(e=>e.kind=="day4"),"Offline notification remains in the durable outbox.");
            yield return Frames(30);
            Require(M.Conversations.CommittedNpcLines==0&&!M.Client.CanGenerate,"No fixed NPC replies stand in for a missing model.");
            checks.Add(offline?"Disconnected service: playable scene, explicit status, durable Day4 notification and no invented NPC replies":"Unconfigured model: playable scene, ten-person Day4 delivery, explicit status and no invented NPC replies");
            recording=false;
        }
        static Checkpoint ReadCheckpoint(){return JsonUtility.FromJson<Checkpoint>(S.State.memory.checkpoint);}
        static void SetupCapture()
        {
            if(render!=null)return;
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
            if(error!=null&&M?.Client!=null)error+="\nClient: "+M.Client.Status+"; queued="+S.State.memory.outbox.Count+"; lastError="+M.Client.LastError;
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
    public sealed class MemoryImeVerificationInput : BaseInput
    {
        public string Composition="";
        public override string compositionString=>Composition;
    }
}
