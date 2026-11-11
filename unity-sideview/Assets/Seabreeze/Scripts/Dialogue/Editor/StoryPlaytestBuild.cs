using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class StoryPlaytestBuild
    {
        static string Arg(string key,string fallback){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        [MenuItem("Seabreeze/Story playtest/Verify progression rules")]
        public static void VerifyModel()
        {
            var config=new StoryProgressSettings();config.Validate();var state=new StoryProgressState();
            Require(state.CreditChat("linxu","first",1,0,config),"First completed chat scores");
            Require(!state.CreditChat("linxu","second",1,0,config),"Same-period chat is capped");
            Require(state.CreditChat("z","z-first",1,0,config),"Each NPC has independent chat cap");
            Require(state.CreditChat("linxu","next",1,1,config),"Next period enables chat score");
            Require(state.CreditTask("linxu","task-1",config),"Task credits");
            Require(!state.CreditTask("linxu","task-1",config),"Duplicate task cannot credit or advance time");
            Require(state.Person("linxu").points==14&&state.Person("z").points==2,"Weighted scores independent");
            for(int i=2;i<=3;i++)state.CreditTask("linxu","task-"+i,config);
            Require(state.queuedClips.SequenceEqual(new[]{"linxu_1"}),"First milestone queued once");
            state.FinishClip("linxu_1");
            var restored=JsonUtility.FromJson<StoryProgressState>(JsonUtility.ToJson(state));restored.Sanitize();
            Require(!restored.CreditTask("linxu","task-1",config)&&!restored.CreditChat("linxu","again",1,0,config),"Save restore preserves receipt and chat caps");
            for(int i=4;i<=10;i++)restored.CreditTask("linxu","task-"+i,config);
            Require(restored.winner=="linxu"&&restored.Person("linxu").points==100,"First NPC at maximum wins");
            Require(restored.queuedClips.SequenceEqual(new[]{"linxu_2","linxu_3","linxu_ending"}),"Chapters precede ending");
            Require(!restored.CreditTask("z","after-end",config),"Winner is locked");
            restored.FinishClip("linxu_ending");Require(restored.endingSeen,"Ending presentation acknowledgement persists");
            var many=new StoryProgressState();var alternate=new StoryProgressSettings{taskPoints=90,chatPoints=2};many.CreditTask("z","large",alternate);
            Require(many.queuedClips.SequenceEqual(new[]{"z_1","z_2","z_3"}),"Crossed milestones remain ordered");
            bool rejected=false;try{new StoryProgressSettings{milestones=new[]{50,25}}.Validate();}catch(InvalidOperationException){rejected=true;}Require(rejected,"Invalid config rejected");
            Debug.Log("STORY_PROGRESS_RULES: PASS (chat cap, tasks, replay, restore, milestones, winner, configuration)");
        }
        [MenuItem("Seabreeze/Story playtest/Build local player")]
        public static void Build()
        {
            VerifyModel();
            string output=Path.GetFullPath(Arg("-storyBuildOutput","Builds/StoryPlaytest/ProjectZStory.app"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string oldName=PlayerSettings.productName,oldId=PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Standalone);
            BuildReport report;
            try{
                PlayerSettings.productName="Project Z Story";PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,"com.projectz.storyplaytest");
                report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            }finally{PlayerSettings.productName=oldName;PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,oldId);}
            Require(report.summary.result==BuildResult.Succeeded,"Player build failed: "+report.summary.result);
            string media=Arg("-storyMediaSource","");
            if(media!="")
            {
                string target=Path.Combine(output,"Contents/Resources/Data/StreamingAssets/StoryPlaytest");Directory.CreateDirectory(target);
                foreach(string file in Directory.GetFiles(media,"*.mp4"))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
            }
            using(var sign=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/codesign"){Arguments="--force --deep --sign - \""+output+"\"",UseShellExecute=false}))
            {sign.WaitForExit();Require(sign.ExitCode==0,"Local development signing failed");}
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-result.json"),JsonUtility.ToJson(new BuildResultInfo{passed=true,player=output,unity=Application.unityVersion,bytes=(long)report.summary.totalSize},true));
            Debug.Log("STORY_PLAYER_BUILD: "+output);
        }
        [Serializable]sealed class BuildResultInfo{public bool passed;public string player,unity;public long bytes;}
    }
}
