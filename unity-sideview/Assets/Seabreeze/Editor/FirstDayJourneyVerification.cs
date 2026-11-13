#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using ZLive;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
namespace Seabreeze.Editor
{
    // Runs the real scene loop: no phase assignment, synthetic dialogue or completion receipts.
    [InitializeOnLoad]
    public static class FirstDayJourneyVerification
    {
        const string Flag="Seabreeze.FirstDayJourneyVerification";
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        static string output,lastPhase,chatId;
        static double deadline,nextAction,phaseAt,lastReport,nextResume;
        static InputSettings originalInput,testInput;
        static Keyboard keyboard;
        static RenderTexture liveTarget;
        static Key[] heldKeys=Array.Empty<Key>();
        static readonly HashSet<string> voicedActors=new HashSet<string>();
        static int titleStage,chatTurns,chatRetries;
        static bool finished,songRequested,closingRequested,songCompleted;
        static string lastAmbient;static readonly JArray worldEvidence=new JArray();static readonly HashSet<string> motionSeen=new HashSet<string>();
        static readonly JArray events=new JArray(),errors=new JArray(),liveEvidence=new JArray();
        static readonly HashSet<string> liveEventIds=new HashSet<string>();
        static FirstDayJourneyVerification(){EditorApplication.update+=Tick;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Flag,false)&&t==LogType.Exception&&!s.Contains("UnityEditor.Search."))errors.Add(m);};}
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
            if(string.IsNullOrEmpty(output)||!output.StartsWith("/private/tmp/"))throw new Exception("Use isolated temporary saves");
            if(!File.Exists(Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE")))throw new Exception("Use an explicit test live connection");
            Directory.CreateDirectory(output);SessionState.SetString(Flag+"Output",output);SessionState.SetBool(Flag,true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();EditorApplication.EnterPlaymode();
        }
        static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Private).GetValue(o);
        static void Log(string text){events.Add(new JObject{["time"]=Math.Round(EditorApplication.timeSinceStartup,2),["event"]=text});Debug.Log("DAY01_JOURNEY "+text);}
        static void Keys(params Key[] keys){heldKeys=keys;}
        static void BeforeInput(){}
        static void AfterInput(){if(keyboard!=null){InputState.Change(keyboard,new KeyboardState(heldKeys));keyboard.MakeCurrent();}}
        static void Capture(string name){if(!Application.isBatchMode || (GameSession.Instance?.FirstDay?.Phase!=FirstDayPhase.Live && GameSession.Instance?.FirstDay?.Phase!=FirstDayPhase.EveningLive))ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));else if(liveTarget!=null){var previous=RenderTexture.active;var pixels=new Texture2D(liveTarget.width,liveTarget.height,TextureFormat.RGBA32,false);try{RenderTexture.active=liveTarget;pixels.ReadPixels(new Rect(0,0,liveTarget.width,liveTarget.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(pixels);}}}
        static void Write(bool? passed=null,string error=null){File.WriteAllText(Path.Combine(output,"journey-report.json"),new JObject{["passed"]=passed,["runtime"]=RuntimeState(),["phase"]=lastPhase,["events"]=events,["errors"]=errors,["songCompleted"]=songCompleted,["liveEvidence"]=liveEvidence,["worldEvidence"]=worldEvidence,["voicedActors"]=new JArray(voicedActors),["error"]=error,["scope"]="Actual UI handlers, keyboard movement, real configured model and playback receipts. No injected story phase or task completion."}.ToString());}
        static JObject RuntimeState()
        {
            var g=GameSession.Instance;var w=WorldDirector.Instance;
            var p=w?.Player;var actor=g?.State?.firstDay==null?null:g?.FirstDay?.Cast.FirstOrDefault(id=>!g.State.firstDay.greeted.Contains(id));
            return new JObject{["npcs"]=g?.Memory?.Spawner==null?new JArray():new JArray(g.Memory.Spawner.Actors.Where(a=>a!=null).Select(a=>new JObject{["id"]=a.npcId,["x"]=a.transform.position.x,["present"]=a.IsPresent,["moving"]=(bool?)a.Movement?.IsMoving,["ambient"]=g.Memory.Spawner.IsAmbientMoving(a.npcId),["result"]=a.Movement?.LastStartFailure})),["playerX"]=(float?)p?.position.x,["transitioning"]=(bool?)w?.IsTransitioning,["playerEnabled"]=(bool?)p?.GetComponent<PlayerController>()?.enabled,["frame"]=Time.frameCount,["held"]=string.Join(",",heldKeys),["axis"]=(float?)p?.GetComponent<PlayerController>()?.MoveAxis,["horizontal"]=GameInput.Horizontal,
                ["inputBlocked"]=(bool?)g?.InputBlocked,["firstDayBusy"]=(bool?)g?.FirstDay?.BlocksInput,["chatOpen"]=(bool?)g?.Memory?.PlayerChatOpen,
                ["closing"]=(bool?)g?.Memory?.View?.Closing,["scripted"]=(bool?)g?.Memory?.View?.ScriptedOpen,["scale"]=Time.timeScale,
                ["target"]=actor,["keyboard"]=(int?)Keyboard.current?.deviceId,["testKeyboard"]=(int?)keyboard?.deviceId,
                ["obstacles"]=p==null?new JArray():new JArray(Physics2D.RaycastAll(p.position+Vector3.up,Vector2.right,2).Select(h=>h.collider.name+" / "+h.collider.gameObject.layer+" / "+h.distance)),
                ["serviceError"]=g?.Memory?.Client?.LastError,["backgroundError"]=g?.Memory?.Client?.BackgroundError,["runtimePending"]=(int?)g?.State?.memory?.runtimeOutbox?.Count,["focused"]=Application.isFocused};
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||finished)return;
            try
            {
                output=SessionState.GetString(Flag+"Output","");var now=EditorApplication.timeSinceStartup;
                if(deadline==0){deadline=now+1800;Application.runInBackground=true;
                    originalInput=InputSystem.settings;testInput=UnityEngine.Object.Instantiate(originalInput);
                    testInput.hideFlags=HideFlags.DontSave;testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings=testInput;keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.onBeforeUpdate+=BeforeInput;InputSystem.onAfterUpdate+=AfterInput;}
                if(File.Exists(Path.Combine(output,"stop-verification")))throw new Exception("Verification stopped for repair");
                if(now>deadline)throw new Exception("Journey timeout in "+lastPhase);
                if(now-lastReport>5){lastReport=now;Write();Capture("current");}
                var game=GameSession.Instance;var world=WorldDirector.Instance;
                if(game==null||!game.IsReady)return;
                if(now<nextAction)return;
                Keys();
                if(game.IsAtTitle)
                {
                    if(titleStage==0&&Environment.GetCommandLineArgs().Contains("-journeyContinue")){typeof(StartPageView).GetMethod("Choose",Private).Invoke(game.GetComponent<StartPageView>(),new object[]{false});titleStage=2;Log("Continued actual saved journey");nextAction=now+1;return;}
                    if(titleStage==0){typeof(StartPageView).GetMethod("Choose",Private).Invoke(game.GetComponent<StartPageView>(),new object[]{true});titleStage=1;nextAction=now+1;return;}
                    var input=UnityEngine.Object.FindFirstObjectByType<TMP_InputField>();if(input==null)return;input.text="小海";
                    UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Confirm player name").onClick.Invoke();Capture("01-name");titleStage=2;Log("Confirmed fictional player name using landing form");nextAction=now+1;return;
                }
                var memory=game.Memory;if(memory==null||world?.Stage==null||world.IsTransitioning)return;
                var bubbles=UnityEngine.Object.FindObjectsByType<NpcSpeechBubble>(FindObjectsSortMode.None).Where(b=>b.Visible).ToArray();
                if(bubbles.Length>3)throw new Exception("More than three overhead bubbles");
                string ambient=string.Join(" | ",bubbles.Select(b=>b.VisibleText));
                if(ambient!=""&&ambient!=lastAmbient){lastAmbient=ambient;worldEvidence.Add(new JObject{["time"]=now,["conversation"]=memory.Conversations.ActiveId,["bubbles"]=ambient,["count"]=bubbles.Length});}
                foreach(var npc in memory.Spawner.Actors.Where(a=>a?.Movement!=null&&a.Movement.ActualSpeed>2&&a.Visual?.Pose=="Walk"))if(motionSeen.Add(npc.npcId))worldEvidence.Add(new JObject{["actor"]=npc.npcId,["speed"]=npc.Movement.ActualSpeed,["cycleSeconds"]=npc.Visual.NpcCycleSeconds,["playerSpeed"]=world.Player.GetComponent<PlayerController>().speed});
                string phase=game.FirstDay.Phase.ToString();
                if(phase!=lastPhase){lastPhase=phase;phaseAt=now;Log("Stage "+phase+" at "+game.CurrentPlace);Capture("stage-"+phase);}
                if(now-phaseAt>600)throw new Exception("No progress for ten minutes: "+phase+"; service="+memory.Client.LastError);
                if(game.State.day>=2&&!game.FirstDay.Active)
                {
                    if(errors.Count>0)throw new Exception("Game exceptions: "+errors);
                    if(!songCompleted)throw new Exception("Day 2 reached without a verified complete song playback");
                    if(game.Clock.RestActions!=0)throw new Exception("Day 2 must start with four unused rest actions");
                    Log("Second livestream completed; Day 2 restored with "+game.Clock.RestActions+" rests");Finish(true,null);return;
                }
                var invite=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.name=="Accept invitation"&&b.isActiveAndEnabled&&b.interactable);
                if(invite!=null){Log("Clicked invitation: "+invite.GetComponentInChildren<TMP_Text>().text);invite.onClick.Invoke();nextAction=now+1;return;}
                if(game.FirstDay.Phase==FirstDayPhase.Live||game.FirstDay.Phase==FirstDayPhase.EveningLive){
                    var room=UnityEngine.Object.FindFirstObjectByType<LiveFeatureController>();
                    if(room!=null&&Application.isBatchMode){var doc=room.GetComponent<UIDocument>();if(doc?.panelSettings!=null&&doc.panelSettings.targetTexture==null){if(liveTarget==null){liveTarget=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);liveTarget.Create();}doc.panelSettings.targetTexture=liveTarget;}}
                    if(room!=null&&(Application.isFocused||Application.isBatchMode)&&now>=nextResume&&Field<bool>(room,"connected")&&Field<bool>(room,"locallyPaused")){
                        nextResume=now+3;typeof(LiveFeatureController).GetMethod("ResumeRoom",Private).Invoke(room,null);Log("Resumed actual playback after editor focus change");}
                }
                if(game.FirstDay.Phase==FirstDayPhase.Live){
                    var feature=UnityEngine.Object.FindFirstObjectByType<LiveFeatureController>();
                    var doc=feature?.GetComponent<UIDocument>();
                    var action=doc?.rootVisualElement.Q<UnityEngine.UIElements.Button>("arrival-gift-action") ?? doc?.rootVisualElement.Q<UnityEngine.UIElements.Button>("arrival-reply");
                    if(action!=null && action.resolvedStyle.display!=DisplayStyle.None && action.enabledInHierarchy){
                        using(var click=NavigationSubmitEvent.GetPooled()){click.target=action;action.SendEvent(click);}
                        Log("Activated visible gift action");nextAction=now+2;
                    }
                    return;
                }
                if(game.FirstDay.Phase==FirstDayPhase.EveningLive)
                {
                    var feature=UnityEngine.Object.FindFirstObjectByType<LiveFeatureController>();if(feature==null||!Field<bool>(feature,"connected"))return;
                    var state=Field<JObject>(feature,"state");
                    if(state["room"]?["participants"] is JArray participants && participants.Any(p=>(string)p=="liyuan"))throw new Exception("Liyuan entered first evening");
                    foreach(var e in state["publicEvents"] as JArray??new JArray())
                    {
                        if(liveEventIds.Add((string)e["id"]))liveEvidence.Add(e.DeepClone());
                        if((string)e["type"]=="talent.completed")
                        {
                            var receipt=JObject.Parse((string)e["text"]);
                            if((string)receipt["kind"]=="singing"&&(int?)receipt["durationMs"]>0&&(int?)receipt["positionMs"]>=(int?)receipt["durationMs"]-250)
                            {if(!songCompleted){Log("Verified complete rendered song playback: "+(string)receipt["title"]);Capture("song-completed");}songCompleted=true;}
                        }
                    }
                    // Verify Z's proactive song and autonomous ending, without player prompting.
                    return;
                }
                if(memory.PlayerChatOpen){Chat(game,now);return;}
                if(memory.View.Closing||game.FirstDay.BlocksInput)return;
                if(game.FirstDay.Phase==FirstDayPhase.Harbor){if(!game.State.firstDay.harborReady)WalkToActor(game,world,"z");return;}
                if(game.FirstDay.Phase==FirstDayPhase.WalkToSquare){Keys(Key.D);return;}
                if(game.FirstDay.Phase==FirstDayPhase.MeetIslanders)
                {
                    if(!game.State.firstDay.announced||now-phaseAt<(Environment.GetCommandLineArgs().Contains("-journeyContinue")?10:55))return;
                    var actor=FirstDayDirector.FirstDayCast.FirstOrDefault(id=>!game.State.firstDay.greeted.Contains(id));
                    if(actor!=null)WalkToActor(game,world,actor);return;
                }
                if(game.FirstDay.Phase==FirstDayPhase.WelcomeTask)
                {
                    if(now-phaseAt<45)return;
                    var cp=JObject.Parse(game.State.memory.checkpoint??"{}");var rows=(cp["tasks"]?["state"] as JArray??new JArray());
                    var definition=(cp["tasks"]?["definitions"] as JArray??new JArray()).OfType<JObject>().FirstOrDefault(d=>rows.Any(r=>(string)r["task"]==(string)d["id"]&&new[]{"offered","active"}.Contains((string)r["status"])));
                    if(definition==null){var resident=memory.Spawner.Actors.FirstOrDefault(a=>a!=null&&a.IsPresent);if(resident!=null)WalkToActor(game,world,resident.npcId);return;}
                    var row=rows.First(r=>(string)r["task"]==(string)definition["id"]);
                    if((string)row["status"]=="offered"){WalkToActor(game,world,(string)definition["owner"]);return;}
                    var progress=JObject.Parse((string)row["progress"]??"{}");var step=((JArray)definition["steps"]).FirstOrDefault(s=>(bool?)progress[(string)s["id"]]?["succeeded"]!=true);
                    if(step==null)return;
                    if((string)step["action"]=="talk_to")WalkToActor(game,world,(string)step["target"]);
                    else {var camera=UnityEngine.Object.FindFirstObjectByType<PhotoCaptureService>();if(camera.Capture()){Log("Captured actual photo for generated task");nextAction=now+3;}}
                }
            }
            catch(Exception e){Finish(false,e.ToString());}
        }
        static void WalkToActor(GameSession game,WorldDirector world,string id)
        {
            var npc=game.Memory.Spawner.Find(id);if(npc==null)return;
            float delta=npc.transform.position.x-world.Player.position.x;
            if(Mathf.Abs(delta)>.65f){Keys(delta>0?Key.D:Key.A);return;}
            game.Memory.BeginPlayerChat(id); // Same distance-checked handler invoked by E.
            nextAction=EditorApplication.timeSinceStartup+.5;
        }
        static void Chat(GameSession game,double now)
        {
            var memory=game.Memory;var c=memory.Conversations;
            if(c.PlayerActor==null)return;
            if(memory.Client.SpeechPlaying)voicedActors.Add(c.PlayerActor.npcId);
            if(chatId!=c.ActiveId){chatId=c.ActiveId;chatTurns=0;chatRetries=0;Log("Opened conversation with "+c.PlayerActor.npcId);}
            if(c.Sending||!memory.View.PlayerInputReady||!memory.View.LineFullyVisible||!memory.Client.CanGenerate)return;
            if(!string.IsNullOrEmpty(memory.Client.LastError))
            {
                if(chatRetries++>=2)throw new Exception("Dialogue retry failed: "+memory.Client.LastError);
                Field<TMP_InputField>(memory.View,"input").text="";Field<Button>(memory.View,"send").onClick.Invoke();Log("Retried reply through visible button");nextAction=now+2;return;
            }
            if(game.FirstDay.Phase==FirstDayPhase.WelcomeTask)
            {
                var accept=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.isActiveAndEnabled&&b.interactable&&b.GetComponentInChildren<TMP_Text>()?.text=="接受");
                if(accept!=null){Log("Accepted AI-generated welcome task from visible card");Capture("welcome-task");accept.onClick.Invoke();nextAction=now+2;return;}
            }
            if(chatTurns==0)
            {
                string text=game.FirstDay.Phase==FirstDayPhase.Harbor?"你好 Z，我很高兴来岛上，愿意跟你去广场认识大家。":game.FirstDay.Phase==FirstDayPhase.WelcomeTask?"我想了解今天岛上的生活，有什么小事可以一起做？我们聊聊吧。":"你好，很高兴认识你！我叫小海，今天刚来岛上，你平时喜欢做什么？";
                if(game.FirstDay.Phase==FirstDayPhase.WelcomeTask)
                {
                    var cp=JObject.Parse(game.State.memory.checkpoint??"{}");
                    var row=(cp["tasks"]?["state"] as JArray??new JArray()).FirstOrDefault(r=>(string)r["status"]=="active");
                    var def=(cp["tasks"]?["definitions"] as JArray??new JArray()).FirstOrDefault(d=>(string)d["id"]==(string)row?["task"]);
                    if(def!=null){var progress=JObject.Parse((string)row["progress"]??"{}");var step=((JArray)def["steps"]).FirstOrDefault(st=>(bool?)progress[(string)st["id"]]?["succeeded"]!=true);if(step!=null)text="关于大家说的“"+(string)step["label"]+"”，你怎么想？";}
                }
                var input=Field<TMP_InputField>(memory.View,"input");input.text=text;Field<Button>(memory.View,"send").onClick.Invoke();chatTurns++;Log("Sent player greeting to "+c.PlayerActor.npcId);nextAction=now+1;return;
            }
            if(c.IntroductionReady)
            {
                Log("Completed exchange with "+c.PlayerActor.npcId);Capture("chat-"+c.PlayerActor.npcId);
                typeof(MemoryDialogueView).GetMethod("CloseConversationView",Private).Invoke(memory.View,null);nextAction=now+1;
            }
        }
        static void SendLive(LiveFeatureController feature,string text)
        {Field<UnityEngine.UIElements.TextField>(feature,"input").value=text;typeof(LiveFeatureController).GetMethod("Send",Private).Invoke(feature,null);Log("Sent livestream player message: "+text);}
        static void Finish(bool passed,string error){finished=true;Keys();InputSystem.onBeforeUpdate-=BeforeInput;InputSystem.onAfterUpdate-=AfterInput;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(originalInput!=null)InputSystem.settings=originalInput;if(testInput!=null)UnityEngine.Object.Destroy(testInput);Write(passed,error);Log(passed?"PASSED":"FAILED: "+error);SessionState.SetBool(Flag,false);EditorApplication.Exit(passed?0:1);}
    }
}
#endif
