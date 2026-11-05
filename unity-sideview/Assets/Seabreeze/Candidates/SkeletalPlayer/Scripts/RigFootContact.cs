using System;
using UnityEngine;

namespace Seabreeze.Candidates
{
    // Animator evaluates first; official SpriteSkin deformation runs at LateUpdate order 10.
    // This candidate-only layer preserves planted contacts while Idle takes over the upper body.
    [DefaultExecutionOrder(-5)]
    [DisallowMultipleComponent]
    public sealed class RigFootContact : MonoBehaviour
    {
        [Serializable] public sealed class Leg
        {
            public Transform thigh, shin, foot;
            [NonSerialized] public float upper, lower;
            [NonSerialized] public Vector3 home, footDirection, previous, target, stepFrom, stepTo, releaseLocal;
            public void Bind(Transform facing)
            {
                upper = Vector3.Distance(facing.InverseTransformPoint(thigh.position), facing.InverseTransformPoint(shin.position));
                lower = Vector3.Distance(facing.InverseTransformPoint(shin.position), facing.InverseTransformPoint(foot.position));
                home = facing.InverseTransformPoint(foot.position);
                footDirection = facing.InverseTransformVector(foot.TransformVector(Vector3.right)).normalized;
            }
        }
        public RigAnimatorDriver driver;
        public Transform facingRoot, pelvis;
        public Leg near = new Leg(), far = new Leg();
        [Min(.1f)] public float stepDuration = .25f;
        [Min(0)] public float stepHeight = .09f;
        public bool Settling { get; private set; }
        public int MovingFoot { get; private set; } = -1; // 0 near, 1 far, -1 both planted
        public Vector3 NearTarget => near.target;
        public Vector3 FarTarget => far.target;

        bool ready, wasWalking, havePrevious;
        int revision = -1, stage, first;
        float elapsed, release = 1;
        Leg[] legs;

        void Awake()
        {
            ready = driver && facingRoot && pelvis && near.thigh && near.shin && near.foot && far.thigh && far.shin && far.foot;
            if (!ready) { enabled = false; return; }
            legs = new[] { near, far };
            foreach (var leg in legs) leg.Bind(facingRoot);
        }

        void LateUpdate()
        {
            if (!ready) return;
            if (revision != driver.ResetVersion)
            {
                revision = driver.ResetVersion;
                wasWalking = havePrevious = Settling = false; MovingFoot = -1; release = 1;
                foreach (var leg in legs) leg.target = facingRoot.TransformPoint(leg.home);
            }
            if (driver.Walking)
            {
                if (!wasWalking)
                {
                    release = 0;
                    foreach (var leg in legs) leg.releaseLocal = facingRoot.InverseTransformPoint(leg.target);
                }
                Settling = false; MovingFoot = -1;
                release = Mathf.Min(1, release + Time.deltaTime / .12f);
                if (release < 1)
                {
                    float blend = Smooth(release);
                    foreach (var leg in legs)
                    {
                        leg.target = Vector3.Lerp(facingRoot.TransformPoint(leg.releaseLocal), leg.foot.position, blend);
                        var aim = Vector3.Lerp(facingRoot.TransformVector(leg.footDirection), leg.foot.TransformVector(Vector3.right), blend);
                        Solve(leg, leg.target, aim);
                    }
                }
            }
            else
            {
                // When already standing, follow any physics settling of the actor's ground height.
                // Retaining an initial pre-contact world Y would leave the shoes hovering after spawn.
                if (!wasWalking && !Settling)
                    foreach (var leg in legs) leg.target = facingRoot.TransformPoint(leg.home);
                if (wasWalking && havePrevious)
                {
                    foreach (var leg in legs) leg.target = leg.previous;
                    // Finish the airborne foot first. If both are planted, move the foot furthest from home.
                    float nearLift = near.target.y - facingRoot.TransformPoint(near.home).y;
                    float farLift = far.target.y - facingRoot.TransformPoint(far.home).y;
                    first = Mathf.Max(nearLift, farLift) > .025f ? (nearLift >= farLift ? 0 : 1) :
                        (Vector3.Distance(near.target, facingRoot.TransformPoint(near.home)) >= Vector3.Distance(far.target, facingRoot.TransformPoint(far.home)) ? 0 : 1);
                    stage = 0; Settling = true; BeginStep(first);
                }
                if (Settling)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / stepDuration);
                    var moving = legs[MovingFoot];
                    moving.target = Vector3.Lerp(moving.stepFrom, moving.stepTo, Smooth(t)) +
                        Vector3.up * (stepHeight * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2));
                    if (t >= 1)
                    {
                        moving.target = moving.stepTo;
                        if (stage++ == 0) BeginStep(1 - first);
                        else { Settling = false; MovingFoot = -1; }
                    }
                }
                // Keep both targets, including through Idle's breathing; only the airborne foot moves.
                LowerPelvisForReach();
                foreach (var leg in legs) Solve(leg, leg.target, facingRoot.TransformVector(leg.footDirection));
            }
            foreach (var leg in legs) leg.previous = leg.foot.position;
            havePrevious = true; wasWalking = driver.Walking;
        }

        void BeginStep(int index)
        {
            MovingFoot = index; elapsed = 0;
            var leg = legs[index]; leg.stepFrom = leg.target;
            leg.stepTo = facingRoot.TransformPoint(leg.home);
            // No alternating foot motion is needed for a contact already at its resting location.
            if (Vector3.Distance(leg.stepFrom, leg.stepTo) < .015f) elapsed = stepDuration;
        }
        void LowerPelvisForReach()
        {
            float offset = 0;
            foreach (var leg in legs)
            {
                var hip = facingRoot.InverseTransformPoint(leg.thigh.position);
                var target = facingRoot.InverseTransformPoint(leg.target);
                float reach = (leg.upper + leg.lower) * .997f;
                float dx = target.x - hip.x;
                float height = Mathf.Sqrt(Mathf.Max(.0001f, reach * reach - dx * dx));
                offset = Mathf.Min(offset, target.y + height - hip.y);
            }
            pelvis.position += facingRoot.TransformVector(Vector3.up * offset);
        }
        void Solve(Leg leg, Vector3 worldTarget, Vector3 worldFootDirection)
        {
            Vector2 origin = facingRoot.InverseTransformPoint(leg.thigh.position);
            Vector2 target = facingRoot.InverseTransformPoint(worldTarget);
            Vector2 delta = target - origin;
            float d = Mathf.Clamp(delta.magnitude, Mathf.Abs(leg.upper - leg.lower) + .0001f, leg.upper + leg.lower - .0001f);
            var direction = delta.normalized;
            float along = (leg.upper * leg.upper - leg.lower * leg.lower + d * d) / (2 * d);
            float height = Mathf.Sqrt(Mathf.Max(0, leg.upper * leg.upper - along * along));
            Vector2 knee = origin + direction * along + new Vector2(-direction.y, direction.x) * height;
            Aim(leg.thigh, facingRoot.TransformPoint(knee) - leg.thigh.position, leg.shin.localPosition);
            Aim(leg.shin, worldTarget - leg.shin.position, leg.foot.localPosition);
            Aim(leg.foot, worldFootDirection, Vector3.right);
        }
        static void Aim(Transform bone, Vector3 worldDirection, Vector3 localChildDirection)
        {
            Vector3 local = bone.parent.InverseTransformVector(worldDirection);
            float angle = Mathf.Atan2(local.y, local.x) - Mathf.Atan2(localChildDirection.y, localChildDirection.x);
            bone.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
        }
        static float Smooth(float x) => x * x * x * (x * (x * 6 - 15) + 10);
    }
}
