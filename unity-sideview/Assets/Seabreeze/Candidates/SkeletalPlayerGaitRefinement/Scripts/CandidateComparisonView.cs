using TMPro;
using UnityEngine;

namespace Seabreeze.Candidates.GaitRefinement
{
    [DisallowMultipleComponent]
    public sealed class CandidateComparisonView : MonoBehaviour
    {
        public CandidatePreviewMotor motor;
        public Camera previewCamera;
        public TMP_Text readout;
        public Vector3 cameraOffset = new Vector3(2.8f, 1.8f, -10);
        bool previousRunInBackground;

        void Awake()
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        void OnDestroy() => Application.runInBackground = previousRunInBackground;

        void LateUpdate()
        {
            if (motor == null) return;
            if (previewCamera != null)
                previewCamera.transform.position = new Vector3(motor.transform.position.x + cameraOffset.x,
                    cameraOffset.y, cameraOffset.z);
            if (readout == null) return;
            float newStride = motor.driver ? motor.driver.strideLength : 0;
            float oldStride = motor.baselineDriver ? motor.baselineDriver.strideLength : 0;
            float speed = Mathf.Abs(motor.ActualSpeed);
            float newCadence = newStride > 0 ? 120 * speed / newStride : 0;
            float oldCadence = oldStride > 0 ? 120 * speed / oldStride : 0;
            readout.text = $"{(motor.autoWalk ? "自动" : "手动")} · 相同实际速度 {speed:0.00} · 新版 {newCadence:0} / 旧版 {oldCadence:0} 步每分钟\n" +
                "1 舒缓 1.65  ·  2 快步 2.2  ·  3 原速 4.2  |  A/D 移动 · 松手停步 · 空格 自动/手动";
        }
    }
}
