using UnityEngine;

namespace ZFromMoon.World
{
    /// <summary>Slow lantern-style breathing for an additive glow sprite.</summary>
    [DisallowMultipleComponent]
    public sealed class GlowPulse : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer target;
        [SerializeField, Min(0f)] private float speed = 0.9f;
        [SerializeField, Range(0f, 1f)] private float minimum = 0.16f;
        [SerializeField, Range(0f, 1f)] private float maximum = 0.32f;
        [SerializeField] private float phase;

        private void Reset()
        {
            target = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            if (target == null) return;
            float wave = (Mathf.Sin((Time.time + phase) * speed) + 1f) * 0.5f;
            Color color = target.color;
            color.a = Mathf.Lerp(minimum, maximum, wave);
            target.color = color;
        }
    }
}
