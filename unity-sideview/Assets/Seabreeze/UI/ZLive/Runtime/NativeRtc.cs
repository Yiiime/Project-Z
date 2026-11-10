using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ZLive {
// C ABI avoids an unsupported Unity/macOS vendor SDK. Only short-lived room
// tokens cross this boundary; AppKey and AK/SK remain on the Node service.
public static class NativeRtc {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StartFn(string app,string room,string user,string token,string library);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StateFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int MuteFn(int muted);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StopFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ReferenceFn([In] float[] samples,int count,int channels,int rate,float gain);
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr dlopen(string file,int mode);
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern IntPtr dlsym(IntPtr handle,string name);
    static IntPtr library;static MuteFn mute;static StartFn start;static StateFn state;static StopFn stop;static ReferenceFn reference;
    static string directory;static volatile bool active;
    public static string RuntimeDirectory {
        get {
#if UNITY_EDITOR_OSX
            return Path.GetFullPath(Path.Combine(Application.dataPath,"../Tools/native-rtc/.build/runtime"));
#elif UNITY_STANDALONE_OSX
            return Path.GetFullPath(Path.Combine(Application.dataPath,"Frameworks/ZLiveRtc"));
#else
            return "";
#endif
        }
    }
    static T Symbol<T>(string name) where T:class {
        var pointer=dlsym(library,name);if(pointer==IntPtr.Zero)throw new LiveProtocolException("rtc_plugin_invalid");
        return Marshal.GetDelegateForFunctionPointer(pointer,typeof(T)) as T;
    }
    public static void EnsureAvailable() {
        if(library!=IntPtr.Zero)return;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        directory=RuntimeDirectory;string file=Path.Combine(directory,"libZLiveRtc.dylib");
        if(!File.Exists(file))throw new LiveProtocolException("rtc_plugin_missing");
        library=dlopen(file,2);if(library==IntPtr.Zero)throw new LiveProtocolException("rtc_plugin_invalid");
        try{start=Symbol<StartFn>("ZRtcStart");mute=Symbol<MuteFn>("ZRtcSetMuted");state=Symbol<StateFn>("ZRtcState");stop=Symbol<StopFn>("ZRtcStop");reference=Symbol<ReferenceFn>("ZRtcReference");}
        catch{library=IntPtr.Zero;throw;}
#else
        throw new LiveProtocolException("rtc_platform_unsupported");
#endif
    }
    public static void Start(string app,string room,string user,string token) {
        EnsureAvailable();Stop();if(start(app,room,user,token,directory)!=0)throw new LiveProtocolException("rtc_join_failed");active=true;
    }
    public static void SetMuted(bool value){if(!active||mute(value?1:0)!=0)throw new LiveProtocolException("rtc_mute_failed");}
    public static int State=>active&&state!=null?state():0;
    public static void Stop(){active=false;stop?.Invoke();}
    public static void Reference(float[] data,int channels,int rate,float gain){if(active)reference?.Invoke(data,data.Length,channels,rate,gain);}
}
public sealed class NativeRtcAudioReference:MonoBehaviour {
    internal readonly CallOutputReference Output=new CallOutputReference();
    int rate;float gain=1;AudioSource source;
    void Awake(){source=GetComponent<AudioSource>();rate=AudioSettings.outputSampleRate;}
    void Update(){rate=AudioSettings.outputSampleRate;gain=source!=null&&!source.mute?source.volume:0;}
    void OnAudioFilterRead(float[] data,int channels){if(gain>0)Output.Write(data,channels,rate);NativeRtc.Reference(data,channels,rate,gain);}
}
}
