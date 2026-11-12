using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    // World interactions feed the existing durable task queue; task state stays in its checkpoint.
    public sealed class WorldTaskController : MonoBehaviour
    {
        sealed class Target
        {
            public string task, step, title;
            public JObject rule;
            public Interactable marker;
            public SpriteRenderer glint;
        }
        readonly List<Target> targets = new List<Target>();
        readonly Dictionary<string, JObject> definitions = new Dictionary<string, JObject>();
        readonly Dictionary<string, JObject> states = new Dictionary<string, JObject>();
        MemoryGameController memory;
        GameSession session;
        WorldDirector world;
        WorldScene stage;
        MemorySaveState timeline;
        string checkpoint;
        RectTransform tracker;
        TextMeshProUGUI trackerText;

        public void Initialize(MemoryGameController owner, GameSession s, WorldDirector w)
        {
            memory = owner; session = s; world = w;
            var asset = Resources.Load<TextAsset>("Memory/task-catalog");
            foreach (var t in JObject.Parse(asset.text)["tasks"].OfType<JObject>())
                definitions[(string)t["id"]] = t;
        }

        public string Title(string id) => definitions.TryGetValue(id, out var d) ? (string)d["title"] : id;
        public string Status(string id)
        {
            Refresh();
            return states.TryGetValue(id, out var s) ? (string)s["status"] : "available";
        }
        bool Pending(Target t) => session.State.memory.outbox.Any(e => e.kind == "task_receipt" && e.taskId == t.task && e.stepId == t.step);
        bool Ready(Target t)
        {
            if (Status(t.task) != "active") return false;
            var row = states[t.task];
            var progress = row["progress"] as JObject ?? JObject.Parse((string)row["progress"] ?? "{}");
            var step = definitions[t.task]["steps"].First(s => (string)s["id"] == t.step);
            return (bool?)progress[t.step]?["succeeded"] != true &&
                step["depends_on"].All(id => (bool?)progress[(string)id]?["succeeded"] == true);
        }

        public string Prompt(Interactable item)
        {
            var t = targets.Find(x => x.marker == item);
            if (t == null) return null;
            return Pending(t) ? "已查看 · 正在记录" : (string)t.rule["prompt"];
        }

        public bool Interact(Interactable item)
        {
            var t = targets.Find(x => x.marker == item);
            if (t == null) return false;
            // Also validate callers other than PlayerInteraction (e.g. delayed callbacks).
            if (!session.IsReady || session.InputBlocked || world.IsTransitioning || world.Stage != stage ||
                world.Player == null || Vector2.Distance(world.Player.position, item.transform.position) >= item.radius)
                return true;
            if (Pending(t)) { session.Story.Notify("已查看长椅，记录会在连接恢复后自动同步。"); return true; }
            if (!Ready(t)) { session.Story.Notify((string)t.rule["idle_text"]); return true; }
            memory.Client.ConfirmTaskStep(t.task, t.step, "player", new JObject
            {
                ["receipt_id"] = "interaction_" + session.State.memory.worldId + "_" + t.task + "_" + t.step,
                ["interaction_target"] = item.id, ["interaction_place"] = stage.place.ToString(),
                ["interaction_input"] = "E", ["interaction_x"] = world.Player.position.x,
                ["interaction_y"] = world.Player.position.y
            });
            session.Story.Notify("已查看长椅 · 正在记录委托进度");
            return true;
        }

        void Refresh()
        {
            var saved = session.State.memory;
            bool baseline = timeline != saved;
            if (!baseline && checkpoint == saved.checkpoint) return;
            var old = states.ToDictionary(p => p.Key, p => (string)p.Value["status"]);
            timeline = saved; checkpoint = saved.checkpoint; states.Clear();
            if (string.IsNullOrEmpty(checkpoint)) return;
            var cp = JObject.Parse(checkpoint);
            if ((string)cp["worldId"] != saved.worldId) return;
            foreach (var row in cp["tasks"]?["state"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string id = (string)row["task"], status = (string)row["status"];
                states[id] = row;
                if (!baseline && old.TryGetValue(id, out var before) && before != status && definitions.ContainsKey(id))
                {
                    if (status == "active") session.Story.Notify("已接受委托 · " + Title(id));
                    if (status == "completed") session.Story.Notify("委托完成 · " + Title(id));
                }
            }
        }

        void Bind()
        {
            stage = world.Stage; targets.Clear();
            foreach (var definition in definitions.Values)
            foreach (var step in definition["steps"].OfType<JObject>())
            {
                var rule = step["interaction"] as JObject;
                if (rule == null || (string)rule["place"] != stage.place.ToString()) continue;
                string id = (string)rule["target"];
                var marker = stage.GetComponentsInChildren<Interactable>(true).FirstOrDefault(i => i.id == id);
                if (marker == null)
                {
                    var root = new GameObject("Task interaction " + id);
                    root.transform.SetParent(stage.transform, false);
                    root.transform.position = new Vector3((float)rule["x"], (float)rule["y"], 0);
                    marker = root.AddComponent<Interactable>(); marker.id = id;
                    marker.radius = (float)rule["radius"]; marker.promptOffset = new Vector3(0, 2.25f, 0);
                }
                if (!world.Interactables.Contains(marker)) world.Interactables.Add(marker);
                // The previous map's notice board is anchored on top of this painted chair.
                // Disable that obsolete runtime prop only; the authored scene remains untouched.
                if (id == "street_corner_bench")
                    foreach (var old in stage.GetComponentsInChildren<Interactable>(true))
                        if (old.id == "noticeboard" && Vector2.Distance(old.transform.position, marker.transform.position) < .25f)
                            old.gameObject.SetActive(false);
                var glint = WorldArt.Sprite("Task marker", WorldArt.Oval, new Vector2(0, 1.65f), Vector2.one * .12f,
                    new Color(1, .84f, .55f), 35, marker.transform);
                targets.Add(new Target { task = (string)definition["id"], step = (string)step["id"],
                    title = (string)definition["title"], rule = rule, marker = marker, glint = glint });
            }
        }

        void CreateTracker()
        {
            var root = new GameObject("World task tracker", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 5;
            var scale = root.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
            var panel = new GameObject("Tracked task", typeof(RectTransform), typeof(Image));
            tracker = panel.GetComponent<RectTransform>(); tracker.SetParent(root.transform, false);
            tracker.anchorMin = tracker.anchorMax = tracker.pivot = Vector2.one;
            tracker.anchoredPosition = new Vector2(-34, -114); tracker.sizeDelta = new Vector2(370, 106);
            var image = panel.GetComponent<Image>(); image.color = new Color(.09f, .15f, .19f, .88f); image.raycastTarget = false;
            var label = new GameObject("Task instruction", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(tracker, false); trackerText = label.GetComponent<TextMeshProUGUI>();
            trackerText.font = Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            if (trackerText.font == null) trackerText.font = TMP_Settings.defaultFontAsset;
            trackerText.fontSize = 20; trackerText.color = new Color(.98f, .94f, .85f); trackerText.raycastTarget = false;
            trackerText.rectTransform.anchorMin = Vector2.zero; trackerText.rectTransform.anchorMax = Vector2.one;
            trackerText.rectTransform.offsetMin = new Vector2(16, 12); trackerText.rectTransform.offsetMax = new Vector2(-16, -12);
        }

        void Update()
        {
            if (session?.State?.memory == null) return;
            Refresh();
            if (world.Stage != null && !world.IsTransitioning && stage != world.Stage) Bind();
            foreach (var t in targets)
                if (t.glint != null) t.glint.enabled = Ready(t) && !Pending(t);
            var active = definitions.Values.FirstOrDefault(d => states.TryGetValue((string)d["id"], out var s) &&
                (string)s["status"] == "active" && d["steps"].Any(step => step["interaction"] != null));
            if (active != null && tracker == null) CreateTracker();
            if (tracker != null)
            {
                tracker.gameObject.SetActive(active != null && session.IsReady && !session.InputBlocked && !world.IsTransitioning);
                if (active != null) trackerText.text = (string)active["title"] + "\n<size=17>" +
                    (string)active["steps"][0]["label"] + "\nTab · 手机查看委托</size>";
            }
        }
    }
}
