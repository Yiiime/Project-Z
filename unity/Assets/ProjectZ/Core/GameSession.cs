using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectZ.Core
{
    public sealed class GameSession
    {
        private readonly EpisodeCatalog catalog;
        private readonly WorldState state;
        private readonly HashSet<string> consumedRequests = new HashSet<string>();

        public GameSession(EpisodeCatalog catalog, WorldState restored = null)
        {
            ContentRules.Validate(catalog);
            this.catalog = catalog;
            if (restored != null) ValidateState(restored, catalog);
            state = restored == null ? new WorldState {
                slotId = Guid.NewGuid().ToString("N"), contentVersion = catalog.version
            } : Clone(restored);
            ValidateState(state, catalog);
        }

        public WorldState Snapshot() => Clone(state);
        public EpisodeDefinition Definition(string id) => catalog.episodes.FirstOrDefault(e => e.id == id)
            ?? throw new ArgumentException("未知事件：" + id);
        public EpisodeProgress Progress(string id) => state.episodes.Where(e => e.episodeId == id)
            .Select(CloneProgress).FirstOrDefault();
        public bool CanFollowup(string id)
        {
            var progress = Progress(id);
            return progress != null && !progress.followupSeen && progress.chosenDay < state.day;
        }

        public CharacterRequest Choose(string episodeId, string choiceId)
        {
            var episode = Definition(episodeId);
            if (Progress(episodeId) != null) throw new InvalidOperationException("本局已经完成这次选择。");
            var choice = episode.choices.FirstOrDefault(c => c.id == choiceId)
                ?? throw new ArgumentException("未知选项。");
            var fact = Append("choice", episode, choice.fact);
            state.episodes.Add(new EpisodeProgress { episodeId = episodeId, choiceId = choiceId,
                sourceEventId = fact.id, chosenDay = state.day });
            state.respect = Math.Max(-3, Math.Min(3, state.respect + choice.respectDelta));
            return Request("choice", choice.reply, choice.emotion, choice.action, fact);
        }

        public CharacterRequest BeginFollowup(string episodeId)
        {
            if (!CanFollowup(episodeId)) throw new InvalidOperationException("这次接续还未到时机，或已经完成。");
            var progress = Progress(episodeId);
            var choice = Definition(episodeId).choices.First(c => c.id == progress.choiceId);
            var source = state.events.First(e => e.id == progress.sourceEventId);
            // Only mark delivered after a valid response has actually been shown.
            return Request("followup", choice.followup, choice.emotion, choice.followupAction, source);
        }

        public bool Accept(CharacterRequest request, CharacterResponse response, out string reason)
        {
            reason = ResponseRules.Validate(request, response, state);
            if (reason != null) return false;
            if (!consumedRequests.Add(request.requestId)) { reason = "此回应已经消费。"; return false; }
            if (request.phase == "followup")
            {
                var source = request.observations.Single();
                var progress = state.episodes.Single(e => e.sourceEventId == source.id);
                if (progress.followupSeen) { reason = "此接续已经完成。"; return false; }
                progress.followupSeen = true;
                Append("followup", Definition(progress.episodeId), "Z 与玩家再次谈起了这段共同经历。");
            }
            return true;
        }

        public void NextDay()
        {
            if (state.day >= 100000) throw new InvalidOperationException("当前原型已到时间上限。");
            state.day++;
            state.revision++;
            consumedRequests.Clear();
        }

        public CharacterRequest Conversation(string playerText)
        {
            if (string.IsNullOrWhiteSpace(playerText) || playerText.Length > 500)
                throw new ArgumentException("请输入 1–500 字。");
            var request = Request("conversation", "我想先听听你怎么想。", "neutral", "look", null);
            request.playerText = playerText;
            // Player statements are inputs, never automatically promoted to world events.
            request.observations = state.events.Where(e => e.type == "choice").Select(CloneEvent).ToList();
            return request;
        }

        private WorldEvent Append(string type, EpisodeDefinition episode, string fact)
        {
            state.revision++;
            var entry = new WorldEvent { id = Guid.NewGuid().ToString("N"), sequence = state.events.Count + 1,
                day = state.day, type = type, episodeId = episode.id, locationId = episode.locationId, fact = fact };
            state.events.Add(entry);
            return entry;
        }

        private CharacterRequest Request(string phase, string text, string emotion, string action, WorldEvent source)
        {
            var request = new CharacterRequest { requestId = Guid.NewGuid().ToString("N"), slotId = state.slotId,
                revision = state.revision, phase = phase, authoredText = text, emotion = emotion,
                suggestedAction = action, allowedActions = new List<string>(ContentRules.Actions) };
            if (source != null) request.observations.Add(CloneEvent(source));
            return request;
        }

        public static void ValidateState(WorldState data, EpisodeCatalog catalog)
        {
            if (data == null || data.schemaVersion != 1 || data.contentVersion != catalog.version ||
                !Guid.TryParseExact(data.slotId, "N", out _) || data.day < 1 || data.day > 100000 ||
                data.revision < 0 || data.events == null || data.episodes == null || Math.Abs((long)data.respect) > 3)
                throw new ArgumentException("存档版本或字段无效。");
            var eventIds = new HashSet<string>();
            for (var i = 0; i < data.events.Count; i++)
            {
                var entry = data.events[i];
                if (entry == null || !Guid.TryParseExact(entry.id, "N", out _) || !eventIds.Add(entry.id) ||
                    entry.sequence != i + 1 || entry.day < 1 || entry.day > data.day || entry.actorId != "z" ||
                    (i > 0 && entry.day < data.events[i - 1].day) ||
                    string.IsNullOrWhiteSpace(entry.fact) || (entry.type != "choice" && entry.type != "followup") ||
                    !catalog.episodes.Any(e => e.id == entry.episodeId && e.locationId == entry.locationId))
                    throw new ArgumentException("存档事件来源无效。");
            }
            var progressIds = new HashSet<string>();
            foreach (var progress in data.episodes)
            {
                if (progress == null || !progressIds.Add(progress.episodeId)) throw new ArgumentException("重复事件进度。");
                var definition = catalog.episodes.FirstOrDefault(e => e.id == progress.episodeId);
                var source = data.events.FirstOrDefault(e => e.id == progress.sourceEventId);
                var choice = definition?.choices.FirstOrDefault(c => c.id == progress.choiceId);
                var followups = data.events.Where(e => e.episodeId == progress.episodeId && e.type == "followup").ToList();
                if (choice == null || source == null || source.type != "choice" || source.episodeId != progress.episodeId ||
                    source.day != progress.chosenDay || source.fact != choice.fact ||
                    followups.Count != (progress.followupSeen ? 1 : 0) ||
                    followups.Any(e => e.day <= progress.chosenDay))
                    throw new ArgumentException("存档选择和事件不一致。");
            }
            if (data.events.Count(e => e.type == "choice") != data.episodes.Count ||
                data.events.Any(e => !progressIds.Contains(e.episodeId)) ||
                data.revision != data.events.Count + data.day - 1)
                throw new ArgumentException("存档事件数量不一致。");
            var respect = 0;
            foreach (var entry in data.events.Where(e => e.type == "choice"))
            {
                var progress = data.episodes.Single(p => p.sourceEventId == entry.id);
                var choice = catalog.episodes.Single(e => e.id == progress.episodeId).choices.Single(c => c.id == progress.choiceId);
                respect = Math.Max(-3, Math.Min(3, respect + choice.respectDelta));
            }
            if (respect != data.respect) throw new ArgumentException("关系状态与实际经历不一致。");
        }

        private static EpisodeProgress CloneProgress(EpisodeProgress p) => new EpisodeProgress {
            episodeId = p.episodeId, choiceId = p.choiceId, sourceEventId = p.sourceEventId,
            chosenDay = p.chosenDay, followupSeen = p.followupSeen };
        private static WorldEvent CloneEvent(WorldEvent e) => new WorldEvent { id = e.id, sequence = e.sequence,
            day = e.day, type = e.type, actorId = e.actorId, episodeId = e.episodeId, locationId = e.locationId, fact = e.fact };
        private static WorldState Clone(WorldState s) => new WorldState { schemaVersion = s.schemaVersion,
            contentVersion = s.contentVersion, slotId = s.slotId, revision = s.revision, day = s.day, respect = s.respect,
            episodes = s.episodes?.Select(CloneProgress).ToList(), events = s.events?.Select(CloneEvent).ToList() };
    }
}
