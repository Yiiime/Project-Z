using System.Collections;
using UnityEngine;

namespace ZFromMoon.World
{
    /// <summary>Follows the player horizontally, clamped to the location bounds, with a dialogue push-in.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float followDamping = 0.18f;
        [SerializeField] private float fixedY;
        [SerializeField] private float fixedZ = -10f;
        [SerializeField] private float defaultOrthoSize = 5.4f;
        [SerializeField] private Vector2 fallbackBoundsX = new Vector2(-9f, 9f);

        private Camera view;
        private SceneContext location;
        private bool snapped;
        private Vector3 velocity;

        private float focusX;
        private float focusOrthoSize;
        private float focusWeight;
        private float focusTarget;
        private float focusSpeed = 2f;

        public float CurrentOrthoSize => view != null ? view.orthographicSize : defaultOrthoSize;

        private void Awake()
        {
            view = GetComponent<Camera>();
            if (view != null && Mathf.Approximately(defaultOrthoSize, 0f))
                defaultOrthoSize = view.orthographicSize;
        }

        public void SnapNow()
        {
            snapped = false;
        }

        public void FocusOn(float worldX, float orthoSize, float duration)
        {
            focusX = worldX;
            focusOrthoSize = orthoSize;
            focusTarget = 1f;
            focusSpeed = duration > 0.01f ? 1f / duration : 100f;
        }

        public void ReleaseFocus(float duration)
        {
            focusTarget = 0f;
            focusSpeed = duration > 0.01f ? 1f / duration : 100f;
        }

        private void LateUpdate()
        {
            if (view == null) return;

            if (target == null)
            {
                var motor = FindFirstObjectByType<Player.PlayerMotor>();
                if (motor != null) target = motor.transform;
                if (target == null) return;
            }

            RefreshLocation();

            Vector2 bounds = location != null ? location.CameraBoundsX : fallbackBoundsX;
            float followX = Mathf.Clamp(target.position.x, bounds.x, bounds.y);

            focusWeight = Mathf.MoveTowards(focusWeight, focusTarget, focusSpeed * Time.deltaTime);
            float focusEase = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(focusWeight));
            float desiredX = Mathf.Lerp(followX, Mathf.Clamp(focusX, bounds.x, bounds.y), focusEase);

            Vector3 position = transform.position;
            if (!snapped)
            {
                position = new Vector3(desiredX, fixedY, fixedZ);
                snapped = true;
                velocity = Vector3.zero;
            }
            else
            {
                float x = Mathf.SmoothDamp(position.x, desiredX, ref velocity.x, followDamping);
                position = new Vector3(x, fixedY, fixedZ);
            }

            transform.position = position;

            float size = Mathf.Lerp(defaultOrthoSize, focusOrthoSize, focusEase);
            if (size > 0.1f) view.orthographicSize = size;
        }

        private void RefreshLocation()
        {
            if (location != null && location.gameObject.scene.isLoaded) return;
            var found = FindFirstObjectByType<SceneContext>();
            location = found;
        }
    }
}
