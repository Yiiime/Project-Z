using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Seabreeze
{
    // Opt-in verification only. Production UI uses the small reference checkpoint.
    public static class MemoryVerificationSnapshot
    {
        public static async Task<JObject> Read(MemoryLabClient client)
        {
            if(!(Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"").StartsWith("/private/tmp/",StringComparison.Ordinal))
                throw new InvalidOperationException("Verification requires an isolated save root");
            var request=client.RequestBase();request.portable=true;
            using(var http=new HttpClient(new HttpClientHandler {UseProxy=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(10)})
            using(var body=new StringContent(JsonConvert.SerializeObject(request),Encoding.UTF8,"application/json"))
            {
                http.DefaultRequestHeaders.Add("X-Z-Memory","1");
                var response=await http.PostAsync(MemoryRuntimeConfig.Settings.serviceUrl+"/api/game/checkpoint",body);
                response.EnsureSuccessStatusCode();var reply=JObject.Parse(await response.Content.ReadAsStringAsync());
                return JObject.Parse((string)reply["checkpoint"]);
            }
        }
    }
}
