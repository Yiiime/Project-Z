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
        EndingDemo.EndingDemoView.Recollection[][] endingDailyMemories = Array.Empty<EndingDemo.EndingDemoView.Recollection[]>();
        string endingClosingText="";
        int endingPage,endingDisplayGeneration;
        bool endingGenerating,seventhNightOpening,endingOriginalShown;
        float endingVisible,endingNextRevealAt;
        int endingFullCharacterCount;
        int[] endingRevealStops=Array.Empty<int>();
        int endingRevealIndex;
        Color32[][] endingRevealBaseColors=Array.Empty<Color32[]>();
        Coroutine endingBlackReveal;
        string endingContextRevision="";
        bool endingSourceChecking,endingUploadOpening,endingHistoryShown;
        float nextEndingSourceCheck;
        EndingRun previousEnding,currentEnding;

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
            endingSourceChecking=endingUploadOpening=endingHistoryShown=false;endingContextRevision="";previousEnding=currentEnding=null;nextEndingSourceCheck=0;
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
            endingSceneImage.color=new Color(.72f,.76f,.82f,.82f);endingSceneImage.raycastTarget=false;
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
            Button("Reconsider current island memories",overlayRoot,TL,TL,new Vector2(45,-30),new Vector2(345,48),"以本局经历重新演出",()=>{if(!endingGenerating){seventhNight=null;GenerateSeventhNight();}},false,18);
            endingUpload=Button("Upload ending memories",overlayRoot,TL,TL,new Vector2(410,-30),new Vector2(285,48),"上传 / 更换记忆文件 ↗",OpenEndingMemoryUpload,false,18);
            endingHistory=Button("Previous ending version",overlayRoot,TL,TL,new Vector2(715,-30),new Vector2(235,48),"看上次结局",ToggleEndingHistory,false,18);endingHistory.interactable=false;
            BuildBlackOutcome();
            string saved=session.State.memory?.seventhNightEndingJson;
            if(!string.IsNullOrEmpty(saved))try{var cached=JsonConvert.DeserializeObject<EndingRun>(saved);if(cached?.worldId==session.State.memory.worldId){seventhNight=currentEnding=cached;PrepareDailyPages();endingContextRevision=(string)Newtonsoft.Json.Linq.JObject.Parse(saved)["contextRevision"]??"";}}catch{ }
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
            endingBlackBody=blackBodyRect.gameObject.AddComponent<TextMeshProUGUI>();endingBlackBody.font=UIPrimitives.Font;endingBlackBody.text="";endingBlackBody.fontSize=34;endingBlackBody.color=Paper;endingBlackBody.richText=false;endingBlackBody.textWrappingMode=TextWrappingModes.Normal;endingBlackBody.overflowMode=TextOverflowModes.Overflow;endingBlackBody.alignment=TextAlignmentOptions.Center;
            endingBlackBody.lineSpacing=14;endingBlackScroll.content=blackBodyRect;
            endingBlackBody.raycastTarget=false;
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
                endingContextRevision=(string)reply.ending["contextRevision"]??endingContextRevision;endingGenerating=false;endingHistoryShown=false;ShowEndingPage(0);
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
                string revision=(string)source["contextRevision"]??"",phase=(string)source["phase"]??"idle";
                bool changed=!string.IsNullOrEmpty(endingContextRevision)&&revision!=endingContextRevision;
                if(string.IsNullOrEmpty(endingContextRevision))endingContextRevision=revision;
                if(changed){endingDisplayGeneration++;endingContextRevision=revision;endingHistoryShown=false;endingUploadOpening=false;endingUpload.interactable=true;previousEnding=currentEnding;currentEnding=seventhNight=null;session.State.memory.seventhNightEndingRequestId="";endingGenerating=true;endingTitle.text="Z 正在回望新的经历";SetEndingWords("已收到新记忆。她会结合你们之后真实发生的经历，再作出自己的选择。",false);endingPrevious.interactable=endingOriginal.interactable=endingNext.interactable=false;}
                if(reply.endingPrevious!=null)previousEnding=reply.endingPrevious.ToObject<EndingRun>();
                if(reply.ending!=null&&!endingHistoryShown&&(seventhNight==null||seventhNight.id!=(string)reply.ending["id"])){
                    seventhNight=currentEnding=reply.ending.ToObject<EndingRun>();PrepareDailyPages();endingGenerating=false;ShowEndingPage(0);
                }else if(changed&&phase=="idle")GenerateSeventhNight();
                if(endingGenerating){
                    endingSource.text=phase=="recalling"?$"{(int?)source["completed"]??0} / {(int?)source["memoryCount"]??0} 段经历已整理":phase=="deciding"?"正在结合全部知情经历，形成她自己的答案":"正在同步最新记忆";
                    if(phase=="failed")EndingFailure((string)source["error"]??"生成未完成，请重试。");
                }else if(!endingHistoryShown)endingSource.text=$"{(string)source["sourceName"]} · 本局经历会继续补充 · 已保存";
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
            public EndingDemo.EndingDemoView.DailyPage[] dailyPages;
        }
        void PrepareDailyPages()
        {
            endingClosingText="";
            endingDailyPages=seventhNight?.dailyPages??Array.Empty<EndingDemo.EndingDemoView.DailyPage>();
            if(endingDailyPages.Length==0)endingDailyPages=BuildDailyPagesFromRecollections(seventhNight?.recollections);
            endingDailyMemories=GroupMemoriesByDay(seventhNight?.recollections,endingDailyPages);
            var approved=Resources.Load<TextAsset>("Dialogue/Ending/approved");
            if(approved==null||seventhNight?.decision==null||seventhNight.outcome==null)return;
            try
            {
                var saved=JsonConvert.DeserializeObject<ApprovedEnding>(approved.text);
                if(saved==null||string.IsNullOrEmpty(saved.fingerprint)||saved.dailyPages==null||saved.dailyPages.Length==0)return;
                bool fingerprintMatch=string.Equals(saved.fingerprint,RecollectionFingerprint(seventhNight.recollections),StringComparison.Ordinal);
                if(fingerprintMatch)
                {
                    endingDailyPages=saved.dailyPages;endingDailyMemories=GroupMemoriesByDay(seventhNight.recollections,endingDailyPages);
                    if(seventhNight.decision.choice=="leave"&&seventhNight.outcome.status=="pending")endingClosingText=saved.closingText??"";
                }
            }
            catch(Exception e){Debug.LogWarning("Approved ending resource could not be read: "+e.Message);}
        }
        static EndingDemo.EndingDemoView.DailyPage[] BuildDailyPagesFromRecollections(EndingDemo.EndingDemoView.Recollection[] memories)
        {
            if(memories==null||memories.Length==0)return Array.Empty<EndingDemo.EndingDemoView.DailyPage>();
            var byDay=new Dictionary<int,List<EndingDemo.EndingDemoView.Recollection>>();
            foreach(var memory in memories)
            {
                if(memory==null)continue;
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
        static EndingDemo.EndingDemoView.Recollection[][] GroupMemoriesByDay(EndingDemo.EndingDemoView.Recollection[] memories,EndingDemo.EndingDemoView.DailyPage[] pages)
        {
            var groups=new EndingDemo.EndingDemoView.Recollection[pages?.Length??0][];
            for(int i=0;i<groups.Length;i++)
            {
                var list=new List<EndingDemo.EndingDemoView.Recollection>();int day=pages[i]?.day??0;
                if(memories!=null)foreach(var memory in memories)if(memory!=null&&memory.day==day)list.Add(memory);
                groups[i]=list.ToArray();
            }
            return groups;
        }
        static string RecollectionFingerprint(EndingDemo.EndingDemoView.Recollection[] memories)
        {
            if(memories==null)return "";
            var pieces=new string[memories.Length];
            for(int i=0;i<memories.Length;i++)pieces[i]=(memories[i]?.day??0).ToString()+"\n"+(memories[i]?.original??"");
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n\u001e\n",pieces)))).Replace("-","").ToLowerInvariant();
        }
        void EndingFailure(string error)
        {
            endingGenerating=false;endingTitle.text="让这个夜晚，等一等";SetEndingWords(error,false);
            endingNext.interactable=true;endingNext.GetComponentInChildren<TextMeshProUGUI>().text="重新连接并重试";endingPrevious.interactable=endingOriginal.interactable=false;
        }
        void ShowEndingPage(int page)
        {
            if(seventhNight?.decision==null)return;
            if(endingDailyPages==null||endingDailyPages.Length==0)PrepareDailyPages();
            int dailyCount=endingDailyPages.Length;
            endingPage=Mathf.Clamp(page,0,dailyCount);endingOriginalShown=false;
            int total=dailyCount+1;
            if(endingPage<dailyCount){
                ExitBlackOutcome();var daily=endingDailyPages[endingPage];endingTitle.text=daily.title;endingBody.text=daily.text;endingEyebrow.text="第 "+daily.day+" 天";UpdateEndingScene(daily);
            }
            else{ShowBlackOutcome();return;}
            SetEndingWords(endingBody.text,true);
            endingCharacter?.SetState("sad",true);
            endingProgress.text=$"{endingPage+1} / {total}  ·  {seventhNight.memoryCount} 段经历";
            endingPrevious.interactable=endingPage>0;endingOriginal.interactable=endingDailyMemories.Length>endingPage&&endingDailyMemories[endingPage].Length>0;endingNext.interactable=true;
            endingNext.GetComponentInChildren<TextMeshProUGUI>().text="继续 →";
        }
        void UpdateEndingScene(EndingDemo.EndingDemoView.DailyPage daily)
        {
            if(endingSceneImage==null)return;
            string value=(daily?.title??"")+"\n"+(daily?.text??"");
            bool radio=value.IndexOf("收音机",StringComparison.Ordinal)>=0||value.IndexOf("李庭",StringComparison.Ordinal)>=0;
            endingSceneImage.texture=Resources.Load<Texture2D>(radio?"Art/ending_radio":"Art/lighthouse")??Resources.Load<Texture2D>("Art/lighthouse");
            endingSceneImage.color=radio?Color.white:new Color(.72f,.76f,.82f,.82f);
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
            return FirstEndingSentence(source);
        }
        string FullEndingText()
        {
            string body=string.IsNullOrWhiteSpace(endingClosingText)?seventhNight?.outcome?.text:endingClosingText;
            if(string.IsNullOrWhiteSpace(body))body=seventhNight?.decision?.decision??"";
            if(!string.IsNullOrWhiteSpace(seventhNight?.decision?.lastLine))body += "\n\n" + seventhNight.decision.lastLine;
            return body;
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
            endingBlackDetailsShown=true;endingBlackTitle.text=seventhNight?.outcome?.label??"";endingBlackBody.text=FullEndingText();SetBlackDetailsLayout();endingBlackWordsGroup.alpha=1;endingBlackNext.gameObject.SetActive(true);
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
            if(endingPage>=endingDailyPages.Length)CloseOverlay();else ShowEndingPage(endingPage+1);
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
            if(seventhNight==null||endingPage<0||endingPage>=endingDailyPages.Length||endingDailyMemories.Length<=endingPage)return;
            endingOriginalShown=!endingOriginalShown;var memories=endingDailyMemories[endingPage];
            var originals=new List<string>();foreach(var memory in memories)if(!string.IsNullOrWhiteSpace(memory.original))originals.Add(memory.original.Trim());
            SetEndingWords(endingOriginalShown?string.Join("\n\n",originals.ToArray()):endingDailyPages[endingPage].text,false);
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
