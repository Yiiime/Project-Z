using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Seabreeze.EndingDemo
{
    /// <summary>Independent seventh-night demo. The local authoring service owns the scenario and model decision.</summary>
    public sealed class EndingDemoView : MonoBehaviour
    {
        public string serviceUrl = "http://127.0.0.1:4319";
        [Serializable] public class ModelStatus { public bool configured; public string providerName; }
        [Serializable] public class Status { public string token; public ModelStatus model; }
        [Serializable] public class Memory { public string id, title, text, kind; public int day; public bool zKnows; }
        [Serializable] public class Scenario { public string title; public Memory[] memories; }
        [Serializable] public class ScenarioReply { public string revision; public Scenario scenario; }
        [Serializable] public class GenerateRequest { public string mode, revision; }
        [Serializable] public class Decision { public string choice, title, opening, reason, decision, lastLine; }
        [Serializable] public class Recollection { public string memoryId, title, text, original; public int day; }
        [Serializable] public class Outcome { public string status, label, text; }
        [Serializable] public class EndingRun { public string id, mode,worldId,runId,sourceName,playerName,lastMessage; public Decision decision; public Recollection[] recollections; public Outcome outcome; public int memoryCount,liveMemoryCount,sourceDay,sourceSeq; }
        [Serializable] class ErrorReply { public string error; }

        readonly Color paper = new Color(.93f, .91f, .87f), muted = new Color(.65f, .69f, .75f), gold = new Color(.85f, .73f, .54f);
        RectTransform content;
        TMP_FontAsset font;
        Status status;
        ScenarioReply scenario;
        EndingRun run;
        bool busy;
        int page;
        string error = "";
        Texture2D coast, portrait;

        void Start()
        {
            Application.runInBackground = true;
            font = Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
            if (font == null) font = TMP_Settings.defaultFontAsset;
            coast = Resources.Load<Texture2D>("Art/lighthouse");
            portrait = Resources.Load<Texture2D>("Art/CastMotion/z/z_idle_000");
            var root = new GameObject("Ending Demo Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000); scaler.matchWidthOrHeight = .5f;
            var bg = Rect("Coast", root.transform, 0, 0, 1600, 1000).gameObject.AddComponent<RawImage>(); bg.texture = coast;
            bg.color = new Color(.35f, .4f, .5f); bg.raycastTarget = false;
            Panel("Night", root.transform, 0, 0, 1600, 1000, new Color(.035f, .065f, .11f, .72f));
            content = Rect("Pages", root.transform, 0, 0, 1600, 1000);
            if (EventSystem.current == null) new GameObject("Ending Demo Event System", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);
            Dock(); StartCoroutine(LoadScenario());
        }
        RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(width, height); return rt;
        }
        void Panel(string name, Transform parent, float x, float y, float width, float height, Color color)
        { var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; }
        TextMeshProUGUI Text(string name, string value, float x, float y, float width, float height, float size, Color color)
        {
            var text = Rect(name, content, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color; text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false; return text;
        }
        void ScrollText(string name, string value, float x, float y, float width, float height, float size)
        {
            var outer = Rect(name, content, x, y, width, height);
            var scroll = outer.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 35;
            var viewport = Rect("Viewport", outer, 0, 0, width, height); viewport.gameObject.AddComponent<RectMask2D>();
            var textRect = Rect("Text", viewport, 0, 0, width - 15, height);
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.text = value; text.richText = false;
            text.fontSize = size; text.color = paper; text.lineSpacing = 12; text.textWrappingMode = TextWrappingModes.Normal;
            var fitter = textRect.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = textRect;
        }
        Button Button(string caption, float x, float y, float width, Action action, bool primary = false, bool enabled = true)
        {
            var image = Rect(caption, content, x, y, width, 56).gameObject.AddComponent<Image>();
            image.color = primary ? new Color(.85f, .76f, .61f) : new Color(.14f, .19f, .25f, .8f);
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.interactable = enabled;
            button.onClick.AddListener(() => { if (!busy) action(); });
            var text = Rect("Caption", image.transform, 15, 8, width - 30, 40).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = caption; text.fontSize = 20; text.alignment = TextAlignmentOptions.Center;
            text.color = primary ? new Color(.08f, .12f, .18f) : paper; text.raycastTarget = false;
            return button;
        }
        void Clear()
        {
            for (int i = content.childCount - 1; i >= 0; i--) { var child = content.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
            Text("Brand", "PROJECT Z  /  来自月亮的 Z", 60, 32, 650, 35, 18, muted);
        }
        void Dock()
        {
            Clear();
            if (portrait != null) { var z = Rect("Z", content, 1040, 170, 500, 1000).gameObject.AddComponent<RawImage>(); z.texture = portrait; z.color = new Color(.8f, .82f, .86f); z.raycastTarget = false; }
            Text("Day", "DAY 07    ·    23:17    ·    月亮岛码头", 130, 180, 850, 45, 19, gold);
            Text("Title", "把明天\n交还给你。", 130, 245, 850, 270, 88, paper);
            Text("Intro", "七天的回忆，走到了同一个夜晚。\n这一次，最后一句话留给 Z。", 138, 548, 820, 90, 25, muted);
            int known = 0; if (scenario?.scenario?.memories != null) foreach (var m in scenario.scenario.memories) if (m.zKnows) known++;
            Text("Count", scenario == null ? "正在连接本机经历库" : known + " 段知情经历  /  " + scenario.scenario.title, 138, 660, 850, 45, 21, gold);
            Button("上传故事文件 ↗", 138, 730, 260, () => Application.OpenURL(serviceUrl + "/editor"));
            Button("重新加载经历", 420, 730, 240, () => StartCoroutine(LoadScenario()));
            Button("听听她的选择 →", 138, 805, 300, () => StartCoroutine(Generate("model")), true, status?.model?.configured == true && scenario != null);
            Button("预览离开示例", 460, 805, 210, () => StartCoroutine(Generate("preview-leave")), false, scenario != null);
            Button("预览留下示例", 690, 805, 210, () => StartCoroutine(Generate("preview-stay")), false, scenario != null);
            Text("Note", string.IsNullOrEmpty(error) ? "模型自主选择需先连接；示例仅用于预览演出。" : error, 138, 885, 1200, 65, 19, muted);
        }
        IEnumerator LoadScenario()
        {
            busy = true; error = "";
            using (var request = UnityWebRequest.Get(serviceUrl + "/api/status")) {
                request.timeout = 10; yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) error = "请先启动结局服务（模块内的启动结局试玩.command），再点重新加载。";
                else status = JsonUtility.FromJson<Status>(request.downloadHandler.text);
            }
            if (string.IsNullOrEmpty(error)) using (var request = UnityWebRequest.Get(serviceUrl + "/api/scenario")) {
                request.SetRequestHeader("X-Ending-Token", status.token); request.timeout = 10; yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) error = "经历尚未加载，请确认服务后重试。";
                else scenario = JsonUtility.FromJson<ScenarioReply>(request.downloadHandler.text);
            }
            busy = false; Dock();
        }
        IEnumerator Generate(string mode)
        {
            if (scenario == null || status == null) yield break;
            busy = true; Clear(); Text("Waiting", "Z 正在想起这些日子", 240, 370, 1120, 85, 52, paper);
            Text("Waiting note", "她会先作出自己的选择，再回望每一段知情经历。", 240, 490, 1120, 60, 25, muted);
            var payload = JsonUtility.ToJson(new GenerateRequest { mode = mode, revision = scenario.revision });
            using (var request = new UnityWebRequest(serviceUrl + "/api/generate", "POST")) {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payload)); request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json"); request.SetRequestHeader("X-Ending-Token", status.token);
                request.timeout = 300; yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) {
                    var reply = JsonUtility.FromJson<ErrorReply>(request.downloadHandler.text);
                    error = reply != null && !string.IsNullOrEmpty(reply.error) ? reply.error : "生成未完成，请检查本机服务并重试。";
                } else { run = JsonUtility.FromJson<EndingRun>(request.downloadHandler.text); error = ""; page = 0; }
            }
            busy = false; if (string.IsNullOrEmpty(error)) Ending(); else Dock();
        }
        void Ending()
        {
            Clear(); Panel("Ending veil", content, 0, 92, 1600, 900, new Color(.035f, .065f, .10f, .7f));
            int count = run.recollections.Length, total = count + 3;
            Text("Mode", run.mode == "model" ? "她根据本局经历作出的选择" : "示例演出 · 不代表模型决策", 200, 125, 1200, 40, 18, gold);
            if (page == 0) {
                Text("Ending title", run.decision.title, 200, 250, 1200, 125, 64, paper);
                ScrollText("Opening", run.decision.opening, 200, 440, 1150, 320, 31);
            } else if (page <= count) {
                var memory = run.recollections[page - 1];
                Text("Day", "DAY " + memory.day.ToString("00") + "   ·   仍然记得", 200, 220, 1150, 45, 20, gold);
                Text("Memory title", memory.title, 200, 290, 1200, 110, 47, paper);
                ScrollText("Recollection", memory.text, 200, 440, 1150, 350, 29);
            } else if (page == count + 1) {
                Text("Decision title", "所以，这一次。", 200, 230, 1200, 100, 60, paper);
                ScrollText("Decision", run.decision.reason + "\n\n" + run.decision.decision, 200, 380, 1150, 410, 29);
            } else {
                Text("Outcome", run.outcome.label, 200, 220, 1200, 50, 23, gold);
                ScrollText("Aftermath", run.outcome.text, 200, 315, 1150, 220, 28);
                Text("Last line", run.decision.lastLine, 200, 610, 1150, 165, 45, paper);
            }
            Text("Progress", (page + 1) + " / " + total + "   ·   " + count + " 段知情经历", 700, 868, 550, 45, 18, muted);
            Button("← 上一段", 200, 850, 210, () => { page--; Ending(); }, false, page > 0);
            if (page < total - 1) Button("继续 →", 1190, 850, 210, () => { page++; Ending(); }, true);
            else Button("回到码头", 1190, 850, 210, Dock, true);
            Button("打开独立结局页面 ↗", 1030, 25, 370, () => Application.OpenURL(serviceUrl + "/ending?run=" + run.id));
        }
    }
}
