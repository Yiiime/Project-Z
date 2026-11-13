using System.Linq;
namespace Seabreeze
{
    // Initial positions only. Relationships guide the real conversation planner;
    // residents remain free to walk and player conversation always takes priority.
    public sealed class FirstDayGathering
    {
        static readonly string[] ids = { "z", "zhengyi", "xiaoxiaoqing", "linxu", "liting", "zhou_bobo", "axing" };
        static readonly float[] positions = { -6.4f, -5.3f, -1.5f, -.4f, .7f, 4.5f, 5.6f };
        public static bool TryPosition(string actor, out float x)
        { int i = System.Array.IndexOf(ids, actor); x = i < 0 ? 0 : positions[i]; return i >= 0; }
        public static bool Related(string actor, string other)
        {
            if (actor == "z" || other == "z") return true;
            string[] group = actor == "zhou_bobo" || actor == "axing" ? new[] { "zhou_bobo", "axing" }
                : actor == "zhengyi" ? new[] { "z", "zhengyi" } : new[] { "xiaoxiaoqing", "linxu", "liting" };
            return group.Contains(other);
        }
        public void BeginGreeting(string actor) { }
        public void EndGreeting() { }
        public void Tick(GameSession session, WorldDirector world) { }
        public void Reset() { }
    }
}
