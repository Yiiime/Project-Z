using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        static TextMeshProUGUI VisibleText(Transform root,string fragment)
        {
            var label=Array.Find(root.GetComponentsInChildren<TextMeshProUGUI>(),t=>t.text.Contains(fragment));
            Require(label!=null,"Missing ending text: "+fragment);return label;
        }
        static Rect ScreenRect(RectTransform transform)
        {
            var corners=new Vector3[4];transform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y);
        }
        static Rect VisibleGlyphBounds(TextMeshProUGUI text,int start)
        {
            text.ForceMeshUpdate();
            var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            for(int i=0;i<text.textInfo.characterCount;i++){
                var ch=text.textInfo.characterInfo[i];if(!ch.isVisible||ch.index<start)continue;
                min=Vector2.Min(min,text.transform.TransformPoint(ch.bottomLeft));
                max=Vector2.Max(max,text.transform.TransformPoint(ch.topRight));
            }
            Require(min.y<max.y,"No visible glyphs in ending text");
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        void CheckDetailPage(GameUI ui,EndingDemoView.EndingRun run,bool longText)
        {
            Canvas.ForceUpdateCanvases();
            var root=Field<Image>(ui,"endingBlackScreen").transform;
            var words=VisibleText(root,run.decision.lastLine);
            var status=VisibleText(root,run.outcome.label);
            var button=Find(ui,"Ending black continue");Require(button.gameObject.activeInHierarchy&&button.interactable,"Missing return button");
            var scroll=words.GetComponentInParent<ScrollRect>();
            if(longText){
                Require(scroll!=null&&scroll.content.rect.height>scroll.viewport.rect.height+1,"Long ending cannot scroll");
                var pointer=new PointerEventData(EventSystem.current){position=ScreenRect(scroll.viewport).center,scrollDelta=new Vector2(0,-3)};
                var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                Require(hits.Count>0 && ExecuteEvents.GetEventHandler<IScrollHandler>(hits[0].gameObject)==scroll.gameObject,"Scroll wheel misses the ending text");
                float initial=scroll.verticalNormalizedPosition;
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.scrollHandler);
                Require(scroll.verticalNormalizedPosition<initial,"Scroll wheel did not move the ending text");
                scroll.StopMovement();scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();
            }
            var lastLine=VisibleGlyphBounds(words,words.text.LastIndexOf(run.decision.lastLine,StringComparison.Ordinal));
            var story=longText?lastLine:VisibleGlyphBounds(words,0);
            var state=VisibleGlyphBounds(status,0);var back=ScreenRect((RectTransform)button.transform);
            var readingArea=scroll==null?new Rect(0,0,Screen.width,Screen.height):ScreenRect(scroll.viewport);
            Require(story.yMin>=readingArea.yMin-1 && story.yMax<=readingArea.yMax+1,"Ending text is clipped");
            Require(!story.Overlaps(state) && !state.Overlaps(back) && !story.Overlaps(back),"Ending story, status and return button overlap");
            Require(state.yMin>=0&&state.yMax<=Screen.height&&back.yMin>=0&&back.yMax<=Screen.height,"Ending controls are outside the window");
        }
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
            bool checkLegacyRefresh=Array.IndexOf(Environment.GetCommandLineArgs(),"-endingLegacyQA")>=0;
            if(checkLegacyRefresh)
            {
                // Reproduce an old save whose cached ending predates versioned
                // upload support. This must not adopt the new source silently.
                var old=new EndingDemoView.EndingRun{id="legacy_without_revision",worldId=session.State.memory.worldId,
                    sourceName="OLD_LEGACY_FILE",memoryCount=0,liveMemoryCount=0,recollections=Array.Empty<EndingDemoView.Recollection>(),
                    decision=new EndingDemoView.Decision{title="旧缓存",opening="旧文件里的回望",reason="旧理由",choice="stay",decision="旧选择",lastLine="旧末句"},
                    outcome=new EndingDemoView.Outcome{label="旧结果",text="旧结果"}};
                session.State.memory.seventhNightEndingJson=Newtonsoft.Json.JsonConvert.SerializeObject(old);
            }
            var shortcut=Find(ui,"Seventh night shortcut");Require(shortcut!=null&&shortcut.gameObject.activeInHierarchy,"Main game shortcut is missing");shortcut.onClick.Invoke();
            deadline=Time.realtimeSinceStartup+150;
            while((string.IsNullOrEmpty(session.State.memory.seventhNightEndingJson)||Field<bool>(ui,"endingGenerating")||(checkLegacyRefresh&&session.State.memory.seventhNightEndingJson.Contains("legacy_without_revision")))&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(!string.IsNullOrEmpty(session.State.memory.seventhNightEndingJson),"Ending was not persisted to the game save");
            var result=JsonUtility.FromJson<EndingDemoView.EndingRun>(session.State.memory.seventhNightEndingJson);
            Require(result.liveMemoryCount>0,"Ending omitted the current game memory");
            Require(result.worldId==session.State.memory.worldId,"Ending used another save's world");
            if(checkLegacyRefresh)Require(result.id!="legacy_without_revision"&&result.sourceName!="OLD_LEGACY_FILE","Legacy cache did not refresh against the current source");
            var portrait=Field<Live2DCharacterView>(ui,"endingCharacter");Require(portrait?.CurrentModel!=null&&portrait?.CurrentProfile?.characterId=="z","Game Z Live2D is missing");
            deadline=Time.realtimeSinceStartup+10;while(ui.IsOverlayTransitioning&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(ui.HasOverlay&&Mathf.Approximately(Time.timeScale,0),"Main gameplay did not pause under its ending overlay");
            var pages=Field<EndingDemoView.RecapPage[]>(ui,"endingRecapPages");
            Require(pages.Length==3 && pages[0].startDay==1 && pages[0].endDay==1 && pages[1].startDay==2 && pages[1].endDay==3 && pages[2].startDay==4 && pages[2].endDay==7,"Seven days should use three chronological recap pages");
            var title=Field<TextMeshProUGUI>(ui,"endingTitle");
            var body=Field<TextMeshProUGUI>(ui,"endingBody");
            int firstPage=Field<int>(ui,"endingPage");
            Require(firstPage==0&&title.text==GameUI.RenderEndingDisplay(pages[0].title,result.playerName),"Ending should open with the early memories");
            for(int i=0;i<pages.Length;i++)
            {
                Call(ui,"ShowEndingPage",firstPage+i);
                Require(title.text==GameUI.RenderEndingDisplay(pages[i].title,result.playerName) && body.text.Contains("\n\n"),"Missing title or paragraph break");
            }
            Call(ui,"ShowEndingPage",firstPage);
            Call(ui,"ToggleEndingOriginal");Require(Field<bool>(ui,"endingOriginalShown"),"Cannot read source");
            Call(ui,"ToggleEndingOriginal");Require(body.text.Contains("\n\n"),"Cannot return to daily recollection");
            Call(ui,"ShowEndingPage",pages.Length);
            yield return new WaitForSecondsRealtime(1.6f);
            Require(Field<bool>(ui,"endingBlackRevealed")&&!Field<bool>(ui,"endingBlackDetailsShown"),"Decision did not remain readable before the automatic transition");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"auto-choice.png"));yield return new WaitForEndOfFrame();
            bool autoFadeOut=false,autoFadeIn=false;deadline=Time.realtimeSinceStartup+12;
            while((!Field<bool>(ui,"endingBlackDetailsShown")||Field<bool>(ui,"endingBlackTransitioning"))&&Time.realtimeSinceStartup<deadline)
            {
                float alpha=Field<CanvasGroup>(ui,"endingBlackContentGroup").alpha;
                if(alpha>.1f&&alpha<.9f){if(Field<bool>(ui,"endingBlackDetailsShown"))autoFadeIn=true;else autoFadeOut=true;}
                yield return null;
            }
            Require(autoFadeOut&&autoFadeIn&&Field<bool>(ui,"endingBlackDetailsShown")&&!Field<bool>(ui,"endingBlackTransitioning"),"The two black ending pages did not switch smoothly without input");
            CheckDetailPage(ui,Field<EndingDemoView.EndingRun>(ui,"seventhNight"),false);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"auto-details.png"));yield return new WaitForEndOfFrame();
            Call(ui,"ShowEndingPage",firstPage);
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
                pages=Field<EndingDemoView.RecapPage[]>(ui,"endingRecapPages");
                Require(pages.Length==3 && pages[0].title=="那个没折齐的纸角" && pages[1].title=="咖啡都凉了" && pages[2].title=="直播断了以后","Older save did not load all three reviewed pages");
                var args=Environment.GetCommandLineArgs();
                int wi=Array.IndexOf(args,"-screen-width"),hi=Array.IndexOf(args,"-screen-height");
                int width=0,height=0;
                if(wi>=0&&hi>=0&&wi+1<args.Length&&hi+1<args.Length&&int.TryParse(args[wi+1],out width)&&int.TryParse(args[hi+1],out height)){
                    Screen.SetResolution(width,height,FullScreenMode.Windowed);
                    deadline=Time.realtimeSinceStartup+8;
                    while((Screen.width!=width||Screen.height!=height)&&Time.realtimeSinceStartup<deadline)yield return null;
                    Require(Screen.width==width&&Screen.height==height,"Game did not reach the requested test resolution");
                    yield return new WaitForEndOfFrame();
                }
                var next=Find(ui,"Continue ending");
                title=Field<TextMeshProUGUI>(ui,"endingTitle");
                body=Field<TextMeshProUGUI>(ui,"endingBody");
                Require(title.text==pages[0].title && next.interactable,"Older save should open on the first recap");
                var reviewed=Field<EndingDemoView.EndingRun>(ui,"seventhNight");
                Require(body.text.Contains("字小节")&&!body.text.Contains("旅人")&&SevenCharacterCatalog.Name("player")=="字小节","Old save and island use different player names");
                string earlyPages = pages[0].text + "\n" + pages[1].text;
                foreach (var islander in SevenCharacterCatalog.Actors)
                    if (islander.id != "z") Require(earlyPages.Contains(islander.displayName), "Missing early memory of " + islander.displayName);
                for(int i=0;i<pages.Length;i++){
                    if(i>0)next.onClick.Invoke();
                    Require(title.text==pages[i].title && body.text==pages[i].text,"Next button did not follow the three-page sequence");
                    Call(ui,"ToggleEndingOriginal");
                    foreach(var memory in reviewed.recollections){
                        bool belongs=memory.day>=pages[i].startDay&&memory.day<=pages[i].endDay;
                        if(!string.IsNullOrWhiteSpace(memory.original))Require(body.text.Contains(GameUI.RenderEndingDisplay(memory.original.Trim(),reviewed.playerName))==belongs,"Source viewer mixed the two periods");
                    }
                    Call(ui,"ToggleEndingOriginal");
                    Canvas.ForceUpdateCanvases();
                    var viewport=ScreenRect(body.GetComponentInParent<ScrollRect>().viewport);
                    var glyphs=VisibleGlyphBounds(body,0);
                    Require(glyphs.yMin>=viewport.yMin-1 && glyphs.yMax<=viewport.yMax+1 && glyphs.xMin>=viewport.xMin-2 && glyphs.xMax<=viewport.xMax+2,"Reviewed recap clipped in the demo window on page "+(i+1));
                    if(i<2){
                        yield return new WaitForSecondsRealtime(.4f);
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(Path.Combine(output,i==0?"recap-early.png":"recap-middle.png"));yield return new WaitForEndOfFrame();
                        yield return new WaitForSecondsRealtime(.25f);
                    }
                }
                Call(ui,"ShowEndingPage",firstPage+2);
                body=Field<TextMeshProUGUI>(ui,"endingBody");
                Require(body.text==pages[2].text && body.text.Split(new[]{"\n\n"},StringSplitOptions.None).Length==2,"Late recap should separate Z's thoughts from the events");
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
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"recap-late-reveal.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(7);
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"recap-late.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(.2f);
                next.onClick.Invoke();
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
                var advance=Find(ui,"Ending black input");Require(advance!=null,"Missing black screen advance");
                bool sawFadeOut=false,sawFadeIn=false;deadline=Time.realtimeSinceStartup+10;
                while((!Field<bool>(ui,"endingBlackDetailsShown")||Field<bool>(ui,"endingBlackTransitioning"))&&Time.realtimeSinceStartup<deadline)
                {
                    float alpha=Field<CanvasGroup>(ui,"endingBlackContentGroup").alpha;
                    if(alpha>.1f&&alpha<.9f){if(Field<bool>(ui,"endingBlackDetailsShown"))sawFadeIn=true;else sawFadeOut=true;}
                    yield return null;
                }
                Require(Field<bool>(ui,"endingBlackDetailsShown")&&!Field<bool>(ui,"endingBlackTransitioning"),"Ending details did not advance automatically");
                Require(sawFadeOut&&sawFadeIn,"Ending changed without a smooth fade-out and fade-in");
                yield return new WaitForEndOfFrame();yield return null;
                var displayed=Field<EndingDemoView.EndingRun>(ui,"seventhNight");
                CheckDetailPage(ui,displayed,false);
                Require(blackWords.text.Contains("字小节")&&!blackWords.text.Contains("旅人"),"Ending uses a different player name");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"legacy-complete.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(.25f);
                // Exercise an unusually long, unreviewed ending in this isolated world.
                var longRun=JsonUtility.FromJson<EndingDemoView.EndingRun>(JsonUtility.ToJson(displayed));
                longRun.decision.choice="stay";longRun.decision.decision="我想留下。";longRun.decision.lastLine="明天我还想再见他们。";
                longRun.outcome.label="决定留下";longRun.outcome.text=string.Concat(System.Linq.Enumerable.Repeat("我想起今天一起说过的话。\n",40));
                typeof(GameUI).GetField("seventhNight",Fields).SetValue(ui,longRun);
                Call(ui,"PrepareDailyPages");Call(ui,"ShowEndingPage",firstPage+pages.Length);
                deadline=Time.realtimeSinceStartup+12;
                while((!Field<bool>(ui,"endingBlackDetailsShown")||Field<bool>(ui,"endingBlackTransitioning"))&&Time.realtimeSinceStartup<deadline)yield return null;
                Require(Field<bool>(ui,"endingBlackDetailsShown")&&!Field<bool>(ui,"endingBlackTransitioning"),"Long ending did not automatically advance");
                yield return new WaitForEndOfFrame();yield return null;CheckDetailPage(ui,longRun,true);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"long-ending-bottom.png"));yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(.25f);
                Find(ui,"Ending black continue").onClick.Invoke();deadline=Time.realtimeSinceStartup+10;
                while(ui.HasOverlay&&Time.realtimeSinceStartup<deadline)yield return null;
                Require(!ui.HasOverlay&&!session.InputBlocked,"Return button did not restore island controls");
            }
            passed=true;
        }
    }
}
