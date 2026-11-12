using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Seabreeze.EndingDemo
{
    public static class IntegratedEndingBuild
    {
        [MenuItem("Seabreeze/Ending Demo/Build full game with ending entry")]
        public static void Build()
        {
            string originalProduct=PlayerSettings.productName;
            string originalId=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            try {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"com.projectz.islandending");
                string path=Path.GetFullPath("Builds/IslandEnding/ProjectZ-IslandEnding.app");Directory.CreateDirectory(Path.GetDirectoryName(path));
                var options=BuildOptions.Development;
                if(Environment.GetEnvironmentVariable("Z_ENDING_SCRIPTS_ONLY")=="1")options|=BuildOptions.BuildScriptsOnly;
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=path,target=BuildTarget.StandaloneOSX,options=options});
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Integrated ending build failed: "+report.summary.result);
                UnityEngine.Debug.Log("ISLAND_ENDING_BUILD_OK "+path);
            } finally { PlayerSettings.productName=originalProduct;PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,originalId); }
        }
    }
}
