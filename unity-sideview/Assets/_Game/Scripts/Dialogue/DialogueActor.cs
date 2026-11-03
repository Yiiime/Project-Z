using UnityEngine;
using ZFromMoon.Interaction;

namespace ZFromMoon.Dialogue
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Interactable))]
    public sealed class DialogueActor : MonoBehaviour
    {
        [SerializeField] private DialogueDefinition definition;

        public DialogueDefinition Definition => definition;
    }
}
