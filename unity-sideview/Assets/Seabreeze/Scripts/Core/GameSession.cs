using System;
using UnityEngine;

namespace Seabreeze
{
    [DefaultExecutionOrder(-900)]
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }
        public SaveData State;
        public GameClock Clock { get; private set; }
        public DialogueRunner Dialogue { get; private set; }
        public StoryDirector Story { get; private set; }
        public StoryDatabase Database { get; private set; }
        public MemoryGameController Memory { get; internal set; }
        public event Action Changed;
        public event Action<PlaceId> TravelRequested;
        public bool IsReady { get; private set; }
        public bool IsAtTitle { get; internal set; }
        public bool HasResumeSave { get; private set; }
        bool overlay;
        public bool NonPlayerInputBlocked => IsAtTitle || overlay || (Dialogue && Dialogue.IsOpen);
        public bool InputBlocked => NonPlayerInputBlocked || (Memory != null && Memory.PlayerChatOpen);
        public PlaceId CurrentPlace => State != null && Enum.TryParse(State.place, out PlaceId p) ? p : PlaceId.Street;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }
        public void Initialize(bool atTitle = false)
        {
            if (IsReady) return;
            Instance = this;
            IsAtTitle = atTitle;
            Database = Resources.Load<StoryDatabase>("Content/StoryDatabase");
            State = SaveRepository.Load();
            HasResumeSave = State.started;
            if (MemoryRuntimeConfig.Enabled && State.memory == null) State.memory = new MemorySaveState();
            Clock = GetComponent<GameClock>() ?? gameObject.AddComponent<GameClock>();
            Dialogue = GetComponent<DialogueRunner>() ?? gameObject.AddComponent<DialogueRunner>();
            Story = GetComponent<StoryDirector>() ?? gameObject.AddComponent<StoryDirector>();
            Clock.Initialize(this);
            Dialogue.Initialize(this);
            Story.Initialize(this);
            IsReady = true;
            if (!Database) Debug.LogError("Seabreeze story database missing. Run Seabreeze/Rebuild Game Content in the Editor.");
            if (!IsAtTitle) BeginStory();
            NotifyChanged();
        }

        void BeginStory()
        {
            if (!State.started)
            {
                State.started = true;
                if (MemoryRuntimeConfig.Enabled) Story.AddJournal("来到小岛。可以走近岛民按 E 交谈；坐长椅或喝咖啡推进游戏时段。");
                else { Story.AddJournal("来到汐风镇的第一天。今天没有行程，也许先沿着海边走走。咖啡馆在街道左侧，灯塔在右侧。"); Dialogue.StartDialogue("intro"); }
            }
            else if (!MemoryRuntimeConfig.Enabled && !string.IsNullOrEmpty(State.resumeDialogue)) Dialogue.StartDialogue(State.resumeDialogue);
            Story.Evaluate();
        }

        // The title scene may attach a backdrop while its save is still only a preview.
        // Read again on Continue so those presentation-only changes never become progress.
        internal bool PrepareTitleEntry(bool newGame)
        {
            var next = newGame ? new SaveData() : SaveRepository.Load();
            if (!newGame && !next.started) return false;
            Dialogue.Cancel();
            State = next;
            if (MemoryRuntimeConfig.Enabled && State.memory == null) State.memory = new MemorySaveState();
            overlay = false;
            Story.ResetTransientState();
            Clock.Refresh();
            BeginStory();
            NotifyChanged();
            return true;
        }
        public void SetOverlay(bool value) { overlay = value; }
        public bool HasFlag(string key) => State != null && !string.IsNullOrEmpty(key) && State.flags.Contains(key);
        public void SetFlag(string key)
        {
            if (!string.IsNullOrEmpty(key) && !HasFlag(key)) State.flags.Add(key);
        }
        public void NotifyChanged() { Changed?.Invoke(); }
        public bool Save()
        {
            if (IsAtTitle || State == null) return false;
            Memory?.BeforeSave();
            bool saved = SaveRepository.Write(State);
            if (!saved && Story) Story.Notify("暂时无法写入存档，当前进度仍保留在本次游戏中。");
            return saved;
        }
        public void NewGame()
        {
            Memory?.BeforeStateChange();
            Dialogue.Cancel();
            State = new SaveData { started = true };
            if (MemoryRuntimeConfig.Enabled) State.memory = new MemorySaveState();
            overlay = false;
            Story.ResetTransientState();
            Story.AddJournal(MemoryRuntimeConfig.Enabled ? "新的一次小岛生活。走近岛民按 E 交谈。" : "来到汐风镇的第一天。今天没有行程，也许先沿着海边走走。咖啡馆在街道左侧，灯塔在右侧。");
            Clock.Refresh();
            TravelRequested?.Invoke(PlaceId.Street);
            if (!MemoryRuntimeConfig.Enabled) Dialogue.StartDialogue("intro");
            Memory?.AfterStateChange();
            NotifyChanged();
            Save();
        }
        public void LoadSavedGame()
        {
            if (!MemoryRuntimeConfig.Enabled) return;
            Memory?.BeforeStateChange(); Dialogue.Cancel();
            State = SaveRepository.Load();
            if (State.memory == null) State.memory = new MemorySaveState();
            State.started = true; overlay = false; Story.ResetTransientState(); Clock.Refresh();
            TravelRequested?.Invoke(CurrentPlace);
            Memory?.AfterStateChange(); NotifyChanged();
        }
        public void Travel(PlaceId place)
        {
            if (Dialogue && Dialogue.IsOpen) { Story.Notify("先把这段话听完吧。"); return; }
            TravelRequested?.Invoke(place);
        }
        public void SetPlace(PlaceId place)
        {
            State.place = place.ToString();
            SetFlag("visited_" + place);
            if (!IsAtTitle) Story?.Evaluate();
            NotifyChanged();
            Save();
        }
        void OnApplicationPause(bool paused) { if (paused && IsReady) Save(); }
        void OnApplicationQuit() { if (IsReady) Save(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
