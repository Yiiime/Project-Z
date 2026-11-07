using UnityEngine;

namespace Seabreeze
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        public float speed = 4.2f, acceleration = 28, deceleration = 36;
        public bool Grounded { get; private set; }

        public float MoveAxis { get; private set; }

        Rigidbody2D body;
        CapsuleCollider2D capsule;
        CharacterVisual visual;
        Vector3 previousVisualPosition;
        bool hasVisualSample;
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
        }

        void Update()
        {
            bool block = GameSession.Instance == null || GameSession.Instance.InputBlocked || (WorldDirector.Instance != null && WorldDirector.Instance.IsTransitioning);
            MoveAxis = block ? 0 : GameInput.Horizontal;
            if (transform.position.y < -8)
                WorldDirector.Instance.Respawn();
        }

        void FixedUpdate()
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(1 << 8);
            filter.useTriggers = false;
            int count = capsule.Cast(Vector2.down, filter, hits, .12f);
            Grounded = false;
            for (int i = 0; i < count; i++)
                if (hits[i].normal.y > .5f)
                    Grounded = true;
            float vx = Mathf.MoveTowards(body.linearVelocity.x, MoveAxis * speed, (Mathf.Abs(MoveAxis) > .1f ? acceleration : deceleration) * Time.fixedDeltaTime);
            var velocity = new Vector2(vx, body.linearVelocity.y);
            body.linearVelocity = velocity;
        }

        void LateUpdate()
        {
            if (visual == null) return;
            Vector3 position = transform.position;
            float dt = Time.deltaTime;
            float dx = hasVisualSample ? position.x - previousVisualPosition.x : 0;
            if (Mathf.Abs(dx) > Mathf.Max(1, speed * dt * 3))
            {
                dx = 0;
                visual.ResetPlayerMotion();
            }
            previousVisualPosition = position;
            hasVisualSample = true;
            visual.Animate(dt > .000001f ? dx / dt : 0, Grounded, body.linearVelocity.y, dt);
        }

        public void Teleport(Vector2 point)
        {
            body.linearVelocity = Vector2.zero;
            body.position = point;
            transform.position = point;
            previousVisualPosition = transform.position;
            hasVisualSample = false;
            if (visual != null) visual.ResetPlayerMotion();
        }
    }
}
