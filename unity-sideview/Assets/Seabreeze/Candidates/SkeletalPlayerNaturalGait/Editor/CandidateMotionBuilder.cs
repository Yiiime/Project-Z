using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Seabreeze.Candidates.NaturalGait.Editor
{
    // Candidate-only authoring. The original PSB, skinning and previous motions stay intact.
    // Contact poses are solved offline into editable Transform curves, not sprite frames.
    public static class CandidateMotionBuilder
    {
        const string Root = "Assets/Seabreeze/Candidates/SkeletalPlayerNaturalGait";
        const string PrefabPath = Root + "/Prefabs/PlayerRigCandidate.prefab";
        const string AnimationPath = Root + "/Animation";
        const float Stride = 1.65f, Stance = .60f, ToeRoll = .38f;
        const float Ppu = 549.3421053f;
        static readonly Vector2 Heel = new Vector2(-42, -92) / Ppu;
        static readonly Vector2 Forefoot = new Vector2(153, -92) / Ppu;
        static readonly string[] BoneNames = { "Pelvis", "Spine", "Chest", "Neck", "Head",
            "NearUpperArm", "NearForearm", "NearHand", "FarUpperArm", "FarForearm", "FarHand",
            "NearThigh", "NearShin", "NearFoot", "FarThigh", "FarShin", "FarFoot" };
        static readonly AnimationCurve PelvisCurve = MakePelvisCurve();
        static readonly AnimationCurve SoleTilt = Curve(0,14, .04f,6, .12f,0, .38f,0,
            .40f,-1, .45f,-3, .50f,-8, .55f,-18, .60f,-32, .64f,-28,
            .72f,-6, .82f,2, .92f,10, 1,14);

        sealed class Rest
        {
            public Transform transform;
            public Vector3 position, scale, worldPosition;
            public Quaternion rotation, worldRotation;
            public Rest(Transform t)
            {
                transform=t;position=t.localPosition;rotation=t.localRotation;scale=t.localScale;
                worldPosition=t.position;worldRotation=t.rotation;
            }
            public void Restore() { transform.localPosition=position;transform.localRotation=rotation;transform.localScale=scale; }
        }
        sealed class Leg
        {
            public Rest thigh, shin, foot;
            public float upperLength, lowerLength, upperAngle, lowerAngle;
            public Transform heel, forefoot;
            public float maximumReachExcess, worstPhase;
            float currentPhase;
            readonly AnimationCurve swingX, swingY;
            readonly float groundY, strikeHeelX;
            public Leg(Dictionary<string,Rest> bones,string side)
            {
                thigh=bones[side+"Thigh"];shin=bones[side+"Shin"];foot=bones[side+"Foot"];
                Vector3 upper=shin.worldPosition-thigh.worldPosition, lower=foot.worldPosition-shin.worldPosition;
                upperLength=upper.magnitude;lowerLength=lower.magnitude;upperAngle=Angle(upper);lowerAngle=Angle(lower);
                groundY=foot.worldPosition.y+Heel.y;
                strikeHeelX=.38f+Rot(Heel,14).x;
                heel=Marker(foot,"HeelContact",Heel);forefoot=Marker(foot,"ForefootContact",Forefoot);
                var from=Support(Stance);var to=Support(0);
                // Early swing flexes first; terminal swing extends gradually. The ankle
                // height comes from the knee/hip geometry at authored landmarks.
                swingX=Curve(.60f,from.x-thigh.worldPosition.x, .65f,-.45f, .68f,-.38f,
                    .72f,-.23f, .78f,.06f, .86f,.30f, .94f,.435f, 1,to.x-thigh.worldPosition.x);
                EndpointSlopes(swingX,-Stride,-Stride);
                swingY=Curve(.60f,from.y, .65f,HeightForKnee(.65f,52), .68f,HeightForKnee(.68f,58),
                    .72f,HeightForKnee(.72f,58), .78f,HeightForKnee(.78f,46),
                    .86f,HeightForKnee(.86f,27), .94f,HeightForKnee(.94f,11), 1,to.y);
                // Sole tilt has extrema at both seams, so contact ankle Y velocity is zero.
                EndpointSlopes(swingY,0,0);
            }
            float HeightForKnee(float phase,float flexion)
            {
                float reach2=upperLength*upperLength+lowerLength*lowerLength+
                    2*upperLength*lowerLength*Mathf.Cos(flexion*Mathf.Deg2Rad);
                float x=swingX.Evaluate(phase);
                return thigh.worldPosition.y+PelvisOffset(phase)-Mathf.Sqrt(Mathf.Max(.01f,reach2-x*x));
            }
            public Vector3 Support(float phase)
            {
                float tilt=SoleTilt.Evaluate(phase);
                bool fore=phase>=ToeRoll;
                var pivot=fore?Forefoot:Heel;
                var contact=new Vector2(thigh.worldPosition.x+strikeHeelX-Stride*phase+
                    (fore?Forefoot.x-Heel.x:0),groundY);
                return contact-Rot(pivot,tilt);
            }
            public Vector3 Target(float phase)
            {
                phase=Mathf.Repeat(phase,1);
                if(phase<=Stance)return Support(phase);
                float tilt=SoleTilt.Evaluate(phase);
                var target=new Vector3(thigh.worldPosition.x+swingX.Evaluate(phase),swingY.Evaluate(phase),foot.worldPosition.z);
                // Correct clearance by raising the swing ankle, never by dragging the pelvis.
                float floor=groundY-Mathf.Min(Rot(Heel,tilt).y,Rot(Forefoot,tilt).y);
                float safeReach=(upperLength+lowerLength)*.998f;
                float dx=target.x-thigh.transform.position.x;
                float reachFloor=thigh.transform.position.y-Mathf.Sqrt(Mathf.Max(.001f,safeReach*safeReach-dx*dx));
                target.y=Mathf.Max(target.y,Mathf.Max(floor,reachFloor));
                return target;
            }
            public void Pose(float phase)
            {
                phase=Mathf.Repeat(phase,1);currentPhase=phase;
                Solve(Target(phase));
                foot.transform.rotation=Quaternion.Euler(0,0,SoleTilt.Evaluate(phase))*foot.worldRotation;
            }
            public void Idle()
            {
                Solve(foot.worldPosition);foot.transform.rotation=foot.worldRotation;
            }
            void Solve(Vector3 target)
            {
                Vector2 origin=thigh.transform.position,delta=(Vector2)target-origin;
                if(delta.magnitude-upperLength-lowerLength>maximumReachExcess) { maximumReachExcess=delta.magnitude-upperLength-lowerLength;worstPhase=currentPhase; }
                float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(upperLength-lowerLength)+.0001f,upperLength+lowerLength-.0001f);
                var direction=delta.normalized;
                float along=(upperLength*upperLength-lowerLength*lowerLength+d*d)/(2*d);
                float height=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
                Vector2 knee=origin+direction*along+new Vector2(-direction.y,direction.x)*height;
                thigh.transform.rotation=Quaternion.Euler(0,0,Angle(knee-origin)-upperAngle)*thigh.worldRotation;
                shin.transform.rotation=Quaternion.Euler(0,0,Angle((Vector2)target-(Vector2)shin.transform.position)-lowerAngle)*shin.worldRotation;
            }
            public float KneeAngle() => Vector2.Angle(shin.transform.position-thigh.transform.position,foot.transform.position-shin.transform.position);
        }
        static Transform Marker(Rest foot,string name,Vector2 worldOffset)
        {
            var marker=foot.transform.Find(name);
            if(!marker)marker=new GameObject(name).transform;
            marker.SetParent(foot.transform,false);
            marker.position=foot.worldPosition+(Vector3)worldOffset;
            return marker;
        }
        static Vector2 Rot(Vector2 v,float degrees) => Quaternion.Euler(0,0,degrees)*v;
        static float PelvisOffset(float phase) => PelvisCurve.Evaluate(Mathf.Repeat(phase,.5f));
        static AnimationCurve MakePelvisCurve()
        {
            float[] knots={0,-.040f,.04f,-.054f,.10f,-.035f,.20f,-.006f,.25f,0,.35f,-.012f,.40f,-.018f,.45f,-.027f};
            var values=new List<float>();
            for(int repeat=-1;repeat<=2;repeat++)
                for(int i=0;i<knots.Length;i+=2) {values.Add(knots[i]+repeat*.5f);values.Add(knots[i+1]);}
            return Curve(values.ToArray());
        }
        // Monotone Hermite slopes retain motion through internal landmarks without
        // overshooting near-straight knees. Extrema are the only zero-velocity keys.
        static AnimationCurve Curve(params float[] pairs)
        {
            var keys=new Keyframe[pairs.Length/2];
            for(int i=0;i<keys.Length;i++)keys[i]=new Keyframe(pairs[2*i],pairs[2*i+1]);
            for(int i=0;i<keys.Length;i++)
            {
                float m=0;
                if(i>0 && i<keys.Length-1)
                {
                    float a=(keys[i].value-keys[i-1].value)/(keys[i].time-keys[i-1].time);
                    float b=(keys[i+1].value-keys[i].value)/(keys[i+1].time-keys[i].time);
                    if(a*b>0)m=2*a*b/(a+b);
                }
                keys[i].inTangent=keys[i].outTangent=m;
            }
            return new AnimationCurve(keys);
        }
        static void EndpointSlopes(AnimationCurve curve,float first,float last)
        {
            var keys=curve.keys;keys[0].inTangent=keys[0].outTangent=first;
            keys[keys.Length-1].inTangent=keys[keys.Length-1].outTangent=last;curve.keys=keys;
        }

        [MenuItem("Seabreeze/Candidates/Natural Gait/2. Build Idle and Walk")]
        public static void BuildMotion()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Leave Play Mode before authoring.");
            Directory.CreateDirectory(AnimationPath);
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var transforms=root.GetComponentsInChildren<UnityEngine.U2D.Animation.SpriteSkin>(true)
                    .SelectMany(s=>s.boneTransforms).Where(t=>t!=null).Distinct().ToArray();
                var bones=BoneNames.ToDictionary(n=>n,n=>new Rest(transforms.Single(t=>t.name==n)));
                var near=new Leg(bones,"Near");var far=new Leg(bones,"Far");
                Action<float> walkPose=phase=>
                {
                    float cycle=phase*2*Mathf.PI;
                    Offset(bones["Pelvis"],new Vector3(0,PelvisOffset(phase),0));
                    // No lateral pelvis oscillation or root motion. Only restrained sagittal sway.
                    Rotate(bones["Spine"],-.8f+.25f*Mathf.Sin(cycle));
                    Rotate(bones["Chest"],-.4f-.20f*Mathf.Sin(cycle));
                    Rotate(bones["Neck"],1.0f-.05f*Mathf.Sin(cycle));
                    Rotate(bones["Head"],.20f);
                    Arm(bones,"Near",phase);Arm(bones,"Far",phase+.5f);
                    near.Pose(phase);far.Pose(phase+.5f);
                };
                var idle=Bake(root,bones,"PlayerRig_Idle",3,48,phase=>
                {
                    float breath=Mathf.Sin(phase*2*Mathf.PI);
                    // Breathe below the straight-leg limit so the first walk blend
                    // does not briefly lock both knees or lift the soles.
                    Offset(bones["Pelvis"],new Vector3(0,-.005f+.002f*breath,0));
                    Rotate(bones["Spine"],-.22f*breath);Rotate(bones["Chest"],-.12f*breath);Rotate(bones["Neck"],.26f*breath);
                    Rotate(bones["NearForearm"],.3f*breath);Rotate(bones["FarForearm"],-.25f*breath);
                    near.Idle();far.Idle();
                });
                near.maximumReachExcess=far.maximumReachExcess=0;
                var walk=Bake(root,bones,"PlayerRig_Walk",1,160,walkPose);
                var samples=new List<PhaseSample>();
                foreach(float phase in new[]{0,.04f,.10f,.20f,.25f,.35f,.40f,.45f,.50f,.55f,.60f,.65f,.68f,.72f,.78f,.86f,.94f,1})
                {
                    foreach(var rest in bones.Values)rest.Restore();walkPose(phase);
                    samples.Add(new PhaseSample {phase=phase,knee=near.KneeAngle(),pelvisOffset=PelvisOffset(phase),soleTilt=SoleTilt.Evaluate(phase),
                        heelHeight=near.heel.position.y,forefootHeight=near.forefoot.position.y});
                }
                foreach(var rest in bones.Values)rest.Restore();
                if(Mathf.Max(near.maximumReachExcess,far.maximumReachExcess)>.002f)
                    throw new InvalidOperationException($"Authored gait exceeds reach near={near.maximumReachExcess}, far={far.maximumReachExcess}; phase={near.worstPhase}, hip={near.thigh.worldPosition.y}, ankle={near.foot.worldPosition.y}; adjust authored poses.");
                var controller=BuildController(idle,walk);
                var animator=root.GetComponent<Animator>();animator.runtimeAnimatorController=controller;
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var driver=root.GetComponent<RigAnimatorDriver>();driver.animator=animator;driver.facingRoot=root.transform.Find("FacingRoot");
                driver.strideLength=Stride;driver.walkClipLength=1;driver.previewInPlace=true;driver.previewSpeed=1.65f;
                var contact=root.GetComponent<RigFootContact>();contact.driver=driver;contact.facingRoot=driver.facingRoot;contact.pelvis=bones["Pelvis"].transform;
                contact.near=new RigFootContact.Leg{thigh=near.thigh.transform,shin=near.shin.transform,foot=near.foot.transform,heel=near.heel,forefoot=near.forefoot};
                contact.far=new RigFootContact.Leg{thigh=far.thigh.transform,shin=far.shin.transform,foot=far.foot.transform,heel=far.heel,forefoot=far.forefoot};
                contact.stanceFraction=Stance;contact.heelLandingEnd=.12f;contact.toeRollStart=ToeRoll;
                root.name="PlayerRigNaturalGaitCandidate";
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                File.WriteAllText(AnimationPath+"/MotionCalibration.json",JsonUtility.ToJson(new Calibration {
                    strideLength=Stride,stanceFraction=Stance,doubleSupportFraction=2*Stance-1,
                    minimumPelvisOffset=-.054f,maximumPelvisOffset=0,comfortableSpeed=1.65f,
                    maximumReachExcess=Mathf.Max(near.maximumReachExcess,far.maximumReachExcess),
                    nearUpperLeg=near.upperLength,nearLowerLeg=near.lowerLength,phaseSamples=samples.ToArray()
                },true));
                AssetDatabase.SaveAssets();AssetDatabase.Refresh();
                Debug.Log("Natural gait candidate authored: 60% stance, heel/forefoot roll, 1.65 stride; original rig and Player preserved.");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Arm(Dictionary<string,Rest> bones,string side,float phase)
        {
            float cycle=phase*2*Mathf.PI;
            Rotate(bones[side+"UpperArm"],-8f*Mathf.Cos(cycle));
            Rotate(bones[side+"Forearm"],3.5f+2.2f*Mathf.Sin(cycle-.25f));
            Rotate(bones[side+"Hand"],-.8f*Mathf.Cos(cycle-.18f));
        }
        static float Angle(Vector2 v)=>Mathf.Atan2(v.y,v.x)*Mathf.Rad2Deg;
        static float Angle(Vector3 v)=>Mathf.Atan2(v.y,v.x)*Mathf.Rad2Deg;
        static void Rotate(Rest b,float degrees)=>b.transform.localRotation=b.rotation*Quaternion.Euler(0,0,degrees);
        static void Offset(Rest b,Vector3 offset)=>b.transform.localPosition=b.position+offset;
        static AnimationClip Bake(GameObject root, Dictionary<string,Rest> bones, string name, float length, int intervals, Action<float> pose)
        {
            string path = AnimationPath + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.name = name; clip.frameRate = 60;
            foreach (var entry in bones)
            {
                var b = entry.Value;
                var x = new AnimationCurve(); var y = new AnimationCurve(); var z = new AnimationCurve();
                float previous = b.rotation.eulerAngles.z;
                for (int i=0;i<=intervals;i++)
                {
                    foreach (var rest in bones.Values) rest.Restore();
                    pose((float)i/intervals);
                    float time = length*i/intervals;
                    float angle = previous + Mathf.DeltaAngle(previous,b.transform.localEulerAngles.z);
                    previous = angle;
                    x.AddKey(time,b.transform.localPosition.x); y.AddKey(time,b.transform.localPosition.y); z.AddKey(time,angle);
                }
                SmoothLoop(x); SmoothLoop(y); SmoothLoop(z);
                string bonePath = AnimationUtility.CalculateTransformPath(b.transform,root.transform);
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(bonePath,typeof(Transform),"m_LocalPosition.x"),x);
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(bonePath,typeof(Transform),"m_LocalPosition.y"),y);
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(bonePath,typeof(Transform),"localEulerAnglesRaw.z"),z);
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime=true; settings.loopBlend=false; settings.startTime=0; settings.stopTime=length;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            EditorUtility.SetDirty(clip); return clip;
        }
        static void SmoothLoop(AnimationCurve curve)
        {
            for(int i=0;i<curve.length;i++) curve.SmoothTangents(i,0);
            var first=curve.keys[0]; var last=curve.keys[curve.length-1];
            float slope=(curve.keys[1].value-curve.keys[curve.length-2].value)/(curve.keys[1].time + last.time-curve.keys[curve.length-2].time);
            first.inTangent=first.outTangent=slope; last.inTangent=last.outTangent=slope;
            curve.MoveKey(0,first); curve.MoveKey(curve.length-1,last);
            curve.preWrapMode=curve.postWrapMode=WrapMode.Loop;
        }
        static AnimatorController BuildController(AnimationClip idle, AnimationClip walk)
        {
            string path=AnimationPath+"/PlayerRig.controller";
            var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(!c)c=AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach(var parameter in c.parameters)c.RemoveParameter(parameter);
            c.AddParameter("Walking",AnimatorControllerParameterType.Bool);
            c.AddParameter("CycleRate",AnimatorControllerParameterType.Float);
            var machine=c.layers[0].stateMachine;
            foreach(var state in machine.states)machine.RemoveState(state.state);
            var i=machine.AddState("Idle",new Vector3(250,90)); i.motion=idle; i.writeDefaultValues=false;
            var w=machine.AddState("Walk",new Vector3(500,90)); w.motion=walk; w.writeDefaultValues=false;
            w.speedParameter="CycleRate"; w.speedParameterActive=true;
            machine.defaultState=i;
            var start=i.AddTransition(w);start.hasExitTime=false;start.hasFixedDuration=true;start.duration=.12f;start.offset=.25f;
            start.AddCondition(AnimatorConditionMode.If,0,"Walking");
            var stop=w.AddTransition(i);stop.hasExitTime=false;stop.hasFixedDuration=true;stop.duration=.16f;
            stop.AddCondition(AnimatorConditionMode.IfNot,0,"Walking");
            EditorUtility.SetDirty(c);return c;
        }
        [Serializable] sealed class Calibration
        {
            public float strideLength,stanceFraction,doubleSupportFraction,minimumPelvisOffset,maximumPelvisOffset,comfortableSpeed,maximumReachExcess;
            public float nearUpperLeg,nearLowerLeg;
            public PhaseSample[] phaseSamples;
        }
        [Serializable] sealed class PhaseSample
        {
            public float phase,knee,pelvisOffset,soleTilt,heelHeight,forefootHeight;
        }
    }
}
