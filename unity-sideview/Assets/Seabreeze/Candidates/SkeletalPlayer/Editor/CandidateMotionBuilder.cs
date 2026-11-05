using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Seabreeze.Candidates.Editor
{
    // Authoring utility, not a runtime solver. Curves remain editable in Animation.
    public static class CandidateMotionBuilder
    {
        const string Root = "Assets/Seabreeze/Candidates/SkeletalPlayer";
        const string PrefabPath = Root + "/Prefabs/PlayerRigCandidate.prefab";
        const string AnimationPath = Root + "/Animation";
        static readonly string[] BoneNames = { "Pelvis", "Spine", "Chest", "Neck", "Head",
            "NearUpperArm", "NearForearm", "NearHand", "FarUpperArm", "FarForearm", "FarHand",
            "NearThigh", "NearShin", "NearFoot", "FarThigh", "FarShin", "FarFoot" };

        sealed class Rest
        {
            public Transform transform;
            public Vector3 position, scale, worldPosition;
            public Quaternion rotation, worldRotation;
            public Rest(Transform t)
            {
                transform = t; position = t.localPosition; rotation = t.localRotation; scale = t.localScale;
                worldPosition = t.position; worldRotation = t.rotation;
            }
            public void Restore() { transform.localPosition = position; transform.localRotation = rotation; transform.localScale = scale; }
        }

        sealed class Leg
        {
            public Rest thigh, shin, foot;
            public float upperLength, lowerLength, upperAngle, lowerAngle;
            public Leg(Dictionary<string, Rest> b, string side)
            {
                thigh = b[side + "Thigh"]; shin = b[side + "Shin"]; foot = b[side + "Foot"];
                var upper = shin.worldPosition - thigh.worldPosition;
                var lower = foot.worldPosition - shin.worldPosition;
                upperLength = upper.magnitude; lowerLength = lower.magnitude;
                upperAngle = Angle(upper); lowerAngle = Angle(lower);
                if (upperLength < .15f || lowerLength < .15f) throw new InvalidOperationException("Invalid leg bind pose: " + side);
            }
            public float RequiredLowering(float stride, float stance)
            {
                float half = stride * stance * .5f;
                float reach = (upperLength + lowerLength) * .985f;
                float height = Mathf.Sqrt(Mathf.Max(.01f, reach * reach - half * half));
                return Mathf.Min(0, foot.worldPosition.y + height - thigh.worldPosition.y - .018f);
            }
            public void Pose(float phase, float stride, float stance)
            {
                FootPath(phase, stride, stance, out float x, out float lift, out float tilt);
                // During stance d(foot.x)/d(phase) == -stride, cancelling root translation.
                Vector3 target = foot.worldPosition;
                target.x = thigh.worldPosition.x + x;
                target.y += lift;
                Solve(target);
                foot.transform.rotation = Quaternion.Euler(0, 0, tilt) * foot.worldRotation;
            }
            public float LoweringAtPhase(float phase, float stride, float stance)
            {
                FootPath(phase, stride, stance, out float x, out float lift, out _);
                float reach = (upperLength + lowerLength) * .997f;
                float height = Mathf.Sqrt(Mathf.Max(.01f, reach * reach - x * x));
                // The supporting leg straightens as the pelvis passes over it.
                // A fixed contact-height pelvis produces an artificial crouch in midstance.
                return Mathf.Min(0, foot.worldPosition.y + lift + height - thigh.worldPosition.y);
            }
            static void FootPath(float phase, float stride, float stance, out float x, out float lift, out float tilt)
            {
                float u = Mathf.Repeat(phase, 1);
                float half = stride * stance * .5f;
                tilt = 0;
                if (u <= stance) { x = half - stride * u; lift = 0; }
                else
                {
                    float t = (u - stance) / (1 - stance);
                    // Equal backwards derivatives at toe-off and touchdown; no discontinuous velocity.
                    float h00 = 2*t*t*t - 3*t*t + 1, h10 = t*t*t - 2*t*t + t;
                    float h01 = -2*t*t*t + 3*t*t, h11 = t*t*t - t*t;
                    float tangent = -stride * (1 - stance);
                    x = h00 * -half + h10 * tangent + h01 * half + h11 * tangent;
                    float arc = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2);
                    lift = .10f * arc;
                    tilt = 8f * arc;
                }
            }
            void Solve(Vector3 target)
            {
                Vector2 origin = thigh.transform.position;
                Vector2 delta = (Vector2)target - origin;
                float d = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength - lowerLength) + .0001f,
                    upperLength + lowerLength - .0001f);
                Vector2 direction = delta.normalized;
                float along = (upperLength*upperLength - lowerLength*lowerLength + d*d) / (2*d);
                float height = Mathf.Sqrt(Mathf.Max(0, upperLength*upperLength - along*along));
                Vector2 knee = origin + direction * along + new Vector2(-direction.y, direction.x) * height;
                thigh.transform.rotation = Quaternion.Euler(0, 0, Angle(knee - origin) - upperAngle) * thigh.worldRotation;
                Vector2 ankleDirection = (Vector2)target - (Vector2)shin.transform.position;
                shin.transform.rotation = Quaternion.Euler(0, 0, Angle(ankleDirection) - lowerAngle) * shin.worldRotation;
            }
        }

        [MenuItem("Seabreeze/Candidates/2. Build Skeletal Idle and Walk")]
        public static void BuildMotion()
        {
            Directory.CreateDirectory(AnimationPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                // PSB layer objects may share names such as Head with their bones.
                var transforms = root.GetComponentsInChildren<UnityEngine.U2D.Animation.SpriteSkin>(true)
                    .SelectMany(s => s.boneTransforms).Where(t => t != null).Distinct().ToArray();
                var bones = BoneNames.ToDictionary(n => n, n => new Rest(transforms.Single(t => t.name == n)));
                var near = new Leg(bones, "Near"); var far = new Leg(bones, "Far");
                const float stance = .52f;
                float stride = 2.6f;
                while (stride > 1.8f && Mathf.Min(near.RequiredLowering(stride, stance), far.RequiredLowering(stride, stance)) < -.14f)
                    stride -= .025f;
                float minLowering = 0, maxLowering = float.NegativeInfinity;
                for (int sample = 0; sample <= 128; sample++)
                {
                    float phase = sample / 128f;
                    float height = Mathf.Min(near.LoweringAtPhase(phase, stride, stance),
                        far.LoweringAtPhase(phase + .5f, stride, stance));
                    minLowering = Mathf.Min(minLowering, height);
                    maxLowering = Mathf.Max(maxLowering, height);
                }
                var idle = Bake(root, bones, "PlayerRig_Idle", 3f, 48, phase =>
                {
                    float breath = Mathf.Sin(phase * Mathf.PI * 2);
                    Offset(bones["Pelvis"], new Vector3(0, .006f * breath, 0));
                    Rotate(bones["Spine"], -.28f * breath);
                    Rotate(bones["Chest"], -.18f * breath);
                    Rotate(bones["Neck"], .30f * breath);
                    Rotate(bones["NearForearm"], .35f * breath);
                    Rotate(bones["FarForearm"], -.3f * breath);
                    // Idle breath moves the body, while both feet retain the bind contacts.
                    near.PoseIdle(); far.PoseIdle();
                });
                var walk = Bake(root, bones, "PlayerRig_Walk", 1f, 64, phase =>
                {
                    float cycle = phase * 2 * Mathf.PI;
                    float supportHeight = Mathf.Min(near.LoweringAtPhase(phase, stride, stance),
                        far.LoweringAtPhase(phase + .5f, stride, stance));
                    Offset(bones["Pelvis"], new Vector3(.004f * Mathf.Sin(cycle), supportHeight, 0));
                    Rotate(bones["Pelvis"], .55f * Mathf.Sin(cycle));
                    Rotate(bones["Spine"], -1.2f + .45f * Mathf.Sin(cycle));
                    Rotate(bones["Chest"], -.6f - .35f * Mathf.Sin(cycle));
                    Rotate(bones["Neck"], 1.3f - .35f * Mathf.Sin(cycle));
                    Rotate(bones["Head"], .5f);
                    Arm(bones, "Near", phase); Arm(bones, "Far", phase + .5f);
                    near.Pose(phase, stride, stance); far.Pose(phase + .5f, stride, stance);
                });
                foreach (var b in bones.Values) b.Restore();
                var controller = BuildController(idle, walk);
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var driver = root.GetComponent<RigAnimatorDriver>() ?? root.AddComponent<RigAnimatorDriver>();
                driver.animator = animator; driver.facingRoot = root.transform.Find("FacingRoot");
                driver.strideLength = stride; driver.walkClipLength = 1;
                driver.previewInPlace = true; driver.previewSpeed = 1.4f;
                var contact = root.GetComponent<RigFootContact>() ?? root.AddComponent<RigFootContact>();
                contact.driver = driver; contact.facingRoot = driver.facingRoot; contact.pelvis = bones["Pelvis"].transform;
                contact.near = new RigFootContact.Leg { thigh = bones["NearThigh"].transform, shin = bones["NearShin"].transform, foot = bones["NearFoot"].transform };
                contact.far = new RigFootContact.Leg { thigh = bones["FarThigh"].transform, shin = bones["FarShin"].transform, foot = bones["FarFoot"].transform };
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                File.WriteAllText(AnimationPath + "/MotionCalibration.json", JsonUtility.ToJson(new Calibration {
                    strideLength = stride, stanceFraction = stance, minimumPelvisOffset = minLowering,
                    maximumPelvisOffset = maxLowering, swingFootLift = .10f,
                    nearUpperLeg = near.upperLength, nearLowerLeg = near.lowerLength,
                    farUpperLeg = far.upperLength, farLowerLeg = far.lowerLength
                }, true));
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                Debug.Log($"Skeletal candidate motions authored. Stride={stride:F3}, pelvis offset={minLowering:F3}..{maxLowering:F3}. Original Player unchanged.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void PoseIdle(this Leg leg) { leg.SolveIdle(); }
        // Extension kept next to the shared solver so both clips use the same rest joints.
        static void SolveIdle(this Leg leg)
        {
            var target = leg.foot.worldPosition;
            Vector2 origin = leg.thigh.transform.position, delta = (Vector2)target - origin;
            float d = Mathf.Clamp(delta.magnitude, Mathf.Abs(leg.upperLength-leg.lowerLength)+.0001f, leg.upperLength+leg.lowerLength-.0001f);
            float a = (leg.upperLength*leg.upperLength-leg.lowerLength*leg.lowerLength+d*d)/(2*d);
            float h = Mathf.Sqrt(Mathf.Max(0, leg.upperLength*leg.upperLength-a*a));
            var direction = delta.normalized;
            Vector2 knee = origin + direction*a + new Vector2(-direction.y,direction.x)*h;
            leg.thigh.transform.rotation = Quaternion.Euler(0,0,Angle(knee-origin)-leg.upperAngle)*leg.thigh.worldRotation;
            leg.shin.transform.rotation = Quaternion.Euler(0,0,Angle((Vector2)target-(Vector2)leg.shin.transform.position)-leg.lowerAngle)*leg.shin.worldRotation;
            leg.foot.transform.rotation = leg.foot.worldRotation;
        }

        static void Arm(Dictionary<string,Rest> bones, string side, float phase)
        {
            float swing = Mathf.Cos(phase * 2 * Mathf.PI);
            Rotate(bones[side+"UpperArm"], -10f * swing);
            Rotate(bones[side+"Forearm"], 3f + 3f * Mathf.Sin(phase * 2 * Mathf.PI));
            Rotate(bones[side+"Hand"], -1.2f * swing);
        }
        static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        static float Angle(Vector3 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        static void Rotate(Rest b, float degrees) => b.transform.localRotation = b.rotation * Quaternion.Euler(0,0,degrees);
        static void Offset(Rest b, Vector3 offset) => b.transform.localPosition = b.position + offset;

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
            public float strideLength,stanceFraction,minimumPelvisOffset,maximumPelvisOffset,swingFootLift;
            public float nearUpperLeg,nearLowerLeg,farUpperLeg,farLowerLeg;
        }
    }
}
