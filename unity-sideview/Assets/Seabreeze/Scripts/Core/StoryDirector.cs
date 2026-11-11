using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seabreeze
{
    public sealed class StoryDirector : MonoBehaviour
    {
        GameSession session;
        readonly List<string> phone = new List<string>();
        bool evaluating;
        public event Action<string> Toast;
        public IReadOnlyList<string> PhoneMessages { get { RefreshPhone(); return phone; } }
        public string Objective
        {
            get
            {
                if (!session) return "沿着海边走走";
                if (session.FirstDay != null && session.State.firstDay != null) return session.FirstDay.Objective;
                if (MemoryRuntimeConfig.Enabled) return "走近岛民按 E 交谈；长椅和咖啡可以推进时段。";
                if (!string.IsNullOrEmpty(session.State.ending)) return "这一日，已经有了自己的名字。还可以继续散步。";
                if (!Has("postcard_found")) return "海边长椅旁，有一张被风吹来的旧明信片。";
                if (!Has("cafe_story")) return "去街道左侧的「潮汐」咖啡馆，问问阿遥。";
                if (!Has("chen_story")) return session.Clock.Period <= TimePeriod.Afternoon ? "陈伯在海边街道。他也许认得明信片上的字。" : "陈伯已经回家；咖啡馆的留言簿里留着他的旧记录。";
                if (!Has("evening_mio")) return session.Clock.Period < TimePeriod.Evening ? "澪约你黄昏去灯塔。坐坐长椅，或喝杯咖啡，让时间慢一点过去。" : "去灯塔观景台，和澪说说你的决定。";
                return "灯塔下的旧邮筒，正等着今天的最后一句话。";
            }
        }
        public void Initialize(GameSession owner) { session = owner; }
        public void ResetTransientState() { evaluating = false; phone.Clear(); }
        bool Has(string flag) => session.HasFlag(flag);
        void Talk(string id) => session.Dialogue.StartDialogue(id);
        public void Notify(string message) { if (!string.IsNullOrEmpty(message)) Toast?.Invoke(message); }

        public bool IsAvailable(string id)
        {
            if (!session || !session.IsReady) return false;
            if (session.FirstDay != null && !session.FirstDay.IsAvailable(id)) return false;
            if (MemoryRuntimeConfig.Enabled && SevenCharacterCatalog.Find(id) != null) return session.Memory != null && session.Memory.CanInteract(id);
            if (id.StartsWith("npc_", StringComparison.Ordinal))
            {
                var npc = session.Database ? session.Database.FindNpc(id) : null;
                if (npc) return npc.IsPresent(session.CurrentPlace, session.Clock.Period, session.State.flags);
            }
            return true;
        }
        public string PromptFor(string id)
        {
            var storyPrompt = session?.FirstDay?.Prompt(id);
            if (storyPrompt != null) return storyPrompt;
            if (MemoryRuntimeConfig.Enabled)
            {
                var actor = SevenCharacterCatalog.Find(id);
                if (actor != null) return "和" + actor.displayName + "交谈";
            }
            switch (id)
            {
                case "npc_mio": return "和澪交谈";
                case "npc_aya": return "和阿遥交谈";
                case "npc_chen": return "和陈伯交谈";
                case "postcard": return Has("postcard_found") ? "看看长椅" : "拾起明信片";
                case "noticeboard": return "看看告示板";
                case "bicycle": return "看看旧自行车";
                case "sea": return "听一会儿海";
                case "bench": case "lighthouse_bench": return "坐一会儿 · 推进时段";
                case "radio": return "听听收音机";
                case "guestbook": return "翻开留言簿";
                case "coffee": return "喝杯咖啡 · 推进时段";
                case "letterbox": return "看看旧邮筒";
                case "telescope": return "看看远处";
                default: return "查看";
            }
        }
        public void Interact(string id)
        {
            if (!session || session.InputBlocked || !IsAvailable(id)) return;
            if (session.FirstDay != null && session.FirstDay.Interact(id)) return;
            if (MemoryRuntimeConfig.Enabled)
            {
                if (SevenCharacterCatalog.Find(id) != null) { session.Memory?.BeginPlayerChat(id); return; }
                if (id == "bench" || id == "lighthouse_bench" || id == "coffee") { session.Clock.Advance(); return; }
                Notify("海风吹过小岛，你留意着身边的人和事。");
                return;
            }
            switch (id)
            {
                case "npc_aya":
                    if (session.CurrentPlace == PlaceId.Lighthouse) { Talk("aya_gathering"); break; }
                    Talk(!Has("postcard_found") ? "aya_greeting" : !Has("cafe_story") ? "aya_card" : !Has("chen_story") ? "aya_hint" : !string.IsNullOrEmpty(session.State.ending) ? "aya_after" : Has("choice_invite") ? "aya_invited" : "aya_repeat");
                    break;
                case "npc_chen":
                    if (session.CurrentPlace == PlaceId.Lighthouse) { Talk("chen_gathering"); break; }
                    Talk(!Has("postcard_found") ? "chen_greeting" : !Has("cafe_story") ? "chen_before_cafe" : !Has("chen_story") ? "chen_card" : "chen_repeat");
                    break;
                case "npc_mio":
                    if (session.CurrentPlace == PlaceId.Lighthouse && session.Clock.Period >= TimePeriod.Evening)
                        Talk(!Has("chen_story") ? "mio_wait" : !Has("evening_mio") ? (Has("choice_invite") ? "mio_evening_invite" : Has("choice_keep") ? "mio_evening_keep" : "mio_evening_deliver") : !string.IsNullOrEmpty(session.State.ending) ? "mio_after" : "mio_evening_repeat");
                    else Talk(!Has("mio_met") ? "mio_greeting" : !Has("chen_story") ? "mio_day_repeat" : "mio_day_plan");
                    break;
                case "postcard": Talk(Has("postcard_found") ? "postcard_empty" : "postcard"); break;
                case "noticeboard": Talk("noticeboard"); break;
                case "bicycle": Talk("bicycle"); break;
                case "sea": Talk("sea"); break;
                case "bench": case "lighthouse_bench": Talk(session.Clock.Period == TimePeriod.Night ? "rest_night" : "rest"); break;
                case "radio": Talk("radio"); break;
                case "guestbook": Talk(Has("cafe_story") && !Has("chen_story") ? "guestbook_chen" : "guestbook"); break;
                case "coffee":
                    bool selfService = session.CurrentPlace == PlaceId.Cafe && !IsAvailable("npc_aya");
                    Talk(selfService ? (session.Clock.Period == TimePeriod.Night ? "coffee_self_night" : "coffee_self") : (session.Clock.Period == TimePeriod.Night ? "coffee_night" : "coffee"));
                    break;
                case "telescope": Talk(session.Clock.Period >= TimePeriod.Evening ? "telescope_evening" : "telescope"); break;
                case "letterbox":
                    Talk(!string.IsNullOrEmpty(session.State.ending) ? "letterbox_after" : !Has("postcard_found") ? "letterbox_first" : !Has("chen_story") ? "letterbox_wait" : !Has("evening_mio") ? "letterbox_mio" : Has("choice_invite") ? "ending_invite" : Has("choice_keep") ? "ending_keep" : "ending_deliver");
                    break;
                default: Notify("海风把声音吹得很远。"); break;
            }
        }
        public void AddJournal(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return;
            // Entries are authored unique; avoid duplicating them on repeat visits or resumed nodes.
            foreach (var existing in session.State.journal) if (existing.EndsWith(entry, StringComparison.Ordinal)) return;
            session.State.journal.Add("第 " + session.State.day + " 天 · " + GameClock.PeriodName(session.Clock.Period) + "\n" + entry);
        }
        public void ApplyEffects(StoryEffect[] effects)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                if (effect == null) continue;
                // A flagged effect is a transaction: replaying the same conversation cannot grant it twice.
                if (!string.IsNullOrEmpty(effect.flag) && Has(effect.flag)) continue;
                session.SetFlag(effect.flag);
                AddJournal(effect.journal);
                if (!string.IsNullOrEmpty(effect.item) && !session.State.items.Contains(effect.item)) session.State.items.Add(effect.item);
                if (!string.IsNullOrEmpty(effect.removeItem)) session.State.items.Remove(effect.removeItem);
                if (!string.IsNullOrEmpty(effect.ending)) session.State.ending = effect.ending;
                if (!string.IsNullOrEmpty(effect.relationship))
                {
                    var value = session.State.relationships.Find(r => r.id == effect.relationship);
                    if (value == null) { value = new RelationshipValue { id = effect.relationship }; session.State.relationships.Add(value); }
                    value.value += effect.relationshipDelta;
                }
                if (effect.advanceTime) session.Clock.AdvanceFromStory();
            }
            session.NotifyChanged();
        }
        public void Evaluate()
        {
            if (MemoryRuntimeConfig.Enabled) { return; }
            if (evaluating || !session || !session.Database) return;
            evaluating = true;
            try
            {
                foreach (var ev in session.Database.events)
                {
                    if (!ev || Has("event_" + ev.id) || session.State.day < ev.minimumDay || session.Clock.Period < ev.earliestPeriod || (!ev.anyPlace && session.CurrentPlace != ev.place)) continue;
                    bool valid = true;
                    foreach (var flag in ev.requiredFlags) if (!Has(flag)) { valid = false; break; }
                    if (!valid) continue;
                    foreach (var flag in ev.forbiddenFlags) if (Has(flag)) { valid = false; break; }
                    if (!valid) continue;
                    session.SetFlag("event_" + ev.id);
                    ApplyEffects(ev.effects);
                    Notify(ev.notification);
                }
            }
            finally { evaluating = false; }
            session.NotifyChanged();
        }
        void RefreshPhone()
        {
            phone.Clear();
            if (!session) return;
            if (MemoryRuntimeConfig.Enabled)
            {
                phone.Add("小岛通讯\n走近岛民按 E 交谈，也可以旁听附近正在发生的交流。");
                phone.Add("月亮岛\n人物按各自经历形成判断。剧情消息由引擎明确投递。");
                return;
            }
            phone.Add("汐风镇天气\n" + session.Clock.WeatherLabel + "。傍晚海堤风大，记得带件外套。");
            phone.Add("民宿房东 · 08:54\n钥匙放在门口的蓝罐子里。晚点回来也没关系，灯给你留着。今天不用赶路。");
            if (Has("mio_met") || Has("chen_story"))
                phone.Add("澪\n我黄昏会去灯塔洗掉最后一格胶卷——开玩笑的，是拍掉。要是你也有空，就来吧。晚一点也没事。");
            if (Has("cafe_story") && !Has("chen_story") && session.Clock.Period >= TimePeriod.Evening)
                phone.Add("阿遥\n陈伯今天提前回去了。他把旧邮路的事情写在店里的留言簿上了。翻到夹着蓝色书签的那页就能找到。");
            if (Has("choice_invite")) phone.Add("阿遥\n收到。最后一壶咖啡装进保温瓶了。陈伯说，他穿上那件没有补丁的外套就出门。");
            if (Has("evening_mio")) phone.Add("澪\n谢谢你过来。旧邮筒在灯塔旁边。你想写多久都可以，我帮你挡着风。");
            if (!string.IsNullOrEmpty(session.State.ending)) phone.Add("澪 · 刚刚\n照片冲出来以后，我寄一张给你。这次一定写上地址。\n\n今天的记忆：「" + session.State.ending + "」");
        }
    }
}
