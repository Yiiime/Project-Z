#if UNITY_EDITOR_OSX
using System;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ZLive.EditorTools {
// Keep SDK binaries out of Git. The reproducible setup script verifies vendor
// archives; copy them only to macOS builds that contain the native live feature.
public sealed class LiveRtcBuild:IPostprocessBuildWithReport {
    public int callbackOrder=>20;
    public void OnPostprocessBuild(BuildReport report) {
        if(report.summary.platform!=BuildTarget.StandaloneOSX)return;
        string source=NativeRtc.RuntimeDirectory;
        if(!File.Exists(Path.Combine(source,"libZLiveRtc.dylib")))throw new BuildFailedException("先运行 bash Tools/native-rtc/build-macos.sh 安装原生连麦依赖。");
        string target=Path.Combine(report.summary.outputPath,"Contents/Frameworks/ZLiveRtc");
        using(var process=Process.Start(new ProcessStartInfo("/usr/bin/ditto"){Arguments="\""+source+"\" \""+target+"\"",UseShellExecute=false})) {
            process.WaitForExit();if(process.ExitCode!=0)throw new BuildFailedException("RTC runtime copy failed");
        }
    }
}
}
#endif
