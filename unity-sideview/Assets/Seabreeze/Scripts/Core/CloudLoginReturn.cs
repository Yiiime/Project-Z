using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Seabreeze
{
    // This is only a browser return hint. Only the TLS claim using the original verifier authenticates.
    internal sealed class CloudLoginReturn : IDisposable
    {
        readonly HttpListener listener;
        public int Port { get; }
        CloudLoginReturn(HttpListener listener, int port) { this.listener = listener; Port = port; }

        public static CloudLoginReturn TryListen()
        {
            HttpListener listener = null;
            try
            {
                listener = new HttpListener();
                var candidate = new TcpListener(IPAddress.Loopback, 0);
                int port;
                try { candidate.Start(1); port = ((IPEndPoint)candidate.LocalEndpoint).Port; }
                finally { candidate.Stop(); }
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/project-z/");
                listener.Start();
                var result = new CloudLoginReturn(listener, port);
                _ = result.Serve();
                return result;
            }
            catch (Exception e) when (e is HttpListenerException || e is SocketException || e is NotSupportedException)
            {
                listener?.Close(); return null; // Polling completes the same transaction if loopback is unavailable.
            }
        }

        async Task Serve()
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    bool ok = context.Request.HttpMethod == "GET" && context.Request.RawUrl == "/project-z/complete" &&
                        context.Request.Headers["Host"] == "127.0.0.1:" + Port;
                    using (var response = context.Response)
                    {
                        response.StatusCode = ok ? 200 : 404;
                        response.ContentType = "text/plain; charset=utf-8";
                        response.Headers["Cache-Control"] = "no-store";
                        var content = Encoding.UTF8.GetBytes(ok ? "请返回 Unity 继续。" : "");
                        response.ContentLength64 = content.Length;
                        await response.OutputStream.WriteAsync(content, 0, content.Length);
                    }
                    if (ok) return;
                }
            }
            catch (Exception e) when (e is HttpListenerException || e is ObjectDisposedException || e is IOException) { }
        }

        public void BringGameForward()
        {
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app != IntPtr.Zero) Activate(app, sel_registerName("activateIgnoringOtherApps:"), 1);
#endif
        }

        public void Dispose() => listener.Close();
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr objc_getClass(string name);
        [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr sel_registerName(string name);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern void Activate(IntPtr receiver, IntPtr selector, byte argument);
#endif
    }
}
