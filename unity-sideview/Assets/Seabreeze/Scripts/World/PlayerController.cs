using UnityEngine;

namespace Seabreeze
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        public float speed = 4.2f, acceleration = 28, deceleration = 36, jumpSpeed = 8.6f;
        [Min(0)] public float accelerationRamp = .06f;
        public bool Grounded { get; private set; }

        public float MoveAxis { get; private set; }

        Rigidbody2D body;
        CapsuleCollider2D capsule;
        CharacterVisual visual;
        float coyote, buffer;
        float horizontalAcceleration;
        Vector3 lastVisualPosition;
        bool conversationLocked;
        Vector2 suspendedVelocity;
        bool priorSimulation;
        readonly RaycastHit2D[] hits = new RaycastHit2D[4];
        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            body.freezeRotation = true;
            body.gravityScale = 2.5f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            capsule.size = new Vector2(.5f, 2.35f);
            capsule.offset = new Vector2(0, 1.2f);
            visual = GetComponentInChildren<CharacterVisual>();
            lastVisualPosition = transform.position;
        }

        void Update()
        {
            bool conversation = GameSession.Instance != null && GameSession.Instance.Conversation != null && GameSession.Instance.Conversation.IsActive;
            if (conversation != conversationLocked) SetConversationLocked(conversation);
            bool block = GameSession.Instance == null || GameSession.Instance.InputBlocked || (WorldDirector.Instance != null && WorldDirector.Instance.IsTransitioning);
            MoveAxis = block ? 0 : GameInput.Horizontal;
            if (block)
                buffer = 0;
            if (!block && GameInput.JumpPressed)
                buffer = .12f;
            else
                buffer -= Time.deltaTime;
            if (transform.position.y < -8)
                WorldDirector.Instance.Respawn();
        }

        void FixedUpdate()
        {
            if (conversationLocked) return;
            RefreshGrounded();
            coyote = Grounded ? .12f : coyote - Time.fixedDeltaTime;
            float vx = HorizontalVelocity(body.linearVelocity.x, MoveAxis * speed);
            var velocity = new Vector2(vx, body.linearVelocity.y);
            if (buffer > 0 && coyote > 0 && GameSession.Instance != null && !GameSession.Instance.InputBlocked && !WorldDirector.Instance.IsTransitioning)
            {
                velocity.y = jumpSpeed;
                buffer = 0;
                coyote = 0;
                Grounded = false;
            }

            body.linearVelocity = velocity;
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
            return Mathf.Clamp(next, -speed, speed);
        }

        void LateUpdate()
        {
            float distance = transform.position.x - lastVisualPosition.x;
            lastVisualPosition = transform.position;
            if (!visual || Time.deltaTime <= 0) return;
            if (conversationLocked) distance = 0;
            visual.AnimateLocomotion(distance / Time.deltaTime, Grounded, body.linearVelocity.y,
                Mathf.Abs(distance), Time.deltaTime);
        }

        void RefreshGrounded()
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(1 << 8);
            filter.useTriggers = false;
            int count = capsule.Cast(Vector2.down, filter, hits, .12f);
            Grounded = false;
            for (int i = 0; i < count; i++)
                if (hits[i].normal.y > .5f)
                    Grounded = true;
        }

        public void Teleport(Vector2 point)
        {
            suspendedVelocity = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            body.position = point;
            transform.position = point;
            lastVisualPosition = point;
            horizontalAcceleration = 0;
            Physics2D.SyncTransforms();
            if (conversationLocked)
            {
                var ground = Physics2D.CapsuleCast(point + capsule.offset, capsule.size, capsule.direction,
                    0, Vector2.down, .15f, 1 << 8);
                Grounded = ground.collider != null && ground.normal.y > .5f;
            }
            else RefreshGrounded();
            if (visual) visual.ResetLocomotion();
            buffer = 0;
        }
        public void SetConversationLocked(bool value)
        {
            if (!body || value == conversationLocked) return;
            conversationLocked = value;
            horizontalAcceleration = 0;
            lastVisualPosition = transform.position;
            MoveAxis = 0; buffer = 0;
            if (value)
            {
                // An opening/resumed conversation may lock before the first physics tick.
                // Probe while the collider still participates so a grounded spawn keeps its idle pose.
                Physics2D.SyncTransforms();
                RefreshGrounded();
                suspendedVelocity = body.linearVelocity;
                priorSimulation = body.simulated;
                body.linearVelocity = Vector2.zero;
                body.simulated = false;
                if (visual) visual.Animate(0, Grounded, 0);
            }
            else
            {
                body.simulated = priorSimulation;
                body.linearVelocity = new Vector2(0, suspendedVelocity.y);
            }
        }
    }
}
