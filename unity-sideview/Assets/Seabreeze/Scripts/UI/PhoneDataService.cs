using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seabreeze
{
    public sealed class PhoneMessage
    {
        public string Id, ThreadId, SenderId, Title, Text, TaskId, DateLabel;
        public bool IsRead;
    }
    public sealed class PhoneTask
    {
        public string Id, Title, Description, OwnerId, Status;
        public List<PhoneTaskStep> Steps = new List<PhoneTaskStep>();
    }
    public sealed class PhoneTaskStep
    {
        public string Id, Label;
        public bool Complete, Ready;
    }
    public sealed class PhoneMemory
    {
        public string Id, Title, Text, Place, DateLabel, ImagePath;
        public string[] ActorIds = Array.Empty<string>();
        public bool IsPhoto;
    }
    public sealed class PhonePerson
    {
        public string Id, Name, Occupation;
        public Color Color;
        public List<PhoneKnownFact> Facts = new List<PhoneKnownFact>();
    }
    public sealed class PhoneKnownFact { public string Text, Source; }

    /// <summary>
    /// Read-only projection of player-visible game state. Phone actions only change
    /// phone preferences; task decisions and receipts remain owned by the game engine.
    /// </summary>
    public sealed class PhoneDataService : MonoBehaviour
    {
        readonly List<PhoneMessage> messages = new List<PhoneMessage>();
        readonly List<PhoneTask> tasks = new List<PhoneTask>();
        readonly List<PhoneMemory> memories = new List<PhoneMemory>();
        readonly List<PhonePerson> people = new List<PhonePerson>();
        readonly Dictionary<string, JObject> taskDefinitions = new Dictionary<string, JObject>();
        GameSession session;
        SaveData state;
        JObject checkpoint;
        string checkpointText, lastActor, lastConnection, lastWorldId, projectionSignature;
        int lastSequence = -1, lastOutboxCount = -1;
        float nextPoll;
        bool dirty = true, refreshing, catalogLoaded, checkpointValid;

        public event Action Changed;
        public IReadOnlyList<PhoneMessage> Messages => messages;
        public IReadOnlyList<PhoneTask> Tasks => tasks;
        public IReadOnlyList<PhoneMemory> Memories => memories;
        public IReadOnlyList<PhonePerson> People => people;
        public bool TaskDataAvailable { get; private set; }
        public string TaskDataHint { get; private set; } = "委托记录尚未同步。";
        public string ExperienceDataHint { get; private set; } = "";
        public int UnreadCount => messages.Count(m => !m.IsRead);
        public string PinnedTaskId => session != null ? session.State?.phone?.pinnedTaskId ?? "" : "";

        public void Initialize(GameSession owner)
        {
            if (session != null) session.Changed -= Invalidate;
            session = owner;
            if (session != null) session.Changed += Invalidate;
            state = null;
            dirty = true;
            Refresh();
        }

        void OnDestroy() { if (session != null) session.Changed -= Invalidate; }
        void Invalidate() { dirty = true; }
        void Update()
        {
            if (Time.unscaledTime < nextPoll || session == null || session.State == null) return;
            nextPoll = Time.unscaledTime + .5f;
            var memory = session.State.memory;
            var actor = session.Memory?.Conversations?.PlayerActor;
            string actorId = actor != null ? actor.npcId : "";
            string connection = session.Memory?.Client?.Status ?? "";
            if (dirty || state != session.State || lastWorldId != (memory?.worldId ?? "") || checkpointText != memory?.checkpoint
                || lastSequence != (memory?.nextSeq ?? 0) || lastOutboxCount != (memory?.outbox?.Count ?? 0)
                || lastActor != actorId || lastConnection != connection) Refresh();
        }

        public void Refresh()
        {
            if (refreshing || session == null || session.State == null) return;
            refreshing = true;
            try
            {
                bool stateChanged = state != session.State;
                if (stateChanged)
                {
                    state = session.State;
                    checkpointText = null;
                    checkpoint = null;
                    checkpointValid = false;
                    if (state.phone == null) state.phone = new PhoneSaveState();
                    state.phone.Sanitize();
                }
                var phone = state.phone;
                string worldId = state.memory?.worldId ?? "";
                // A changed memory world is a new timeline even if a caller reused SaveData.
                bool worldChanged = !string.IsNullOrEmpty(phone.worldId) && !string.IsNullOrEmpty(worldId) && phone.worldId != worldId;
                if (worldChanged)
                {
                    phone = state.phone = new PhoneSaveState();
                    checkpoint = null;
                    checkpointText = null;
                    checkpointValid = false;
                }
                if (!string.IsNullOrEmpty(worldId)) phone.worldId = worldId;
                ReadCheckpoint(stateChanged || worldChanged || lastWorldId != worldId);
                BuildMessages();
                BuildTasks();
                bool witnessed = ProjectWitnessedExperiences() || worldChanged;
                BuildMemoriesAndPeople();
                // A completed/declined task should not remain the active phone objective.
                if (!string.IsNullOrEmpty(phone.pinnedTaskId)
                    && TaskDataAvailable && !tasks.Any(t => t.Id == phone.pinnedTaskId && (t.Status == "active" || t.Status == "offered")))
                {
                    phone.pinnedTaskId = "";
                    witnessed = true;
                }
                lastWorldId = worldId;
                lastSequence = state.memory?.nextSeq ?? 0;
                lastOutboxCount = state.memory?.outbox?.Count ?? 0;
                lastActor = session.Memory?.Conversations?.PlayerActor?.npcId ?? "";
                lastConnection = session.Memory?.Client?.Status ?? "";
                dirty = false;
                if (witnessed) session.Save();
                NotifyProjectionChanged();
            }
            finally { refreshing = false; }
        }

        public void MarkThreadRead(string threadId)
        {
            if (session == null || session.State == null) return;
            if (state != session.State) Refresh();
            bool changed = false;
            foreach (var message in messages.Where(m => m.ThreadId == threadId && !m.IsRead))
            {
                if (!state.phone.readMessageIds.Contains(message.Id)) state.phone.readMessageIds.Add(message.Id);
                message.IsRead = true;
                changed = true;
            }
            if (changed) SavePreferences();
        }

        public void TogglePinnedTask(string id)
        {
            Refresh();
            if (state == null || !tasks.Any(t => t.Id == id && (t.Status == "active" || t.Status == "offered"))) return;
            state.phone.pinnedTaskId = state.phone.pinnedTaskId == id ? "" : id;
            SavePreferences();
        }

        public bool IsFavorite(string id) => session != null && session.State?.phone?.favoriteMemoryIds.Contains(id) == true;
        public void ToggleFavorite(string id)
        {
            if (session == null || session.State == null) return;
            if (state != session.State) Refresh();
            if (state == null || !memories.Any(m => m.Id == id)) return;
            if (!state.phone.favoriteMemoryIds.Remove(id)) state.phone.favoriteMemoryIds.Add(id);
            SavePreferences();
        }
        void SavePreferences() { session.Save(); NotifyProjectionChanged(); }

        void NotifyProjectionChanged()
        {
            // Backend activity can change checkpoint JSON without changing anything the
            // player can see. Keep a stable projection so scrolling is never rebuilt for it.
            var signature = new StringBuilder();
            AppendSignature(signature, state.memory?.worldId, TaskDataAvailable ? "1" : "0", TaskDataHint,
                ExperienceDataHint, PinnedTaskId);
            foreach (var message in messages)
                AppendSignature(signature, message.Id, message.ThreadId, message.SenderId, message.Title,
                    message.Text, message.TaskId, message.DateLabel, message.IsRead ? "1" : "0");
            foreach (var task in tasks)
            {
                AppendSignature(signature, task.Id, task.Title, task.Description, task.OwnerId, task.Status);
                foreach (var step in task.Steps)
                    AppendSignature(signature, step.Id, step.Label, step.Complete ? "1" : "0", step.Ready ? "1" : "0");
            }
            foreach (var memory in memories)
            {
                AppendSignature(signature, memory.Id, memory.Title, memory.Text, memory.Place, memory.DateLabel,
                    memory.ImagePath, memory.IsPhoto ? "1" : "0");
                AppendSignature(signature, memory.ActorIds);
            }
            foreach (var person in people)
            {
                AppendSignature(signature, person.Id, person.Name, person.Occupation, ColorUtility.ToHtmlStringRGBA(person.Color));
                foreach (var fact in person.Facts) AppendSignature(signature, fact.Text, fact.Source);
            }
            AppendSignature(signature, state.phone.favoriteMemoryIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
            string next = signature.ToString();
            if (next == projectionSignature) return;
            projectionSignature = next; // Set before callbacks, so a callback Refresh is a no-op.
            Changed?.Invoke();
        }

        static void AppendSignature(StringBuilder builder, params string[] fields)
        {
            // Length-prefix each value, including record boundaries, to avoid concatenation collisions.
            builder.Append(fields.Length).Append(':');
            foreach (string field in fields)
            {
                string value = field ?? "";
                builder.Append(value.Length).Append(':').Append(value);
            }
            builder.Append(';');
        }

        void ReadCheckpoint(bool force)
        {
            string text = state.memory?.checkpoint;
            if (!force && text == checkpointText) return;
            checkpointText = text;
            checkpoint = null;
            checkpointValid = false;
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var parsed = JObject.Parse(text);
                if (Integer(parsed["version"]) != 2 || Text(parsed["worldId"]) != state.memory.worldId) return;
                if (!(parsed["world"] is JObject world) || Integer(world["epoch"]) == null || Number(world["hour"]) == null
                    || !(parsed["records"] is JObject records) || !(records["event"] is JArray)
                    || !(parsed["tasks"] is JObject taskState) || !(taskState["state"] is JArray)) return;
                checkpoint = parsed;
                checkpointValid = true;
            }
            catch (JsonException) { }
            catch (FormatException) { }
            catch (ArgumentException) { }
            catch (InvalidCastException) { }
        }

        void BuildMessages()
        {
            messages.Clear();
            if (session.Story == null) return;
            foreach (string raw in session.Story.PhoneMessages)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                int split = raw.IndexOf('\n');
                string title = split < 0 ? "小岛通讯" : raw.Substring(0, split).Trim();
                string text = split < 0 ? raw : raw.Substring(split + 1).Trim();
                string id = "notice_" + StableId(raw);
                messages.Add(new PhoneMessage
                {
                    Id = id, ThreadId = "system_" + StableId(title), SenderId = "system", Title = title,
                    Text = text, TaskId = "", DateLabel = "小岛公告", IsRead = state.phone.readMessageIds.Contains(id)
                });
            }
        }

        void BuildTasks()
        {
            tasks.Clear();
            TaskDataAvailable = checkpointValid;
            TaskDataHint = checkpointValid ? "委托进度来自已同步的游戏记录。" : string.IsNullOrEmpty(checkpointText)
                ? "委托记录尚未同步；收到委托后会显示在这里。" : "暂时无法读取委托记录，等待同步后重试。";
            if (checkpointValid && session.Memory?.Client != null && !session.Memory.Client.Reachable)
                TaskDataHint = "当前显示已保存的委托记录，连接恢复后会继续同步。";
            if (!checkpointValid) return;
            if (!catalogLoaded)
            {
                var asset = Resources.Load<TextAsset>("Memory/task-catalog");
                try
                {
                    foreach (var definition in JObject.Parse(asset != null ? asset.text : "{}")["tasks"] ?? new JArray())
                        if (definition is JObject obj && !string.IsNullOrEmpty((string)obj["id"])) taskDefinitions[(string)obj["id"]] = obj;
                    catalogLoaded = true;
                }
                catch (JsonException) { }
            }
            if (taskDefinitions.Count == 0) { TaskDataAvailable = false; TaskDataHint = "暂时无法读取委托说明。"; return; }
            try
            {
                foreach (var row in (JArray)checkpoint["tasks"]["state"])
                {
                    if (!(row is JObject taskRow) || Text(taskRow["status"]) == null || Text(taskRow["task"]) == null)
                        throw new JsonException("Invalid task row.");
                    string status = Text(taskRow["status"]), id = Text(taskRow["task"]);
                    if (status != "offered" && status != "active" && status != "completed" && status != "declined") continue;
                    if (string.IsNullOrEmpty(id) || !taskDefinitions.TryGetValue(id, out var definition)) continue;
                    if (!(taskRow["progress"] is JObject) && taskRow["progress"]?.Type != JTokenType.String)
                        throw new JsonException("Invalid task progress container.");
                    var progress = taskRow["progress"] is JObject progressObject ? progressObject
                        : JObject.Parse(Text(taskRow["progress"]));
                    var task = new PhoneTask { Id = id, Title = (string)definition["title"] ?? id,
                        Description = (string)definition["description"] ?? "", OwnerId = (string)definition["owner"] ?? "", Status = status };
                    foreach (var step in definition["steps"] ?? new JArray())
                    {
                        string stepId = (string)step["id"];
                        bool complete = Succeeded(progress, stepId);
                        bool dependenciesComplete = (step["depends_on"] ?? new JArray()).All(d => Succeeded(progress, (string)d));
                        task.Steps.Add(new PhoneTaskStep { Id = stepId, Label = (string)step["label"] ?? stepId,
                            Complete = complete, Ready = status == "active" && !complete && dependenciesComplete });
                    }
                    tasks.Add(task);
                }
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidCastException || e is FormatException)
            {
                tasks.Clear(); TaskDataAvailable = false; TaskDataHint = "委托记录暂时无法读取，等待同步后重试。";
            }
        }

        bool ProjectWitnessedExperiences()
        {
            bool changed = false;
            ExperienceDataHint = !string.IsNullOrEmpty(checkpointText) && checkpoint == null
                ? "暂时无法读取同步经历，仍保留已记录的相识与回忆。" : "";
            if (checkpoint?["records"]?["event"] is JArray events)
            {
                int? epoch = Integer(checkpoint["world"]?["epoch"]);
                double? now = Number(checkpoint["world"]?["hour"]);
                foreach (var token in events.OrderBy(e => e is JObject obj ? Integer(obj["seq"]) ?? 0 : 0))
                {
                    if (!(token is JObject e) || Integer(e["seq"]) == null || Number(e["hour"]) == null
                        || Text(e["id"]) == null || Text(e["kind"]) == null || !(e["visible"] is JArray)
                        || e["speaker"] != null && Text(e["speaker"]) == null || e["recipient"] != null && Text(e["recipient"]) == null)
                    { ExperienceDataHint = "部分同步经历暂时无法读取，仍保留已记录的相识与回忆。"; continue; }
                    if (epoch.HasValue && Integer(e["epoch"]) != epoch) continue;
                    if (now.HasValue && Number(e["hour"]) > now.Value + .001) continue;
                    if (!Includes(e["visible"], "player")) continue;
                    string id = Text(e["id"]), speaker = Text(e["speaker"]), recipient = Text(e["recipient"]);
                    EventDate(Number(e["hour"]), out int day, out int period);
                    string place = ResolvePlace(Text(e["location"]));
                    // Merely overhearing two islanders does not establish acquaintance.
                    string channel = Text(e["channel"]);
                    bool spoken = Text(e["kind"]) == "claim" && (channel == "private_player_chat" || channel == "face_to_face");
                    if (spoken && speaker == "player") changed |= RecordMeeting(recipient, day, period, place, "当面交谈");
                    if (spoken && recipient == "player") changed |= RecordMeeting(speaker, day, period, place, "当面交谈");
                    // Only the host's explicit, verified story publication is a public fact.
                    if (!string.IsNullOrEmpty(id) && id.StartsWith("story_", StringComparison.Ordinal) && Text(e["kind"]) == "fact")
                        changed |= RecordMemory("event_" + id, "岛上见闻", Text(e["text"]), place, day, period, Array.Empty<string>(), id);
                }
            }
            foreach (var e in state.memory?.outbox ?? new List<MemoryEvent>())
            {
                if (e == null || !(e.observers ?? Array.Empty<string>()).Contains("player")) continue;
                if (e.kind == "utterance" && e.speakerId == "player") changed |= RecordMeeting(e.recipientId, e.day, e.period, e.location, "当面交谈");
                if (e.kind == "utterance" && e.recipientId == "player") changed |= RecordMeeting(e.speakerId, e.day, e.period, e.location, "当面交谈");
                if (e.kind == "story_event" && e.verified && !string.IsNullOrEmpty(e.id))
                    changed |= RecordMemory("event_" + e.id, string.IsNullOrEmpty(e.title) ? "岛上见闻" : e.title,
                        e.text, ResolvePlace(e.location), e.day, e.period, Array.Empty<string>(), e.id);
            }
            // Backfill historical direct encounters before observing today's open conversation.
            var actor = session.Memory?.Conversations?.PlayerActor;
            if (actor != null) changed |= RecordMeeting(actor.npcId, state.day, state.period, state.place, "当面交谈");
            foreach (var task in tasks.Where(t => t.Status == "completed"))
            {
                // Task state has no completion date/location. Even a newly received checkpoint
                // may acknowledge a queued receipt from an earlier period, so leave these unknown.
                changed |= RecordMemory("task_" + task.Id, task.Title, "已完成这份委托。\n" + task.Description,
                    "", 0, 0,
                    string.IsNullOrEmpty(task.OwnerId) ? Array.Empty<string>() : new[] { task.OwnerId }, task.Id);
            }
            return changed;
        }

        bool RecordMeeting(string actorId, int day, int period, string place, string source)
        {
            var actor = string.IsNullOrEmpty(actorId) || actorId == "player" ? null : SevenCharacterCatalog.Find(actorId);
            if (actor == null || state.phone.metPeople.Any(p => p.actorId == actorId)) return false;
            state.phone.metPeople.Add(new PhoneMetPerson { actorId = actorId, day = day, period = period, place = ResolvePlace(place), source = source });
            RecordMemory("met_" + actorId, "初次与" + actor.displayName + "交谈", "和" + actor.displayName + "当面打过招呼。",
                ResolvePlace(place), day, period, new[] { actorId }, "meeting_" + actorId);
            return true;
        }

        bool RecordMemory(string id, string title, string text, string place, int day, int period, string[] actors, string source)
        {
            if (string.IsNullOrWhiteSpace(text) || state.phone.memories.Any(m => m.id == id)) return false;
            state.phone.memories.Add(new PhoneSavedMemory { id = id, title = title, text = text, place = place ?? "", day = day,
                period = Mathf.Clamp(period, 0, 3), actorIds = actors, sourceId = source });
            return true;
        }

        void BuildMemoriesAndPeople()
        {
            memories.Clear(); people.Clear();
            foreach (var memory in state.phone.memories.AsEnumerable().Reverse())
                memories.Add(new PhoneMemory { Id = memory.id, Title = memory.title, Text = memory.text, Place = memory.place ?? "",
                    DateLabel = DateLabel(memory.day, memory.period), ActorIds = memory.actorIds ?? Array.Empty<string>(), IsPhoto = false, ImagePath = "" });
            foreach (var met in state.phone.metPeople)
            {
                var actor = SevenCharacterCatalog.Find(met.actorId);
                if (actor == null) continue;
                var person = new PhonePerson { Id = actor.id, Name = actor.displayName, Occupation = actor.occupation, Color = actor.Accent };
                person.Facts.Add(new PhoneKnownFact { Text = "已经当面打过招呼。", Source = DateLabel(met.day, met.period) + " · " + (met.source ?? "当面交谈") });
                foreach (var task in tasks.Where(t => t.OwnerId == actor.id))
                    person.Facts.Add(new PhoneKnownFact { Text = "委托「" + task.Title + "」：" + StatusLabel(task.Status), Source = "已同步的委托记录" });
                people.Add(person);
            }
        }

        static bool Includes(JToken token, string value) => token is JArray array && array.Any(item => Text(item) == value);
        static bool Succeeded(JObject progress, string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || progress[stepId] == null) return false;
            if (!(progress[stepId] is JObject receipt) || receipt["succeeded"]?.Type != JTokenType.Boolean)
                throw new JsonException("Invalid task progress.");
            return receipt["succeeded"].Value<bool>();
        }
        static string Text(JToken token) => token?.Type == JTokenType.String ? token.Value<string>() : null;
        static int? Integer(JToken token)
        {
            if (token?.Type != JTokenType.Integer) return null;
            return int.TryParse(token.ToString(), out int value) ? value : (int?)null;
        }
        static double? Number(JToken token)
        {
            if (token?.Type != JTokenType.Integer && token?.Type != JTokenType.Float) return null;
            return double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) && !double.IsNaN(value) && !double.IsInfinity(value) ? value : (double?)null;
        }
        static string StatusLabel(string status) => status == "offered" ? "待回应" : status == "active" ? "进行中" : status == "completed" ? "已完成" : "已婉拒";
        static string DateLabel(int day, int period) => day > 0 ? "第 " + day + " 天 · " + GameClock.PeriodName((TimePeriod)Mathf.Clamp(period, 0, 3)) : "时间未记录";
        static void EventDate(double? logicalHour, out int day, out int period)
        {
            day = 0; period = 0;
            if (!logicalHour.HasValue || double.IsNaN(logicalHour.Value) || double.IsInfinity(logicalHour.Value) || logicalHour.Value < 0) return;
            day = (int)Math.Floor(logicalHour.Value / 24) + 1;
            double hour = logicalHour.Value % 24;
            period = hour >= 20.6 ? 3 : hour >= 18.2 ? 2 : hour >= 14.1 ? 1 : 0;
        }
        static string ResolvePlace(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (Enum.TryParse<PlaceId>(value, out _)) return value;
            return SevenCharacterCatalog.Anchor(value)?.place ?? "";
        }
        static string StableId(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
