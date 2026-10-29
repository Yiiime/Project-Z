using System;
using System.Collections;
using System.Text;
using ProjectZ.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace ProjectZ.Runtime
{
    public sealed class UnityStateCodec : IStateCodec
    {
        public string Encode(WorldState state) => JsonUtility.ToJson(state, true);
        public WorldState Decode(string json)
        {
            try { return JsonUtility.FromJson<WorldState>(json); }
            catch (Exception ex) { throw new System.IO.InvalidDataException("存档 JSON 无效。", ex); }
        }
    }

    public sealed class CharacterGateway : MonoBehaviour
    {
        public bool UseLiveService { get; set; }
        private UnityWebRequest current;
        private int generation;

        public void Cancel()
        {
            generation++;
            if (current != null) current.Abort();
            current = null;
        }

        public IEnumerator Respond(CharacterRequest request, Action<CharacterResponse, string> completed)
        {
            Cancel();
            var token = generation;
            if (!UseLiveService)
            {
                yield return new WaitForSecondsRealtime(0.2f);
                if (token == generation) completed(ResponseRules.Authored(request), null);
                yield break;
            }
            // The bridge runs on this machine. API credentials never enter the game or the save.
            using (var web = new UnityWebRequest("http://127.0.0.1:4318/character", "POST"))
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                web.timeout = 20;
                current = web;
                yield return web.SendWebRequest();
                if (token != generation) yield break;
                current = null;
                if (web.result != UnityWebRequest.Result.Success)
                {
                    completed(null, "在线回应暂时不可用。可以重试，或明确切换到预写对话。");
                    yield break;
                }
                CharacterResponse response = null;
                string error = null;
                try { response = JsonUtility.FromJson<CharacterResponse>(web.downloadHandler.text); }
                catch (Exception) { error = "在线服务返回了无法读取的回应。"; }
                completed(response, error);
            }
        }

        private void OnDestroy() => Cancel();
    }
}
