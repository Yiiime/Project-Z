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
            deadline=Time.realtimeSinceStartup+10;while(ui.IsOverlayTransitioning&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(ui.HasOverlay&&Mathf.Approximately(Time.timeScale,0),"Main gameplay did not pause under its ending overlay");
            var pages=Field<EndingDemoView.DailyPage[]>(ui,"endingDailyPages");
            Require(pages.Length==7,"Expected one page for each day in the seven-day fixture");
            var title=Field<TextMeshProUGUI>(ui,"endingTitle");
            var body=Field<TextMeshProUGUI>(ui,"endingBody");
            int firstPage=Field<int>(ui,"endingPage");
            Require(title.text==pages[0].title,"Daily reading should open with the first day");
            for(int i=0;i<pages.Length;i++)
            {
                Call(ui,"ShowEndingPage",firstPage+i);
                Require(title.text==pages[i].title && body.text.Contains("\n\n"),"Missing title or paragraph break");
            }
            Call(ui,"ShowEndingPage",firstPage);
            Call(ui,"ToggleEndingOriginal");Require(Field<bool>(ui,"endingOriginalShown"),"Cannot read source");
            Call(ui,"ToggleEndingOriginal");Require(body.text.Contains("\n\n"),"Cannot return to daily recollection");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-endingUploadQA")>=0){
                string oldId=result.id;
                var upload=Find(ui,"Upload ending memories");Require(upload!=null&&upload.interactable,"Native memory upload button is missing");
                upload.onClick.Invoke();File.WriteAllText(Path.Combine(output,"awaiting-upload.txt"),"Upload uploaded-memories.md in the browser opened by the actual game button.");
                deadline=Time.realtimeSinceStartup+240;
                while(Time.realtimeSinceStartup<deadline){
                    var newest=JsonUtility.FromJson<EndingDemoView.EndingRun>(session.State.memory.seventhNightEndingJson);
                    if(newest?.id!=oldId&&!Field<bool>(ui,"endingGenerating")){result=newest;break;}
                    yield return null;
                }
                Require(result.id!=oldId&&result.sourceName=="uploaded-memories.md","Uploaded memory did not automatically produce a native ending");
                Require(session.State.memory.checkpoint.Contains("NEW_UPLOAD_QA"),"Uploaded source did not persist into this game's checkpoint");
                Require(result.liveMemoryCount>0,"New ending omitted actual game experiences");
                Require(Field<EndingDemoView.EndingRun>(ui,"seventhNight")?.id==result.id,"Native display did not follow the uploaded source");
                Call(ui,"NextEndingPage");yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"05-uploaded-native-ending.png"));yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.5f);
                var history=Find(ui,"Previous ending version");Require(history!=null&&history.interactable,"Previous ending is not available");history.onClick.Invoke();yield return null;
                Require(Field<EndingDemoView.EndingRun>(ui,"seventhNight")?.id==oldId,"Old ending snapshot was lost after upload");
                history.onClick.Invoke();yield return null;
                Require(Field<EndingDemoView.EndingRun>(ui,"seventhNight")?.id==result.id,"Could not return to the latest ending");
                File.WriteAllText(Path.Combine(output,"upload-result.json"),session.State.memory.seventhNightEndingJson);
            }
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
            var legacyPath=Path.Combine(output,"legacy.json");
            if(File.Exists(legacyPath))
            {
                // Reopen an old-format ending inside this isolated test world. Keep
                // background polling from replacing the snapshot during capture.
                var legacy=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(legacyPath));
                legacy["worldId"]=session.State.memory.worldId;
                deadline=Time.realtimeSinceStartup+20;
                while((ui.IsOverlayTransitioning||WorldDirector.Instance?.IsTransitioning!=false||!session.Memory.Client.CanGenerate)&&Time.realtimeSinceStartup<deadline)yield return null;
                session.State.memory.seventhNightEndingJson=legacy.ToString();
                ui.OpenSeventhNight();
                deadline=Time.realtimeSinceStartup+15;
                while((Field<EndingDemoView.EndingRun>(ui,"seventhNight")?.id!=(string)legacy["id"])&&Time.realtimeSinceStartup<deadline)yield return null;
                typeof(GameUI).GetField("endingHistoryShown",Fields).SetValue(ui,true);
                typeof(GameUI).GetField("nextEndingSourceCheck",Fields).SetValue(ui,Time.unscaledTime+120);
                while(ui.IsOverlayTransitioning&&Time.realtimeSinceStartup<deadline)yield return null;
                pages=Field<EndingDemoView.DailyPage[]>(ui,"endingDailyPages");
                Require(pages.Length==7 && pages[6].title=="李庭叫我自己听","Older save did not load the reviewed seven days");
                Call(ui,"ShowEndingPage",firstPage+6);
                body=Field<TextMeshProUGUI>(ui,"endingBody");
                Require(body.text==pages[6].text && body.text.Split('\n').Length==5,"Day seven should have four sentences separated by a paragraph break");
                yield return new WaitForSecondsRealtime(.22f);
                var info=body.textInfo;
                int firstAlpha=-1,firstLineCount=0,hiddenLines=0;
                for(int i=0;i<info.characterCount;i++){
                    var ch=info.characterInfo[i];if(!ch.isVisible)continue;
                    int alpha=info.meshInfo[ch.materialReferenceIndex].colors32[ch.vertexIndex].a;
                    if(ch.lineNumber==0){
                        if(firstAlpha<0)firstAlpha=alpha;
                        Require(alpha==firstAlpha,"Characters on the same line faded independently");
                        firstLineCount++;
                    }else if(alpha==0)hiddenLines++;
                }
                Require(firstLineCount>1 && firstAlpha>0 && firstAlpha<255 && hiddenLines>0,"Did not observe a partial whole-sentence fade");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"legacy-day7-reveal.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(7);
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"legacy-day7.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(.2f);
                Call(ui,"NextEndingPage");
                yield return new WaitForEndOfFrame();
                var blackWords=Field<TextMeshProUGUI>(ui,"endingBlackBody");
                Require(Field<Image>(ui,"endingBlackScreen").gameObject.activeInHierarchy && blackWords.text.Length==0,"Ending did not start on an empty black screen");
                yield return new WaitForSecondsRealtime(.40f);
                Require(blackWords.text.Length==0,"Ending text appeared during the black beat");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"legacy-black.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(1.8f);
                Require(blackWords.text=="留洋，我想走。","Expected the reviewed decision after the black beat");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"legacy-choice.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(.4f);
            }
            passed=true;
        }
    }
}
