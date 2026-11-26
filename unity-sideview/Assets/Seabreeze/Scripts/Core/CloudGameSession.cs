using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seabreeze
{
    // Project game sessions only. The browser and the gateway own the GitHub exchange.
    public sealed class CloudGameSession : IDisposable
    {
        public const string Origin = "https://api.litingyi.com";
        public const string MemoryUrl = Origin + "/memory";
        public const string LiveUrl = Origin + "/live";
        public static CloudGameSession Current { get; private set; }
        public string MemberId { get; private set; }
        public string PairingCode { get; private set; }
        public string LoginUrl { get; private set; }
        public bool SignedOut { get; private set; }
        readonly HttpClient http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        readonly SemaphoreSlim renewal = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly CloudRefreshStore store;
        string access, refresh;
        DateTimeOffset expires, renewAfter;

        public static bool Enabled
        {
            get
            {
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                if (Current != null) return true;
                if (Application.isBatchMode || !Application.isPlaying) return false;
                var args = Environment.GetCommandLineArgs();
                foreach (var flag in new[] { "-memoryService", "-memoryDisabled", "-memorySmokeOutput", "-executeMethod", "-runTests" })
                    if (Array.IndexOf(args, flag) >= 0) return false;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE")) ||
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT"))) return false;
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(SaveRepository.VerificationDirectory)) return false;
#endif
                var mode = Environment.GetEnvironmentVariable("Z_GAME_SERVICES");
                if (mode == "cloud") return true;
                if (mode == "local") return false;
#if UNITY_EDITOR
                if (File.Exists(MemoryRuntimeConfig.EditorConnectionFile)) return false;
#else
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                // Recognize the existing self-contained LAN/Day01 distributions, not a build beside source Tools/.
                bool localKit = File.Exists(Path.Combine(root, "local-source.json")) &&
                    File.Exists(Path.Combine(root, "Runtime/node"));
                bool lanKit = File.Exists(Path.Combine(root, "connection-config.json")) &&
                    File.Exists(Path.Combine(root, "Tools/LanPlay/start-client.mjs"));
                if (localKit || lanKit) return false;
#endif
                return true;
#else
                return false;
#endif
            }
        }

        CloudGameSession()
        {
            using (var sha = SHA256.Create())
            {
                // Two checkouts or an Editor and a standalone app must not rotate the same refresh credential.
                var slot = Application.isEditor ? Application.dataPath : Application.persistentDataPath;
                store = new CloudRefreshStore(Base64Url(sha.ComputeHash(Encoding.UTF8.GetBytes(slot))));
            }
        }
        public static CloudGameSession Open() => Current ?? (Current = new CloudGameSession());
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Current?.Dispose(); Current = null; }

        static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        static bool TrustedUri(Uri uri) => uri != null && uri.IsAbsoluteUri && uri.Scheme == "https" &&
            uri.Host == "api.litingyi.com" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

        public async Task LoginAsync()
        {
            var ct = lifetime.Token;
            if (SignedOut) throw new InvalidOperationException("本次登录已退出，请重新启动游戏。");
            refresh = store.Read();
            if (!string.IsNullOrEmpty(refresh))
            {
                try { await GetAccessAsync(null, ct); return; }
                catch (UnauthorizedAccessException) { /* A revoked or expired device must pair again. */ }
            }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (var callback = CloudLoginReturn.TryListen())
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(6));
                var cancel = deadline.Token;
                var random = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
                string verifier = Base64Url(random), challenge;
                using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                var start = new JObject { ["challenge"] = challenge };
                if (callback != null) start["loopbackPort"] = callback.Port;
                try
                {
                    using (var result = await Post("/auth/start", start, cancel))
                    {
                        if (!result.IsSuccessStatusCode) throw new InvalidOperationException("团队登录服务暂未就绪，请稍后重试。");
                        var data = JObject.Parse(await result.Content.ReadAsStringAsync());
                        var id = (string)data["id"];
                        if (string.IsNullOrEmpty(id) || !Uri.TryCreate((string)data["browserUrl"], UriKind.Absolute, out var url) ||
                            !TrustedUri(url) || url.AbsolutePath != "/auth/github/start")
                            throw new InvalidDataException("团队登录地址无效。");
                        PairingCode = (string)data["userCode"];
                        if (PairingCode == null || !System.Text.RegularExpressions.Regex.IsMatch(PairingCode, @"\A[0-9]{6}\z"))
                            throw new InvalidDataException("团队登录配对码无效。");
                        LoginUrl = url.AbsoluteUri;
                        // Allow the Game view to paint its pairing code before handing focus to the browser.
                        await Task.Delay(250, cancel);
                        Application.OpenURL(LoginUrl);
                        while (true)
                        {
                            await Task.Delay(2000, cancel);
                            using (var claim = await Post("/auth/claim", new JObject { ["id"] = id, ["verifier"] = verifier }, cancel))
                            {
                                if (claim.StatusCode == HttpStatusCode.Accepted) continue;
                                if (!claim.IsSuccessStatusCode)
                                    throw new InvalidOperationException(claim.StatusCode == HttpStatusCode.Forbidden
                                        ? "此账号未获准访问，或登录已取消。" : "本次配对已失效，请重新登录。");
                                Accept(await claim.Content.ReadAsStringAsync(), cancel);
                                callback?.BringGameForward();
                                return;
                            }
                        }
                    }
                }
                finally { PairingCode = LoginUrl = null; }
            }
        }

        Task<HttpResponseMessage> Post(string path, JObject json, CancellationToken ct) =>
            PostWithToken(path, json, null, ct);

        async Task<HttpResponseMessage> PostWithToken(string path, JObject json, string bearer, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, Origin + path))
            {
                request.Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                return await http.SendAsync(request, ct);
            }
        }

        void Accept(string json, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var reply = JObject.Parse(json);
            var member = (string)reply["memberId"];
            if (!ulong.TryParse(member, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) ||
                numeric == 0 || numeric.ToString(CultureInfo.InvariantCulture) != member || (MemberId != null && MemberId != member))
                throw new InvalidDataException("游戏登录身份不一致，请重新启动游戏。");
            var nextRefresh = (string)reply["refreshToken"];
            var nextAccess = (string)reply["accessToken"];
            int seconds = (int?)reply["expiresIn"] ?? 0;
            if (string.IsNullOrEmpty(nextRefresh) || string.IsNullOrEmpty(nextAccess) || seconds < 1)
                throw new InvalidDataException("游戏登录凭证无效。");
            store.Write(nextRefresh);
            MemberId = member;
            refresh = nextRefresh;
            access = nextAccess;
            expires = DateTimeOffset.UtcNow.AddSeconds(seconds);
            renewAfter = expires.AddSeconds(-Math.Min(25, seconds / 2));
        }

        internal async Task<string> GetAccessAsync(string rejected, CancellationToken ct)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct))
            {
                var cancel = linked.Token;
                await renewal.WaitAsync(cancel);
                try
                {
                    if (SignedOut) throw new UnauthorizedAccessException();
                    if (access != null && access != rejected && renewAfter > DateTimeOffset.UtcNow) return access;
                    if (refresh == null) throw new UnauthorizedAccessException("登录已过期，请重新启动游戏。");
                    await RotateAsync();
                    return access;
                }
                finally { renewal.Release(); }
            }
        }

        // Rotation belongs to the session, even if a business request times out mid-flight.
        // Both callers hold renewal until the replacement refresh credential is in Keychain.
        async Task RotateAsync()
        {
            using (var result = await Post("/auth/refresh", new JObject { ["refreshToken"] = refresh }, lifetime.Token))
            {
                if (result.StatusCode == HttpStatusCode.Unauthorized || result.StatusCode == HttpStatusCode.Forbidden)
                {
                    store.Clear();
                    refresh = access = null;
                    throw new UnauthorizedAccessException("登录已过期或已撤销，请重新启动游戏。");
                }
                result.EnsureSuccessStatusCode();
                Accept(await result.Content.ReadAsStringAsync(), lifetime.Token);
            }
        }

        public async Task LogoutAsync()
        {
            await renewal.WaitAsync(lifetime.Token);
            try
            {
                if (SignedOut) return;
                // Keep the local credential if the gateway cannot confirm revocation. An offline logout can be retried.
                if (refresh != null)
                {
                    try
                    {
                        if (access == null || expires <= DateTimeOffset.UtcNow.AddSeconds(10)) await RotateAsync();
                        using (var response = await PostWithToken("/auth/logout", new JObject(), access, lifetime.Token))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                await RotateAsync();
                                using (var retry = await PostWithToken("/auth/logout", new JObject(), access, lifetime.Token))
                                    retry.EnsureSuccessStatusCode();
                            }
                            else response.EnsureSuccessStatusCode();
                        }
                    }
                    catch (UnauthorizedAccessException) { /* Already revoked or expired at the gateway. */ }
                }
                store.Clear();
                access = refresh = null;
                SignedOut = true;
                lifetime.Cancel();
            }
            finally { renewal.Release(); }
        }

        public static HttpMessageHandler BusinessHandler(string baseUrl, HttpClientHandler transport)
        {
            if (baseUrl != LiveUrl && baseUrl != MemoryUrl) return transport;
            if (Current == null) throw new InvalidOperationException("请先完成团队登录。");
            transport.AllowAutoRedirect = false;
            transport.UseCookies = false;
            transport.UseProxy = true;
            return new CloudBearerHandler(Current, transport);
        }

        sealed class CloudBearerHandler : DelegatingHandler
        {
            readonly CloudGameSession owner;
            public CloudBearerHandler(CloudGameSession owner, HttpMessageHandler inner) : base(inner) { this.owner = owner; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var uri = request.RequestUri;
                if (!TrustedUri(uri) || !(uri.AbsolutePath.StartsWith("/live/", StringComparison.Ordinal) ||
                    uri.AbsolutePath.StartsWith("/memory/", StringComparison.Ordinal))) throw new InvalidOperationException("游戏服务地址无效。");
                string token = await owner.GetAccessAsync(null, ct);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await base.SendAsync(request, ct);
                if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
                response.Dispose();
                // The gateway rejects before forwarding. Preserve the business payload, idempotency key and stream cursor.
                using (var retry = new HttpRequestMessage(request.Method, uri))
                {
                    foreach (var header in request.Headers)
                        if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                            retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    if (request.Content != null)
                    {
                        retry.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync());
                        foreach (var header in request.Content.Headers) retry.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                    retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await owner.GetAccessAsync(token, ct));
                    return await base.SendAsync(retry, ct);
                }
            }
        }

        public void Dispose()
        {
            lifetime.Cancel(); http.Dispose();
            access = refresh = PairingCode = LoginUrl = null;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
