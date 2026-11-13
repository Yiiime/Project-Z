using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    public sealed partial class MemoryDialogueView
    {
        sealed class DialogueTask
        {
            public string Id, Owner, Status, Title, Detail;
        }

        sealed class TaskCard
        {
            public DialogueTask Task;
            public RectTransform Root;
            public TextMeshProUGUI Title, Detail;
            public Button Primary, Secondary;
        }

        readonly Dictionary<string, JObject> taskDefinitions = new Dictionary<string, JObject>(StringComparer.Ordinal);
        readonly List<TaskCard> taskCards = new List<TaskCard>();
        bool taskCatalogLoaded;
        string lastTaskCheckpoint, lastTaskActor;
        static readonly Color TaskFill = new Color(.77f, .81f, .90f, .84f);
        static readonly Color TaskDetailInk = new Color(.20f, .23f, .32f, .92f);

        // Tasks belong to their issuer, not whichever NPC happens to be on screen.
        // The checkpoint remains the source of truth; this is only a presentation projection.
        void RefreshTasks()
        {
            string checkpoint = session.State.memory?.checkpoint;
            if (checkpoint == lastTaskCheckpoint && selectedActor == lastTaskActor) return;
            lastTaskCheckpoint = checkpoint;
            lastTaskActor = selectedActor;
            LoadTaskDefinitions();
            var visible = new List<DialogueTask>();
            if (!string.IsNullOrEmpty(checkpoint) && !string.IsNullOrEmpty(selectedActor))
            {
                try
                {
                    var snapshot = JObject.Parse(checkpoint);
                    var tasks = snapshot["tasks"] as JObject;
                    foreach (var key in taskDefinitions.Where(kv => (bool?)kv.Value["dynamic"] == true).Select(kv => kv.Key).ToArray()) taskDefinitions.Remove(key);
                    foreach (JObject definition in tasks?["definitions"] as JArray ?? new JArray())
                        if (!string.IsNullOrEmpty((string)definition["id"])) taskDefinitions[(string)definition["id"]] = definition;
                    var rows = tasks?["state"] as JArray;
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    if (rows != null) foreach (var row in rows.OfType<JObject>())
                    {
                        string id = TaskString(row["task"]), status = TaskString(row["status"]);
                        if (string.IsNullOrEmpty(id) || (status != "offered" && status != "active") ||
                            !taskDefinitions.TryGetValue(id, out var definition) ||
                            TaskString(definition["owner"]) != selectedActor || !seen.Add(id)) continue;
                        visible.Add(new DialogueTask
                        {
                            Id = id, Owner = selectedActor, Status = status,
                            Title = TaskString(definition["title"]) ?? "一起完成这件事",
                            Detail = TaskDetail(definition, row, status)
                        });
                    }
                }
                catch (JsonException) { /* A partial checkpoint must never retain another actor's card. */ }
            }

            // Unrelated memory commits arrive often. Keep existing controls and scroll position
            // when the visible task projection has not changed.
            if (visible.Count == taskCards.Count && visible.Select((task, i) => SameTask(task, taskCards[i].Task)).All(same => same)) return;
            DestroyTaskCards();
            foreach (var task in visible) taskCards.Add(BuildTaskCard(task));
            RefreshHistoryLayout();
        }

        void LoadTaskDefinitions()
        {
            if (taskCatalogLoaded) return;
            taskCatalogLoaded = true;
            var asset = Resources.Load<TextAsset>("Memory/task-catalog");
            if (asset == null) return;
            try
            {
                var definitions = JObject.Parse(asset.text)["tasks"] as JArray;
                if (definitions == null) return;
                foreach (var definition in definitions.OfType<JObject>())
                {
                    string id = TaskString(definition["id"]);
                    if (!string.IsNullOrEmpty(id)) taskDefinitions[id] = definition;
                }
            }
            catch (JsonException) { /* Unknown definitions fail closed instead of leaking global tasks. */ }
        }

        static string TaskString(JToken token) => token?.Type == JTokenType.String ? token.Value<string>() : null;
        static bool SameTask(DialogueTask a, DialogueTask b) => a.Id == b.Id && a.Owner == b.Owner &&
            a.Status == b.Status && a.Title == b.Title && a.Detail == b.Detail;

        static string TaskDetail(JObject definition, JObject state, string status)
        {
            if (status == "offered") return "待回应 · 接受后记入任务";
            JObject progress = state["progress"] as JObject;
            if (progress == null)
            {
                try { progress = JObject.Parse(TaskString(state["progress"]) ?? "{}"); }
                catch (JsonException) { progress = new JObject(); }
            }
            bool Done(string step) => !string.IsNullOrEmpty(step) && progress?[step] is JObject receipt &&
                receipt["succeeded"]?.Type == JTokenType.Boolean && receipt["succeeded"].Value<bool>();
            var steps = definition["steps"] as JArray;
            if (steps != null) foreach (var step in steps.OfType<JObject>())
            {
                string id = TaskString(step["id"]);
                if (string.IsNullOrEmpty(id) || Done(id)) continue;
                var dependencies = step["depends_on"] as JArray;
                if (dependencies != null && dependencies.Any(d => !Done(TaskString(d)))) continue;
                return "进行中 · " + (TaskString(step["label"]) ?? "一起商量下一步吧。");
            }
            // No invented completion: the server still owns receipts and the terminal state.
            return "进行中 · 等待后续进展";
        }

        TaskCard BuildTaskCard(DialogueTask task)
        {
            var panel = (RoundedPanel)Panel("Task card / " + task.Id, content, Vector2.up, Vector2.up,
                Vector2.zero, new Vector2(600, 160), TaskFill, true);
            panel.rectTransform.anchorMax = Vector2.one;
            panel.CornerRadius = 26;
            panel.SetBorder(new Color(.96f, .97f, 1, .60f), 1);
            var card = new TaskCard { Task = task, Root = panel.rectTransform };
            var medallion = (RoundedPanel)Panel("Task icon", card.Root, Vector2.up, Vector2.up,
                new Vector2(22, -24), new Vector2(48, 48), new Color(.96f, .97f, 1, .26f));
            medallion.CornerRadius = 24;
            medallion.SetBorder(Color.clear, 0);
            var icon = Rect("Task outline", medallion.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(25, 25)).gameObject.AddComponent<LineIcon>();
            icon.symbol = LineIcon.Symbol.Journal;
            icon.color = BubbleInk;
            icon.raycastTarget = false;
            card.Title = Label("Task title", card.Root, Vector2.up, Vector2.up, new Vector2(90, -22),
                new Vector2(480, 36), task.Title, 24, BubbleInk);
            card.Detail = Label("Task detail", card.Root, Vector2.up, Vector2.up, new Vector2(90, -62),
                new Vector2(480, 48), task.Detail, 18, TaskDetailInk);
            card.Title.richText = card.Detail.richText = false;
            card.Title.lineSpacing = card.Detail.lineSpacing = 3;
            bool offered = task.Status == "offered";
            card.Primary = TaskAction(card.Root, offered ? "接受" : "邀请协作", -22, true, () =>
            {
                if (!CanActOnTask(card)) return;
                card.Primary.interactable = false;
                if (card.Secondary != null) card.Secondary.interactable = false;
                if (offered) memory.RespondTask(task.Id, true);
                else memory.InviteTask(task.Id, task.Owner, "我接受了这项委托。你愿意和我一起做吗？只帮一部分或者另约也可以。");
            });
            if (offered) card.Secondary = TaskAction(card.Root, "暂不接受", -150, false, () =>
            {
                if (!CanActOnTask(card)) return;
                card.Primary.interactable = card.Secondary.interactable = false;
                memory.RespondTask(task.Id, false);
            });
            return card;
        }

        Button TaskAction(RectTransform parent, string caption, float right, bool primary, Action action)
        {
            var panel = (RoundedPanel)Panel(caption, parent, Vector2.right, Vector2.right, new Vector2(right, 18),
                new Vector2(116, 40), primary ? new Color(.96f, .97f, 1, .52f) : new Color(1, 1, 1, .09f), true);
            panel.CornerRadius = 20;
            panel.SetBorder(new Color(.96f, .97f, 1, primary ? .65f : .38f), 1);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.highlightedColor = new Color(.89f, .93f, 1, 1);
            colors.pressedColor = new Color(.72f, .81f, .96f, 1);
            colors.disabledColor = new Color(.83f, .87f, .95f, .55f);
            colors.fadeDuration = .14f;
            button.colors = colors;
            button.onClick.AddListener(() => action());
            Label("Caption", panel.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(108, 32), caption, 18, BubbleInk, TextAlignmentOptions.Center);
            return button;
        }

        bool CanActOnTask(TaskCard card) => PlayerInputReady && memory.Client.CanGenerate &&
            !memory.Conversations.Sending && memory.Conversations.PlayerActor?.npcId == card.Task.Owner &&
            selectedActor == card.Task.Owner && taskCards.Contains(card) &&
            lastTaskCheckpoint == session.State.memory?.checkpoint && lastTaskActor == selectedActor;

        void UpdateTaskActions(bool visible)
        {
            foreach (var card in taskCards)
            {
                bool canAct = visible && CanActOnTask(card);
                card.Primary.interactable = canAct;
                if (card.Secondary != null) card.Secondary.interactable = canAct;
            }
        }

        float LayoutTaskCards(float width, float y)
        {
            if (taskCards.Count > 0) y += 14;
            foreach (var card in taskCards)
            {
                float textWidth = Mathf.Max(100, width - 116);
                float titleHeight = Mathf.Max(34, card.Title.GetPreferredValues(card.Title.text, textWidth, 0).y);
                float detailHeight = Mathf.Max(28, card.Detail.GetPreferredValues(card.Detail.text, textWidth, 0).y);
                float height = 22 + titleHeight + 10 + detailHeight + 18 + 40 + 18;
                card.Root.anchoredPosition = new Vector2(8, -y);
                card.Root.sizeDelta = new Vector2(-16, height);
                card.Title.rectTransform.sizeDelta = new Vector2(textWidth, titleHeight);
                card.Detail.rectTransform.anchoredPosition = new Vector2(90, -(22 + titleHeight + 10));
                card.Detail.rectTransform.sizeDelta = new Vector2(textWidth, detailHeight);
                y += height + 16;
            }
            return y;
        }

        void DestroyTaskCards()
        {
            foreach (var card in taskCards)
            {
                card.Root.gameObject.SetActive(false);
                Destroy(card.Root.gameObject);
            }
            taskCards.Clear();
        }

        void ClearTaskCards()
        {
            DestroyTaskCards();
            lastTaskActor = lastTaskCheckpoint = null;
        }
    }
}
