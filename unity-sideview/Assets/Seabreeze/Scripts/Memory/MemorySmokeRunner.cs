using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Seabreeze
{
    // Explicit standalone QA entry; never enabled during ordinary play.
    public sealed class MemorySmokeRunner : MonoBehaviour
    {
        [Serializable] sealed class Report {public bool passed;public string error,unityVersion,mode,savePath;public int publicMemories;public float playerDistance;public string[] issues;}
        [Serializable] sealed class Checkpoint {public Item[] memories;}
        [Serializable] sealed class Item {public string origin;}
        readonly List<string> issues=new List<string>();
        string output;bool ending;Keyboard keyboard;InputSettings settings,original;
        float distance;
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-memorySmokeOutput");
            output=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):"";
            string saves=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
            if(!Temporary(output)||!Temporary(saves)){Debug.LogError("Standalone smoke requires temporary output and save paths.");Application.Quit(1);yield break;}
            Directory.CreateDirectory(output);Application.logMessageReceived+=Log;
            float deadline=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<deadline&&(GameSession.Instance?.Memory==null||!GameSession.Instance.Memory.Client.Synchronized))yield return null;
            var s=GameSession.Instance;var w=WorldDirector.Instance;
            if(s?.Memory==null||!s.Memory.Client.Synchronized){Finish("Startup did not synchronize.");yield break;}
            original=InputSystem.settings;settings=Instantiate(original);settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=settings;
            keyboard=InputSystem.AddDevice<Keyboard>();
            float start=w.Player.position.x,until=Time.realtimeSinceStartup+1;
            while(Time.realtimeSinceStartup<until){keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));yield return null;}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());distance=w.Player.position.x-start;
            if(distance<.3f||!w.Player.GetComponentInChildren<CharacterVisual>().UsesPlayerMotion){Finish("Player movement/approved artwork failed.");yield break;}
            while(s.State.day<4){s.Clock.Advance();yield return null;}
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline&&(!s.State.memory.day4Shown||!s.Memory.Client.Synchronized))yield return null;
            var checkpoint=JsonUtility.FromJson<Checkpoint>(s.State.memory.checkpoint);
            if(checkpoint?.memories==null||checkpoint.memories.Count(m=>m.origin=="public_day4")!=7){Finish("Day4 did not reach seven NPCs.");yield break;}
            string image=Path.Combine(output,"standalone.png");ScreenCapture.CaptureScreenshot(image);
            deadline=Time.realtimeSinceStartup+10;while(!File.Exists(image)&&Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(1);
            Finish(File.Exists(image)?null:"Screenshot unavailable.");
        }
        static bool Temporary(string path)=>!string.IsNullOrEmpty(path)&&(path.StartsWith("/tmp/",StringComparison.Ordinal)||path.StartsWith("/private/tmp/",StringComparison.Ordinal)||path.StartsWith(Path.GetTempPath(),StringComparison.Ordinal));
        void Log(string message,string stack,LogType type){if(!ending&&(type==LogType.Error||type==LogType.Exception)){issues.Add(message);Finish(message);}}
        void Finish(string error)
        {
            if(ending)return;ending=true;Application.logMessageReceived-=Log;
            var s=GameSession.Instance;Checkpoint checkpoint=null;
            if(!string.IsNullOrEmpty(s?.State?.memory?.checkpoint))try{checkpoint=JsonUtility.FromJson<Checkpoint>(s.State.memory.checkpoint);}catch{ }
            var report=new Report { passed=error==null,error=error??"",unityVersion=Application.unityVersion,mode=s?.Memory?.Client.Mode??"unknown",savePath=SaveRepository.PathOnDisk,
                publicMemories=checkpoint?.memories?.Count(m=>m.origin=="public_day4")??0,playerDistance=distance,issues=issues.ToArray() };
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(original!=null)InputSystem.settings=original;if(settings!=null)Destroy(settings);
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));Application.Quit(error==null?0:1);
        }
    }
}
