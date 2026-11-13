using System;
using UnityEngine;

namespace Seabreeze
{
    public sealed class GameClock : MonoBehaviour
    {
        GameSession session;
        public event Action Changed;
        public TimePeriod Period => session != null ? (TimePeriod)session.State.period : TimePeriod.Morning;
        public int Day => session != null ? session.State.day : 1;
        public int RestActions => session == null ? 0 : session.State.restActionDay == Day ? session.State.restActions : 0;
        public bool LiveReady => !(session?.FirstDay?.Active ?? false) && RestActions >= 4;
        public string TimeLabel => new[] { "上午  09:20", "午后  14:10", "傍晚  18:15", "夜晚  20:40" }[(int)Period];
        public string WeatherLabel => session != null ? session.State.weather : "晴";
        public void Initialize(GameSession owner) { session = owner; Refresh(); }
        public void Advance()
        {
            if (!session || session.Dialogue.IsOpen) return;
            AdvanceFromStory();
        }
        internal void AdvanceFromStory()
        {
            if (!session) return;
            if (session.FirstDay != null && session.FirstDay.Active)
            { session.Story?.Notify("先完成今天的相遇，休息后可以继续。 "); return; }
            if (session.State.restActionDay != Day) { session.State.restActionDay = Day; session.State.restActions = 0; }
            if (session.State.restActions >= 4) { session.Story?.Notify("今天的休息安排已完成，可以进入直播。 "); return; }
            session.State.restActions++;
            // Four rests advance the four daily slots. Only a completed live changes day.
            session.State.period = Math.Min(3, session.State.restActions);
            Refresh();
            session.Story?.Evaluate();
            session.Story?.Notify(Period == TimePeriod.Morning ? "新的一天，海风又从窗边吹了进来。" : "不知不觉，已经是" + PeriodName(Period) + "了。");
            session.NotifyChanged();
            session.Save();
        }
        public bool CompleteLiveSession(string sessionId)
        {
            if (!session || session.State == null || !Guid.TryParse(sessionId, out var id)) return false;
            if (session.FirstDay != null && session.FirstDay.Active && session.FirstDay.Phase != FirstDayPhase.EveningLive) return false;
            if (!(session.FirstDay?.Active ?? false) && !LiveReady) return false;
            string marker = "live_day_completed_" + id.ToString("N");
            if (session.HasFlag(marker) || session.State.day == int.MaxValue) return false;
            // Save the receipt and next morning together. Replay after loading is a no-op.
            session.SetFlag(marker);
            if (session.State.firstDay?.phase == FirstDayPhase.EveningLive) session.State.firstDay.phase = FirstDayPhase.Complete;
            session.State.day++;
            session.State.period = (int)TimePeriod.Morning;
            session.State.restActionDay = session.State.day;
            session.State.restActions = 0;
            Refresh();
            session.Story?.Evaluate();
            session.Story?.Notify("直播结束了。新的一天，海风又从窗边吹了进来。");
            session.NotifyChanged();
            session.Save();
            return true;
        }
        public void Refresh()
        {
            if (!session) return;
            session.State.weather = session.State.day % 3 == 0 ? "薄雾 · 微风" : Period == TimePeriod.Morning ? "晴 · 海风轻柔" : Period == TimePeriod.Afternoon ? "晴 · 云影缓行" : Period == TimePeriod.Evening ? "晴 · 晚风渐凉" : "晴 · 月色清朗";
            Changed?.Invoke();
        }
        public static string PeriodName(TimePeriod period) => new[] { "上午", "午后", "傍晚", "夜晚" }[(int)period];
    }
}
