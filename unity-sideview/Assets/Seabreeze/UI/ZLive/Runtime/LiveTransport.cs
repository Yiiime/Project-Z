using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZLive {
// Desktop native adapter. Provider credentials never enter this process.
public sealed class LiveTransport : IDisposable {
    // Keep protocol timestamps as ISO strings. JToken's Date -> string cast can lose
    // the UTC suffix, making valid commands appear expired in non-UTC time zones.
    public static JObject ParseWireJson(string json) {
        using(var text=new StringReader(json))using(var reader=new JsonTextReader(text){DateParseHandling=DateParseHandling.None})return JObject.Load(reader);
    }
    readonly HttpClient http;
    readonly CancellationTokenSource life = new CancellationTokenSource();
    public string BaseUrl {get;} public string Token {get;}
    public string SessionId {get; private set;} public string Lease {get; private set;} public int Epoch {get; private set;}
    public long Cursor {get;set;}
    public event Action<JObject> Event; public event Action<string> Connection;
    public LiveTransport(string baseUrl,string token,string certificateSha256=null) {
        var uri=new Uri(baseUrl);
        if(uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback))throw new Exception("HTTPS is required outside loopback.");
        var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        if(!string.IsNullOrEmpty(certificateSha256)) {
            if(uri.Scheme!="https"||!System.Text.RegularExpressions.Regex.IsMatch(certificateSha256,"^[a-f0-9]{64}$"))throw new Exception("invalid_certificate_pin");
            handler.ServerCertificateCustomValidationCallback=(request,certificate,chain,errors)=>{
                if(certificate==null||request.RequestUri.Host!=uri.Host)return false;
                using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(certificate.RawData)).Replace("-","").ToLowerInvariant()==certificateSha256;
            };
        }
        http=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        BaseUrl=baseUrl.TrimEnd('/');Token=token;http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    }
    public string Route(string suffix="")=>"/v1/live/sessions/"+SessionId+suffix;
    public async Task<JObject> Request(string route,JObject body=null,string key=null) {
        key=key??Guid.NewGuid().ToString();
        for(int attempt=0;;attempt++) {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token)) {
                timeout.CancelAfter(15000);
                try {
                    using(var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,BaseUrl+route)) {
                        if(body!=null){request.Content=new StringContent(body.ToString(Newtonsoft.Json.Formatting.None),Encoding.UTF8,"application/json");request.Headers.Add("Idempotency-Key",key);}
                        using(var response=await http.SendAsync(request,timeout.Token)) {
                            var value=ParseWireJson(await response.Content.ReadAsStringAsync());
                            if(!response.IsSuccessStatusCode)throw new LiveProtocolException((string)value["error"]??"request_failed");
                            return value;
                        }
                    }
                } catch(LiveProtocolException){throw;}
                catch(Exception) when(!life.IsCancellationRequested && attempt<2){await Task.Delay(350*(attempt+1),life.Token);}
            }
        }
    }
    public async Task<JObject> Enter(string previous,bool audio,bool character) {
        JObject snapshot=null;
        if(!string.IsNullOrEmpty(previous))try{snapshot=await Request("/v1/live/sessions/"+previous);}catch(LiveProtocolException e)when(e.Message=="not_found"){}
        if((string)snapshot?["phase"]=="ended")snapshot=null;
        if(snapshot==null)snapshot=await Request("/v1/live/sessions",new JObject{{"protocolVersion",1},{"mode","private"},{"capabilities",new JObject{{"audioEnabled",audio},{"characterReady",character}}}});
        SessionId=(string)snapshot["sessionId"];Lease=(string)snapshot["executorLeaseId"];Epoch=(int)snapshot["epoch"];Cursor=(long)snapshot["lastServerSeq"];return snapshot;
    }
    public JObject Envelope()=>new JObject{{"protocolVersion",1},{"eventId",Guid.NewGuid().ToString()},{"sessionId",SessionId},{"epoch",Epoch}};
    public Task<JObject> Input(string type,JObject payload){var b=Envelope();b["type"]=type;b["payload"]=payload;return Request(Route("/inputs"),b);}
    public Task<JObject> Control(string action,string task=null,int revision=0){var b=Envelope();b["action"]=action;if(task!=null){b["taskId"]=task;b["resourceRevision"]=revision;}return Request(Route("/control"),b);}
    public Task<JObject> Receipt(JObject command,JObject payload){var b=Envelope();b["type"]="execution.receipt";b["executorLeaseId"]=Lease;b["commandId"]=command["commandId"]?.DeepClone();b["taskId"]=command["taskId"]?.DeepClone()??JValue.CreateNull();b["resourceRevision"]=command["resourceRevision"]?.DeepClone()??JValue.CreateNull();b["payload"]=payload;return Request(Route("/receipts"),b);}
    public async Task<byte[]> Artifact(string id,CancellationToken cancel){if(id==null||!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{64}$"))throw new Exception("invalid_artifact");using(var r=await http.GetAsync(BaseUrl+Route("/artifacts/"+id),cancel)){r.EnsureSuccessStatusCode();if(r.Content.Headers.ContentLength>12*1024*1024)throw new Exception("artifact_too_large");var bytes=await r.Content.ReadAsByteArrayAsync();if(bytes.Length>12*1024*1024)throw new Exception("artifact_too_large");using(var sha=System.Security.Cryptography.SHA256.Create()){var actual=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();if(actual!=id)throw new Exception("artifact_hash_mismatch");}return bytes;}}
    public async Task<JObject> Transcribe(byte[] pcm,int version,CancellationToken cancel,bool dictation=false) {
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token,cancel)) {
            timeout.CancelAfter(35000);
            using(var request=new HttpRequestMessage(HttpMethod.Post,BaseUrl+Route(dictation?"/dictation":"/asr"))) {
                request.Headers.Add("X-Live-Call-Version",version.ToString());
                request.Content=new ByteArrayContent(pcm);
                request.Content.Headers.ContentType=new MediaTypeHeaderValue("audio/pcm");
                using(var response=await http.SendAsync(request,timeout.Token)) {
                    var result=ParseWireJson(await response.Content.ReadAsStringAsync());
                    if(!response.IsSuccessStatusCode)throw new LiveProtocolException((string)result["error"]??"asr_unavailable");
                    return result;
                }
            }
        }
    }
    public async Task ReadPcm(string id,Action<JObject> receive,CancellationToken cancel) {
        if(!Guid.TryParse(id,out _))throw new LiveProtocolException("invalid_audio_stream");
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token,cancel)) {
            timeout.CancelAfter(40000);
            using(var response=await http.GetAsync(BaseUrl+Route("/audio-stream/"+id),HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                if(!response.IsSuccessStatusCode)throw new LiveProtocolException("tts_stream_failed");
                using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(timeout.Token.Register(()=>stream.Dispose()))using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                    int bytes=0;bool done=false;while(!done){timeout.Token.ThrowIfCancellationRequested();string line=await reader.ReadLineAsync().ConfigureAwait(false);if(line==null)throw new LiveProtocolException("tts_stream_incomplete");bytes+=line.Length;if(bytes>8000000||line.Length>2000000)throw new LiveProtocolException("tts_stream_too_large");var item=ParseWireJson(line);receive(item);done=(string)item["type"]=="done";}
                }
            }
        }
    }
    public Task<JObject> Voice(string action,string callId=null){var b=Envelope();b["executorLeaseId"]=Lease;if(callId!=null)b["callId"]=callId;return Request(Route("/voice/"+action),b);}
    public async Task<JObject> EnrollVoice(byte[] wav,CancellationToken cancel) {
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token,cancel)) {
            timeout.CancelAfter(30000);
            using(var request=new HttpRequestMessage(HttpMethod.Post,BaseUrl+Route("/voice/enroll"))) {
                request.Headers.Add("X-Live-Epoch",Epoch.ToString());request.Headers.Add("X-Live-Executor-Lease",Lease);
                request.Content=new ByteArrayContent(wav);request.Content.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");
                using(var response=await http.SendAsync(request,timeout.Token)){var result=ParseWireJson(await response.Content.ReadAsStringAsync());if(!response.IsSuccessStatusCode)throw new LiveProtocolException((string)result["error"]??"voiceprint_registration_failed");return result;}
            }
        }
    }
    public async Task Heartbeat(){var b=Envelope();b["executorLeaseId"]=Lease;b["lastServerSeq"]=Cursor;await Request(Route("/heartbeat"),b);}
    public async Task Subscribe(){while(!life.IsCancellationRequested){try{
        using(var request=new HttpRequestMessage(HttpMethod.Get,BaseUrl+Route("/events?afterSeq="+Cursor)))using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,life.Token)){
            if(!response.IsSuccessStatusCode)throw new LiveProtocolException("resync_required");Connection?.Invoke("connected");
            using(var stream=await response.Content.ReadAsStreamAsync())using(var reader=new StreamReader(stream,Encoding.UTF8)){
                var data=new StringBuilder();while(!life.IsCancellationRequested){var line=await reader.ReadLineAsync();if(line==null)throw new IOException("stream_closed");if(line.Length>1500000||data.Length>2000000)throw new IOException("event_too_large");if(line.StartsWith("data:"))data.Append(line.Substring(5).TrimStart());if(line.Length==0&&data.Length>0){var evt=ParseWireJson(data.ToString());data.Clear();Event?.Invoke(evt);}}
            }
        }
    }catch(Exception)when(!life.IsCancellationRequested){Connection?.Invoke("disconnected");await Task.Delay(1500,life.Token);}}}
    public void Dispose(){life.Cancel();http.Dispose();life.Dispose();}
}
public sealed class LiveProtocolException:Exception {public LiveProtocolException(string code):base(code){}}
}
