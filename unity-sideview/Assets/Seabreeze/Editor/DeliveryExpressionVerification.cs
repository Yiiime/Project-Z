using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZLive;

namespace Seabreeze.Editor
{
    // Explicit, isolated Play Mode verification. No provider keys or player saves.
    [InitializeOnLoad]
    public static class DeliveryExpressionVerification
    {
        const string Key="Z.DeliveryExpressionVerification";
        static readonly List<TaskCompletionSource<bool>> frames=new List<TaskCompletionSource<bool>>();
        static readonly JArray checks=new JArray();
        static bool running;
        static string output;
        static DeliveryExpressionVerification(){EditorApplication.update+=Tick;}
        public static void Begin(){
            output=Environment.GetEnvironmentVariable("Z_DELIVERY_QA_DIR");
            if(string.IsNullOrEmpty(output)||!Path.GetFullPath(output).StartsWith("/private/tmp/",StringComparison.Ordinal))throw new Exception("Use isolated Z_DELIVERY_QA_DIR under /private/tmp/");
            SessionState.SetString(Key+".Output",output);SessionState.SetBool(Key,true);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Show();
            EditorApplication.EnterPlaymode();
        }
        static void Tick(){
            var pending=frames.ToArray();frames.Clear();foreach(var frame in pending)frame.TrySetResult(true);
            if(!running&&SessionState.GetBool(Key,false)&&EditorApplication.isPlaying&&!EditorApplication.isCompiling){running=true;output=SessionState.GetString(Key+".Output","");_ = Verify();}
        }
        static Task Frame(){var t=new TaskCompletionSource<bool>();frames.Add(t);return t.Task;}
        static async Task Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await Frame();}
        static void Check(string name,bool ok,string detail=""){checks.Add(new JObject{{"name",name},{"passed",ok},{"detail",detail}});if(!ok)throw new Exception(name+": "+detail);}
        static void Capture(CharacterPresenter c,string name){
            var previous=RenderTexture.active;RenderTexture.active=c.Texture;
            var image=new Texture2D(c.Texture.width,c.Texture.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
            RenderTexture.active=previous;UnityEngine.Object.Destroy(image);
        }
        static async Task Verify(){
            NativeLiveHost host=null;bool passed=false;float scale=Time.timeScale;
            Directory.CreateDirectory(output);
            try{
                var parent=new GameObject("Delivery Expression QA");host=NativeLiveHost.Open(parent.transform,()=>{});
                host.GetComponent<LiveFeatureController>().Configure("http://127.0.0.1:9","offline-delivery-verification");
                Application.runInBackground=true;await Wait(2);Time.timeScale=0;
                var character=host.GetComponent<CharacterPresenter>();var player=host.GetComponent<LiveAudioPlayer>();
                Check("native_v3_ready",character!=null&&character.Ready&&character.PortraitResourcePath==CharacterPresenter.NativePortraitResource);
                var source=JObject.Parse(File.ReadAllText("Tools/MemoryLab/content/current/contracts/portrait-expressions.json"));
                Check("Z_keys_equal_user_json",source["actors"]["z"]["expressions"].Values<string>().SequenceEqual(CharacterPresenter.DialogueExpressions));
                foreach(var expression in CharacterPresenter.DialogueExpressions){
                    character.Cue("happy","none","smooth",0,false,expression);character.BeginSpeaking(true);await Wait(.85f);
                    Check("selected_"+expression,character.AppliedExpression==expression);
                    var asset=character.PortraitProfile.FindExpression(expression);
                    if(asset!=null){
                        var face=asset.Parameters.Where(p=>p.Id=="ParamBrowForm"||p.Id=="ParamMouthForm"||p.Id=="ParamCheek"||p.Id=="ParamMouthVariant").ToArray();
                        Check("parameters_"+expression,face.Length>0&&face.All(p=>Mathf.Abs(character.ParameterValue(p.Id)-p.Value)<.15f));
                    }else if(expression!="neutral")Check("authored_clip_"+expression,character.ActiveExpressionMotion==expression);
                    Capture(character,expression);character.EndCue();await Wait(.1f);
                    Check("reset_"+expression,character.AppliedExpression=="neutral"&&character.ActiveExpressionMotion==null);
                }
                character.Cue("happy","wave","smooth",0,false,"smile");
                Check("invalid_alias_resets_neutral",character.AppliedExpression=="neutral");character.EndCue();
                string spoken="行，你去吧。",delivery="舍不得但故作无所谓，音量稍低。";var captions=new List<string>();var receipts=new List<string>();bool observed=false;
                var originalCaption=player.Caption;player.Caption=text=>{captions.Add(text);originalCaption?.Invoke(text);observed|=character.AppliedExpression=="sad";};
                player.Frame=Frame;player.Receipt=(command,receipt)=>{receipts.Add((string)receipt["status"]);return Task.CompletedTask;};player.Pause(false);
                var packet=new JObject{{"payload",new JObject{{"actorId","z"},{"text",spoken},{"delivery",delivery},{"expression","sad"},{"mood","happy"},{"gesture","none"},{"transition","smooth"},{"speechId","delivery-qa"},{"modality","text"}}}};
                await player.Host(packet,CancellationToken.None);
                Check("wire_expression_overrides_mood",observed);
                Check("subtitle_only_spoken_text",captions.Contains(spoken)&&captions.All(t=>spoken.StartsWith(t,StringComparison.Ordinal)));
                Check("actual_playback_receipts",receipts.Contains("started")&&receipts.Contains("completed"));
                Check("post_playback_reset",character.AppliedExpression=="neutral");
                packet["payload"]["actorId"]="linxu";bool rejected=false;try{await player.Host(packet,CancellationToken.None);}catch(Exception e){rejected=e.Message=="host_actor_mismatch";}
                Check("host_cannot_impersonate_NPC",rejected);
                passed=true;
            }catch(Exception e){checks.Add(new JObject{{"name","exception"},{"passed",false},{"detail",e.ToString()}});}
            finally{
                Time.timeScale=scale;SessionState.SetBool(Key,false);if(host!=null)host.Close();
                File.WriteAllText(Path.Combine(output,"report.json"),new JObject{{"passed",passed},{"source","Unity Play Mode; real V3 model and LiveAudioPlayer; text-only playback, no TTS credentials"},{"checks",checks}}.ToString());
                Debug.Log("DELIVERY_EXPRESSION_"+(passed?"PASSED":"FAILED"));EditorApplication.Exit(passed?0:1);
            }
        }
    }
}
