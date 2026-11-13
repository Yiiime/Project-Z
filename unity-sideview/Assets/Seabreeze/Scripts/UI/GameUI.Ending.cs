using System;
using System.Collections;
using Newtonsoft.Json;
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
        EndingRun seventhNight;
        int endingPage,endingDisplayGeneration;
        bool endingGenerating,seventhNightOpening,endingOriginalShown;
        float endingVisible;
        int endingFullCharacterCount;
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
            endingColumn=EndingRegion("Seventh night story",overlayRoot,new Vector2(.48f,.09f),new Vector2(.955f,.90f));
            endingEyebrow=Label("Seventh night chapter",endingColumn,TL,TL,Vector2.zero,new Vector2(840,35),"第七天 · 夜晚 · 月亮岛码头",18,Accent);
            endingTitle=Label("Z ending title",endingColumn,TL,TL,new Vector2(0,-68),new Vector2(845,110),"这些日子，仍然在这里",43,Paper);endingTitle.richText=false;
            endingSource=Label("Game memory source",endingColumn,TL,TL,new Vector2(0,-174),new Vector2(850,45),"正在同步本局中 Z 已知的经历",17,Muted);endingSource.richText=false;
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
            string saved=session.State.memory?.seventhNightEndingJson;
            if(!string.IsNullOrEmpty(saved))try{var cached=JsonConvert.DeserializeObject<EndingRun>(saved);if(cached?.worldId==session.State.memory.worldId){seventhNight=currentEnding=cached;endingContextRevision=(string)Newtonsoft.Json.Linq.JObject.Parse(saved)["contextRevision"]??"";}}catch{ }
            if(seventhNight!=null)ShowEndingPage(0);else GenerateSeventhNight();
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
                seventhNight=currentEnding=reply.ending.ToObject<EndingRun>();endingContextRevision=(string)reply.ending["contextRevision"]??endingContextRevision;endingGenerating=false;endingHistoryShown=false;ShowEndingPage(0);
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
                    seventhNight=currentEnding=reply.ending.ToObject<EndingRun>();endingGenerating=false;ShowEndingPage(0);
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
            endingHistoryShown=!endingHistoryShown;seventhNight=endingHistoryShown?previousEnding:currentEnding;ShowEndingPage(0);
            endingHistory.GetComponentInChildren<TextMeshProUGUI>().text=endingHistoryShown?"返回当前结局":"看上次结局";
            if(endingHistoryShown)endingSource.text="上次结局的记忆快照 · 当前上传文件和游戏进度不会改变";
        }
        void EndingFailure(string error)
        {
            endingGenerating=false;endingTitle.text="让这个夜晚，等一等";SetEndingWords(error,false);
            endingNext.interactable=true;endingNext.GetComponentInChildren<TextMeshProUGUI>().text="重新连接并重试";endingPrevious.interactable=endingOriginal.interactable=false;
        }
        void ShowEndingPage(int page)
        {
            if(seventhNight?.decision==null)return;
            endingPage=Mathf.Clamp(page,0,seventhNight.recollections.Length+2);endingOriginalShown=false;
            int memories=seventhNight.recollections.Length,total=memories+3;
            endingSource.text=$"本局 Z 已知经历 {seventhNight.liveMemoryCount} 段 · 七日剧情 {seventhNight.memoryCount-seventhNight.liveMemoryCount} 段 · 已随此存档保存";
            if(endingPage==0){endingTitle.text=seventhNight.decision.title;endingBody.text=seventhNight.decision.opening;endingEyebrow.text="第七晚 · 她自己的答案";}
            else if(endingPage<=memories){var m=seventhNight.recollections[endingPage-1];endingTitle.text=m.title;endingBody.text=m.text;endingEyebrow.text="第 "+m.day+" 天 · 仍然记得";}
            else if(endingPage==memories+1){endingTitle.text="所以，这一次。";endingBody.text=seventhNight.decision.reason+"\n\n"+seventhNight.decision.decision;endingEyebrow.text="这些经历之后 · Z 的选择";}
            else{endingTitle.text=seventhNight.outcome.label;endingBody.text=seventhNight.outcome.text+"\n\n"+seventhNight.decision.lastLine;endingEyebrow.text="故事继续 · 最后一句话留给她";}
            SetEndingWords(endingBody.text,true);
            endingCharacter?.SetState(endingPage==0||endingPage>memories?"serious":"sad",true);
            endingProgress.text=$"{endingPage+1} / {total}  ·  {seventhNight.memoryCount} 段经历";
            endingPrevious.interactable=endingPage>0;endingOriginal.interactable=endingPage>0&&endingPage<=memories;endingNext.interactable=true;
            endingNext.GetComponentInChildren<TextMeshProUGUI>().text=endingPage==total-1?"回到小岛 →":"继续 →";
        }
        void NextEndingPage()
        {
            if(endingGenerating)return;
            if(seventhNight==null){GenerateSeventhNight();return;}
            if(endingBody.maxVisibleCharacters!=int.MaxValue){endingBody.maxVisibleCharacters=int.MaxValue;endingBody.ForceMeshUpdate();endingCharacter?.SetState("serious",false);return;}
            if(endingPage>=seventhNight.recollections.Length+2)CloseOverlay();else ShowEndingPage(endingPage+1);
        }
        void SetEndingWords(string value,bool typewriter)
        {
            endingBody.text=value??"";endingBody.maxVisibleCharacters=int.MaxValue;endingBody.ForceMeshUpdate();
            endingFullCharacterCount=Mathf.Max(endingBody.textInfo.characterCount,endingBody.text.Length);
            float width=Mathf.Max(200,((RectTransform)endingBody.transform.parent).rect.width-16);
            float height=Mathf.Max(1,endingBody.GetPreferredValues(endingBody.text,width,0).y+16);
            endingBody.rectTransform.sizeDelta=new Vector2(-16,height);endingBody.rectTransform.anchoredPosition=Vector2.zero;
            endingScroll.StopMovement();endingScroll.verticalNormalizedPosition=1;
            endingVisible=typewriter?0:endingFullCharacterCount;endingBody.maxVisibleCharacters=typewriter?0:int.MaxValue;
            endingBody.ForceMeshUpdate();
        }
        void ToggleEndingOriginal()
        {
            if(seventhNight==null||endingPage<1||endingPage>seventhNight.recollections.Length)return;
            endingOriginalShown=!endingOriginalShown;var memory=seventhNight.recollections[endingPage-1];
            SetEndingWords(endingOriginalShown?memory.original:memory.text,false);
            endingCharacter?.SetState("serious",false);
        }
        void UpdateSeventhNight()
        {
            if(Time.unscaledTime>=nextEndingSourceCheck)RefreshEndingSource();
            if(endingBody!=null&&endingBody.maxVisibleCharacters!=int.MaxValue&&!endingGenerating){endingVisible+=Time.unscaledDeltaTime*Style.TypewriterSpeed;endingBody.maxVisibleCharacters=Mathf.FloorToInt(endingVisible);if(endingVisible>=endingFullCharacterCount){endingBody.maxVisibleCharacters=int.MaxValue;endingCharacter?.SetState("serious",false);}endingBody.ForceMeshUpdate();}
            if(GameInput.Pressed(Key.Escape))CloseOverlay();
            else if(GameInput.Pressed(Key.Enter)||GameInput.Pressed(Key.Space)||GameInput.Pressed(Key.E))NextEndingPage();
        }
        void ReleaseSeventhNight()
        {
            endingDisplayGeneration++;endingGenerating=false;seventhNightOpening=false;
            if(endingCharacter!=null){endingCharacter.Hide();endingCharacter=null;}
            if(overlayMode=="z-ending")session?.Memory?.Client?.Focus(null);
        }
    }
}
