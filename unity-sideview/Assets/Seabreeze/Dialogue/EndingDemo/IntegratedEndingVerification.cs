using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze.EndingDemo
{
    /// <summary>Only runs with an explicit QA argument and isolated test save path.</summary>
    public sealed class IntegratedEndingVerification : MonoBehaviour
    {
        string output; bool passed; string failure="";
        const BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-endingGameQA");if(i<0)return;
            string saves=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
            if(!saves.StartsWith("/private/tmp/",StringComparison.Ordinal)&&!saves.StartsWith("/tmp/",StringComparison.Ordinal))throw new InvalidOperationException("Ending QA requires isolated temporary saves.");
            var qa=new GameObject("Integrated ending verification").AddComponent<IntegratedEndingVerification>();DontDestroyOnLoad(qa);
            qa.output=i+1<args.Length?args[i+1]:Path.Combine(saves,"ending-qa");
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(output);var run=Run();
            while(true){object next=null;bool more=false;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){failure=e.ToString();break;}if(!more)break;yield return next;}
            File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":"+(passed?"true":"false")+",\"failure\":\""+failure.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","")+"\"}");
            Debug.Log(passed?"INTEGRATED_ENDING_QA_PASS":"INTEGRATED_ENDING_QA_FAIL "+failure);Application.Quit(passed?0:1);
        }
        static T Field<T>(GameUI ui,string name)=>(T)typeof(GameUI).GetField(name,Fields).GetValue(ui);
        static void Call(GameUI ui,string name,params object[] args)=>typeof(GameUI).GetMethod(name,Fields).Invoke(ui,args);
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static Button Find(GameUI ui,string name)=>Array.Find(ui.GetComponentsInChildren<Button>(true),b=>b.name==name);
        void TextDiagnostic(GameUI ui,string stage)
        {
            var text=Field<TextMeshProUGUI>(ui,"endingBody");var view=(RectTransform)text.transform.parent;
            var a=new Vector3[4];var b=new Vector3[4];text.rectTransform.GetWorldCorners(a);view.GetWorldCorners(b);
            int nonzero=0,colored=0;foreach(var v in text.mesh.vertices)if(v.sqrMagnitude>.1f)nonzero++;foreach(var c in text.mesh.colors32)if(c.a>0)colored++;
            var clip=text.materialForRendering.GetVector("_ClipRect");
            var data=new {page=Field<int>(ui,"endingPage"),length=text.text.Length,max=text.maxVisibleCharacters,count=text.textInfo.characterCount,
                cull=text.canvasRenderer.cull,alpha=text.canvasRenderer.GetAlpha(),render=(int)text.renderMode,meshVertices=text.mesh.vertexCount,
                width=text.rectTransform.rect.width,height=text.rectTransform.rect.height,x=text.rectTransform.anchoredPosition.x,y=text.rectTransform.anchoredPosition.y,
                bodyBottom=a[0].y,bodyTop=a[1].y,viewBottom=b[0].y,viewTop=b[1].y,nonzero,colored,clipX=clip.x,clipY=clip.y,clipZ=clip.z,clipW=clip.w};
            File.WriteAllText(Path.Combine(output,stage+"-text.json"),Newtonsoft.Json.JsonConvert.SerializeObject(data));
        }
        IEnumerator Run()
        {
            float deadline=Time.realtimeSinceStartup+90;
            while((GameUI.Instance==null||GameSession.Instance?.Memory?.Client?.CanGenerate!=true||WorldDirector.Instance?.IsTransitioning!=false)&&Time.realtimeSinceStartup<deadline)yield return null;
            var ui=GameUI.Instance;var session=GameSession.Instance;
            Require(ui!=null&&session?.Memory?.Client?.CanGenerate==true,"Main game did not initialize its memory service");
            int day=session.State.day,period=session.State.period;string place=session.State.place;
            session.Memory.PublishStoryEvent("ending_qa_shared","玩家当面对Z说：我记得你说过想看岛外，我会尊重你的选择。",new[]{"player","z"});
            deadline=Time.realtimeSinceStartup+30;while(!session.Memory.Client.CanGenerate&&Time.realtimeSinceStartup<deadline)yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"01-main-game.png"));yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.5f);
            var shortcut=Find(ui,"Seventh night shortcut");Require(shortcut!=null&&shortcut.gameObject.activeInHierarchy,"Main game shortcut is missing");shortcut.onClick.Invoke();
            deadline=Time.realtimeSinceStartup+150;
            while((string.IsNullOrEmpty(session.State.memory.seventhNightEndingJson)||Field<bool>(ui,"endingGenerating"))&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(!string.IsNullOrEmpty(session.State.memory.seventhNightEndingJson),"Ending was not persisted to the game save");
            var result=JsonUtility.FromJson<EndingDemoView.EndingRun>(session.State.memory.seventhNightEndingJson);
            Require(result.liveMemoryCount>0,"Ending omitted the current game memory");
            Require(result.worldId==session.State.memory.worldId,"Ending used another save's world");
            var portrait=Field<Live2DCharacterView>(ui,"endingCharacter");Require(portrait?.CurrentModel!=null&&portrait.CurrentProfile.characterId=="z","Game Z Live2D is missing");
            deadline=Time.realtimeSinceStartup+10;while(ui.IsOverlayTransitioning&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(ui.HasOverlay&&Mathf.Approximately(Time.timeScale,0),"Main gameplay did not pause under its ending overlay");
            Call(ui,"NextEndingPage");yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"02-z-native-ending.png"));yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.5f);
            for(int page=0;page<result.memoryCount+2;page++){
                if(Field<TextMeshProUGUI>(ui,"endingBody").maxVisibleCharacters!=int.MaxValue)Call(ui,"NextEndingPage");
                Call(ui,"NextEndingPage");yield return null;
                Require(!string.IsNullOrWhiteSpace(Field<TextMeshProUGUI>(ui,"endingBody").text),"An ending page is empty");
                Require(Field<TextMeshProUGUI>(ui,"endingBody").rectTransform.rect.height>1,"An ending page has collapsed text layout");
                if(page==0){Call(ui,"NextEndingPage");ScreenCapture.CaptureScreenshot(Path.Combine(output,"03-memory-in-game.png"));yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.5f);TextDiagnostic(ui,"memory");}
            }
            Require(Field<int>(ui,"endingPage")==result.memoryCount+2,"Full memory traversal did not reach the last page");
            Call(ui,"NextEndingPage");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"04-z-choice.png"));yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.5f);
            TextDiagnostic(ui,"choice");
            Find(ui,"Return to island").onClick.Invoke();deadline=Time.realtimeSinceStartup+10;while(ui.HasOverlay&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(!ui.HasOverlay&&Mathf.Approximately(Time.timeScale,1)&&!session.InputBlocked,"Return did not restore main gameplay");
            Require(session.State.day==day&&session.State.period==period&&session.State.place==place,"Demo overwrote the running world's time or place");
            Call(ui,"OpenOverlay","phone");deadline=Time.realtimeSinceStartup+10;while(ui.IsOverlayTransitioning&&Time.realtimeSinceStartup<deadline)yield return null;
            var phone=Find(ui,"Seventh night ending");Require(phone!=null,"Phone home ending button is missing");phone.onClick.Invoke();
            deadline=Time.realtimeSinceStartup+10;while(ui.IsOverlayTransitioning&&Time.realtimeSinceStartup<deadline)yield return null;yield return null;
            Require(Field<EndingDemoView.EndingRun>(ui,"seventhNight")?.id==result.id,"Phone entry did not replay the saved ending");
            ui.CloseOverlay();deadline=Time.realtimeSinceStartup+10;while(ui.HasOverlay&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(session.Save(),"Game save write failed");session.LoadSavedGame();
            Require(JsonUtility.FromJson<EndingDemoView.EndingRun>(session.State.memory.seventhNightEndingJson)?.id==result.id,"Ending disappeared after loading the game's save");
            passed=true;
        }
    }
}
