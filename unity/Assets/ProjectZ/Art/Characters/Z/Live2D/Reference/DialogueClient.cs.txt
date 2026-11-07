using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZDialogueDemo
{
    /// <summary>
    /// 只负责和本地对话服务通信。
    /// 服务端决定人设、近期上下文和长期记忆；客户端不会生成假回复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueClient : MonoBehaviour
    {
        private const string SaveIdPrefsKey = "ZDialogueDemo.SaveId";
        private const string DefaultBaseUrl = "http://localhost:8796";
        private const int MaxMessageCharacters = 2000;
        private const int MaxSegments = 3;
        private const int MaxSegmentCharacters = 240;

        [Header("服务")]
        [SerializeField] private string baseUrl = DefaultBaseUrl;
        [SerializeField] private string saveId = "main";
        [SerializeField, Min(1)] private int requestTimeoutSeconds = 20;
        [SerializeField, Min(1)] private int chatTimeoutSeconds = 130;

        [Header("生命周期")]
        [SerializeField] private bool initializeOnEnable;

        private readonly Dictionary<string, Coroutine> chatCoroutines =
            new Dictionary<string, Coroutine>();
        private readonly Dictionary<string, List<UnityWebRequest>> turnRequests =
            new Dictionary<string, List<UnityWebRequest>>();
        private readonly HashSet<UnityWebRequest> activeRequests =
            new HashSet<UnityWebRequest>();

        private Coroutine initializationCoroutine;
        private string activeTurnId;
        private string activeTurnSaveId;
        private string pendingAckTurnId;
        private string pendingAckSaveId;
        private bool ackInFlight;
        private int saveIdGeneration;
        private bool isQuitting;

        public event Action<HealthResponse> HealthUpdated;
        public event Action<SessionResponse> SessionLoaded;
        public event Action<string, string> TurnStarted;
        public event Action<ChatResponse> ChatTurnReady;
        public event Action<string, AckResponse> TurnAcknowledged;
        public event Action<string> TurnCancelled;
        public event Action<string, string> RequestFailed;

        public string BaseUrl
        {
            get => baseUrl;
            set => baseUrl = NormalizeBaseUrl(value);
        }

        public string SaveId => saveId;
        public string ActiveTurnId => activeTurnId;
        public string PendingAckTurnId => pendingAckTurnId;
        public bool IsBusy { get; private set; }
        public bool IsInitialized { get; private set; }
        public HealthResponse LastHealth { get; private set; }
        public SessionResponse LastSession { get; private set; }
        public ChatResponse LastResponse { get; private set; }
        public int LastMemoryCount { get; private set; }

        [Serializable]
        public sealed class HealthResponse
        {
            public string status;
            public string provider;
            public bool configured;
            public string skill_version;
        }

        [Serializable]
        public sealed class SessionResponse
        {
            public string save_id;
            public DialogueHistoryEntry[] history;
            public MemoryEntry[] memories;
        }

        [Serializable]
        public sealed class DialogueHistoryEntry
        {
            public string role;
            public string content;
            public string text;
            public string turn_id;
        }

        [Serializable]
        public sealed class MemoryEntry
        {
            public string key;
            public string value;
            public string evidence;
            public bool active = true;
        }

        [Serializable]
        public sealed class ChatResponse
        {
            public string turn_id;
            public ChatSegment[] segments;
            public string provider;
            public int memory_count;
        }

        [Serializable]
        public sealed class ChatSegment
        {
            public string text;
            public string expression;
            public string gesture;
            public string gaze;
        }

        [Serializable]
        public sealed class AckResponse
        {
            public bool ok;
            public string save_id;
            public string turn_id;
            public int memory_count;
        }

        [Serializable]
        private sealed class ChatRequest
        {
            public string save_id;
            public string turn_id;
            public string message;
        }

        [Serializable]
        private sealed class TurnRequest
        {
            public string save_id;
            public string turn_id;
        }

        [Serializable]
        private sealed class ErrorResponse
        {
            public string error;
            public string message;
        }

        private void Awake()
        {
            baseUrl = NormalizeBaseUrl(baseUrl);
            saveId = ReadSavedId(saveId);
        }

        private void OnEnable()
        {
            if (initializeOnEnable)
                Initialize();
        }

        private void OnDestroy()
        {
            var turnId = activeTurnId;
            var turnSaveId = activeTurnSaveId;
            if (!isQuitting && !string.IsNullOrEmpty(turnId))
            {
                InvalidateTurn(turnId);
                FireAndForgetCancel(turnId, turnSaveId);
            }
            AbortAllRequests();
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
            var turnId = activeTurnId;
            var turnSaveId = activeTurnSaveId;
            AbortAllRequests();
            if (!string.IsNullOrEmpty(turnId))
                FireAndForgetCancel(turnId, turnSaveId);
        }

        /// <summary>检查服务并加载当前存档。可由 DialoguePanel 或 QA 组件调用。</summary>
        public void Initialize()
        {
            if (initializationCoroutine != null)
                return;

            initializationCoroutine = StartCoroutine(InitializeRoutine());
        }

        public void CheckHealth()
        {
            StartCoroutine(CheckHealthRoutine(false));
        }

        public void LoadSession()
        {
            StartCoroutine(LoadSessionRoutine(saveId, saveIdGeneration));
        }

        public void SetSaveId(string value)
        {
            var normalized = NormalizeSaveId(value);
            if (string.IsNullOrEmpty(normalized))
            {
                RequestFailed?.Invoke(string.Empty, "存档名只能使用字母、数字、下划线和短横线");
                return;
            }

            var oldTurnId = activeTurnId;
            var oldTurnSaveId = activeTurnSaveId;
            if (!string.IsNullOrEmpty(oldTurnId))
            {
                InvalidateTurn(oldTurnId);
                StartCoroutine(PostCancelRoutine(oldTurnId, oldTurnSaveId, false));
                TurnCancelled?.Invoke(oldTurnId);
            }

            saveId = normalized;
            saveIdGeneration++;
            PlayerPrefs.SetString(SaveIdPrefsKey, saveId);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 发起新回合。若旧回合仍在等待，先向服务端发取消请求，并丢弃旧结果。
        /// </summary>
        public void SendMessage(string message)
        {
            message = (message ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                RequestFailed?.Invoke(string.Empty, "请输入内容");
                return;
            }

            if (message.Length > MaxMessageCharacters)
            {
                RequestFailed?.Invoke(string.Empty, "消息超过 2000 个字符");
                return;
            }

            if (!string.IsNullOrEmpty(pendingAckTurnId))
            {
                RequestFailed?.Invoke(pendingAckTurnId, "上一回合尚未保存，请先重试保存或取消");
                return;
            }

            var oldTurnId = activeTurnId;
            var oldTurnSaveId = activeTurnSaveId;
            if (!string.IsNullOrEmpty(oldTurnId))
            {
                InvalidateTurn(oldTurnId);
                StartCoroutine(PostCancelRoutine(oldTurnId, oldTurnSaveId, false));
            }

            var turnId = Guid.NewGuid().ToString("N");
            var requestSaveId = saveId;
            activeTurnId = turnId;
            activeTurnSaveId = requestSaveId;
            pendingAckTurnId = null;
            pendingAckSaveId = null;
            IsBusy = true;
            TurnStarted?.Invoke(turnId, message);
            chatCoroutines[turnId] = StartCoroutine(PostChatRoutine(turnId, requestSaveId, message));
        }

        /// <summary>取消当前等待或尚未确认的回合。</summary>
        public void CancelCurrentTurn()
        {
            var turnId = activeTurnId;
            var turnSaveId = activeTurnSaveId;
            if (string.IsNullOrEmpty(turnId))
                return;

            InvalidateTurn(turnId);
            StartCoroutine(PostCancelRoutine(turnId, turnSaveId, true));
            TurnCancelled?.Invoke(turnId);
        }

        /// <summary>
        /// UI 将完整回复显示后调用。只有当前待确认回合可以 ACK。
        /// </summary>
        public void AcknowledgeTurn(string turnId)
        {
            if (string.IsNullOrEmpty(turnId) || turnId != pendingAckTurnId || turnId != activeTurnId)
            {
                RequestFailed?.Invoke(turnId ?? string.Empty, "回合已过期，未发送确认");
                return;
            }

            if (ackInFlight)
                return;

            ackInFlight = true;
            StartCoroutine(PostAckRoutine(turnId, pendingAckSaveId));
        }

        /// <summary>ACK 网络失败时可由 UI/QA 重试，不会重新生成台词。</summary>
        public void RetryPendingAcknowledge()
        {
            if (string.IsNullOrEmpty(pendingAckTurnId) || ackInFlight)
                return;
            ackInFlight = true;
            StartCoroutine(PostAckRoutine(pendingAckTurnId, pendingAckSaveId));
        }

        private IEnumerator InitializeRoutine()
        {
            yield return CheckHealthRoutine(true);
            if (IsInitialized)
                yield return LoadSessionRoutine(saveId, saveIdGeneration);
            initializationCoroutine = null;
        }

        private IEnumerator CheckHealthRoutine(bool partOfInitialization)
        {
            using (var request = UnityWebRequest.Get(BuildUrl("/health")))
            {
                RegisterRequest(request);
                request.timeout = requestTimeoutSeconds;
                yield return request.SendWebRequest();
                UnregisterRequest(request);

                if (!IsSuccessful(request))
                {
                    IsInitialized = false;
                    RequestFailed?.Invoke(string.Empty, FormatRequestError(request));
                    yield break;
                }

                if (!TryParseJson(request.downloadHandler.text, out HealthResponse health, out var parseError))
                {
                    IsInitialized = false;
                    RequestFailed?.Invoke(string.Empty, parseError);
                    yield break;
                }

                LastHealth = health;
                // configured=false 仍表示本地服务可达，允许继续读取存档记忆；真正聊天时由服务端返回 503。
                IsInitialized = !string.IsNullOrWhiteSpace(health.status) &&
                                !string.Equals(health.status, "error", StringComparison.OrdinalIgnoreCase);
                HealthUpdated?.Invoke(health);
                if (!IsInitialized && partOfInitialization)
                    RequestFailed?.Invoke(string.Empty, "对话服务未就绪");
            }
        }

        private IEnumerator LoadSessionRoutine(string requestSaveId, int requestSaveGeneration)
        {
            var url = BuildUrl("/v1/session?save_id=" + Uri.EscapeDataString(requestSaveId));
            using (var request = UnityWebRequest.Get(url))
            {
                RegisterRequest(request);
                request.timeout = requestTimeoutSeconds;
                yield return request.SendWebRequest();
                UnregisterRequest(request);

                if (!IsSuccessful(request))
                {
                    RequestFailed?.Invoke(string.Empty, FormatRequestError(request));
                    yield break;
                }

                if (!TryParseJson(request.downloadHandler.text, out SessionResponse session, out var parseError))
                {
                    RequestFailed?.Invoke(string.Empty, parseError);
                    yield break;
                }

                if (requestSaveGeneration != saveIdGeneration || requestSaveId != saveId)
                    yield break;
                if (!string.IsNullOrEmpty(session.save_id) && session.save_id != requestSaveId)
                {
                    RequestFailed?.Invoke(string.Empty, "存档 ID 不匹配，已丢弃存档响应");
                    yield break;
                }

                LastSession = session;
                LastMemoryCount = session.memories == null ? 0 : session.memories.Length;
                SessionLoaded?.Invoke(session);
            }
        }

        private IEnumerator PostChatRoutine(string turnId, string requestSaveId, string message)
        {
            var body = JsonUtility.ToJson(new ChatRequest
            {
                save_id = requestSaveId,
                turn_id = turnId,
                message = message,
            });

            using (var request = CreateJsonRequest("POST", "/v1/chat", body))
            {
                RegisterRequest(request, turnId);
                request.timeout = chatTimeoutSeconds;
                yield return request.SendWebRequest();
                UnregisterRequest(request, turnId);

                if (!IsCurrentTurn(turnId))
                    yield break;

                if (!IsSuccessful(request))
                {
                    IsBusy = false;
                    RequestFailed?.Invoke(turnId, FormatRequestError(request));
                    RemoveChatCoroutine(turnId);
                    yield break;
                }

                if (!TryParseJson(request.downloadHandler.text, out ChatResponse response, out var parseError))
                {
                    IsBusy = false;
                    RequestFailed?.Invoke(turnId, parseError);
                    RemoveChatCoroutine(turnId);
                    yield break;
                }
                if (!ValidateChatResponse(turnId, response, out var validationError))
                {
                    IsBusy = false;
                    RequestFailed?.Invoke(turnId, validationError);
                    RemoveChatCoroutine(turnId);
                    yield break;
                }

                IsBusy = false;
                pendingAckTurnId = turnId;
                pendingAckSaveId = requestSaveId;
                LastResponse = response;
                LastMemoryCount = response.memory_count;
                ChatTurnReady?.Invoke(response);
                RemoveChatCoroutine(turnId);
            }
        }

        private IEnumerator PostAckRoutine(string turnId, string requestSaveId)
        {
            var body = JsonUtility.ToJson(new TurnRequest { save_id = requestSaveId, turn_id = turnId });
            using (var request = CreateJsonRequest("POST", "/v1/ack", body))
            {
                RegisterRequest(request, turnId);
                yield return request.SendWebRequest();
                UnregisterRequest(request, turnId);

                if (!IsCurrentTurn(turnId) || pendingAckTurnId != turnId)
                {
                    ackInFlight = false;
                    yield break;
                }

                if (!IsSuccessful(request))
                {
                    ackInFlight = false;
                    RequestFailed?.Invoke(turnId, FormatRequestError(request));
                    yield break;
                }

                if (!TryParseJson(request.downloadHandler.text, out AckResponse ack, out var parseError))
                {
                    ackInFlight = false;
                    RequestFailed?.Invoke(turnId, parseError);
                    yield break;
                }
                if (ack == null || ack.turn_id != turnId)
                {
                    ackInFlight = false;
                    RequestFailed?.Invoke(turnId, "确认回合 ID 缺失或不匹配");
                    yield break;
                }
                if (ack.memory_count > 0)
                    LastMemoryCount = ack.memory_count;
                ackInFlight = false;
                pendingAckTurnId = null;
                pendingAckSaveId = null;
                activeTurnId = null;
                activeTurnSaveId = null;
                LastResponse = null;
                TurnAcknowledged?.Invoke(turnId, ack);
            }
        }

        private IEnumerator PostCancelRoutine(string turnId, string requestSaveId, bool notify)
        {
            var body = JsonUtility.ToJson(new TurnRequest { save_id = requestSaveId, turn_id = turnId });
            using (var request = CreateJsonRequest("POST", "/v1/cancel", body))
            {
                RegisterRequest(request, turnId);
                yield return request.SendWebRequest();
                UnregisterRequest(request, turnId);
                if (notify && !IsSuccessful(request))
                    RequestFailed?.Invoke(turnId, FormatRequestError(request));
            }
        }

        private void InvalidateTurn(string turnId)
        {
            if (chatCoroutines.TryGetValue(turnId, out var coroutine) && coroutine != null)
                StopCoroutine(coroutine);
            chatCoroutines.Remove(turnId);
            AbortRequestsForTurn(turnId);

            if (activeTurnId == turnId)
            {
                activeTurnId = null;
                activeTurnSaveId = null;
            }
            if (pendingAckTurnId == turnId)
            {
                pendingAckTurnId = null;
                pendingAckSaveId = null;
            }
            ackInFlight = false;
            IsBusy = false;
        }

        private bool IsCurrentTurn(string turnId)
        {
            return !string.IsNullOrEmpty(turnId) && string.Equals(activeTurnId, turnId, StringComparison.Ordinal);
        }

        private void RemoveChatCoroutine(string turnId)
        {
            chatCoroutines.Remove(turnId);
        }

        private void FireAndForgetCancel(string turnId, string requestSaveId)
        {
            var body = JsonUtility.ToJson(new TurnRequest { save_id = requestSaveId, turn_id = turnId });
            var request = CreateJsonRequest("POST", "/v1/cancel", body);
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
        }

        private UnityWebRequest CreateJsonRequest(string method, string path, string body)
        {
            var request = new UnityWebRequest(BuildUrl(path), method)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? string.Empty)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = requestTimeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            return request;
        }

        private void RegisterRequest(UnityWebRequest request, string turnId = null)
        {
            if (request == null)
                return;
            activeRequests.Add(request);
            if (string.IsNullOrEmpty(turnId))
                return;
            if (!turnRequests.TryGetValue(turnId, out var list))
            {
                list = new List<UnityWebRequest>();
                turnRequests[turnId] = list;
            }
            list.Add(request);
        }

        private void UnregisterRequest(UnityWebRequest request, string turnId = null)
        {
            if (request == null)
                return;
            activeRequests.Remove(request);
            if (string.IsNullOrEmpty(turnId) || !turnRequests.TryGetValue(turnId, out var list))
                return;
            list.Remove(request);
            if (list.Count == 0)
                turnRequests.Remove(turnId);
        }

        private void AbortRequestsForTurn(string turnId)
        {
            if (string.IsNullOrEmpty(turnId) || !turnRequests.TryGetValue(turnId, out var list))
                return;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var request = list[i];
                if (request == null)
                    continue;
                request.Abort();
                request.Dispose();
                activeRequests.Remove(request);
            }
            turnRequests.Remove(turnId);
        }

        private void AbortAllRequests()
        {
            foreach (var request in activeRequests)
            {
                if (request == null)
                    continue;
                request.Abort();
                request.Dispose();
            }
            activeRequests.Clear();
            turnRequests.Clear();
        }

        private string BuildUrl(string path)
        {
            return NormalizeBaseUrl(baseUrl) + "/" + path.TrimStart('/');
        }

        private static bool IsSuccessful(UnityWebRequest request)
        {
            return request != null && request.result == UnityWebRequest.Result.Success;
        }

        private static string FormatRequestError(UnityWebRequest request)
        {
            if (request == null)
                return "网络请求失败";

            var body = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
            var detail = string.Empty;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var error = JsonUtility.FromJson<ErrorResponse>(body);
                    detail = !string.IsNullOrWhiteSpace(error?.message)
                        ? error.message
                        : error?.error;
                }
                catch (Exception)
                {
                    // 服务端可能返回纯文本，下面保留 Unity 的请求错误即可。
                }
            }

            var status = request.responseCode > 0 ? "HTTP " + request.responseCode : string.Empty;
            var requestError = string.IsNullOrWhiteSpace(request.error) ? string.Empty : request.error;
            var result = string.Join(" ", new[] { status, requestError, detail });
            return string.IsNullOrWhiteSpace(result) ? "网络请求失败" : result.Trim();
        }

        private static bool ValidateChatResponse(string localTurnId, ChatResponse response, out string error)
        {
            error = string.Empty;
            if (response == null)
            {
                error = "对话接口返回了无效 JSON";
                return false;
            }

            if (response.turn_id != localTurnId)
            {
                error = "回合 ID 不匹配，已丢弃回复";
                return false;
            }

            if (response.segments == null || response.segments.Length == 0 || response.segments.Length > MaxSegments)
            {
                error = "回复句数必须为 1 到 3 句";
                return false;
            }

            for (var i = 0; i < response.segments.Length; i++)
            {
                var segment = response.segments[i];
                if (segment == null || string.IsNullOrWhiteSpace(segment.text))
                {
                    error = "回复包含空台词";
                    return false;
                }

                segment.text = segment.text.Trim();
                if (segment.text.Length > MaxSegmentCharacters)
                {
                    error = "单句回复超过 240 个字符";
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseJson<T>(string json, out T value, out string error) where T : class
        {
            value = null;
            error = "接口返回了无效 JSON";
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "接口返回了空响应";
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return false;
            }

            if (value == null)
                return false;
            return true;
        }

        private static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultBaseUrl;
            return value.Trim().TrimEnd('/');
        }

        private static string ReadSavedId(string fallback)
        {
            var saved = PlayerPrefs.GetString(SaveIdPrefsKey, fallback);
            return string.IsNullOrEmpty(NormalizeSaveId(saved)) ? "main" : NormalizeSaveId(saved);
        }

        private static string NormalizeSaveId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
                return string.Empty;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var valid = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                            (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!valid || (i == 0 && !(char.IsLetterOrDigit(c))))
                    return string.Empty;
            }

            return value;
        }
    }
}
