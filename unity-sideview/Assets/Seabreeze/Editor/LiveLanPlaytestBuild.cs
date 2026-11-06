using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Seabreeze.Editor {
public static class LiveLanPlaytestBuild {
    public static void VerifyConnection() {
        var c=JObject.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../Tools/live-service/.runtime/office/player-1.json"))));
        System.Threading.Tasks.Task.Run(async()=>{
            using(var transport=new ZLive.LiveTransport((string)c["baseUrl"],(string)c["token"],(string)c["certificateSha256"])) {
                var health=await transport.Request("/health");if((bool?)health["ok"]!=true)throw new Exception("health_failed");
                await transport.Enter(null,false,false);await transport.Control("end");
            }
            bool rejected=false;
            using(var wrong=new ZLive.LiveTransport((string)c["baseUrl"],(string)c["token"],new string('0',64)))try{await wrong.Request("/health");}catch{rejected=true;}
            if(!rejected)throw new Exception("pin_not_enforced");
        }).GetAwaiter().GetResult();
        Debug.Log("OFFICE_VERIFY_PASS: HTTPS, pinned certificate, authenticated session create/end, wrong pin rejected.");
    }
    [MenuItem("Seabreeze/Build Office Live Mac (Player 1)")]
    public static void Build() {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string file=Path.Combine(root,"Tools/live-service/.runtime/office/player-1.json");
        var c=JObject.Parse(File.ReadAllText(file));
        var url=new Uri((string)c["baseUrl"]);
        if(url.Scheme!="https"||!System.Text.RegularExpressions.Regex.IsMatch((string)c["certificateSha256"]??"","^[a-f0-9]{64}$"))throw new Exception("Invalid office connection configuration");
        var safe=new JObject{{"baseUrl",(string)c["baseUrl"]},{"token",(string)c["token"]},{"certificateSha256",(string)c["certificateSha256"]}};
        string app=Path.Combine(root,"Builds/OfficeLive/Player-1/Z Office Live.app");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=app,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Office build failed: "+report.summary.result);
        string dest=Path.Combine(app,"Contents/Resources/zlive-connection.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dest));File.WriteAllText(dest,safe.ToString());
        var start=new System.Diagnostics.ProcessStartInfo("/usr/bin/codesign","--force --deep --sign - \""+app.Replace("\"","\\\"")+"\""){UseShellExecute=false};
        using(var process=System.Diagnostics.Process.Start(start)){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Office app signing failed");}
        Debug.Log("Office Live build ready: "+app);
    }
}
}
