using UnityEngine;

namespace ZFromMoon.Player
{
    /// <summary>Reads movement state and updates only the character's visual child.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerView : MonoBehaviour
    {
        private const float MovingThreshold = 0.02f;
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");

        [SerializeField] private PlayerMotor motor;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;

        private Animator cachedAnimator;
        private RuntimeAnimatorController cachedController;
        private bool hasSpeedParameter;
        private bool hasIsMovingParameter;

        private void Awake()
        {
            if (motor == null || visualRoot == null)
            {
                Debug.LogError("PlayerView requires an explicitly assigned motor and visual root.", this);
                enabled = false;
                return;
            }

            if (visualRoot == motor.transform || !visualRoot.IsChildOf(motor.transform))
            {
                Debug.LogError("PlayerView visual root must be a child of the motor, not the physics root.", this);
                enabled = false;
                return;
            }

            RefreshAnimatorBindings();
        }

        private void LateUpdate()
        {
            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * (motor.FacingSign < 0 ? -1f : 1f);
            visualRoot.localScale = scale;

            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            if (animator != cachedAnimator || controller != cachedController)
            {
                RefreshAnimatorBindings();
            }

            if (animator == null || cachedController == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            float speed = motor.HorizontalSpeed;
            if (hasSpeedParameter)
            {
                animator.SetFloat(SpeedParameter, speed);
            }

            if (hasIsMovingParameter)
            {
                animator.SetBool(IsMovingParameter, speed > MovingThreshold);
            }
        }

        private void RefreshAnimatorBindings()
        {
            cachedAnimator = animator;
            cachedController = animator != null ? animator.runtimeAnimatorController : null;
            hasSpeedParameter = false;
            hasIsMovingParameter = false;

            // A static placeholder needs no Animator or controller.
            if (cachedController == null)
            {
                return;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == SpeedParameter && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeedParameter = true;
                }

                if (parameter.nameHash == IsMovingParameter && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsMovingParameter = true;
                }
            }

            if (!hasSpeedParameter || !hasIsMovingParameter)
            {
                string unsupported = !hasSpeedParameter && !hasIsMovingParameter
                    ? "Speed (float), IsMoving (bool)"
                    : !hasSpeedParameter ? "Speed (float)" : "IsMoving (bool)";
                Debug.LogWarning(
                    $"PlayerView controller '{cachedController.name}' does not provide {unsupported}. "
                    + "Those parameters will be skipped until the controller is replaced.",
                    this);
            }
        }
    }
}
