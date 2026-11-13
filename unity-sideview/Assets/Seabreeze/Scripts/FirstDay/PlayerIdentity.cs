using System;
using System.Globalization;
using System.Text;

namespace Seabreeze
{
    public static class PlayerIdentity
    {
        public static string PendingName { get; set; } = "";
        public static string DisplayName => string.IsNullOrEmpty(GameSession.Instance?.State?.playerName) ? "玩家" : GameSession.Instance.State.playerName;
        public static string Normalize(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in (name ?? "").Trim().Normalize(NormalizationForm.FormC))
                if (!char.IsControl(c) && c != '<' && c != '>') clean.Append(c);
            var elements = StringInfo.GetTextElementEnumerator(clean.ToString());
            var result = new StringBuilder(); int count = 0;
            while (count++ < 12 && elements.MoveNext()) result.Append(elements.GetTextElement());
            return result.ToString().Trim();
        }
    }
}
