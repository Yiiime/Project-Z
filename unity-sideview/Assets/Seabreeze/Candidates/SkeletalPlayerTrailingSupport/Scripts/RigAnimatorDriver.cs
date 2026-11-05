using UnityEngine;

namespace Seabreeze.Candidates.TrailingSupport
{
    // The preview motor samples first; Animator evaluates after both Update calls.
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class RigAnimatorDriver : MonoBehaviour
    {
        public Animator animator;
        public Transform facingRoot;
        [Min(.1f)] public float strideLength = 2.8f;
        [Min(.01f)] public float walkClipLength = 1f;

        [Tooltip("Play an in-place walk only until a motion source supplies its first sample.")]
        public bool previewInPlace = true;
        [Tooltip("Signed preview speed in world units/second; ignored once a motor supplies motion.")]
        public float previewSpeed = 1.4f;

        [SerializeField, Tooltip("Runtime telemetry: signed speed measured by the motion source.")]
        float actualSpeed;
        [SerializeField, Tooltip("Runtime telemetry: multiplier of the Walk state's clip speed.")]
        float cycleRate;
        [SerializeField] bool walking;

        public float ActualSpeed => actualSpeed;
        public float CycleRate => cycleRate;
        public bool Walking => walking;
        public int ResetVersion { get; private set; }
        public bool IsExternallyDriven => hasMotionSource;
        public float FacingSign => facingRoot && facingRoot.localScale.x < 0 ? -1 : 1;

        // Read after Animator evaluation (the contact layer uses LateUpdate), including
        // the destination state's clock while Idle blends into Walk.
        public float ActiveWalkPhase
        {
            get
            {
                if (!animator || !animator.isActiveAndEnabled) return 0;
                if (animator.IsInTransition(0))
                {
                    var next = animator.GetNextAnimatorStateInfo(0);
                    if (next.shortNameHash == WalkStateId) return Mathf.Repeat(next.normalizedTime, 1);
                }
                var current = animator.GetCurrentAnimatorStateInfo(0);
                return current.shortNameHash == WalkStateId ? Mathf.Repeat(current.normalizedTime, 1) : 0;
            }
        }
        public float WalkBlend
        {
            get
            {
                if (!animator || !animator.isActiveAndEnabled) return 0;
                var current = animator.GetCurrentAnimatorStateInfo(0);
                if (!animator.IsInTransition(0)) return current.shortNameHash == WalkStateId ? 1 : 0;
                float t = Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime);
                return animator.GetNextAnimatorStateInfo(0).shortNameHash == WalkStateId ? t : 1 - t;
            }
        }

        static readonly int WalkingId = Animator.StringToHash("Walking");
        static readonly int CycleRateId = Animator.StringToHash("CycleRate");
        static readonly int WalkStateId = Animator.StringToHash("Walk");
        bool hasMotionSource, sampleGrounded = true, facingLeft;
        float sampledSpeed, facingScaleX = 1;
        int sampleFrame = -1;
        Transform cachedFacingRoot;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;
            CacheFacingRoot();
        }

        public void SampleMotion(float signedDeltaX, float deltaTime, bool grounded = true)
        {
            hasMotionSource = true;
            sampleFrame = Time.frameCount;
            sampleGrounded = grounded;
            sampledSpeed = deltaTime > 0 ? signedDeltaX / deltaTime : 0;
        }

        public void ResetMotion()
        {
            ResetVersion++;
            // Stay externally driven after a reset; a stopped/disabled motor must not
            // accidentally fall back to the standalone in-place demonstration.
            hasMotionSource = true;
            sampleFrame = Time.frameCount;
            sampledSpeed = actualSpeed = cycleRate = 0;
            sampleGrounded = true;
            walking = false;
            if (animator != null)
            {
                animator.SetBool(WalkingId, false);
                animator.SetFloat(CycleRateId, 0);
            }
        }

        void Update()
        {
            bool freshSample = sampleFrame == Time.frameCount;
            actualSpeed = hasMotionSource ? (freshSample ? sampledSpeed : 0) :
                previewInPlace ? previewSpeed : 0;
            bool grounded = !hasMotionSource || (freshSample && sampleGrounded);
            float speed = Mathf.Abs(actualSpeed);
            bool previouslyWalking = walking;
            walking = grounded && speed > (walking ? .04f : .1f);
            cycleRate = grounded ? speed * Mathf.Max(.01f, walkClipLength) / Mathf.Max(.1f, strideLength) : 0;

            CacheFacingRoot();
            if (facingRoot != null && speed > .15f && facingLeft != (actualSpeed < 0))
            {
                facingLeft = actualSpeed < 0;
                // Mirror the complete visual hierarchy, never the physics root.
                if (facingRoot.GetComponent<Rigidbody2D>() == null)
                {
                    var scale = facingRoot.localScale;
                    scale.x = facingLeft ? -facingScaleX : facingScaleX;
                    facingRoot.localScale = scale;
                }
            }

            if (animator == null) return;
            animator.SetFloat(CycleRateId, cycleRate);
            animator.SetBool(WalkingId, walking);
            if (walking && !previouslyWalking && animator.IsInTransition(0))
            {
                var current = animator.GetCurrentAnimatorStateInfo(0);
                var next = animator.GetNextAnimatorStateInfo(0);
                if (current.shortNameHash == WalkStateId && next.shortNameHash != WalkStateId)
                {
                    // Resume an interrupted stop at the surviving gait phase, rather
                    // than finish entering Idle and restart at the fixed entry offset.
                    float phase = Mathf.Repeat(current.normalizedTime, 1);
                    // A normalized offset keeps the pose independent of the changing
                    // CycleRate multiplier while the actor accelerates out of a stop.
                    float duration = .12f * Mathf.Max(.01f, cycleRate) / walkClipLength;
                    animator.CrossFade(current.fullPathHash, duration, 0, phase);
                }
            }
            // Idle and transition clocks remain active; only Walk uses CycleRate.
        }

        void CacheFacingRoot()
        {
            if (facingRoot == null || cachedFacingRoot == facingRoot) return;
            cachedFacingRoot = facingRoot;
            facingScaleX = Mathf.Abs(facingRoot.localScale.x);
            facingLeft = facingRoot.localScale.x < 0;
        }
    }
}
