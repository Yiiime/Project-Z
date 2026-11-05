using System;
using UnityEngine;

namespace Seabreeze.Candidates.NaturalGait
{
    // Animator supplies the authored gait. This final, candidate-only correction owns
    // ground contacts; SpriteSkin deforms later (execution order 10).
    [DefaultExecutionOrder(-5)]
    [DisallowMultipleComponent]
    public sealed class RigFootContact : MonoBehaviour
    {
        public enum ContactKind { Swing, Heel, Sole, Forefoot }

        [Serializable] public sealed class Leg
        {
            public Transform thigh, shin, foot, heel, forefoot;
            [NonSerialized] public float upper, lower, groundLocalY, phase, previousPhase;
            [NonSerialized] public Vector3 home, footDirection, previous, target, stepFrom, stepTo;
            [NonSerialized] public Vector3 previousDirection, previousLocal, previousHeel, previousForefoot, anchor;
            [NonSerialized] public Vector3 releaseLocal, releaseDirection, stopDirection, stopMarker;
            [NonSerialized] public bool anchored;
            [NonSerialized] public ContactKind kind;
            public void Bind(Transform facing)
            {
                upper = Vector3.Distance(facing.InverseTransformPoint(thigh.position), facing.InverseTransformPoint(shin.position));
                lower = Vector3.Distance(facing.InverseTransformPoint(shin.position), facing.InverseTransformPoint(foot.position));
                home = facing.InverseTransformPoint(foot.position);
                footDirection = facing.InverseTransformVector(foot.TransformVector(Vector3.right)).normalized;
                groundLocalY = Mathf.Min(facing.InverseTransformPoint(heel.position).y, facing.InverseTransformPoint(forefoot.position).y);
                previousDirection = footDirection;
            }
        }

        public RigAnimatorDriver driver;
        public Transform facingRoot, pelvis;
        public Leg near = new Leg(), far = new Leg();
        [Range(.5f, .7f)] public float stanceFraction = .60f;
        [Range(.02f, .2f)] public float heelLandingEnd = .12f;
        [Range(.2f, .55f)] public float toeRollStart = .38f;
        [Min(.1f)] public float stepDuration = .25f;
        [Min(0)] public float stepHeight = .09f;
        [Min(.01f)] public float startReleaseDuration = .12f;
        [Min(.01f)] public float stopFlattenDuration = .18f;
        public bool Settling { get; private set; }
        public int MovingFoot { get; private set; } = -1;
        public Vector3 NearTarget => near.target;
        public Vector3 FarTarget => far.target;
        public Vector3 NearAnchor => near.anchor;
        public Vector3 FarAnchor => far.anchor;
        public float NearPhase => near.phase;
        public float FarPhase => far.phase;
        public ContactKind NearContact => near.kind;
        public ContactKind FarContact => far.kind;
        public float ReachCorrection { get; private set; }

        bool ready, wasWalking, havePrevious;
        int revision = -1, stage, first;
        float elapsed, stopElapsed, release = 1, facingSign;
        Leg[] legs;

        void Awake()
        {
            ready = driver && facingRoot && pelvis && Valid(near) && Valid(far);
            if (!ready) { enabled = false; return; }
            legs = new[] { near, far };
            foreach (var leg in legs) leg.Bind(facingRoot);
            facingSign = driver.FacingSign;
        }
        static bool Valid(Leg leg) => leg.thigh && leg.shin && leg.foot && leg.heel && leg.forefoot;

        void LateUpdate()
        {
            if (!ready) return;
            ReachCorrection = 0;
            if (revision != driver.ResetVersion)
            {
                revision = driver.ResetVersion; facingSign = driver.FacingSign;
                wasWalking = havePrevious = Settling = false; MovingFoot = -1; release = 1;
                foreach (var leg in legs)
                {
                    leg.target = facingRoot.TransformPoint(leg.home);
                    leg.previous = leg.target;
                    leg.previousLocal = leg.home;
                    leg.previousDirection = leg.footDirection;
                    leg.anchored = false; leg.kind = ContactKind.Sole;
                }
            }
            else if (facingSign != driver.FacingSign)
            {
                // A facing change mirrors the last visible pose. It is not a teleport
                // to the bind pose: closing both legs here caused a one-frame pop.
                facingSign = driver.FacingSign;
                wasWalking = Settling = false; MovingFoot = -1;
                foreach (var leg in legs)
                {
                    leg.target = facingRoot.TransformPoint(havePrevious ? leg.previousLocal : leg.home);
                    leg.previous = leg.target;
                    leg.anchored = false;
                }
            }
            near.phase = driver.ActiveWalkPhase;
            far.phase = Mathf.Repeat(near.phase + .5f, 1);
            if (driver.Walking) Walk();
            else Stand();
            foreach (var leg in legs)
            {
                leg.previous = leg.foot.position;
                leg.previousLocal = facingRoot.InverseTransformPoint(leg.previous);
                leg.previousDirection = facingRoot.InverseTransformVector(leg.foot.TransformVector(Vector3.right)).normalized;
                leg.previousHeel = leg.heel.position; leg.previousForefoot = leg.forefoot.position;
                leg.previousPhase = leg.phase;
            }
            havePrevious = true; wasWalking = driver.Walking;
        }

        void Walk()
        {
            if (!wasWalking)
            {
                release = 0;
                foreach (var leg in legs)
                {
                    leg.releaseLocal = facingRoot.InverseTransformPoint(leg.target);
                    leg.releaseDirection = leg.previousDirection;
                    leg.anchored = false;
                }
            }
            Settling = false; MovingFoot = -1;
            release = Mathf.Min(1, release + Time.deltaTime / startReleaseDuration);
            foreach (var leg in legs)
            {
                Vector3 authoredDirection = facingRoot.InverseTransformVector(leg.foot.TransformVector(Vector3.right)).normalized;
                ContactKind desired = leg.phase >= stanceFraction ? ContactKind.Swing :
                    leg.phase >= toeRollStart ? ContactKind.Forefoot : leg.phase >= heelLandingEnd ? ContactKind.Sole : ContactKind.Heel;
                if (release < 1 || driver.WalkBlend < .999f)
                {
                    // Release the previous planted pose into the actual Animator blend.
                    // Capture a world anchor only after that pose has finished blending.
                    float blend = Smooth(release);
                    leg.target = Vector3.Lerp(facingRoot.TransformPoint(leg.releaseLocal), leg.foot.position, blend);
                    var direction = DirectionLerp(leg.releaseDirection, authoredDirection, blend);
                    KeepSoleAboveGround(leg, ref leg.target, direction);
                    Solve(leg, leg.target, direction);
                    leg.anchored = false; leg.kind = desired;
                    continue;
                }
                if (desired == ContactKind.Swing)
                {
                    leg.anchored = false; leg.kind = desired;
                    leg.target = leg.foot.position;
                    // This is only a floor safeguard, not the source of the swing arc.
                    KeepSoleAboveGround(leg, ref leg.target, authoredDirection);
                    Solve(leg, leg.target, authoredDirection);
                    continue;
                }

                Transform marker = desired == ContactKind.Forefoot ? leg.forefoot : leg.heel;
                bool wrapped = leg.phase < leg.previousPhase;
                // A standalone prefab preview is a treadmill: its root intentionally
                // stays still, so the support surface moves backwards at preview speed.
                if (leg.anchored && !driver.IsExternallyDriven)
                    leg.anchor -= facingRoot.TransformVector(Vector3.right) * Mathf.Abs(driver.ActualSpeed) * Time.deltaTime;
                if (!leg.anchored || wrapped)
                {
                    leg.anchor = marker.position;
                    leg.anchor.y = GroundY(leg);
                    leg.anchored = true;
                }
                else if (desired == ContactKind.Forefoot && leg.kind != ContactKind.Forefoot)
                {
                    // Transfer the same rigid sole from heel to forefoot while it is
                    // flat. The geometric offset preserves its world placement.
                    leg.anchor += MarkerOffset(leg.forefoot, authoredDirection) - MarkerOffset(leg.heel, authoredDirection);
                    leg.anchor.y = GroundY(leg);
                }
                leg.kind = desired;
                leg.target = leg.anchor - MarkerOffset(marker, authoredDirection);
                Solve(leg, leg.target, authoredDirection);
            }
        }

        void Stand()
        {
            if (wasWalking && havePrevious)
            {
                stopElapsed = 0;
                foreach (var leg in legs)
                {
                    leg.target = leg.previous;
                    leg.stopDirection = leg.previousDirection;
                    float tilt = Vector2.SignedAngle(leg.footDirection, leg.stopDirection);
                    bool heelPivot = tilt >= 0;
                    leg.stopMarker = (heelPivot ? leg.heel : leg.forefoot).localPosition;
                    leg.anchor = heelPivot ? leg.previousHeel : leg.previousForefoot;
                    leg.anchor.y = GroundY(leg);
                }
                bool nearSwing = near.kind == ContactKind.Swing, farSwing = far.kind == ContactKind.Swing;
                first = nearSwing != farSwing ? (nearSwing ? 0 : 1) :
                    (Vector3.Distance(near.target, facingRoot.TransformPoint(near.home)) >=
                     Vector3.Distance(far.target, facingRoot.TransformPoint(far.home)) ? 0 : 1);
                stage = 0; Settling = true; BeginStep(first);
            }
            if (!Settling)
            {
                // Follow only the actor's small spawn/ground settling while already idle.
                foreach (var leg in legs)
                {
                    leg.target = facingRoot.TransformPoint(leg.home);
                    leg.kind = ContactKind.Sole; leg.anchored = false;
                }
                LowerPelvisForReach();
                foreach (var leg in legs) Solve(leg, leg.target, leg.footDirection);
                return;
            }

            stopElapsed += Time.deltaTime;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stepDuration);
            float flatten = Smooth(Mathf.Clamp01(stopElapsed / stopFlattenDuration));
            var moving = legs[MovingFoot];
            for (int i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                Vector3 direction = DirectionLerp(leg.stopDirection, leg.footDirection, flatten);
                if (i == MovingFoot)
                {
                    leg.target = Vector3.Lerp(leg.stepFrom, leg.stepTo, Smooth(t)) +
                        Vector3.up * (stepHeight * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2));
                    KeepSoleAboveGround(leg, ref leg.target, direction);
                    leg.kind = ContactKind.Swing;
                }
                else if (stage == 0)
                {
                    // An elevated heel settles around its planted forefoot; an initial
                    // heel strike settles around its heel. Never flatten about the ankle.
                    leg.target = leg.anchor - LocalOffset(leg.stopMarker, direction);
                    leg.kind = flatten >= 1 ? ContactKind.Sole :
                        Vector2.SignedAngle(leg.footDirection, leg.stopDirection) >= 0 ? ContactKind.Heel : ContactKind.Forefoot;
                }
                else leg.kind = ContactKind.Sole; // First step's newly planted home contact.
            }
            // Reach is a safeguard used for stop transitions only. Walking pelvis
            // motion always comes from the independently authored load-bearing curve.
            LowerPelvisForReach();
            foreach (var leg in legs)
                Solve(leg, leg.target, DirectionLerp(leg.stopDirection, leg.footDirection, flatten));
            if (t >= 1)
            {
                moving.target = moving.stepTo;
                if (stage++ == 0) BeginStep(1 - first);
                else { Settling = false; MovingFoot = -1; }
            }
        }

        void BeginStep(int index)
        {
            MovingFoot = index; elapsed = 0;
            var leg = legs[index]; leg.stepFrom = leg.target;
            leg.stepTo = facingRoot.TransformPoint(leg.home);
            if (Vector3.Distance(leg.stepFrom, leg.stepTo) < .015f) elapsed = stepDuration;
        }
        float GroundY(Leg leg) => facingRoot.TransformPoint(new Vector3(0, leg.groundLocalY, 0)).y;
        Vector3 MarkerOffset(Transform marker, Vector3 localDirection) => LocalOffset(marker.localPosition, localDirection);
        Vector3 LocalOffset(Vector3 markerLocal, Vector3 localDirection)
        {
            float angle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;
            return facingRoot.TransformVector(Quaternion.Euler(0, 0, angle) * markerLocal);
        }
        void KeepSoleAboveGround(Leg leg, ref Vector3 ankle, Vector3 localDirection)
        {
            float lowest = Mathf.Min(MarkerOffset(leg.heel, localDirection).y, MarkerOffset(leg.forefoot, localDirection).y);
            ankle.y = Mathf.Max(ankle.y, GroundY(leg) - lowest);
        }
        static Vector3 DirectionLerp(Vector3 from, Vector3 to, float t)
        {
            // LerpAngle uses degrees; keep all public vectors in the facing-root basis.
            float angle = Mathf.LerpAngle(Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg,
                Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
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
            ReachCorrection = offset;
            pelvis.position += facingRoot.TransformVector(Vector3.up * offset);
        }
        void Solve(Leg leg, Vector3 worldTarget, Vector3 localFootDirection)
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
            Aim(leg.foot, facingRoot.TransformVector(localFootDirection), Vector3.right);
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
