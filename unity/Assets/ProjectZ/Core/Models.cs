using System;
using System.Collections.Generic;

namespace ProjectZ.Core
{
    [Serializable] public sealed class EpisodeCatalog
    {
        public int version = 1;
        public List<EpisodeDefinition> episodes = new List<EpisodeDefinition>();
    }

    [Serializable] public sealed class EpisodeDefinition
    {
        public string id = "";
        public string title = "";
        public string locationId = "";
        public string invitation = "";
        public List<ChoiceDefinition> choices = new List<ChoiceDefinition>();
    }

    [Serializable] public sealed class ChoiceDefinition
    {
        public string id = "";
        public string label = "";
        public string fact = "";
        public string reply = "";
        public string followup = "";
        public string emotion = "neutral";
        public string action = "look";
        public string followupAction = "look";
        public int respectDelta;
    }

    [Serializable] public sealed class WorldState
    {
        public int schemaVersion = 1;
        public int contentVersion = 1;
        public string slotId = "";
        public int revision;
        public int day = 1;
        public int respect;
        public List<EpisodeProgress> episodes = new List<EpisodeProgress>();
        public List<WorldEvent> events = new List<WorldEvent>();
    }

    [Serializable] public sealed class EpisodeProgress
    {
        public string episodeId = "";
        public string choiceId = "";
        public string sourceEventId = "";
        public int chosenDay;
        public bool followupSeen;
    }

    [Serializable] public sealed class WorldEvent
    {
        public string id = "";
        public int sequence;
        public int day;
        public string type = "";
        public string actorId = "z";
        public string episodeId = "";
        public string locationId = "";
        public string fact = "";
    }

    [Serializable] public sealed class CharacterRequest
    {
        public string requestId = "";
        public string slotId = "";
        public int revision;
        public string actorId = "z";
        public string phase = "";
        public string playerText = "";
        public string authoredText = "";
        public string emotion = "neutral";
        public string suggestedAction = "look";
        public List<string> allowedActions = new List<string>();
        public List<WorldEvent> observations = new List<WorldEvent>();
    }

    [Serializable] public sealed class CharacterResponse
    {
        public string requestId = "";
        public string slotId = "";
        public int revision;
        public string text = "";
        public string emotion = "neutral";
        public string action = "look";
        public string mode = "authored";
        public List<string> sourceEventIds = new List<string>();
    }

    public static class ContentRules
    {
        public static readonly HashSet<string> Actions = new HashSet<string> { "look", "nod", "wave", "pause" };
        public static readonly HashSet<string> Emotions = new HashSet<string> { "neutral", "warm", "thoughtful", "firm" };

        public static void Validate(EpisodeCatalog catalog)
        {
            if (catalog == null || catalog.version < 1 || catalog.episodes == null || catalog.episodes.Count == 0)
                throw new ArgumentException("事件配置为空或版本无效。");
            var ids = new HashSet<string>();
            foreach (var episode in catalog.episodes)
            {
                if (episode == null || string.IsNullOrWhiteSpace(episode.id) || !ids.Add(episode.id) ||
                    string.IsNullOrWhiteSpace(episode.locationId) || string.IsNullOrWhiteSpace(episode.invitation) ||
                    episode.choices == null || episode.choices.Count < 2)
                    throw new ArgumentException("事件需要唯一 ID、地点、邀请和至少两个选项。");
                var choices = new HashSet<string>();
                foreach (var choice in episode.choices)
                    if (choice == null || string.IsNullOrWhiteSpace(choice.id) || !choices.Add(choice.id) ||
                        string.IsNullOrWhiteSpace(choice.label) || string.IsNullOrWhiteSpace(choice.fact) ||
                        string.IsNullOrWhiteSpace(choice.reply) || string.IsNullOrWhiteSpace(choice.followup) ||
                        !Actions.Contains(choice.action) || !Actions.Contains(choice.followupAction) ||
                        !Emotions.Contains(choice.emotion) || Math.Abs((long)choice.respectDelta) > 1)
                        throw new ArgumentException("选项字段、动作、情绪或关系变化无效：" + episode.id);
            }
        }
    }
}
