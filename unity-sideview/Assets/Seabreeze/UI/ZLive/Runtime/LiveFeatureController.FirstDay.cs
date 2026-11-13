using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Seabreeze;

namespace ZLive
{
    public sealed partial class LiveFeatureController
    {
        public string SceneMode = "daily";
        public Action<JObject> SceneCommand;
        string lastScenePhase;
        string StoryKey => SceneMode + "." + GameSession.Instance?.State?.memory?.worldId + "." + GameSession.Instance?.State?.day;
        JObject StoryBinding()
        {
            var game = GameSession.Instance;
            if (game?.State?.memory == null) return null;
            return new JObject { ["mode"] = SceneMode, ["playerName"] = PlayerIdentity.DisplayName,
                ["worldId"] = game.State.memory.worldId, ["day"] = game.State.day };
        }
        public async void AcceptArrival()
        {
            if (transport == null || SceneMode != "arrival") return;
            try { await transport.Input("story.accept_arrival", new JObject()); }
            catch (Exception error) { Error(error); lastScenePhase = null; ApplyScene(state); }
        }
        void ApplyScene(JObject snapshot)
        {
            var scene = snapshot["story"] as JObject;
            string phase = (string)scene?["phase"];
            if (phase == lastScenePhase) return;
            lastScenePhase = phase;
            if (SceneMode == "arrival" && phase == "offered") SceneCommand?.Invoke(new JObject {
                ["tool"] = "offer_arrival", ["title"] = scene["invitation"]?["title"], ["description"] = scene["invitation"]?["description"] });
            if (SceneMode == "arrival" && (phase == "accepted" || phase == "departed") && GameSession.Instance?.State?.firstDay != null)
            { GameSession.Instance.State.firstDay.invitationAccepted = true; GameSession.Instance.Save(); }
            if (SceneMode == "arrival" && phase == "departed") SceneCommand?.Invoke(new JObject { ["tool"] = "depart_island" });
        }
        async Task PresentScene(JObject command, CancellationToken cancel)
        {
            var payload = (JObject)command["payload"];
            if ((string)payload["tool"] == "draw_guest")
            {
                var panel = new VisualElement(); panel.style.position = Position.Absolute;
                panel.style.left = Length.Percent(22); panel.style.right = Length.Percent(22);
                panel.style.top = Length.Percent(33); panel.style.height = 180;
                panel.style.backgroundColor = new Color(.04f,.08f,.15f,.95f);
                panel.style.justifyContent = Justify.Center; panel.style.alignItems = Align.Center;
                var label = new Label(); label.style.fontSize = 34; label.style.color = Color.white;
                panel.Add(label); document.rootVisualElement.Add(panel);
                try
                {
                    float start = Time.unscaledTime;
                    while (Time.unscaledTime - start < 3.5f)
                    { cancel.ThrowIfCancellationRequested(); label.text = "幸运来客 · " + (1000 + Mathf.FloorToInt((Time.unscaledTime - start) * 31) % 137); await NextFrame(); }
                    label.text = (string)payload["playerName"]; await VisibleFrame(panel,cancel);
                    start = Time.unscaledTime; while (Time.unscaledTime - start < 1) { cancel.ThrowIfCancellationRequested(); await NextFrame(); }
                    await Report(command,new JObject { ["status"] = "displayed" });
                }
                finally { panel.RemoveFromHierarchy(); }
            }
            else
            {
                // Invitation visibility is reconstructed from the acknowledged session state.
                await VisibleFrame(document.rootVisualElement,cancel);
                await Report(command,new JObject { ["status"] = "displayed" });
                if ((string)payload["tool"] == "depart_island") SceneCommand?.Invoke(payload);
            }
        }
    }
}
