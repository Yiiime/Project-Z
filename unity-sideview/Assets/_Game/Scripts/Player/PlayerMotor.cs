using UnityEngine;

namespace ZFromMoon.Player
{
    /// <summary>Horizontal physics only; independent of input, session state and artwork.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 3.2f;
        private Rigidbody2D body;
        private float horizontal;
        private bool movementEnabled;

        public int FacingSign { get; private set; } = 1;
        public float HorizontalSpeed => Body.simulated ? Mathf.Abs(Body.linearVelocity.x) : 0f;
        private Rigidbody2D Body => body != null ? body : body = GetComponent<Rigidbody2D>();

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            // The App can awaken before its additive location and floor exist.
            SetSimulationEnabled(false);
        }

        public void SetHorizontal(float value)
        {
            horizontal = movementEnabled ? Mathf.Clamp(value, -1f, 1f) : 0f;
            if (Mathf.Abs(horizontal) > 0.01f)
                FacingSign = horizontal < 0f ? -1 : 1;
        }

        public void SetMovementEnabled(bool value)
        {
            movementEnabled = value;
            if (!value)
            {
                horizontal = 0f;
                Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
            }
        }

        public void SetSimulationEnabled(bool value)
        {
            if (!value)
            {
                SetMovementEnabled(false);
                Body.linearVelocity = Vector2.zero;
                Body.angularVelocity = 0f;
            }
            Body.simulated = value;
        }

        /// <summary>The position denotes the player's feet, matching the spawn anchor.</summary>
        public void Teleport(Vector2 position)
        {
            horizontal = 0f;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.position = position;
        }

        private void FixedUpdate()
        {
            if (!Body.simulated) return;
            float speed = movementEnabled ? horizontal * moveSpeed : 0f;
            Body.linearVelocity = new Vector2(speed, Body.linearVelocity.y);
        }

        private void OnDisable()
        {
            SetSimulationEnabled(false);
        }
    }
}
