using UnityEngine;

namespace ZFromMoon.EditorTools
{
    /// <summary>
    /// 极简视差：根据相机相对初始位置的水平偏移，按系数移动背景层。
    /// 挂载在每个背景 Sprite 上；factor 越小越远（移动越少），foreground 可用 >1。
    /// Demo 用，后续可替换为更完整的视差系统。
    /// </summary>
    [DisallowMultipleComponent]
    public class ParallaxPlaceholder : MonoBehaviour
    {
        [Tooltip("视差系数：0=固定不动(最远)，1=与世界同步(地面)，>1=前景反向多移")]
        public float factor = 1f;

        private Transform cam;
        private Vector3 startCamPos;
        private Vector3 startPos;

        private void Start()
        {
            if (Camera.main != null)
            {
                cam = Camera.main.transform;
                startCamPos = cam.position;
            }
            startPos = transform.position;
        }

        private void LateUpdate()
        {
            if (cam == null)
            {
                if (Camera.main != null)
                {
                    cam = Camera.main.transform;
                    startCamPos = cam.position;
                    startPos = transform.position;
                }
                return;
            }

            float delta = cam.position.x - startCamPos.x;
            Vector3 p = startPos;
            p.x = startPos.x - delta * factor;
            transform.position = p;
        }
    }
}
