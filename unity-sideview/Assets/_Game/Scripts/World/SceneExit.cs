using UnityEngine;
using ZFromMoon.Core;
using ZFromMoon.Interaction;

namespace ZFromMoon.World
{
    /// <summary>Scene-owned destination data selected through the normal E interaction flow.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SceneExit : Interactable
    {
        [SerializeField] private string targetScenePath;
        [SerializeField] private string targetEntryId;

        public string TargetScenePath => targetScenePath;
        public string TargetEntryId => targetEntryId;

        public void Configure(string scenePath, string entryId)
        {
            targetScenePath = scenePath;
            targetEntryId = entryId;
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box != null) box.isTrigger = true;
        }

        public override bool CanInteract(GameState state)
        {
            return base.CanInteract(state)
                && !string.IsNullOrWhiteSpace(targetScenePath)
                && !string.IsNullOrWhiteSpace(targetEntryId);
        }
    }
}
