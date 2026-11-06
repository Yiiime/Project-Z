namespace ZLive
{
    // Semantic live commands are mapped to Z's authored parameters; Haru arm curves
    // are deliberately not applied to a different model's rig.
    public static class ZLivePerformanceMap
    {
        public static string Expression(string mood)
        {
            switch (mood)
            {
                case "happy": case "warm": case "smile": return "smile";
                case "surprised": case "surprise": return "surprise";
                case "thinking": case "sad": case "troubled": return "troubled";
                case "shy": case "blush": return "blush";
                case "angry": case "cry": case "bawl": case "sigh": case "gloom": return mood;
                default: return "neutral";
            }
        }

        public static string Gesture(string action)
        {
            switch (action)
            {
                case "wave": case "handWave": case "wavePose": return "wave";
                case "explain": case "openArms": case "shrug": return "explain";
                case "point": case "present": case "presentRight": case "draw": return "point";
                default: return "none";
            }
        }

        public static string Motion(string mood, string action)
        {
            switch (mood)
            {
                case "cry": case "bawl": case "sigh": case "gloom": return mood;
            }
            switch (action)
            {
                case "nod": case "shake": case "tilt": case "lean": case "emphasis":
                case "startle": case "discover": case "lookLeft": case "lookRight": return action;
            }
            switch (mood)
            {
                case "happy": case "smile": return "happy";
                case "warm": return "warm";
                case "thinking": case "troubled": return "think";
                case "sad": return "sad";
                case "shy": case "blush": return "shy";
                case "angry": return "angry";
                case "surprised": case "surprise": return "surprise";
                case "serious": return "serious";
                default: return "nod";
            }
        }
    }
}
