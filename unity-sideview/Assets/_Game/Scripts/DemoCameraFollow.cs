using UnityEngine;

namespace ZFromMoon.EditorTools
{
    /// <summary>Demo 用：相机水平跟随目标，垂直与纵深保持不变。</summary>
    [DisallowMultipleComponent]
    public sealed class DemoCameraFollow : MonoBehaviour
    {
        public Transform target;
        public float fixedY;
        public float fixedZ = -10f;
        public float damp;

        public void Configure(Transform t, float y, float z, float d)
        {
            target = t; fixedY = y; fixedZ = z; damp = d;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 p = transform.position;
            float tx = target.position.x;
            if (damp > 0f)
                p.x = Mathf.Lerp(p.x, tx, 1f - Mathf.Exp(-damp * Time.deltaTime * 10f));
            else
                p.x = tx;
            p.y = fixedY;
            p.z = fixedZ;
            transform.position = p;
        }
    }
}
