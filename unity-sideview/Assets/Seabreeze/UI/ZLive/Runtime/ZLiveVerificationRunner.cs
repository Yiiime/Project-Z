#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ZLive.ModelPerformance;

namespace ZLive
{
    // Only instantiated by the explicit editor verification command; no player bootstrap.
    [DefaultExecutionOrder(20000)]
    public sealed class ZLiveVerificationRunner : MonoBehaviour
    {
        public string OutputDirectory;
        public Action<bool> Completed;
        readonly JArray checks = new JArray();
        readonly List<TaskCompletionSource<bool>> frames = new List<TaskCompletionSource<bool>>();
        bool passed = true;
        CharacterPresenter character;
        LiveAudioPlayer player;
        Seabreeze.NativeLiveHost host;
        float originalTimeScale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(Application.isEditor)return;
            foreach(var arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--z-live2d-verify=")) {
                var runner=new GameObject("Live Z verification").AddComponent<ZLiveVerificationRunner>();
                runner.OutputDirectory=arg.Substring("--z-live2d-verify=".Length);
                runner.Completed=ok=>Application.Quit(ok?0:1);
                break;
            }
        }

        async void Start()
        {
            originalTimeScale = Time.timeScale;
            Directory.CreateDirectory(OutputDirectory);
            try { await Verify(); }
            catch (Exception e) { Check("unexpected_exception", false, e.ToString()); }
            finally
            {
                if (host != null) host.Close();
                Time.timeScale = originalTimeScale;
                File.WriteAllText(Path.Combine(OutputDirectory, "report.json"), new JObject {
                    {"passed", passed}, {"source", "Unity Play Mode / Street / NativeLiveHost; generated test audio, no provider calls"}, {"checks", checks}
                }.ToString());
                Completed?.Invoke(passed);
            }
        }

        void LateUpdate()
        {
            var pending = frames.ToArray(); frames.Clear();
            foreach (var frame in pending) frame.TrySetResult(true);
        }
        Task Frame() { var frame = new TaskCompletionSource<bool>(); frames.Add(frame); return frame.Task; }
        async Task Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) await Frame(); }
        void Check(string name, bool ok, string detail = "") { passed &= ok; checks.Add(new JObject {{"name",name},{"passed",ok},{"detail",detail}}); }

        async Task Open()
        {
            host = Seabreeze.NativeLiveHost.Open(transform, () => {});
            // Never load a developer's saved service credentials during offline verification.
            host.GetComponent<LiveFeatureController>().Configure("http://127.0.0.1:9", "offline-verification");
            await Wait(2);
            character = host.GetComponent<CharacterPresenter>();
            player = host.GetComponent<LiveAudioPlayer>();
            Check("model_ready", character != null && character.Ready);
            if (character == null || !character.Ready) throw new Exception("Z model failed to initialize");
            var v3Profile = character.PortraitResourcePath == CharacterPresenter.NativePortraitResource &&
                character.PortraitProfile != null && character.PortraitProfile.name == "ZFinal-v03";
            var v3Prefab = PrefabHasParameter("ParamZSpeech");
            Check("v3_profile_selected", v3Profile, character.PortraitResourcePath + ", name=" + (character.PortraitProfile != null ? character.PortraitProfile.name : "<null>"));
            Check("v3_prefab_native_parameter", v3Prefab);
            Check("v3_parameter_count", character.ParameterCount == 40, "count=" + character.ParameterCount);
            Check("speech_parameter_resolved", character.SpeechParameter == "ParamZSpeech", character.SpeechParameter);
            if (!v3Profile || !v3Prefab || character.ParameterCount != 40 || character.SpeechParameter != "ParamZSpeech")
                throw new Exception("night Z did not load the V3 portrait profile/prefab");
            player.Frame = Frame;
            player.Receipt = (command, receipt) => Task.CompletedTask;
            player.Source.volume = 0;
            player.Pause(false);
        }

        async Task Verify()
        {
            await Open();
            Time.timeScale = 0; // The phone pauses the world; character and audio must keep updating.
            Check("main_Z_portrait_loaded",character.UsingStoryPortrait);
            var profile=character.PortraitProfile;
            foreach(var mood in new[]{"sad","shy","serious","happy"}){
                string key=mood=="happy"?"happy_wave":mood;
                character.Cue(mood,mood=="happy"?"wave":"none","smooth");character.BeginSpeaking(true);
                await Wait(.8f);
                var expression=profile?.FindExpression(key);
                bool matches=expression!=null;
                if(matches)foreach(var entry in expression.Parameters)
                    if(entry.Id=="ParamBrowForm"||entry.Id=="ParamMouthForm"||entry.Id=="ParamCheek"||entry.Id=="ParamMouthVariant")
                        matches &= Mathf.Abs(character.ParameterValue(entry.Id)-entry.Value)<.12f;
                Check("main_profile_"+key,matches);
                CheckLegacyMouthDefault("main_profile_"+key);
                character.EndCue();
            }
            float clock = character.ZPerformance.Elapsed;
            await Wait(.25f);
            Check("unscaled_live_animation", character.ZPerformance.Elapsed > clock);
            var combinations = new [] {
                ("neutral","none"), ("happy","wave"), ("surprised","point"),
                ("angry","explain"), ("thinking","none"), ("shy","wave"),
                ("cry","wave"), ("bawl","explain"), ("sigh","none"), ("gloom","point")
            };
            foreach (var (mood, gesture) in combinations)
            {
                character.Cue(mood,gesture,"smooth"); character.BeginSpeaking(true);
                await Wait(.7f);
                Check(mood+"_expression", string.Equals(character.ZPerformance.CurrentExpression.ToString(), ZLivePerformanceMap.Expression(mood),StringComparison.OrdinalIgnoreCase));
                if (gesture != "none") {
                    string id = "ParamPose" + char.ToUpperInvariant(gesture[0]) + gesture.Substring(1);
                    Check(mood+"_gesture",character.ParameterValue(id)>.5f, id+"="+character.ParameterValue(id));
                }
                if(mood=="cry"||mood=="bawl"||mood=="gloom")Check(mood+"_accents",character.Accents.AnchorsReady && character.Accents.VisibleIntensity>.5f,character.Accents.ActiveAccent+" intensity="+character.Accents.VisibleIntensity+" anchors="+character.Accents.AnchorsReady);
                CheckLegacyMouthDefault(mood);
                if(mood=="cry") {
                    var g=character.Accents.GetComponent<UnityEngine.UI.Graphic>();
                    var renderer=g!=null?g.GetComponent<CanvasRenderer>():null;
                    Check("cry_canvas_renderer",renderer!=null);
                    var mesh=renderer!=null?renderer.GetMesh():null;
                    checks.Add(new JObject{{"name","accent_render_diagnostics"},{"passed",renderer!=null},{"vertices",mesh!=null?mesh.vertexCount:0},{"culled",renderer!=null&&renderer.cull},{"rect",g!=null?g.rectTransform.rect.ToString():"<null>"},{"world",g!=null?g.transform.position.ToString():"<null>"},{"scale",g!=null?g.transform.lossyScale.ToString():"<null>"}});
                }
                Capture(mood);
                if(mood=="happy")ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory,"live-room.png"));
                character.EndCue(); await Wait(.8f);
                Check(mood+"_reset", character.ZPerformance.CurrentExpression==ZFaceExpression.Neutral && character.ZPerformance.CurrentGesture==ZGesture.None && SpeechValue()<.01f);
            }
            character.Cue("happy","wave","smooth");character.BeginSpeaking(true);await Wait(.3f);
            player.Pause(true);await Wait(.1f);clock=character.ZPerformance.Elapsed;
            await Wait(.3f);Check("pause_freezes_performance",Mathf.Abs(character.ZPerformance.Elapsed-clock)<.001f && SpeechValue()<.01f);
            player.Pause(false);await Wait(.3f);Check("resume_performance",character.ZPerformance.Elapsed>clock);character.EndCue();

            var clip=AudioClip.Create("Z verification tone",24000*2,1,24000,false);
            var samples=new float[clip.samples];for(int i=0;i<samples.Length;i++)samples[i]=.10f*Mathf.Sin(i*2*Mathf.PI*220/24000)*(.5f+.5f*Mathf.Sin(i*2*Mathf.PI*3/24000));clip.SetData(samples,0);
            var cache=(Dictionary<string,AudioClip>)typeof(LiveAudioPlayer).GetField("cache",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player);cache["qa-tone"]=clip;
            var command=new JObject {{"payload",new JObject{{"text","口型验证"},{"mood","happy"},{"gesture","wave"},{"modality","audio"},{"audioArtifactId","qa-tone"}}}};
            await SampleAudio("speech",player.Host(command,CancellationToken.None));
            player.Source.volume=.15f;player.Source.mute=false;
            var audible=player.Host(command,CancellationToken.None);
            float outputPeak=0;var output=new float[1024];
            while(!audible.IsCompleted){await Frame();AudioListener.GetOutputData(output,0);foreach(var value in output)outputPeak=Mathf.Max(outputPeak,Mathf.Abs(value));}
            await audible;
            Check("listener_output_nonzero",outputPeak>.00001f,"peak="+outputPeak+", listenerVolume="+AudioListener.volume+", listenerPause="+AudioListener.pause+", sampleRate="+AudioSettings.outputSampleRate);
            var reconnect=player.Host(command,CancellationToken.None);await Wait(.4f);
            AudioSettings.Reset(AudioSettings.GetConfiguration());
            await reconnect;
            Check("audio_device_reset_completes",!player.Source.isPlaying);
            var stalled=player.Host(command,CancellationToken.None);await Wait(.4f);player.Source.Stop();
            await stalled;
            Check("stalled_source_recovers",!player.Source.isPlaying);
            var stopped=player.Host(command,CancellationToken.None);await Wait(.3f);player.Stop();
            bool stopCancelled=false;try{await stopped;}catch(OperationCanceledException){stopCancelled=true;}
            Check("explicit_stop_does_not_recover",stopCancelled&&!player.Source.isPlaying);
            var unavailable=player.Host(command,CancellationToken.None);await Wait(.3f);player.Source.Stop();
            await Wait(1.1f);player.Source.Stop();bool outputFailed=false;
            try{await unavailable;}catch(Exception e){outputFailed=e.Message=="audio_output_unavailable";}
            Check("unavailable_output_does_not_complete",outputFailed&&!player.Source.isPlaying);
            player.Source.volume=0;
            var song=new JObject{{"payload",new JObject{{"artifactId","qa-song"},{"kind","singing"},{"clips",new JArray(new JObject{{"artifactId","qa-tone"},{"text","测试唱段"},{"mood","warm"},{"gesture","explain"}})}}}};
            await player.Prepare(song,CancellationToken.None);
            await SampleAudio("singing",player.Play(song,CancellationToken.None));
            var cts=new CancellationTokenSource();var interrupted=player.Host(command,cts.Token);await Wait(.3f);cts.Cancel();
            try{await interrupted;}catch(OperationCanceledException){}
            await Wait(.2f);Check("cancel_resets",!player.Source.isPlaying && character.ZPerformance.CurrentGesture==ZGesture.None && SpeechValue()<.01f);cts.Dispose();

            int before=FindObjectsByType<CharacterPresenter>(FindObjectsSortMode.None).Length;
            host.Close();await Wait(2);Check("close_releases_character",FindObjectsByType<CharacterPresenter>(FindObjectsSortMode.None).Length==before-1);
            await Open();Check("reopen_single_character",FindObjectsByType<CharacterPresenter>(FindObjectsSortMode.None).Length==1);
            Capture("reopened");
        }

        async Task SampleAudio(string name,Task playback)
        {
            float maximum=0,minimum=1,legacyBaseline=character.ParameterDefaultValue("ParamMouthOpenY"),legacyMaximumDelta=0;
            while(!playback.IsCompleted){await Frame();float mouth=SpeechValue();maximum=Mathf.Max(maximum,mouth);minimum=Mathf.Min(minimum,mouth);if(character.UsesNativeSpeechParameter)legacyMaximumDelta=Mathf.Max(legacyMaximumDelta,Mathf.Abs(character.ParameterValue("ParamMouthOpenY")-legacyBaseline));}
            await playback;await Wait(.2f);
            Check(name+"_lip_sync",maximum>.15f && maximum-minimum>.1f,"min="+minimum+", max="+maximum);
            Check(name+"_completion_reset",!player.Source.isPlaying && SpeechValue()<.01f && character.ZPerformance.CurrentGesture==ZGesture.None);
            if(character.UsesNativeSpeechParameter)Check(name+"_legacy_mouth_not_double_written",legacyMaximumDelta<.03f,"delta="+legacyMaximumDelta);
        }

        float SpeechValue(){return character==null?0:character.ParameterValue(character.SpeechParameter);}

        void CheckLegacyMouthDefault(string name)
        {
            if(!character.UsesNativeSpeechParameter)return;
            var baseline=character.ParameterDefaultValue("ParamMouthOpenY");
            var value=character.ParameterValue("ParamMouthOpenY");
            Check(name+"_legacy_mouth_default",Mathf.Abs(value-baseline)<.03f,"value="+value+", default="+baseline);
        }

        bool PrefabHasParameter(string id)
        {
            var prefab=character?.PortraitProfile?.prefab;
            if(prefab==null)return false;
            foreach(var parameter in prefab.GetComponentsInChildren<Live2D.Cubism.Core.CubismParameter>(true))
                if(parameter.Id==id)return true;
            return false;
        }

        void Capture(string name)
        {
            var previous=RenderTexture.active;
            RenderTexture.active=character.Texture;
            var image=new Texture2D(character.Texture.width,character.Texture.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory,name+".png"),image.EncodeToPNG());
            Destroy(image);RenderTexture.active=previous;
        }
    }
}
#endif
