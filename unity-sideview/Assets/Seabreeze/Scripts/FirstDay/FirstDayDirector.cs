using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seabreeze
{
    // The host owns durable facts. The main Agent chooses when to use available scene tools.
    public sealed class FirstDayDirector : MonoBehaviour
    {
        public static readonly string[] FirstDayCast = { "z", "xiaoxiaoqing", "linxu", "zhengyi", "liting", "axing", "zhou_bobo" };
        GameSession session; WorldDirector world; FirstDayOverlay invitations; NpcSpeechBubble bubble;
        NativeLiveHost live; FirstDayEscort escort; FirstDayCinema cinema;
        bool titleFilmStarted, titleFilmFinished, filmRoutine, preparingIntroduction;
        public bool FilmActive => cinema != null && cinema.IsPlaying;
        public bool PreparingHarbor => filmRoutine && Phase == FirstDayPhase.Harbor;
        const int RequiredGreetings = 3;
        public void BeginTitleFilm()
        {
            titleFilmStarted = true; titleFilmFinished = false;
            cinema.PlayIntro(() => titleFilmFinished = true);
        }
        IEnumerator Intro()
        {
            if (filmRoutine) yield break;
            filmRoutine = true;
            if (!titleFilmStarted) BeginTitleFilm();
            OpenLive(true);
            while (!titleFilmFinished) yield return null;
            session.State.firstDay.introSeen = true; session.Save();
            live?.Reveal(); filmRoutine = false;
        }
        readonly FirstDayGathering gathering = new FirstDayGathering();
        public bool GatheringActive => session != null && !session.IsAtTitle && Phase == FirstDayPhase.MeetIslanders && session.CurrentPlace == PlaceId.Square;
        bool busy, planning, harborOpening, receiptPending;
        GameObject nightFade, fireworksFade;
        float nextPlan, nextIntroduction; int generation;
        readonly Dictionary<string, string> completedTalks = new Dictionary<string, string>();
        readonly HashSet<string> pendingSteps = new HashSet<string>();
        public bool BlocksInput => busy;
        public bool Active => session?.State?.firstDay != null && Phase != FirstDayPhase.None && Phase != FirstDayPhase.Complete;
        public FirstDayPhase Phase => session?.State?.firstDay?.phase ?? FirstDayPhase.None;
        public string[] Cast => FirstDayCast;
        public int GreetedCount => session?.State?.firstDay?.greeted.Count(Cast.Contains) ?? 0;
        public string Objective
        {
            get
            {
                if (session.Clock.PendingNightRest) return "走到床边按 E，休息一夜";
                if (!Active) return "今日休息 " + session.Clock.RestActions + "/4";
                if (Phase == FirstDayPhase.MeetIslanders)
                {
                    if (!session.State.firstDay.announced) return "稍等 Z 向大家介绍你";
                    return "认识朋友 " + Mathf.Min(GreetedCount, RequiredGreetings) + "/3 · 与任意三位朋友交流";
                }
                if (Phase == FirstDayPhase.Live) return session.Memory?.Client.CanGenerate == true ? "Z 正在准备直播，请稍候" : "连接暂未就绪，正在重试 · 请确认小岛主机在线";
                if (Phase == FirstDayPhase.Harbor) return session.State.firstDay.harborReady ? "等 Z 邀请你去市民广场" : "和 Z 打个招呼 · 输入回应，听完后关闭对话";
                if (Phase == FirstDayPhase.WalkToSquare) return "跟随 Z 向右前往市民广场 · A / D 或方向键移动";
                if (Phase == FirstDayPhase.WelcomeTask) return string.IsNullOrEmpty(session.State.firstDay.objective) ? "走近一位朋友按 E，聊聊能一起做的小事" : session.State.firstDay.objective;
                if (Phase == FirstDayPhase.EveningReady) return "迎新小事完成了 · 等待 Z 的晚间直播";
                return "继续今天的小岛生活";
            }
        }
        public void Initialize(GameSession owner, WorldDirector director)
        {
            session = owner; world = director;
            invitations = gameObject.AddComponent<FirstDayOverlay>(); invitations.Initialize();
            cinema = gameObject.AddComponent<FirstDayCinema>(); cinema.Initialize(owner);
            escort = gameObject.AddComponent<FirstDayEscort>(); escort.Initialize(owner, director);
        }
        public JObject Context()
        {
            var state = session.State.firstDay;
            return new JObject {
                ["phase"] = !Active ? session.Clock.LiveReady ? "DailyReady" : "Complete" : Phase.ToString(),
                ["version"] = (state?.stageVersion ?? 0) + session.State.day * 1000,
                ["playerName"] = PlayerIdentity.DisplayName, ["greeted"] = new JArray(state?.greeted ?? new List<string>()),
                ["announced"] = state?.announced ?? false, ["openingStarted"] = state?.openingStarted ?? false,
                ["harborReady"] = state?.harborReady ?? false, ["restActions"] = session.Clock.RestActions,
                ["currentActor"] = session.Memory?.Conversations.PlayerActor?.npcId,
                ["conversation"] = session.Memory?.Conversations.ActiveId ?? "",
                ["place"] = session.CurrentPlace.ToString(),
                ["photoIds"] = new JArray(session.State.phone?.photos.Select(p => p.id) ?? Enumerable.Empty<string>())
            };
        }
        public void ResetForStateChange()
        {
            if (nightFade != null) Destroy(nightFade); nightFade = null;
            if (fireworksFade != null) Destroy(fireworksFade); fireworksFade = null;
            if (world?.Follow != null) world.Follow.target = world.Player;
            cinema?.Cancel(); if (session.Memory?.View != null) session.Memory.View.CinemaHidden = false; filmRoutine = false; titleFilmStarted = titleFilmFinished = preparingIntroduction = false;
            gathering.Reset(); generation++; StopAllCoroutines(); invitations.Hide(); bubble?.Clear();
            if (live != null) { live.Close(); live = null; }
            busy = planning = harborOpening = receiptPending = false;
            completedTalks.Clear(); pendingSteps.Clear(); escort.ResetRoute();
            session.Memory?.View.ResetView(); nextPlan = nextIntroduction = 0;
        }
        public void ResumeAfterTravel() { StartCoroutine(ResumeWhenReady()); }
        IEnumerator ResumeWhenReady() { yield return null; while (world.IsTransitioning) yield return null; Resume(); }
        public void Resume()
        {
            if (session.IsAtTitle || filmRoutine) return;
            if (session.Clock.PendingNightRest) { if (session.CurrentPlace != PlaceId.PlayerHome && !busy) ReturnHomeAfterLive(); return; }
            if (Phase == FirstDayPhase.Live && !session.State.firstDay.introSeen) { StartCoroutine(Intro()); return; }
            if (Phase == FirstDayPhase.Arrival || Phase == FirstDayPhase.Harbor && !session.State.firstDay.arrivalFilmSeen) StartCoroutine(Arrive());
            else if (Phase == FirstDayPhase.Fireworks) StartCoroutine(Fireworks());
            else if (Phase == FirstDayPhase.EveningLive) OpenLive(false);
            else if (Phase == FirstDayPhase.Live && session.State.firstDay.openingStarted) OpenLive(true);
            nextPlan = 0;
        }
        void SetPhase(FirstDayPhase phase)
        {
            var state = session.State.firstDay; state.phase = phase; state.stageVersion++;
            state.objective = ""; state.agentRequest = ""; planning = false; nextPlan = 0;
            session.NotifyChanged(); session.Save();
        }
        void Update()
        {
            if (session == null || !session.IsReady || session.IsAtTitle || world == null || world.Stage == null || world.IsTransitioning) return;
            gathering.Tick(session, world);
            bubble?.Tick(session.Memory?.PlayerChatOpen ?? false);
            if (!busy && Phase == FirstDayPhase.Harbor && !harborOpening && !session.State.firstDay.harborOpened && session.Memory?.Spawner.Find("z") != null)
                StartCoroutine(OpenHarborConversation());
            if (Phase == FirstDayPhase.WalkToSquare && !preparingIntroduction && Time.unscaledTime >= nextIntroduction && string.IsNullOrEmpty(session.State.firstDay.preparedIntroduction)) PrepareIntroduction();
            if (Phase == FirstDayPhase.MeetIslanders && GreetedCount >= RequiredGreetings && !busy && !session.Memory.PlayerChatOpen)
            { SetPhase(FirstDayPhase.Fireworks); StartCoroutine(Fireworks()); return; }
            if (Phase == FirstDayPhase.WalkToSquare && session.CurrentPlace == PlaceId.Square)
            {
                SetPhase(FirstDayPhase.MeetIslanders);
                session.State.period = (int)TimePeriod.Afternoon; session.Clock.Refresh(); session.Memory?.Spawner.Reload();
                if (!string.IsNullOrEmpty(session.State.firstDay.preparedIntroduction)) StartCoroutine(Announce(session.State.firstDay.preparedIntroduction));
            }
            if (Phase == FirstDayPhase.WelcomeTask) ObserveTask();
            if (busy || planning || preparingIntroduction || live != null || invitations.Visible || (session.Memory?.PlayerChatOpen ?? false) || GameUI.Instance.HasOverlay || Time.unscaledTime < nextPlan) return;
            bool wantsPlan = Phase == FirstDayPhase.Live || Phase == FirstDayPhase.Harbor && session.State.firstDay.harborReady ||
                Phase == FirstDayPhase.MeetIslanders && (!session.State.firstDay.announced || GreetedCount >= RequiredGreetings) ||
                Phase == FirstDayPhase.EveningReady || !Active && session.Clock.LiveReady;
            if (wantsPlan && session.Memory.Spawner.IsStageReady(world.Stage) && (session.Memory?.Client.CanGenerate ?? false)) RequestPlan();
        }
        void PrepareIntroduction()
        {
            if (!(session.Memory?.Client.CanGenerate ?? false)) return;
            preparingIntroduction = true; nextIntroduction = Time.unscaledTime + 8; var owner = session.State; int stamp = generation;
            var request = session.Memory.Client.RequestBase(); request.requestId = "intro_" + Guid.NewGuid().ToString("N");
            session.Memory.Client.Generate("/api/game/story/plan", request, reply => {
                if (owner != session.State || stamp != generation) return;
                preparingIntroduction = false;
                if (Phase != FirstDayPhase.WalkToSquare && Phase != FirstDayPhase.MeetIslanders) return;
                if ((string)reply.storyPlan?["tool"] != "announce_player") return;
                owner.firstDay.preparedIntroduction = (string)reply.storyPlan["speech"]; session.Save();
                if (Phase == FirstDayPhase.MeetIslanders && !owner.firstDay.announced && !busy) StartCoroutine(Announce(owner.firstDay.preparedIntroduction));
            }, _ => { if (owner == session.State && stamp == generation) preparingIntroduction = false; });
        }
        void RequestPlan()
        {
            planning = true; nextPlan = Time.unscaledTime + 8;
            int stamp = generation; var owner = session.State; var phase = Phase; int version = owner.firstDay?.stageVersion ?? 0;
            var request = session.Memory.Client.RequestBase();
            if (Active)
            {
                if (string.IsNullOrEmpty(owner.firstDay.agentRequest)) owner.firstDay.agentRequest = Guid.NewGuid().ToString("N");
                request.requestId = owner.firstDay.agentRequest; session.Save();
            }
            session.Memory.Client.Generate("/api/game/story/plan", request, reply =>
            {
                if (stamp != generation || owner != session.State) return;
                planning = false;
                if (Active) owner.firstDay.agentRequest = "";
                if (Phase != phase || (owner.firstDay?.stageVersion ?? 0) != version || session.Memory.PlayerChatOpen || world.IsTransitioning || busy || GameUI.Instance.HasOverlay) return;
                var plan = reply.storyPlan; string tool = (string)plan?["tool"];
                if (tool == "open_live") { if (Phase == FirstDayPhase.Live) OpenLive(true); else if (Phase == FirstDayPhase.EveningReady || session.Clock.LiveReady) StartCoroutine(NotifyLive()); }
                else if (tool == "offer_walk" && Phase == FirstDayPhase.Harbor) OfferWalk((string)plan["title"], (string)plan["description"]);
                else if (tool == "announce_player" && Phase == FirstDayPhase.MeetIslanders && !owner.firstDay.announced) StartCoroutine(Announce((string)plan["speech"]));
                else if (tool == "show_fireworks" && Phase == FirstDayPhase.MeetIslanders && GreetedCount >= RequiredGreetings) { SetPhase(FirstDayPhase.Fireworks); StartCoroutine(Fireworks()); }
            }, error => { if (stamp != generation || owner != session.State) return; planning = false; session.Story.Notify(error); });
        }
        void OfferWalk(string title, string description)
        {
            busy = true; session.State.firstDay.objective = title; session.Save();
            invitations.Show(title, description, "确认", () => {
                busy = false; SetPhase(FirstDayPhase.WalkToSquare);
                session.Memory.PublishStoryEvent("walk_accepted", PlayerIdentity.DisplayName + "接受了 Z 一起去市民广场的邀请。", new[] { "player", "z" }, true);
            }, () => { busy = false; nextPlan = Time.unscaledTime + 8; });
        }
        IEnumerator Announce(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            busy = true;
            while (!session.Memory.Spawner.IsStageReady(world.Stage)) yield return null;
            if (bubble == null) { bubble = gameObject.AddComponent<NpcSpeechBubble>(); bubble.Initialize(invitations.Surface, session.Memory, world); }
            bubble.Show("z", text);
            if (!bubble.Visible) { busy = false; yield break; }
            float end = Time.unscaledTime + NpcSpeechBubble.ReadDuration(text);
            while (Time.unscaledTime < end) { bubble.Tick(false); yield return null; }
            var audience = Cast.Concat(new[] { "player" }).ToArray();
            session.Memory.PublishStoryEvent("player_introduced", "Z 陪" + PlayerIdentity.DisplayName + "从码头来到市民广场，并向在场的朋友介绍这位新朋友：" + text, audience, true);
            session.State.firstDay.announced = true; session.State.firstDay.stageVersion++; session.Save(); busy = false;
        }
        IEnumerator OpenHarborConversation()
        {
            harborOpening = true; yield return null;
            if (Phase != FirstDayPhase.Harbor) { harborOpening = false; yield break; }
            session.Memory.BeginPlayerChat("z");
            if (session.Memory.Conversations.PlayerActor != null) session.State.firstDay.harborOpened = true;
            session.Save(); harborOpening = false;
        }
        public void ConversationCompleted(string actor, string conversation, string evidence)
        {
            completedTalks[actor] = evidence;
            if (Phase == FirstDayPhase.Harbor && actor == "z") { session.State.firstDay.harborReady = true; session.Save(); nextPlan = 0; }
            if (Phase == FirstDayPhase.MeetIslanders && session.State.firstDay.announced && Cast.Contains(actor) && !session.State.firstDay.greeted.Contains(actor))
            {
                session.State.firstDay.greeted.Add(actor); session.State.firstDay.stageVersion++;
                session.Memory.PublishStoryEvent("greeted_" + actor, PlayerIdentity.DisplayName + "在市民广场与" + SevenCharacterCatalog.Name(actor) + "完成了一轮初次交流。", new[] { "player", actor }, true);
                session.NotifyChanged(); session.Save(); nextPlan = 0;
            }
        }
        public void BeginTask(bool accepted)
        {
            if (!accepted || Phase != FirstDayPhase.WelcomeTask) return;
            completedTalks.Clear(); pendingSteps.Clear();
            session.State.firstDay.taskPhotoIds = session.State.phone.photos.Select(p => p.id).ToList(); session.Save();
        }
        void ObserveTask()
        {
            if (receiptPending || !session.Memory.Client.Synchronized || string.IsNullOrEmpty(session.State.memory.checkpoint)) return;
            JObject cp; try { cp = JObject.Parse(session.State.memory.checkpoint); } catch { return; }
            var definitions = (cp["tasks"]?["definitions"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
            var rows = (cp["tasks"]?["state"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
            foreach (var definition in definitions)
            {
                string id = (string)definition["id"]; var row = rows.FirstOrDefault(r => (string)r["task"] == id);
                if ((string)row?["status"] == "completed") {
                    session.State.firstDay.taskId = id;
                    session.Memory.PublishStoryEvent("welcome_complete_" + id, PlayerIdentity.DisplayName + "实际完成了" + SevenCharacterCatalog.Name((string)definition["owner"]) + "提出的小事：" + (string)definition["title"], Cast.Concat(new[] { "player" }).ToArray(), true);
                    SetPhase(FirstDayPhase.EveningReady); return;
                }
                if ((string)row?["status"] != "active") continue;
                session.State.firstDay.taskId = id;
                var progress = JObject.Parse((string)row["progress"] ?? "{}");
                foreach (JObject step in definition["steps"])
                {
                    string stepId = (string)step["id"], target = (string)step["target"], action = (string)step["action"], evidence = null;
                    if ((bool?)progress[stepId]?["succeeded"] == true) continue;
                    if (((JArray)step["depends_on"]).Any(d => (bool?)progress[(string)d]?["succeeded"] != true)) break;
                    session.State.firstDay.objective = (string)definition["title"] + "\n下一步：" + (string)step["label"] + (action == "take_photo" ? " · 按 P 拍照" : " · 走近按 E");
                    if (action == "talk_to") completedTalks.TryGetValue(target, out evidence);
                    if (action == "take_photo") evidence = session.State.phone.photos.FirstOrDefault(p => p.place == target && !session.State.firstDay.taskPhotoIds.Contains(p.id))?.id;
                    if (evidence != null && !pendingSteps.Contains(id + stepId)) ReportStep(id, stepId, action, target, evidence);
                    break;
                }
            }
        }
        void ReportStep(string task, string step, string action, string target, string evidence)
        {
            pendingSteps.Add(task + step); receiptPending = true; var owner = session.State; int stamp = generation;
            var request = session.Memory.Client.RequestBase(); request.taskId = task; request.stepId = step; request.succeeded = true; request.performerId = "player";
            request.receiptId = "welcome_" + task + "_" + step;
            request.details["action"] = action; request.details["target"] = target; request.details["evidence_id"] = evidence;
            session.Memory.Client.Generate("/api/game/task/receipt", request, _ => { if (owner == session.State && stamp == generation) receiptPending = false; }, error => {
                if (owner != session.State || stamp != generation) return; receiptPending = false; pendingSteps.Remove(task + step); session.Story.Notify(error);
            });
        }
        IEnumerator Fireworks()
        {
            if (filmRoutine) yield break;
            filmRoutine = busy = true; bool done = false;
            // Let the third greeting close, then give Z a visible, readable send-off.
            while (session.Memory.View.Closing || !session.Memory.Spawner.IsStageReady(world.Stage)) yield return null;
            session.Memory.Conversations.CancelAll("一起去看烟花");
            var z = session.Memory.Spawner.Find("z");
            if (z != null)
            {
                world.Follow.target = z.transform;
                yield return new WaitForSecondsRealtime(.7f);
                if (bubble == null) { bubble = gameObject.AddComponent<NpcSpeechBubble>(); bubble.Initialize(invitations.Surface, session.Memory, world); }
                const string invitation = "烟花快开始啦，我们快去沙滩吧";
                bubble.Show("z", invitation);
                float end = Time.unscaledTime + NpcSpeechBubble.ReadDuration(invitation);
                while (Time.unscaledTime < end) { bubble.Tick(false); yield return null; }
                bubble.Clear();
            }
            fireworksFade = new GameObject("Fireworks scene fade", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
            var canvas = fireworksFade.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 800;
            var shade = fireworksFade.GetComponent<UnityEngine.UI.Image>();
            yield return FadeFireworks(shade, 0, 1, .65f);
            world.Follow.target = world.Player; world.Follow.Snap();
            // Keep opaque black below the film so its final frame never reveals the daytime scene.
            canvas.sortingOrder = 500;
            cinema.PlayFireworks(() => done = true);
            while (!done) yield return null;
            session.State.period = (int)TimePeriod.Night; session.Clock.Refresh();
            SetPhase(FirstDayPhase.WelcomeTask); gathering.Reset();
            // Keep the people already here; releasing the gathering restores autonomous decisions.
            // Rebuilding now would teleport everyone back to their default locations.
            session.Memory.PublishStoryEvent("fireworks_story", "玩家与 Z 和朋友们在沙滩看完烟花，随后回到市民广场，夜晚可以自由找朋友聊天。", Cast.Concat(new[] { "player" }).ToArray(), true);
            canvas.sortingOrder = 800;
            yield return FadeFireworks(shade, 1, 0, .85f);
            Destroy(fireworksFade); fireworksFade = null;
            filmRoutine = busy = false;
        }
        static IEnumerator FadeFireworks(UnityEngine.UI.Image shade, float from, float to, float duration)
        {
            shade.color = new Color(0, 0, 0, from);
            for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            { shade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / duration))); yield return null; }
            shade.color = new Color(0, 0, 0, to);
        }
        IEnumerator Arrive()
        {
            if (filmRoutine) yield break;
            filmRoutine = busy = true; bool done = false;
            session.Memory?.Conversations.CancelAll("抵岛");
            // The film restores this scale before the journey coroutine resumes.
            // Keep the prepared dialogue valid across that intervening frame.
            Time.timeScale = 1;
            cinema.PlayArrival(() => done = true);
            SetPhase(FirstDayPhase.Harbor); session.State.period = (int)TimePeriod.Afternoon;
            world.Travel(PlaceId.Harbor);
            while (world.IsTransitioning) yield return null;
            var z = session.Memory?.Spawner.Find("z");
            world.Player.GetComponent<PlayerController>().Teleport(new Vector2(z != null ? z.transform.position.x - 1.2f : -9, .08f)); world.Follow.Snap();
            // Publish arrival before preparing the greeting, so its proposal remains current.
            session.Memory.PublishStoryEvent("arrival_accepted", PlayerIdentity.DisplayName + "接受七天邀请并抵达码头，与 Z 见面。", new[] { "player", "z" }, true);
            session.Memory.View.CinemaHidden = true;
            Time.timeScale = 1;
            session.Memory.BeginPlayerChat("z");
            session.State.firstDay.harborOpened = session.Memory.Conversations.PlayerActor != null;
            while (!done) yield return null;
            session.Memory.View.CinemaHidden = false; session.State.firstDay.arrivalFilmSeen = true;
            GameUI.Instance.SetTitleVisible(false); session.Clock.Refresh(); filmRoutine = busy = false; Time.timeScale = 1; session.Save();
        }
        void OpenLive(bool opening)
        {
            if (live != null) return;
            if (opening) { session.State.firstDay.openingStarted = true; session.Save(); }
            else if (Active) SetPhase(FirstDayPhase.EveningLive);
            GameUI.Instance.CloseOverlayImmediately(); busy = true; Time.timeScale = 0;
            GameUI.Instance.SetTitleVisible(true);
            var owner = session.State; int stamp = generation;
            live = NativeLiveHost.Open(transform, () => {
                if (opening) ShowOpeningRetry();
                else { var host = live; live = null; host?.Close(); busy = false; Time.timeScale = 1; GameUI.Instance.SetTitleVisible(false); if (Active) SetPhase(FirstDayPhase.EveningReady); else nextPlan = Time.unscaledTime + 8; }
            }, id => {
                if (owner != session.State || stamp != generation) return;
                if (opening) { var previous = live; live = null; previous?.Close(); ShowOpeningRetry(); return; }
                StartCoroutine(CompleteEvening(id, owner, stamp));
            }, opening ? null : session.Memory, opening ? "arrival" : Active ? "first_evening" : "daily", command => {
                if (owner != session.State || stamp != generation || !opening || Phase != FirstDayPhase.Live) return;
                if ((string)command["tool"] == "offer_arrival") invitations.Show((string)command["title"], (string)command["description"], "确认登岛", () => {
                    live?.AcceptArrival();
                });
                if ((string)command["tool"] == "depart_island" && owner.firstDay.invitationAccepted) {
                    var host = live; live = null; host?.Close(); SetPhase(FirstDayPhase.Arrival); StartCoroutine(Arrive());
                }
            }, opening && !session.State.firstDay.introSeen);
        }
        IEnumerator CompleteEvening(string id, SaveData owner, int stamp)
        {
            var host = live;
            if (host == null) yield break;
            var sync = host.SynchronizeExit();
            while (!sync.IsCompleted) yield return null;
            if (owner != session.State || stamp != generation) yield break;
            if (sync.IsFaulted || sync.IsCanceled)
            {
                invitations.Show("正在保存今晚的相遇", "连接暂时中断，进度会保留。恢复后再试一次。", "重试", () => StartCoroutine(CompleteEvening(id, owner, stamp)));
                yield break;
            }
            live = null; host.Close();
            yield return LoadingPlaceholder("after_evening_live");
            busy = false; Time.timeScale = 1; GameUI.Instance.SetTitleVisible(false);
            if (session.Clock.CompleteLiveSession(id)) ReturnHomeAfterLive();
        }
        public void ReturnHomeAfterLive()
        {
            if (!session.Clock.PendingNightRest || busy) return;
            StartCoroutine(ReturnHome());
        }
        IEnumerator ReturnHome()
        {
            busy = true; Time.timeScale = 1;
            var owner = session.State;
            yield return ShutdownTransition();
            if (session.State != owner) yield break;
            session.Travel(PlaceId.PlayerHome);
            yield return null;
            while (world.IsTransitioning && session.State == owner) yield return null;
            if (session.State != owner) yield break;
            busy = false; session.Save();
        }
        IEnumerator RestAtBed()
        {
            busy = true; var owner = session.State;
            nightFade = new GameObject("Rest until morning",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.Image));
            var canvas = nightFade.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=800;
            var image=nightFade.GetComponent<UnityEngine.UI.Image>();image.color=Color.clear;
            for(float t=0;t<.45f;t+=Time.unscaledDeltaTime){image.color=new Color(0,0,0,Mathf.Clamp01(t/.45f));yield return null;}
            image.color=Color.black;
            if(session.State==owner && session.Clock.CompleteNightRest())session.Memory?.Spawner.Reload();
            yield return new WaitForSecondsRealtime(.45f);
            for(float t=0;t<.7f;t+=Time.unscaledDeltaTime){image.color=new Color(0,0,0,1-Mathf.Clamp01(t/.7f));yield return null;}
            Destroy(nightFade);nightFade=null;busy=false;
        }
        IEnumerator ShutdownTransition()
        {
            var screen = new GameObject("电脑关机", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
            var canvas = screen.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 800;
            var image = screen.GetComponent<UnityEngine.UI.Image>(); image.color = Color.black;
            var label = UIPrimitives.Label("Shutdown", screen.transform, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(600,80), "正在关机…", 28, Color.white, TMPro.TextAlignmentOptions.Center);
            yield return new WaitForSecondsRealtime(1.4f); label.text = "";
            yield return new WaitForSecondsRealtime(.6f); Destroy(screen);
        }
        void ShowOpeningRetry() { invitations.Show("小岛邀请", "可以继续直播，或稍后再试。", "继续观看", () => { if (live == null) OpenLive(true); }); }
        IEnumerator LoadingPlaceholder(string point) { yield break; } // Reserved: no loading UI until its design is supplied.
        IEnumerator NotifyLive() { busy = true; yield return LoadingPlaceholder("before_evening_live"); yield return GameUI.Instance.ShowLiveNotification(); busy = false; OpenLive(false); }
        public IEnumerable<SevenCharacter> StagedCast(PlaceId place)
        {
            if (!Active) return null;
            if ((Phase == FirstDayPhase.Harbor || Phase == FirstDayPhase.WalkToSquare) && place == PlaceId.Harbor) return SevenCharacterCatalog.Actors.Where(a => a.id == "z");
            if (Phase == FirstDayPhase.WalkToSquare && place == PlaceId.Street) return SevenCharacterCatalog.Actors.Where(a => a.id == "z");
            if (place == PlaceId.Square && (Phase == FirstDayPhase.WalkToSquare || Phase == FirstDayPhase.MeetIslanders || Phase == FirstDayPhase.Fireworks || Phase == FirstDayPhase.WelcomeTask || Phase == FirstDayPhase.EveningReady)) return SevenCharacterCatalog.Actors.Where(a => Cast.Contains(a.id) || a.id == "liyuan" && session.State.memory.positions.Any(p => p.actorId == "liyuan" && p.place == "Square" && p.day == 1));
            return null;
        }
        public float CastPosition(string actor, PlaceId place)
        {
            if (actor == "z" && Phase == FirstDayPhase.WalkToSquare) return escort.ArrivalX(place);
            if (place == PlaceId.Harbor) return -7.8f;
            if (place == PlaceId.Square && Phase == FirstDayPhase.MeetIslanders && FirstDayGathering.TryPosition(actor, out float x)) return x;
            return -7.4f + Array.IndexOf(Cast, actor) * 2.05f;
        }
        public bool CanTravel(PlaceId target) => !(Phase == FirstDayPhase.MeetIslanders && session.CurrentPlace == PlaceId.Square && GreetedCount < RequiredGreetings && target != PlaceId.Square);
        public bool IsAvailable(string id) => id == "firstday_bed" ? session.Clock.PendingNightRest && session.CurrentPlace == PlaceId.PlayerHome : !Active || !(id == "bench" || id == "lighthouse_bench" || id == "coffee" || id.StartsWith("firstday_"));
        public string Prompt(string id) => id == "firstday_bed" ? "休息一夜" : null;
        public bool Interact(string id)
        {
            if(id=="firstday_bed")
            {
                var bed=world.Interactables.FirstOrDefault(i=>i!=null&&i.id==id);
                if(!busy&&IsAvailable(id)&&bed!=null&&world.Player!=null&&Vector2.Distance(world.Player.position,bed.transform.position)<bed.radius)StartCoroutine(RestAtBed());
                return true;
            }
            if (!Active) return false;
            if (Phase == FirstDayPhase.MeetIslanders && !session.State.firstDay.announced)
            { session.Story.Notify("Z 正准备向大家介绍你。 "); return true; }
            if (!IsAvailable(id)) { session.Story.Notify("先和大家完成今天的相遇。 "); return true; }
            if (Cast.Contains(id)) { session.Memory.BeginPlayerChat(id); return true; }
            return false;
        }
        void OnDestroy() { if(nightFade!=null)Destroy(nightFade); if(fireworksFade!=null)Destroy(fireworksFade); gathering.Reset(); if (live != null) live.Close(); }
    }
}
