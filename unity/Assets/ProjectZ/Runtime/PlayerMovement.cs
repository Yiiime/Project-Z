using UnityEngine;

namespace ProjectZ.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        public bool Locked { get; set; }
        public Transform CameraTransform { get; set; }
        private CharacterController controller;
        private float verticalVelocity;
        private void Awake() => controller = GetComponent<CharacterController>();
        private void Update()
        {
            var input = Vector3.zero;
            if (!Locked)
            {
                input.x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                input.z = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            }
            var yaw = CameraTransform != null ? CameraTransform.eulerAngles.y : 0;
            var direction = Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(input, 1);
            verticalVelocity = controller.isGrounded ? -2 : verticalVelocity + Physics.gravity.y * Time.deltaTime;
            controller.Move((direction * 4.2f + Vector3.up * verticalVelocity) * Time.deltaTime);
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 12);
            var position = transform.position;
            if (position.y < -5) { controller.enabled = false; transform.position = new Vector3(0, 1, 0); controller.enabled = true; }
        }
    }

    public sealed class FollowCamera : MonoBehaviour
    {
        public Transform Target;
        public Transform ConversationTarget;
        public bool Locked;
        private float yaw = -25;
        private Renderer[] playerRenderers;
        private bool wasConversing;
        private float conversationOrbit;
        private float conversationDistance = 2.65f;
        public float ConversationOrbit => conversationOrbit;
        private void LateUpdate()
        {
            if (Target == null) return;
            var conversing = ConversationTarget != null;
            if (conversing != wasConversing)
            {
                if (playerRenderers == null) playerRenderers = Target.GetComponentsInChildren<Renderer>();
                foreach (var renderer in playerRenderers) renderer.enabled = !conversing;
                wasConversing = conversing;
                conversationOrbit = 0;
            }
            if (ConversationTarget != null)
            {
                conversationOrbit += ((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                    (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0)) * Time.deltaTime * 55;
                conversationDistance = Mathf.Clamp(conversationDistance + ((Input.GetKey(KeyCode.DownArrow) ? 1 : 0) -
                    (Input.GetKey(KeyCode.UpArrow) ? 1 : 0)) * Time.deltaTime, 1.7f, 3.4f);
                var direction = Target.position - ConversationTarget.position;
                direction.y = 0;
                if (direction.sqrMagnitude < .01f) direction = ConversationTarget.forward;
                direction = Quaternion.Euler(0, 15 + conversationOrbit, 0) * direction.normalized;
                transform.position = ConversationTarget.position + direction * conversationDistance + Vector3.up * 1.55f;
                transform.rotation = Quaternion.LookRotation(ConversationTarget.position + Vector3.up * 1.15f - transform.position);
                return;
            }
            if (!Locked) yaw += ((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0)) * Time.deltaTime * 75;
            var rotation = Quaternion.Euler(37, yaw, 0);
            var target = Target.position + Vector3.up * .9f;
            transform.position = target + rotation * new Vector3(0, 0, -12.5f);
            transform.rotation = rotation;
        }
    }
}
