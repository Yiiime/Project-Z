using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace Seabreeze
{
    [Serializable] public sealed class StoryClipDescription { public string id,title; public string[] lines; }
    [Serializable] public sealed class StoryClipBook { public StoryClipDescription[] clips; }
    public sealed class StoryPlaytestRuntime : MonoBehaviour
    {
        public static StoryPlaytestRuntime Instance {get;private set;}
        public bool MovieOpen => video != null;
        public string CurrentClip => clipId;
        public bool Preparing => preparing;
        GameSession session; SaveData observed;
        StoryProgressState State => session.State.storyProgress;
        MemoryGameController Memory => session.Memory;
        WorldDirector World => WorldDirector.Instance;
        string notice="走近一位岛民按 E 聊天，再请 TA 提一个小委托。",selected="linxu",clipId,mediaError;
        bool preparing,loadingArtwork,showPeople=true;
        float mediaStarted;
        Texture2D itemTexture,containerTexture;
        string textureQuest;
        GameObject props,carried; WorldScene propScene;
        VideoPlayer video; RenderTexture videoTexture; StoryProgressState mediaOwner;
        readonly Dictionary<string,StoryClipDescription> clips=new Dictionary<string,StoryClipDescription>();
        GUIStyle label,title,small,button; Font font;
        readonly Color ink=new Color(.065f,.085f,.135f,.97f),gold=new Color(.9f,.72f,.42f),muted=new Color(.67f,.73f,.8f);
        void Awake()
        {
            Application.runInBackground=true;
            Instance=this;session=GetComponent<GameSession>();observed=session.State;
            var book=Resources.Load<TextAsset>("StoryPlaytest/stories");
            if(book!=null)foreach(var c in JsonUtility.FromJson<StoryClipBook>(book.text).clips)clips[c.id]=c;
        }
        void Update()
        {
            if(session==null||!session.IsReady||World?.Stage==null)return;
            if(observed!=session.State)
            {
                StopAllCoroutines();preparing=loadingArtwork=false;StopMovie();ClearProps();ReleaseTextures();observed=session.State;notice="已恢复这局的剧情进度。";
            }
            if(State.quest!=null&&State.quest.ready&&!State.quest.completed&&!loadingArtwork&&string.IsNullOrEmpty(State.quest.error))
            {
                if(textureQuest!=State.quest.id)StartCoroutine(LoadArtwork(State.quest));
                else if(propScene!=World.Stage||props==null)SpawnProps();
            }
            if(State.quest!=null&&!State.quest.ready&&!preparing&&string.IsNullOrEmpty(State.quest.error)&&Memory?.Client!=null)StartCoroutine(ResumeAssets(State.quest));
            if(State.quest?.picked==true&&carried!=null&&World.Player!=null)carried.transform.position=World.Player.position+new Vector3(.45f,1.55f,0);
            if(video!=null)
            {
                if(!video.isPrepared&&Time.unscaledTime-mediaStarted>45&&string.IsNullOrEmpty(mediaError))mediaError="影片准备超时，可以重试。";
                return;
            }
            if(World.IsTransitioning||session.InputBlocked||Memory?.PlayerChatOpen==true)return;
            if(State.queuedClips.Count>0)StartMovie(State.queuedClips[0]);
            else if(State.livePending&&State.winner=="")StartMovie("night_live");
        }
        public void RecordChat(string actor,string conversation)
        {
            if(State.CreditChat(actor,conversation,session.State.day,session.State.period,StoryPlaytest.Settings))
            {
                notice=SevenCharacterCatalog.Name(actor)+" · 完整交流 +"+StoryPlaytest.Settings.chatPoints;
                session.Save();session.NotifyChanged();
                if(!StoryPlaytest.Verifying&&State.queuedClips.Count==0&&(State.quest==null||State.quest.completed))StartCoroutine(AfterChatOffer(actor));
            }
            else notice="这段交流已记住，本时段的聊天分已经获得。";
        }
        IEnumerator AfterChatOffer(string actor){while(Memory?.PlayerChatOpen==true)yield return null;if(!preparing)RequestQuest(actor,true);}
        public void RequestQuest(string actor,bool spontaneous=false)
        {
            if(preparing||State.winner!=""||State.livePending||State.quest!=null&&!State.quest.completed&&State.quest.error=="")return;
            selected=actor;Memory?.Conversations.Cancel("准备委托");StartCoroutine(Plan(actor,spontaneous));
        }
        IEnumerator Plan(string actor,bool spontaneous)
        {
            preparing=true;notice="正在听取"+SevenCharacterCatalog.Name(actor)+"的想法…";var owner=session.State;
            float until=Time.unscaledTime+35;
            while(Memory!=null&&!Memory.Client.CanGenerate&&Time.unscaledTime<until)yield return null;
            if(owner!=session.State)yield break;
            if(Memory==null||!Memory.Client.CanGenerate){notice="模型服务还没就绪。请稍候再试。";preparing=false;yield break;}
            var request=Memory.Client.RequestBase();request.actorId=actor;request.scene=session.CurrentPlace.ToString();request.details=new JObject{{"points",State.Person(actor).points},{"spontaneous",spontaneous}};
            bool done=false;MemoryReply reply=null;string error=null;
            Memory.Client.Generate("/api/game/story/plan",request,r=>{reply=r;done=true;},e=>{error=e;done=true;});
            while(!done&&owner==session.State)yield return null;
            if(owner!=session.State)yield break;
            if(error!=null||reply?.storyTask==null){notice=error??"暂时没有生成委托，请重试。";preparing=false;yield break;}
            var data=reply.storyTask;if((string)data["status"]=="none"){notice="这次交流已记住。TA 这会儿没有需要帮忙的小事。";preparing=false;yield break;}ClearProps();ReleaseTextures();
            float x=World.Player.position.x,half=Mathf.Max(6,World.Stage.width/2-2);
            var q=new StoryPlaytestQuest{id=(string)data["id"],actor=actor,title=(string)data["title"],request=(string)data["request"],item=(string)data["item"],container=(string)data["container"],scene=session.CurrentPlace.ToString(),assetMode=(string)data["assetMode"],error="",itemX=Mathf.Clamp(x+2,-half,half),targetX=Mathf.Clamp(x+7,-half,half)};
            if(Mathf.Abs(q.targetX-q.itemX)<2)q.targetX=Mathf.Clamp(x-5,-half,half);
            State.quest=q;session.Save();notice="正在现场绘制物体与容器（"+q.assetMode+"）。可以继续走动。";
            yield return PollAssets(q);preparing=false;
        }
        IEnumerator PollAssets(StoryPlaytestQuest q)
        {
            float until=Time.unscaledTime+420;
            while(State.quest==q&&Time.unscaledTime<until)
            {
                while(!Memory.Client.CanGenerate&&Time.unscaledTime<until)yield return null;
                if(Time.unscaledTime>=until)break;
                var request=Memory.Client.RequestBase();request.taskId=q.id;bool done=false;MemoryReply reply=null;string error=null;
                Memory.Client.Generate("/api/game/story/status",request,r=>{reply=r;done=true;},e=>{error=e;done=true;});
                while(!done&&State.quest==q)yield return null;
                if(State.quest!=q)yield break;
                if(error!=null){notice=error;yield return new WaitForSecondsRealtime(3);continue;}
                var data=reply?.storyTask;
                if((string)data?["status"]=="ready")
                {
                    q.itemUrl=(string)data["itemUrl"];q.containerUrl=(string)data["containerUrl"];q.ready=true;q.error="";
                    notice="物体准备好了。可以查看委托并决定是否接受。";session.Save();
                    Memory.PublishStoryEvent("offer_"+q.id,SevenCharacterCatalog.Name(q.actor)+"向玩家提出委托："+q.request,new[]{"player",q.actor},true);
                    yield break;
                }
                if((string)data?["status"]=="failed"){q.error=(string)data["error"];notice=q.error;session.Save();yield break;}
                yield return new WaitForSecondsRealtime(2);
            }
            if(State.quest==q){q.error="资产准备超时，可以重试委托。";notice=q.error;session.Save();}
        }
        IEnumerator ResumeAssets(StoryPlaytestQuest q){preparing=true;yield return PollAssets(q);preparing=false;}
        public void AcceptQuest()
        {
            var q=State.quest;if(q==null||!q.ready||q.completed||State.winner!="")return;
            q.accepted=true;notice="去找「"+q.item+"」，走近按 E 拾取。";session.Save();
        }
        public void PickUp(string id)
        {
            var q=State.quest;if(q==null||q.id!=id||!q.ready||!q.accepted||q.picked||q.completed||session.InputBlocked||session.CurrentPlace.ToString()!=q.scene)return;
            if(Mathf.Abs(World.Player.position.x-q.itemX)>1.8f)return;
            q.picked=true;notice="已经拿到物品。把它放进「"+q.container+"」。";session.Save();SpawnProps();
        }
        public void Deposit(string id)
        {
            var q=State.quest;if(q==null||q.id!=id||!q.accepted||!q.picked||q.completed||session.InputBlocked||session.CurrentPlace.ToString()!=q.scene)return;
            if(Mathf.Abs(World.Player.position.x-q.targetX)>1.8f)return;
            if(!State.CreditTask(q.actor,q.id,StoryPlaytest.Settings))return;
            q.completed=true;notice=SevenCharacterCatalog.Name(q.actor)+" · 委托完成 +"+StoryPlaytest.Settings.taskPoints;
            Memory?.PublishStoryEvent("complete_"+q.id,"玩家实际捡起"+q.item+"，并放入"+q.container+"，完成了「"+q.title+"」。",new[]{"player",q.actor},true);
            ClearProps();session.Clock.CompleteStoryTaskPeriod();session.Save();
        }
        IEnumerator LoadArtwork(StoryPlaytestQuest q)
        {
            loadingArtwork=true;Texture2D first=null,second=null;
            foreach(var kind in new[]{"item","container"})
            {
                string path=kind=="item"?q.itemUrl:q.containerUrl;
                if(string.IsNullOrEmpty(path)){notice="缺少物体图片地址。";loadingArtwork=false;yield break;}
                using(var req=UnityWebRequestTexture.GetTexture(MemoryRuntimeConfig.Settings.serviceUrl+path))
                {
                    req.timeout=25;yield return req.SendWebRequest();
                    if(State.quest!=q){loadingArtwork=false;yield break;}
                    if(req.result!=UnityWebRequest.Result.Success){q.error="物体图片下载失败，请恢复本机服务后重试。";notice=q.error;loadingArtwork=false;yield break;}
                    var tex=DownloadHandlerTexture.GetContent(req);if(kind=="item")first=tex;else second=tex;
                }
            }
            ReleaseTextures();itemTexture=first;containerTexture=second;textureQuest=q.id;loadingArtwork=false;SpawnProps();
        }
        void SpawnProps()
        {
            ClearProps();var q=State.quest;if(q==null||!q.ready||q.completed||q.scene!=session.CurrentPlace.ToString()||itemTexture==null||containerTexture==null)return;
            propScene=World.Stage;props=new GameObject("Generated story quest "+q.id);props.transform.SetParent(propScene.transform,false);
            if(!q.picked)MakeProp(itemTexture,q.itemX,false,q);
            else {carried=new GameObject("Carried quest item");carried.transform.SetParent(props.transform);var r=carried.AddComponent<SpriteRenderer>();r.sprite=MakeSprite(itemTexture,.45f,.5f);r.sortingOrder=100;}
            MakeProp(containerTexture,q.targetX,true,q);
        }
        void MakeProp(Texture2D texture,float x,bool target,StoryPlaytestQuest q)
        {
            var obj=new GameObject(target?q.container:q.item);obj.transform.SetParent(props.transform,false);obj.transform.position=new Vector3(x,.08f,0);
            var r=obj.AddComponent<SpriteRenderer>();r.sprite=MakeSprite(texture,target?1.1f:.75f,target?1.4f:.85f);r.sortingOrder=36;
            var marker=obj.AddComponent<StoryQuestProp>();marker.id=q.id;marker.target=target;marker.runtime=this;marker.radius=1.8f;marker.promptOffset=new Vector3(0,target?1.6f:1.1f,0);World.Interactables.Add(marker);
        }
        static Sprite MakeSprite(Texture2D texture,float width,float height)
        {
            var pixels=texture.GetPixels32();int left=texture.width,right=-1,bottom=texture.height,top=-1;
            for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)if(pixels[y*texture.width+x].a>24){left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
            if(right<left)throw new InvalidOperationException("Generated image is completely transparent.");
            var rect=new Rect(left,bottom,right-left+1,top-bottom+1);return Sprite.Create(texture,rect,new Vector2(.5f,0),Mathf.Max(rect.width/width,rect.height/height));
        }
        void ClearProps()
        {
            if(World!=null)World.Interactables.RemoveAll(p=>p is StoryQuestProp);
            if(props!=null)Destroy(props);props=carried=null;propScene=null;
        }
        void ReleaseTextures(){if(itemTexture!=null)Destroy(itemTexture);if(containerTexture!=null)Destroy(containerTexture);itemTexture=containerTexture=null;textureQuest=null;}
        string MediaPath(string id)
        {
            string root=Environment.GetEnvironmentVariable("Z_STORY_MEDIA_ROOT");
            if(string.IsNullOrEmpty(root))root=Path.Combine(Application.streamingAssetsPath,"StoryPlaytest");
            return Path.Combine(root,id+".mp4");
        }
        public void StartMovie(string id)
        {
            if(video!=null)return;clipId=id;mediaOwner=State;mediaError="";mediaStarted=Time.unscaledTime;
            Memory?.Conversations.CancelAll("剧情播放");session.SetOverlay(true);
            var go=new GameObject("Story film player");video=go.AddComponent<VideoPlayer>();video.playOnAwake=false;video.isLooping=false;video.skipOnDrop=true;
            videoTexture=new RenderTexture(1280,720,0);video.targetTexture=videoTexture;video.renderMode=VideoRenderMode.RenderTexture;
            video.audioOutputMode=VideoAudioOutputMode.Direct;video.url=MediaPath(id);
            video.errorReceived+=(v,e)=>{mediaError="影片暂时无法播放，可以重试。";Debug.LogWarning(e);};
            video.prepareCompleted+=v=>v.Play();video.loopPointReached+=v=>FinishMovie();
            if(!File.Exists(video.url)){mediaError="没有找到本地影片："+id;return;}video.Prepare();
        }
        public void FinishMovie()
        {
            if(video==null||mediaOwner!=State||!string.IsNullOrEmpty(mediaError))return;
            string id=clipId;StopMovie();
            if(id=="night_live")
            {
                string receipt=State.liveId;State.livePending=false;session.Clock.CompleteLiveSession(receipt);notice="直播结束。新的一天，可以继续选择和谁相处。";
            }
            else
            {
                State.FinishClip(id);notice=State.endingSeen?"这一局，走到了"+SevenCharacterCatalog.Name(State.winner)+"的结局。":"故事已经记下，继续认识身边的人。";
                if(clips.TryGetValue(id,out var c))Memory?.PublishStoryEvent("film_"+id,"玩家观看了「"+c.title+"」："+string.Join(" ",c.lines),new[]{"player"},true);
            }
            session.Save();session.NotifyChanged();
        }
        void StopMovie(){if(video!=null){video.Stop();Destroy(video.gameObject);}video=null;if(videoTexture!=null){videoTexture.Release();Destroy(videoTexture);}videoTexture=null;clipId=null;session?.SetOverlay(false);}
        void Styles()
        {
            if(label!=null)return;font=Resources.Load<Font>("Fonts/NotoSansSC-Regular");
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=18,wordWrap=true,normal={textColor=Color.white}};
            small=new GUIStyle(label){fontSize=14,normal={textColor=muted}};title=new GUIStyle(label){fontSize=25,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=17,wordWrap=true,padding=new RectOffset(12,12,8,8)};
        }
        void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        public bool QuestAvailable(string id,bool target)=>State.quest?.id==id&&State.quest.accepted&&!State.quest.completed&&State.winner==""&&(!State.quest.picked||target);
        public string QuestPrompt(bool target)=>target?(State.quest.picked?"放入 "+State.quest.container:"需要先拾取 "+State.quest.item):"拾起 "+State.quest.item;
        void OnGUI()
        {
            if(session==null||!session.IsReady)return;Styles();var previous=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1440f,Screen.height/900f,1));
            if(video!=null)
            {
                Fill(new Rect(0,0,1440,900),Color.black);
                if(videoTexture!=null&&video.isPrepared)GUI.DrawTexture(new Rect(80,90,1280,720),videoTexture,ScaleMode.ScaleToFit);
                GUI.Label(new Rect(70,28,1200,44),clipId=="night_live"?"第 "+session.State.day+" 天 · 夜间直播":clips.TryGetValue(clipId,out var c)?c.title:"人物故事",title);
                if(!string.IsNullOrEmpty(mediaError))
                {GUI.Label(new Rect(400,400,650,90),mediaError,label);if(GUI.Button(new Rect(570,530,280,48),"重试播放",button)){string id=clipId;StopMovie();StartMovie(id);}}
                else if(!video.isPrepared)GUI.Label(new Rect(620,425,300,50),"正在准备影片…",label);
                else if(Time.unscaledTime-mediaStarted>1&&GUI.Button(new Rect(1180,836,195,42),clipId=="night_live"?"结束直播 · 到明天":"跳过本片",button))FinishMovie();
                GUI.Label(new Rect(70,848,1000,30),"剧情样片 · 用于验证故事节奏，正式演出可替换",small);GUI.matrix=previous;return;
            }
            if(GameUI.Instance!=null&&GameUI.Instance.HasOverlay){GUI.matrix=previous;return;}
            var actor=Memory?.Conversations.PlayerActor;
            if(actor!=null)selected=actor.npcId;
            if(actor!=null)
            {
                Fill(new Rect(30,664,540,188),ink);
                GUI.Label(new Rect(50,680,495,38),SevenCharacterCatalog.Name(selected)+" · 故事 "+State.Person(selected).points+" / "+StoryPlaytest.Settings.maximum,title);
                GUI.Label(new Rect(50,723,495,36),"结束完整交流可获得聊天分，每时段最多一次。",small);
                GUI.enabled=!preparing&&State.winner==""&&(State.quest==null||State.quest.completed||!string.IsNullOrEmpty(State.quest.error));
                if(GUI.Button(new Rect(50,768,495,47),preparing?"正在准备委托…":"请"+SevenCharacterCatalog.Name(selected)+"提个小委托",button))RequestQuest(selected);
                GUI.enabled=true;GUI.matrix=previous;return;
            }
            float x=1040,y=112;Fill(new Rect(x-15,y-12,392,690),ink);
            GUI.Label(new Rect(x,y,350,40),"小岛 · 人物故事",title);y+=43;
            GUI.Label(new Rect(x,y,360,46),"第 "+session.State.day+" 天 · "+GameClock.PeriodName(session.Clock.Period)+"\n任务 +"+StoryPlaytest.Settings.taskPoints+" · 聊天 +"+StoryPlaytest.Settings.chatPoints+" · 满分进入结局",small);y+=58;
            if(GUI.Button(new Rect(x,y,350,32),showPeople?"收起人物进度 ▴":"展开人物进度 ▾",button))showPeople=!showPeople;y+=39;
            if(showPeople)foreach(var person in SevenCharacterCatalog.Actors)
            {
                int score=State.people.Find(p=>p.actor==person.id)?.points??0;
                GUI.Label(new Rect(x,y,84,27),person.displayName,label);Fill(new Rect(x+92,y+8,165,9),new Color(.2f,.25f,.3f));Fill(new Rect(x+92,y+8,165*score/(float)StoryPlaytest.Settings.maximum,9),gold);
                GUI.Label(new Rect(x+270,y,90,26),score+" / "+StoryPlaytest.Settings.maximum,small);y+=30;
            }
            y+=10;var q=State.quest;
            if(State.endingSeen)
            {
                GUI.Label(new Rect(x,y,350,65),"本局结局："+SevenCharacterCatalog.Name(State.winner),title);y+=80;
                if(GUI.Button(new Rect(x,y,350,45),"开始新一局（保留影片文件）",button)){session.NewGame();notice="新的一局开始了。";}
            }
            else if(q!=null&&!q.completed)
            {
                GUI.Label(new Rect(x,y,350,54),q.title,title);y+=58;
                GUI.Label(new Rect(x,y,350,90),q.request,label);y+=94;
                float delta=(q.picked?q.targetX:q.itemX)-(World.Player!=null?World.Player.position.x:0);
                GUI.Label(new Rect(x,y,350,44),!q.ready?"资产准备中…":!q.accepted?"委托已准备好":(q.picked?"② 放入容器":"① 拾起物品")+" · "+(delta>=0?"右侧 ":"左侧 ")+Mathf.Abs(delta).ToString("0.0")+" 米 · E",label);y+=48;
                if(q.ready&&!q.accepted&&GUI.Button(new Rect(x,y,170,43),"接受委托",button))AcceptQuest();
                if(!preparing&&GUI.Button(new Rect(x+(q.ready&&!q.accepted?180:0),y,170,43),q.error!=""?"重新尝试":"暂不做了",button)){State.quest=null;ClearProps();ReleaseTextures();session.Save();}
            }
            else
            {
                GUI.Label(new Rect(x,y,350,65),"和岛民说说话，请 TA 提一个适合当下的小委托。",label);y+=78;
                GUI.enabled=actor!=null&&!preparing&&State.winner=="";
                if(GUI.Button(new Rect(x,y,350,46),preparing?"正在准备委托…":actor!=null?"请"+SevenCharacterCatalog.Name(selected)+"提个委托":"先走近岛民 · 按 E 交谈",button))RequestQuest(selected);
                GUI.enabled=true;
            }
            GUI.Label(new Rect(1050,818,350,65),notice,small);
            GUI.matrix=previous;
        }
        void OnDestroy(){StopMovie();ClearProps();ReleaseTextures();if(Instance==this)Instance=null;}
    }
    public sealed class StoryQuestProp : Interactable
    {
        public StoryPlaytestRuntime runtime;public bool target;
        public override bool Available=>runtime!=null&&runtime.QuestAvailable(id,target);
        public override string Prompt=>runtime.QuestPrompt(target);
        public override void Activate(){if(!Available)return;if(target)runtime.Deposit(id);else runtime.PickUp(id);}
    }
}
