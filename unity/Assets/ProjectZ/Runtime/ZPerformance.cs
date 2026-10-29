using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProjectZ.Runtime
{
    [DefaultExecutionOrder(100)]
    public sealed class ZPerformance : MonoBehaviour
    {
        public Transform Head, Chest, RightArm, LeftArm, RightForearm, LeftForearm, RightHand;
        public Transform GazeTarget;
        public SkinnedMeshRenderer Face;
        public Animator MotionAnimator;
        public Avatar SourceAvatar;
        public int SmileIndex { get; private set; } = -1;
        public string CurrentAction { get; private set; } = "look";
        public float ActionElapsed => Time.time - startedAt;

        private sealed class PlantedLeg
        {
            public Transform Upper, Lower, Foot;
            public Vector3 Anchor;
            public Quaternion Rotation;
            public float UpperLength, LowerLength;
        }
        private readonly List<PlantedLeg> legs = new List<PlantedLeg>();
        private Transform neck, hips, leftEye, rightEye;
        private Transform[] hairTips, skirtBones;
        private int firmIndex, thoughtfulIndex, blinkIndex, mouthIndex, joyIndex;
        private string emotion = "neutral";
        private float startedAt, blinkAt, gazeShiftAt, yaw, pitch, yawVelocity, pitchVelocity;
        private Vector2 eyeOffset, hairLag, hairVelocity, skirtLag, skirtVelocity;
        private Quaternion previousHeadRotation;
        private Vector3 previousHipsPosition;
        private bool initialized;
        private readonly System.Random random = new System.Random(113);

        private void Start() => Initialize();
        private void Initialize()
        {
            if (initialized) return;
            if (MotionAnimator == null || MotionAnimator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Z is missing her standing animation controller.");
            var transforms = GetComponentsInChildren<Transform>();
            Transform Bone(string name) => transforms.First(t => t.name == name);
            neck = Bone("J_Bip_C_Neck"); hips = Bone("J_Bip_C_Hips");
            leftEye = Bone("J_Adj_L_FaceEye"); rightEye = Bone("J_Adj_R_FaceEye");
            hairTips = transforms.Where(t => t.name.StartsWith("HairJoint") &&
                !t.Cast<Transform>().Any(c => c.name.StartsWith("HairJoint"))).ToArray();
            skirtBones = transforms.Where(t => t.name.StartsWith("Z_Skirt_")).ToArray();
            foreach (var side in new[] { "L", "R" })
            {
                var upper = Bone("J_Bip_" + side + "_UpperLeg");
                var lower = Bone("J_Bip_" + side + "_LowerLeg");
                var foot = Bone("J_Bip_" + side + "_Foot");
                legs.Add(new PlantedLeg { Upper = upper, Lower = lower, Foot = foot,
                    Anchor = transform.InverseTransformPoint(foot.position),
                    Rotation = Quaternion.Inverse(transform.rotation) * foot.rotation,
                    UpperLength = Vector3.Distance(upper.position, lower.position),
                    LowerLength = Vector3.Distance(lower.position, foot.position) });
            }
            SmileIndex = Shape("Smile"); firmIndex = Shape("Firm"); thoughtfulIndex = Shape("Thoughtful");
            blinkIndex = Shape("Blink"); mouthIndex = Shape("MouthA"); joyIndex = Shape("Joy");
            blinkAt = Time.time + 2.7f; gazeShiftAt = Time.time + 1.9f;
            MotionAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            MotionAnimator.applyRootMotion = false;
            MotionAnimator.Play("Idle", 0, 0);
            previousHeadRotation = Head.rotation; previousHipsPosition = hips.localPosition;
            initialized = true;
        }

        public void FaceToward(Vector3 position)
        {
            var direction = position - transform.position; direction.y = 0;
            if (direction.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(direction);
            yaw = pitch = yawVelocity = pitchVelocity = 0;
            hairLag = hairVelocity = skirtLag = skirtVelocity = Vector2.zero;
            if (initialized) { previousHeadRotation = Head.rotation; previousHipsPosition = hips.localPosition; }
        }
        public void Perform(string nextAction, string nextEmotion)
        {
            Initialize();
            var state = nextAction == "wave" ? "Wave" : nextAction == "nod" ? "Nod" : nextAction == "pause" ? "Pause" : "Idle";
            if (nextAction != "look" || CurrentAction != "look")
                MotionAnimator.CrossFadeInFixedTime(state, state == "Idle" ? .32f : .22f, 0);
            CurrentAction = nextAction; emotion = nextEmotion; startedAt = Time.time;
            if (nextAction == "nod") blinkAt = Time.time + .40f;
        }
        private int Shape(string name)
        {
            for (var i = 0; i < Face.sharedMesh.blendShapeCount; i++)
            {
                var candidate = Face.sharedMesh.GetBlendShapeName(i);
                if (candidate == name || candidate.EndsWith("." + name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
        private void Weight(int index, float target, float speed = 7)
        {
            if (index >= 0) Face.SetBlendShapeWeight(index,
                Mathf.Lerp(Face.GetBlendShapeWeight(index), target, 1 - Mathf.Exp(-Time.deltaTime * speed)));
        }

        private void LateUpdate()
        {
            Initialize();
            UpdateGaze();
            UpdateExpression();
            foreach (var leg in legs) PlantFoot(leg);
            UpdateSecondaryMotion();
        }
        private void UpdateGaze()
        {
            var targetYaw = 0f; var targetPitch = 0f;
            if (GazeTarget != null)
            {
                var local = transform.InverseTransformDirection(GazeTarget.position + Vector3.up * 1.45f - Head.position);
                targetYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                targetPitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            }
            yaw = Mathf.SmoothDampAngle(yaw, Mathf.Clamp(targetYaw, -34, 34), ref yawVelocity, .28f, 90);
            pitch = Mathf.SmoothDampAngle(pitch, Mathf.Clamp(targetPitch, -10, 10), ref pitchVelocity, .32f, 45);
            Rotate(Chest, yaw * .10f, 0);
            Rotate(neck, yaw * .25f, pitch * .35f);
            Rotate(Head, yaw * .65f, pitch * .65f);
            if (Time.time >= gazeShiftAt)
            {
                eyeOffset = new Vector2((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() - .5f);
                gazeShiftAt = Time.time + 1.8f + (float)random.NextDouble() * 2.6f;
            }
            var eyeYaw = Mathf.Clamp(targetYaw - yaw, -11, 11) + eyeOffset.x;
            var eyePitch = Mathf.Clamp(targetPitch - pitch, -7, 7) + eyeOffset.y;
            Rotate(leftEye, eyeYaw, eyePitch); Rotate(rightEye, eyeYaw, eyePitch);
        }
        private void Rotate(Transform bone, float yawAngle, float pitchAngle)
        {
            bone.rotation = Quaternion.AngleAxis(yawAngle, transform.up) *
                Quaternion.AngleAxis(pitchAngle, transform.right) * bone.rotation;
        }
        private void UpdateExpression()
        {
            var warmth = emotion == "warm" ? 42 + 12 * Mathf.Exp(-ActionElapsed * .5f) : 3;
            Weight(SmileIndex, warmth, 5);
            Weight(joyIndex, emotion == "warm" ? 7 : 0, 4);
            Weight(firmIndex, emotion == "firm" ? 34 : 0, 6);
            Weight(thoughtfulIndex, emotion == "thoughtful" ? 25 : 0, 5);
            var phase = Time.time - blinkAt;
            var blink = phase < 0 ? 0 : phase < .07f ? Mathf.SmoothStep(0, 100, phase / .07f) :
                phase < .095f ? 100 : Mathf.SmoothStep(100, 0, (phase - .095f) / .14f);
            if (blinkIndex >= 0) Face.SetBlendShapeWeight(blinkIndex, blink);
            if (phase > .235f) blinkAt = Time.time + 3.2f + (float)random.NextDouble() * 2.5f;
            // No synthetic speech loop: this prototype has no voice to synchronize against.
            Weight(mouthIndex, 0);
        }

        private void PlantFoot(PlantedLeg leg)
        {
            var target = transform.TransformPoint(leg.Anchor);
            var delta = target - leg.Upper.position;
            var distance = Mathf.Clamp(delta.magnitude, .001f, leg.UpperLength + leg.LowerLength - .0001f);
            var direction = delta.normalized;
            var along = (leg.UpperLength * leg.UpperLength - leg.LowerLength * leg.LowerLength + distance * distance) / (2 * distance);
            var height = Mathf.Sqrt(Mathf.Max(0, leg.UpperLength * leg.UpperLength - along * along));
            var bend = Vector3.ProjectOnPlane(transform.forward, direction).normalized;
            var knee = leg.Upper.position + direction * along + bend * height;
            leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - leg.Upper.position, knee - leg.Upper.position) * leg.Upper.rotation;
            leg.Lower.rotation = Quaternion.FromToRotation(leg.Foot.position - leg.Lower.position, target - leg.Lower.position) * leg.Lower.rotation;
            leg.Foot.rotation = transform.rotation * leg.Rotation;
        }
        private void UpdateSecondaryMotion()
        {
            var dt = Mathf.Max(.001f, Time.deltaTime);
            var delta = Head.rotation * Quaternion.Inverse(previousHeadRotation);
            delta.ToAngleAxis(out var angle, out var axis);
            var angular = transform.InverseTransformDirection(axis) * Mathf.DeltaAngle(0, angle) / dt;
            var desiredHair = Vector2.ClampMagnitude(new Vector2(-angular.x * .004f, -angular.y * .003f), 2);
            hairLag = Vector2.SmoothDamp(hairLag, desiredHair, ref hairVelocity, .16f, 30, dt);
            for (var i = 0; i < hairTips.Length; i++)
                Rotate(hairTips[i], hairLag.y * .6f, hairLag.x * .6f + Mathf.Sin(Time.time * .8f + i) * .20f);
            var velocity = (hips.localPosition - previousHipsPosition) / dt;
            skirtLag = Vector2.SmoothDamp(skirtLag, Vector2.ClampMagnitude(new Vector2(velocity.z, -velocity.x) * 18, .8f),
                ref skirtVelocity, .24f, 6, dt);
            for (var i = 0; i < skirtBones.Length; i++)
                skirtBones[i].rotation = Quaternion.AngleAxis(skirtLag.y, transform.forward) *
                    Quaternion.AngleAxis(skirtLag.x + Mathf.Sin(Time.time * .7f + i * .4f) * .22f, transform.right) * skirtBones[i].rotation;
            previousHeadRotation = Head.rotation; previousHipsPosition = hips.localPosition;
        }
    }
}
