using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;
using EndingRun = Seabreeze.EndingDemo.EndingDemoView.EndingRun;

namespace Seabreeze
{
    public sealed partial class GameUI
    {
        Live2DCharacterView endingCharacter;
        RectTransform endingColumn;
        TextMeshProUGUI endingTitle,endingBody,endingEyebrow,endingProgress,endingSource;
        Button endingNext,endingPrevious,endingOriginal,endingUpload,endingHistory;
        ScrollRect endingScroll;
        RawImage endingSceneImage;
        Image endingBlackScreen;
        CanvasGroup endingBlackGroup;
        CanvasGroup endingBlackWordsGroup;
        TextMeshProUGUI endingBlackTitle,endingBlackBody;
        RectTransform endingBlackViewport;
        ScrollRect endingBlackScroll;
        Button endingBlackNext;
        EndingRun seventhNight;
        EndingDemo.EndingDemoView.DailyPage[] endingDailyPages = Array.Empty<EndingDemo.EndingDemoView.DailyPage>();
        EndingDemo.EndingDemoView.RecapPage[] endingRecapPages = Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
        EndingDemo.EndingDemoView.Recollection[][] endingRecapMemories = Array.Empty<EndingDemo.EndingDemoView.Recollection[]>();
        string endingClosingText="";
        string endingPlayerName=SevenCharacterCatalog.DefaultPlayerName;
        bool endingUsesOldRainPreset;
        int endingPage,endingDisplayGeneration;
        bool endingGenerating,seventhNightOpening,endingOriginalShown;
        float endingVisible,endingNextRevealAt;
        int endingFullCharacterCount;
        int[] endingRevealStops=Array.Empty<int>();
        int endingRevealIndex;
        Color32[][] endingRevealBaseColors=Array.Empty<Color32[]>();
        Coroutine endingBlackReveal;
        string endingContextRevision="";
        string endingSourceRevision="";
        bool endingSourceChecking,endingUploadOpening,endingHistoryShown;
        float nextEndingSourceCheck;
        EndingRun previousEnding,currentEnding;
        const string OrdinaryPresetFingerprint="63eeebbd998284aaadd8006526689c5da4c50842ec8928d781d038b820d6c07f";
        const string OrdinaryLegacyPresetFingerprint="2fe591641e7ff153625e1493468e0c87a8f74a858957ab1bccda3e49b38346b8";
        const string RainPresetFingerprint="576e221a2ca203a74ed2a8d26f3738820d92af9b9e39d4adef1e1304f196a675";
        const string RainLegacyPresetFingerprint="02ec00798cd87aad71992eedd790079ec070d42cb1175f558ab30d7919dfa43d";

        public void OpenSeventhNight()
        {
            if(!initialized||seventhNightOpening||overlayBusy||overlayMode=="z-ending")return;
            StartCoroutine(OpenSeventhNightWhenReady());
        }
        IEnumerator OpenSeventhNightWhenReady()
        {
            seventhNightOpening=true;var owner=session.State;
            session.Memory?.Conversations?.CancelAll("进入第七晚结局");
            float deadline=Time.unscaledTime+8;
            while(session.State==owner&&(IsTransitioning||(session.Memory?.PlayerChatOpen??false))&&Time.unscaledTime<deadline)yield return null;
            seventhNightOpening=false;
            if(session.State!=owner)yield break;
            if(IsTransitioning||(session.Memory?.PlayerChatOpen??false)){ShowToast("先让眼前这段交流结束，再进入第七晚。");yield break;}
            OpenOverlay("z-ending");
        }
        RectTransform EndingRegion(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var rect=Rect(name,parent,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
            rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;return rect;
        }
        void BuildSeventhNight()
        {
            endingDisplayGeneration++;endingPage=0;endingGenerating=false;endingOriginalShown=false;seventhNight=null;
            endingSourceChecking=endingUploadOpening=endingHistoryShown=false;endingContextRevision=endingSourceRevision="";previousEnding=currentEnding=null;nextEndingSourceCheck=0;
            var background=Stretch("Moon island ending coast",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
            background.texture=Resources.Load<Texture2D>("Art/lighthouse");background.color=new Color(.26f,.29f,.38f);background.raycastTarget=false;
            var veil=Stretch("Seventh night veil",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();veil.color=new Color(.025f,.04f,.08f,.60f);veil.raycastTarget=true;
            var portrait=EndingRegion("Z ending portrait",overlayRoot,new Vector2(.035f,.06f),new Vector2(.455f,.90f));
            endingCharacter=portrait.gameObject.AddComponent<Live2DCharacterView>();endingCharacter.Initialize(portrait);
            var definition=SevenCharacterCatalog.Find("z");
            var profile=Resources.Load<Live2DModelProfile>(definition?.portrait??"Memory/Portraits/ZFinal-v03");
            if(profile!=null)endingCharacter.Show(profile);
            // Keep the Live2D model initialized for saved-game compatibility. The
            // recollection layout uses a quiet scene image on the right instead.
            portrait.gameObject.SetActive(false);
            var imageRegion=EndingRegion("Ending scene image",overlayRoot,Vector2.zero,Vector2.one);
            endingSceneImage=imageRegion.gameObject.AddComponent<RawImage>();
            endingSceneImage.texture=Resources.Load<Texture2D>("Art/lighthouse");
            endingSceneImage.color=new Color(.38f,.41f,.49f,.88f);endingSceneImage.raycastTarget=false;
            var imageAspect=imageRegion.gameObject.AddComponent<AspectRatioFitter>();imageAspect.aspectRatio=16f/9f;imageAspect.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
            endingColumn=EndingRegion("Seventh night story",overlayRoot,new Vector2(.065f,.10f),new Vector2(.60f,.90f));
            endingEyebrow=Label("Seventh night chapter",endingColumn,TL,TL,Vector2.zero,new Vector2(840,35),"第七天 · 夜晚 · 月亮岛码头",18,Accent);
            endingTitle=Label("Z ending title",endingColumn,TL,TL,new Vector2(0,-68),new Vector2(845,110),"这些日子，仍然在这里",43,Paper);endingTitle.richText=false;
            endingSource=Label("Game memory source",endingColumn,TL,TL,new Vector2(0,-174),new Vector2(850,45),"正在同步本局中 Z 已知的经历",17,Muted);endingSource.richText=false;
            endingSource.gameObject.SetActive(false);
            var viewport=Stretch("Ending scroll",endingColumn,new Vector2(0,180),new Vector2(0,-238));viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();endingScroll=scroll;scroll.horizontal=false;scroll.scrollSensitivity=38;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.viewport=viewport;
            var textRect=Rect("Ending words",viewport,TL,TL,Vector2.zero,new Vector2(820,300));textRect.anchorMax=Vector2.one;textRect.sizeDelta=new Vector2(-16,300);
            endingBody=textRect.gameObject.AddComponent<TextMeshProUGUI>();endingBody.font=UIPrimitives.Font;endingBody.fontSize=27;endingBody.color=Paper;endingBody.richText=false;endingBody.lineSpacing=10;
            endingBody.textWrappingMode=TextWrappingModes.Normal;
            scroll.content=textRect;
            endingProgress=Label("Ending page progress",endingColumn,BL,BL,new Vector2(0,105),new Vector2(830,35),"",18,Muted);
            endingPrevious=Button("Previous ending memory",endingColumn,BL,BL,new Vector2(0,35),new Vector2(185,55),"← 上一段",()=>ShowEndingPage(endingPage-1),false,20);
            endingOriginal=Button("Ending memory source",endingColumn,BL,BL,new Vector2(210,35),new Vector2(275,55),"看这一段原文",ToggleEndingOriginal,false,20);
            endingNext=Button("Continue ending",endingColumn,BL,BL,new Vector2(520,35),new Vector2(290,55),"继续 →",NextEndingPage,true,21);
            Button("Return to island",overlayRoot,new Vector2(1,1),new Vector2(1,1),new Vector2(-45,-30),new Vector2(250,48),"返回小岛 · Esc",CloseOverlay,false,18);
            Button("Reconsider current island memories",overlayRoot,TL,TL,new Vector2(45,-30),new Vector2(345,48),"以本局经历重新演出",()=>{if(!endingGenerating){seventhNight=null;session.State.memory.seventhNightEndingRequestId="";GenerateSeventhNight();}},false,18);
            endingUpload=Button("Upload ending memories",overlayRoot,TL,TL,new Vector2(410,-30),new Vector2(285,48),"上传 / 更换记忆文件 ↗",OpenEndingMemoryUpload,false,18);
            endingHistory=Button("Previous ending version",overlayRoot,TL,TL,new Vector2(715,-30),new Vector2(235,48),"看上次结局",ToggleEndingHistory,false,18);endingHistory.interactable=false;
            BuildBlackOutcome();
            string saved=session.State.memory?.seventhNightEndingJson;
            if(!string.IsNullOrEmpty(saved))try{var cached=JsonConvert.DeserializeObject<EndingRun>(saved);if(cached?.worldId==session.State.memory.worldId){seventhNight=currentEnding=cached;PrepareDailyPages();var json=Newtonsoft.Json.Linq.JObject.Parse(saved);endingContextRevision=(string)json["contextRevision"]??"";endingSourceRevision=(string)json["sourceRevision"]??"";}}catch{ }
            if(seventhNight!=null)ShowEndingPage(0);else GenerateSeventhNight();
        }
        void BuildBlackOutcome()
        {
            endingBlackScreen=Stretch("Ending black screen",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();
            endingBlackScreen.color=Color.black;endingBlackScreen.raycastTarget=false;
            var blackCanvas=endingBlackScreen.gameObject.AddComponent<Canvas>();blackCanvas.overrideSorting=true;blackCanvas.sortingOrder=1000;
            endingBlackScreen.gameObject.AddComponent<GraphicRaycaster>();
            endingBlackGroup=endingBlackScreen.gameObject.AddComponent<CanvasGroup>();endingBlackGroup.alpha=1;
            var backdrop=Stretch("Solid black",endingBlackScreen.transform,Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
            backdrop.texture=Texture2D.blackTexture;backdrop.color=Color.white;backdrop.raycastTarget=false;
            var inputImage=Stretch("Ending black input",endingBlackScreen.transform,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();inputImage.color=Color.clear;inputImage.raycastTarget=true;
            var advance=inputImage.gameObject.AddComponent<Button>();advance.transition=Selectable.Transition.None;advance.targetGraphic=inputImage;advance.navigation=new Navigation{mode=Navigation.Mode.None};advance.onClick.AddListener(NextEndingPage);
            endingBlackTitle=Label("Ending black title",endingBlackScreen.transform,TC,TC,new Vector2(0,-110),new Vector2(1120,52),"",22,Muted,TextAlignmentOptions.Center);
            endingBlackViewport=Stretch("Ending black words viewport",endingBlackScreen.transform,Vector2.zero,Vector2.zero);var viewportGraphic=endingBlackViewport.gameObject.AddComponent<Image>();viewportGraphic.color=Color.clear;viewportGraphic.raycastTarget=true;endingBlackViewport.gameObject.AddComponent<RectMask2D>();
            endingBlackScroll=endingBlackViewport.gameObject.AddComponent<ScrollRect>();endingBlackScroll.horizontal=false;endingBlackScroll.vertical=true;endingBlackScroll.scrollSensitivity=38;endingBlackScroll.movementType=ScrollRect.MovementType.Clamped;endingBlackScroll.viewport=endingBlackViewport;
            var blackBodyRect=Rect("Ending black words",endingBlackViewport,TL,TL,Vector2.zero,new Vector2(0,120));blackBodyRect.anchorMax=new Vector2(1,1);blackBodyRect.pivot=TL;
            endingBlackBody=blackBodyRect.gameObject.AddComponent<TextMeshProUGUI>();endingBlackBody.font=UIPrimitives.Font;endingBlackBody.text="";endingBlackBody.fontSize=34;endingBlackBody.color=Paper;endingBlackBody.richText=false;endingBlackBody.raycastTarget=false;endingBlackBody.textWrappingMode=TextWrappingModes.Normal;endingBlackBody.overflowMode=TextOverflowModes.Overflow;endingBlackBody.alignment=TextAlignmentOptions.Center;
            endingBlackBody.lineSpacing=14;endingBlackScroll.content=blackBodyRect;
            endingBlackWordsGroup=endingBlackBody.gameObject.AddComponent<CanvasGroup>();endingBlackWordsGroup.alpha=0;
            endingBlackNext=Button("Ending black continue",endingBlackScreen.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,62),new Vector2(300,54),"回到小岛 →",CloseOverlay,true,20);
            SetBlackFirstLineLayout();
            endingBlackScreen.gameObject.SetActive(false);
        }
        void GenerateSeventhNight()
        {
            if(session.Memory?.Client==null){EndingFailure("当前存档的记忆系统尚未就绪。返回小岛后重试。");return;}
            endingGenerating=true;endingTitle.text="Z 正在回望这些日子";SetEndingWords("你们说过的话、一起做过的事，会和第七晚的剧情一起交给她。\n她会自己作出选择，再逐段想起。",false);
            endingPrevious.interactable=endingOriginal.interactable=endingNext.interactable=false;
            int generation=endingDisplayGeneration;var owner=session.State;
            session.Memory.Client.Focus("z");
            session.Memory.Client.GenerateEnding("model",reply=>{
                if(generation!=endingDisplayGeneration||session.State!=owner||overlayMode!="z-ending")return;
                seventhNight=currentEnding=reply.ending.ToObject<EndingRun>();
                PrepareDailyPages();
                endingContextRevision=(string)reply.ending["contextRevision"]??endingContextRevision;endingSourceRevision=(string)reply.ending["sourceRevision"]??endingSourceRevision;endingGenerating=false;endingHistoryShown=false;ShowEndingPage(0);
            },error=>{if(generation==endingDisplayGeneration&&session.State==owner&&overlayMode=="z-ending")EndingFailure(error);});
        }
        void OpenEndingMemoryUpload()
        {
            if(endingUploadOpening||session.Memory?.Client==null)return;
            endingUploadOpening=true;endingUpload.interactable=false;var owner=session.State;int generation=endingDisplayGeneration;
            session.Memory.Client.OpenEndingUpload(url=>{
                if(session.State!=owner||overlayMode!="z-ending"||generation!=endingDisplayGeneration)return;
                endingUploadOpening=false;endingUpload.interactable=true;Application.OpenURL(url);
                endingSource.text="在上传页确认后，这里会自动使用新记忆重新演出。";
            },error=>{if(session.State==owner&&overlayMode=="z-ending"&&generation==endingDisplayGeneration){endingUploadOpening=false;endingUpload.interactable=true;ShowToast(error);}});
        }
        void RefreshEndingSource()
        {
            if(endingSourceChecking||session.Memory?.Client==null)return;
            endingSourceChecking=true;nextEndingSourceCheck=Time.unscaledTime+2;var owner=session.State;int generation=endingDisplayGeneration;
            session.Memory.Client.ReadEndingSource(reply=>{
                if(session.State!=owner||overlayMode!="z-ending"||generation!=endingDisplayGeneration)return;
                endingSourceChecking=false;
                var source=reply.endingSource;if(source==null)return;
                string revision=(string)source["contextRevision"]??"",sourceRevision=(string)source["sourceRevision"]??"",phase=(string)source["phase"]??"idle";
                // Older builds saved endings without a context revision. Never
                // adopt today's revision as the baseline for an older snapshot.
                bool legacySnapshot=string.IsNullOrEmpty(endingSourceRevision)&&currentEnding!=null;
                bool changed=!string.IsNullOrEmpty(sourceRevision)&&(legacySnapshot||(!string.IsNullOrEmpty(endingSourceRevision)&&sourceRevision!=endingSourceRevision));
                endingSourceRevision=sourceRevision;
                if(string.IsNullOrEmpty(endingContextRevision))endingContextRevision=revision;
                if(changed){endingDisplayGeneration++;endingContextRevision=revision;endingHistoryShown=false;endingUploadOpening=false;endingUpload.interactable=true;previousEnding=currentEnding;currentEnding=seventhNight=null;session.State.memory.seventhNightEndingRequestId="";endingGenerating=true;endingTitle.text="Z 正在回望新的经历";SetEndingWords("已收到新记忆。她会结合你们之后真实发生的经历，再作出自己的选择。",false);endingPrevious.interactable=endingOriginal.interactable=endingNext.interactable=false;}
                if(reply.endingPrevious!=null)previousEnding=reply.endingPrevious.ToObject<EndingRun>();
                if(reply.ending!=null&&!endingHistoryShown&&(seventhNight==null||seventhNight.id!=(string)reply.ending["id"])){
                    seventhNight=currentEnding=reply.ending.ToObject<EndingRun>();PrepareDailyPages();endingGenerating=false;ShowEndingPage(0);
                }else if(changed&&(phase=="idle"||phase=="outdated"))GenerateSeventhNight();
                if(endingGenerating){
                    endingSource.text=phase=="recalling"?$"{(int?)source["completed"]??0} / {(int?)source["memoryCount"]??0} 段经历已整理":phase=="deciding"?"正在结合全部知情经历，形成她自己的答案":"正在同步最新记忆";
                    if(phase=="failed")EndingFailure((string)source["error"]??"经历已变化，请按最新记忆重试。");
                }else if(!endingHistoryShown)endingSource.text=$"{(string)source["sourceName"]} · 已保存"+((bool?)source["newExperiencesAvailable"]==true?" · 本局有新变化，可重新演出":"");
                endingHistory.interactable=previousEnding!=null&&currentEnding!=null&&!endingGenerating;
                endingHistory.GetComponentInChildren<TextMeshProUGUI>().text=endingHistoryShown?"返回当前结局":"看上次结局";
            },_=>{if(session.State==owner&&overlayMode=="z-ending"&&generation==endingDisplayGeneration)endingSourceChecking=false;});
        }
        void ToggleEndingHistory()
        {
            if(endingGenerating||previousEnding==null||currentEnding==null)return;
            endingHistoryShown=!endingHistoryShown;seventhNight=endingHistoryShown?previousEnding:currentEnding;PrepareDailyPages();ShowEndingPage(0);
            endingHistory.GetComponentInChildren<TextMeshProUGUI>().text=endingHistoryShown?"返回当前结局":"看上次结局";
            if(endingHistoryShown)endingSource.text="上次结局的记忆快照 · 当前上传文件和游戏进度不会改变";
        }
        [Serializable] class ApprovedEnding
        {
            public string fingerprint,closingText;
        }
        public static string RenderEndingPlayerName(string value)
        {
            var name=value?.Trim();
            return string.IsNullOrEmpty(name)||name=="旅人"||name=="玩家"?SevenCharacterCatalog.DefaultPlayerName:name;
        }
        public static string RenderEndingDisplay(string value)
        {
            return RenderEndingDisplay(value,SevenCharacterCatalog.DefaultPlayerName);
        }
        public static string RenderEndingDisplay(string value,string playerName)
        {
            if(string.IsNullOrEmpty(value))return value??"";
            var displayName=RenderEndingPlayerName(playerName);
            var pattern=System.Text.RegularExpressions.Regex.Escape(displayName)+"|旅人|玩家|字小节";
            return System.Text.RegularExpressions.Regex.Replace(value,pattern,_=>displayName);
        }
        static bool OldRainPreset(EndingRun run)
        {
            if(run?.playerName?.Trim()!="听雨"||run.recollections==null)return false;
            var authored=Array.FindAll(run.recollections,m=>m?.memoryId!=null&&m.memoryId.StartsWith("rain_",StringComparison.Ordinal));
            if(authored.Length!=26)return false;
            var fingerprints=RecollectionFingerprints(authored,SevenCharacterCatalog.DefaultPlayerName);
            foreach(var fingerprint in fingerprints)
                if(fingerprint==RainLegacyPresetFingerprint||fingerprint==RainPresetFingerprint)return true;
            return false;
        }
        string EndingWordsForPlayer(string raw)
        {
            if(endingUsesOldRainPreset&&raw!=null)raw=raw.Replace("听雨",SevenCharacterCatalog.DefaultPlayerName);
            return RenderEndingDisplay(raw,endingPlayerName);
        }
        void PrepareDailyPages()
        {
            endingClosingText="";
            endingUsesOldRainPreset=OldRainPreset(seventhNight);
            endingPlayerName=endingUsesOldRainPreset?SevenCharacterCatalog.DefaultPlayerName:RenderEndingPlayerName(seventhNight?.playerName);
            endingDailyPages=seventhNight?.dailyPages??Array.Empty<EndingDemo.EndingDemoView.DailyPage>();
            if(endingDailyPages.Length==0)endingDailyPages=BuildDailyPagesFromRecollections(seventhNight?.recollections);
            endingDailyPages=SortDailyPages(endingDailyPages);
            endingRecapPages=seventhNight?.recapPages??Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
            endingRecapPages=SortRecapPages(endingRecapPages);
            // A saved run may still carry the old 1–3 / 4–7 snapshot. Rebuild
            // from its actual daily pages instead of showing a made-up range.
            if(!IsCurrentRecapShape(endingRecapPages,endingDailyPages))endingRecapPages=BuildRecapPagesFromDaily(endingDailyPages);
            endingRecapMemories=GroupMemoriesByRecap(seventhNight?.recollections,endingRecapPages);
            string[] fingerprints=RecollectionFingerprints(seventhNight?.recollections,endingPlayerName);
            var approvedRecaps=EndingDemo.EndingRecapText.Approved(fingerprints);
            if(IsCurrentRecapShape(approvedRecaps,endingDailyPages))
            {
                endingRecapPages=SortRecapPages(approvedRecaps);
                endingRecapMemories=GroupMemoriesByRecap(seventhNight?.recollections,endingRecapPages);
            }
            else
            {
                // A cached two-page run may contain a few real game_ memories.
                // Reuse only the reviewed early ranges after hashing every other
                // original in order; leave the 4–7 page generated from this run.
                var approvedEarly=ApprovedEarlyRecapsForLiveRun(seventhNight,endingPlayerName);
                if(approvedEarly.Length>0)
                {
                    endingRecapPages=MergeApprovedEarlyRecaps(endingRecapPages,approvedEarly,endingDailyPages);
                    endingRecapMemories=GroupMemoriesByRecap(seventhNight?.recollections,endingRecapPages);
                }
            }
            var approved=Resources.Load<TextAsset>("Dialogue/Ending/approved");
            if(approved==null||seventhNight?.decision==null||seventhNight.outcome==null)return;
            try
            {
                var saved=JsonConvert.DeserializeObject<ApprovedEnding>(approved.text);
                if(saved==null)return;
                if(seventhNight.decision.choice=="leave"&&seventhNight.outcome.status=="pending"&&Array.IndexOf(fingerprints,saved.fingerprint)>=0)
                    endingClosingText=saved.closingText??"";
            }
            catch(Exception e){Debug.LogWarning("Approved ending resource could not be read: "+e.Message);}
        }
        static string[] RecollectionFingerprints(EndingDemo.EndingDemoView.Recollection[] memories,string playerName)
        {
            var fingerprints=new List<string>();
            AddFingerprint(fingerprints,RawRecollectionFingerprint(memories));
            if(RenderEndingPlayerName(playerName)==SevenCharacterCatalog.DefaultPlayerName)
            {
                // The service accepts both the current display name and the old
                // traveller token. Keep both hashes without changing the save.
                AddFingerprint(fingerprints,RecollectionFingerprint(memories));
                AddFingerprint(fingerprints,RecollectionFingerprint(memories,"旅人",SevenCharacterCatalog.DefaultPlayerName));
            }
            return fingerprints.ToArray();
        }
        static void AddFingerprint(List<string> fingerprints,string fingerprint)
        {
            if(!string.IsNullOrEmpty(fingerprint)&&!fingerprints.Contains(fingerprint))fingerprints.Add(fingerprint);
        }
        static bool IsCurrentRecapShape(EndingDemo.EndingDemoView.RecapPage[] pages,EndingDemo.EndingDemoView.DailyPage[] dailyPages)
        {
            if(pages==null||pages.Length==0||dailyPages==null||dailyPages.Length==0)return false;
            int[,] ranges={{1,1},{2,3},{4,7}};int expected=0;
            for(int i=0;i<ranges.GetLength(0);i++)if(HasDailyInRange(dailyPages,ranges[i,0],ranges[i,1]))expected++;
            if(pages.Length!=expected)return false;
            int index=0;
            for(int i=0;i<ranges.GetLength(0);i++)
            {
                if(!HasDailyInRange(dailyPages,ranges[i,0],ranges[i,1]))continue;
                var page=pages[index++];
                if(page==null||page.startDay!=ranges[i,0]||page.endDay!=ranges[i,1]||string.IsNullOrWhiteSpace(page.title)||string.IsNullOrWhiteSpace(page.text))return false;
            }
            return index==pages.Length;
        }
        static bool HasDailyInRange(EndingDemo.EndingDemoView.DailyPage[] dailyPages,int startDay,int endDay)
        {
            foreach(var daily in dailyPages)if(daily!=null&&daily.day>=startDay&&daily.day<=endDay)return true;
            return false;
        }
        static EndingDemo.EndingDemoView.RecapPage[] ApprovedEarlyRecapsForLiveRun(EndingRun run,string playerName)
        {
            if(run?.recollections==null||run.recollections.Length==0||string.IsNullOrEmpty(run.sourceName))return Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
            if(run.sourceName!="七日完整模拟-对话与行动.md"&&run.sourceName!="七日随机记忆-雨停以后-20261113.md")return Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
            var baseMemories=new List<EndingDemo.EndingDemoView.Recollection>();bool hasLive=false;
            foreach(var memory in run.recollections)
            {
                if(IsLiveRecapMemory(memory)){hasLive=true;continue;}
                if(memory!=null)baseMemories.Add(memory);
            }
            if(!hasLive||baseMemories.Count==0)return Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
            var fingerprints=RecollectionFingerprints(baseMemories.ToArray(),playerName);
            bool ordinary=run.sourceName=="七日完整模拟-对话与行动.md";
            bool rain=run.sourceName=="七日随机记忆-雨停以后-20261113.md";
            bool known=false;
            foreach(var fingerprint in fingerprints)
            {
                if(ordinary&&(fingerprint==OrdinaryLegacyPresetFingerprint||fingerprint==OrdinaryPresetFingerprint))known=true;
                if(rain&&(fingerprint==RainLegacyPresetFingerprint||fingerprint==RainPresetFingerprint))known=true;
            }
            return known?EndingDemo.EndingRecapText.Approved(fingerprints):Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
        }
        static bool IsLiveRecapMemory(EndingDemo.EndingDemoView.Recollection memory)
        {
            return memory?.memoryId!=null&&memory.memoryId.StartsWith("game_",StringComparison.Ordinal);
        }
        static EndingDemo.EndingDemoView.RecapPage[] MergeApprovedEarlyRecaps(EndingDemo.EndingDemoView.RecapPage[] generated,EndingDemo.EndingDemoView.RecapPage[] approved,EndingDemo.EndingDemoView.DailyPage[] dailyPages)
        {
            var merged=new List<EndingDemo.EndingDemoView.RecapPage>(generated??Array.Empty<EndingDemo.EndingDemoView.RecapPage>());
            for(int i=0;i<2;i++)
            {
                var reviewed=Array.Find(approved,page=>page!=null&&page.startDay==(i==0?1:2)&&page.endDay==(i==0?1:3));
                if(reviewed==null||!HasDailyInRange(dailyPages,reviewed.startDay,reviewed.endDay))continue;
                int index=merged.FindIndex(page=>page!=null&&page.startDay==reviewed.startDay&&page.endDay==reviewed.endDay);
                if(index>=0)merged[index]=reviewed;else merged.Add(reviewed);
            }
            merged.Sort((a,b)=>(a?.startDay??0).CompareTo(b?.startDay??0));
            return merged.ToArray();
        }
        static EndingDemo.EndingDemoView.DailyPage[] BuildDailyPagesFromRecollections(EndingDemo.EndingDemoView.Recollection[] memories)
        {
            if(memories==null||memories.Length==0)return Array.Empty<EndingDemo.EndingDemoView.DailyPage>();
            var byDay=new Dictionary<int,List<EndingDemo.EndingDemoView.Recollection>>();
            foreach(var memory in memories)
            {
                if(memory==null||memory.day<1||memory.day>7)continue;
                List<EndingDemo.EndingDemoView.Recollection> group;
                if(!byDay.TryGetValue(memory.day,out group)){group=new List<EndingDemo.EndingDemoView.Recollection>();byDay.Add(memory.day,group);}
                group.Add(memory);
            }
            var days=new List<int>(byDay.Keys);days.Sort();var pages=new List<EndingDemo.EndingDemoView.DailyPage>();
            foreach(var day in days)
            {
                var group=byDay[day];var facts=new List<string>();var feelings=new List<string>();string title="";
                foreach(var memory in group)
                {
                    if(string.IsNullOrWhiteSpace(title)&&!string.IsNullOrWhiteSpace(memory.title))title=memory.title.Trim();
                    var sentences=SplitLegacySentences(memory.text);if(sentences.Count==0)continue;
                    string fact="";for(int i=0;i<sentences.Count;i++)if(!LooksLikeLegacyFeeling(sentences[i])){fact=sentences[i];break;}
                    if(string.IsNullOrEmpty(fact))fact=sentences[0];
                    if(!facts.Contains(fact))facts.Add(fact);
                    for(int i=sentences.Count-1;i>=0;i--)
                    {
                        var feeling=sentences[i];
                        if(feeling!=fact&&LooksLikeLegacyFeeling(feeling)){if(!feelings.Contains(feeling))feelings.Add(feeling);break;}
                    }
                    if(facts.Count>=2)break;
                }
                if(string.IsNullOrEmpty(title))title="第 "+day+" 天";
                if(facts.Count==0)facts.Add("这一天没有留下可回看的文字。");
                if(feelings.Count==0)feelings.Add("当时是什么心情，我现在说不准了。");
                pages.Add(new EndingDemo.EndingDemoView.DailyPage{day=day,title=title,text=string.Join("\n",facts)+"\n\n"+string.Join("\n",feelings)});
            }
            return pages.ToArray();
        }
        static List<string> SplitLegacySentences(string value)
        {
            var result=new List<string>();if(string.IsNullOrWhiteSpace(value))return result;
            var current=new StringBuilder();
            foreach(var character in value.Trim())
            {
                if(character=='\r')continue;
                if(character=='\n'){AddLegacySentence(result,current);continue;}
                current.Append(character);
                if("。！？!?；;".IndexOf(character)>=0)AddLegacySentence(result,current);
            }
            AddLegacySentence(result,current);return result;
        }
        static void AddLegacySentence(List<string> result,StringBuilder current)
        {
            var sentence=current.ToString().Trim();current.Length=0;if(!string.IsNullOrEmpty(sentence))result.Add(sentence);
        }
        static bool LooksLikeLegacyFeeling(string sentence)
        {
            string[] cues={"我想","我愿意","我觉得","我感觉","我希望","我不","觉得","感觉","高兴","难过","害怕","担心","舍不得","不想","愿意","希望","后悔","生气","安心","紧张","遗憾","庆幸","想起","忘了","忘记","不知道","没想好","想要","喜欢","讨厌","在意","难受","放心"};
            foreach(var cue in cues)if(sentence.IndexOf(cue,StringComparison.Ordinal)>=0)return true;
            return false;
        }
        static EndingDemo.EndingDemoView.RecapPage[] BuildRecapPagesFromDaily(EndingDemo.EndingDemoView.DailyPage[] dailyPages)
        {
            if(dailyPages==null||dailyPages.Length==0)return Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
            var pages=new List<EndingDemo.EndingDemoView.RecapPage>();
            AddRecapRange(pages,dailyPages,1,1,4);
            AddRecapRange(pages,dailyPages,2,3,4);
            AddRecapRange(pages,dailyPages,4,7,4);
            return pages.ToArray();
        }
        static void AddRecapRange(List<EndingDemo.EndingDemoView.RecapPage> pages,EndingDemo.EndingDemoView.DailyPage[] dailyPages,int startDay,int endDay,int maxFacts)
        {
            var facts=new List<string>();var feelingCandidates=new List<string>();var seenDays=new HashSet<int>();bool found=false;string title="";
            foreach(var daily in dailyPages)
            {
                if(daily==null||daily.day<startDay||daily.day>endDay||!seenDays.Add(daily.day))continue;found=true;
                if(string.IsNullOrWhiteSpace(title)&&!string.IsNullOrWhiteSpace(daily.title))title=daily.title.Trim();
                string value=daily.text??"";int split=value.IndexOf("\n\n",StringComparison.Ordinal);
                string factText=split<0?value:value.Substring(0,split);string feelingText=split<0?"":value.Substring(split+2);
                foreach(var fact in RecapLines(factText))
                {
                    if(!facts.Contains(fact))facts.Add(fact);
                    if(facts.Count>=maxFacts)break;
                }
                foreach(var feeling in RecapLines(feelingText))if(!feelingCandidates.Contains(feeling))feelingCandidates.Add(feeling);
                if(facts.Count>=maxFacts)break;
            }
            if(!found)return;
            if(facts.Count==0)facts.Add("这一天没有留下可回看的事实。");
            var feelings=new List<string>();if(feelingCandidates.Count>0)feelings.Add(feelingCandidates[0]);if(feelingCandidates.Count>1&&feelingCandidates[feelingCandidates.Count-1]!=feelings[0])feelings.Add(feelingCandidates[feelingCandidates.Count-1]);
            if(feelings.Count==0)feelings.Add("当时是什么心情，我现在说不准了。");
            if(string.IsNullOrEmpty(title))title=startDay==endDay?"第 "+startDay+" 天":"第 "+startDay+"–"+endDay+" 天";
            var text=string.Join("\n",facts.ToArray())+"\n\n"+string.Join("\n",feelings.ToArray());
            pages.Add(new EndingDemo.EndingDemoView.RecapPage{startDay=startDay,endDay=endDay,title=title,text=text});
        }
        static string FirstRecapLine(string value)
        {
            foreach(var line in RecapLines(value))return line;
            return "";
        }
        static List<string> RecapLines(string value)
        {
            var lines=new List<string>();if(string.IsNullOrWhiteSpace(value))return lines;
            foreach(var line in value.Replace("\r","").Split('\n')){var trimmed=line.Trim();if(!string.IsNullOrEmpty(trimmed)&&!lines.Contains(trimmed))lines.Add(trimmed);}
            return lines;
        }
        static EndingDemo.EndingDemoView.RecapPage[] SortRecapPages(EndingDemo.EndingDemoView.RecapPage[] pages)
        {
            if(pages==null||pages.Length<2)return pages??Array.Empty<EndingDemo.EndingDemoView.RecapPage>();
            var sorted=new List<EndingDemo.EndingDemoView.RecapPage>(pages);sorted.Sort((a,b)=>(a?.startDay??0).CompareTo(b?.startDay??0));return sorted.ToArray();
        }
        static EndingDemo.EndingDemoView.DailyPage[] SortDailyPages(EndingDemo.EndingDemoView.DailyPage[] pages)
        {
            if(pages==null||pages.Length<2)return pages??Array.Empty<EndingDemo.EndingDemoView.DailyPage>();
            var sorted=new List<EndingDemo.EndingDemoView.DailyPage>(pages);sorted.Sort((a,b)=>(a?.day??0).CompareTo(b?.day??0));return sorted.ToArray();
        }
        static EndingDemo.EndingDemoView.Recollection[][] GroupMemoriesByRecap(EndingDemo.EndingDemoView.Recollection[] memories,EndingDemo.EndingDemoView.RecapPage[] pages)
        {
            var groups=new EndingDemo.EndingDemoView.Recollection[pages?.Length??0][];
            for(int i=0;i<groups.Length;i++)
            {
                var list=new List<EndingDemo.EndingDemoView.Recollection>();int start=pages[i]?.startDay??0,end=pages[i]?.endDay??start;
                if(memories!=null)foreach(var memory in memories)if(memory!=null&&memory.day>=start&&memory.day<=end)list.Add(memory);
                groups[i]=list.ToArray();
            }
            return groups;
        }
        static string RecollectionFingerprint(EndingDemo.EndingDemoView.Recollection[] memories)
        {
            return RecollectionFingerprint(memories,"字小节","旅人");
        }
        static string RawRecollectionFingerprint(EndingDemo.EndingDemoView.Recollection[] memories)
        {
            return RecollectionFingerprint(memories,null,null);
        }
        static string RecollectionFingerprint(EndingDemo.EndingDemoView.Recollection[] memories,string from,string to)
        {
            if(memories==null)return "";
            var pieces=new string[memories.Length];
            for(int i=0;i<memories.Length;i++)
            {
                var original=memories[i]?.original??"";
                if(!string.IsNullOrEmpty(from))original=original.Replace(from,to??"");
                pieces[i]=(memories[i]?.day??0).ToString()+"\n"+original;
            }
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n\u001e\n",pieces)))).Replace("-","").ToLowerInvariant();
        }
        static string EndingDayRangeLabel(EndingDemo.EndingDemoView.RecapPage page)
        {
            if(page==null)return "";
            return page.startDay==page.endDay?"第 "+page.startDay+" 天":"第 "+page.startDay+"–"+page.endDay+" 天";
        }
        void EndingFailure(string error)
        {
            endingGenerating=false;endingTitle.text="让这个夜晚，等一等";SetEndingWords(error,false);
            endingNext.interactable=true;endingNext.GetComponentInChildren<TextMeshProUGUI>().text="重新连接并重试";endingPrevious.interactable=endingOriginal.interactable=false;
        }
        void ShowEndingPage(int page)
        {
            if(seventhNight?.decision==null)return;
            if(endingRecapPages==null||endingRecapPages.Length==0)PrepareDailyPages();
            int recapCount=endingRecapPages.Length;
            endingPage=Mathf.Clamp(page,0,recapCount);endingOriginalShown=false;
            var recap=endingPage<recapCount?endingRecapPages[endingPage]:null;
            if(endingPage<recapCount){
                ExitBlackOutcome();endingTitle.text=EndingWordsForPlayer(recap.title);endingBody.text=EndingWordsForPlayer(recap.text);endingEyebrow.text=EndingDayRangeLabel(recap);UpdateEndingScene(recap.title,recap.text);
            }
            else{ShowBlackOutcome();return;}
            SetEndingWords(endingBody.text,true);
            endingCharacter?.SetState("sad",true);
            endingProgress.text=$"{endingPage+1} / {recapCount}  ·  {EndingDayRangeLabel(recap)}";
            endingPrevious.interactable=endingPage>0;endingOriginal.interactable=endingRecapMemories.Length>endingPage&&endingRecapMemories[endingPage].Length>0;endingNext.interactable=true;
            endingNext.GetComponentInChildren<TextMeshProUGUI>().text=endingPage==recapCount-1?"进入结局 →":"下一页 →";
        }
        void UpdateEndingScene(string title,string text)
        {
            if(endingSceneImage==null)return;
            string value=(title??"")+"\n"+(text??"");
            bool radio=value.IndexOf("收音机",StringComparison.Ordinal)>=0||value.IndexOf("李庭",StringComparison.Ordinal)>=0;
            endingSceneImage.texture=Resources.Load<Texture2D>(radio?"Art/ending_radio":"Art/lighthouse")??Resources.Load<Texture2D>("Art/lighthouse");
            endingSceneImage.color=radio?Color.white:new Color(.38f,.41f,.49f,.88f);
        }
        void ShowBlackOutcome()
        {
            if(endingBlackReveal!=null)StopCoroutine(endingBlackReveal);
            endingBlackReveal=StartCoroutine(RevealBlackOutcome());
        }
        IEnumerator RevealBlackOutcome()
        {
            endingBlackRevealed=false;endingBlackDetailsShown=false;endingBlackScreen.gameObject.SetActive(true);endingBlackGroup.alpha=1;
            endingColumn.gameObject.SetActive(false);if(endingSceneImage!=null)endingSceneImage.gameObject.SetActive(false);
            SetBlackFirstLineLayout();
            endingBlackTitle.text="";endingBlackBody.text="";endingBlackWordsGroup.alpha=0;endingBlackNext.gameObject.SetActive(false);
            yield return new WaitForSecondsRealtime(.9f);
            if(endingBlackScreen==null||!endingBlackScreen.gameObject.activeInHierarchy)yield break;
            endingBlackBody.text=FirstEndingSentence();SetBlackFirstLineLayout();
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/.42f){endingBlackWordsGroup.alpha=t;yield return null;}
            endingBlackWordsGroup.alpha=1;endingBlackRevealed=true;
        }
        bool endingBlackRevealed;
        bool endingBlackDetailsShown;
        static string FirstEndingSentence(string value)
        {
            if(string.IsNullOrWhiteSpace(value))return "";
            var line=value.Trim();int newline=line.IndexOf('\n');if(newline>=0)line=line.Substring(0,newline).Trim();
            for(int i=0;i<line.Length;i++)if("。！？!?".IndexOf(line[i])>=0)return line.Substring(0,i+1).Trim();
            return line;
        }
        string FirstEndingSentence()
        {
            var source=!string.IsNullOrWhiteSpace(endingClosingText)?endingClosingText:seventhNight?.decision?.decision;
            if(string.IsNullOrWhiteSpace(source))source=seventhNight?.decision?.lastLine;
            if(string.IsNullOrWhiteSpace(source))source=seventhNight?.outcome?.text;
            return EndingWordsForPlayer(FirstEndingSentence(source));
        }
        string FullEndingText()
        {
            string body=string.IsNullOrWhiteSpace(endingClosingText)?seventhNight?.outcome?.text:endingClosingText;
            if(string.IsNullOrWhiteSpace(body))body=seventhNight?.decision?.decision??"";
            if(!string.IsNullOrWhiteSpace(seventhNight?.decision?.lastLine))body += "\n\n" + seventhNight.decision.lastLine;
            return EndingWordsForPlayer(body);
        }
        void SetBlackFirstLineLayout()
        {
            if(endingBlackViewport==null||endingBlackBody==null)return;
            endingBlackScroll.enabled=false;endingBlackTitle.gameObject.SetActive(false);
            endingBlackViewport.GetComponent<Image>().raycastTarget=false;
            endingBlackViewport.offsetMin=Vector2.zero;endingBlackViewport.offsetMax=Vector2.zero;
            var rect=endingBlackBody.rectTransform;rect.anchorMin=new Vector2(.08f,.5f);rect.anchorMax=new Vector2(.92f,.5f);rect.pivot=C;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(0,160);
        }
        void LayoutBlackOutcomeBody()
        {
            Canvas.ForceUpdateCanvases();
            endingBlackBody.ForceMeshUpdate();
            float width=Mathf.Max(1,endingBlackViewport.rect.width);
            float height=Mathf.Max(endingBlackViewport.rect.height,endingBlackBody.GetPreferredValues(endingBlackBody.text,width,0).y+24);
            endingBlackBody.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
            endingBlackScroll.StopMovement();endingBlackScroll.verticalNormalizedPosition=1;
        }
        void SetBlackDetailsLayout()
        {
            if(endingBlackViewport==null||endingBlackBody==null)return;
            endingBlackTitle.gameObject.SetActive(true);
            endingBlackViewport.GetComponent<Image>().raycastTarget=true;
            endingBlackViewport.offsetMin=new Vector2(100,180);endingBlackViewport.offsetMax=new Vector2(-100,-190);
            var rect=endingBlackBody.rectTransform;rect.anchorMin=TL;rect.anchorMax=new Vector2(1,1);rect.pivot=TL;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(0,120);
            endingBlackScroll.enabled=true;LayoutBlackOutcomeBody();
        }
        void ShowBlackOutcomeDetails()
        {
            if(!endingBlackRevealed||endingBlackDetailsShown)return;
            endingBlackDetailsShown=true;endingBlackTitle.text=EndingWordsForPlayer(seventhNight?.outcome?.label??"");endingBlackBody.text=FullEndingText();SetBlackDetailsLayout();endingBlackWordsGroup.alpha=1;endingBlackNext.gameObject.SetActive(true);
        }
        void ExitBlackOutcome()
        {
            endingBlackRevealed=false;endingBlackDetailsShown=false;
            if(endingBlackReveal!=null){StopCoroutine(endingBlackReveal);endingBlackReveal=null;}
            if(endingBlackScreen!=null)endingBlackScreen.gameObject.SetActive(false);
            if(endingBlackWordsGroup!=null)endingBlackWordsGroup.alpha=0;
            if(endingColumn!=null)endingColumn.gameObject.SetActive(true);
            if(endingSceneImage!=null)endingSceneImage.gameObject.SetActive(true);
        }
        void NextEndingPage()
        {
            if(endingGenerating)return;
            if(endingBlackScreen!=null&&endingBlackScreen.gameObject.activeSelf){if(endingBlackRevealed){if(endingBlackDetailsShown)CloseOverlay();else ShowBlackOutcomeDetails();}return;}
            if(seventhNight==null){GenerateSeventhNight();return;}
            if(endingRevealIndex<endingRevealStops.Length){FinishEndingReveal();endingCharacter?.SetState("serious",false);return;}
            if(endingPage>=endingRecapPages.Length)CloseOverlay();else ShowEndingPage(endingPage+1);
        }
        void SetEndingWords(string value,bool typewriter)
        {
            endingBody.text=value??"";endingBody.maxVisibleCharacters=int.MaxValue;endingBody.ForceMeshUpdate();
            endingFullCharacterCount=Mathf.Max(endingBody.textInfo.characterCount,endingBody.text.Length);
            float width=Mathf.Max(200,((RectTransform)endingBody.transform.parent).rect.width-16);
            float height=Mathf.Max(1,endingBody.GetPreferredValues(endingBody.text,width,0).y+16);
            endingBody.rectTransform.sizeDelta=new Vector2(-16,height);endingBody.rectTransform.anchoredPosition=Vector2.zero;
            endingScroll.StopMovement();endingScroll.verticalNormalizedPosition=1;
            endingBody.ForceMeshUpdate();
            endingRevealStops=BuildRevealStops(endingBody.text,endingFullCharacterCount);endingRevealIndex=typewriter?0:endingRevealStops.Length;
            endingNextRevealAt=Time.unscaledTime+.08f;endingVisible=typewriter?0:endingFullCharacterCount;
            endingRevealBaseColors=CaptureEndingBaseColors();
            ApplyEndingReveal(typewriter?0:1);
        }
        static int[] BuildRevealStops(string value,int characterCount)
        {
            var stops=new List<int>();if(string.IsNullOrEmpty(value)||characterCount<=0)return stops.ToArray();
            for(int i=0;i<value.Length;i++)if(value[i]=='\n')stops.Add(Mathf.Clamp(i+1,1,characterCount));
            if(stops.Count==0||stops[stops.Count-1]<characterCount)stops.Add(characterCount);
            return stops.ToArray();
        }
        Color32[][] CaptureEndingBaseColors()
        {
            var meshes=endingBody.textInfo.meshInfo;var colors=new Color32[meshes.Length][];
            for(int i=0;i<meshes.Length;i++){colors[i]=new Color32[meshes[i].colors32.Length];Array.Copy(meshes[i].colors32,colors[i],colors[i].Length);}
            return colors;
        }
        void ApplyEndingReveal(float currentProgress)
        {
            if(endingBody==null||endingRevealBaseColors.Length==0)return;
            int previousStop=endingRevealIndex<=0?0:endingRevealStops[Mathf.Min(endingRevealIndex-1,endingRevealStops.Length-1)];
            int currentStop=endingRevealIndex>=endingRevealStops.Length?endingFullCharacterCount:endingRevealStops[endingRevealIndex];
            float progress=endingRevealIndex>=endingRevealStops.Length?1:Mathf.Clamp01(currentProgress);
            var info=endingBody.textInfo;
            for(int i=0;i<info.characterCount;i++)
            {
                var character=info.characterInfo[i];if(!character.isVisible)continue;
                float alpha=i<previousStop?1:i>=currentStop?0:progress;
                int material=character.materialReferenceIndex,vertex=character.vertexIndex;
                if(material<0||material>=info.meshInfo.Length||vertex<0||vertex+3>=info.meshInfo[material].colors32.Length)continue;
                var colors=info.meshInfo[material].colors32;var baseColors=endingRevealBaseColors[material];
                for(int v=0;v<4;v++){var color=baseColors[vertex+v];color.a=(byte)Mathf.RoundToInt(color.a*alpha);colors[vertex+v]=color;}
            }
            endingBody.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }
        void FinishEndingReveal()
        {
            endingRevealIndex=endingRevealStops.Length;endingNextRevealAt=0;endingVisible=endingFullCharacterCount;ApplyEndingReveal(1);
        }
        void ToggleEndingOriginal()
        {
            if(seventhNight==null||endingPage<0||endingPage>=endingRecapPages.Length||endingRecapMemories.Length<=endingPage)return;
            endingOriginalShown=!endingOriginalShown;var memories=endingRecapMemories[endingPage];
            var originals=new List<string>();foreach(var memory in memories)if(!string.IsNullOrWhiteSpace(memory.original))originals.Add(memory.original.Trim());
            SetEndingWords(endingOriginalShown?EndingWordsForPlayer(string.Join("\n\n",originals.ToArray())):EndingWordsForPlayer(endingRecapPages[endingPage].text),false);
            endingCharacter?.SetState("serious",false);
        }
        void UpdateSeventhNight()
        {
            if(Time.unscaledTime>=nextEndingSourceCheck)RefreshEndingSource();
            if(endingBody!=null&&!endingGenerating&&endingRevealIndex<endingRevealStops.Length&&Time.unscaledTime>=endingNextRevealAt){
                const float fadeDuration=.58f,pauseDuration=.12f;
                float progress=Mathf.Clamp01((Time.unscaledTime-endingNextRevealAt)/fadeDuration);ApplyEndingReveal(progress);
                if(progress>=1){endingRevealIndex++;endingNextRevealAt=Time.unscaledTime+pauseDuration;ApplyEndingReveal(0);if(endingRevealIndex>=endingRevealStops.Length)endingCharacter?.SetState("serious",false);}
            }
            if(GameInput.Pressed(Key.Escape))CloseOverlay();
            else if(GameInput.Pressed(Key.Enter)||GameInput.Pressed(Key.Space)||GameInput.Pressed(Key.E))NextEndingPage();
        }
        void ReleaseSeventhNight()
        {
            if(endingBlackReveal!=null){StopCoroutine(endingBlackReveal);endingBlackReveal=null;}
            endingDisplayGeneration++;endingGenerating=false;seventhNightOpening=false;
            if(endingCharacter!=null){endingCharacter.Hide();endingCharacter=null;}
            if(overlayMode=="z-ending")session?.Memory?.Client?.Focus(null);
        }
    }
}
