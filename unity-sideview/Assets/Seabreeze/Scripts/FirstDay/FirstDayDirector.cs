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
        NativeLiveHost live; FirstDayEscort escort;
        bool busy, planning, harborOpening, receiptPending;
        float nextPlan; int generation;
        readonly Dictionary<string, string> completedTalks = new Dictionary<string, string>();
        readonly HashSet<string> pendingSteps = new HashSet<string>();
        public bool BlocksInput => busy;
        public bool Active => session?.State?.firstDay != null && Phase != FirstDayPhase.None && Phase != FirstDayPhase.Complete;
        public FirstDayPhase Phase => session?.State?.firstDay?.phase ?? FirstDayPhase.None;
        public string[] Cast => FirstDayCast;
        public int GreetedCount => session?.State?.firstDay?.greeted.Count(Cast.Contains) ?? 0;
        public string Objective => Active ? !string.IsNullOrEmpty(session.State.firstDay.objective) ? session.State.firstDay.objective :
            Phase == FirstDayPhase.MeetIslanders ? "认识岛上的朋友 " + GreetedCount + "/7" :
            Phase == FirstDayPhase.Live ? "正在等待 Z 的直播" :
            Phase == FirstDayPhase.WalkToSquare ? "和 Z 一起前往市民广场" :
            Phase == FirstDayPhase.WelcomeTask ? "和朋友聊聊，选一件想一起做的小事" : "继续今天的小岛生活" :
            "今日休息 " + session.Clock.RestActions + "/4";
        public void Initialize(GameSession owner, WorldDirector director)
        {
            session = owner; world = director;
            invitations = gameObject.AddComponent<FirstDayOverlay>(); invitations.Initialize();
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
            generation++; StopAllCoroutines(); invitations.Hide(); bubble?.Clear();
            if (live != null) { live.Close(); live = null; }
            busy = planning = harborOpening = receiptPending = false;
            completedTalks.Clear(); pendingSteps.Clear(); escort.ResetRoute();
            session.Memory?.View.ResetView(); nextPlan = 0;
        }
        public void ResumeAfterTravel() { StartCoroutine(ResumeWhenReady()); }
        IEnumerator ResumeWhenReady() { yield return null; while (world.IsTransitioning) yield return null; Resume(); }
        public void Resume()
        {
            if (session.IsAtTitle) return;
            if (Phase == FirstDayPhase.Arrival) StartCoroutine(Arrive());
            else if (Phase == FirstDayPhase.Fireworks) StartCoroutine(FireworksPlaceholder());
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
            bubble?.Tick(session.Memory?.PlayerChatOpen ?? false);
            if (Phase == FirstDayPhase.Harbor && !harborOpening && !session.State.firstDay.harborOpened && session.Memory?.Spawner.Find("z") != null)
                StartCoroutine(OpenHarborConversation());
            if (Phase == FirstDayPhase.WalkToSquare && session.CurrentPlace == PlaceId.Square)
            {
                SetPhase(FirstDayPhase.MeetIslanders);
                session.State.period = (int)TimePeriod.Afternoon; session.Clock.Refresh(); session.Memory?.Spawner.Reload();
            }
            if (Phase == FirstDayPhase.WelcomeTask) ObserveTask();
            if (busy || planning || live != null || invitations.Visible || (session.Memory?.PlayerChatOpen ?? false) || GameUI.Instance.HasOverlay || Time.unscaledTime < nextPlan) return;
            bool wantsPlan = Phase == FirstDayPhase.Live || Phase == FirstDayPhase.Harbor && session.State.firstDay.harborReady ||
                Phase == FirstDayPhase.MeetIslanders && (!session.State.firstDay.announced || GreetedCount == Cast.Length) ||
                Phase == FirstDayPhase.EveningReady || !Active && session.Clock.LiveReady;
            if (wantsPlan && session.Memory.Spawner.IsStageReady(world.Stage) && (session.Memory?.Client.CanGenerate ?? false)) RequestPlan();
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
                else if (tool == "show_fireworks" && Phase == FirstDayPhase.MeetIslanders && GreetedCount == Cast.Length) { SetPhase(FirstDayPhase.Fireworks); StartCoroutine(FireworksPlaceholder()); }
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
            if (bubble == null) { bubble = gameObject.AddComponent<NpcSpeechBubble>(); bubble.Initialize(invitations.Surface, session.Memory, world); }
            bubble.Show("z", text);
            if (!bubble.Visible) { busy = false; yield break; }
            float end = Time.unscaledTime + NpcSpeechBubble.ReadDuration(text);
            while (Time.unscaledTime < end) { bubble.Tick(false); yield return null; }
            var audience = Cast.Concat(new[] { "player" }).ToArray();
            session.Memory.PublishStoryEvent("player_introduced", "Z 向在场的人介绍新朋友：" + text, audience, true);
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
                session.State.firstDay.taskId = id; session.State.firstDay.objective = (string)definition["title"];
                var progress = JObject.Parse((string)row["progress"] ?? "{}");
                foreach (JObject step in definition["steps"])
                {
                    string stepId = (string)step["id"], target = (string)step["target"], action = (string)step["action"], evidence = null;
                    if ((bool?)progress[stepId]?["succeeded"] == true) continue;
                    if (((JArray)step["depends_on"]).Any(d => (bool?)progress[(string)d]?["succeeded"] != true)) break;
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
        IEnumerator FireworksPlaceholder()
        {
            busy = true; session.State.period = (int)TimePeriod.Night; session.Clock.Refresh();
            // No media asset is configured. This is a story transition, not a video receipt.
            yield return new WaitForSecondsRealtime(.8f);
            session.Memory.PublishStoryEvent("fireworks_story", "玩家与 Z 在榕树广场一起度过烟花时刻，随后回到大家身边。", Cast.Concat(new[] { "player" }).ToArray(), true);
            SetPhase(FirstDayPhase.WelcomeTask); busy = false;
        }
        IEnumerator Arrive()
        {
            busy = true; session.Memory?.Conversations.CancelAll("抵岛"); Time.timeScale = 1;
            SetPhase(FirstDayPhase.Harbor); session.State.period = (int)TimePeriod.Afternoon;
            session.Memory.PublishStoryEvent("arrival_accepted", PlayerIdentity.DisplayName + "接受了七天登岛邀请，Z在码头与这位新朋友见面。", new[] { "player", "z" }, true);
            session.State.firstDay.harborOpened = false; world.Travel(PlaceId.Harbor);
            while (world.IsTransitioning) yield return null;
            var z = session.Memory?.Spawner.Find("z");
            world.Player.GetComponent<PlayerController>().Teleport(new Vector2(z != null ? z.transform.position.x - 1.2f : -9, .08f)); world.Follow.Snap();
            GameUI.Instance.SetTitleVisible(false); session.Clock.Refresh(); busy = false; session.Save();
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
                var host = live; live = null; host?.Close(); busy = false; Time.timeScale = 1; GameUI.Instance.SetTitleVisible(false);
                session.Clock.CompleteLiveSession(id); session.Memory?.Spawner.Reload();
            }, opening ? "arrival" : Active ? "first_evening" : "daily", command => {
                if (owner != session.State || stamp != generation || !opening || Phase != FirstDayPhase.Live) return;
                if ((string)command["tool"] == "offer_arrival") invitations.Show((string)command["title"], (string)command["description"], "确认登岛", () => {
                    live?.AcceptArrival();
                });
                if ((string)command["tool"] == "depart_island" && owner.firstDay.invitationAccepted) {
                    var host = live; live = null; host?.Close(); SetPhase(FirstDayPhase.Arrival); StartCoroutine(Arrive());
                }
            });
        }
        void ShowOpeningRetry() { invitations.Show("小岛邀请", "可以继续直播，或稍后再试。", "继续观看", () => { if (live == null) OpenLive(true); }); }
        IEnumerator NotifyLive() { busy = true; yield return GameUI.Instance.ShowLiveNotification(); busy = false; OpenLive(false); }
        public IEnumerable<SevenCharacter> StagedCast(PlaceId place)
        {
            if (!Active) return null;
            if ((Phase == FirstDayPhase.Harbor || Phase == FirstDayPhase.WalkToSquare) && place == PlaceId.Harbor) return SevenCharacterCatalog.Actors.Where(a => a.id == "z");
            if (Phase == FirstDayPhase.WalkToSquare && place == PlaceId.Street) return SevenCharacterCatalog.Actors.Where(a => a.id == "z");
            if (place == PlaceId.Square && Phase != FirstDayPhase.Live && Phase != FirstDayPhase.Arrival) return SevenCharacterCatalog.Actors.Where(a => Cast.Contains(a.id));
            return null;
        }
        public float CastPosition(string actor, PlaceId place)
        {
            if (actor == "z" && Phase == FirstDayPhase.WalkToSquare) return escort.ArrivalX(place);
            if (place == PlaceId.Harbor) return -7.8f;
            return -7.4f + Array.IndexOf(Cast, actor) * 2.05f;
        }
        public bool CanTravel(PlaceId target) => !(Phase == FirstDayPhase.MeetIslanders && session.CurrentPlace == PlaceId.Square && GreetedCount < Cast.Length && target != PlaceId.Square);
        public bool IsAvailable(string id) => !Active || !(id == "bench" || id == "lighthouse_bench" || id == "coffee" || id.StartsWith("firstday_"));
        public string Prompt(string id) => null;
        public bool Interact(string id)
        {
            if (!Active) return false;
            if (Phase == FirstDayPhase.MeetIslanders && !session.State.firstDay.announced)
            { session.Story.Notify("Z 正准备向大家介绍你。 "); return true; }
            if (!IsAvailable(id)) { session.Story.Notify("先和大家完成今天的相遇。 "); return true; }
            if (Cast.Contains(id)) { session.Memory.BeginPlayerChat(id); return true; }
            return false;
        }
        void OnDestroy() { if (live != null) live.Close(); }
    }
}
