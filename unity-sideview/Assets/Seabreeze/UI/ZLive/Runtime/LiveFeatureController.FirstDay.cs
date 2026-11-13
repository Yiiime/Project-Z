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
        bool invitationCollectedInGift;
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
            if (SceneMode == "arrival" && phase == "offered" && invitationCollectedInGift) AcceptArrival();
            else if (SceneMode == "arrival" && phase == "offered") SceneCommand?.Invoke(new JObject {
                ["tool"] = "offer_arrival", ["title"] = scene["invitation"]?["title"], ["description"] = scene["invitation"]?["description"] });
            if (SceneMode == "arrival" && (phase == "accepted" || phase == "departed") && GameSession.Instance?.State?.firstDay != null)
            { GameSession.Instance.State.firstDay.invitationAccepted = true; GameSession.Instance.Save(); }
            if (SceneMode == "arrival" && (phase == "accepted" || phase == "departed")) SceneCommand?.Invoke(new JObject { ["tool"] = "depart_island" });
        }
        async Task PresentScene(JObject command, CancellationToken cancel)
        {
            var payload = (JObject)command["payload"];
            if ((string)payload["tool"] == "draw_guest")
            {
                using (var view = new ArrivalGiftView(document.rootVisualElement,(string)payload["playerName"]))
                {
                    float idle=0,animation=0;
                    while(!view.Collected)
                    {
                        cancel.ThrowIfCancellationRequested();
                        view.SetPaused(locallyPaused||(!Application.isFocused&&!Application.isBatchMode));
                        if(!locallyPaused&&(Application.isFocused||Application.isBatchMode)) {
                            idle+=Time.unscaledDeltaTime;
                            if(view.Opened)animation+=Time.unscaledDeltaTime;
                            view.Tick(idle,animation);
                        }
                        await NextFrame();
                    }
                    invitationCollectedInGift=true;
                    await VisibleFrame(view.Root,cancel);
                    await Report(command,new JObject { ["status"]="displayed",["playerActivated"]=true,["playerName"]=(string)payload["playerName"] });
                    AcceptArrival();
                }
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
