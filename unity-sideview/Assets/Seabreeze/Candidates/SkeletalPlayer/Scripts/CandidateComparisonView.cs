using TMPro;
using UnityEngine;

namespace Seabreeze.Candidates
{
    [DisallowMultipleComponent]
    public sealed class CandidateComparisonView : MonoBehaviour
    {
        public CandidatePreviewMotor motor;
        public CharacterVisual legacyVisual;
        public Camera previewCamera;
        public TMP_Text readout;
        public Vector3 cameraOffset = new Vector3(2.8f, 1.6f, -10);
        public bool showLegacy = true;

        const float LegacySpacing = 5.6f;
        bool previousRunInBackground;

        void Awake()
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            if (legacyVisual == null) return;
            // Without a PlayerController parent, CharacterVisual.Update supplies idle
            // every frame. This view owns its animation clock instead.
            legacyVisual.enabled = false;
            legacyVisual.Build(0);
            legacyVisual.transform.localScale = Vector3.one * 1.17f;
            legacyVisual.gameObject.SetActive(showLegacy);
        }

        void OnDestroy() => Application.runInBackground = previousRunInBackground;

        void LateUpdate()
        {
            if (motor == null) return;
            Vector3 playerPosition = motor.transform.position;

            if (legacyVisual != null)
            {
                if (legacyVisual.gameObject.activeSelf != showLegacy)
                {
                    legacyVisual.gameObject.SetActive(showLegacy);
                    legacyVisual.ResetLocomotion();
                }
                legacyVisual.transform.position = playerPosition + Vector3.right * LegacySpacing;
                if (showLegacy && Time.deltaTime > 0)
                {
                    // ActualSpeed is this frame's measured displacement / deltaTime.
                    // Reuse that sample so a motor teleport reset also resets the distance.
                    float distance = Mathf.Abs(motor.ActualSpeed) * Time.deltaTime;
                    legacyVisual.AnimateLocomotion(motor.ActualSpeed, motor.Grounded, 0,
                        distance, Time.deltaTime);
                }
            }

            if (previewCamera != null)
                previewCamera.transform.position = new Vector3(playerPosition.x + cameraOffset.x,
                    cameraOffset.y, cameraOffset.z);

            if (readout != null)
                readout.text = $"{(motor.autoWalk ? "自动" : "手动")}  ·  实际速度 {motor.ActualSpeed:0.00;-0.00;0.00}\nA/D 移动  ·  松手 Idle  ·  空格 自动/手动";
        }
    }
}
