using UnityEngine;
using ZFromMoon.Core;

namespace ZFromMoon.Interaction
{
    /// <summary>Scene-owned interaction data. No reference to the persistent App or player.</summary>
    [DisallowMultipleComponent]
    public class Interactable : MonoBehaviour
    {
        [SerializeField] private string interactionId;
        [SerializeField] private string displayName;
        [SerializeField] private string actionLabel = "交谈";
        [SerializeField] private Transform interactionPoint;
        [SerializeField] private Transform promptAnchor;
        [SerializeField] private int priority;
        [SerializeField] private bool interactionEnabled = true;
        [SerializeField] private string requiredFlag;
        [SerializeField, TextArea] private string responseText;

        public string InteractionId => interactionId;
        public string DisplayName => displayName;
        public string ActionLabel => actionLabel;
        public string ResponseText => responseText;
        public Transform InteractionPoint => interactionPoint != null ? interactionPoint : transform;
        public Transform PromptAnchor => promptAnchor != null ? promptAnchor : transform;
        public int Priority => priority;

        /// <summary>Availability may read state, but interacting does not mutate it here.</summary>
        public virtual bool CanInteract(GameState state)
        {
            return isActiveAndEnabled && interactionEnabled
                && !string.IsNullOrWhiteSpace(interactionId)
                && (string.IsNullOrWhiteSpace(requiredFlag) || (state != null && state.HasFlag(requiredFlag)));
        }
    }
}
