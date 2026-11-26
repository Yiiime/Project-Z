using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    /// <summary>Local, authored films and rolling credits. The journey owns the gameplay input lock.</summary>
    public sealed class FirstDayCinema : MonoBehaviour
    {
        public bool IsPlaying { get; private set; }
        public string CurrentFilm { get; private set; }
        GameSession session;
        RectTransform root;
        EndingPresentationView ending;
        CanvasGroup group;
        GameObject modal;
        RawImage picture;
        TextMeshProUGUI hint, status;
        VideoPlayer player;
        AudioSource filmAudio;
        RenderTexture target;
        Action finished;
        float previousScale, creditsTime, lastProgressAt;
        double lastVideoTime;
        bool completed, inCredits, failed, confirming, playbackStarted;
        readonly System.Collections.Generic.Dictionary<AudioSource,bool> mutedWorld = new System.Collections.Generic.Dictionary<AudioSource,bool>();
        Coroutine playback;
        readonly Color paper = new Color(.94f, .94f, .90f);
        readonly Color muted = new Color(.64f, .73f, .79f);

        public void Initialize(GameSession game) { session = game; }
        public void PlayIntro(Action after) => PlayFilm("intro", "来自月亮的 Z", after);
        public void PlayArrival(Action after) => PlayFilm("arrival", "第一章 · 海风送来一封邀请", after);
        public void PlayFireworks(Action after) => PlayFilm("fireworks", "第一夜 · 和你一起看烟花", after);
        public void PlayCredits(Action after)
        {
            if (IsPlaying) return;
            Begin("credits", after); inCredits = true;
            ending = root.gameObject.AddComponent<EndingPresentationView>();
            ending.Initialize(root, session?.State?.phone);
            hint = Label("Credit controls",root,Vector2.zero,Vector2.zero,new Vector2(54,32),new Vector2(1100,30),"空格 · 加快片尾     Esc · 返回确认",17,muted);
            var finishButton = UIPrimitives.Button("Leave credits",root,new Vector2(1,0),new Vector2(1,0),new Vector2(-48,26),new Vector2(220,48),"回到小岛 →",ConfirmSkip,false,19);
            finishButton.GetComponent<Image>().color = new Color(.10f,.16f,.22f,.7f);
        }

        void PlayFilm(string id, string title, Action after)
        {
            if (IsPlaying) return;
            Begin(id,after);
            picture = Stretch("Film image",root,Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
            picture.color = Color.white; picture.raycastTarget = false;
            var fit = picture.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 16f/9;
            status = Label("Preparing film",root,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(900,100),title,30,paper,TextAlignmentOptions.Center);
            hint = Label("Film controls",root,Vector2.zero,Vector2.zero,new Vector2(54,30),new Vector2(1000,30),"Esc · 跳过确认     P · 留住这一刻",17,muted);
            var skip = UIPrimitives.Button("Skip film",root,new Vector2(1,0),new Vector2(1,0),new Vector2(-48,24),new Vector2(180,48),"跳过影片",ConfirmSkip,false,18);
            skip.GetComponent<Image>().color = new Color(.06f,.10f,.16f,.70f);
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false; player.isLooping = false; player.waitForFirstFrame = true;
            player.skipOnDrop = true; player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.renderMode = VideoRenderMode.RenderTexture; player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            filmAudio = gameObject.AddComponent<AudioSource>(); filmAudio.playOnAwake = false; filmAudio.ignoreListenerPause = true; filmAudio.spatialBlend = 0; filmAudio.volume = .64f;
            player.controlledAudioTrackCount = 1; player.EnableAudioTrack(0,true); player.SetTargetAudioSource(0,filmAudio);
            target = new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32) { name="First-day film", hideFlags=HideFlags.DontSave };
            target.Create(); player.targetTexture = target; picture.texture = target;
            player.loopPointReached += EndOfFilm; player.errorReceived += FilmError;
            player.source = VideoSource.VideoClip;
            player.clip = Resources.Load<VideoClip>("FirstDay/Video/"+id);
            playback = StartCoroutine(PrepareFilm());
        }

        void Begin(string id, Action after)
        {
            foreach(var audio in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if(!audio.ignoreListenerPause) { mutedWorld[audio] = audio.mute; audio.mute = true; }
            IsPlaying = true; CurrentFilm = id; completed = failed = confirming = inCredits = playbackStarted = false;
            finished = after; previousScale = Time.timeScale; Time.timeScale = 0; creditsTime = 0;
            var go = new GameObject("First-day cinema",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(CanvasGroup));
            go.transform.SetParent(transform,false); root = (RectTransform)go.transform;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 550;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            group = go.GetComponent<CanvasGroup>();
            var backdrop = Stretch("Cinema black",root,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();
            backdrop.color = new Color(.018f,.035f,.065f); backdrop.raycastTarget = true;
        }

        IEnumerator PrepareFilm()
        {
            failed = false; confirming = false; playbackStarted = false; CloseModal();
            if (status != null) status.gameObject.SetActive(true);
            player.Stop(); if (player.clip == null) { ShowFailure("影片资源暂时不可用。"); yield break; } player.Prepare();
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!player.isPrepared && !failed && Time.realtimeSinceStartup < deadline) yield return null;
            if (failed) yield break;
            if (!player.isPrepared) { ShowFailure("影片暂时没有准备好。"); yield break; }
            // Preparation may finish while a skip dialog is open. Do not start audio
            // behind that dialog; Cancel destroys this coroutine instead of resuming it.
            while (confirming && !failed && IsPlaying) yield return null;
            if (failed || !IsPlaying || player == null) yield break;
            if (status != null) status.gameObject.SetActive(false);
            lastVideoTime = -1; lastProgressAt = Time.realtimeSinceStartup;
            playbackStarted = true;
            player.Play();
        }

        void Update()
        {
            if (!IsPlaying) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (confirming && !failed) Resume(); else if (!failed) ConfirmSkip();
                return;
            }
            if (confirming || failed) return;
            if (inCredits)
            {
                creditsTime += Time.unscaledDeltaTime * (keyboard != null && keyboard.spaceKey.isPressed ? 3 : 1);
                ending?.Render(creditsTime);
                if (creditsTime >= EndingPresentationView.Duration) Finish();
            }
            // A native decoder can stop without reporting an error or end event.
            // Once playback was requested, watch progress even if isPlaying becomes false.
            else if (player != null && playbackStarted)
            {
                if (player.time > lastVideoTime+.01) { lastVideoTime=player.time; lastProgressAt=Time.realtimeSinceStartup; }
                else if (Time.realtimeSinceStartup-lastProgressAt > 10) ShowFailure("影片播放暂时中断。");
            }
        }

        void EndOfFilm(VideoPlayer source) { if (player != null && source == player && !failed) Finish(); }
        void FilmError(VideoPlayer source,string message)
        {
            if (player == null || source != player || !IsPlaying) return;
            Debug.LogWarning("First-day film could not play: "+message); ShowFailure("影片暂时无法播放。");
        }
        void ConfirmSkip()
        {
            if (!IsPlaying || confirming || failed) return;
            confirming = true; if (player != null && player.isPlaying) player.Pause();
            ShowModal(inCredits ? "要结束片尾吗？" : "跳过这段影片？",inCredits ? "每一份陪伴，都让这座小岛多了一点温度。" : "故事会继续；这段相遇已经成为第一天的回忆。",inCredits ? "回到小岛" : "继续故事",Finish,"继续观看",Resume);
        }
        void ShowFailure(string reason)
        {
            if (!IsPlaying || failed) return;
            failed = true; confirming = true; if (player != null) player.Pause();
            if (status != null) status.gameObject.SetActive(false);
            ShowModal(reason,"可以重试，也可以继续故事。你的第一天不会丢失。","重试影片",Retry,"继续故事",Finish);
        }
        void Retry() { if (playback != null) StopCoroutine(playback); playback=StartCoroutine(PrepareFilm()); }
        void Resume() { CloseModal(); confirming=false; lastProgressAt=Time.realtimeSinceStartup; if (player != null && player.isPrepared) player.Play(); }
        void ShowModal(string title,string body,string primaryText,Action primary,string secondaryText,Action secondary)
        {
            CloseModal();
            var shade = Stretch("Cinema confirmation",root,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>(); shade.color=new Color(.01f,.025f,.05f,.65f); shade.raycastTarget=true; modal=shade.gameObject;
            var card = Panel("Film dialog",shade.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(700,360),new Color(.055f,.10f,.16f,.98f),true);
            Label("Heading",card.transform,Vector2.up,Vector2.up,new Vector2(42,-38),new Vector2(616,50),title,30,paper);
            Label("Explanation",card.transform,Vector2.up,Vector2.up,new Vector2(42,-112),new Vector2(616,110),body,22,muted);
            UIPrimitives.Button("Secondary",card.transform,Vector2.zero,Vector2.zero,new Vector2(42,34),new Vector2(278,58),secondaryText,secondary);
            UIPrimitives.Button("Primary",card.transform,Vector2.zero,Vector2.zero,new Vector2(346,34),new Vector2(310,58),primaryText,primary,true);
        }
        void CloseModal() { if (modal != null) { modal.SetActive(false); Destroy(modal); modal=null; } }
        void Finish()
        {
            if (!IsPlaying || completed) return;
            completed=true; var callback=finished; finished=null;
            Cleanup(); callback?.Invoke();
        }
        public void Cancel() { finished=null; Cleanup(); }
        void Cleanup()
        {
            if (playback != null) { StopCoroutine(playback); playback=null; }
            if (player != null) { player.loopPointReached-=EndOfFilm; player.errorReceived-=FilmError; player.Stop(); player.targetTexture=null; Destroy(player); player=null; }
            if (filmAudio != null) { Destroy(filmAudio); filmAudio=null; }
            if (target != null) { target.Release(); Destroy(target); target=null; }
            if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); root=null; }
            foreach(var entry in mutedWorld) if(entry.Key != null) entry.Key.mute = entry.Value; mutedWorld.Clear();
            if (IsPlaying) Time.timeScale=previousScale;
            IsPlaying=false; playbackStarted=false; CurrentFilm=null; ending=null; modal=null; picture=null; status=null; hint=null;
        }
        void OnDestroy() { Cancel(); }
    }
}
