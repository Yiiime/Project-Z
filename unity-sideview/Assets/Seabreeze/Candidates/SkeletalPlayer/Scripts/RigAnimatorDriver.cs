using UnityEngine;

namespace Seabreeze.Candidates
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

        static readonly int WalkingId = Animator.StringToHash("Walking");
        static readonly int CycleRateId = Animator.StringToHash("CycleRate");
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
