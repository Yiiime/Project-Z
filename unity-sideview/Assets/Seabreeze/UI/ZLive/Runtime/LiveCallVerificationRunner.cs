#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZLive {
// Opt-in Player verification; never runs in ordinary play and never captures audio.
public sealed class LiveCallVerificationRunner:MonoBehaviour {
    string output,url;readonly JArray checks=new JArray();readonly List<TaskCompletionSource<bool>> frames=new List<TaskCompletionSource<bool>>();
    bool passed=true;Seabreeze.NativeLiveHost host;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Bootstrap(){if(Application.isEditor)return;string url=null,output=null;foreach(string arg in Environment.GetCommandLineArgs()){if(arg.StartsWith("--z-call-verify="))url=arg.Substring(16);if(arg.StartsWith("--z-call-output="))output=arg.Substring(16);}if(url==null||output==null)return;var runner=new GameObject("Native call QA").AddComponent<LiveCallVerificationRunner>();runner.url=url;runner.output=output;}
    void Check(string name,bool ok,string detail=""){passed&=ok;checks.Add(new JObject{{"name",name},{"passed",ok},{"detail",detail}});}
    Task Frame(){var t=new TaskCompletionSource<bool>();frames.Add(t);return t.Task;}
    void LateUpdate(){var copy=frames.ToArray();frames.Clear();foreach(var t in copy)t.TrySetResult(true);}
    async Task Wait(float seconds){double end=Time.realtimeSinceStartupAsDouble+seconds;while(Time.realtimeSinceStartupAsDouble<end)await Frame();}
    async void Start(){Directory.CreateDirectory(output);try{await Verify();}catch(Exception e){Check("exception",false,e.ToString());}finally{host?.Close();File.WriteAllText(Path.Combine(output,"report.json"),new JObject{{"passed",passed},{"checks",checks},{"source","Unity Player; generated local sine PCM; no microphone or cloud calls"}}.ToString());Application.Quit(passed?0:1);}}
    async Task Verify(){
        NativeRtc.EnsureAvailable();Check("rtc_universal_plugin_loads",NativeRtc.State==0);NativeRtc.Stop();
        host=Seabreeze.NativeLiveHost.Open(transform,()=>{});var controller=host.GetComponent<LiveFeatureController>();controller.Configure("http://127.0.0.1:9","offline-verification");await Wait(2);controller.enabled=false;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(LiveFeatureController);
        type.GetField("state",flags).SetValue(controller,JObject.Parse("{\"phase\":\"live\",\"call\":{\"active\":true,\"id\":\"qa\",\"input\":\"idle\"}}"));
        type.GetField("locallyPaused",flags).SetValue(controller,false);type.GetField("connected",flags).SetValue(controller,true);
        type.GetMethod("UpdateCallView",flags).Invoke(controller,null);type.GetMethod("UpdateView",flags).Invoke(controller,null);
        type.GetMethod("ShowPlayerSpeech",flags).Invoke(controller,new object[]{"今天办公室有点吵，想来这里歇一会儿。",true});
        type.GetMethod("SetHostCaption",flags).Invoke(controller,new object[]{"那就先歇一会儿，今天慢慢聊。"});
        await Wait(.3f);var root=host.GetComponent<UIDocument>().rootVisualElement;
        Check("single_microphone",root.Query<Button>(className:"call-toggle").ToList().Count==1&&root.Query<Button>(className:"companion-button").ToList().Count==0);
        var dialogue=root.Q(className:"call-dialogue");Check("two_dialogue_rows",dialogue.childCount==2);Check("original_font_size",Math.Abs(root.Q<Label>(className:"caption").resolvedStyle.fontSize-14)<.1);
        Check("bounded_panel",root.Q(className:"speech-box").resolvedStyle.height<=166.1f);
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"call-panel.png"));await Wait(.3f);
        var player=host.GetComponent<LiveAudioPlayer>();player.Frame=Frame;player.Pause(false);player.Source.volume=.12f;
        using(var transport=new LiveTransport(url,"call-qa")){await transport.Enter(null,true,false);player.Transport=transport;
            var receipts=new List<JObject>();float began=Time.realtimeSinceStartup,first=-1;
            player.Receipt=(c,r)=>{receipts.Add((JObject)r.DeepClone());if((string)r["status"]=="started")first=Time.realtimeSinceStartup-began;return Task.CompletedTask;};
            var command=new JObject{{"payload",new JObject{{"speechId","qa-1"},{"audioStreamId","00000000-0000-4000-8000-000000000001"},{"modality","pcm-stream"},{"text","今天慢慢聊。"},{"mood","warm"},{"gesture","none"},{"transition","smooth"}}}};
            var task=player.Host(command,CancellationToken.None);double deadline=Time.realtimeSinceStartupAsDouble+10;
            while(!task.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)await Frame();Check("stream_finishes",task.IsCompleted);if(!task.IsCompleted)throw new Exception("stream hung");await task;
            Check("starts_before_final_audio",first>=0&&first<1.2f,first.ToString("F3")+" seconds");
            Check("completion_once",receipts.FindAll(r=>(string)r["status"]=="completed").Count==1);
            Check("complete_position",(int?)receipts.Find(r=>(string)r["status"]=="completed")?["positionMs"]==1600);
            receipts.Clear();using(var cancel=new CancellationTokenSource()){
                command["payload"]["speechId"]="qa-2";task=player.Host(command,cancel.Token);await Wait(.25f);
                float volume=player.Source.volume;await player.FadeOut("qa-2",180,CancellationToken.None);Check("quiet_fade",player.Source.volume<.01f);
                cancel.Cancel();try{await task;}catch(OperationCanceledException){}Check("no_false_completion_after_interrupt",!receipts.Exists(r=>(string)r["status"]=="completed"));Check("volume_restored",Math.Abs(player.Source.volume-volume)<.001f);Check("stopped_after_interrupt",!player.Source.isPlaying);
            }
        }
    }
}
}
#endif
