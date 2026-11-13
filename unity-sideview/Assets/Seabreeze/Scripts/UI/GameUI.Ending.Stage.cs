using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    public sealed partial class GameUI
    {
        EndingStageArt endingArt;
        RawImage endingBackdrop;
        RectTransform endingPortrait,endingPhone;
        CanvasGroup endingStageFade;
        TextMeshProUGUI endingQuote,endingAct,endingWorldLink;
        GameObject endingMemoryShelf,endingTools;
        Button endingAuto;
        readonly HashSet<int> endingVisited=new HashSet<int>();
        readonly List<Button> endingMemoryThreads=new List<Button>();
        string endingStageRun="";
        float endingStageStarted,endingAutoAt;
        bool endingAutoPlay;
        static readonly string[] EndingThreadNames={"照片与见面","花店的日常","旧物与设备","直播里的话","那些约定"};
        static readonly string[] EndingThreadIcons={"album","people","tasks","live","messages"};

        void BuildEndingStage()
        {
            endingArt=Resources.Load<EndingStageArt>("UI/EndingStageArt");
            endingStageRun="";endingVisited.Clear();endingMemoryThreads.Clear();endingAutoPlay=false;
            endingBackdrop=Stretch("Moon island ending coast",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
            endingBackdrop.texture=endingArt?.bedroom??Resources.Load<Texture2D>("Art/lighthouse");endingBackdrop.raycastTarget=false;
            var veil=Stretch("Night color",overlayRoot,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();veil.color=new Color(.025f,.045f,.12f,.40f);veil.raycastTarget=true;
            endingPhone=EndingRegion("Ending phone frame",overlayRoot,new Vector2(.27f,.19f),new Vector2(.73f,.87f));
            var frame=endingPhone.gameObject.AddComponent<RoundedPanel>();frame.color=new Color(.035f,.05f,.085f,.85f);frame.CornerRadius=40;frame.SetBorder(new Color(.8f,.77f,.9f,.5f),2);frame.raycastTarget=false;
            Label("Ending phone header",endingPhone,TL,TL,new Vector2(35,-20),new Vector2(600,35),"Z · 第七晚",20,Paper);
            endingPhone.gameObject.SetActive(false);
            endingPortrait=EndingRegion("Z ending portrait",overlayRoot,new Vector2(.54f,.16f),new Vector2(.91f,.88f));
            endingCharacter=endingPortrait.gameObject.AddComponent<Live2DCharacterView>();endingCharacter.Initialize(endingPortrait);
            var definition=SevenCharacterCatalog.Find("z");var profile=Resources.Load<Live2DModelProfile>(definition?.portrait??"Memory/Portraits/ZFinal-v03");if(profile!=null)endingCharacter.Show(profile);
            endingAct=Label("Ending act",overlayRoot,TL,TL,new Vector2(60,-98),new Vector2(920,38),"第七晚 · 把这些日子带上",22,Accent);
            endingEyebrow=Label("Seventh night chapter",overlayRoot,TL,TL,new Vector2(60,-143),new Vector2(920,32),"Z 的家",18,Paper);
            endingTitle=Label("Z ending title",overlayRoot,TL,TL,new Vector2(60,-197),new Vector2(830,90),"留在这些日子里的话",38,Paper);endingTitle.richText=false;
            endingQuote=Label("A line actually heard",overlayRoot,TL,TL,new Vector2(60,-300),new Vector2(700,155),"",24,Paper);endingQuote.richText=false;
            endingStageFade=endingQuote.gameObject.AddComponent<CanvasGroup>();
            endingMemoryShelf=Rect("Memory shelf",overlayRoot,TL,TL,new Vector2(60,-460),new Vector2(740,180)).gameObject;
            for(int i=0;i<EndingThreadNames.Length;i++)
            {
                int thread=i;
                var b=Button("Ending memory thread "+i,endingMemoryShelf.transform,TL,TL,new Vector2((i%3)*235,-(i/3)*82),new Vector2(220,68),EndingThreadNames[i],()=>OpenEndingThread(thread),false,18);
                FinalKitAssets.Icon(EndingThreadIcons[i],b.transform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-23,0),new Vector2(24,24));endingMemoryThreads.Add(b);
            }
            endingColumn=EndingRegion("Seventh night story",overlayRoot,new Vector2(.045f,.025f),new Vector2(.955f,.29f));
            var subtitle=Stretch("Cinematic dialogue panel",endingColumn,Vector2.zero,Vector2.zero).gameObject.AddComponent<RoundedPanel>();subtitle.color=new Color(.025f,.04f,.07f,.88f);subtitle.CornerRadius=18;subtitle.raycastTarget=false;
            var viewport=Stretch("Ending scroll",endingColumn,new Vector2(28,85),new Vector2(-28,-18));viewport.gameObject.AddComponent<RectMask2D>();var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;
            endingScroll=viewport.gameObject.AddComponent<ScrollRect>();endingScroll.horizontal=false;endingScroll.viewport=viewport;endingScroll.scrollSensitivity=38;endingScroll.movementType=ScrollRect.MovementType.Clamped;
            var words=Rect("Ending words",viewport,TL,TL,Vector2.zero,new Vector2(1650,160));words.anchorMax=Vector2.one;words.sizeDelta=new Vector2(-16,160);
            endingBody=words.gameObject.AddComponent<TextMeshProUGUI>();endingBody.font=UIPrimitives.Font;endingBody.fontSize=26;endingBody.color=Paper;endingBody.richText=false;endingBody.lineSpacing=8;endingBody.textWrappingMode=TextWrappingModes.Normal;endingScroll.content=words;
            endingProgress=Label("Ending page progress",endingColumn,BL,BL,new Vector2(28,29),new Vector2(690,30),"",17,Muted);
            endingPrevious=Button("Previous ending memory",endingColumn,new Vector2(1,0),new Vector2(1,0),new Vector2(-585,18),new Vector2(170,48),"← 上一段",()=>ShowEndingPage(endingPage-1),false,18);
            endingOriginal=Button("Ending memory source",endingColumn,new Vector2(1,0),new Vector2(1,0),new Vector2(-360,18),new Vector2(205,48),"展开这段原文",ToggleEndingOriginal,false,18);
            endingNext=Button("Continue ending",endingColumn,new Vector2(1,0),new Vector2(1,0),new Vector2(-28,18),new Vector2(310,48),"继续 →",NextEndingPage,true,20);
            endingWorldLink=Label("This game's ending snapshot",overlayRoot,new Vector2(1,1),new Vector2(1,1),new Vector2(-60,-145),new Vector2(780,35),"",17,Muted,TextAlignmentOptions.TopRight);
            endingTools=Rect("Ending tools",overlayRoot,TL,TL,new Vector2(60,-18),new Vector2(1200,62)).gameObject;
            Button("Reconsider current island memories",endingTools.transform,TL,TL,Vector2.zero,new Vector2(285,46),"以本局经历重新演出",()=>{if(!endingGenerating){seventhNight=null;GenerateSeventhNight();}},false,17);
            endingUpload=Button("Upload ending memories",endingTools.transform,TL,TL,new Vector2(303,0),new Vector2(280,46),"上传 / 更换记忆文件 ↗",OpenEndingMemoryUpload,false,17);
            endingHistory=Button("Previous ending version",endingTools.transform,TL,TL,new Vector2(600,0),new Vector2(200,46),"看上次结局",ToggleEndingHistory,false,17);endingHistory.interactable=false;
            endingAuto=Button("Ending autoplay",endingTools.transform,TL,TL,new Vector2(820,0),new Vector2(190,46),"自动演出：关",()=>{endingAutoPlay=!endingAutoPlay;endingAuto.GetComponentInChildren<TextMeshProUGUI>().text=endingAutoPlay?"自动演出：开":"自动演出：关";ResetEndingAutoTime();},false,17);
            Button("Return to island",overlayRoot,new Vector2(1,1),new Vector2(1,1),new Vector2(-60,-18),new Vector2(240,46),"返回小岛 · Esc",CloseOverlay,false,17);
            endingSource=Label("Game memory source",overlayRoot,BL,BL,new Vector2(86,97),new Vector2(900,24),"正在同步本局经历",14,Muted);endingSource.richText=false;
        }

        void EnsureEndingStageRun()
        {
            if(endingStageRun==seventhNight.id)return;
            endingStageRun=seventhNight.id;endingVisited.Clear();
        }
        static int EndingThread(Seabreeze.EndingDemo.EndingDemoView.Recollection memory)
        {
            string text=memory.title??"";
            if(Regex.IsMatch(text,"直播|弹幕|屏幕|下播|断线"))return 3;
            if(Regex.IsMatch(text,"花|养护|浇水|包装|价格牌"))return 1;
            if(Regex.IsMatch(text,"照片|照相|见面|迎接|合照"))return 0;
            if(Regex.IsMatch(text,"维修|音箱|设备|旋钮|收音机|盒子|研究所|照明"))return 2;
            return 4;
        }
        void OpenEndingThread(int thread)
        {
            if(endingGenerating||seventhNight==null)return;
            int first=-1;
            for(int i=0;i<seventhNight.recollections.Length;i++)if(EndingThread(seventhNight.recollections[i])==thread){if(first<0)first=i;if(!endingVisited.Contains(i)){ShowEndingPage(i+1);return;}}
            if(first>=0)ShowEndingPage(first+1);
        }
        Texture2D EndingMemoryPlace(string title,out string place)
        {
            title=title??"";
            if(Regex.IsMatch(title,"花|浇水|养护|价格牌")){place="晴野的花店";return endingArt?.flowers;}
            if(Regex.IsMatch(title,"照相|照片|合照|创造管理|替代")){place="周伯伯的照相馆";return endingArt?.photo;}
            if(Regex.IsMatch(title,"咖啡|李庭|旋钮|盒子|旧物")){place="李庭的咖啡店";return endingArt?.cafe;}
            if(Regex.IsMatch(title,"研究所|维修|设备|阿星|照明|音箱")){place="阿星的研究所";return endingArt?.research;}
            if(Regex.IsMatch(title,"森林|梨园|小屋")){place="森林小屋";return endingArt?.forest;}
            if(Regex.IsMatch(title,"码头|船|出航|海况")){place="月亮岛码头";return endingArt?.dock;}
            place="月亮岛公共街";return endingArt?.street;
        }
        void ShowEndingStage(int page)
        {
            int count=seventhNight.recollections.Length;
            bool memory=page>0&&page<=count;
            bool phone=page==count+1||(memory&&EndingThread(seventhNight.recollections[page-1])==3);
            bool outcome=page==count+3;
            endingPhone.gameObject.SetActive(phone);endingMemoryShelf.SetActive(page==0||outcome);
            endingQuote.text="";string place;Texture2D art;
            if(page==0){art=endingArt?.bedroom;place="Z 的家 · 回望这些日子";endingAct.text="第一幕 · 把这些日子带上";}
            else if(memory)
            {
                var m=seventhNight.recollections[page-1];art=phone?endingArt?.bedroom:EndingMemoryPlace(m.title,out _);
                EndingMemoryPlace(m.title,out place);if(phone)place="手机里的直播回忆";
                endingAct.text="第一幕 · "+EndingThreadNames[EndingThread(m)];
                foreach(string line in (m.original??"").Split('\n'))if(Regex.IsMatch(line,"^Z[：:]")){endingQuote.text="那时，她说\n“"+line.Substring(2).Trim().Trim('“','”')+"”";break;}
                if(endingQuote.text.Length>160)endingQuote.text=endingQuote.text.Substring(0,157)+"…";
                string feeling=Regex.IsMatch(m.text??"","笑|开心|玩笑")?"happy":Regex.IsMatch(m.text??"","生气|失约|伤人")?"angry":"serious";endingCharacter?.SetState(feeling,true);
            }
            else if(phone){art=endingArt?.bedroom;place="手机 · 她想说的话";endingAct.text="第二幕 · 这次，听我说完";endingQuote.text=string.IsNullOrWhiteSpace(seventhNight.lastMessage)?"":(seventhNight.playerName??"旅人")+"说\n“"+seventhNight.lastMessage+"”";}
            else{art=endingArt?.dock;place="月亮岛码头";endingAct.text=outcome?"尾声 · 这段故事的去向":"第三幕 · 由她回答";endingQuote.text=outcome?seventhNight.outcome.label:"";}
            endingBackdrop.texture=art??Resources.Load<Texture2D>("Art/lighthouse");endingBackdrop.color=phone?new Color(.35f,.40f,.55f):new Color(.72f,.77f,.90f);
            endingEyebrow.text=(memory?"第 "+seventhNight.recollections[page-1].day+" 天 · ":"")+place;
            endingPortrait.anchorMin=phone?new Vector2(.32f,.22f):outcome?new Vector2(.66f,.24f):new Vector2(.53f,.18f);
            endingPortrait.anchorMax=phone?new Vector2(.68f,.85f):outcome?new Vector2(.92f,.83f):new Vector2(.89f,.89f);endingPortrait.offsetMin=endingPortrait.offsetMax=Vector2.zero;
            endingProgress.text=outcome?"去留意愿与实际结果分别保留":$"已回望 {endingVisited.Count} / {count} 段经历 · E / 空格继续";
            endingWorldLink.text=$"本局第 {session.State.day} 天 · {session.State.place} · {seventhNight.liveMemoryCount} 段实际经历";
            for(int t=0;t<endingMemoryThreads.Count;t++)
            {
                int total=0,seen=0;for(int i=0;i<count;i++)if(EndingThread(seventhNight.recollections[i])==t){total++;if(endingVisited.Contains(i))seen++;}
                endingMemoryThreads[t].interactable=total>0;endingMemoryThreads[t].GetComponentInChildren<TextMeshProUGUI>().text=EndingThreadNames[t]+$"\n{seen} / {total}";
            }
            endingStageStarted=Time.unscaledTime;endingStageFade.alpha=0;ResetEndingAutoTime();
        }
        void ResetEndingAutoTime(){endingAutoAt=Time.unscaledTime+Mathf.Max(8,(endingBody?.text.Length??0)/12f+3);}
        void TickEndingStage()
        {
            if(endingBackdrop==null||endingGenerating||seventhNight==null)return;
            float elapsed=Time.unscaledTime-endingStageStarted;
            endingStageFade.alpha=Mathf.Clamp01(elapsed/.7f);
            // Slow framing motion uses unscaled time; map time and player movement stay paused.
            float zoom=1+Mathf.Min(.025f,elapsed*.0012f);endingBackdrop.rectTransform.localScale=Vector3.one*zoom;
            if(endingAutoPlay&&!endingOriginalShown&&endingPage<seventhNight.recollections.Length+3&&Time.unscaledTime>=endingAutoAt){NextEndingPage();ResetEndingAutoTime();}
        }
        void DisposeEndingStage()
        {
            endingVisited.Clear();endingMemoryThreads.Clear();endingStageRun="";endingAutoPlay=false;
            endingBackdrop=null;endingPhone=null;endingPortrait=null;endingStageFade=null;
        }
    }
}
