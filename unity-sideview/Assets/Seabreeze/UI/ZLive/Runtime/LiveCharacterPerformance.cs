using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace ZLive {
// Port of z-live-demo/ui/live2d-stage.js. All timing values come from the same character config.
// Independent of Unity rendering so the original JS timeline can be used as the regression oracle.
public sealed class LiveCharacterPerformance {
    public readonly Dictionary<string,float> Pose=new Dictionary<string,float>(),Face=new Dictionary<string,float>();
    public string Phase{get;private set;}="idle";public string Mood{get;private set;}="neutral";public string Gesture=>activeName??"none";
    double elapsedMs;public float TimeSeconds=>(float)(elapsedMs/1000);public float ArmMix{get;private set;}public float EmotionStrength{get;private set;}=1;public float EyeOpen{get;private set;}=1;
    public Vector2 Pointer{get;private set;}public Vector2 Focus{get;private set;}public float Breath=>Mathf.Max(0,.25f*Mathf.Sin(TimeSeconds*Mathf.PI*2/3.2345f));
    public bool Paused,Reduced;public float Mouth{get;private set;}
    readonly JObject config;readonly Dictionary<string,float> lastGesture=new Dictionary<string,float>(),from=new Dictionary<string,float>();readonly List<string> history=new List<string>();
    Dictionary<string,float> carried;float carriedMix,phaseAt,emotionStarted,emotionEnded=-1,pointerUntil,noticeUntil,gestureAt,fromMix,duration,waveHold;
    Vector2 focusTarget,focusVelocity;JObject active;string activeName,speechGesture="none",transition="auto";float gestureHold;bool hasSpeech,waiting,playbackStarted,reactionStarted,sudden,lowerBridge;
    static readonly string[] FaceKeys={"mouthForm","brow","browForm","browAngle","browAsymmetry","eyeSmile","eyeOpen","eyeForm","blush","pupil"};
    public LiveCharacterPerformance(JObject value){config=(JObject)value.DeepClone();foreach(var p in ((JObject)config["restPose"]).Properties())Pose[p.Name]=(float)p.Value;}
    static float F(JToken t,string key,float fallback=0)=>(float?)t?[key]??fallback;
    static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
    float At=>(float)elapsedMs;float Perf(string name)=>F(config["performance"],name);
    static float Curve(float u,params float[] v){for(int i=2;i<v.Length;i+=2)if(u<=v[i])return Mathf.Lerp(v[i-1],v[i+1],Ease((u-v[i-2])/(v[i]-v[i-2])));return v[v.Length-1];}
    void SetPhase(string phase){if(Phase==phase)return;Phase=phase;phaseAt=At;}
    void ReleaseGesture(){active=null;activeName=null;}
    public void Prepare(string mood,string gesture,string mode="auto",float holdMs=0,bool carryArms=false){
        carried=carryArms?new Dictionary<string,float>(Pose):null;carriedMix=ArmMix;ReleaseGesture();
        string next=config["moods"]?[mood??""]!=null?mood:"neutral";if(Mood!=next||EmotionStrength<.1f)emotionStarted=At;Mood=next;emotionEnded=-1;
        hasSpeech=true;speechGesture=gesture??"none";transition=mode=="smooth"||mode=="sudden"?mode:"auto";gestureHold=Mathf.Clamp(holdMs,0,30000);reactionStarted=playbackStarted=false;Mouth=0;SetPhase("thinking");
        if(transition=="sudden"||(transition=="auto"&&F(config["gestures"]?[speechGesture],"priority")==2)){StartGesture();reactionStarted=active!=null;}
    }
    public void BeginSpeaking(){if(!hasSpeech)return;SetPhase("speaking");emotionEnded=-1;if(!playbackStarted&&!reactionStarted&&!Paused)StartGesture();playbackStarted=true;}
    public void EndSpeech(){if(!hasSpeech)return;hasSpeech=false;ReleaseGesture();carried=null;Mouth=0;if(emotionEnded<0)emotionEnded=At;SetPhase(waiting?"thinking":"listening");}
    public void SetWaiting(bool value){waiting=value;if(!hasSpeech&&waiting)SetPhase("thinking");else if(!hasSpeech&&Phase=="thinking")SetPhase("listening");}
    public void NoticeMessage(){noticeUntil=At+1400;}
    public void TrackPointer(Vector2 local,Vector2 size){if(Paused||Reduced||Phase!="idle"||size.x<=0||size.y<=0)return;Pointer=new Vector2(Mathf.Clamp((local.x-size.x/2)/size.x,-.55f,.55f),Mathf.Clamp((size.y*.35f-local.y)/size.y,-.45f,.45f));pointerUntil=At+1500;focusTarget=Pointer;}
    public void ClearPointer(){pointerUntil=0;}
    void StartGesture(){
        if(Paused||Reduced)return;string name=speechGesture;if(name=="wave")name="handWave";if(name=="explain")name=(string)config["moods"][Mood]["explainGesture"]??"openArms";
        if(speechGesture=="explain"&&history.Contains(name))name="nod";var motion=config["gestures"]?[name] as JObject;
        if(motion==null||(string)motion["kind"]=="none"||(bool?)motion["automatic"]!=true)return;
        bool jump=transition=="sudden"||(transition=="auto"&&F(motion,"priority")==2);float cooldown=Perf("cooldownMs")==0?0:jump?1200:Perf("cooldownMs");if(lastGesture.TryGetValue(name,out float last)&&At-last<cooldown)return;
        active=(JObject)motion.DeepClone();activeName=name;sudden=jump;lowerBridge=name=="relaxArms"&&ArmMix>.01f&&!jump;
        waveHold=F(active["timing"],"holdMs");if((string)active["kind"]=="arms"){
            var timing=(JObject)active["timing"];if(jump){timing["enterMs"]=180;timing["exitMs"]=650;}else if(transition=="smooth"){timing["enterMs"]=550;timing["exitMs"]=650;}
            if(lowerBridge)timing["enterMs"]=900;timing["holdMs"]=Mathf.Max(F(timing,"holdMs"),gestureHold);duration=F(timing,"enterMs")+F(timing,"holdMs")+F(timing,"exitMs");
        }else duration=F(active,"durationMs");
        gestureAt=At;fromMix=ArmMix;from.Clear();foreach(var item in Pose)from[item.Key]=item.Value;lastGesture[name]=At;history.Add(name);if(history.Count>2)history.RemoveAt(0);
    }
    public void Step(float dtMs,float? audioLevel=0,bool speaking=false){
        if(Paused)return;dtMs=Mathf.Clamp(dtMs,1,50);elapsedMs+=dtMs;float at=At;var mood=(JObject)config["moods"][Mood];float sinceEnd=emotionEnded<0?0:at-emotionEnded;EmotionStrength=1-Ease((sinceEnd-Perf("emotionHoldMs"))/Perf("emotionReleaseMs"));
        bool reacting=active!=null&&sudden&&at-gestureAt<500;var desired=(JObject)mood.DeepClone();if(active?["face"] is JObject reaction&&at-gestureAt<F(active,"faceMs",500))foreach(var p in reaction.Properties())desired[p.Name]=p.Value.DeepClone();
        float smooth=1-Mathf.Exp(-dtMs/(reacting?45:Perf("transitionMs")));foreach(string k in FaceKeys){float baseline=F(config["moods"]["neutral"],k,k=="eyeOpen"?1:0),value=baseline+(F(desired,k,k=="eyeOpen"?1:0)-baseline)*EmotionStrength;Face[k]=(Face.TryGetValue(k,out float old)?old:baseline)+(value-(Face.TryGetValue(k,out old)?old:baseline))*smooth;}
        if(active!=null&&at-gestureAt>=duration)ReleaseGesture();
        var target=new Dictionary<string,float>{{"ParamAngleX",0},{"ParamAngleY",0},{"ParamAngleZ",0},{"ParamBodyAngleX",0},{"ParamBodyAngleY",0},{"ParamBodyAngleZ",0},{"ParamEyeBallX",0},{"ParamEyeBallY",0}};
        if(!Reduced){float phaseAge=at-phaseAt,gazeCycle=Perf("gazeCameraMs")+Perf("gazeChatMs");if(Phase=="listening"&&phaseAge>2400&&!hasSpeech)SetPhase("idle");float gx=0,gy=0;
            if(Phase=="thinking"&&Mood=="neutral"){gx=-.2f;gy=.15f;target["ParamAngleZ"]=-3;}else if(Phase=="listening"){target["ParamAngleY"]=-2;target["ParamBodyAngleY"]=1;}
            if(Phase=="idle"&&phaseAge%7500>5700)gx=.12f;
            if(at<noticeUntil||(Phase=="speaking"&&(Mood=="neutral"||Mood=="happy"||Mood=="warm")&&phaseAge%gazeCycle>Perf("gazeCameraMs"))){gx=.3f;gy=-.05f;}
            if(Phase=="idle"&&at<pointerUntil){gx=Pointer.x;gy=Pointer.y;}
            target["ParamEyeBallX"]=gx;target["ParamEyeBallY"]=gy;target["ParamAngleX"]=gx*12;target["ParamAngleY"]+=gy*8;target["ParamBodyAngleZ"]=Mathf.Sin(TimeSeconds*.65f)*(Mood=="serious"?.12f:.5f);
            if(mood["pose"] is JObject attitude)foreach(var p in attitude.Properties())target[p.Name]=(target.TryGetValue(p.Name,out float old)?old:0)+(float)p.Value*EmotionStrength;
            if(active!=null){float u=Mathf.Clamp01((at-gestureAt)/duration),energy=Mood=="serious"||Mood=="sad"||Mood=="warm"?.75f:1,accent=Curve(u,0,0,.25f,1,.62f,1,1,0)*energy;switch((string)active["kind"]){case "nod":target["ParamAngleY"]+=Curve(u,0,0,.14f,2,.38f,-13,.52f,-13,1,0)*energy;break;case "shake":target["ParamAngleX"]+=Curve(u,0,0,.22f,-12,.48f,12,.72f,-7,1,0)*energy;break;case "tilt":target["ParamAngleZ"]-=10*accent;break;case "lean":target["ParamAngleY"]-=5*accent;target["ParamBodyAngleY"]+=5*accent;break;case "emphasis":target["ParamAngleY"]-=7*accent;target["ParamBodyAngleZ"]+=3*accent;break;case "lookDown":target["ParamAngleY"]-=14*accent;target["ParamEyeBallY"]-=.35f*accent;break;}}
        }
        float age=active==null?0:at-gestureAt;var timing2=active?["timing"];float enter=timing2==null?0:Ease(age/F(timing2,"enterMs")),recover=timing2==null?0:Ease((age-F(timing2,"enterMs")-F(timing2,"holdMs"))/F(timing2,"exitMs")),weight=enter*(1-recover);bool arms=(string)active?["kind"]=="arms";float useB=!Reduced&&(string)active?["armSet"]=="B"?1:0;
        if(active!=null&&lowerBridge)ArmMix=fromMix*(1-Ease((age-420)/120));else if(arms)ArmMix=fromMix+(useB-fromMix)*Ease(age/Mathf.Min(F(timing2,"enterMs"),180));else if(carried!=null)ArmMix=carriedMix;else ArmMix+=(useB-ArmMix)*(1-Mathf.Exp(-dtMs/180));
        if(!Reduced&&active?["head"] is JObject head)foreach(var p in head.Properties())target[p.Name]+=((float)p.Value-target[p.Name])*weight;
        foreach(var p in ((JObject)config["restPose"]).Properties()){string id=p.Name;float rest=carried!=null&&!arms&&carried.TryGetValue(id,out float held)?held:(float)p.Value;target[id]=rest;if(arms){float goal=F(active["arms"],id,rest),start=from.TryGetValue(id,out float old)?old:rest;bool hand=id.Contains("Change")||id.Contains("Dhange");target[id]=hand?goal:age<F(timing2,"enterMs")?start+(goal-start)*enter:goal+(rest-goal)*recover;
                if(lowerBridge&&age<F(timing2,"enterMs")){if(id=="ParamArmLB"||id=="ParamArmRB")target[id]=start+(rest-start)*Ease(age/420);else if(id=="ParamArmLA"||id=="ParamArmRA")target[id]=1+(goal-1)*Ease((age-540)/360);else target[id]=age<540?start:goal;}
                if(F(active,"waveCycles")>0&&id=="ParamArmRB"&&age>=F(timing2,"enterMs")&&age<F(timing2,"enterMs")+waveHold){float u=(age-F(timing2,"enterMs"))/waveHold;target[id]+=.18f*Mathf.Sin(u*Mathf.PI*2*F(active,"waveCycles"))*Mathf.Pow(Mathf.Sin(u*Mathf.PI),2);}
            }}
        foreach(var p in target){bool arm=config["restPose"][p.Key]!=null;float tau=p.Key.Contains("Change")||p.Key.Contains("Dhange")?65:arm?340:active!=null&&sudden?50:Perf("returnMs");float old=Pose.TryGetValue(p.Key,out float v)?v:p.Value;Pose[p.Key]=arms&&arm?p.Value:old+(p.Value-old)*(1-Mathf.Exp(-dtMs/tau));}
        bool reactionFace=active!=null&&sudden&&age<F(active,"faceMs",500);float t=TimeSeconds%4.7f,blink=!reactionFace&&t<.16f?Mathf.Abs(t-.08f)/.08f:1,joy=(TimeSeconds-emotionStarted/1000)%5,grin=!reactionFace&&!Reduced&&Mood=="happy"?Ease((joy-.3f)/.25f)*(1-Ease((joy-1.4f)/.35f))*EmotionStrength:0;EyeOpen=blink*Face["eyeOpen"]*(1-grin);
        float lip=0;if(speaking)lip=audioLevel.HasValue?Mathf.Clamp01((audioLevel.Value-F(config["lipSync"],"noiseFloor"))*F(config["lipSync"],"gain")):(.12f+Mathf.Abs(Mathf.Sin(TimeSeconds*19))*.62f)*(.65f+Mathf.Sin(TimeSeconds*4.1f)*.25f);Mouth+=(lip-Mouth)*(1-Mathf.Exp(-dtMs/F(config["lipSync"],lip>Mouth?"attackMs":"releaseMs")));
        StepFocus(dtMs);
    }
    void StepFocus(float dt){var delta=focusTarget-Focus;if(Mathf.Abs(delta.x)<.01f&&Mathf.Abs(delta.y)<.01f)return;float distance=delta.magnitude,speed=5.333333333333333f*dt/1000,acceleration=.006666666666666667f*speed*dt;var change=Vector2.ClampMagnitude(speed*delta/distance-focusVelocity,acceleration);focusVelocity+=change;focusVelocity=Vector2.ClampMagnitude(focusVelocity,.5f*(Mathf.Sqrt(acceleration*acceleration+8*acceleration*distance)-acceleration));Focus+=focusVelocity;}
    // Web SDK evaluates focus + breath before physics, then the authored pose overwrites head/face.
    public Dictionary<string,float> PhysicsInputs()=>new Dictionary<string,float>{{"ParamAngleX",Focus.x*30+7.5f*Mathf.Sin(TimeSeconds*Mathf.PI*2/6.5345f)},{"ParamAngleY",Focus.y*30+4*Mathf.Sin(TimeSeconds*Mathf.PI*2/3.5345f)},{"ParamAngleZ",Focus.x*Focus.y*-30+5*Mathf.Sin(TimeSeconds*Mathf.PI*2/5.5345f)},{"ParamBodyAngleX",Focus.x*10+2*Mathf.Sin(TimeSeconds*Mathf.PI*2/15.5345f)},{"ParamBodyAngleZ",0},{"ParamBreath",Breath}};
}
}
