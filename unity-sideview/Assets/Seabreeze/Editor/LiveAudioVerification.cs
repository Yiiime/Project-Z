using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZLive;

namespace Seabreeze.Editor {
[InitializeOnLoad]
public static class LiveAudioVerification {
    const string Key="ZLive.AudioVerification";
    static LiveAudioVerification(){EditorApplication.update+=Tick;}
    public static void Run(){
        SessionState.SetBool(Key,true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    static void Tick(){
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        SessionState.SetBool(Key,false);Verify();
    }
    static async Task<float> Output(LiveAudioPlayer player,JObject command){
        await Task.Delay(250); // Let the previous DSP buffer drain after changing mute.
        float peak=0;var samples=new float[1024];
        var playback=player.Host(command,CancellationToken.None);
        while(!playback.IsCompleted){await Task.Delay(16);AudioListener.GetOutputData(samples,0);foreach(float sample in samples)peak=Mathf.Max(peak,Mathf.Abs(sample));}
        await playback;return peak;
    }
    static async void Verify(){
        string directory=Path.GetFullPath("Builds/LiveSession/audio-verification");Directory.CreateDirectory(directory);
        var results=new JObject();int exit=1;GameObject root=null;
        float volume=AudioListener.volume;bool paused=AudioListener.pause;
        try{
            root=new GameObject("Isolated audio output verification");root.AddComponent<AudioListener>();
            var player=root.AddComponent<LiveAudioPlayer>();player.Frame=()=>Task.Delay(16);player.Receipt=(_,__)=>Task.CompletedTask;
            player.Source.volume=.2f;player.Source.mute=false;AudioListener.pause=true;
            var clip=AudioClip.Create("offline-tone",48000,1,48000,false);var pcm=new float[48000];
            for(int i=0;i<pcm.Length;i++)pcm[i]=.1f*Mathf.Sin(i*2*Mathf.PI*220/48000);clip.SetData(pcm,0);
            var cache=(Dictionary<string,AudioClip>)typeof(LiveAudioPlayer).GetField("cache",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player);cache["tone"]=clip;
            var command=new JObject{{"payload",new JObject{{"text","测试音"},{"modality","audio"},{"audioArtifactId","tone"}}}};
            AudioListener.volume=0;player.Source.ignoreListenerVolume=false;
            float oldPeak=await Output(player,command);results["old_world_muted_peak"]=oldPeak;
            player.Source.ignoreListenerVolume=true;
            float fixedPeak=await Output(player,command);results["fixed_world_muted_peak"]=fixedPeak;
            player.Source.mute=true;
            float mutedPeak=await Output(player,command);results["live_muted_peak"]=mutedPeak;
            player.Source.mute=false;player.Source.volume=0;
            float zeroPeak=await Output(player,command);results["live_zero_volume_peak"]=zeroPeak;
            if(oldPeak>=.00001f||fixedPeak<=.00001f||mutedPeak>=.00001f||zeroPeak>=.00001f)throw new Exception("Audio isolation output assertion failed");
            results["passed"]=true;exit=0;
        }catch(Exception e){results["passed"]=false;results["error"]=e.ToString();}
        finally{
            AudioListener.volume=volume;AudioListener.pause=paused;if(root!=null)UnityEngine.Object.Destroy(root);
            File.WriteAllText(Path.Combine(directory,"report.json"),results.ToString());
            Debug.Log("LIVE_AUDIO_VERIFICATION_"+(exit==0?"PASS":"FAIL"));EditorApplication.Exit(exit);
        }
    }
}
}
