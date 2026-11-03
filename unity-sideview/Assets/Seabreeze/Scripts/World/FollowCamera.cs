using UnityEngine;

namespace Seabreeze
{
    public class FollowCamera : MonoBehaviour
    {
        public Transform target;
        public float minX = -16, maxX = 16, baseY = 3.75f;
        float velocity;
        Camera cam;
        void Awake()
        {
            cam = GetComponent<Camera>();
        }

        public void Snap()
        {
            if (target == null)
                return;
            float x = Constrain(target.position.x);
            transform.position = new Vector3(x, baseY, -10);
            velocity = 0;
        }

        float Constrain(float x)
        {
            float half = cam.orthographicSize * cam.aspect;
            return maxX - minX <= half * 2 ? (minX + maxX) / 2 : Mathf.Clamp(x, minX + half, maxX - half);
        }

        void LateUpdate()
        {
            if (target == null)
                return;
            float look = (GameSession.Instance != null && !GameSession.Instance.InputBlocked && !WorldDirector.Instance.IsTransitioning) ? GameInput.Horizontal * .7f : 0;
            transform.position = new Vector3(Mathf.SmoothDamp(transform.position.x, Constrain(target.position.x + look), ref velocity, .25f), baseY, -10);
        }
    }
}
