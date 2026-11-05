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
        public DialogueFlowController Conversation { get; private set; }
        public StoryDatabase Database { get; private set; }
        public event Action Changed;
        public event Action<PlaceId> TravelRequested;
        public bool IsReady { get; private set; }
        bool overlay;
        public bool InputBlocked => overlay || (Dialogue && Dialogue.IsOpen) || (Conversation && Conversation.IsActive);
        public PlaceId CurrentPlace => State != null && Enum.TryParse(State.place, out PlaceId p) ? p : PlaceId.Street;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }
        public void Initialize()
        {
            if (IsReady) return;
            Instance = this;
            Database = Resources.Load<StoryDatabase>("Content/StoryDatabase");
            State = SaveRepository.Load();
            Clock = GetComponent<GameClock>() ?? gameObject.AddComponent<GameClock>();
            Dialogue = GetComponent<DialogueRunner>() ?? gameObject.AddComponent<DialogueRunner>();
            Story = GetComponent<StoryDirector>() ?? gameObject.AddComponent<StoryDirector>();
            Clock.Initialize(this);
            Dialogue.Initialize(this);
            Story.Initialize(this);
            Conversation = gameObject.AddComponent<DialogueFlowController>();
            Conversation.Initialize(this);
            IsReady = true;
            if (!Database) Debug.LogError("Seabreeze story database missing. Run Seabreeze/Rebuild Game Content in the Editor.");
            if (!State.started)
            {
                State.started = true;
                Story.AddJournal("来到汐风镇的第一天。今天没有行程，也许先沿着海边走走。咖啡馆在街道左侧，灯塔在右侧。");
                Dialogue.StartDialogue("intro");
            }
            else if (!string.IsNullOrEmpty(State.resumeDialogue)) Dialogue.StartDialogue(State.resumeDialogue, State.resumeInteractionId);
            Story.Evaluate();
            NotifyChanged();
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
            if (State == null) return false;
            bool saved = SaveRepository.Write(State);
            if (!saved && Story) Story.Notify("暂时无法写入存档，当前进度仍保留在本次游戏中。");
            return saved;
        }
        public void NewGame()
        {
            Dialogue.Cancel();
            Conversation.ResetImmediately();
            State = new SaveData { started = true };
            overlay = false;
            Story.ResetTransientState();
            Story.AddJournal("来到汐风镇的第一天。今天没有行程，也许先沿着海边走走。咖啡馆在街道左侧，灯塔在右侧。");
            Clock.Refresh();
            TravelRequested?.Invoke(PlaceId.Street);
            Dialogue.StartDialogue("intro");
            NotifyChanged();
            Save();
        }
        public void Travel(PlaceId place)
        {
            if (Conversation && Conversation.IsActive) { Story.Notify("先把这段话听完吧。"); return; }
            TravelRequested?.Invoke(place);
        }
        public void SetPlace(PlaceId place)
        {
            State.place = place.ToString();
            SetFlag("visited_" + place);
            Story?.Evaluate();
            NotifyChanged();
            Save();
        }
        void OnApplicationPause(bool paused) { if (paused && IsReady) Save(); }
        void OnApplicationQuit() { if (IsReady) Save(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
