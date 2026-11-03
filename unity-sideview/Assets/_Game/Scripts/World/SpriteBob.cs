using UnityEngine;

namespace ZFromMoon.World
{
    /// <summary>A very small vertical breathing motion so a static pose does not look frozen.</summary>
    [DisallowMultipleComponent]
    public sealed class SpriteBob : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float amplitude = 0.02f;
        [SerializeField, Min(0f)] private float speed = 1.4f;
        [SerializeField] private float phase;

        private Vector3 basePosition;
        private bool ready;

        public void Configure(float amplitude, float speed, float phase)
        {
            this.amplitude = amplitude;
            this.speed = speed;
            this.phase = phase;
        }

        private void OnEnable()
        {
            basePosition = transform.localPosition;
            ready = true;
        }

        private void Update()
        {
            if (!ready) return;
            Vector3 position = basePosition;
            position.y += Mathf.Sin((Time.time + phase) * speed) * amplitude;
            transform.localPosition = position;
        }
    }
}
