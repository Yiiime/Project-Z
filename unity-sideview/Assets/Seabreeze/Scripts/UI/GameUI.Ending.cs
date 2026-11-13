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
            BuildEndingStage();
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
            EnsureEndingStageRun();
            endingPage=Mathf.Clamp(page,0,seventhNight.recollections.Length+3);endingOriginalShown=false;
            int memories=seventhNight.recollections.Length;
            if(endingPage==0){endingTitle.text="留在这些日子里的话";endingBody.text=seventhNight.decision.opening;}
            else if(endingPage<=memories){var m=seventhNight.recollections[endingPage-1];endingTitle.text=m.title;endingBody.text=m.text;endingVisited.Add(endingPage-1);}
            else if(endingPage==memories+1){endingTitle.text="这次，听我说完";endingBody.text=seventhNight.decision.reason;}
            else if(endingPage==memories+2){endingTitle.text=seventhNight.decision.choice=="leave"?"我想去看看":"今夜，我想留下";endingBody.text=seventhNight.decision.decision;}
            else{endingTitle.text=seventhNight.decision.title;endingBody.text=seventhNight.outcome.text+"\n\n"+seventhNight.decision.lastLine;}
            SetEndingWords(endingBody.text,true);ShowEndingStage(endingPage);
            endingPrevious.interactable=endingPage>0;endingOriginal.interactable=endingPage>0&&endingPage<=memories;endingNext.interactable=true;
            endingNext.GetComponentInChildren<TextMeshProUGUI>().text=endingPage==memories+3?"回到小岛 →":endingPage==memories+1?"听她的选择 →":"继续 →";
        }
        void NextEndingPage()
        {
            if(endingGenerating)return;
            if(seventhNight==null){GenerateSeventhNight();return;}
            if(endingBody.maxVisibleCharacters!=int.MaxValue){endingBody.maxVisibleCharacters=int.MaxValue;endingBody.ForceMeshUpdate();endingCharacter?.SetState("serious",false);return;}
            if(endingPage>=seventhNight.recollections.Length+3){CloseOverlay();return;}
            int next=endingPage+1;
            if(next>seventhNight.recollections.Length&&endingVisited.Count<seventhNight.recollections.Length)
                for(int i=0;i<seventhNight.recollections.Length;i++)if(!endingVisited.Contains(i)){next=i+1;break;}
            ShowEndingPage(next);
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
            TickEndingStage();
            if(Time.unscaledTime>=nextEndingSourceCheck)RefreshEndingSource();
            if(endingBody!=null&&endingBody.maxVisibleCharacters!=int.MaxValue&&!endingGenerating){endingVisible+=Time.unscaledDeltaTime*Style.TypewriterSpeed;endingBody.maxVisibleCharacters=Mathf.FloorToInt(endingVisible);if(endingVisible>=endingFullCharacterCount){endingBody.maxVisibleCharacters=int.MaxValue;endingCharacter?.SetState("serious",false);}endingBody.ForceMeshUpdate();}
            if(GameInput.Pressed(Key.Escape))CloseOverlay();
            else if(GameInput.Pressed(Key.Enter)||GameInput.Pressed(Key.Space)||GameInput.Pressed(Key.E))NextEndingPage();
        }
        void ReleaseSeventhNight()
        {
            endingDisplayGeneration++;endingGenerating=false;seventhNightOpening=false;
            DisposeEndingStage();
            if(endingCharacter!=null){endingCharacter.Hide();endingCharacter=null;}
            if(overlayMode=="z-ending")session?.Memory?.Client?.Focus(null);
        }
    }
}
