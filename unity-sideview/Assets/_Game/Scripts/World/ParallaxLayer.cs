using UnityEngine;

namespace ZFromMoon.World
{
    /// <summary>
    /// Horizontal parallax for one background plate.
    /// depth 0 = locked to the camera (infinitely far), 1 = world-locked, above 1 = foreground.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParallaxLayer : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;
        [SerializeField, Range(0f, 2f)] private float depth = 1f;
        [SerializeField] private float referenceCameraX;

        private Vector3 basePosition;
        private bool ready;

        public void Configure(Transform cameraTransform, float depth, float referenceX)
        {
            this.cameraTransform = cameraTransform;
            this.depth = depth;
            referenceCameraX = referenceX;
            Capture();
        }

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
            Capture();
        }

        private void Capture()
        {
            basePosition = transform.position;
            ready = true;
        }

        private void LateUpdate()
        {
            if (!ready || cameraTransform == null) return;
            float delta = cameraTransform.position.x - referenceCameraX;
            Vector3 position = basePosition;
            position.x = basePosition.x + delta * (1f - depth);
            transform.position = position;
        }
    }
}
