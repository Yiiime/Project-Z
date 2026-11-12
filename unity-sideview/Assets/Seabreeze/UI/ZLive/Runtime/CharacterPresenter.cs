using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Live2D.Cubism.Core;
using ZLive.ModelPerformance;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Physics;
namespace ZLive {
public sealed class CharacterPresenter:MonoBehaviour {
    public const string NativePortraitResource = "Memory/Portraits/ZFinal-v03";
    public const string LegacyPortraitResource = "Memory/Portraits/Z";
    public const string LegacyLivePrefabResource = "ZLiveModel";
    const string NativeSpeechParameter = "ParamZSpeech";
    const string LegacySpeechParameter = "ParamMouthOpenY";
    Seabreeze.Live2DModelProfile portraitProfile;
    public bool UsingStoryPortrait {get;private set;}
    Camera portraitCamera;CubismModel cubismModel;
    JObject config;bool textSpeaking;public LiveCharacterPerformance Performance{get;private set;}
    public AudioSource Audio;public bool Paused;public bool Ready{get;private set;}public RenderTexture Texture{get;private set;}
    CubismPhysicsController physics;
    public ZPerformanceController ZPerformance { get; private set; }
    public Camera PortraitCamera => portraitCamera;
    public ZEmotionAccents Accents { get; private set; }
    string pendingGesture = "none", pendingMotion = "idle";
    bool cueActive;
    string speechParameter = LegacySpeechParameter;
    readonly Dictionary<string,CubismParameter> parameters=new Dictionary<string,CubismParameter>();
    public Seabreeze.Live2DModelProfile PortraitProfile => portraitProfile;
    public string PortraitResourcePath {get;private set;}
    public int ParameterCount => parameters.Count;
    public string SpeechParameter => speechParameter;
    public bool UsesNativeSpeechParameter => speechParameter == NativeSpeechParameter;
    void Awake(){config=JObject.Parse(Resources.Load<TextAsset>("character").text);Performance=new LiveCharacterPerformance(config);}
    public void Initialize(){
        portraitProfile=Resources.Load<Seabreeze.Live2DModelProfile>(NativePortraitResource);
        PortraitResourcePath=NativePortraitResource;
        if(!HasStoryPrefab(portraitProfile)){
            portraitProfile=Resources.Load<Seabreeze.Live2DModelProfile>(LegacyPortraitResource);
            PortraitResourcePath=LegacyPortraitResource;
        }
        var prefab=portraitProfile!=null?portraitProfile.prefab:null;
        UsingStoryPortrait=HasStoryPrefab(portraitProfile);
        if(!UsingStoryPortrait){prefab=Resources.Load<GameObject>(LegacyLivePrefabResource);PortraitResourcePath=LegacyLivePrefabResource;}if(prefab==null){Debug.LogError("[ZLive] Z 模型未准备，请运行 Seabreeze > Prepare Live Z Model。");return;}var model=Instantiate(prefab,transform);model.name="Z · Live2D";cubismModel=model.GetComponent<CubismModel>();
        foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
        foreach(var p in model.GetComponentsInChildren<CubismParameter>(true))parameters[p.Id]=p;
        var animator=model.GetComponent<Animator>();if(animator!=null)animator.enabled=false;
        physics=model.GetComponent<CubismPhysicsController>();
        ZPerformance=model.AddComponent<ZPerformanceController>();
        model.AddComponent<ZSighFaceArt>();
        var before=model.AddComponent<CubismCharacterInputBridge>();before.Presenter=this;
        var bridge=model.AddComponent<CubismCharacterBridge>();bridge.Presenter=this;
        var update=model.GetComponent<CubismUpdateController>();if(update==null)update=model.AddComponent<CubismUpdateController>();update.Refresh();
        speechParameter=ResolveSpeechParameter();
        Texture=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);Texture.Create();var camera=new GameObject("Character Camera").AddComponent<Camera>();portraitCamera=camera;camera.transform.SetParent(transform,true);camera.GetUniversalAdditionalCameraData().SetRenderer(1);camera.orthographic=true;camera.orthographicSize=3.5f;camera.transform.position=new Vector3(0,1,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<30;camera.targetTexture=Texture;
        Accents=ZEmotionAccents.Create(ZPerformance,portraitCamera,transform);
        Ready=ZPerformance.IsModelReady && parameters.ContainsKey(speechParameter);
    }
    public void SetViewport(float width,float height,float pixelsPerUnit=1){
        if(!Ready||portraitCamera==null||width<1||height<1)return;
        var canvas=cubismModel.CanvasInformation;float ppu=canvas.PixelsPerUnit;
        float modelWidth=canvas.CanvasWidth/ppu,modelHeight=canvas.CanvasHeight/ppu;
        var layout=config["layout"];float fit=Mathf.Min(height*(float)layout["height"]/modelHeight,width*(float)layout["maxWidth"]/modelWidth);
        float viewHeight=height/fit,viewWidth=width/fit;
        portraitCamera.orthographicSize=viewHeight*.5f;
        portraitCamera.transform.position=new Vector3((canvas.CanvasWidth*.5f-canvas.CanvasOriginX)/ppu+(.5f-(float)layout["x"])*viewWidth,(canvas.CanvasHeight-canvas.CanvasOriginY)/ppu+((float)layout["top"]-.5f)*viewHeight,-10);
        float scale=Mathf.Clamp(pixelsPerUnit,1,4);float limit=Mathf.Min(1,4096/Mathf.Max(width*scale,height*scale));int w=Mathf.Max(64,Mathf.CeilToInt(width*scale*limit)),h=Mathf.Max(64,Mathf.CeilToInt(height*scale*limit));
        if(Texture.width==w&&Texture.height==h)return;
        portraitCamera.targetTexture=null;Texture.Release();Destroy(Texture);
        Texture=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32){filterMode=FilterMode.Bilinear,antiAliasing=4,useMipMap=false};Texture.Create();portraitCamera.targetTexture=Texture;
    }
    // Same stage-local normalization, idle ownership and timeout as the web demo.
    public void TrackPointer(Vector2 local,Vector2 size){Performance.Paused=Paused;Performance.TrackPointer(local,size);}
    public void ClearPointerGaze()=>Performance.ClearPointer();
    public Vector2 PointerGaze=>Performance.Pointer;
    public Vector2 EyeDirection=>new Vector2(ParameterValue("ParamEyeBallX"),ParameterValue("ParamEyeBallY"));
    public float ParameterValue(string id){
        if(parameters.TryGetValue(id,out var p))return p.Value;
        return 0;
    }
    public float ParameterDefaultValue(string id){
        if(parameters.TryGetValue(id,out var p))return p.DefaultValue;
        return 0;
    }
    void OnApplicationFocus(bool focused){if(!focused)ClearPointerGaze();}
    public void Cue(string moodName,string action,string transition,float holdMs=0,bool carryArms=false){
        textSpeaking=false; cueActive=true;ResetSpeechParameters();
        Performance.Prepare(moodName,action,transition,holdMs,carryArms);
        if(ZPerformance==null)return;
        ZPerformance.CancelCurrentPerformance();
        ZPerformance.SetExpression(ZLivePerformanceMap.Expression(moodName));
        pendingGesture=ZLivePerformanceMap.Gesture(action);
        if(UsingStoryPortrait){
            var key=moodName=="happy"&&pendingGesture=="wave"?"happy_wave":moodName;
            ZPerformance.SetPortraitExpression(portraitProfile.FindExpression(key));
        }
        pendingMotion=ZLivePerformanceMap.Motion(moodName,action);
        if(transition=="sudden")StartZGesture();
    }
    void StartZGesture(){if(ZPerformance==null)return;ZPerformance.SetGesture(Performance.Reduced?"none":pendingGesture);ZPerformance.SetMotionPreset(Performance.Reduced?"idle":pendingMotion);}
    public void BeginSpeaking(bool textOnly=false){textSpeaking=textOnly;Performance.BeginSpeaking();StartZGesture();}
    public void EndCue(){textSpeaking=false;cueActive=false;Performance.EndSpeech();ZPerformance?.ResetPerformance();ResetSpeechParameters();}
    public void SetWaiting(bool waiting){Performance.SetWaiting(waiting);if(!cueActive&&ZPerformance!=null){var motion=waiting?ZMotionPreset.Think:ZMotionPreset.Idle;if(ZPerformance.CurrentMotion!=motion)ZPerformance.SetMotionPreset(motion);}}
    public void NoticeMessage()=>Performance.NoticeMessage();
    // Web SDK focus/breath runs before physics; authored dialogue channels run after it.
    // Keeping that order is essential: otherwise idle hair/scarf stop or physics overrides gaze.
    public void BeforePhysics(){if(!Ready)return;Performance.Paused=Paused;
        ZPerformance.Paused=Paused;
        ZPerformance.SetMotionAmplitudeMultiplier(Performance.Reduced?.25f:1f);
        if(Performance.Reduced)ZPerformance.SetGesture("none");
        if(physics!=null)physics.enabled=!Paused;
        float level=0;bool speaking=Audio!=null&&Audio.isPlaying;
        if(!Paused&&speaking&&Audio.clip!=null){
            // Read the playing clip before output mute, matching the web analyser. Silent QA still animates lips.
            if(Audio.clip.GetData(samples,Mathf.Clamp(Audio.timeSamples,0,Audio.clip.samples-1))){foreach(float v in samples)level+=v*v;level=Mathf.Sqrt(level/samples.Length);}
        }
        Performance.Step(Time.unscaledDeltaTime*1000,textSpeaking?(float?)null:level,speaking||textSpeaking);
        ZPerformance.LiveSpeechMouth=speaking||textSpeaking;
    }
    public void ApplyFrame(){
        if(!Ready)return;
        // Voice owns only mouth openness, after the Z expression controller (order 1000).
        // Z retains its own eyebrow, eyelid, cheek and gesture parameter bindings.
        Set(speechParameter,Paused?0:Performance.Mouth);
        if(!Paused){
            if(Performance.Pose.TryGetValue("ParamEyeBallX",out var x))Set("ParamEyeBallX",x);
            if(Performance.Pose.TryGetValue("ParamEyeBallY",out var y))Set("ParamEyeBallY",y);
        }
    }
    readonly float[] samples=new float[256];
    static bool HasStoryPrefab(Seabreeze.Live2DModelProfile profile){var prefab=profile!=null?profile.prefab:null;return prefab!=null&&prefab.GetComponent<CubismModel>()?.Moc!=null;}
    string ResolveSpeechParameter(){
        if(parameters.ContainsKey(NativeSpeechParameter))return NativeSpeechParameter;
        if(portraitProfile!=null&&!string.IsNullOrEmpty(portraitProfile.mouthParameter)&&parameters.ContainsKey(portraitProfile.mouthParameter))return portraitProfile.mouthParameter;
        return parameters.ContainsKey(LegacySpeechParameter)?LegacySpeechParameter:string.Empty;
    }
    void ResetSpeechParameters(){
        if(parameters.ContainsKey(NativeSpeechParameter))Set(NativeSpeechParameter,ParameterDefaultValue(NativeSpeechParameter));
        if(parameters.ContainsKey(LegacySpeechParameter))Set(LegacySpeechParameter,ParameterDefaultValue(LegacySpeechParameter));
    }
    void Set(string id,float value){
        if(parameters.TryGetValue(id,out var p))p.Value=Mathf.Clamp(value,p.MinimumValue,p.MaximumValue);
    }
    void OnDestroy(){if(Texture!=null){Texture.Release();Destroy(Texture);}}
}
public sealed class CubismCharacterInputBridge:MonoBehaviour,ICubismUpdatable {
    public CharacterPresenter Presenter;public int ExecutionOrder=>CubismUpdateExecutionOrder.CubismPhysicsController-1;public bool NeedsUpdateOnEditing=>false;public bool HasUpdateController{get;set;}
    public void OnLateUpdate(){Presenter?.BeforePhysics();}
}
public sealed class CubismCharacterBridge:MonoBehaviour,ICubismUpdatable {
    public CharacterPresenter Presenter;public int ExecutionOrder=>1001;public bool NeedsUpdateOnEditing=>false;public bool HasUpdateController{get;set;}
    public void OnLateUpdate(){Presenter?.ApplyFrame();}
}
}
