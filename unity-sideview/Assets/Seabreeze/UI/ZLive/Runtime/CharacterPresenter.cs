using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Physics;
namespace ZLive {
public sealed class CharacterPresenter:MonoBehaviour {
    Camera portraitCamera;CubismModel cubismModel;
    JObject config;bool textSpeaking;public LiveCharacterPerformance Performance{get;private set;}
    public AudioSource Audio;public bool Paused;public bool Ready{get;private set;}public RenderTexture Texture{get;private set;}
    CubismPhysicsController physics;
    readonly Dictionary<string,CubismParameter> parameters=new Dictionary<string,CubismParameter>();
    readonly Dictionary<string,CubismPart> parts=new Dictionary<string,CubismPart>();
    void Awake(){config=JObject.Parse(Resources.Load<TextAsset>("character").text);Performance=new LiveCharacterPerformance(config);}
    public void Initialize(){
        var prefab=Resources.Load<GameObject>("Haru");if(prefab==null)return;var model=Instantiate(prefab,transform);model.name="Z · Haru";cubismModel=model.GetComponent<CubismModel>();
        foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
        foreach(var p in model.GetComponentsInChildren<CubismParameter>(true))parameters[p.Id]=p;
        foreach(var p in model.GetComponentsInChildren<CubismPart>(true))parts[p.Id]=p;
        var animator=model.GetComponent<Animator>();if(animator!=null)animator.enabled=false;
        physics=model.GetComponent<CubismPhysicsController>();
        var before=model.AddComponent<CubismCharacterInputBridge>();before.Presenter=this;
        var bridge=model.AddComponent<CubismCharacterBridge>();bridge.Presenter=this;
        var update=model.GetComponent<CubismUpdateController>();if(update==null)update=model.AddComponent<CubismUpdateController>();update.Refresh();
        Texture=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);Texture.Create();var camera=new GameObject("Character Camera").AddComponent<Camera>();portraitCamera=camera;camera.transform.SetParent(transform,true);camera.GetUniversalAdditionalCameraData().SetRenderer(1);camera.orthographic=true;camera.orthographicSize=3.5f;camera.transform.position=new Vector3(0,1,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<30;camera.targetTexture=Texture;
        Ready=parameters.Count>0;
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
    void OnApplicationFocus(bool focused){if(!focused)ClearPointerGaze();}
    public void Cue(string moodName,string action,string transition,float holdMs=0,bool carryArms=false){textSpeaking=false;Performance.Prepare(moodName,action,transition,holdMs,carryArms);}
    public void BeginSpeaking(bool textOnly=false){textSpeaking=textOnly;Performance.BeginSpeaking();}
    public void EndCue(){textSpeaking=false;Performance.EndSpeech();}
    public void SetWaiting(bool waiting)=>Performance.SetWaiting(waiting);
    public void NoticeMessage()=>Performance.NoticeMessage();
    // Web SDK focus/breath runs before physics; authored dialogue channels run after it.
    // Keeping that order is essential: otherwise idle hair/scarf stop or physics overrides gaze.
    public void BeforePhysics(){if(!Ready)return;Performance.Paused=Paused;
        if(physics!=null)physics.enabled=!Paused;
        float level=0;bool speaking=Audio!=null&&Audio.isPlaying;
        if(!Paused&&speaking&&Audio.clip!=null){
            // Read the playing clip before output mute, matching the web analyser. Silent QA still animates lips.
            if(Audio.clip.GetData(samples,Mathf.Clamp(Audio.timeSamples,0,Audio.clip.samples-1))){foreach(float v in samples)level+=v*v;level=Mathf.Sqrt(level/samples.Length);}
        }
        Performance.Step(Time.unscaledDeltaTime*1000,textSpeaking?(float?)null:level,speaking||textSpeaking);
        if(!Paused)foreach(var p in Performance.PhysicsInputs())Set(p.Key,p.Value);
    }
    public void ApplyFrame(){if(!Ready)return;var face=Performance.Face;if(face.Count==0)return;
        foreach(var p in Performance.Pose)Set(p.Key,p.Value);
        Set("ParamMouthOpenY",Paused?0:Performance.Mouth);Set("ParamMouthForm",face["mouthForm"]);
        foreach(string side in new[]{"L","R"}){Set("ParamBrow"+side+"Y",face["brow"]+(side=="L"?1:-1)*face["browAsymmetry"]);Set("ParamBrow"+side+"Form",face["browForm"]);Set("ParamBrow"+side+"Angle",face["browAngle"]);Set("ParamBrow"+side+"X",0);Set("ParamEye"+side+"Smile",face["eyeSmile"]);Set("ParamEye"+side+"Open",Performance.EyeOpen);Part("Part01Arm"+side+"A001",1-Performance.ArmMix);Part("Part01Arm"+side+"B001",Performance.ArmMix);}
        Set("ParamTere",face["blush"]);Set("ParamEyeBallForm",face["pupil"]);Set("ParamEyeForm",face["eyeForm"]);Set("ParamTear",0);
    }
    readonly float[] samples=new float[256];
    void Set(string id,float value){
        if(parameters.TryGetValue(id,out var p))p.Value=Mathf.Clamp(value,p.MinimumValue,p.MaximumValue);
    }
    void Part(string id,float value){
        if(parts.TryGetValue(id,out var p))p.Opacity=value;
    }
    void OnDestroy(){if(Texture!=null){Texture.Release();Destroy(Texture);}}
}
public sealed class CubismCharacterInputBridge:MonoBehaviour,ICubismUpdatable {
    public CharacterPresenter Presenter;public int ExecutionOrder=>CubismUpdateExecutionOrder.CubismPhysicsController-1;public bool NeedsUpdateOnEditing=>false;public bool HasUpdateController{get;set;}
    public void OnLateUpdate(){Presenter?.BeforePhysics();}
}
public sealed class CubismCharacterBridge:MonoBehaviour,ICubismUpdatable {
    public CharacterPresenter Presenter;public int ExecutionOrder=>CubismUpdateExecutionOrder.CubismPhysicsController+1;public bool NeedsUpdateOnEditing=>false;public bool HasUpdateController{get;set;}
    public void OnLateUpdate(){Presenter?.ApplyFrame();}
}
}
