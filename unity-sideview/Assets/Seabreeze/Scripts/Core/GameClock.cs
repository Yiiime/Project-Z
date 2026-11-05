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
        public string TimeLabel => new[] { "上午  09:20", "午后  14:10", "傍晚  18:15", "夜晚  20:40" }[(int)Period];
        public string WeatherLabel => session != null ? session.State.weather : "晴";
        public void Initialize(GameSession owner) { session = owner; Refresh(); }
        public void Advance()
        {
            if (!session || session.InputBlocked) return;
            AdvanceFromStory();
            session.Save();
        }
        internal void AdvanceFromStory()
        {
            if (!session) return;
            session.State.period++;
            if (session.State.period > 3) { session.State.period = 0; session.State.day++; }
            Refresh();
            session.Story?.Evaluate();
            session.Story?.Notify(Period == TimePeriod.Morning ? "新的一天，海风又从窗边吹了进来。" : "不知不觉，已经是" + PeriodName(Period) + "了。");
            session.NotifyChanged();
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
