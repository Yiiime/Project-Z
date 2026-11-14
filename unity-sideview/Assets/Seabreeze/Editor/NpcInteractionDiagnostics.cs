#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class NpcInteractionDiagnostics
    {
        const string Flag="Seabreeze.NpcInteractionDiagnostics";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static string output,actor;static int step,index,baseline;static double next,deadline;
        static Keyboard keyboard;static Key[] keys=Array.Empty<Key>();static InputSettings originalInput,testInput;
        static readonly JArray events=new JArray();static bool finished;static EditorWindow gameView;
        static NpcInteractionDiagnostics(){EditorApplication.update+=Tick;}
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
            if(string.IsNullOrEmpty(output)||!output.StartsWith("/private/tmp/"))throw new Exception("Use isolated saves");
            SessionState.SetString(Flag+"output",output);SessionState.SetBool(Flag,true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            gameView=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));gameView.autoRepaintOnSceneChange=true;gameView.Show();gameView.Focus();EditorApplication.EnterPlaymode();
        }
        static void Input(){if(keyboard!=null){InputState.Change(keyboard,new KeyboardState(keys));keyboard.MakeCurrent();}}
        static object Field(object o,string name)=>o?.GetType().GetField(name,Private)?.GetValue(o);
        static JObject State()
        {
            var g=GameSession.Instance;var m=g?.Memory;var w=WorldDirector.Instance;
            return new JObject{["day"]=g?.State?.day,["place"]=g?.State?.place,["phase"]=g?.FirstDay?.Phase.ToString(),["inputBlocked"]=g?.InputBlocked,["nonPlayerBlocked"]=g?.NonPlayerInputBlocked,["firstDayBusy"]=g?.FirstDay?.BlocksInput,["overlay"]=(bool?)Field(g,"overlay"),["dialogueOpen"]=g?.Dialogue?.IsOpen,["transitioning"]=w?.IsTransitioning,["timeScale"]=Time.timeScale,["current"]=w?.Player?.GetComponent<PlayerInteraction>()?.Current?.id,["chatActor"]=m?.Conversations?.PlayerActor?.npcId,["sending"]=m?.Conversations?.Sending,["closing"]=m?.View?.Closing,["scripted"]=m?.View?.ScriptedOpen,["canGenerate"]=m?.Client?.CanGenerate,["canGenerateFor"]=m?.Client!=null&&actor!=null?m.Client.CanGenerateFor(actor):(bool?)null,["serviceStatus"]=m?.Client?.Status,["serviceError"]=m?.Client?.LastError,["backgroundError"]=m?.Client?.BackgroundError,["focusing"]=(bool?)Field(m?.Client,"focusing"),["liveBinding"]=m?.Client?.LiveBinding!=null,["outbox"]=g?.State?.memory?.outbox.Count,["runtimeOutbox"]=g?.State?.memory?.runtimeOutbox.Count,["inputReady"]=m?.View?.PlayerInputReady,["actors"]=m?.Spawner==null?new JArray():new JArray(m.Spawner.Actors.Select(a=>new JObject{["id"]=a.npcId,["present"]=a.IsPresent,["ready"]=a.Movement?.IsReady,["x"]=a.transform.position.x}))};
        }
        static void Record(string message){events.Add(new JObject{["time"]=EditorApplication.timeSinceStartup,["event"]=message,["state"]=State()});File.WriteAllText(Path.Combine(output,"npc-diagnostic.json"),new JObject{["events"]=events}.ToString());Debug.Log("NPC_DIAGNOSTIC "+message);}
        static void Tick()
        {
            if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||finished)return;
            if(gameView==null)gameView=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));gameView.Repaint();
            try{
                output=SessionState.GetString(Flag+"output","");double now=EditorApplication.timeSinceStartup;
                if(deadline==0){deadline=now+240;Application.runInBackground=true;originalInput=InputSystem.settings;testInput=UnityEngine.Object.Instantiate(originalInput);testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=testInput;keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.onAfterUpdate+=Input;}
                if(now>deadline)throw new Exception("Probe timeout at step "+step);
                var g=GameSession.Instance;var w=WorldDirector.Instance;if(g==null||!g.IsReady)return;
                if(step==0){if(!g.IsAtTitle)return;typeof(StartPageView).GetMethod("Choose",Private).Invoke(g.GetComponent<StartPageView>(),new object[]{false});step=1;next=now+3;return;}
                if(now<next)return;keys=Array.Empty<Key>();
                if(g.IsAtTitle||w?.Stage==null||w.IsTransitioning)return;
                var m=g.Memory;
                if(step==1){Record("Loaded actual saved state");g.Travel(PlaceId.Street);step=2;next=now+3;return;}
                if(step==2){var actors=m.Spawner.Actors.Where(a=>a.IsPresent).OrderBy(a=>a.npcId).ToArray();if(actors.Length==0)throw new Exception("No Street NPCs");if(index>=Math.Min(3,actors.Length)){Record("PASS: E interaction and real replies");Finish(0);return;}if(actors.Length==0)throw new Exception("No Street NPCs");actor=actors[index].npcId;w.Player.GetComponent<PlayerController>().Teleport(new Vector2(actors[index].transform.position.x+1,actors[index].transform.position.y));step=3;next=now+1;return;}
                if(step==3){Record("Before E for "+actor);if(w.Player.GetComponent<PlayerInteraction>().Current?.id!=actor)throw new Exception("Expected NPC not selected by interaction scanner");keys=new[]{Key.E};step=4;next=now+.15;return;}
                if(step==4){step=5;next=now+3;return;}
                if(step==5){Record("After E for "+actor);ScreenCapture.CaptureScreenshot(Path.Combine(output,"opened-"+actor+".png"));if(m.Conversations.PlayerActor?.npcId!=actor)throw new Exception("E did not open target NPC");if(!m.Client.CanGenerate||!m.View.PlayerInputReady){next=now+3;return;}baseline=m.Conversations.CommittedNpcLines;((TMP_InputField)Field(m.View,"input")).text="早上好，今天你准备做些什么？";typeof(MemoryDialogueView).GetMethod("Send",Private).Invoke(m.View,null);Record("Sent diagnostic greeting");step=6;next=now+2;return;}
                if(step==6){if(m.Conversations.Sending){next=now+2;return;}Record("Reply completed");if(m.Conversations.CommittedNpcLines<=baseline)throw new Exception("No NPC reply committed");ScreenCapture.CaptureScreenshot(Path.Combine(output,"reply-"+actor+".png"));m.Conversations.Cancel();index++;step=2;next=now+2;}
            }catch(Exception e){Record("FAILED: "+e.Message);Finish(1);}
        }
        static void Finish(int code){finished=true;SessionState.SetBool(Flag,false);InputSystem.onAfterUpdate-=Input;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(originalInput!=null)InputSystem.settings=originalInput;EditorApplication.delayCall+=()=>EditorApplication.Exit(code);}
    }
}
#endif
