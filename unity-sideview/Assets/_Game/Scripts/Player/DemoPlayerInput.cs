using UnityEngine;

namespace ZFromMoon.Player
{
    /// <summary>
    /// Demo 用极简输入驱动：直接读键盘 A/D 或 左右方向键，喂给 PlayerMotor。
    /// 不依赖 GameSession/App，用于独立场景 DockParallax 快速验证移动与视差。
    /// 正式流程仍走 PlayerController + GameSession，本脚本不要挂到正式 Player 上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class DemoPlayerInput : MonoBehaviour
    {
        private PlayerMotor motor;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            motor.SetSimulationEnabled(true);
        }

        private void Update()
        {
            float h = 0f;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
            }
            motor.SetSimulationEnabled(true);
            motor.SetMovementEnabled(true);
            motor.SetHorizontal(h);
        }
    }
}
