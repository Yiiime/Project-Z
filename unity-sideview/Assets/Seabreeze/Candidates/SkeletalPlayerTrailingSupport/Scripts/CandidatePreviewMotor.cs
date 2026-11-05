using UnityEngine;
using UnityEngine.InputSystem;

namespace Seabreeze.Candidates.TrailingSupport
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class CandidatePreviewMotor : MonoBehaviour
    {
        public RigAnimatorDriver driver;
        [Tooltip("Unmodified previous skeletal candidate; receives exactly the same displacement sample.")]
        public Seabreeze.Candidates.NaturalGait.RigAnimatorDriver baselineDriver;
        [Min(0)] public float speed = 1.65f;
        [Min(0)] public float acceleration = 28;
        [Min(0)] public float deceleration = 36;
        [Min(0)] public float accelerationRamp = .06f;
        public bool autoWalk = true;
        [Min(.1f)] public float autoChangeSeconds = 3;
        [Min(0)] public float autoRestSeconds = 1;
        public LayerMask groundLayers = 1 << 8;
        [Min(0)] public float groundProbeDistance = .12f;

        [SerializeField, Tooltip("Runtime telemetry: signed speed from actual frame-to-frame displacement.")]
        float actualSpeed;
        [SerializeField] bool grounded;
        public float ActualSpeed => actualSpeed;
        public bool Grounded => grounded;
        public float MoveAxis { get; private set; }

        Rigidbody2D body;
        CapsuleCollider2D capsule;
        Vector3 lastSamplePosition;
        float horizontalAcceleration, autoElapsed, autoDirection = 1;
        readonly RaycastHit2D[] groundHits = new RaycastHit2D[4];

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            if (driver == null) driver = GetComponentInChildren<RigAnimatorDriver>(true);
            body.freezeRotation = true;
            body.gravityScale = 2.5f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        void OnEnable()
        {
            autoElapsed = 0;
            ResetMotion();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) speed = 1.65f;
                if (keyboard.digit2Key.wasPressedThisFrame) speed = 2.2f;
                if (keyboard.digit3Key.wasPressedThisFrame) speed = 4.2f;
            }
            bool left = keyboard != null && (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed);
            bool right = keyboard != null && (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed);
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                autoWalk = !autoWalk;
                autoElapsed = 0;
            }
            if (left || right)
            {
                autoWalk = false;
                autoElapsed = 0;
            }

            if (autoWalk)
            {
                autoElapsed += Time.deltaTime;
                float duration = Mathf.Max(.1f, autoChangeSeconds);
                float segment = duration + Mathf.Max(0, autoRestSeconds);
                if (autoElapsed >= segment)
                {
                    // Preserve timer parity if a long frame spans more than one turn.
                    int turns = Mathf.FloorToInt(autoElapsed / segment);
                    if ((turns & 1) != 0) autoDirection = -autoDirection;
                    autoElapsed %= segment;
                }
                MoveAxis = autoElapsed < duration ? autoDirection : 0;
            }
            else MoveAxis = (right ? 1 : 0) - (left ? 1 : 0);

            RefreshGrounded();
            float deltaX = transform.position.x - lastSamplePosition.x;
            lastSamplePosition = transform.position;
            actualSpeed = Time.deltaTime > 0 ? deltaX / Time.deltaTime : 0;
            if (driver != null) driver.SampleMotion(deltaX, Time.deltaTime, grounded);
            if (baselineDriver != null) baselineDriver.SampleMotion(deltaX, Time.deltaTime, grounded);
        }

        void FixedUpdate()
        {
            if (body == null || !body.simulated) return;
            var velocity = body.linearVelocity;
            velocity.x = HorizontalVelocity(velocity.x, MoveAxis * speed);
            body.linearVelocity = velocity;
            // Horizontal limits belong to the scene's physical walls, not a position clamp.
        }

        float HorizontalVelocity(float current, float target)
        {
            float difference = target - current;
            if (Mathf.Abs(difference) < .001f)
            {
                horizontalAcceleration = 0;
                return target;
            }
            bool braking = Mathf.Abs(target) < .01f || current * target < 0;
            float limit = braking ? deceleration : acceleration;
            float desired = Mathf.Sign(difference) * limit;
            if (horizontalAcceleration * desired < 0) horizontalAcceleration = 0;
            horizontalAcceleration = accelerationRamp <= 0 ? desired :
                Mathf.MoveTowards(horizontalAcceleration, desired, limit * Time.fixedDeltaTime / accelerationRamp);
            float next = current + horizontalAcceleration * Time.fixedDeltaTime;
            if ((target - next) * difference <= 0)
            {
                horizontalAcceleration = 0;
                return target;
            }
            // A speed-preset change decelerates with the same ramp instead of snapping velocity.
            return next;
        }

        void RefreshGrounded()
        {
            grounded = false;
            if (capsule == null || !capsule.enabled) return;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(groundLayers);
            filter.useTriggers = false;
            int count = capsule.Cast(Vector2.down, filter, groundHits, groundProbeDistance);
            for (int i = 0; i < count; i++)
                if (groundHits[i].normal.y > .5f) { grounded = true; break; }
        }

        public void Teleport(Vector2 position)
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
            ResetMotion();
        }

        public void ResetMotion()
        {
            lastSamplePosition = transform.position;
            actualSpeed = horizontalAcceleration = 0;
            MoveAxis = 0;
            if (driver != null) driver.ResetMotion();
            if (baselineDriver != null) baselineDriver.ResetMotion();
        }

        void OnDisable()
        {
            if (body != null)
                body.linearVelocity = new Vector2(0, body.linearVelocity.y);
            ResetMotion();
        }
    }
}
