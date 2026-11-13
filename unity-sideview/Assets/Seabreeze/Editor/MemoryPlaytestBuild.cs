using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class MemoryPlaytestBuild
    {
        public static void BuildDay01()
        {
            string destination=Path.GetFullPath(Arg("-memoryBuild","Builds/Day01-Playtest/ProjectZ-Day01.app"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=destination,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(destination),"build-report.txt"),$"Result: {report.summary.result}\nUnity: {Application.unityVersion}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nSize: {report.summary.totalSize}\n");
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Day0–1 build failed.");
        }
        [MenuItem("Seabreeze/Build eight-role Skill and memory playtest")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build from Edit mode.");
            string destination=Path.GetFullPath(Arg("-memoryBuild","Builds/SevenMemory/ProjectZSevenMemory.app"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=destination,target=BuildTarget.StandaloneOSX,options=BuildOptions.None });
            var folder=Path.GetDirectoryName(destination);
            File.WriteAllText(Path.Combine(folder,"build-report.txt"),$"Result: {report.summary.result}\nUnity: {Application.unityVersion}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nSize: {report.summary.totalSize}\n");
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Unified eight-role build failed.");
            string service=Path.GetFullPath("Tools/MemoryLab");
            foreach(string file in Directory.GetFiles(service,"*",SearchOption.AllDirectories))
            {
                string relative=file.Substring(service.Length+1);
                if(relative.Split(Path.DirectorySeparatorChar).Any(p=>p==".local"||p==".data"||p=="node_modules"||p=="tests"||p=="test-results")||relative.EndsWith(".log",StringComparison.Ordinal))continue;
                string target=Path.Combine(folder,"MemoryLab",relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
            }
            string node=Arg("-memoryNode",Environment.GetEnvironmentVariable("Z_MEMORY_NODE")??"");
            if(!string.IsNullOrEmpty(node)&&File.Exists(node))
            {
                Directory.CreateDirectory(Path.Combine(folder,"Runtime"));File.Copy(node,Path.Combine(folder,"Runtime/node"),true);
                string license=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(node)),"LICENSE");
                if(!File.Exists(license))license=Path.Combine(service,"licenses/NODE-LICENSE.txt");
                if(File.Exists(license))File.Copy(license,Path.Combine(folder,"Runtime/NODE-LICENSE.txt"),true);
            }
            File.WriteAllText(Path.Combine(folder,"启动七人记忆试玩.command"),"#!/bin/sh\nset -eu\ncd \"$(dirname \"$0\")\"\nif [ -x Runtime/node ]; then export Z_MEMORY_NODE=\"$PWD/Runtime/node\"; fi\nsh MemoryLab/start.sh --background\nopen ProjectZSevenMemory.app --args -memoryService \"http://127.0.0.1:${Z_MEMORY_PORT:-4330}\"\n");
            MakeExecutable(Path.Combine(folder,"启动七人记忆试玩.command"));
            if(File.Exists(Path.Combine(folder,"Runtime/node")))MakeExecutable(Path.Combine(folder,"Runtime/node"));
            Debug.Log("Unified eight-role playable built: "+destination);
        }
        static void MakeExecutable(string path)
        {
            var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName="/bin/chmod",Arguments="+x \""+path.Replace("\\","\\\\").Replace("\"","\\\"")+"\"",UseShellExecute=false,CreateNoWindow=true });
            process.WaitForExit();if(process.ExitCode!=0)throw new IOException("Could not mark launcher executable.");process.Dispose();
        }
        static string Arg(string name,string fallback){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
    }
}
