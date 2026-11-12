using System;
using UnityEngine;

namespace Seabreeze
{
    /// <summary>Music alone: scene selection and interruption-safe crossfades, separate from ambience and speech.</summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        [Serializable] sealed class Score { public Track[] tracks; public float crossfadeSeconds = 2.4f; }
        [Serializable] sealed class Track { public string mood, title, resource; public float gain = .42f; }
        const string PreferenceKey = "Seabreeze.Music.Enabled";
        public static MusicDirector Instance { get; private set; }
        public event Action Changed;
        public bool MusicEnabled { get; private set; } = true;
        public string CurrentTitle => activeTrack != null ? activeTrack.title : "小岛音乐";
        public string CurrentMood => activeTrack != null ? activeTrack.mood : "";
        public bool CinematicSuppressed => cinematicSuppressed;

        GameSession session;
        Score score;
        Track activeTrack, pendingTrack;
        readonly AudioSource[] sources = new AudioSource[2];
        readonly float[] envelopes = new float[2];
        readonly float[] fadeFrom = new float[2];
        int activeSource;
        float fadeElapsed, masterGain, refreshTimer;
        string moodOverride;
        bool cinematicSuppressed, initialized;
        float FadeDuration => Mathf.Max(.15f, score?.crossfadeSeconds ?? 2.4f);

        public void Initialize(GameSession owner)
        {
            if (initialized) return;
            if (Instance && Instance != this) { Destroy(this); return; }
            Instance = this; session = owner; initialized = true;
            var data = Resources.Load<TextAsset>("Audio/Original/score");
            score = data ? JsonUtility.FromJson<Score>(data.text) : new Score();
            MusicEnabled = PlayerPrefs.GetInt(PreferenceKey, 1) != 0;
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false; sources[i].loop = true;
                sources[i].spatialBlend = 0; sources[i].volume = 0;
                sources[i].priority = 160;
                // NativeLiveHost pauses world audio; live voices explicitly ignore that pause.
                sources[i].ignoreListenerPause = false;
            }
            session.Changed += RefreshSelection; session.Clock.Changed += RefreshSelection;
            RefreshSelection();
        }

        public void ToggleMusic() => SetMusicEnabled(!MusicEnabled);
        public void SetMusicEnabled(bool enabled)
        {
            if (MusicEnabled == enabled) return;
            MusicEnabled = enabled;
            PlayerPrefs.SetInt(PreferenceKey, enabled ? 1 : 0); PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Mute only this score while a movie owns its soundtrack. Does not alter the user's preference.</summary>
        public void SetCinematicSuppressed(bool suppressed) { cinematicSuppressed = suppressed; }

        /// <summary>Optional title/day/evening/home/credits cue. Null or empty returns to place/time selection.</summary>
        public void SetMoodOverride(string mood) { moodOverride = mood; RefreshSelection(); }

        void RefreshSelection()
        {
            if (!initialized || session == null || score?.tracks == null) return;
            string mood = SelectMood();
            Track next = Array.Find(score.tracks, t => t.mood == mood);
            if (next == null || next == activeTrack) { pendingTrack = null; return; }
            if (activeTrack != null && next.resource == activeTrack.resource)
            {
                // Title and home share a theme: keep its musical position rather than layering two offsets.
                pendingTrack = null;
                activeTrack = next; fadeFrom[0] = envelopes[0]; fadeFrom[1] = envelopes[1]; fadeElapsed = 0;
                Changed?.Invoke(); return;
            }
            if (activeTrack != null && fadeElapsed < FadeDuration)
            {
                // Keep both audible sources until their crossfade ends. New requests replace
                // this single pending cue, so rapid travel never builds a playlist or cuts a voice.
                pendingTrack = next;
                return;
            }
            pendingTrack = null;
            BeginTrack(next);
        }

        void BeginTrack(Track next)
        {
            var clip = Resources.Load<AudioClip>(next.resource);
            if (!clip) { Debug.LogWarning("Missing original music cue: " + next.resource); return; }
            // Called only for the first cue or after the preceding fade completes;
            // the incoming slot is silent, so replacing its clip cannot drop the mix level.
            int incoming = 1 - activeSource;
            sources[incoming].Stop(); envelopes[incoming] = 0;
            sources[incoming].clip = clip; sources[incoming].volume = 0; sources[incoming].Play();
            activeSource = incoming; activeTrack = next;
            fadeFrom[0] = envelopes[0]; fadeFrom[1] = envelopes[1]; fadeElapsed = 0;
            Changed?.Invoke();
        }

        string SelectMood()
        {
            if (!string.IsNullOrEmpty(moodOverride)) return moodOverride;
            if (session.IsAtTitle) return "title";
            switch (session.CurrentPlace)
            {
                case PlaceId.Cafe: case PlaceId.Bookshop: case PlaceId.CabinInterior:
                case PlaceId.CaptainHome: case PlaceId.LiTingHome: case PlaceId.ZiqingHome:
                case PlaceId.ZHome: case PlaceId.ZhouHome: case PlaceId.ZBedroom: case PlaceId.PlayerHome: return "home";
            }
            return session.Clock.Period >= TimePeriod.Evening ? "evening" : "day";
        }

        void Update()
        {
            if (!initialized) return;
            float dt = Time.unscaledDeltaTime;
            // IsAtTitle can change without a scene change; refresh even when the clock is paused.
            refreshTimer -= dt;
            if (refreshTimer <= 0) { refreshTimer = .3f; RefreshSelection(); }
            float targetGain = MusicEnabled && !cinematicSuppressed ? 1 : 0;
            if (session != null && ((session.Memory != null && session.Memory.PlayerChatOpen) ||
                (session.Dialogue != null && session.Dialogue.IsOpen))) targetGain *= .55f;
            masterGain = Mathf.MoveTowards(masterGain, targetGain, dt * 2.5f);
            fadeElapsed += dt;
            float t = Mathf.Clamp01(fadeElapsed / FadeDuration);
            t = t * t * (3 - 2 * t);
            for (int i = 0; i < sources.Length; i++)
            {
                float target = i == activeSource && activeTrack != null ? activeTrack.gain : 0;
                envelopes[i] = Mathf.Lerp(fadeFrom[i], target, t);
                sources[i].volume = envelopes[i] * masterGain;
                if (t >= 1 && i != activeSource && sources[i].isPlaying) sources[i].Stop();
            }
            if (t >= 1 && pendingTrack != null)
            {
                var next = pendingTrack;
                pendingTrack = null;
                BeginTrack(next);
            }
        }

        void OnDestroy()
        {
            if (session != null) { session.Changed -= RefreshSelection; if (session.Clock != null) session.Clock.Changed -= RefreshSelection; }
            if (Instance == this) Instance = null;
            foreach (var source in sources) if (source) { source.Stop(); Destroy(source); }
        }
    }
}
