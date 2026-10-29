using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectZ.Core
{
    public static class ResponseRules
    {
        // Structural checks do not prove that free-form language is factually correct.
        public static string Validate(CharacterRequest request, CharacterResponse response, WorldState state)
        {
            if (request == null || response == null) return "回应为空。";
            if (request.slotId != state.slotId || response.slotId != state.slotId ||
                request.revision != state.revision || response.revision != state.revision ||
                response.requestId != request.requestId) return "回应属于旧存档、旧时刻或另一个请求。";
            if (string.IsNullOrWhiteSpace(response.text) || response.text.Length > 240) return "回应长度无效。";
            if (!ContentRules.Actions.Contains(response.action) || !request.allowedActions.Contains(response.action) ||
                !ContentRules.Emotions.Contains(response.emotion)) return "回应要求了未支持的表演。";
            if (response.mode != "authored" && response.mode != "live") return "回应来源未标明。";
            if (response.sourceEventIds == null || response.sourceEventIds.Distinct().Count() != response.sourceEventIds.Count)
                return "引用列表无效。";
            var allowed = new HashSet<string>(request.observations.Select(e => e.id));
            if (response.sourceEventIds.Any(id => !allowed.Contains(id) || !state.events.Any(e => e.id == id)))
                return "回应引用了不存在或当前不可知的事件。";
            if (request.phase == "followup" && (allowed.Count != 1 || !allowed.SetEquals(response.sourceEventIds)))
                return "接续没有引用对应的共同经历。";
            return null;
        }

        public static CharacterResponse Authored(CharacterRequest request) => new CharacterResponse {
            requestId = request.requestId, slotId = request.slotId, revision = request.revision,
            text = request.authoredText, emotion = request.emotion, action = request.suggestedAction,
            mode = "authored", sourceEventIds = request.observations.Select(e => e.id).ToList()
        };
    }
}
